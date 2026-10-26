
using Cachd.Core;

namespace Cachd.Prediction;

public interface INextPagePredictor
{
    IReadOnlyList<PagePrediction> Predict(
        IReadOnlyList<NavigationEvent> history,
        string currentUrl,
        int maxResults);
}
