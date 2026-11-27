
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CefSharp;
using CefSharp.Wpf;

namespace Cachd.Browser.Services;

public sealed class OfflineSnapshotService
{
    private readonly string _storageDirectory;
    private const string FormStateMarker = "data-cachd-local-form-state=\"v2\"";
    private const string SourceHashMarker = "data-cachd-source-hash=\"";

    public OfflineSnapshotService()
    {
        _storageDirectory = Path.Combine(
            Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData),
            "Cachd",
            "OfflinePages"
        );

        Directory.CreateDirectory(_storageDirectory);
    }

    public async Task<string?> SaveAsync(
        ChromiumWebBrowser browser,
        bool loadCompletionSignaled = false)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Offline] MHTML save - IsInitialized: {browser.IsBrowserInitialized}, IsLoading: {browser.IsLoading}");

        if (!browser.IsBrowserInitialized || (browser.IsLoading && !loadCompletionSignaled))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] MHTML save - Browser not ready");
            return null;
        }

        if (browser.IsLoading && loadCompletionSignaled)
        {
            System.Diagnostics.Debug.WriteLine(
                "[Cachd Offline] Browser still reports loading after the completed-load event; capturing the current document.");
        }

        var cefBrowser = browser.GetBrowser();
        var url = cefBrowser.MainFrame?.Url;

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Offline] MHTML save - URL: {url}");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] MHTML save - Invalid URL");
            return null;
        }

        if (uri.Scheme is not ("http" or "https"))
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] MHTML save - Invalid scheme: {uri.Scheme}");
            return null;
        }

        try
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] MHTML save - Calling DevTools");

            var response = await browser
                .GetDevToolsClient()
                .Page
                .CaptureSnapshotAsync(CefSharp.DevTools.Page.CaptureSnapshotFormat.Mhtml);

            if (!string.Equals(
                    cefBrowser.MainFrame?.Url,
                    uri.AbsoluteUri,
                    StringComparison.OrdinalIgnoreCase))
            {
                System.Diagnostics.Debug.WriteLine(
                    "[Cachd Offline] Snapshot discarded because navigation changed during capture.");
                return null;
            }

            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] MHTML save - Data length: {response.Data?.Length ?? 0}");

            if (string.IsNullOrWhiteSpace(response.Data))
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Offline] MHTML save - Empty response data");
                return null;
            }

            var normalizedUrl = uri.AbsoluteUri;
            var hash = Convert.ToHexString(
                SHA256.HashData(Encoding.UTF8.GetBytes(normalizedUrl))
            );

            var filePath = Path.Combine(
                _storageDirectory,
                $"{hash}.mhtml"
            );

            if (!OfflineSnapshotResolver.IsValidSnapshotArchive(response.Data, normalizedUrl))
                return null;

            var temporaryPath = filePath + ".tmp";
            await File.WriteAllTextAsync(temporaryPath, response.Data, Encoding.UTF8);
            File.Move(temporaryPath, filePath, overwrite: true);

            var htmlPath = await Task.Run(() => EnsureInteractiveHtml(filePath));
            if (htmlPath is not null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Offline] Interactive HTML copy saved: {htmlPath}");
            }
            else
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Offline] Could not extract HTML document from archive: {filePath}");
            }

            var metadataPath = filePath + ".json";
            var metadataTemporaryPath = metadataPath + ".tmp";
            var metadata = new OfflineSnapshotMetadata(normalizedUrl, DateTimeOffset.UtcNow);
            await File.WriteAllTextAsync(
                metadataTemporaryPath,
                JsonSerializer.Serialize(metadata),
                Encoding.UTF8);
            File.Move(metadataTemporaryPath, metadataPath, overwrite: true);

            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] Snapshot saved: {url}"
            );

            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] File: {filePath}"
            );

            return filePath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] Snapshot error: {ex.Message} | {ex.StackTrace}"
            );

            return null;
        }
    }

    private static bool LooksLikeHtmlDocument(string content)
    {
        return content.Contains("<html", StringComparison.OrdinalIgnoreCase)
            && !content.Contains("multipart/related", StringComparison.OrdinalIgnoreCase);
    }

    public static string? EnsureInteractiveHtml(string mhtmlPath)
    {
        try
        {
            if (!File.Exists(mhtmlPath))
                return null;

            var htmlPath = Path.ChangeExtension(mhtmlPath, ".html");
            var sourceHash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(mhtmlPath)));
            if (File.Exists(htmlPath))
            {
                var existingHtml = File.ReadAllText(htmlPath);
                if (existingHtml.Contains(FormStateMarker, StringComparison.Ordinal)
                    && existingHtml.Contains(SourceHashMarker, StringComparison.Ordinal))
                {
                    var hashMatch = Regex.Match(
                        existingHtml,
                        @"data-cachd-source-hash=""([0-9A-F]+)""",
                        RegexOptions.IgnoreCase);
                    if (hashMatch.Success
                        && hashMatch.Groups[1].Value.Equals(sourceHash, StringComparison.OrdinalIgnoreCase))
                        return htmlPath;
                }
            }

            var extractedHtml = MhtmlParser.ExtractHtmlWithResources(mhtmlPath);
            if (!LooksLikeHtmlDocument(extractedHtml))
                return null;

            var snapshotId = Path.GetFileNameWithoutExtension(mhtmlPath);
            var preparedHtml = PrepareInteractiveHtml(extractedHtml, snapshotId, sourceHash);
            var temporaryPath = htmlPath + ".tmp";
            File.WriteAllText(temporaryPath, preparedHtml, Encoding.UTF8);
            File.Move(temporaryPath, htmlPath, overwrite: true);
            return htmlPath;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] Could not prepare interactive HTML for {mhtmlPath}: {ex.Message}");
            return null;
        }
    }

    private static string PrepareInteractiveHtml(string html, string snapshotId, string sourceHash)
    {
        html = Regex.Replace(html, @"<script\b[^>]*>[\s\S]*?</script\s*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<(iframe|object|embed|applet|frame)\b[^>]*>[\s\S]*?</\1\s*>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html, @"<(iframe|object|embed|applet|frame)\b[^>]*/?>", "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html,
            @"\s+on[a-z][\w:-]*\s*=\s*(?:""[^""]*""|'[^']*'|[^\s>]+)",
            "", RegexOptions.IgnoreCase);
        html = Regex.Replace(html,
            @"(?<name>\b(?:href|src|action|formaction)\s*=\s*)(?<q>[""'])(?<value>.*?)\k<q>",
            match => WebUtility.HtmlDecode(match.Groups["value"].Value)
                    .TrimStart()
                    .StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)
                ? match.Groups["name"].Value + match.Groups["q"].Value + "#" + match.Groups["q"].Value
                : match.Value,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var script = FormStateScript
            .Replace("__SNAPSHOT_ID__", snapshotId, StringComparison.Ordinal)
            .Replace("__SOURCE_HASH__", sourceHash, StringComparison.Ordinal);
        var bodyEnd = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return bodyEnd >= 0
            ? html.Insert(bodyEnd, script)
            : html + script;
    }

    private const string FormStateScript = """
        <script data-cachd-local-form-state="v2" data-cachd-source-hash="__SOURCE_HASH__">
        (() => {
          const storageKey = 'cachd-form-state-v1:__SNAPSHOT_ID__';
          const excluded = /password|passwd|passcode|secret|token|auth|csrf|cookie|credit|card|cvv|cvc|ssn|social.?security|one.?time.?code|(^|[^a-z])otp([^a-z]|$)|(^|[^a-z])pin([^a-z]|$)/i;
          const controls = Array.from(document.querySelectorAll('input, textarea, select'));
          const eligible = (el) => {
            const type = (el.type || '').toLowerCase();
            const identity = `${el.name || ''} ${el.id || ''} ${el.autocomplete || ''}`;
            return !excluded.test(identity)
              && !['password', 'hidden', 'file', 'submit', 'button', 'reset', 'image'].includes(type)
              && !el.disabled;
          };
          const save = () => {
            try {
              const state = controls.map((el, index) => {
                if (!eligible(el)) return null;
                if (el instanceof HTMLInputElement && ['checkbox', 'radio'].includes(el.type))
                  return { index, type: el.type, name: el.name, checked: el.checked };
                if (el instanceof HTMLSelectElement && el.multiple)
                  return { index, type: 'select-multiple', name: el.name,
                    values: Array.from(el.selectedOptions, option => option.value) };
                return { index, type: el.type || el.tagName.toLowerCase(), name: el.name,
                  value: String(el.value || '').slice(0, 50000) };
              });
              const json = JSON.stringify(state);
              if (json.length > 900000) throw new Error('Form data is too large');
              localStorage.setItem(storageKey, json);
              message.textContent = 'Form values saved on this device';
            } catch (error) {
              message.textContent = 'Could not save form values';
              console.warn('[Cachd Offline] Form state save failed', error);
            }
          };
          const host = document.createElement('div');
          host.style.cssText = 'all:initial!important;position:fixed!important;right:12px!important;bottom:12px!important;z-index:2147483647!important';
          const shadow = host.attachShadow({ mode: 'closed' });
          shadow.innerHTML = `<style>
            :host { all: initial; }
            .panel { display:flex; align-items:center; gap:10px; max-width:min(520px,calc(100vw - 24px)); box-sizing:border-box; padding:10px 12px; color:#E8E8E8; background:rgba(16,26,32,.97); border:1px solid #34434B; border-radius:12px; box-shadow:0 10px 32px rgba(0,0,0,.38); backdrop-filter:blur(14px); font:13px/1.4 'Segoe UI',Arial,sans-serif; }
            .indicator { width:8px; height:8px; flex:none; border-radius:50%; background:#D9B141; box-shadow:0 0 10px rgba(217,177,65,.35); }
            .message { min-width:0; color:#E8E8E8; overflow-wrap:anywhere; }
            button { flex:none; padding:6px 10px; color:#D9B141; background:#202B32; border:1px solid #34434B; border-radius:8px; font:500 12px 'Segoe UI',Arial,sans-serif; white-space:nowrap; cursor:pointer; transition:background .16s ease,border-color .16s ease; }
            button:hover { background:#26343B; border-color:#D9B141; }
            button:focus-visible { outline:2px solid #D9B141; outline-offset:2px; }
          </style><div class="panel"><span class="indicator" aria-hidden="true"></span><span class="message" role="status">Changes will be saved on this device</span><button type="button">Clear saved values</button></div>`;
          const message = shadow.querySelector('.message');
          const clear = shadow.querySelector('button');
          clear.addEventListener('click', () => {
            try {
              localStorage.removeItem(storageKey);
              for (const el of controls) {
                if (!eligible(el)) continue;
                if (el instanceof HTMLInputElement && ['checkbox', 'radio'].includes(el.type)) el.checked = false;
                else if (el instanceof HTMLSelectElement) el.selectedIndex = -1;
                else el.value = '';
              }
              message.textContent = 'Saved values cleared';
            } catch (error) {
              message.textContent = 'Could not clear saved values';
              console.warn('[Cachd Offline] Form state clear failed', error);
            }
          });

          try {
            const saved = JSON.parse(localStorage.getItem(storageKey) || '[]');
            if (saved.length) message.textContent = 'Restored saved values from this device';
            for (const item of saved) {
              const el = controls[item.index];
              if (!el || !eligible(el) || el.type !== item.type || (el.name || '') !== item.name) continue;
              if (item.type === 'checkbox' || item.type === 'radio') el.checked = !!item.checked;
              else if (item.type === 'select-multiple') {
                const values = new Set(item.values || []);
                for (const option of el.options) option.selected = values.has(option.value);
              } else if (typeof item.value === 'string') el.value = item.value;
            }
          } catch (error) {
            message.textContent = 'Local form storage is unavailable';
            console.warn('[Cachd Offline] Form state restore failed', error);
          }

          document.body.append(host);
          document.addEventListener('input', save, true);
          document.addEventListener('change', save, true);
          document.addEventListener('submit', save, true);
          window.addEventListener('pagehide', save);
        })();
        </script>
        """;
}

