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

            string htmlContent = null;
            var resources = new Dictionary<string, string>();

            foreach (var part in parts)
            {
                if (string.IsNullOrWhiteSpace(part)) continue;

                var headerEnd = part.IndexOf("\r\n\r\n");
                if (headerEnd == -1) headerEnd = part.IndexOf("\n\n");
                if (headerEnd == -1) continue;

                var headers = part.Substring(0, headerEnd);
                var body = part.Substring(headerEnd + (part.IndexOf("\r\n\r\n") != -1 ? 4 : 2));

                var contentTypeMatch = Regex.Match(headers, @"Content-Type:\s*([^\r\n;]+)", RegexOptions.IgnoreCase);
                var contentLocationMatch = Regex.Match(headers, @"Content-Location:\s*([^\r\n]+)", RegexOptions.IgnoreCase);
                var contentIdMatch = Regex.Match(headers, @"Content-ID:\s*<([^>]+)>", RegexOptions.IgnoreCase);
                var contentTransferEncodingMatch = Regex.Match(headers, @"Content-Transfer-Encoding:\s*([^\r\n]+)", RegexOptions.IgnoreCase);

                var contentType = contentTypeMatch.Success ? contentTypeMatch.Groups[1].Value.Trim() : "";
                var contentLocation = contentLocationMatch.Success ? contentLocationMatch.Groups[1].Value.Trim() : "";
                var contentId = contentIdMatch.Success ? contentIdMatch.Groups[1].Value.Trim() : "";
                var encoding = contentTransferEncodingMatch.Success ? contentTransferEncodingMatch.Groups[1].Value.Trim() : "";

                byte[] resourceBytes = null;
                if (encoding.Equals("base64", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        resourceBytes = Convert.FromBase64String(body.Trim());
                        body = Encoding.UTF8.GetString(resourceBytes);
                    }
                    catch
                    {
                        resourceBytes = Encoding.UTF8.GetBytes(body);
                    }
                }
                else if (encoding.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
                {
                    body = DecodeQuotedPrintable(body);
                    resourceBytes = Encoding.UTF8.GetBytes(body);
                }
                else
                {
                    resourceBytes = Encoding.UTF8.GetBytes(body);
                }

                if (contentType.Contains("text/html", StringComparison.OrdinalIgnoreCase))
                {
                    htmlContent = body;
                    if (encoding.Equals("quoted-printable", StringComparison.OrdinalIgnoreCase))
                    {
                        htmlContent = DecodeQuotedPrintable(htmlContent);
                        System.Diagnostics.Debug.WriteLine("[Cachd MHTML] Decoded HTML from quoted-printable");
                    }
                    System.Diagnostics.Debug.WriteLine($"[Cachd MHTML] Found HTML content, length: {body.Length}");
                }
                else if (!string.IsNullOrEmpty(contentLocation) || !string.IsNullOrEmpty(contentId))
                {
                    var mimeType = GetMimeTypeFromContentType(contentType);
                    var dataUri = $"data:{mimeType};base64,{Convert.ToBase64String(resourceBytes ?? Encoding.UTF8.GetBytes(body))}";
                    
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

    private static string DecodeQuotedPrintable(string input)
    {
        var result = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            if (input[i] == '=' && i + 2 < input.Length)
            {
                var hex = input.Substring(i + 1, 2);
                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out var value))
                {
                    result.Append((char)value);
                    i += 2;
                }
                else
                {
                    result.Append(input[i]);
                }
            }
            else if (input[i] == '=' && i + 1 < input.Length && input[i + 1] == '\n')
            {
                i += 1;
            }
            else
            {
                result.Append(input[i]);
            }
        }
        return result.ToString();
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
