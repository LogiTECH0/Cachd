using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Cachd.Browser.Handlers;
using Cachd.Browser.Services;
using Cachd.Core;
using Cachd.Prediction;
using Cachd.Preloading;
using CefSharp;
using CefBrowser = CefSharp.Wpf.ChromiumWebBrowser;

namespace Cachd.Browser;

public partial class MainWindow : Window
{
    private readonly List<BrowserTab> _tabs = [];
    private readonly List<string> _visitHistory = [];
    private readonly List<NavigationEvent> _navigationHistory = [];

    private readonly INextPagePredictor _transitionPredictor = new TransitionPredictor();

    private readonly INextPagePredictor _baselinePredictor = new MostFrequentPredictor();
    private readonly PreloadingManager _preloadingManager = new();
    private readonly CacheManager _cacheManager = new(ResourceProfile.Balanced.MaxCachedPages);
    private ChromiumPreloadExecutor? _preloadExecutor;

    public MainWindow()
    {
        InitializeComponent();
        CheckRequestContext();

        try
        {
            _preloadExecutor = new ChromiumPreloadExecutor();

            _preloadExecutor.PreloadCompleted += url => _cacheManager.MarkPreloaded(url);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Cachd Preloading] Initialization failed: {ex}");
        }

        LoadVisitHistory();
        LoadNavigationHistory();
        AddTab();
    }

    private string GetHomePageUrl()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Pages", "Home", "index.html");

        return new Uri(path).AbsoluteUri;
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

    private void UpdateHomePage(BrowserTab tab)
    {
        if (!IsHomePage(tab.CurrentUrl))
            return;

        var json = JsonSerializer.Serialize(GetMostVisitedSites());

        tab.Browser.ExecuteScriptAsync($"window.renderMostVisited({json});");
    }

    private bool IsHomePage(string? url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return false;

        return uri.IsFile
            && uri.LocalPath.EndsWith(
                Path.Combine("Pages", "Home", "index.html"),
                StringComparison.OrdinalIgnoreCase
            );
    }

    private void AddTab(string? url = null)
    {
        url ??= GetHomePageUrl();
        var browser = new CefBrowser
        {
            Address = url,
            KeyboardHandler = new BrowserKeyboardHandler(),
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

        tab.FrameLoadHandler = (_, e) =>
        {
            if (!e.Frame.IsMain)
                return;

            var loadedUrl = e.Url;

            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    RecordNavigation(tab, loadedUrl);

                    if (IsHomePage(loadedUrl))
                        UpdateHomePage(tab);
                })
            );
        };

        tab.TitleChangedHandler = (_, e) =>
        {
            Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    var title = browser.Title;

                    titleText.Text = string.IsNullOrWhiteSpace(title) ? "Нова вкладка" : title;
                })
            );
        };

        browser.FrameLoadEnd += tab.FrameLoadHandler;
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

        tab.Browser.FrameLoadEnd -= tab.FrameLoadHandler;
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

        UpdateSecurityIcon(tab.CurrentUrl);

        UpdatePredictions(tab);
    }

    private static string GetDisplayTitle(string url)
    {
        if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https")
        {
            return uri.Host;
        }

        return "Нова вкладка";
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
        tab.TitleText.Text = isWebPage ? uri.Host : "Нова вкладка";

        if (GetActiveTab() == tab)
        {
            AddressBox.Text =
                IsHomePage(url) ? string.Empty
                : isWebPage ? url
                : string.Empty;
            UpdateSecurityIcon(url);
            UpdatePredictions(tab);
        }
    }

    private void UpdatePredictions(BrowserTab tab)
    {
        PredictionsList.Items.Clear();

        if (
            !Uri.TryCreate(tab.CurrentUrl, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
        )
        {
            PredictionsList.Items.Add("Відкрий сторінку, щоб почати прогнозування.");

            return;
        }
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
        var predictions = _transitionPredictor.Predict(tab.History, tab.CurrentUrl, 5);

        if (predictions.Count == 0)
        {
            predictions = _baselinePredictor.Predict(tab.History, tab.CurrentUrl, 5);
            PredictionsList.Items.Add("Недостатньо історії для прогнозування.");
            return;
        }
        System.Diagnostics.Debug.WriteLine($"[Cachd Debug] Predictions count: {predictions.Count}");

        ////// ТЕСТИ ////// if (false && _preloadExecutor is not null) ////////// if (_preloadExecutor is not null)
        if (_preloadExecutor is not null)
        {
            var candidate = _preloadingManager.SelectCandidate(
                tab.CurrentUrl,
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
            !Uri.TryCreate(input, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https")
        )
        {
            if (input.Contains(' '))
            {
                var query = Uri.EscapeDataString(input);

                tab.Browser.Address = $"https://www.google.com/search?q={query}";

                return;
            }

            input = $"https://{input}";
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
            tab.Browser.FrameLoadEnd -= tab.FrameLoadHandler;
            tab.Browser.TitleChanged -= tab.TitleChangedHandler;
            tab.Browser.Dispose();
        }
        _preloadExecutor?.Dispose();

        _tabs.Clear();

        base.OnClosed(e);
    }

    private sealed class BrowserTab
    {
        public CefBrowser Browser { get; }

        public TabItem Item { get; }

        public TextBlock TitleText { get; }

        public List<NavigationEvent> History { get; } = [];

        public string? CurrentUrl { get; set; }

        public EventHandler<FrameLoadEndEventArgs> FrameLoadHandler { get; set; } = null!;

        public DependencyPropertyChangedEventHandler TitleChangedHandler { get; set; } = null!;

        public BrowserTab(CefBrowser browser, TabItem item, TextBlock titleText)
        {
            Browser = browser;
            Item = item;
            TitleText = titleText;
        }
    }
}
