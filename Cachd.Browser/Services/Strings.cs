namespace Cachd.Browser;

public static class Strings
{
    public static string Language { get; set; } = "uk";

    private static readonly Dictionary<string, (string uk, string en)> _map = new()
    {
        ["tab.new"] = ("Нова вкладка", "New tab"),
        ["nav.go"] = ("Перейти", "Go"),
        ["pred.open"] = ("Відкрий сторінку, щоб почати прогнозування.", "Open a page to start predicting."),
        ["pred.few"] = ("Недостатньо історії для прогнозування.", "Not enough history to predict."),
    };

    public static string Get(string key) =>
        _map.TryGetValue(key, out var v) ? (Language == "uk" ? v.uk : v.en) : key;
}
