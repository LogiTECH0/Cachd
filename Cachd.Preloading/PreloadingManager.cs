
using Cachd.Core;

namespace Cachd.Preloading;

public sealed class PreloadingManager
{
    private readonly HashSet<string> _scheduledUrls = new(
        StringComparer.Ordinal);

    public string? SelectCandidate(
        string currentUrl,
        IReadOnlyList<PagePrediction> predictions)
    {
        foreach (var prediction in predictions.OrderByDescending(p => p.Score))
        {
            if (!Uri.TryCreate(
                    prediction.Url,
                    UriKind.Absolute,
                    out var uri))
            {
                continue;
            }

            if (uri.Scheme is not ("http" or "https"))
                continue;

            if (string.Equals(
                    prediction.Url,
                    currentUrl,
                    StringComparison.Ordinal))
            {
                continue;
            }

            if (_scheduledUrls.Add(prediction.Url))
                return prediction.Url;
        }

        return null;
    }
}
