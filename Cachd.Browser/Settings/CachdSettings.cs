
namespace Cachd.Browser.Settings;

public sealed class CachdSettings
{
    public string Mode { get; set; } = "balanced";
    public bool PreloadingEnabled { get; set; } = true;
    public bool MemorySaverEnabled { get; set; } = true;
    public int CacheBudgetMb { get; set; } = 250;
    public int MaxConcurrentPreloads { get; set; } = 2;
    public bool ManualOverride { get; set; } = false;
    public string StartupPage { get; set; } = "home";
    public string Theme { get; set; } = "dark";
    public string Language { get; set; } = "uk";
}
