
namespace Cachd.Preloading;

using Cachd.Core;

public sealed class PreloadingManager
{
    private const double MinimumScore = 2;

    private readonly object _sync = new();

    private readonly HashSet<string> _scheduledUrls =
        new(StringComparer.OrdinalIgnoreCase);

    private ResourceProfile _profile;
    private bool _enabled = true;

    public ResourceProfile Profile
    {
        get
        {
            lock (_sync)
                return _profile;
        }
    }

    public bool IsEnabled
    {
        get
        {
            lock (_sync)
                return _enabled;
        }
    }

    public PreloadingManager(ResourceProfile? profile = null)
    {
        _profile = profile ?? ResourceProfile.Balanced;
    }

    public void SetProfile(ResourceProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        lock (_sync)
        {
            _profile = profile;
        }
    }

    public void SetEnabled(bool enabled)
    {
        lock (_sync)
        {
            _enabled = enabled;

            if (!enabled)
                _scheduledUrls.Clear();
        }
    }

    public void CompletePreload(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        if (uri.Scheme is not ("http" or "https"))
            return;

        lock (_sync)
        {
            _scheduledUrls.Remove(uri.AbsoluteUri);
        }
    }

    public string? SelectCandidate(
        string currentUrl,
        IReadOnlyList<PagePrediction> predictions,
        CacheManager cacheManager)
    {
        if (!Uri.TryCreate(currentUrl, UriKind.Absolute, out var currentUri))
            return null;

        lock (_sync)
        {
            if (!_enabled)
                return null;

            foreach (var scheduledUrl in _scheduledUrls.ToArray())
            {
                var page = cacheManager.GetPage(scheduledUrl);

                if (page?.Status == CacheStatus.Expired)
                    _scheduledUrls.Remove(scheduledUrl);
            }

            var activeCount = cacheManager.Pages.Count(
                page => page.Status == CacheStatus.Active);

            if (activeCount + _scheduledUrls.Count >= _profile.MaxPreloadedPages)
                return null;

            foreach (var prediction in predictions.OrderByDescending(p => p.Score))
            {
                if (prediction.Score < MinimumScore)
                    continue;

                if (!Uri.TryCreate(
                    prediction.Url,
                    UriKind.Absolute,
                    out var uri))
                    continue;

                if (uri.Scheme is not ("http" or "https"))
                    continue;

                var normalizedUrl = uri.AbsoluteUri;

                if (string.Equals(
                    normalizedUrl,
                    currentUri.AbsoluteUri,
                    StringComparison.OrdinalIgnoreCase))
                    continue;

                if (_scheduledUrls.Contains(normalizedUrl))
                    continue;

                var cachedPage = cacheManager.GetPage(normalizedUrl);

                if (cachedPage is not null)
                    continue;

                if (_scheduledUrls.Add(normalizedUrl))
                    return normalizedUrl;
            }

            return null;
        }
    }
}
