using System.IO;
using System.Globalization;
using System.Windows;
using CefSharp;
using CefSharp.Wpf;
using Cachd.Browser.Handlers;
using Cachd.Browser.Settings;

namespace Cachd.Browser;

public partial class App : Application
{
    public static SettingsStore SettingsStore { get; private set; } = null!;

    public static CachdSettings CurrentSettings { get; private set; } = null!;

    public static event Action<CachdSettings>? SettingsChanged;

    protected override void OnStartup(StartupEventArgs e)
    {
        CefSharpSettings.ShutdownOnExit = false;
        SettingsStore = new SettingsStore();
        CurrentSettings = SettingsStore.Load();

        string cachePath = Path.Combine(
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

        long cacheBudgetBytes =
            (long)CurrentSettings.CacheBudgetMb * 1024 * 1024;

        settings.CefCommandLineArgs.Add(
            "disk-cache-size",
            cacheBudgetBytes.ToString(CultureInfo.InvariantCulture)
        );

        settings.RegisterScheme(new CefCustomScheme
        {
            SchemeName = "cachd",
            SchemeHandlerFactory = new CachdSchemeHandlerFactory()
        });

        if (!Cef.Initialize(settings))
        {
            throw new InvalidOperationException(
                "Failed to initialize Chromium."
            );
        }

        base.OnStartup(e);
    }

    public static void SaveSettings(CachdSettings settings)
    {
        settings = SettingsStore.Normalize(settings);

        SettingsStore.Save(settings);
        CurrentSettings = settings;

        SettingsChanged?.Invoke(settings);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Cef.Shutdown();
        base.OnExit(e);
    }
}
