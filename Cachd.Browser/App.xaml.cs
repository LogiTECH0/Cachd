
using System.IO;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;

namespace Cachd.Browser;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        CefSharpSettings.ShutdownOnExit = false;

        var cachePath = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Cachd",
            "Cache"
        );

        Directory.CreateDirectory(cachePath);

        var settings = new CefSettings
        {
            CachePath = cachePath
        };

        if (!Cef.Initialize(settings))
        {
            throw new InvalidOperationException(
                "Failed to initialize Chromium."
            );
        }

        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cef.Shutdown();

        base.OnExit(e);
    }
}
