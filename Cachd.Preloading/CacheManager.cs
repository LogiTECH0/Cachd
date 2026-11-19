namespace Cachd.Preloading;

public sealed class CacheManager
{
    private readonly object _lock = new();

    private readonly Dictionary<string, CachedPage> _pages = new(StringComparer.Ordinal);

    public IReadOnlyCollection<CachedPage> Pages
    {
        get
        {
            lock (_lock)
            {
                return _pages.Values.Select(ClonePage).ToArray();
            }
        }
    }

    public void AddCandidate(string url, double predictionScore)
    {
        if (!TryNormalizeUrl(url, out var normalizedUrl))
            return;

        lock (_lock)
        {
            if (_pages.TryGetValue(normalizedUrl, out var existing))
            {
                existing.PredictionScore = predictionScore;

                if (existing.Status == CacheStatus.Expired)
                    existing.Status = CacheStatus.Candidate;

                return;
            }

            _pages[normalizedUrl] = new CachedPage
            {
                Url = normalizedUrl,
                PredictionScore = predictionScore,
                Status = CacheStatus.Candidate,
            };
        }
    }

    public void MarkPreloaded(
    string url,
    DateTimeOffset? currentTime = null)
    {
        if (!TryNormalizeUrl(url, out var normalizedUrl))
            return;

        lock (_lock)
        {
            if (!_pages.TryGetValue(normalizedUrl, out var page))
                return;

            var now = currentTime ?? DateTimeOffset.UtcNow;

            page.FirstPreloadedAt ??= now;
            page.LastPreloadedAt = now;
            page.Status = CacheStatus.Active;

            System.Diagnostics.Debug.WriteLine($"[Cachd Cache] Activated: {page.Url}");
        }
    }

    public void MarkVisited(
    string url,
    DateTimeOffset? currentTime = null)
    {
        if (!TryNormalizeUrl(url, out var normalizedUrl))
            return;

        lock (_lock)
        {
            if (!_pages.TryGetValue(normalizedUrl, out var page))
                return;

            page.LastVisitedAt = currentTime ?? DateTimeOffset.UtcNow;
            page.VisitCount++;

            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Cache] Visited: {page.Url} | Count: {page.VisitCount}"
            );
        }
    }

    public CachedPage? GetPage(string url)
    {
        if (!TryNormalizeUrl(url, out var normalizedUrl))
            return null;

        lock (_lock)
        {
            return _pages.TryGetValue(normalizedUrl, out var page) ? ClonePage(page) : null;
        }
    }

    public void MarkExpired(string url)
    {
        if (!TryNormalizeUrl(url, out var normalizedUrl))
            return;

        lock (_lock)
        {
            if (_pages.TryGetValue(normalizedUrl, out var page))
                page.Status = CacheStatus.Expired;
        }
    }

    public int MarkInactivePagesExpired(
        TimeSpan inactivityLimit,
        DateTimeOffset? currentTime = null
    )
    {
        if (inactivityLimit <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(inactivityLimit));

        var now = currentTime ?? DateTimeOffset.UtcNow;
        var expiredCount = 0;

        lock (_lock)
        {
            foreach (var page in _pages.Values)
            {
                if (page.Status != CacheStatus.Active)
                    continue;

                var lastActivity =
                    page.LastVisitedAt ?? page.LastPreloadedAt ?? page.FirstPreloadedAt;

                if (lastActivity is null)
                    continue;

                if (now - lastActivity.Value < inactivityLimit)
                    continue;

                page.Status = CacheStatus.Expired;
                expiredCount++;
            }
        }

        System.Diagnostics.Debug.WriteLine($"[Cachd Cache] Marked as expired: {expiredCount}");

        return expiredCount;
    }

    public void RemoveExpired()
    {
        lock (_lock)
        {
            var expiredUrls = _pages
                .Where(pair => pair.Value.Status == CacheStatus.Expired)
                .Select(pair => pair.Key)
                .ToList();

            foreach (var url in expiredUrls)
                _pages.Remove(url);

            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Cache] Removed expired pages: {expiredUrls.Count}"
            );
        }
    }

    private static CachedPage ClonePage(CachedPage page)
    {
        return new CachedPage
        {
            Url = page.Url,
            FirstPreloadedAt = page.FirstPreloadedAt,
            LastPreloadedAt = page.LastPreloadedAt,
            LastVisitedAt = page.LastVisitedAt,
            VisitCount = page.VisitCount,
            PredictionScore = page.PredictionScore,
            Status = page.Status,
        };
    }

    private static bool TryNormalizeUrl(string url, out string normalizedUrl)
    {
        normalizedUrl = string.Empty;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        if (uri.Scheme is not ("http" or "https"))
            return false;

        normalizedUrl = uri.AbsoluteUri;
        return true;
    }
}
