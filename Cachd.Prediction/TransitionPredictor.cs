using Cachd.Core;

namespace Cachd.Prediction;

public sealed class TransitionPredictor : INextPagePredictor
{
    public IReadOnlyList<PagePrediction> Predict(
        IReadOnlyList<NavigationEvent> history,
        string currentUrl,
        int maxResults)
    {
        if (maxResults <= 0 || history.Count == 0)
        {
            return Array.Empty<PagePrediction>();
        }

        var transitions = new Dictionary<string, int>();

        foreach (var navigationEvent in history)
        {
            if (navigationEvent.PreviousUrl != currentUrl)
            {
                continue;
            }

            if (navigationEvent.Url == currentUrl)
            {
                continue;
            }

            if (!transitions.ContainsKey(navigationEvent.Url))
            {
                transitions[navigationEvent.Url] = 0;
            }

            transitions[navigationEvent.Url]++;
        }

        return transitions
            .OrderByDescending(transition => transition.Value)
            .ThenBy(transition => transition.Key, StringComparer.Ordinal)
            .Take(maxResults)
            .Select(transition => new PagePrediction(
                transition.Key,
                transition.Value))
            .ToList();
    }
}