
namespace Cachd.Preloading;

public enum ResourceMode
{
    Low,
    Balanced,
    Performance,
    Custom
}

public sealed class ResourceProfile
{
    public ResourceMode Mode { get; init; }

    public int MaxPreloadedPages { get; init; }

    public int MaxCachedPages { get; init; }

    public int MaxConcurrentPreloads { get; init; }

    public TimeSpan CacheExpiration { get; init; }

    public static ResourceProfile Low => new()
    {
        Mode = ResourceMode.Low,
        MaxPreloadedPages = 1,
        MaxCachedPages = 10,
        MaxConcurrentPreloads = 1,
        CacheExpiration = TimeSpan.FromHours(12)
    };

    public static ResourceProfile Balanced => new()
    {
        Mode = ResourceMode.Balanced,
        MaxPreloadedPages = 3,
        MaxCachedPages = 30,
        MaxConcurrentPreloads = 2,
        CacheExpiration = TimeSpan.FromHours(24)
    };

    public static ResourceProfile Performance => new()
    {
        Mode = ResourceMode.Performance,
        MaxPreloadedPages = 5,
        MaxCachedPages = 60,
        MaxConcurrentPreloads = 4,
        CacheExpiration = TimeSpan.FromHours(48)
    };

    public static ResourceProfile Custom(
        int maxPreloadedPages,
        int maxCachedPages,
        int maxConcurrentPreloads,
        TimeSpan cacheExpiration)
    {
        if (maxPreloadedPages < 1)
            throw new ArgumentOutOfRangeException(nameof(maxPreloadedPages));

        if (maxCachedPages < maxPreloadedPages)
            throw new ArgumentException(
                "MaxCachedPages cannot be lower than MaxPreloadedPages.");

        if (maxConcurrentPreloads < 1)
            throw new ArgumentOutOfRangeException(nameof(maxConcurrentPreloads));

        if (cacheExpiration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(cacheExpiration));

        return new ResourceProfile
        {
            Mode = ResourceMode.Custom,
            MaxPreloadedPages = maxPreloadedPages,
            MaxCachedPages = maxCachedPages,
            MaxConcurrentPreloads = maxConcurrentPreloads,
            CacheExpiration = cacheExpiration
        };
    }

    public static ResourceProfile DetectFromRam(int totalRamGb)
    {
        if (totalRamGb < 8)
            return Low;

        if (totalRamGb < 16)
            return Balanced;

        return Performance;
    }

}
