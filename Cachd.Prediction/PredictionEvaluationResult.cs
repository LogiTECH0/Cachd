namespace Cachd.Prediction;

public sealed record PredictionEvaluationResult(
    int EvaluatedCases,
    int Hits,
    double HitRate
);