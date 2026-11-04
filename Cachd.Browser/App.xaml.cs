using System.Windows;
using CefSharp;
using CefSharp.Wpf;

namespace Cachd.Browser;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        var settings = new CefSettings();

        Cef.Initialize(settings);

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cef.Shutdown();

        base.OnExit(e);
    }
}