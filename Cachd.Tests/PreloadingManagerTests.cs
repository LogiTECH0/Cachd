
using Cachd.Core;
using Cachd.Preloading;

namespace Cachd.Tests;

public class PreloadingManagerTests
{
    private static ResourceProfile TestProfile(int maxPages = 2)
    {
        return ResourceProfile.Custom(
            maxPreloadedPages: maxPages,
            maxCachedPages: 10,
            maxConcurrentPreloads: 1,
            cacheExpiration: TimeSpan.FromHours(24));
    }

    [Fact]
    public void SelectCandidate_ShouldRespectMaxPreloadedPages()
    {
        var manager = new PreloadingManager(TestProfile(2));
        var cache = new CacheManager();

        var predictions = new[]
        {
            new PagePrediction("https://first.com", 10),
            new PagePrediction("https://second.com", 8),
            new PagePrediction("https://third.com", 6)
        };

        Assert.Equal(
            "https://first.com/",
            manager.SelectCandidate("https://current.com", predictions, cache));

        Assert.Equal(
            "https://second.com/",
            manager.SelectCandidate("https://current.com", predictions, cache));

        Assert.Null(
            manager.SelectCandidate("https://current.com", predictions, cache));
    }

    [Fact]
    public void CompletePreload_ShouldAllowAnotherCandidate()
    {
        var manager = new PreloadingManager(TestProfile(1));
        var cache = new CacheManager();

        var first = new[]
        {
            new PagePrediction("https://first.com", 10)
        };

        Assert.Equal(
            "https://first.com/",
            manager.SelectCandidate("https://current.com", first, cache));

        manager.CompletePreload("https://first.com/");

        var second = new[]
        {
            new PagePrediction("https://second.com", 8)
        };

        Assert.Equal(
            "https://second.com/",
            manager.SelectCandidate("https://current.com", second, cache));
    }

    [Fact]
    public void SelectCandidate_ShouldSkipAlreadyActivePage()
    {
        var manager = new PreloadingManager(TestProfile());
        var cache = new CacheManager();

        cache.AddCandidate("https://active.com", 10);
        cache.MarkPreloaded("https://active.com");

        var predictions = new[]
        {
            new PagePrediction("https://active.com", 10),
            new PagePrediction("https://other.com", 8)
        };

        Assert.Equal(
            "https://other.com/",
            manager.SelectCandidate("https://current.com", predictions, cache));
    }

    [Fact]
    public void SelectCandidate_ShouldSkipLowScorePredictions()
    {
        var manager = new PreloadingManager(TestProfile());
        var cache = new CacheManager();

        var predictions = new[]
        {
            new PagePrediction("https://weak.com", 1),
            new PagePrediction("https://strong.com", 5)
        };

        Assert.Equal(
            "https://strong.com/",
            manager.SelectCandidate("https://current.com", predictions, cache));
    }
}
