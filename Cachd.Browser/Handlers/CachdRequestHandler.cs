using CefSharp;
using CefSharp.Wpf;

namespace Cachd.Browser.Handlers;

public sealed class CachdRequestHandler
{
    private readonly Action<ChromiumWebBrowser, string>? _onLoadFailed;

    public CachdRequestHandler(Action<ChromiumWebBrowser, string>? onLoadFailed = null)
    {
        _onLoadFailed = onLoadFailed;
    }

    public void Attach(ChromiumWebBrowser browser)
    {
        browser.LoadError += (_, e) => OnLoadError(browser, e);
    }

    private void OnLoadError(ChromiumWebBrowser browser, LoadErrorEventArgs e)
    {
        if (e.Frame.IsMain && e.ErrorCode != CefErrorCode.Aborted)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] Load failed: {e.FailedUrl} | Error: {e.ErrorCode}");

            _onLoadFailed?.Invoke(browser, e.FailedUrl);
        }
    }
}
