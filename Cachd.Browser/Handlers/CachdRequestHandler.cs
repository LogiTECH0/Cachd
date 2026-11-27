using CefSharp;
using CefSharp.Wpf;

namespace Cachd.Browser.Handlers;

public sealed class CachdRequestHandler : CefSharp.Handler.RequestHandler
{
    private readonly Action<ChromiumWebBrowser, string>? _onLoadFailed;
    private readonly Action<string>? _openLinkInNewTab;

    public CachdRequestHandler(
        Action<ChromiumWebBrowser, string>? onLoadFailed = null,
        Action<string>? openLinkInNewTab = null)
    {
        _onLoadFailed = onLoadFailed;
        _openLinkInNewTab = openLinkInNewTab;
    }

    public void Attach(ChromiumWebBrowser browser)
    {
        browser.RequestHandler = this;
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

    protected override bool OnBeforeBrowse(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        IRequest request,
        bool userGesture,
        bool isRedirect)
    {
        if (frame.IsMain
            && userGesture
            && !isRedirect
            && (request.TransitionType & TransitionType.SourceMask) == TransitionType.LinkClicked)
        {
            _openLinkInNewTab?.Invoke(request.Url);
            return true;
        }

        return false;
    }
}
