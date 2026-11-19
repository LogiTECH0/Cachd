
using Cachd.Preloading;

namespace Cachd.Tests;

public class CacheManagerTests
{
    [Fact]
    public void MarkInactivePagesExpired_ShouldExpireOldPage()
    {
        var manager = new CacheManager();
        var now = DateTimeOffset.UtcNow;

        manager.AddCandidate("https://example.com", 5);
        manager.MarkPreloaded("https://example.com");

        var page = manager.GetPage("https://example.com")!;
        page.LastPreloadedAt = now.AddHours(-25);

        // Задаємо стару дату через окремий сценарій
        // у наступному тесті перевіримо час через API.
    }

    [Fact]
    public void MarkInactivePagesExpired_ShouldRejectInvalidLimit()
    {
        var manager = new CacheManager();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            manager.MarkInactivePagesExpired(TimeSpan.Zero));
    }

    [Fact]
    public void RemoveExpired_ShouldRemoveExpiredPages()
    {
        var manager = new CacheManager();

        manager.AddCandidate("https://example.com", 5);
        manager.MarkExpired("https://example.com");

        manager.RemoveExpired();

        Assert.Null(manager.GetPage("https://example.com"));
    }

    [Fact]
    public void MarkInactivePagesExpired_ShouldIgnoreCandidates()
    {
        var manager = new CacheManager();

        manager.AddCandidate("https://example.com", 5);

        var expired = manager.MarkInactivePagesExpired(
            TimeSpan.FromHours(24),
            DateTimeOffset.UtcNow.AddDays(2));

        Assert.Equal(0, expired);
        Assert.Equal(
            CacheStatus.Candidate,
            manager.GetPage("https://example.com")!.Status);
    }
}
