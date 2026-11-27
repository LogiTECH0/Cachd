using System.IO;
using System.Text.Json;
using Cachd.Browser.Services;

namespace Cachd.Browser.Settings;

public sealed class SettingsStore
{
    private readonly string _filePath;

    private static readonly JsonSerializerOptions JsonOptions = new(
    JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public SettingsStore()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Cachd"
        );

        Directory.CreateDirectory(directory);

        _filePath = Path.Combine(directory, "settings.json");
    }

    public CachdSettings Load()
    {
        if (!File.Exists(_filePath))
            return CreateDefault();

        try
        {
            string json = File.ReadAllText(_filePath);

            var settings = JsonSerializer.Deserialize<CachdSettings>(
                json,
                JsonOptions
            );

            return Validate(settings ?? CreateDefault());
        }
        catch
        {
            return CreateDefault();
        }
    }

    public void Save(CachdSettings settings)
    {
        settings = Validate(settings);

        string json = JsonSerializer.Serialize(
            settings,
            JsonOptions
        );

        string temporaryPath = _filePath + ".tmp";

        File.WriteAllText(temporaryPath, json);
        File.Move(temporaryPath, _filePath, true);
    }

    private static CachdSettings CreateDefault()
    {
        string mode;

        try
        {
            mode = SystemMemoryInfo.GetRecommendedMode();
        }
        catch
        {
            mode = "balanced";
        }

        return mode switch
        {
            "low" => new CachdSettings
            {
                Mode = "low",
                CacheBudgetMb = 100,
                MaxConcurrentPreloads = 1
            },

            "performance" => new CachdSettings
            {
                Mode = "performance",
                MemorySaverEnabled = false,
                CacheBudgetMb = 1000,
                MaxConcurrentPreloads = 4
            },

            _ => new CachdSettings()
        };
    }

    private static CachdSettings Validate(CachdSettings settings)
    {
        settings.Mode = settings.Mode.ToLowerInvariant();

        if (settings.Mode is not (
            "low" or "balanced" or "performance" or "custom"))
        {
            settings.Mode = "balanced";
        }
        settings.StartupPage =
            settings.StartupPage is "home" or "blank"
                ? settings.StartupPage
                : "home";
        settings.Theme = settings.Theme is "light" or "dark"
            ? settings.Theme
            : "dark";
        settings.CacheBudgetMb = Math.Clamp(
            settings.CacheBudgetMb, 100, 1000);

        settings.MaxConcurrentPreloads = Math.Clamp(
            settings.MaxConcurrentPreloads, 1, 4);

        return settings;
    }
    public CachdSettings Normalize(CachdSettings settings)
    {
        return Validate(settings);
    }
}
