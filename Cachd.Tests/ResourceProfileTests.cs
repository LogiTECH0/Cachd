
using Cachd.Preloading;

namespace Cachd.Tests;

public class ResourceProfileTests
{
    [Theory]
    [InlineData(2, ResourceMode.Low)]
    [InlineData(4, ResourceMode.Low)]
    [InlineData(6, ResourceMode.Balanced)]
    [InlineData(8, ResourceMode.Balanced)]
    [InlineData(12, ResourceMode.Performance)]
    [InlineData(16, ResourceMode.Performance)]
    [InlineData(32, ResourceMode.Performance)]
    public void DetectFromRam_ShouldSelectCorrectProfile(
        int ramGb,
        ResourceMode expectedMode)
    {
        var profile = ResourceProfile.DetectFromRam(ramGb);

        Assert.Equal(expectedMode, profile.Mode);
    }

    [Fact]
    public void LowProfile_ShouldHaveExpectedLimits()
    {
        var profile = ResourceProfile.Low;

        Assert.Equal(1, profile.MaxPreloadedPages);
        Assert.Equal(10, profile.MaxCachedPages);
        Assert.Equal(1, profile.MaxConcurrentPreloads);
    }

    [Fact]
    public void BalancedProfile_ShouldHaveExpectedLimits()
    {
        var profile = ResourceProfile.Balanced;

        Assert.Equal(3, profile.MaxPreloadedPages);
        Assert.Equal(30, profile.MaxCachedPages);
        Assert.Equal(1, profile.MaxConcurrentPreloads);
    }

    [Fact]
    public void PerformanceProfile_ShouldHaveExpectedLimits()
    {
        var profile = ResourceProfile.Performance;

        Assert.Equal(5, profile.MaxPreloadedPages);
        Assert.Equal(60, profile.MaxCachedPages);
        Assert.Equal(2, profile.MaxConcurrentPreloads);
    }

    [Fact]
    public void CustomProfile_ShouldStoreProvidedSettings()
    {
        var profile = ResourceProfile.Custom(
            4,
            40,
            2,
            TimeSpan.FromHours(36));

        Assert.Equal(ResourceMode.Custom, profile.Mode);
        Assert.Equal(4, profile.MaxPreloadedPages);
        Assert.Equal(40, profile.MaxCachedPages);
        Assert.Equal(2, profile.MaxConcurrentPreloads);
        Assert.Equal(TimeSpan.FromHours(36), profile.CacheExpiration);
    }

    [Fact]
    public void CustomProfile_ShouldRejectInvalidCacheLimit()
    {
        Assert.Throws<ArgumentException>(() =>
            ResourceProfile.Custom(
                5,
                3,
                1,
                TimeSpan.FromHours(24)));
    }

    [Fact]
    public void CustomProfile_ShouldRejectZeroPreloadedPages()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            ResourceProfile.Custom(
                0,
                10,
                1,
                TimeSpan.FromHours(24)));
    }
}
