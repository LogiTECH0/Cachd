using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Cachd.Browser.Services;

internal static class OfflineSnapshotResolver
{
    public static bool IsValidSnapshotArchive(string data, string expectedUrl)
    {
        if (!data.Contains("MIME-Version:", StringComparison.OrdinalIgnoreCase)
            || !data.Contains("multipart/related", StringComparison.OrdinalIgnoreCase)
            || !data.Contains("Content-Type: text/html", StringComparison.OrdinalIgnoreCase))
            return false;

        var snapshotLocationMatch = Regex.Match(
            data,
            @"(?im)^Snapshot-Content-Location:\s*(?<url>[^\r\n]+)");
        var documentLocationMatch = Regex.Match(
            data,
            @"(?im)^Content-Location:\s*(?<url>[^\r\n]+)");
        var archivedUrl = snapshotLocationMatch.Success
            ? snapshotLocationMatch.Groups["url"].Value.Trim()
            : documentLocationMatch.Success
                ? documentLocationMatch.Groups["url"].Value.Trim()
                : null;

        return archivedUrl is not null
            && Uri.TryCreate(archivedUrl, UriKind.Absolute, out var archivedUri)
            && Uri.TryCreate(expectedUrl, UriKind.Absolute, out var expectedUri)
            && archivedUri.Scheme is "http" or "https"
            && Uri.Compare(archivedUri, expectedUri, UriComponents.HttpRequestUrl,
                UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
    }

    public static string? FindSnapshotPath(string storageDirectory, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var requestedUri)
            || requestedUri.Scheme is not ("http" or "https")
            || !Directory.Exists(storageDirectory))
            return null;

        var snapshots = Directory.EnumerateFiles(storageDirectory, "*.mhtml")
            .Select(path => (Path: path, Metadata: ReadMetadata(path)))
            .Where(item => item.Metadata is not null)
            .Select(item => (item.Path, Metadata: item.Metadata!))
            .ToArray();

        var exact = snapshots.FirstOrDefault(item => Uri.Compare(
            new Uri(item.Metadata.Url), requestedUri,
            UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped,
            StringComparison.OrdinalIgnoreCase) == 0);

        if (exact.Path is not null)
            return exact.Path;

        var samePage = snapshots
            .Where(item => Uri.Compare(
                new Uri(item.Metadata.Url), requestedUri,
                UriComponents.SchemeAndServer | UriComponents.Path,
                UriFormat.SafeUnescaped,
                StringComparison.OrdinalIgnoreCase) == 0)
            .Take(2)
            .ToArray();

        return samePage.Length == 1 ? samePage[0].Path : null;
    }

    public static OfflineSnapshotMetadata? ReadMetadata(string mhtmlPath)
    {
        try
        {
            if (!File.Exists(mhtmlPath))
                return null;

            var snapshotUrl = ReadSnapshotContentLocation(mhtmlPath);
            var metadataPath = mhtmlPath + ".json";
            OfflineSnapshotMetadata? metadata = null;
            if (File.Exists(metadataPath))
                metadata = JsonSerializer.Deserialize<OfflineSnapshotMetadata>(File.ReadAllText(metadataPath));

            if (metadata is null)
            {
                if (snapshotUrl is not null)
                    metadata = new OfflineSnapshotMetadata(
                        snapshotUrl,
                        new DateTimeOffset(File.GetLastWriteTimeUtc(mhtmlPath), TimeSpan.Zero));
            }

            if (metadata is null
                || !Uri.TryCreate(metadata.Url, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https"))
                return null;

            if (snapshotUrl is not null
                && (!Uri.TryCreate(snapshotUrl, UriKind.Absolute, out var archivedUri)
                    || archivedUri.Scheme is not ("http" or "https")
                    || Uri.Compare(uri, archivedUri, UriComponents.HttpRequestUrl,
                        UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) != 0))
                return null;

            return metadata with { Url = uri.AbsoluteUri };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd Offline] Invalid snapshot metadata: {ex.Message}");
            return null;
        }
    }

    public static string? ReadSnapshotContentLocation(string mhtmlPath)
    {
        try
        {
            using var reader = new StreamReader(mhtmlPath);
            string? line;
            var bytesRead = 0;
            string? snapshotUrl = null;
            string? contentUrl = null;
            while ((line = reader.ReadLine()) is not null && bytesRead < 65536)
            {
                bytesRead += line.Length + 2;
                if (line.StartsWith("Snapshot-Content-Location:", StringComparison.OrdinalIgnoreCase))
                    snapshotUrl = line["Snapshot-Content-Location:".Length..].Trim();
                else if (contentUrl is null
                    && line.StartsWith("Content-Location:", StringComparison.OrdinalIgnoreCase))
                    contentUrl = line["Content-Location:".Length..].Trim();

                if (line.StartsWith("--", StringComparison.Ordinal))
                    break;
            }

            return snapshotUrl ?? contentUrl;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd Offline] Could not read snapshot URL: {ex.Message}");
            return null;
        }
    }
}

internal sealed record OfflineSnapshotMetadata(string Url, DateTimeOffset SavedAt);
