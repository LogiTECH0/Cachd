
namespace Cachd.Preloading;

using Cachd.Core;

public sealed class PreloadingManager
{
    private const double MinimumScore = 2;

    private readonly HashSet<string> _scheduledUrls = new(
        StringComparer.OrdinalIgnoreCase);

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

            // Сторінка вже є в кеші або очікує завантаження.
            if (cachedPage is not null &&
                cachedPage.Status != CacheStatus.Expired)
            {
                continue;
            }

            // Повторний відбір раніше запланованої сторінки.
            if (cachedPage?.Status == CacheStatus.Expired)
                _scheduledUrls.Remove(normalizedUrl);

            if (_scheduledUrls.Add(normalizedUrl))
                return normalizedUrl;
        }

        return null;
    }
}
