using System.IO;
using System.Text.Json;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using System.Windows.Interop;
using Cachd.Browser.Handlers;
using Cachd.Browser.Services;
using Cachd.Browser.Settings;
using Cachd.Core;
using Cachd.Prediction;
using Cachd.Preloading;
using CefSharp;
using CefSharp.DevTools.Network;
using CefBrowser = CefSharp.Wpf.ChromiumWebBrowser;
using CacheStatus = Cachd.Preloading.CacheStatus;

namespace Cachd.Browser;

public partial class MainWindow : Window
{
    private const int WmGetMinMaxInfo = 0x0024;
    private const uint MonitorDefaultToNearest = 0x00000002;

    private readonly List<BrowserTab> _tabs = [];
    private readonly List<string> _visitHistory = [];
    private readonly List<NavigationEvent> _navigationHistory = [];
    private bool _isOfflineModeEnabled;
    private readonly INextPagePredictor _transitionPredictor = new TransitionPredictor();

    private readonly INextPagePredictor _baselinePredictor = new MostFrequentPredictor();
    private ResourceProfile _currentProfile = ResourceProfile.Balanced;

    private readonly PreloadingManager _preloadingManager = new(ResourceProfile.Balanced);

    private readonly CacheManager _cacheManager = new(ResourceProfile.Balanced);
    private readonly OfflineCacheBridge _offlineCacheBridge;
    private readonly OfflineSnapshotService _offlineSnapshotService;
    private ChromiumPreloadExecutor? _preloadExecutor;

