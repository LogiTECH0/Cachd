using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Cachd.Browser.Services;

public static class MhtmlParser
{
    public static string ExtractHtmlWithResources(string mhtmlPath)
    {
        try
        {
            var content = File.ReadAllText(mhtmlPath, Encoding.UTF8);
            
            var boundaryMatch = Regex.Match(content, @"boundary=""?([^""\r\n]+)""?", RegexOptions.IgnoreCase);
            if (!boundaryMatch.Success)
            {
                System.Diagnostics.Debug.WriteLine("[Cachd MHTML] No boundary found");
                return content;
            }

            var boundary = boundaryMatch.Groups[1].Value;
            var parts = content.Split($"--{boundary}");

            var snapshotLocationMatch = Regex.Match(
                content,
                @"(?im)^Snapshot-Content-Location:\s*(?<url>[^\r\n]+)");
            var snapshotUrl = snapshotLocationMatch.Success
                ? snapshotLocationMatch.Groups["url"].Value.Trim()
                : null;
            var snapshotStartMatch = Regex.Match(
                content,
                @"(?im)^\s*start\s*=\s*<(?<id>[^>]+)>");
            var snapshotContentId = snapshotStartMatch.Success
                ? snapshotStartMatch.Groups["id"].Value.Trim()
                : null;

            string? htmlContent = null;
            var selectedMainDocument = false;
            var resources = new Dictionary<string, string>();

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;

                var headerEnd = part.IndexOf("\r\n\r\n");
                if (headerEnd == -1) headerEnd = part.IndexOf("\n\n");
                if (headerEnd == -1) continue;

                var headers = part.Substring(0, headerEnd);
                var body = part.Substring(headerEnd + (part.IndexOf("\r\n\r\n") != -1 ? 4 : 2));

                var contentTypeMatch = Regex.Match(headers, @"Content-Type:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                var contentLocationMatch = Regex.Match(headers, @"Content-Location:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                var contentIdMatch = Regex.Match(headers, @"Content-ID:\s*<([^>]+)>", RegexOptions.IgnoreCase);
                var contentTransferEncodingMatch = Regex.Match(headers, @"Content-Transfer-Encoding:\s*([^\r\n]+)", RegexOptions.IgnoreCase);

                var contentType = contentTypeMatch.Success ? contentTypeMatch.Groups[1].Value.Trim() : "";
                var textEncoding = GetTextEncoding(headers);
                var contentLocation = contentLocationMatch.Success ? contentLocationMatch.Groups[1].Value.Trim() : "";
                var contentId = contentIdMatch.Success ? contentIdMatch.Groups[1].Value.Trim() : "";
                var encoding = contentTransferEncodingMatch.Success ? contentTransferEncodingMatch.Groups[1].Value.Trim() : "";

                byte[] resourceBytes;
                if (encoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        resourceBytes = Convert.FromBase64String(body.Trim());
                        body = textEncoding.GetString(resourceBytes);
                    }
                    catch
                    {
                        resourceBytes = Encoding.UTF8.GetBytes(body);
                    }
                }
                else if (encoding.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
                {
                    resourceBytes = DecodeQuotedPrintableBytes(body);
                    body = textEncoding.GetString(resourceBytes);
                }
                else
                {
                    resourceBytes = Encoding.UTF8.GetBytes(body);
                    body = textEncoding.GetString(resourceBytes);
                }

                if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    var matchesSnapshotLocation = !string.IsNullOrEmpty(snapshotUrl)
                        && Uri.TryCreate(snapshotUrl, UriKind.Absolute, out var snapshotUri)
                        && Uri.TryCreate(contentLocation, UriKind.Absolute, out var partUri)
                        && Uri.Compare(snapshotUri, partUri, UriComponents.HttpRequestUrl,
                            UriFormat.SafeUnescaped, StringComparison.OrdinalIgnoreCase) == 0;
                    var matchesSnapshotContentId = !string.IsNullOrEmpty(snapshotContentId)
                        && string.Equals(contentId, snapshotContentId, StringComparison.OrdinalIgnoreCase);

                    if (matchesSnapshotLocation || matchesSnapshotContentId)
                    {
                        htmlContent = body;
                        selectedMainDocument = true;
                    }
                    else if (!selectedMainDocument && htmlContent is null)
                    {
                        htmlContent = body;
                    }

                    System.Diagnostics.Debug.WriteLine(
                        $"[Cachd MHTML] HTML part: {contentLocation} | Main document: {matchesSnapshotLocation || matchesSnapshotContentId} | Length: {body.Length}");
                }
                else if (!string.IsNullOrEmpty(contentLocation) || !string.IsNullOrEmpty(contentId))
                {
                    var mimeType = GetMimeTypeFromContentType(contentType);
                    var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(resourceBytes)}";
                    
                    System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Resource: {contentLocation ?? contentId} | Type: {mimeType} | Size: {(resourceBytes?.Length ?? body.Length)} bytes");
                    
                    if (!string.IsNullOrEmpty(contentLocation))
                    {
                        resources[contentLocation] = dataUri;
                        System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Stored resource by location: {contentLocation} ({mimeType})");
                        
                        if (contentLocation.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                        {
                            var cidShort = contentLocation.Split('@')[0];
                            resources[cidShort] = dataUri;
                            System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Also stored short CID: {cidShort}");
                        }
                    }
                    if (!string.IsNullOrEmpty(contentId))
                    {
                        var cidFull = $"cid:{contentId}";
                        var cidShort = $"cid:{contentId.Split('@')[0]}";
                        resources[cidFull] = dataUri;
                        resources[cidShort] = dataUri;
                        System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Stored resource by CID: {cidFull} and {cidShort} ({mimeType})");
                    }
                }
            }

            if (htmlContent == null)
            {
                System.Diagnostics.Debug.WriteLine("[Cachd MHTML] No HTML content found");
                return content;
            }

            htmlContent = Regex.Replace(htmlContent, "=\r?\n", "");
            System.Diagnostics.Debug.WriteLine("[Cachd MHTML] Removed soft line breaks from HTML");

            System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] HTML content length: {htmlContent.Length}");
            System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Resources count: {resources.Count}");

