using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    private readonly ClipboardMonitor _clipboardMonitor = new();
    private TradeApiClient _tradeApi;
    private readonly LeagueService _leagueService = new();
    private AppConfig _config;
    private CancellationTokenSource? _priceCts;
    private readonly DispatcherTimer _autoDismissTimer;
    private int _dismissCountdown;
    private PriceResult? _currentPriceResult;
    private bool _listingsExpanded;
    private bool _monitoringActive;
    private bool _compareMode;
    private CharacterApiClient? _characterApi;
    private ImmutableDictionary<EquipmentSlot, ItemData> _equippedItems = ImmutableDictionary<EquipmentSlot, ItemData>.Empty;
    private readonly StatDefinitionService _statDefService = new();
    private ItemScoringService? _scoringService;
    private readonly ProfileManager _profileManager = new();
    private WeightProfile? _activeProfile;
    private EquipmentSlot? _dreamSearchSlot;

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

    // Make this window invisible to UI Automation so it doesn't block
    // Windows Voice Typing and other accessibility tools from detecting text fields
    protected override AutomationPeer? OnCreateAutomationPeer() => null;

    // ═══════════════════════════════════════════════════
    // INITIALIZATION
    // ═══════════════════════════════════════════════════

    private async void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        // Hook WndProc to prevent mouse clicks from activating this window
        var source = HwndSource.FromHwnd(_hwnd);
        source?.AddHook(WndProc);

        // Set WS_EX_TOOLWINDOW + WS_EX_NOACTIVATE so we never steal focus
        var style = NativeMethods.GetWindowLongPtr(_hwnd, NativeMethods.GWL_EXSTYLE);
        NativeMethods.SetWindowLongPtr(
            _hwnd,
            NativeMethods.GWL_EXSTYLE,
            style | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_NOACTIVATE);

        PositionBottomRight();

        _clipboardMonitor.ClipboardChanged += OnClipboardChanged;

        await _leagueService.InitializeAsync();
        InitializeSettings();

        _statDefService.Load();
        _scoringService = new ItemScoringService(_statDefService);
        LoadActiveProfile();
    }

    private void LoadActiveProfile()
    {
        var profileName = _config.ActiveProfile;
        if (string.IsNullOrEmpty(profileName))
            profileName = "Default";
        _activeProfile = _profileManager.Load(profileName);
    }

    private nint WndProc(nint hwnd, int msg, nint wParam, nint lParam, ref bool handled)
    {
        if (msg == NativeMethods.WM_MOUSEACTIVATE)
        {
            handled = true;
            return NativeMethods.MA_NOACTIVATE;
        }
        return nint.Zero;
    }

    private void PositionBottomRight()
    {
        var workArea = SystemParameters.WorkArea;
        Left = workArea.Right - ActualWidth - 20;
        Top = workArea.Bottom - ActualHeight - 20;
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

        AccountNameTextBox.Text = _config.AccountName;
        StatDefsVersionText.Text = $"v{_statDefService.Version} \u2014 Updated: {_statDefService.LastUpdated}";
        InitializeProfileUI();
    }

    // ═══════════════════════════════════════════════════
    // CLIPBOARD → ITEM → PRICE
    // ═══════════════════════════════════════════════════

    private void OnClipboardChanged(object? sender, string clipboardText)
    {
        Dispatcher.Invoke(() =>
        {
            var item = ItemParser.Parse(clipboardText);
            if (_compareMode)
            {
                ShowComparisonTooltip(item);
            }
            else
            {
                ShowItemTooltip(item);
                _ = FetchPriceAsync(item);
            }
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

        // Show tooltip and reposition
        ItemTooltip.Visibility = Visibility.Visible;
        StartAutoDismissTimer();

        // Reposition after layout updates
        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
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

                // Reposition after price info changes size
                Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
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

        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
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
        }
        else
        {
            SettingsPanel.Visibility = Visibility.Visible;
        }

        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
    }

    private void ToggleMonitorButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_monitoringActive)
        {
            _clipboardMonitor.Stop();
            _monitoringActive = false;
            ToggleMonitorButton.Content = "\u25B6";
            ToggleMonitorButton.ToolTip = "Start monitoring";
            StatusText.Text = "Stopped";
            StatusText.Foreground = (Brush)FindResource("DimText");
            DismissTooltip();
        }
        else
        {
            _clipboardMonitor.Start(this);
            _monitoringActive = true;
            ToggleMonitorButton.Content = "\u25A0";
            ToggleMonitorButton.ToolTip = "Stop monitoring";
            StatusText.Text = "Monitoring";
            StatusText.Foreground = (Brush)FindResource("AccentGreen");
        }

        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
    }

    private void ExitButton_OnClick(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void SettingsCloseButton_OnClick(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = Visibility.Collapsed;
        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
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

        _config.AccountName = AccountNameTextBox.Text.Trim();
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
        ListingsPanel.Visibility = Visibility.Collapsed;

        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
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
    // COMPARE MODE
    // ═══════════════════════════════════════════════════

    private async void CompareModeButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_compareMode)
        {
            _compareMode = false;
            CompareModeButton.ToolTip = "Switch to Compare mode";
            StatusText.Text = _monitoringActive ? "Monitoring" : "Stopped";
            StatusText.Foreground = _monitoringActive
                ? (Brush)FindResource("AccentGreen")
                : (Brush)FindResource("DimText");
        }
        else
        {
            if (string.IsNullOrEmpty(_config.AccountName))
            {
                StatusText.Text = "Set account name in settings";
                StatusText.Foreground = (Brush)FindResource("ErrorRed");
                return;
            }

            StatusText.Text = "Fetching gear...";
            StatusText.Foreground = (Brush)FindResource("AccentGold");

            try
            {
                _characterApi = new CharacterApiClient(_config.PoeSessId);
                var characters = await _characterApi.GetCharactersAsync(_config.AccountName);

                if (characters.Count == 0)
                {
                    StatusText.Text = "No characters found";
                    StatusText.Foreground = (Brush)FindResource("ErrorRed");
                    return;
                }

                var league = _leagueService.CurrentLeague;
                var character = characters.FirstOrDefault(c =>
                    c.League.Equals(league, StringComparison.OrdinalIgnoreCase))
                    ?? characters[0];

                _equippedItems = await _characterApi.GetEquippedItemsAsync(_config.AccountName, character.Name);

                _compareMode = true;
                CompareModeButton.ToolTip = "Switch to Price Check mode";
                StatusText.Text = $"Compare: {character.Name} ({_equippedItems.Count} slots)";
                StatusText.Foreground = (Brush)FindResource("AccentGold");
            }
            catch (Exception ex)
            {
                StatusText.Text = $"API error: {ex.Message}";
                StatusText.Foreground = (Brush)FindResource("ErrorRed");
            }
        }

        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
    }

    private void ShowComparisonTooltip(ItemData droppedItem)
    {
        var slot = ItemParser.DetectSlot(droppedItem);
        if (slot == null)
        {
            ShowItemTooltip(droppedItem);
            PriceLoadingText.Visibility = Visibility.Collapsed;
            PriceErrorText.Text = "Cannot determine equipment slot";
            PriceErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (!_equippedItems.TryGetValue(slot.Value, out var equippedItem))
        {
            ShowItemTooltip(droppedItem);
            PriceLoadingText.Visibility = Visibility.Collapsed;
            PriceErrorText.Text = "No equipped item in this slot";
            PriceErrorText.Visibility = Visibility.Visible;
            return;
        }

        if (_scoringService == null || _activeProfile == null)
        {
            ShowItemTooltip(droppedItem);
            PriceLoadingText.Visibility = Visibility.Collapsed;
            PriceErrorText.Text = "Scoring service not ready";
            PriceErrorText.Visibility = Visibility.Visible;
            return;
        }

        var slotKey = slot.Value.ToString();
        ItemData? dreamTarget = null;
        if (_activeProfile.DreamBuild.TryGetValue(slotKey, out var dt))
            dreamTarget = dt;

        var result = _scoringService.Compare(droppedItem, equippedItem, dreamTarget, _activeProfile.Weights);

        ShowItemTooltip(droppedItem);

        PriceLoadingText.Visibility = Visibility.Collapsed;
        ActionButtonsPanel.Visibility = Visibility.Collapsed;

        var verdictSymbol = result.Verdict switch
        {
            ComparisonVerdict.Upgrade => "\u25B2",
            ComparisonVerdict.Downgrade => "\u25BC",
            _ => "\u2501"
        };

        var verdictColor = result.Verdict switch
        {
            ComparisonVerdict.Upgrade => FindResource("AccentGreen") as Brush,
            ComparisonVerdict.Downgrade => FindResource("ErrorRed") as Brush,
            _ => FindResource("AccentGold") as Brush
        };

        var verdictText = result.Verdict switch
        {
            ComparisonVerdict.Upgrade => "UPGRADE",
            ComparisonVerdict.Downgrade => "DOWNGRADE",
            _ => "SIDEGRADE"
        };

        PriceText.Text = $"{verdictSymbol} {verdictText}  Score: {result.DroppedTotalScore:F0} vs {result.EquippedTotalScore:F0}";
        PriceText.Foreground = verdictColor;
        PriceText.Visibility = Visibility.Visible;

        var detailText = string.Empty;
        if (_activeProfile.DisplayMode == CompareDisplayMode.CategoryBreakdown
            || _activeProfile.DisplayMode == CompareDisplayMode.FullStatDiff)
        {
            var lines = result.CategoryScores
                .Where(c => Math.Abs(c.Difference) > 0.1)
                .Select(c =>
                {
                    var sign = c.Difference > 0 ? "+" : "";
                    return $"{c.DisplayName}: {sign}{c.Difference:F0}";
                });
            detailText = string.Join("\n", lines);
        }

        if (_activeProfile.DisplayMode == CompareDisplayMode.FullStatDiff && result.StatDiffs.Count > 0)
        {
            if (detailText.Length > 0)
                detailText += "\n\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500\u2500";

            var diffLines = result.StatDiffs
                .Where(d => !d.IsUnscored)
                .Select(d =>
                {
                    var prefix = d.IsGain ? "+" : "-";
                    return $"{prefix} {d.ModLine}";
                });
            detailText += "\n" + string.Join("\n", diffLines);
        }

        if (result.UnscoredMods.Count > 0)
        {
            detailText += $"\n\n? {result.UnscoredMods.Count} unscored mod(s)";
        }

        ListingCountText.Text = detailText;
        ListingCountText.Foreground = (Brush)FindResource("SubText");
        ListingCountText.Visibility = string.IsNullOrEmpty(detailText) ? Visibility.Collapsed : Visibility.Visible;

        if (result.DreamPercentOfTarget.HasValue)
        {
            PriceErrorText.Text = $"vs Dream: {result.DreamPercentOfTarget:F0}% of target";
            PriceErrorText.Foreground = result.DreamPercentOfTarget >= 100
                ? (Brush)FindResource("AccentGreen")
                : (Brush)FindResource("AccentGold");
            PriceErrorText.Visibility = Visibility.Visible;
        }

        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
    }

    // ═══════════════════════════════════════════════════
    // PROFILE & DREAM BUILD
    // ═══════════════════════════════════════════════════

    private void InitializeProfileUI()
    {
        ArchetypeComboBox.Items.Clear();
        foreach (var archetype in Enum.GetValues<Archetype>())
            ArchetypeComboBox.Items.Add(archetype.ToString());

        DisplayModeComboBox.Items.Clear();
        DisplayModeComboBox.Items.Add("Simple");
        DisplayModeComboBox.Items.Add("Category Breakdown");
        DisplayModeComboBox.Items.Add("Full Stat Diff");

        RefreshProfileList();
        BuildDreamBuildGrid();
    }

    private void RefreshProfileList()
    {
        ProfileComboBox.Items.Clear();
        var profiles = _profileManager.ListProfiles();
        if (profiles.Count == 0)
        {
            _profileManager.CreateFromArchetype("Default", Archetype.Balanced);
            profiles = _profileManager.ListProfiles();
        }
        foreach (var name in profiles)
            ProfileComboBox.Items.Add(name);
        ProfileComboBox.SelectedItem = _config.ActiveProfile;
    }

    private void BuildDreamBuildGrid()
    {
        DreamBuildGrid.Children.Clear();
        foreach (var slot in Enum.GetValues<EquipmentSlot>())
        {
            var slotName = slot.ToString();
            ItemData? item = null;
            var hasTarget = _activeProfile?.DreamBuild.TryGetValue(slotName, out item) == true && item != null;
            var displayText = hasTarget ? $"{slotName}\n{item!.Name}" : slotName;

            var btn = new Button
            {
                Content = displayText,
                Tag = slot,
                Margin = new Thickness(2),
                Padding = new Thickness(4),
                FontSize = 10,
                Cursor = Cursors.Hand,
                Background = hasTarget
                    ? new SolidColorBrush(Color.FromArgb(0x33, 0x44, 0xDD, 0x88))
                    : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
                Foreground = hasTarget
                    ? (Brush)FindResource("AccentGreen")
                    : (Brush)FindResource("SubText"),
                BorderThickness = new Thickness(0)
            };
            btn.Click += DreamSlotButton_OnClick;
            DreamBuildGrid.Children.Add(btn);
        }
    }

    private void DreamSlotButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is EquipmentSlot slot)
        {
            _dreamSearchSlot = slot;
            DreamSearchSlotLabel.Text = $"Set target for: {slot}";
            DreamSearchTextBox.Text = string.Empty;
            DreamSearchResults.Children.Clear();
            DreamSearchPanel.Visibility = Visibility.Visible;
            Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
        }
    }

    private async void DreamSearchButton_OnClick(object sender, RoutedEventArgs e)
    {
        var searchTerm = DreamSearchTextBox.Text.Trim();
        if (string.IsNullOrEmpty(searchTerm) || _dreamSearchSlot == null)
            return;

        DreamSearchResults.Children.Clear();
        DreamSearchResults.Children.Add(new TextBlock
        {
            Text = "Searching...",
            FontSize = 10,
            Foreground = (Brush)FindResource("SubText"),
            FontStyle = FontStyles.Italic
        });

        try
        {
            var league = _leagueService.CurrentLeague;
            var searchItem = new ItemData
            {
                Name = searchTerm,
                Rarity = ItemRarity.Unique,
                BaseType = searchTerm
            };

            var result = await _tradeApi.PriceCheckAsync(searchItem, league);
            DreamSearchResults.Children.Clear();

            if (result == null || result.Listings.Count == 0)
            {
                DreamSearchResults.Children.Add(new TextBlock
                {
                    Text = "No items found",
                    FontSize = 10,
                    Foreground = (Brush)FindResource("ErrorRed")
                });
                return;
            }

            var setBtn = new Button
            {
                Content = $"Set \"{searchTerm}\" as target",
                Style = (Style)FindResource("ActionButton"),
                Tag = searchTerm,
                Margin = new Thickness(0, 4, 0, 0)
            };
            setBtn.Click += (_, _) =>
            {
                if (_activeProfile != null && _dreamSearchSlot != null)
                {
                    var targetItem = new ItemData { Name = searchTerm, BaseType = searchTerm };
                    _activeProfile.DreamBuild[_dreamSearchSlot.Value.ToString()] = targetItem;
                    _profileManager.Save(_activeProfile);
                    BuildDreamBuildGrid();
                    DreamSearchPanel.Visibility = Visibility.Collapsed;
                }
            };
            DreamSearchResults.Children.Add(setBtn);
        }
        catch (Exception ex)
        {
            DreamSearchResults.Children.Clear();
            DreamSearchResults.Children.Add(new TextBlock
            {
                Text = $"Search failed: {ex.Message}",
                FontSize = 10,
                Foreground = (Brush)FindResource("ErrorRed"),
                TextWrapping = TextWrapping.Wrap
            });
        }
    }

    private void DreamClearButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_activeProfile != null && _dreamSearchSlot != null)
        {
            _activeProfile.DreamBuild[_dreamSearchSlot.Value.ToString()] = null;
            _profileManager.Save(_activeProfile);
            BuildDreamBuildGrid();
            DreamSearchPanel.Visibility = Visibility.Collapsed;
            Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
        }
    }

    private void ProfileComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProfileComboBox.SelectedItem is not string selected) return;
        _config.ActiveProfile = selected;
        _activeProfile = _profileManager.Load(selected);
        ArchetypeComboBox.SelectedItem = _activeProfile.Archetype.ToString();
        DisplayModeComboBox.SelectedIndex = (int)_activeProfile.DisplayMode;
        BuildDreamBuildGrid();
    }

    private void ArchetypeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _activeProfile == null) return;
        if (ArchetypeComboBox.SelectedItem is not string selected) return;
        if (!Enum.TryParse<Archetype>(selected, out var archetype)) return;

        _activeProfile.Archetype = archetype;
        _activeProfile.Weights = new Dictionary<string, int>(ProfileManager.GetPresetWeights(archetype));
        _profileManager.Save(_activeProfile);
    }

    private void DisplayModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!IsLoaded || _activeProfile == null) return;
        _activeProfile.DisplayMode = (CompareDisplayMode)DisplayModeComboBox.SelectedIndex;
        _profileManager.Save(_activeProfile);
    }

    private void NewProfileButton_OnClick(object sender, RoutedEventArgs e)
    {
        var name = $"Profile {_profileManager.ListProfiles().Count + 1}";
        _profileManager.CreateFromArchetype(name, Archetype.Balanced);
        _config.ActiveProfile = name;
        RefreshProfileList();
    }

    // ═══════════════════════════════════════════════════
    // STAT DEFINITIONS UPDATE
    // ═══════════════════════════════════════════════════

    private async void UpdateStatDefsButton_OnClick(object sender, RoutedEventArgs e)
    {
        StatDefsVersionText.Text = "Updating...";
        try
        {
            var success = await _statDefService.UpdateFromRemoteAsync();
            if (success)
            {
                _scoringService = new ItemScoringService(_statDefService);
                StatDefsVersionText.Text = $"v{_statDefService.Version} \u2014 Updated: {_statDefService.LastUpdated}";
            }
            else
            {
                StatDefsVersionText.Text = "Update failed \u2014 using cached version";
            }
        }
        catch (Exception ex)
        {
            StatDefsVersionText.Text = $"Update failed: {ex.Message}";
        }
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
