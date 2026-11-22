
using System.Collections.Generic;
using CefSharp;
using CefSharp.OffScreen;
using System.Linq;

namespace Cachd.Browser.Services;

public sealed class ChromiumPreloadExecutor : IDisposable
{
    private sealed class PreloadWorker
    {
        public ChromiumWebBrowser? Browser { get; set; }

        public bool IsReady { get; set; }

        public string? ActiveUrl { get; set; }

        public EventHandler<LoadingStateChangedEventArgs>? LoadingHandler { get; set; }
    }
    private readonly object _sync = new();
    private readonly List<PreloadWorker> _workers = [];
    private readonly Queue<string> _pendingUrls = new();
    private readonly HashSet<string> _queuedUrls = new(StringComparer.Ordinal);
    private readonly HashSet<string> _preloadedUrls = new(StringComparer.Ordinal);
    private readonly Queue<string> _preloadedOrder = new();
    private readonly IRequestContext _requestContext;
    private const int MaxPendingUrls = 5;
    private readonly int _maxPreloadedUrls;
    private bool _disposed;
    public event Action<string>? PreloadCompleted;
    public event Action<string>? PreloadFinished;

    public ChromiumPreloadExecutor(
        int maxPreloadedUrls = 100,
        int maxConcurrentPreloads = 1)
    {
        if (maxPreloadedUrls < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPreloadedUrls));

