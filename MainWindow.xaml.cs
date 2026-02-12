using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using PoE2Overlay.Models;
using PoE2Overlay.Services;

namespace PoE2Overlay;

public partial class MainWindow : Window
{
    private nint _hwnd;
    private bool _isClickThrough = true;
    private readonly ClipboardMonitor _clipboardMonitor = new();
    private TradeApiClient _tradeApi;
    private readonly LeagueService _leagueService = new();
    private AppConfig _config;
    private CancellationTokenSource? _priceCts;
    private readonly DispatcherTimer _autoDismissTimer;
    private int _dismissCountdown;
    private PriceResult? _currentPriceResult;
    private bool _listingsExpanded;

    public MainWindow()
    {
        InitializeComponent();

        _config = ConfigService.Load();
        _tradeApi = new TradeApiClient(_config.PoeSessId);

        if (!string.IsNullOrEmpty(_config.LeagueOverride))
            _leagueService.SetOverride(_config.LeagueOverride);

        _autoDismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _autoDismissTimer.Tick += AutoDismissTimer_Tick;

        SourceInitialized += OnSourceInitialized;
        KeyDown += OnKeyDown;
    }

    // ═══════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        EnableClickThrough();
        _clipboardMonitor.ClipboardChanged += OnClipboardChanged;
        _clipboardMonitor.Start(this);

