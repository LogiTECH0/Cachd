using System.IO;
using System.Net;
using System.Text;
using CefSharp;
using Cachd.Browser.Services;

namespace Cachd.Browser.Handlers;

public sealed class CachdSchemeHandlerFactory : ISchemeHandlerFactory
{
    public IResourceHandler Create(
        IBrowser browser,
        IFrame frame,
        string schemeName,
        IRequest request
    )
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            return NotFound();

        if (schemeName.Equals("cachd-mhtml", StringComparison.OrdinalIgnoreCase))
        {
            var fileName = uri.Host;
            if (string.IsNullOrEmpty(fileName) || fileName == "/")
            {
                fileName = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));
            }

            var offlinePagesDir = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Cachd",
                "OfflinePages"
            ));

            fileName = Path.GetFileName(fileName);
            var fullPath = Path.GetFullPath(Path.Combine(offlinePagesDir, fileName));
            if (!fullPath.StartsWith(
                    offlinePagesDir + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                return NotFound();

            if (File.Exists(fullPath))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd MHTML] Found exact match: {fullPath}");
                var offlineMimeType = Path.GetExtension(fullPath).Equals(".mhtml", StringComparison.OrdinalIgnoreCase)
                    ? "application/x-mimearchive"
                    : Cef.GetMimeType(Path.GetExtension(fullPath));
                return ResourceHandler.FromFilePath(fullPath, offlineMimeType, autoDisposeStream: true);
            }

            if (!Directory.Exists(offlinePagesDir))
                return NotFound();

            var files = Directory.GetFiles(offlinePagesDir, "*.mhtml");
            var matchingFile = files.FirstOrDefault(f =>
                Path.GetFileName(f).Equals(fileName, StringComparison.OrdinalIgnoreCase));

            if (matchingFile != null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd MHTML] Found case-insensitive match: {matchingFile}");
                return ResourceHandler.FromFilePath(matchingFile, "application/x-mimearchive", autoDisposeStream: true);
            }

            System.Diagnostics.Debug.WriteLine(
                $"[Cachd MHTML] Not found: {fileName} in {offlinePagesDir}");

            return NotFound();
        }

        var baseDirectory = AppContext.BaseDirectory;

        var directoryRoot = uri.Host.ToLowerInvariant() switch
        {
            "home" => Path.Combine(baseDirectory, "Pages", "Home"),
            "settings" => Path.Combine(baseDirectory, "Pages", "Settings"),
            "offline" => Path.Combine(baseDirectory, "Pages", "Offline"),
            "assets" => Path.Combine(baseDirectory, "Assets"),
            _ => null,
        };

        if (directoryRoot is null)
            return NotFound();

        var root = Path.GetFullPath(directoryRoot);

        var relativePath = Uri.UnescapeDataString(uri.AbsolutePath.TrimStart('/'));

        if (string.IsNullOrWhiteSpace(relativePath))
            relativePath = "index.html";

        var filePath = Path.GetFullPath(Path.Combine(root, relativePath));

        var rootWithSeparator = root + Path.DirectorySeparatorChar;

        if (!filePath.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        if (!File.Exists(filePath))
            return NotFound();

        var mimeType = Cef.GetMimeType(Path.GetExtension(filePath));

        return ResourceHandler.FromFilePath(filePath, mimeType, autoDisposeStream: true);
    }

    private static IResourceHandler NotFound()
    {
        return ResourceHandler.ForErrorMessage("Cachd page not found.", HttpStatusCode.NotFound);
    }
}
