using System.IO;
using System.Text.Json;
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
        var visitData = _cacheManager.Pages.ToDictionary(
            p => p.Url,
            p => p,
            StringComparer.Ordinal);

        var pages = new List<(DateTimeOffset SortDate, object Page)>();
        if (Directory.Exists(_storageDirectory))
        {
            foreach (var mhtmlPath in Directory.EnumerateFiles(_storageDirectory, "*.mhtml"))
            {
                var metadata = ReadMetadata(mhtmlPath);
                if (metadata is null)
                    continue;

                var page = _cacheManager.GetPage(metadata.Url);
                var openPath = OfflineSnapshotService.EnsureInteractiveHtml(mhtmlPath) ?? mhtmlPath;
                pages.Add((page?.LastVisitedAt ?? metadata.SavedAt, new
                    {
                        url = metadata.Url,
                        mhtmlUrl = new Uri(openPath).AbsoluteUri,
                        title = GetTitleFromUrl(metadata.Url),
                        lastVisited = (page?.LastVisitedAt ?? metadata.SavedAt).ToUnixTimeSeconds(),
                        visitCount = page?.VisitCount ?? 0
                    }));
            }
        }

        return JsonSerializer.Serialize(pages
            .OrderByDescending(page => page.SortDate)
            .Select(page => page.Page));
    }

    public string? GetSnapshotFileUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var requestedUri)
            || requestedUri.Scheme is not ("http" or "https")
            || !Directory.Exists(_storageDirectory))
            return null;

        foreach (var mhtmlPath in Directory.EnumerateFiles(_storageDirectory, "*.mhtml"))
        {
            var metadata = ReadMetadata(mhtmlPath);
            if (metadata is not null
                && Uri.Compare(new Uri(metadata.Url), requestedUri,
                    UriComponents.AbsoluteUri, UriFormat.SafeUnescaped,
                    StringComparison.OrdinalIgnoreCase) == 0)
            {
                var htmlPath = OfflineSnapshotService.EnsureInteractiveHtml(mhtmlPath);
                return new Uri(htmlPath ?? mhtmlPath).AbsoluteUri;
            }
        }

        return null;
    }

    private static OfflineSnapshotMetadata? ReadMetadata(string mhtmlPath)
    {
        try
        {
            if (!File.Exists(mhtmlPath))
                return null;

            var metadataPath = mhtmlPath + ".json";
            OfflineSnapshotMetadata? metadata = null;
            if (File.Exists(metadataPath))
                metadata = JsonSerializer.Deserialize<OfflineSnapshotMetadata>(File.ReadAllText(metadataPath));

            if (metadata is null)
            {
                using var reader = new StreamReader(mhtmlPath);
                string? line;
                var bytesRead = 0;
                string? recoveredUrl = null;
                while ((line = reader.ReadLine()) is not null && bytesRead < 65536)
                {
                    bytesRead += line.Length + 2;
                    if (line.StartsWith("Content-Location:", StringComparison.OrdinalIgnoreCase))
                    {
                        recoveredUrl = line["Content-Location:".Length..].Trim();
                        break;
                    }
                }

                if (recoveredUrl is not null)
                    metadata = new OfflineSnapshotMetadata(
                        recoveredUrl,
                        new DateTimeOffset(File.GetLastWriteTimeUtc(mhtmlPath), TimeSpan.Zero));
            }

            if (metadata is null
                || !Uri.TryCreate(metadata.Url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
                return null;

            return metadata with { Url = uri.AbsoluteUri };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd Offline] Invalid snapshot metadata: {ex.Message}");
            return null;
        }
    }

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
