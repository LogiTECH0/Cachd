using System.Text.Json;
using Cachd.Browser.Settings;

namespace Cachd.Browser.Services;

public sealed class CachdSystemBridge
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    public string GetRecommendedMode()
        => SystemMemoryInfo.GetRecommendedMode();

    public double GetTotalRamGb()
        => Math.Round(
            SystemMemoryInfo.GetTotalRamBytes() / 1024d / 1024d / 1024d,
            1
        );

    public string GetSettingsJson()
    {
        return JsonSerializer.Serialize(
            App.CurrentSettings,
            JsonOptions
        );
    }

    public string SaveSettingsJson(string json)
    {
        var settings = JsonSerializer.Deserialize<CachdSettings>(
            json,
            JsonOptions
        );

        if (settings is null)
            throw new InvalidOperationException("Invalid settings.");

        App.SaveSettings(settings);

        return GetSettingsJson();
    }
}