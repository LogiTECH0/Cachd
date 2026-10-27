using Cachd.Core;

namespace Cachd.Prediction;

public sealed class HitAtKEvaluator
{
    public PredictionEvaluationResult Evaluate(
        INextPagePredictor predictor,
        IReadOnlyList<NavigationEvent> history,
        int k)
    {
        ArgumentNullException.ThrowIfNull(predictor);
        ArgumentNullException.ThrowIfNull(history);

        if (k <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(k),
                "K must be greater than zero.");
        }

        var evaluatedCases = 0;
        var hits = 0;

        for (var i = 0; i < history.Count; i++)
        {
            var actualEvent = history[i];
            if (actualEvent.PreviousUrl is null)
            {
                continue;
            }
            var trainingHistory = history.Take(i).ToArray();

            var predictions = predictor.Predict(
                trainingHistory,
                actualEvent.PreviousUrl,
                k);

            evaluatedCases++;

            if (predictions.Any(
                prediction => prediction.Url == actualEvent.Url))
            {
                hits++;
            }
        }

        var hitRate = evaluatedCases == 0
            ? 0.0
            : (double)hits / evaluatedCases;

        return new PredictionEvaluationResult(
            evaluatedCases,
            hits,
            hitRate);
    }
}