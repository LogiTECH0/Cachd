using Cachd.Preloading;

namespace Cachd.Tests;

public class CacheManagerTests
{
    [Fact]
    public void MarkInactivePagesExpired_ShouldExpireOldPage()
    {
        var manager = new CacheManager();
        var now = DateTimeOffset.UtcNow;
        const string url = "https://example.com";

        manager.AddCandidate(url, 5);
        manager.MarkPreloaded(url, now.AddHours(-25));

        var expired = manager.MarkInactivePagesExpired(TimeSpan.FromHours(24), now);

        Assert.Equal(1, expired);
        Assert.Equal(CacheStatus.Expired, manager.GetPage(url)!.Status);
    }

    [Fact]
    public void MarkInactivePagesExpired_ShouldNotExpireRecentlyVisitedPage()
    {
        var manager = new CacheManager();
        var now = DateTimeOffset.UtcNow;
        const string url = "https://example.com";

        manager.AddCandidate(url, 5);
        manager.MarkPreloaded(url, now.AddHours(-25));
        manager.MarkVisited(url, now.AddHours(-1));

        var expired = manager.MarkInactivePagesExpired(TimeSpan.FromHours(24), now);

        Assert.Equal(0, expired);
        Assert.Equal(CacheStatus.Active, manager.GetPage(url)!.Status);
    }

    [Fact]
    public void MarkInactivePagesExpired_ShouldRejectInvalidLimit()
    {
        var manager = new CacheManager();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            manager.MarkInactivePagesExpired(TimeSpan.Zero)
        );
    }

    [Fact]
    public void RemoveExpired_ShouldRemoveExpiredPages()
    {
        var manager = new CacheManager();
        const string url = "https://example.com";

        manager.AddCandidate(url, 5);
        manager.MarkExpired(url);

        manager.RemoveExpired();

        Assert.Null(manager.GetPage(url));
    }

    [Fact]
    public void MarkInactivePagesExpired_ShouldIgnoreCandidates()
    {
        var manager = new CacheManager();
        const string url = "https://example.com";

        manager.AddCandidate(url, 5);

        var expired = manager.MarkInactivePagesExpired(
            TimeSpan.FromHours(24),
            DateTimeOffset.UtcNow.AddDays(2)
        );

        Assert.Equal(0, expired);
        Assert.Equal(CacheStatus.Candidate, manager.GetPage(url)!.Status);
    }

    [Fact]
    public void AddCandidate_ShouldEvictLowestScoreWhenLimitReached()
    {
        var manager = new CacheManager(maxCachedPages: 2);

        manager.AddCandidate("https://first.com", 2);
        manager.AddCandidate("https://second.com", 8);
        manager.AddCandidate("https://third.com", 5);

        Assert.Null(manager.GetPage("https://first.com"));
        Assert.NotNull(manager.GetPage("https://second.com"));
        Assert.NotNull(manager.GetPage("https://third.com"));
        Assert.Equal(2, manager.Pages.Count);
    }
}
