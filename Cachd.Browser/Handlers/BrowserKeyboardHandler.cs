using CefSharp;

namespace Cachd.Browser.Handlers;

public sealed class BrowserKeyboardHandler : IKeyboardHandler
{
    public bool OnPreKeyEvent(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        KeyType type,
        int windowsKeyCode,
        int nativeKeyCode,
        CefEventFlags modifiers,
        bool isSystemKey,
        ref bool isKeyboardShortcut)
    {
        if (type != KeyType.RawKeyDown)
            return false;

        if (!modifiers.HasFlag(CefEventFlags.AltDown))
            return false;

        switch (windowsKeyCode)
        {
            case 0x25: 
                if (browser.CanGoBack)
                    browser.GoBack();

                isKeyboardShortcut = true;
                return true;

            case 0x27: 
                if (browser.CanGoForward)
                    browser.GoForward();

                isKeyboardShortcut = true;
                return true;
        }

        return false;
    }

    public bool OnKeyEvent(
        IWebBrowser chromiumWebBrowser,
        IBrowser browser,
        KeyType type,
        int windowsKeyCode,
        int nativeKeyCode,
        CefEventFlags modifiers,
        bool isSystemKey)
    {
        return false;
    }
}