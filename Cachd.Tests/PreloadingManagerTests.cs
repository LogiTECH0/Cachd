
using Cachd.Core;
using Cachd.Preloading;

namespace Cachd.Tests;

public class PreloadingManagerTests
{
    [Fact]
    public void SelectCandidate_ShouldRejectLowScore()
    {
        var manager = new PreloadingManager();
        var cacheManager = new CacheManager();

        var predictions = new[]
        {
            new PagePrediction("https://github.com/", 1)
        };

        var result = manager.SelectCandidate(
            "https://google.com/",
            predictions,
            cacheManager);

        Assert.Null(result);
    }

    [Fact]
    public void SelectCandidate_ShouldAcceptMinimumScore()
    {
        var manager = new PreloadingManager();
        var cacheManager = new CacheManager();

        var predictions = new[]
        {
            new PagePrediction("https://github.com/", 2)
        };

        var result = manager.SelectCandidate(
            "https://google.com/",
            predictions,
            cacheManager);

        Assert.Equal("https://github.com/", result);
    }

    [Fact]
    public void SelectCandidate_ShouldSkipCurrentUrl()
    {
        var manager = new PreloadingManager();
        var cacheManager = new CacheManager();

        var predictions = new[]
        {
            new PagePrediction("https://google.com/", 10)
        };

        var result = manager.SelectCandidate(
            "https://google.com/",
            predictions,
            cacheManager);

        Assert.Null(result);
    }

    [Fact]
    public void SelectCandidate_ShouldRejectFileUrl()
    {
        var manager = new PreloadingManager();
        var cacheManager = new CacheManager();

        var predictions = new[]
        {
            new PagePrediction("file:///C:/test.html", 10)
        };

        var result = manager.SelectCandidate(
            "https://google.com/",
            predictions,
            cacheManager);

        Assert.Null(result);
    }

    [Fact]
    public void SelectCandidate_ShouldNotScheduleSameUrlTwice()
    {
        var manager = new PreloadingManager();
        var cacheManager = new CacheManager();

        var predictions = new[]
        {
            new PagePrediction("https://github.com/", 5)
        };

        var first = manager.SelectCandidate(
            "https://google.com/",
            predictions,
            cacheManager);

        var second = manager.SelectCandidate(
            "https://google.com/",
            predictions,
            cacheManager);

        Assert.Equal("https://github.com/", first);
        Assert.Null(second);
    }

    [Fact]
    public void SelectCandidate_ShouldSkipActivePage()
    {
        var manager = new PreloadingManager();
        var cacheManager = new CacheManager();

        cacheManager.AddCandidate("https://github.com/", 5);
        cacheManager.MarkPreloaded("https://github.com/");

        var predictions = new[]
        {
            new PagePrediction("https://github.com/", 10)
        };

        var result = manager.SelectCandidate(
            "https://google.com/",
            predictions,
            cacheManager);

        Assert.Null(result);
    }

    [Fact]
    public void SelectCandidate_ShouldAllowExpiredPageAgain()
    {
        var manager = new PreloadingManager();
        var cacheManager = new CacheManager();

        cacheManager.AddCandidate("https://github.com/", 5);
        cacheManager.MarkExpired("https://github.com/");

        var predictions = new[]
        {
            new PagePrediction("https://github.com/", 10)
        };

        var result = manager.SelectCandidate(
            "https://google.com/",
            predictions,
            cacheManager);

        Assert.Equal("https://github.com/", result);
    }
}
