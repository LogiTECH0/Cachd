using System.IO;
using System.Text.Json;
using Cachd.Core;
using Cachd.Preloading;

namespace Cachd.Browser.Services;

public sealed class OfflineCacheBridge
{
    private readonly CacheManager _cacheManager;
    private readonly string _storageDirectory;

    public OfflineCacheBridge(CacheManager cacheManager)
    {
        _cacheManager = cacheManager;
        _storageDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cachd",
            "OfflinePages"
        );
    }

    public string GetCachedPagesJson()
    {
        var savedVisitCounts = LoadSavedVisitCounts();
        var visitData = _cacheManager.Pages.ToDictionary(
            p => p.Url,
            p => p,
            StringComparer.Ordinal);

        var pages = new List<(DateTimeOffset SortDate, object Page)>();
        if (Directory.Exists(_storageDirectory))
        {
            foreach (var mhtmlPath in Directory.EnumerateFiles(_storageDirectory, "*.mhtml"))
            {
                var metadata = OfflineSnapshotResolver.ReadMetadata(mhtmlPath);
                if (metadata is null)
                    continue;

                var page = _cacheManager.GetPage(metadata.Url);
                var openPath = OfflineSnapshotService.EnsureInteractiveHtml(mhtmlPath) ?? mhtmlPath;
                pages.Add((page?.LastVisitedAt ?? metadata.SavedAt, new
                    {
                        url = metadata.Url,
                        mhtmlUrl = CreateOfflineUrl(openPath),
                        title = GetTitleFromUrl(metadata.Url),
                        lastVisited = (page?.LastVisitedAt ?? metadata.SavedAt).ToUnixTimeSeconds(),
                        visitCount = page is { VisitCount: > 0 }
                            ? page.VisitCount
                            : savedVisitCounts.GetValueOrDefault(metadata.Url)
                    }));
            }
        }

        return JsonSerializer.Serialize(pages
            .OrderByDescending(page => page.SortDate)
            .Select(page => page.Page));
    }

    private static Dictionary<string, int> LoadSavedVisitCounts()
    {
        try
        {
            var historyPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cachd",
                "navigation-history.json");
            if (!File.Exists(historyPath))
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            return (JsonSerializer.Deserialize<List<NavigationEvent>>(File.ReadAllText(historyPath)) ?? [])
                .GroupBy(item => item.Url, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd Offline] Could not load saved visit counts: {ex.Message}");
            return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public string? GetSnapshotFileUrl(string url)
    {
        var snapshotPath = OfflineSnapshotResolver.FindSnapshotPath(_storageDirectory, url);
        if (snapshotPath is null)
            return null;

        var htmlPath = OfflineSnapshotService.EnsureInteractiveHtml(snapshotPath);
        return CreateOfflineUrl(htmlPath ?? snapshotPath);
    }

    private static string CreateOfflineUrl(string path)
        => $"cachd-mhtml://{Uri.EscapeDataString(Path.GetFileName(path))}";

    private static string GetTitleFromUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return url;

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;

        return host switch
        {
            "github.com" => "GitHub",
            "youtube.com" => "YouTube",
            "spotify.com" => "Spotify",
            "google.com" => "Google",
            "vercel.com" => "Vercel",
            "wikipedia.org" => "Wikipedia",
            "chatgpt.com" => "ChatGPT",
            "reddit.com" => "Reddit",
            _ => host
        };
    }
}
