using CefSharp;
using CefSharp.OffScreen;

namespace Cachd.Browser.Services;

public sealed class ChromiumPreloadExecutor : IDisposable
{
    private readonly ChromiumWebBrowser _browser;
    private readonly object _sync = new();

    private bool _browserReady;
    private bool _disposed;
    private string? _pendingUrl;

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

        _browser.LoadingStateChanged += OnLoadingStateChanged;
    }

    private void OnBrowserCreated(IBrowser browser)
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _browserReady = true;

            if (_pendingUrl is null)
                return;

            var url = _pendingUrl;
            _pendingUrl = null;

            browser.MainFrame?.LoadUrl(url);
        }
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

            if (!_browserReady || _browser.IsLoading)
            {
                _pendingUrl = url;
                return;
            }

            _browser.Load(url);
        }

        System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Loading: {url}");
    }

    private void OnLoadingStateChanged(object? sender, LoadingStateChangedEventArgs e)
    {
        if (e.IsLoading)
            return;

        var loadedUrl = e.Browser.MainFrame?.Url;

        System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Completed: {loadedUrl}");

        lock (_sync)
        {
            if (_disposed || !_browserReady || _pendingUrl is null)
                return;

            var url = _pendingUrl;
            _pendingUrl = null;

            _browser.Load(url);

            System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Loading queued: {url}");
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;
            _pendingUrl = null;
        }

        _browser.LoadingStateChanged -= OnLoadingStateChanged;
        _browser.Dispose();
    }
}
