
using CefSharp;
using System.IO;
using System.Net;

namespace Cachd.Browser.Handlers;

public sealed class CachdSchemeHandlerFactory : ISchemeHandlerFactory
{
    public IResourceHandler Create(
        IBrowser browser,
        IFrame frame,
        string schemeName,
        IRequest request)
    {
        if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var uri))
            return NotFound();

        var baseDirectory = AppContext.BaseDirectory;

        string? directoryRoot = uri.Host.ToLowerInvariant() switch
        {
            "home" => Path.Combine(baseDirectory, "Pages", "Home"),
            "settings" => Path.Combine(baseDirectory, "Pages", "Settings"),
            "assets" => Path.Combine(baseDirectory, "Assets"),
            _ => null
        };

        if (directoryRoot is null)
            return NotFound();

        var root = Path.GetFullPath(directoryRoot);

        var relativePath = Uri.UnescapeDataString(
            uri.AbsolutePath.TrimStart('/')
        );

        if (string.IsNullOrWhiteSpace(relativePath))
            relativePath = "index.html";

        var filePath = Path.GetFullPath(
            Path.Combine(root, relativePath)
        );

        var rootWithSeparator = root + Path.DirectorySeparatorChar;

        if (!filePath.StartsWith(
            rootWithSeparator,
            StringComparison.OrdinalIgnoreCase))
        {
            return NotFound();
        }

        if (!File.Exists(filePath))
            return NotFound();

        var mimeType = Cef.GetMimeType(Path.GetExtension(filePath));

        return ResourceHandler.FromFilePath(
            filePath,
            mimeType,
            autoDisposeStream: true
        );
    }

    private static IResourceHandler NotFound()
    {
        return ResourceHandler.ForErrorMessage(
            "Cachd page not found.",
            HttpStatusCode.NotFound
        );
    }
}
