using CefSharp;
using CefSharp.Handler;

namespace Cachd.Browser.Handlers;

public sealed class CachdLifeSpanHandler : LifeSpanHandler
{
    private readonly Action<string> _openLinkInNewTab;

    public CachdLifeSpanHandler(Action<string> openLinkInNewTab)
    {
        _openLinkInNewTab = openLinkInNewTab;
    }

    protected override bool OnBeforePopup(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        IFrame frame,
        string targetUrl,
        string targetFrameName,
        WindowOpenDisposition targetDisposition,
        bool userGesture,
        IPopupFeatures popupFeatures,
        IWindowInfo windowInfo,
        IBrowserSettings browserSettings,
        ref bool noJavascriptAccess,
        out IWebBrowser newBrowser)
    {
        newBrowser = null!;

        if (userGesture && !string.IsNullOrWhiteSpace(targetUrl))
            _openLinkInNewTab(targetUrl);

        return true;
    }
}
