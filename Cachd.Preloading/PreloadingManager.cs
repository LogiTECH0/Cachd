
namespace Cachd.Preloading;

using Cachd.Core;

public sealed class PreloadingManager
{
    private const double MinimumScore = 2;

    private readonly HashSet<string> _scheduledUrls = new(
        StringComparer.OrdinalIgnoreCase);

    public ResourceProfile Profile { get; private set; }

    public PreloadingManager(ResourceProfile? profile = null)
    {
        Profile = profile ?? ResourceProfile.Balanced;
    }

    public void SetProfile(ResourceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        Profile = profile;
    }

    public string? SelectCandidate(
        string currentUrl,
        IReadOnlyList<PagePrediction> predictions,
        CacheManager cacheManager)
    {
        if (!Uri.TryCreate(
                currentUrl,
                UriKind.Absolute,
                out var currentUri))
        {
            return null;
        }

        foreach (var scheduledUrl in _scheduledUrls.ToArray())
        {
            var page = cacheManager.GetPage(scheduledUrl);

            if (page?.Status == CacheStatus.Expired)
                _scheduledUrls.Remove(scheduledUrl);
        }

        foreach (var prediction in predictions
                     .OrderByDescending(p => p.Score))
        {
            if (prediction.Score < MinimumScore)
                continue;

            if (!Uri.TryCreate(
                    prediction.Url,
                    UriKind.Absolute,
                    out var uri))
            {
                continue;
            }

            if (uri.Scheme is not ("http" or "https"))
                continue;

            var normalizedUrl = uri.AbsoluteUri;

            if (string.Equals(
                    normalizedUrl,
                    currentUri.AbsoluteUri,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var cachedPage = cacheManager.GetPage(normalizedUrl);

            if (cachedPage is not null &&
                cachedPage.Status != CacheStatus.Expired)
            {
                continue;
            }

            if (cachedPage?.Status == CacheStatus.Expired)
                _scheduledUrls.Remove(normalizedUrl);

            if (_scheduledUrls.Count >= Profile.MaxPreloadedPages)
                return null;

            if (_scheduledUrls.Add(normalizedUrl))
                return normalizedUrl;
        }

        return null;
    }
}