        await _leagueService.InitializeAsync();
        InitializeSettings();
    }

    private void InitializeSettings()
    {
        // Populate league dropdown
        LeagueComboBox.Items.Clear();
        LeagueComboBox.Items.Add("Auto-detect");
        foreach (var league in _leagueService.AvailableLeagues)
            LeagueComboBox.Items.Add(league);

        LeagueComboBox.SelectedItem = string.IsNullOrEmpty(_config.LeagueOverride)
            ? "Auto-detect"
            : _config.LeagueOverride;

        // Set slider values from config
        AutoDismissSlider.Value = _config.AutoDismissSeconds;
        AutoDismissValueText.Text = _config.AutoDismissSeconds.ToString();

        OpacitySlider.Value = (int)(_config.OverlayOpacity * 100);
        OpacityValueText.Text = ((int)(_config.OverlayOpacity * 100)).ToString();

        // Mask POESESSID — show last 8 chars
        PoeSessIdTextBox.Text = _config.PoeSessId.Length > 8
            ? new string('*', _config.PoeSessId.Length - 8) + _config.PoeSessId[^8..]
            : _config.PoeSessId;
    }

    // ═══════════════════════════════════════════════════
    // CLIPBOARD → ITEM → PRICE
    // ═══════════════════════════════════════════════════

    private void OnClipboardChanged(object? sender, string clipboardText)
    {
        Dispatcher.Invoke(() =>
        {
            var item = ItemParser.Parse(clipboardText);
            ShowItemTooltip(item);
            _ = FetchPriceAsync(item);
        });
    }

    private void ShowItemTooltip(ItemData item)
    {
        _currentPriceResult = null;
        _listingsExpanded = false;

        // Set rarity-based name color
        ItemNameText.Foreground = item.Rarity switch
        {
            ItemRarity.Unique => new SolidColorBrush(Color.FromRgb(0xAF, 0x60, 0x25)),
            ItemRarity.Rare => new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0x77)),
            ItemRarity.Magic => new SolidColorBrush(Color.FromRgb(0x88, 0x88, 0xFF)),
            ItemRarity.Currency => new SolidColorBrush(Color.FromRgb(0xAA, 0x9E, 0x82)),
            _ => (Brush)FindResource("AccentGold")
        };

        ItemNameText.Text = item.Rarity == ItemRarity.Unique || item.Name != item.BaseType
            ? item.Name : item.BaseType;

        ItemBaseText.Text = item.Name != item.BaseType ? item.BaseType : string.Empty;
        ItemBaseText.Visibility = string.IsNullOrEmpty(ItemBaseText.Text)
            ? Visibility.Collapsed : Visibility.Visible;

        var info = new StringBuilder();
        info.Append($"iLvl {item.ItemLevel}");
        info.Append($"  |  {item.Rarity}");
        if (!string.IsNullOrEmpty(item.ItemClass))
            info.Append($"  |  {item.ItemClass}");
        ItemInfoText.Text = info.ToString();

        var stats = new StringBuilder();
        if (item.Armour > 0) stats.AppendLine($"Armour: {item.Armour}");
        if (item.Evasion > 0) stats.AppendLine($"Evasion: {item.Evasion}");
        if (item.EnergyShield > 0) stats.AppendLine($"Energy Shield: {item.EnergyShield}");
        ItemStatsText.Text = stats.ToString().TrimEnd();
        ItemStatsText.Visibility = stats.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        ImplicitModsText.Text = item.ImplicitMods.Count > 0
            ? string.Join("\n", item.ImplicitMods) : string.Empty;
        ImplicitModsText.Visibility = item.ImplicitMods.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        ExplicitModsText.Text = item.ExplicitMods.Count > 0
            ? string.Join("\n", item.ExplicitMods) : string.Empty;
        ExplicitModsText.Visibility = item.ExplicitMods.Count > 0
            ? Visibility.Visible : Visibility.Collapsed;

        // Reset price section
        PriceLoadingText.Visibility = Visibility.Visible;
        PriceText.Visibility = Visibility.Collapsed;
        ListingCountText.Visibility = Visibility.Collapsed;
        PriceErrorText.Visibility = Visibility.Collapsed;
        ActionButtonsPanel.Visibility = Visibility.Collapsed;
        ListingsPanel.Visibility = Visibility.Collapsed;
        ListingsContainer.Children.Clear();

        // Apply opacity
        ItemTooltip.Opacity = _config.OverlayOpacity;

        // Show and make interactive
        ItemTooltip.Visibility = Visibility.Visible;
        DisableClickThrough();
        StartAutoDismissTimer();
    }

    private async Task FetchPriceAsync(ItemData item)
    {
        _priceCts?.Cancel();
        _priceCts = new CancellationTokenSource();
        var ct = _priceCts.Token;

        try
        {
            var league = _leagueService.CurrentLeague;
            var result = await _tradeApi.PriceCheckAsync(item, league, ct);

            if (ct.IsCancellationRequested) return;

            Dispatcher.Invoke(() =>
            {
                PriceLoadingText.Visibility = Visibility.Collapsed;

                if (result == null || result.TotalListings == 0)
                {
                    PriceErrorText.Text = "No listings found";
                    PriceErrorText.Visibility = Visibility.Visible;
                    return;
                }

                _currentPriceResult = result;

                var currencySymbol = FormatCurrency(result.Currency);
                PriceText.Text = result.Min == result.Max
                    ? $"{result.Median:F0} {currencySymbol}"
                    : $"{result.Min:F0} - {result.Max:F0} {currencySymbol}  (median {result.Median:F0})";
                PriceText.Visibility = Visibility.Visible;

                ListingCountText.Text = $"{result.TotalListings} listings  |  {result.League}";
                ListingCountText.Visibility = Visibility.Visible;

                ActionButtonsPanel.Visibility = Visibility.Visible;
            });
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;

            Dispatcher.Invoke(() =>
            {
                PriceLoadingText.Visibility = Visibility.Collapsed;
                PriceErrorText.Text = $"Price check failed: {ex.Message}";
                PriceErrorText.Visibility = Visibility.Visible;
            });
        }
    }

    // ═══════════════════════════════════════════════════
    // EXPANDED LISTINGS
    // ═══════════════════════════════════════════════════

    private void PopulateListings()
    {
        ListingsContainer.Children.Clear();
        if (_currentPriceResult == null) return;

        foreach (var listing in _currentPriceResult.Listings)
        {
            var row = new DockPanel { Margin = new Thickness(8, 3, 8, 3) };

            var priceBlock = new TextBlock
            {
                Text = $"{listing.Price:F0} {FormatCurrency(listing.Currency)}",
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("AccentGreen"),
                Width = 100
            };

            var sellerBlock = new TextBlock
            {
                Text = listing.AccountName,
                FontSize = 11,
                Foreground = (Brush)FindResource("SubText"),
                TextTrimming = TextTrimming.CharacterEllipsis
            };

            var timeBlock = new TextBlock
            {
                Text = listing.TimeAgo,
                FontSize = 10,
                Foreground = (Brush)FindResource("DimText"),
                Width = 60,
                TextAlignment = TextAlignment.Right
            };

            DockPanel.SetDock(priceBlock, Dock.Left);
            DockPanel.SetDock(timeBlock, Dock.Right);
            row.Children.Add(priceBlock);
            row.Children.Add(timeBlock);
            row.Children.Add(sellerBlock);

            ListingsContainer.Children.Add(row);
        }
    }

    // ═══════════════════════════════════════════════════
    // AUTO-DISMISS TIMER
    // ═══════════════════════════════════════════════════

    private void StartAutoDismissTimer()
    {
        _autoDismissTimer.Stop();
        _dismissCountdown = _config.AutoDismissSeconds;
        UpdateDismissHint();
        _autoDismissTimer.Start();
    }

    private void AutoDismissTimer_Tick(object? sender, EventArgs e)
    {
        // Pause timer while listings are expanded
        if (_listingsExpanded) return;

        _dismissCountdown--;
        UpdateDismissHint();

        if (_dismissCountdown <= 0)
        {
            DismissTooltip();
        }
    }

    private void UpdateDismissHint()
    {
        DismissHintText.Text = _listingsExpanded
            ? "Press Escape to close (timer paused)"
            : $"Auto-close in {_dismissCountdown}s  |  Press Escape to close";
    }

    // ═══════════════════════════════════════════════════
    // EVENT HANDLERS
    // ═══════════════════════════════════════════════════

    private void CloseButton_OnClick(object sender, RoutedEventArgs e) => DismissTooltip();

    private void ExpandButton_OnClick(object sender, RoutedEventArgs e)
    {
        _listingsExpanded = !_listingsExpanded;

        if (_listingsExpanded)
        {
            PopulateListings();
            ListingsPanel.Visibility = Visibility.Visible;
            ExpandButton.Content = "Collapse Listings";
            UpdateDismissHint();
        }
        else
        {
            ListingsPanel.Visibility = Visibility.Collapsed;
            ExpandButton.Content = "Expand Listings";
            UpdateDismissHint();
        }
    }

    private void OpenTradeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_currentPriceResult == null || string.IsNullOrEmpty(_currentPriceResult.QueryId))
            return;

        var league = Uri.EscapeDataString(_currentPriceResult.League);
        var url = $"https://www.pathofexile.com/trade2/search/poe2/{league}/{_currentPriceResult.QueryId}";
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }

    private void GearButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (SettingsPanel.Visibility == Visibility.Visible)
        {
            SettingsPanel.Visibility = Visibility.Collapsed;
            if (ItemTooltip.Visibility != Visibility.Visible)
                EnableClickThrough();
        }
        else
        {
            SettingsPanel.Visibility = Visibility.Visible;
            DisableClickThrough();
        }
    }

    private void ExitButton_OnClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void SettingsCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        if (ItemTooltip.Visibility != Visibility.Visible)
            EnableClickThrough();
    }

    private void LeagueComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (LeagueComboBox.SelectedItem is not string selected) return;

        var overrideValue = selected == "Auto-detect" ? string.Empty : selected;
        _leagueService.SetOverride(overrideValue);
        _config.LeagueOverride = overrideValue;
    }

    private void AutoDismissSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        var val = (int)e.NewValue;
        AutoDismissValueText.Text = val.ToString();
        _config.AutoDismissSeconds = val;
    }

    private void OpacitySlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (!IsLoaded) return;
        var val = (int)e.NewValue;
        OpacityValueText.Text = val.ToString();
        _config.OverlayOpacity = val / 100.0;

        if (ItemTooltip.Visibility == Visibility.Visible)
            ItemTooltip.Opacity = _config.OverlayOpacity;
    }

    private void SaveSettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        // Check if POESESSID was changed (not the masked version)
        var inputSessId = PoeSessIdTextBox.Text;
        if (!inputSessId.Contains('*') && inputSessId.Length > 0)
        {
            _config.PoeSessId = inputSessId;
            _tradeApi = new TradeApiClient(_config.PoeSessId);
        }

        ConfigService.Save(_config);

        SettingsStatusText.Text = "Settings saved!";
        SettingsStatusText.Visibility = Visibility.Visible;

        // Hide status after 2 seconds
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        timer.Tick += (_, _) =>
        {
            SettingsStatusText.Visibility = Visibility.Collapsed;
            timer.Stop();
        };
        timer.Start();
    }

    // ═══════════════════════════════════════════════════
    // DISMISS & KEYBOARD
    // ═══════════════════════════════════════════════════

    private void DismissTooltip()
    {
        _autoDismissTimer.Stop();
        _priceCts?.Cancel();
        _listingsExpanded = false;
        ItemTooltip.Visibility = Visibility.Collapsed;

        if (SettingsPanel.Visibility != Visibility.Visible)
            EnableClickThrough();
    }

    private void OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (SettingsPanel.Visibility == Visibility.Visible)
                SettingsPanel.Visibility = Visibility.Collapsed;

            DismissTooltip();
            e.Handled = true;
        }
    }

    // ═══════════════════════════════════════════════════
    // CLICK-THROUGH
    // ═══════════════════════════════════════════════════

    private void EnableClickThrough()
    {
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(
            _hwnd,
            NativeMethods.GWL_EXSTYLE,
            style
            | NativeMethods.WS_EX_TRANSPARENT
            | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_NOACTIVATE);
        _isClickThrough = true;
    }

    private void DisableClickThrough()
    {
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(
            _hwnd,
            NativeMethods.GWL_EXSTYLE,
            (style & ~(nint)(NativeMethods.WS_EX_TRANSPARENT | NativeMethods.WS_EX_NOACTIVATE))
            | NativeMethods.WS_EX_TOOLWINDOW);
        _isClickThrough = false;
        Activate();
        Focus();
    }

    // ═══════════════════════════════════════════════════
    // HELPERS
    // ═══════════════════════════════════════════════════

    private static string FormatCurrency(string currency) => currency switch
    {
        "chaos" => "Chaos",
        "divine" => "Divine",
        "exalted" => "Exalted",
        "chance" => "Chance",
        "alch" => "Alchemy",
        _ => currency
    };

    protected override void OnClosed(EventArgs e)
    {
        _autoDismissTimer.Stop();
        _priceCts?.Cancel();
        _clipboardMonitor.Dispose();
        base.OnClosed(e);
    }
}