            var cidMatches = Regex.Matches(htmlContent, @"cid:[^""'\)]+");
            System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Found {cidMatches.Count} CID references in HTML");
            foreach (Match match in cidMatches)
            {
                System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] HTML CID reference: {match.Value}");
            }

            foreach (var resource in resources)
            {
                var url = resource.Key;
                var dataUri = resource.Value;

                System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Inlining resource: {url} -> {dataUri.Substring(0, Math.Min(50, dataUri.Length))}...");

                var originalLength = htmlContent.Length;
                htmlContent = htmlContent.Replace(url, dataUri);
                if (htmlContent.Length != originalLength)
                {
                    System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Replaced {url} successfully");
                }
                
                var urlWithoutProtocol = url.Replace("http://", "").Replace("https://", "");
                htmlContent = htmlContent.Replace(urlWithoutProtocol, dataUri);

                if (url.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
                {
                    var cid = url.Substring(4);
                    htmlContent = htmlContent.Replace($"cid:{cid}", dataUri);
                    htmlContent = htmlContent.Replace($"url(cid:{cid})", $"url({dataUri})");
                    htmlContent = htmlContent.Replace($"url(\"cid:{cid}\")", $"url(\"{dataUri}\")");
                    htmlContent = htmlContent.Replace($"url('cid:{cid}')", $"url('{dataUri}')");
                }
            }

            System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Parsed and inlined {resources.Count} resources");
            System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Final HTML length: {htmlContent.Length}");
            return htmlContent;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Parse error: {ex.Message}");
            return File.ReadAllText(mhtmlPath, Encoding.UTF8);
        }
    }

    private static Encoding GetTextEncoding(string headers)
    {
        var charsetMatch = Regex.Match(
            headers,
            @"charset\s*=\s*(?:""([^""]+)""|'([^']+)'|([^;\s]+))",
            RegexOptions.IgnoreCase);
        var charset = charsetMatch.Groups[1].Success
            ? charsetMatch.Groups[1].Value
            : charsetMatch.Groups[2].Success
                ? charsetMatch.Groups[2].Value
                : charsetMatch.Groups[3].Value;

        if (string.IsNullOrWhiteSpace(charset))
            return Encoding.UTF8;

        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(charset.Trim());
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    private static byte[] DecodeQuotedPrintableBytes(string input)
    {
        using var output = new MemoryStream(input.Length);
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '=' && i + 2 < input.Length
                && input[i + 1] == '\r' && input[i + 2] == '\n')
            {
                i += 2;
                continue;
            }

            if (input[i] == '=' && i + 1 < input.Length && input[i + 1] == '\n')
            {
                i++;
                continue;
            }

            if (input[i] == '=' && i + 2 < input.Length)
            {
                var hex = input.Substring(i + 1, 2);
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
                {
                    output.WriteByte((byte)value);
                    i += 2;
                }
                else
                {
                    output.WriteByte((byte)'=');
                }
            }
            else
            {
                var literalBytes = Encoding.UTF8.GetBytes(input.Substring(i, 1));
                output.Write(literalBytes, 0, literalBytes.Length);
            }
        }

        return output.ToArray();
    }

    private static string GetMimeTypeFromContentType(string contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return "application/octet-stream";

        var parts = contentType.Split(';');
        var mimeType = parts[0].Trim();

        return mimeType.ToLowerInvariant() switch
        {
            "text/css" => "text/css",
            "text/javascript" => "text/javascript",
            "application/javascript" => "application/javascript",
            "image/png" => "image/png",
            "image/jpeg" => "image/jpeg",
            "image/gif" => "image/gif",
            "image/svg+xml" => "image/svg+xml",
            "font/woff" => "font/woff",
            "font/woff2" => "font/woff2",
            _ => mimeType
        };
    }
}
