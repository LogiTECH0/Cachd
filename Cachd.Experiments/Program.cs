using System.Globalization;
using System.Text;
using Cachd.Core;
using Cachd.Prediction;

var sessions = new[]
{
    new[] { "A", "B", "C", "D" },
    new[] { "A", "B", "C", "E" },
    new[] { "A", "B", "D" },
    new[] { "A", "B", "C", "D" },
    new[] { "A", "F", "C", "D" },
    new[] { "A", "B", "C", "E" },
    new[] { "F", "C", "D" },
    new[] { "F", "C", "E" },
    new[] { "A", "B", "C", "D" },
    new[] { "A", "F", "C", "E" }
};

var trainingSessions = sessions.Take(7).ToArray();
var testSessions = sessions.Skip(7).ToArray();

var trainingHistory = BuildHistory(trainingSessions);
var testHistory = BuildHistory(testSessions);

INextPagePredictor[] predictors =
[
    new MostFrequentPredictor(),
    new TransitionPredictor()
];

int[] kValues = [1, 3, 5];

var evaluator = new HitAtKEvaluator();
var results = new List<ExperimentRow>();

Console.WriteLine("Cachd — Prediction Experiments");
Console.WriteLine($"Training sessions: {trainingSessions.Length}");
Console.WriteLine($"Test sessions: {testSessions.Length}");
Console.WriteLine($"Training events: {trainingHistory.Count}");
Console.WriteLine($"Test events: {testHistory.Count}");
Console.WriteLine();

Console.WriteLine(
    $"{"Algorithm",-25} {"K",3} {"Cases",8} {"Hits",8} {"HitRate",10}");

Console.WriteLine(new string('-', 60));

foreach (var predictor in predictors)
{
    foreach (var k in kValues)
    {
        var result = evaluator.Evaluate(
            predictor,
            trainingHistory,
            testHistory,
            k);

        var row = new ExperimentRow(
            predictor.GetType().Name,
            k,
            result.EvaluatedCases,
            result.Hits,
            result.HitRate);

        results.Add(row);

        Console.WriteLine(
            $"{row.Algorithm,-25} " +
            $"{row.K,3} " +
            $"{row.EvaluatedCases,8} " +
            $"{row.Hits,8} " +
            $"{row.HitRate,9:P2}");
    }
}

var csv = new StringBuilder();

csv.AppendLine("Algorithm,K,EvaluatedCases,Hits,HitRate");

foreach (var row in results)
{
    csv.AppendLine(
        $"{row.Algorithm}," +
        $"{row.K}," +
        $"{row.EvaluatedCases}," +
        $"{row.Hits}," +
        row.HitRate.ToString("F6", CultureInfo.InvariantCulture));
}

var outputPath = Path.Combine(
    Directory.GetCurrentDirectory(),
    "results.csv");

File.WriteAllText(
    outputPath,
    csv.ToString(),
    Encoding.UTF8);

Console.WriteLine();
Console.WriteLine($"Results saved to: {outputPath}");

static List<NavigationEvent> BuildHistory(
    IEnumerable<string[]> sourceSessions)
{
    var history = new List<NavigationEvent>();
    var timestamp = DateTimeOffset.UnixEpoch;

    foreach (var session in sourceSessions)
    {
        string? previousUrl = null;

        foreach (var url in session)
        {
            history.Add(new NavigationEvent(
                url,
                timestamp,
                previousUrl));

            previousUrl = url;
            timestamp = timestamp.AddMinutes(1);
        }
    }

    return history;
}

internal sealed record ExperimentRow(
    string Algorithm,
    int K,
    int EvaluatedCases,
    int Hits,
    double HitRate);