        if (maxConcurrentPreloads < 1 || maxConcurrentPreloads > 4)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentPreloads));

        _maxPreloadedUrls = maxPreloadedUrls;
        _requestContext = Cef.GetGlobalRequestContext();

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Cache] Shared context: {_requestContext.IsGlobal}");

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Cache] Cache path: {_requestContext.CachePath}");

        for (var i = 0; i < maxConcurrentPreloads; i++)
        {
            CreateWorker(i);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Preloading] Initialized with {maxConcurrentPreloads} workers.");
    }

    private void CreateWorker(int workerId)
    {
        var worker = new PreloadWorker();

        var settings = new BrowserSettings
        {
            Javascript = CefState.Disabled,
            WebGl = CefState.Disabled,
            WindowlessFrameRate = 1
        };

        var browser = new ChromiumWebBrowser(
            address: "about:blank",
            browserSettings: settings,
            requestContext: _requestContext,
            automaticallyCreateBrowser: true,
            onAfterBrowserCreated: browser =>
                OnBrowserCreated(worker, workerId, browser),
            useLegacyRenderHandler: false
        );

        worker.Browser = browser;

        worker.LoadingHandler = (_, args) =>
            OnLoadingStateChanged(worker, workerId, args);

        browser.LoadingStateChanged += worker.LoadingHandler;

        lock (_sync)
        {
            if (_disposed)
            {
                browser.LoadingStateChanged -= worker.LoadingHandler;
                browser.Dispose();
                return;
            }

            _workers.Add(worker);
        }
    }

    private void OnBrowserCreated(
        PreloadWorker worker,
        int workerId,
        IBrowser browser)
    {
        var context = browser.GetHost().RequestContext;

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Preloading] Worker {workerId} created.");

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Cache] Worker {workerId} shared context: {context.IsGlobal}");

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Cache] Worker {workerId} cache path: {context.CachePath}");

        lock (_sync)
        {
            if (_disposed)
                return;

            worker.IsReady = true;
        }

        StartNextPreload();
    }

    public void Preload(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        if (uri.Scheme is not ("http" or "https"))
            return;

        var normalizedUrl = uri.AbsoluteUri;

        lock (_sync)
        {
            if (_disposed)
                return;

            if (_preloadedUrls.Contains(normalizedUrl))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Preloading] Already cached: {normalizedUrl}");

                return;
            }

            if (_queuedUrls.Contains(normalizedUrl))
                return;

            if (_workers.Any(w => w.ActiveUrl == normalizedUrl))
                return;

            if (_pendingUrls.Count >= MaxPendingUrls)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Preloading] Queue full: {normalizedUrl}");

                return;
            }

            _queuedUrls.Add(normalizedUrl);
            _pendingUrls.Enqueue(normalizedUrl);

            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Preloading] Queued: {normalizedUrl}");
        }

        StartNextPreload();
    }

    private void StartNextPreload()
    {
        List<(PreloadWorker Worker, string Url)> tasks = [];

        lock (_sync)
        {
            if (_disposed || _pendingUrls.Count == 0)
                return;

            foreach (var worker in _workers)
            {
                if (_pendingUrls.Count == 0)
                    break;

                if (!worker.IsReady ||
                    worker.ActiveUrl is not null ||
                    worker.Browser is null ||
                    worker.Browser.IsLoading)
                {
                    continue;
                }

                var url = _pendingUrls.Dequeue();

                _queuedUrls.Remove(url);
                worker.ActiveUrl = url;

                tasks.Add((worker, url));
            }
        }
        foreach (var task in tasks)
        {
            StartPreload(task.Worker, task.Url);
        }
    }

    private void StartPreload(PreloadWorker worker, string url)
    {
        try
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Preloading] Started: {url}");

            worker.Browser!.Load(url);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Preloading] Start error: {url} | {ex.Message}");

            CompletePreload(worker, url, false);
        }
    }

    private void OnLoadingStateChanged(
        PreloadWorker worker,
        int workerId,
        LoadingStateChangedEventArgs e)
    {
        if (e.IsLoading)
            return;

        string? loadedUrl = e.Browser.MainFrame?.Url;

        string? activeUrl;

        lock (_sync)
        {
            if (_disposed)
                return;

            activeUrl = worker.ActiveUrl;

            if (activeUrl is null)
                return;
        }

        var success =
            Uri.TryCreate(loadedUrl, UriKind.Absolute, out var uri) &&
            uri.Scheme is "http" or "https";

        if (success)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Preloading] Worker {workerId} completed: {activeUrl} -> {loadedUrl}");
        }
        else
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Preloading] Worker {workerId} failed: {activeUrl}");
        }

        CompletePreload(worker, activeUrl, success);
    }

    private void CompletePreload(
        PreloadWorker worker,
        string url,
        bool success)
    {
        string? completedUrl = null;

        lock (_sync)
        {
            if (_disposed || worker.ActiveUrl != url)
                return;

            worker.ActiveUrl = null;

            if (success && _preloadedUrls.Add(url))
            {
                _preloadedOrder.Enqueue(url);

                while (_preloadedOrder.Count > _maxPreloadedUrls)
                {
                    var oldest = _preloadedOrder.Dequeue();
                    _preloadedUrls.Remove(oldest);
                }

                completedUrl = url;
            }
        }

        if (completedUrl is not null)
            RaiseEvent(PreloadCompleted, completedUrl);

        RaiseEvent(PreloadFinished, url);

        StartNextPreload();
    }

    private static void RaiseEvent(Action<string>? handler, string url)
    {
        if (handler is null)
            return;

        foreach (Action<string> callback in handler.GetInvocationList())
        {
            try
            {
                callback(url);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Preloading] Event error: {ex.Message}");
            }
        }
    }
    public void ClearPending()
    {
        List<string> removedUrls;

        lock (_sync)
        {
            if (_disposed)
                return;

            removedUrls = _pendingUrls.ToList();

            _pendingUrls.Clear();
            _queuedUrls.Clear();
        }

        foreach (var url in removedUrls)
        {
            RaiseEvent(PreloadFinished, url);
        }

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Preloading] Cleared {removedUrls.Count} pending tasks.");
    }
    public void Dispose()
    {
        List<PreloadWorker> workersToDispose;

        lock (_sync)
        {
            if (_disposed)
                return;

            _disposed = true;

            _pendingUrls.Clear();
            _queuedUrls.Clear();

            _preloadedUrls.Clear();
            _preloadedOrder.Clear();

            foreach (var worker in _workers)
            {
                worker.ActiveUrl = null;
                worker.IsReady = false;
            }

            workersToDispose = [.. _workers];
            _workers.Clear();
        }

        foreach (var worker in workersToDispose)
        {
            if (worker.Browser is null)
                continue;

            if (worker.LoadingHandler is not null)
            {
                worker.Browser.LoadingStateChanged -= worker.LoadingHandler;
            }

            worker.Browser.Dispose();
        }

        System.Diagnostics.Debug.WriteLine(
            "[Cachd Preloading] Executor disposed.");
    }
}