    public MainWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) => AttachMaximizeWorkAreaHook();

        CheckRequestContext();

        _offlineCacheBridge = new OfflineCacheBridge(_cacheManager);
        _offlineSnapshotService = new OfflineSnapshotService();

        ApplyRuntimeSettings(App.CurrentSettings);
        try
        {
            _preloadExecutor = new ChromiumPreloadExecutor(
                maxPreloadedUrls: 100,
                maxConcurrentPreloads: _currentProfile.MaxConcurrentPreloads
            );

            _preloadExecutor.PreloadCompleted += url => _cacheManager.MarkPreloaded(url);

            _preloadExecutor.PreloadFinished += url => _preloadingManager.CompletePreload(url);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Initialization failed: {ex}");
        }

        App.SettingsChanged += OnSettingsChanged;

        LoadVisitHistory();
        LoadNavigationHistory();
        Strings.Language = App.CurrentSettings.Language;
        ApplyIntelligenceText();

        AddTab();
    }

    private void AttachMaximizeWorkAreaHook()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        HwndSource.FromHwnd(hwnd)?.AddHook(HandleMaximizeWorkArea);
    }

    private static IntPtr HandleMaximizeWorkArea(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message != WmGetMinMaxInfo || lParam == IntPtr.Zero)
            return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero)
            return IntPtr.Zero;

        var monitorInfo = new MonitorInfo
        {
            Size = Marshal.SizeOf<MonitorInfo>()
        };
        if (!GetMonitorInfo(monitor, ref monitorInfo))
            return IntPtr.Zero;

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        info.MaxPosition.X = monitorInfo.Work.Left - monitorInfo.Monitor.Left;
        info.MaxPosition.Y = monitorInfo.Work.Top - monitorInfo.Monitor.Top;
        info.MaxSize.X = monitorInfo.Work.Right - monitorInfo.Work.Left;
        info.MaxSize.Y = monitorInfo.Work.Bottom - monitorInfo.Work.Top;
        info.MaxTrackSize = info.MaxSize;
        Marshal.StructureToPtr(info, lParam, false);
        handled = true;
        return IntPtr.Zero;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);

    [DllImport("user32.dll", CharSet = CharSet.Auto)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);

    [StructLayout(LayoutKind.Sequential)]
    private struct Point
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public Point Reserved;
        public Point MaxSize;
        public Point MaxPosition;
        public Point MinTrackSize;
        public Point MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MonitorInfo
    {
        public int Size;
        public Rect Monitor;
        public Rect Work;
        public uint Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private async Task ApplyNetworkModeAsync(CefBrowser browser, bool offline)
    {
        if (!browser.IsBrowserInitialized)
            return;

        try
        {
            var devTools = browser.GetDevToolsClient();

            var response = await devTools.ExecuteDevToolsMethodAsync(
                "Network.emulateNetworkConditions",
                new Dictionary<string, object>
                {
                    ["offline"] = offline,
                    ["latency"] = 0,
                    ["downloadThroughput"] = -1,
                    ["uploadThroughput"] = -1,
                }
            );

            Console.WriteLine(
                $"[Cachd Offline] Mode: {(offline ? "ON" : "OFF")}, Success: {response.Success}"
            );
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Cachd Offline] Error: {ex}");
        }
    }

    private async void OfflineModeToggle_Click(object sender, RoutedEventArgs e)
    {
        _isOfflineModeEnabled = !_isOfflineModeEnabled;

        OfflineModeToggleButton.Content = _isOfflineModeEnabled ? "Offline: ON" : "Offline: OFF";

        OfflineModeToggleButton.Foreground = _isOfflineModeEnabled
            ? new SolidColorBrush(Color.FromRgb(217, 177, 65))
            : new SolidColorBrush(Color.FromRgb(170, 180, 186));

        OfflineModeToggleButton.BorderBrush = _isOfflineModeEnabled
            ? new SolidColorBrush(Color.FromRgb(217, 177, 65))
            : new SolidColorBrush(Color.FromRgb(52, 67, 75));

        _preloadExecutor?.SetPaused(_isOfflineModeEnabled);

        foreach (var tab in _tabs)
        {
            await ApplyNetworkModeAsync(tab.Browser, _isOfflineModeEnabled);
        }
    }

    private void OnLoadFailed(CefBrowser failedBrowser, string failedUrl)
    {
        Dispatcher.BeginInvoke(() =>
        {
            var tab = _tabs.FirstOrDefault(t => ReferenceEquals(t.Browser, failedBrowser));

            if (tab == null)
                return;

            if (!Uri.TryCreate(failedUrl, UriKind.Absolute, out var uri))
                return;

            if (uri.Scheme is not ("http" or "https"))
                return;

            var snapshotUrl = _offlineCacheBridge.GetSnapshotFileUrl(uri.AbsoluteUri);
            if (snapshotUrl is not null)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Offline] Opening saved snapshot for {uri.AbsoluteUri}: {snapshotUrl}");
                tab.Browser.Address = snapshotUrl;
                return;
            }

            var cachedPage = _cacheManager.GetPage(failedUrl);

            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Offline] Load failed: {failedUrl} | Cached: {cachedPage?.Status}"
            );

            tab.Browser.Address = $"cachd://offline?url={Uri.EscapeDataString(failedUrl)}";
        });
    }

    private string GetHomePageUrl()
    {
        return "cachd://home";
    }

    private string GetStartupPageUrl()
    {
        return App.CurrentSettings.StartupPage switch
        {
            "blank" => "about:blank",
            _ => GetHomePageUrl(),
        };
    }

    private static string HistoryFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cachd",
            "history.json"
        );
    private static string NavigationHistoryFilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Cachd",
            "navigation-history.json"
        );

    private void LoadNavigationHistory()
    {
        try
        {
            if (!File.Exists(NavigationHistoryFilePath))
                return;

            var history = JsonSerializer.Deserialize<List<NavigationEvent>>(
                File.ReadAllText(NavigationHistoryFilePath)
            );

            if (history != null)
                _navigationHistory.AddRange(history);

            System.Diagnostics.Debug.WriteLine(
                $"[Cachd History] Loaded: {_navigationHistory.Count} navigation events"
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd History] Loading failed: {ex}");
        }
    }

    private void SaveNavigationHistory()
    {
        try
        {
            var directory = Path.GetDirectoryName(NavigationHistoryFilePath)!;
            Directory.CreateDirectory(directory);

            File.WriteAllText(
                NavigationHistoryFilePath,
                JsonSerializer.Serialize(_navigationHistory)
            );
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd History] Saving failed: {ex}");
        }
    }

    private void LoadVisitHistory()
    {
        try
        {
            if (!File.Exists(HistoryFilePath))
                return;

            var history = JsonSerializer.Deserialize<List<string>>(
                File.ReadAllText(HistoryFilePath)
            );

            if (history != null)
                _visitHistory.AddRange(history);
        }
        catch
        {
            _visitHistory.Clear();
        }
    }

    private void SaveVisitHistory()
    {
        try
        {
            var directory = Path.GetDirectoryName(HistoryFilePath)!;
            Directory.CreateDirectory(directory);

            File.WriteAllText(HistoryFilePath, JsonSerializer.Serialize(_visitHistory));
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex.Message);
        }
    }

    private object GetMostVisitedSites()
    {
        return _visitHistory
            .Where(url =>
                Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
            )
            .GroupBy(
                url =>
                {
                    var host = new Uri(url).Host;

                    return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                        ? host[4..]
                        : host;
                },
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group =>
            {
                var host = group.Key;
                var uri = new Uri(group.First());

                var title = host switch
                {
                    "github.com" => "GitHub",
                    "youtube.com" => "YouTube",
                    "spotify.com" => "Spotify",
                    "google.com" => "Google",
                    "vercel.com" => "Vercel",
                    "wikipedia.org" => "Wikipedia",
                    "chatgpt.com" => "ChatGPT",
                    "reddit.com" => "Reddit",
                    _ => host,
                };

                return new
                {
                    url = uri.GetLeftPart(UriPartial.Authority) + "/",
                    title,
                    favicon = $"https://www.google.com/s2/favicons?domain={Uri.EscapeDataString(host)}&sz=64",
                    visits = group.Count(),
                };
            })
            .OrderByDescending(site => site.visits)
            .ThenBy(site => site.title)
            .Take(4)
            .ToList();
    }

    private static ResourceProfile CreateResourceProfile(CachdSettings settings)
    {
        var baseProfile = settings.Mode switch
        {
            "low" => ResourceProfile.Low,
            "performance" => ResourceProfile.Performance,
            _ => ResourceProfile.Balanced,
        };

        if (settings.Mode != "custom")
            return baseProfile;

        return ResourceProfile.Custom(
            baseProfile.MaxPreloadedPages,
            baseProfile.MaxCachedPages,
            settings.MaxConcurrentPreloads,
            baseProfile.CacheExpiration
        );
    }

    private void ApplyRuntimeSettings(CachdSettings settings)
    {
        ApplyTheme(settings.Theme);
        Strings.Language = settings.Language;
        _currentProfile = CreateResourceProfile(settings);

        _preloadingManager.SetProfile(_currentProfile);
        _cacheManager.SetProfile(_currentProfile);

        _preloadingManager.SetEnabled(settings.PreloadingEnabled);

        if (!settings.PreloadingEnabled)
        {
            _preloadExecutor?.ClearPending();
        }

        System.Diagnostics.Debug.WriteLine($"[Cachd Settings] Mode: {settings.Mode}");

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Settings] Preloading: {settings.PreloadingEnabled}"
        );

        System.Diagnostics.Debug.WriteLine(
            $"[Cachd Settings] Parallel preloads: {_currentProfile.MaxConcurrentPreloads}"
        );
    }

    private void ApplyTheme(string theme)
    {
        var isLight = theme == "light";
        var palette = isLight
            ? new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#F4F6F7",
                ["ToolbarBackgroundBrush"] = "#FFFFFF",
                ["TabStripBackgroundBrush"] = "#E9EEF0",
                ["ControlBackgroundBrush"] = "#EDF1F2",
                ["ControlHoverBrush"] = "#E1E8EB",
                ["ControlBorderBrush"] = "#D5DEE2",
                ["SelectedTabBrush"] = "#FFFFFF",
                ["PanelCardBrush"] = "#F7F9FA",
                ["PanelCardBorderBrush"] = "#E1E7EA",
                ["PanelCardHoverBorderBrush"] = "#C29A32",
                ["PrimaryTextBrush"] = "#172126",
                ["SecondaryTextBrush"] = "#66747B",
                ["AccentBrush"] = "#D9B141",
                ["PrimaryActionTextBrush"] = "#172126"
            }
            : new Dictionary<string, string>
            {
                ["WindowBackgroundBrush"] = "#051014",
                ["ToolbarBackgroundBrush"] = "#101A20",
                ["TabStripBackgroundBrush"] = "#0B151A",
                ["ControlBackgroundBrush"] = "#202B32",
                ["ControlHoverBrush"] = "#34434B",
                ["ControlBorderBrush"] = "#34434B",
                ["SelectedTabBrush"] = "#26333B",
                ["PanelCardBrush"] = "#16232A",
                ["PanelCardBorderBrush"] = "#263840",
                ["PanelCardHoverBorderBrush"] = "#6C5B32",
                ["PrimaryTextBrush"] = "#E8E8E8",
                ["SecondaryTextBrush"] = "#AAB4BA",
                ["AccentBrush"] = "#D9B141",
                ["PrimaryActionTextBrush"] = "#051014"
            };

        foreach (var (key, hexColor) in palette)
        {
            Resources[key] = new SolidColorBrush(
                (Color)ColorConverter.ConvertFromString(hexColor)!);
        }

        foreach (var tab in _tabs)
        {
            if (IsHomePage(tab.CurrentUrl))
            {
                var themeName = isLight ? "light" : "dark";
                tab.Browser.ExecuteScriptAsync(
                    $"document.documentElement.dataset.theme = '{themeName}';");
            }
            else
            {
                ApplyUnstyledPageTheme(tab, tab.CurrentUrl, isLight);
            }
        }
    }

    private static void ApplyUnstyledPageTheme(BrowserTab tab, string? url, bool isLight)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https"))
            return;

        const string styleId = "cachd-unstyled-dark-theme";
        var lightThemeScript = $"document.getElementById('{styleId}')?.remove();";
        var darkThemeScript = $$"""
            (() => {
              const styleId = '{{styleId}}';
              const existing = document.getElementById(styleId);
              const hasAuthorStyles = [...document.styleSheets].some((sheet) => {
                try { return sheet.cssRules.length > 0; }
                catch { return true; }
              }) || document.querySelector('[style]') !== null;

              if (hasAuthorStyles) {
                existing?.remove();
                return;
              }
              if (existing || !document.body) return;

              const style = document.createElement('style');
              style.id = styleId;
              style.textContent = `
                :root { color-scheme: dark !important; }
                html, body { background: #051014 !important; color: #f1f1f1 !important; }
                body * { color: #f1f1f1 !important; }
                a, a * { color: #8ab4ff !important; }
                input, button, select, textarea { background: #101a20 !important; }
              `;
              (document.head || document.documentElement).appendChild(style);
            })();
            """;

        tab.Browser.ExecuteScriptAsync(isLight ? lightThemeScript : darkThemeScript);
    }

    private void OnSettingsChanged(CachdSettings settings)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyRuntimeSettings(settings);
                ApplyIntelligenceText();
            }));

            return;
        }
        foreach (var tab in _tabs)
            UpdateTabTitle(tab);
        ApplyRuntimeSettings(settings);
        ApplyIntelligenceText();
    }

    private void UpdateHomePage(BrowserTab tab)
    {
        if (!IsHomePage(tab.CurrentUrl))
            return;

        var json = JsonSerializer.Serialize(GetMostVisitedSites());

        tab.Browser.ExecuteScriptAsync($"window.renderMostVisited({json});");
    }

    private bool IsHomePage(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && uri.Scheme.Equals("cachd", StringComparison.OrdinalIgnoreCase)
            && uri.Host.Equals("home", StringComparison.OrdinalIgnoreCase);
    }

    private void OpenLinkInNewTab(string url)
    {
        Dispatcher.BeginInvoke(new Action(() => AddTab(url)));
    }

    private void AddTab(string? url = null)
    {
        url ??= GetStartupPageUrl();
        var browser = new CefBrowser
        {
            KeyboardHandler = new BrowserKeyboardHandler()
        };

        var requestHandler = new CachdRequestHandler(OnLoadFailed, OpenLinkInNewTab);
        requestHandler.Attach(browser);
        browser.LifeSpanHandler = new CachdLifeSpanHandler(OpenLinkInNewTab);

        browser.JavascriptObjectRepository.Register("cachdSystem", new CachdSystemBridge());
        browser.JavascriptObjectRepository.Register("cachdOffline", _offlineCacheBridge);
        browser.Address = url;
        browser.IsBrowserInitializedChanged += (_, _) =>
        {
            if (browser.IsBrowserInitialized)
            {
                var context = browser.GetBrowser().GetHost().RequestContext;

                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Cache] Tab shared context: {context.IsGlobal}"
                );

                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Cache] Tab cache path: {context.CachePath}"
                );

                _ = ApplyNetworkModeAsync(browser, _isOfflineModeEnabled);
            }
        };

        var tabItem = new TabItem();

        var titleText = new TextBlock
        {
            Text = GetDisplayTitle(url),
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 130,
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 12,
        };

        var closeButton = new Button
        {
            Content = "×",
            Width = 20,
            Height = 20,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(0),
            Background = Brushes.Transparent,
            Foreground = new SolidColorBrush(Color.FromRgb(170, 180, 186)),
            BorderThickness = new Thickness(0),
            FontSize = 15,
            Cursor = Cursors.Hand,
            Focusable = false,
        };

        var header = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
        };

        header.Children.Add(
            new TextBlock
            {
                Text = "◉",
                Foreground = new SolidColorBrush(Color.FromRgb(217, 177, 65)),
                Margin = new Thickness(0, 0, 7, 0),
                VerticalAlignment = VerticalAlignment.Center,
            }
        );

        header.Children.Add(titleText);
        header.Children.Add(closeButton);

        tabItem.Header = header;

        var tab = new BrowserTab(browser, tabItem, titleText);
        tab.History.AddRange(_navigationHistory);

        closeButton.Click += (_, _) => CloseTab(tab);

        tab.FrameLoadHandler = async (_, e) =>
        {
            if (!e.Frame.IsMain)
                return;

            var loadedUrl = e.Url;
            var httpStatusCode = e.HttpStatusCode;
            tab.MarkMainFrameLoadEnded(loadedUrl, httpStatusCode);

            await Dispatcher.BeginInvoke(new Action(() =>
            {
                if (Uri.TryCreate(loadedUrl, UriKind.Absolute, out var loadedUri)
                    && loadedUri.Scheme is "http" or "https"
                    && httpStatusCode <= 0)
                    return;

                RecordNavigation(tab, loadedUrl);

                if (IsHomePage(loadedUrl))
                    UpdateHomePage(tab);
                else
                    ApplyUnstyledPageTheme(tab, loadedUrl, App.CurrentSettings.Theme == "light");
            })).Task;

        };

        tab.FrameLoadStartHandler = (_, e) =>
        {
            if (e.Frame.IsMain)
                tab.MarkMainFrameLoadStarted(e.Url);
        };

        tab.LoadingStateChangedHandler = (_, e) =>
        {
            var isLoading = e.IsLoading;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (GetActiveTab() == tab)
                {
                    PageLoadingIndicator.Visibility = isLoading
                        ? Visibility.Visible
                        : Visibility.Collapsed;
                }
            }));

            if (e.IsLoading)
                return;

            var completedUrl = browser.GetBrowser()?.MainFrame?.Url;
            Dispatcher.InvokeAsync(new Action(async () =>
            {
                if (!_tabs.Contains(tab)
                    || tab.SnapshotSaveInProgress
                    || !Uri.TryCreate(completedUrl, UriKind.Absolute, out var completedUri)
                    || completedUri.Scheme is not ("http" or "https")
                    || !tab.IsSuccessfulMainFrameLoad(completedUri.AbsoluteUri))
                    return;

                tab.SnapshotSaveInProgress = true;
                try
                {
                    if (!string.Equals(
                            tab.Browser.GetBrowser()?.MainFrame?.Url,
                            completedUri.AbsoluteUri,
                            StringComparison.OrdinalIgnoreCase))
                        return;

                    var savedPath = await _offlineSnapshotService.SaveAsync(
                        tab.Browser,
                        loadCompletionSignaled: true);
                    System.Diagnostics.Debug.WriteLine(savedPath is null
                        ? $"[Cachd Offline] Snapshot unavailable for {completedUri}"
                        : $"[Cachd Offline] Snapshot saved: {savedPath}");
                }
                finally
                {
                    tab.SnapshotSaveInProgress = false;
                }
            }), DispatcherPriority.Normal);
        };

        tab.TitleChangedHandler = (_, _) =>
            Dispatcher.BeginInvoke(new Action(() => UpdateTabTitle(tab)));

        browser.FrameLoadStart += tab.FrameLoadStartHandler;
        browser.FrameLoadEnd += tab.FrameLoadHandler;
        browser.LoadingStateChanged += tab.LoadingStateChangedHandler;
        browser.TitleChanged += tab.TitleChangedHandler;

        _tabs.Add(tab);
        BrowserTabs.Items.Add(tabItem);

        BrowserTabs.SelectedItem = tabItem;

        UpdateActiveTab();
    }

    private void CloseTab(BrowserTab tab)
    {
        if (!_tabs.Contains(tab))
            return;

        if (BrowserHost.Content == tab.Browser)
        {
            BrowserHost.Content = null;
        }

        tab.Browser.FrameLoadStart -= tab.FrameLoadStartHandler;
        tab.Browser.FrameLoadEnd -= tab.FrameLoadHandler;
        tab.Browser.LoadingStateChanged -= tab.LoadingStateChangedHandler;
        tab.Browser.TitleChanged -= tab.TitleChangedHandler;

        BrowserTabs.Items.Remove(tab.Item);
        _tabs.Remove(tab);

        tab.Browser.Dispose();

        if (_tabs.Count == 0)
        {
            AddTab();
        }

        UpdateActiveTab();
    }

    private BrowserTab? GetActiveTab()
    {
        return _tabs.FirstOrDefault(tab => tab.Item == BrowserTabs.SelectedItem);
    }

    private void BrowserTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender != BrowserTabs)
            return;

        UpdateActiveTab();
    }

    private void UpdateActiveTab()
    {
        var tab = GetActiveTab();

        if (tab == null)
            return;

        BrowserHost.Content = tab.Browser;

        AddressBox.Text = IsHomePage(tab.CurrentUrl)
            ? string.Empty
            : tab.CurrentUrl ?? string.Empty;
        PageLoadingIndicator.Visibility = tab.Browser.IsLoading
            ? Visibility.Visible
            : Visibility.Collapsed;

        UpdateSecurityIcon(tab.CurrentUrl);

        UpdatePredictions(tab);
    }

    private void UpdateTabTitle(BrowserTab tab)
    {
        if (!_tabs.Contains(tab))
            return;

        tab.TitleText.Text = GetTabTitle(tab);
    }

    private string GetTabTitle(BrowserTab tab)
    {
        var url = tab.CurrentUrl ?? tab.Browser.Address;

        if (IsHomePage(url))
            return Strings.Get("tab.new");

        var title = tab.Browser.Title?.Replace(" — Cachd", "").Trim();

        if (!string.IsNullOrWhiteSpace(title) && !title.Contains("://"))
            return title;

        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
            return uri.Host;

        return Strings.Get("tab.new");
    }

    private static string GetDisplayTitle(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return uri.Host;
        }

        return Strings.Get("tab.new");
    }

    private void NewTab_Click(object sender, RoutedEventArgs e)
    {
        AddTab();
    }

    private void RecordNavigation(BrowserTab tab, string url)
    {
        if (!_tabs.Contains(tab))
            return;

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return;

        if (url == tab.CurrentUrl)
            return;

        bool isWebPage = uri.Scheme is "http" or "https";

        if (isWebPage)
        {
            _cacheManager.MarkVisited(url);
            var navigationEvent = new NavigationEvent(url, DateTimeOffset.Now, tab.CurrentUrl);

            tab.History.Add(navigationEvent);
            _navigationHistory.Add(navigationEvent);

            SaveNavigationHistory();

            _visitHistory.Add(url);
            SaveVisitHistory();
        }

        tab.CurrentUrl = url;
        UpdateTabTitle(tab);

        if (GetActiveTab() == tab)
        {
            AddressBox.Text = IsHomePage(url) ? string.Empty : url;
            UpdateSecurityIcon(url);
            UpdatePredictions(tab);
        }
    }

    private void UpdatePredictions(BrowserTab tab)
    {
        PredictionsList.ItemsSource = null;
        IntelligenceCount.Text = string.Empty;

        var isWebPage = Uri.TryCreate(tab.CurrentUrl, UriKind.Absolute, out var uri)
            && uri.Scheme is "http" or "https";
        foreach (var item in tab.History)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[HISTORY] Previous: {item.PreviousUrl} -> URL: {item.Url}"
            );
        }
        System.Diagnostics.Debug.WriteLine("[Cachd Debug] UpdatePredictions reached!");
        System.Diagnostics.Debug.WriteLine($"[Cachd Debug] Current URL: {tab.CurrentUrl}");
        System.Diagnostics.Debug.WriteLine($"[Cachd Debug] History count: {tab.History.Count}");

        foreach (var item in tab.History)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Debug] History item: {item.PreviousUrl} -> {item.Url}"
            );
        }
        IReadOnlyList<PagePrediction> predictions;
        if (isWebPage)
        {
            predictions = _transitionPredictor.Predict(tab.History, tab.CurrentUrl!, 5);
            if (predictions.Count == 0)
                predictions = _baselinePredictor.Predict(tab.History, tab.CurrentUrl!, 5);
        }
        else
        {
            predictions = GetMostVisitedHostPredictions(tab.History);
        }

        if (predictions.Count == 0)
        {
            SetIntelligenceEmptyState(tab.History.Count == 0
                ? Strings.Get("pred.open")
                : Strings.Get("pred.few"));
        }
        else
        {
            PredictionsList.ItemsSource = predictions
                .Select(CreateIntelligenceSuggestion)
                .ToArray();
            PredictionsList.Visibility = Visibility.Visible;
            IntelligenceEmptyState.Visibility = Visibility.Collapsed;
            IntelligenceCount.Text = predictions.Count.ToString();
        }

        System.Diagnostics.Debug.WriteLine($"[Cachd Debug] Predictions count: {predictions.Count}");

        ////// ТЕСТИ //////
        if (isWebPage && _preloadExecutor is not null)
        {
            var candidate = _preloadingManager.SelectCandidate(
                tab.CurrentUrl!,
                predictions,
                _cacheManager
            );

            if (candidate is not null)
            {
                var prediction = predictions.First(p => p.Url == candidate);

                _cacheManager.AddCandidate(candidate, prediction.Score);

                System.Diagnostics.Debug.WriteLine(
                    $"[Cachd Cache] Candidate registered: {candidate} | Score: {prediction.Score}"
                );

                _preloadExecutor.Preload(candidate);
            }
        }
        /////// ТЕСТИ //////
        foreach (var prediction in predictions)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[Cachd Debug] Prediction: {prediction.Url} | Score: {prediction.Score}"
            );
        }
    }

    private static IReadOnlyList<PagePrediction> GetMostVisitedHostPredictions(
        IReadOnlyList<NavigationEvent> history)
    {
        return history
            .Where(item => Uri.TryCreate(item.Url, UriKind.Absolute, out var pageUri)
                && pageUri.Scheme is "http" or "https")
            .GroupBy(item =>
            {
                var host = new Uri(item.Url).Host;
                return host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
                    ? host[4..]
                    : host;
            }, StringComparer.OrdinalIgnoreCase)
            .Select(group => new PagePrediction(
                group.OrderByDescending(item => item.VisitedAt).First().Url,
                group.Count()))
            .OrderByDescending(prediction => prediction.Score)
            .ThenBy(prediction => prediction.Url, StringComparer.OrdinalIgnoreCase)
            .Take(5)
            .ToArray();
    }

    private void CheckRequestContext()
    {
        var context = Cef.GetGlobalRequestContext();

        System.Diagnostics.Debug.WriteLine("[Cachd Cache] Global context:");

        System.Diagnostics.Debug.WriteLine($"[Cachd Cache] IsGlobal: {context.IsGlobal}");

        System.Diagnostics.Debug.WriteLine($"[Cachd Cache] CachePath: {context.CachePath}");
    }

    private void NavigateToAddress()
    {
        var tab = GetActiveTab();

        if (tab == null)
            return;

        var input = AddressBox.Text.Trim();

        if (string.IsNullOrWhiteSpace(input))
            return;

        if (
            Uri.TryCreate(input, UriKind.Absolute, out var uri)
            && (uri.Scheme == "http" || uri.Scheme == "https" || uri.Scheme == "cachd")
        )
        {
            tab.Browser.Address = input;
            return;
        }

        tab.Browser.Address = input;
    }

    private void GoButton_Click(object sender, RoutedEventArgs e)
    {
        NavigateToAddress();
    }

    private void AddressBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            NavigateToAddress();
            e.Handled = true;
        }
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        var browser = GetActiveTab()?.Browser;

        if (browser?.GetBrowser()?.CanGoBack == true)
        {
            browser.Back();
        }
    }

    private void ForwardButton_Click(object sender, RoutedEventArgs e)
    {
        var browser = GetActiveTab()?.Browser;

        if (browser?.GetBrowser()?.CanGoForward == true)
        {
            browser.Forward();
        }
    }

    private void ReloadButton_Click(object sender, RoutedEventArgs e)
    {
        GetActiveTab()?.Browser.Reload();
    }

    private void UpdateSecurityIcon(string? url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps)
        {
            SecurityIcon.Text = "◈";
            SecurityIcon.Foreground = new SolidColorBrush(Color.FromRgb(40, 200, 64));
        }
        else
        {
            SecurityIcon.Text = "◇";
            SecurityIcon.Foreground = new SolidColorBrush(Color.FromRgb(217, 177, 65));
        }
    }

    private void IntelligenceToggle_Click(object sender, RoutedEventArgs e)
    {
        IntelligencePanel.Visibility =
            IntelligencePanel.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    private void ApplyIntelligenceText()
    {
        IntelligenceTitle.Text = "Your suggestions";
        IntelligenceSubtitle.Text = "Pages you may want to visit next";
        IntelligenceSectionTitle.Text = "UP NEXT";
        IntelligenceTip.Text = "Suggestions improve as you browse.";
        IntelligenceToggleButton.ToolTip = "Your suggestions";
        IntelligenceCloseButton.ToolTip = "Close suggestions";

        if (PredictionsList.Items.Count == 0)
        {
            var activeTab = GetActiveTab();
            var hasWebPage = activeTab is not null
                && Uri.TryCreate(activeTab.CurrentUrl, UriKind.Absolute, out var uri)
                && uri.Scheme is "http" or "https";
            SetIntelligenceEmptyState(hasWebPage
                ? "Browse a few more pages and suggestions will show up here."
                : "Visit a website and helpful suggestions will appear here.");
        }
    }

    private void SetIntelligenceEmptyState(string message)
    {
        IntelligenceEmptyState.Text = message;
        IntelligenceEmptyState.Visibility = Visibility.Visible;
        PredictionsList.Visibility = Visibility.Collapsed;
        IntelligenceCount.Text = string.Empty;
    }

    private IntelligenceSuggestion CreateIntelligenceSuggestion(PagePrediction prediction)
    {
        if (!Uri.TryCreate(prediction.Url, UriKind.Absolute, out var uri))
            return new IntelligenceSuggestion(prediction.Url, string.Empty, prediction.Url);

        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase)
            ? uri.Host[4..]
            : uri.Host;
        var path = uri.AbsolutePath.Trim('/');
        var detail = string.IsNullOrEmpty(path)
            ? "Website page"
            : Uri.UnescapeDataString(path);

        return new IntelligenceSuggestion(host, detail, prediction.Url);
    }

    private void PredictionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (PredictionsList.SelectedItem is not IntelligenceSuggestion suggestion)
            return;

        var tab = GetActiveTab();
        if (tab is not null)
        {
            tab.Browser.Address = suggestion.Url;
            HideIntelligence();
        }

        PredictionsList.SelectedItem = null;
    }

    private void HideIntelligence()
    {
        IntelligencePanel.Visibility = Visibility.Collapsed;
    }

    private void CloseWindow_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void MinimizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeWindow_Click(object sender, RoutedEventArgs e)
    {
        WindowState =
            WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (e.Key == Key.Escape)
        {
            HideIntelligence();
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Alt)
        {
            switch (e.Key)
            {
                case Key.Left:
                    BackButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;

                case Key.Right:
                    ForwardButton_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
            }
        }

        if (e.Key == Key.F5 || (modifiers == ModifierKeys.Control && e.Key == Key.R))
        {
            ReloadButton_Click(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }

        if (modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.T:
                    AddTab();
                    e.Handled = true;
                    break;

                case Key.W:
                    var activeTab = GetActiveTab();

                    if (activeTab != null)
                        CloseTab(activeTab);

                    e.Handled = true;
                    break;

                case Key.Tab:
                    if (_tabs.Count > 1)
                    {
                        var direction = Keyboard.Modifiers.HasFlag(ModifierKeys.Shift) ? -1 : 1;
                        var index = BrowserTabs.SelectedIndex;

                        index = (index + direction + _tabs.Count) % _tabs.Count;
                        BrowserTabs.SelectedIndex = index;
                    }

                    e.Handled = true;
                    break;

                case Key.L:
                    AddressBox.Focus();
                    AddressBox.SelectAll();
                    e.Handled = true;
                    break;
            }
        }
    }

    private void BrowserHost_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers != ModifierKeys.Alt)
            return;

        var browser = GetActiveTab()?.Browser;

        if (browser == null)
            return;

        switch (e.Key)
        {
            case Key.Left:
                if (browser.GetBrowser()?.CanGoBack == true)
                    browser.Back();

                e.Handled = true;
                break;

            case Key.Right:
                if (browser.GetBrowser()?.CanGoForward == true)
                    browser.Forward();

                e.Handled = true;
                break;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        BrowserHost.Content = null;

        foreach (var tab in _tabs)
        {
            tab.Browser.FrameLoadStart -= tab.FrameLoadStartHandler;
            tab.Browser.FrameLoadEnd -= tab.FrameLoadHandler;
            tab.Browser.LoadingStateChanged -= tab.LoadingStateChangedHandler;
            tab.Browser.TitleChanged -= tab.TitleChangedHandler;
            tab.Browser.Dispose();
        }
        _preloadExecutor?.Dispose();
        App.SettingsChanged -= OnSettingsChanged;
        _tabs.Clear();

        base.OnClosed(e);
    }

    private sealed record IntelligenceSuggestion(string Title, string Detail, string Url);

    private sealed class BrowserTab
    {
        public CefBrowser Browser { get; }

        public TabItem Item { get; }

        public TextBlock TitleText { get; }

        public List<NavigationEvent> History { get; } = [];

        public string? CurrentUrl { get; set; }

        private string? _lastMainFrameUrl;
        private int _lastMainFrameHttpStatusCode;

        public EventHandler<FrameLoadStartEventArgs> FrameLoadStartHandler { get; set; } = null!;
        public EventHandler<FrameLoadEndEventArgs> FrameLoadHandler { get; set; } = null!;
        public EventHandler<LoadingStateChangedEventArgs> LoadingStateChangedHandler { get; set; } = null!;
        public bool SnapshotSaveInProgress { get; set; }

        public DependencyPropertyChangedEventHandler TitleChangedHandler { get; set; } = null!;

        public void MarkMainFrameLoadStarted(string url)
        {
            Volatile.Write(ref _lastMainFrameHttpStatusCode, 0);
            Volatile.Write(ref _lastMainFrameUrl, url);
        }

        public void MarkMainFrameLoadEnded(string url, int httpStatusCode)
        {
            Volatile.Write(ref _lastMainFrameUrl, url);
            Volatile.Write(ref _lastMainFrameHttpStatusCode, httpStatusCode);
        }

        public bool IsSuccessfulMainFrameLoad(string url)
        {
            return string.Equals(
                    Volatile.Read(ref _lastMainFrameUrl),
                    url,
                    StringComparison.OrdinalIgnoreCase)
                && Volatile.Read(ref _lastMainFrameHttpStatusCode) is >= 200 and < 400;
        }

        public BrowserTab(CefBrowser browser, TabItem item, TextBlock titleText)
        {
            Browser = browser;
            Item = item;
            TitleText = titleText;
        }
    }
}
