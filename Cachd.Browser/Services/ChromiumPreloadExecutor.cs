using System.Collections.Generic;
using CefSharp;
using CefSharp.OffScreen;

namespace Cachd.Browser.Services;

public sealed class ChromiumPreloadExecutor : IDisposable
{
    private readonly ChromiumWebBrowser _browser;
    private readonly object _sync = new();

    private readonly Queue<string> _pendingUrls = new();
    private readonly HashSet<string> _queuedUrls = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preloadedUrls = new(StringComparer.Ordinal);
    private readonly Queue<string> _preloadedOrder = new();

    private const int MaxPendingUrls = 5;
    private const int MaxPreloadedUrls = 100;

    private bool _browserReady;
    private bool _disposed;
    private string? _activeUrl;

    public ChromiumPreloadExecutor()
    {
        var settings = new BrowserSettings
        {
            Javascript = CefState.Disabled,
            WebGl = CefState.Disabled,
            WindowlessFrameRate = 1,
        };

        _browser = new ChromiumWebBrowser(
            address: "about:blank",
            browserSettings: settings,
            requestContext: null,
            automaticallyCreateBrowser: true,
            onAfterBrowserCreated: OnBrowserCreated,
            useLegacyRenderHandler: false
        );
        var globalContext = Cef.GetGlobalRequestContext();

        System.Diagnostics.Debug.WriteLine($"[Cachd Cache] IsGlobal: {globalContext.IsGlobal}");

        System.Diagnostics.Debug.WriteLine($"[Cachd Cache] CachePath: {globalContext.CachePath}");

        _browser.LoadingStateChanged += OnLoadingStateChanged;
    }

    private void OnBrowserCreated(IBrowser browser)
    {
        var context = browser.GetHost().RequestContext;

        System.Diagnostics.Debug.WriteLine("[Cachd Cache] OffScreen Browser created");

        System.Diagnostics.Debug.WriteLine($"[Cachd Cache] IsGlobal: {context.IsGlobal}");

        System.Diagnostics.Debug.WriteLine($"[Cachd Cache] CachePath: {context.CachePath}");

        lock (_sync)
        {
            if (_disposed)
                return;

            _browserReady = true;
        }

        StartNextPreload();
    }

    public void Preload(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        if (uri.Scheme is not ("http" or "https"))
            return;

        lock (_sync)
        {
            if (_disposed)
                return;

            if (_preloadedUrls.Contains(url))
            {
                System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Already cached: {url}");
                return;
            }

            if (_activeUrl == url || !_queuedUrls.Add(url))
                return;

            if (_pendingUrls.Count >= MaxPendingUrls)
            {
                _queuedUrls.Remove(url);

                System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Queue full: {url}");
                return;
            }

            _pendingUrls.Enqueue(url);

            System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Queued: {url}");
        }
        System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Loading: {url}");

        _browser.Load(url);

        StartNextPreload();
    }

    private void StartNextPreload()
    {
        string? url;

        lock (_sync)
        {
            if (
                _disposed
                || !_browserReady
                || _activeUrl is not null
                || _pendingUrls.Count == 0
                || _browser.IsLoading
            )
            {
                return;
            }

            url = _pendingUrls.Dequeue();
            _queuedUrls.Remove(url);
            _activeUrl = url;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Loading: {url}");

            _browser.Load(url);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Error: {ex.Message}");

            lock (_sync)
            {
                _activeUrl = null;
            }

            StartNextPreload();
        }
    }

    private void OnLoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (e.IsLoading)
            return;

        string? activeUrl;
        string? loadedUrl = e.Browser.MainFrame?.Url;

        lock (_sync)
        {
            if (_disposed)
                return;

            activeUrl = _activeUrl;

            if (activeUrl is null)
                return;

            _activeUrl = null;

            if (
                Uri.TryCreate(loadedUrl, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https"
            )
            {
                if (_preloadedUrls.Add(activeUrl))
                {
                    _preloadedOrder.Enqueue(activeUrl);

                    while (_preloadedOrder.Count > MaxPreloadedUrls)
                    {
                        var oldest = _preloadedOrder.Dequeue();
                        _preloadedUrls.Remove(oldest);
                    }
                }

                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Preloading] Completed: {activeUrl} -> {loadedUrl}"
                );
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Failed: {activeUrl}");
            }
        }

        StartNextPreload();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;

            _pendingUrls.Clear();
            _queuedUrls.Clear();
            _preloadedUrls.Clear();
            _preloadedOrder.Clear();

            _activeUrl = null;
        }

        _browser.LoadingStateChanged -= OnLoadingStateChanged;
        _browser.Dispose();
    }
}
