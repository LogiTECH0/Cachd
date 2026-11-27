using System.Text.Json;
using Cachd.Browser.Services;

namespace Cachd.Tests;

public sealed class OfflineSnapshotTests
{
    [Fact]
    public void FindsPersistedSnapshotFromDiskAfterResolverIsCalledAgain()
    {
        using var store = new TemporarySnapshotStore();
        var url = "https://example.test/wiki/Page?oldid=42";
        var path = store.AddSnapshot("page.mhtml", url);

        Assert.Equal(path, OfflineSnapshotResolver.FindSnapshotPath(store.Path, url));
        Assert.Equal(path, OfflineSnapshotResolver.FindSnapshotPath(store.Path, url));
    }

    [Fact]
    public void ExactMatchIgnoresFragment()
    {
        using var store = new TemporarySnapshotStore();
        var path = store.AddSnapshot("page.mhtml", "https://example.test/wiki/Page?oldid=42");

        Assert.Equal(path, OfflineSnapshotResolver.FindSnapshotPath(
            store.Path,
            "https://example.test/wiki/Page?oldid=42#History"));
    }

    [Fact]
    public void UsesUniquePathWhenQueryDiffers()
    {
        using var store = new TemporarySnapshotStore();
        var path = store.AddSnapshot("page.mhtml", "https://example.test/wiki/Page?oldid=42");

        Assert.Equal(path, OfflineSnapshotResolver.FindSnapshotPath(
            store.Path,
            "https://example.test/wiki/Page?oldid=99"));
    }

    [Fact]
    public void DoesNotGuessWhenMultipleSnapshotsMatchSamePath()
    {
        using var store = new TemporarySnapshotStore();
        store.AddSnapshot("old.mhtml", "https://example.test/wiki/Page?oldid=42");
        store.AddSnapshot("new.mhtml", "https://example.test/wiki/Page?oldid=43");

        Assert.Null(OfflineSnapshotResolver.FindSnapshotPath(
            store.Path,
            "https://example.test/wiki/Page?oldid=99"));
    }

    [Fact]
    public void IgnoresSnapshotThatContainsChromiumOfflineErrorPage()
    {
        using var store = new TemporarySnapshotStore();
        var path = store.AddSnapshot("error.mhtml", "https://example.test/page");
        File.WriteAllText(path, """
            From: <Saved by Blink>
            Snapshot-Content-Location: chrome-error://chromewebdata/
            MIME-Version: 1.0
            Content-Type: multipart/related;
            --boundary
            Content-Type: text/html
            Content-Location: chrome-error://chromewebdata/
            """);

        Assert.Null(OfflineSnapshotResolver.FindSnapshotPath(
            store.Path,
            "https://example.test/page"));
    }

    [Fact]
    public void RejectsChromiumErrorArchiveBeforeItCanBeSavedAsTheRequestedPage()
    {
        const string archive = """
            From: <Saved by Blink>
            Snapshot-Content-Location: chrome-error://chromewebdata/
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="snapshot"

            --snapshot
            Content-Type: text/html
            Content-Location: chrome-error://chromewebdata/
            """;

        Assert.False(OfflineSnapshotResolver.IsValidSnapshotArchive(
            archive,
            "https://example.test/page"));
    }

    [Fact]
    public void AcceptsArchiveWhoseSnapshotLocationMatchesRequestedPage()
    {
        const string archive = """
            From: <Saved by Blink>
            Snapshot-Content-Location: https://example.test/page#section
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="snapshot"

            --snapshot
            Content-Type: text/html
            Content-Location: https://example.test/page#section
            """;

        Assert.True(OfflineSnapshotResolver.IsValidSnapshotArchive(
            archive,
            "https://example.test/page"));
    }

    [Fact]
    public void ExtractsUtf8QuotedPrintableHtmlCorrectly()
    {
        using var store = new TemporarySnapshotStore();
        var path = System.IO.Path.Combine(store.Path, "utf8.mhtml");
        File.WriteAllText(path, """
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="snapshot"

            --snapshot
            Content-Type: text/html; charset="utf-8"
            Content-Transfer-Encoding: quoted-printable
            Content-Location: https://example.test/page

            <html><body><p>=D0=9F=D1=80=D0=B8=D0=B2=D1=96=D1=82</p></body></html>
            --snapshot--
            """.Replace("\n", "\r\n", StringComparison.Ordinal));

        var html = MhtmlParser.ExtractHtmlWithResources(path);

        Assert.Contains("Привіт", html);
    }

    [Fact]
    public void ExtractsMainMhtmlDocumentInsteadOfLastIframe()
    {
        using var store = new TemporarySnapshotStore();
        var path = System.IO.Path.Combine(store.Path, "multi-frame.mhtml");
        File.WriteAllText(path, """
            From: <Saved by Blink>
            Snapshot-Content-Location: https://example.test/page
            MIME-Version: 1.0
            Content-Type: multipart/related; boundary="snapshot"; start="<main@example>"

            --snapshot
            Content-Type: text/html; charset="utf-8"
            Content-ID: <main@example>
            Content-Location: https://example.test/page

            <html><body><h1>Main page content</h1></body></html>
            --snapshot
            Content-Type: text/html; charset="utf-8"
            Content-ID: <frame@example>
            Content-Location: https://player.example.test/embed

            <html><body>Empty iframe</body></html>
            --snapshot--
            """.Replace("\n", "\r\n", StringComparison.Ordinal));

        var html = MhtmlParser.ExtractHtmlWithResources(path);

        Assert.Contains("Main page content", html);
        Assert.DoesNotContain("Empty iframe", html);
    }

    private sealed class TemporarySnapshotStore : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(), "CachdOfflineTests", Guid.NewGuid().ToString("N"));

        public TemporarySnapshotStore() => Directory.CreateDirectory(Path);

        public string AddSnapshot(string fileName, string url)
        {
            var path = System.IO.Path.Combine(Path, fileName);
            File.WriteAllText(path, $"MIME-Version: 1.0\r\nContent-Location: {url}\r\n");
            File.WriteAllText(path + ".json", JsonSerializer.Serialize(new
            {
                Url = url,
                SavedAt = DateTimeOffset.UtcNow
            }));
            return path;
        }

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
