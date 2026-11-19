namespace Cachd.Preloading;

public sealed class CachedPage
{
    public string Url { get; set; } = string.Empty;

    public DateTimeOffset? FirstPreloadedAt { get; set; }

    public DateTimeOffset? LastPreloadedAt { get; set; }

    public DateTimeOffset? LastVisitedAt { get; set; }

    public int VisitCount { get; set; }

    public double PredictionScore { get; set; }

    public CacheStatus Status { get; set; } = CacheStatus.Candidate;
}