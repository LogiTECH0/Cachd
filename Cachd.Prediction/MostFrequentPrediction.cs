using Cachd.Core;
namespace Cachd.Prediction;
public sealed class MostFrequentPredictor : INextPagePredictor
{
    public IReadOnlyList<PagePrediction> Predict(
        IReadOnlyList<NavigationEvent> history,
        string currentUrl,
        int maxResults)
    {
        var visits = new Dictionary<string,int>();
        foreach (var e in history)
        {   
            if (!visits.ContainsKey(e.Url))
                visits[e.Url] = 0;
            visits[e.Url]++;
        }
        return maxResults <= 0 
            ? Array.Empty<PagePrediction>() 
            : visits
                .Where(kv => kv.Key != currentUrl)
                .OrderByDescending(kv => kv.Value)
                .Take(maxResults)
                .Select(kv => new PagePrediction(kv.Key, kv.Value))
                .ToList();
    }
}
