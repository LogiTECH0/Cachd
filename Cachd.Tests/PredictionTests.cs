using Cachd.Core;
using Cachd.Prediction;
using Xunit;

namespace Cachd.Tests;

public sealed class PredictionTests
{
    private readonly INextPagePredictor _predictor = new MostFrequentPredictor();

    private static NavigationEvent Event(string url) => new(url, DateTimeOffset.UnixEpoch, null);

    [Fact]
    public void Predict_RanksPagesByVisitCount()
    {
        NavigationEvent[] history =
        [
            Event("A"),
            Event("B"),
            Event("B"),
            Event("C"),
            Event("B"),
            Event("C"),
        ];

        var result = _predictor.Predict(history, "D", 3);

        Assert.Equal(3, result.Count);
        Assert.Equal("B", result[0].Url);
        Assert.Equal(3, result[0].Score);
        Assert.Equal("C", result[1].Url);
        Assert.Equal(2, result[1].Score);
        Assert.Equal("A", result[2].Url);
        Assert.Equal(1, result[2].Score);
    }

    [Fact]
    public void Predict_ExcludesCurrentUrl()
    {
        NavigationEvent[] history = [Event("A"), Event("A"), Event("B"), Event("B"), Event("B")];

        var result = _predictor.Predict(history, "A", 10);

        var prediction = Assert.Single(result);
        Assert.Equal("B", prediction.Url);
        Assert.Equal(3, prediction.Score);
    }

    [Fact]
    public void Predict_RespectsMaxResults()
    {
        NavigationEvent[] history =
        [
            Event("A"),
            Event("B"),
            Event("B"),
            Event("C"),
            Event("C"),
            Event("C"),
        ];

        var result = _predictor.Predict(history, "D", 2);

        Assert.Equal(2, result.Count);
        Assert.Equal("C", result[0].Url);
        Assert.Equal("B", result[1].Url);
    }

    [Fact]
    public void Predict_ReturnsEmptyForEmptyHistory()
    {
        var result = _predictor.Predict(Array.Empty<NavigationEvent>(), "A", 5);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Predict_ReturnsEmptyForNonPositiveLimit(int maxResults)
    {
        NavigationEvent[] history = [Event("A"), Event("B")];

        var result = _predictor.Predict(history, "C", maxResults);

        Assert.Empty(result);
    }

    [Fact]
    public void Predict_ReturnsEachUrlOnlyOnce()
    {
        NavigationEvent[] history = [Event("A"), Event("B"), Event("A"), Event("B"), Event("A")];

        var result = _predictor.Predict(history, "C", 10);

        Assert.Equal(2, result.Count);
        Assert.Equal(2, result.Select(p => p.Url).Distinct().Count());
    }

    [Fact]
    public void Predict_HandlesEqualScores()
    {
        NavigationEvent[] history = [Event("A"), Event("B")];

        var result = _predictor.Predict(history, "C", 2);

        Assert.Equal(2, result.Count);
        Assert.All(result, prediction => Assert.Equal(1, prediction.Score));
    }

    [Fact]
    public void TransitionPredictor_RanksTransitionsByFrequency()
    {
        var predictor = new TransitionPredictor();

        NavigationEvent[] history =
        [
            new("B", DateTimeOffset.UnixEpoch, "A"),
            new("C", DateTimeOffset.UnixEpoch, "B"),
            new("B", DateTimeOffset.UnixEpoch, "A"),
            new("D", DateTimeOffset.UnixEpoch, "B"),
            new("B", DateTimeOffset.UnixEpoch, "A"),
            new("C", DateTimeOffset.UnixEpoch, "B"),
        ];

        var result = predictor.Predict(history, "B", 3);

        Assert.Equal(2, result.Count);

        Assert.Equal("C", result[0].Url);
        Assert.Equal(2, result[0].Score);

        Assert.Equal("D", result[1].Url);
        Assert.Equal(1, result[1].Score);
    }

    [Fact]
    public void TransitionPredictor_IgnoresTransitionsFromOtherPages()
    {
        var predictor = new TransitionPredictor();

        NavigationEvent[] history =
        [
            new("C", DateTimeOffset.UnixEpoch, "A"),
            new("C", DateTimeOffset.UnixEpoch, "A"),
            new("D", DateTimeOffset.UnixEpoch, "B"),
        ];

        var result = predictor.Predict(history, "B", 5);

        var prediction = Assert.Single(result);

        Assert.Equal("D", prediction.Url);
        Assert.Equal(1, prediction.Score);
    }

    [Fact]
    public void TransitionPredictor_ReturnsEmptyWhenNoTransitionsExist()
    {
        var predictor = new TransitionPredictor();

        NavigationEvent[] history =
        [
            new("A", DateTimeOffset.UnixEpoch, "B"),
            new("C", DateTimeOffset.UnixEpoch, "D"),
        ];

        var result = predictor.Predict(history, "X", 5);

        Assert.Empty(result);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void TransitionPredictor_ReturnsEmptyForNonPositiveLimit(int maxResults)
    {
        var predictor = new TransitionPredictor();

        NavigationEvent[] history = [new("C", DateTimeOffset.UnixEpoch, "B")];

        var result = predictor.Predict(history, "B", maxResults);

        Assert.Empty(result);
    }

    [Fact]
    public void HitAtKEvaluator_CalculatesHitRateCorrectly()
    {
        var predictor = new TransitionPredictor();
        var evaluator = new HitAtKEvaluator();

        NavigationEvent[] history =
        [
            new("A", DateTimeOffset.UnixEpoch, null),
            new("B", DateTimeOffset.UnixEpoch, "A"),
            new("C", DateTimeOffset.UnixEpoch, "B"),
            new("B", DateTimeOffset.UnixEpoch, "A"),
            new("C", DateTimeOffset.UnixEpoch, "B"),
            new("D", DateTimeOffset.UnixEpoch, "B"),
        ];

        var result = evaluator.Evaluate(predictor, history, 1);

        Assert.Equal(5, result.EvaluatedCases);
        Assert.Equal(2, result.Hits);
        Assert.Equal(0.4, result.HitRate, 10);
    }

    [Fact]
    public void HitAtKEvaluator_ReturnsZeroForHistoryWithoutEvaluableCases()
    {
        var predictor = new TransitionPredictor();
        var evaluator = new HitAtKEvaluator();

        NavigationEvent[] history = [new("A", DateTimeOffset.UnixEpoch, null)];

        var result = evaluator.Evaluate(predictor, history, 3);

        Assert.Equal(0, result.EvaluatedCases);
        Assert.Equal(0, result.Hits);
        Assert.Equal(0.0, result.HitRate);
    }

    [Fact]
    public void HitAtKEvaluator_DoesNotLearnFromTestHistory()
    {
        var predictor = new TransitionPredictor();
        var evaluator = new HitAtKEvaluator();

        NavigationEvent[] trainingHistory = [new("B", DateTimeOffset.UnixEpoch, "A")];

        NavigationEvent[] testHistory =
        [
            new("C", DateTimeOffset.UnixEpoch, "B"),
            new("C", DateTimeOffset.UnixEpoch.AddMinutes(1), "B"),
        ];

        var result = evaluator.Evaluate(predictor, trainingHistory, testHistory, 1);

        Assert.Equal(2, result.EvaluatedCases);
        Assert.Equal(0, result.Hits);
        Assert.Equal(0.0, result.HitRate);
    }
}
