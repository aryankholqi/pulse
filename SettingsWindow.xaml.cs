using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Pulse.Services;
using Forms = System.Windows.Forms;

namespace Pulse;

/// <summary>
/// The window Pulse opens with: every overlay option, and a live preview of the overlay
/// sitting on a game at the real screen's proportions. Changes apply (and save) instantly.
/// </summary>
public partial class SettingsWindow : Window
{
    static readonly string[] Swatches = { "#F6F1E7", "#B39DFF", "#7CC8FF", "#FF9A85", "#8FE3B0", "#FFD166", "#FF6FAE" };
    static readonly string[] LabelSwatches = { OverlayStyle.DefaultLabelColor, "#F6F1E7", "#B39DFF", "#7CC8FF", "#8FE3B0", "#FFD166", "#FF9A85" };

    static readonly Corner[] Corners =
        { Corner.TopLeft, Corner.TopCenter, Corner.TopRight, Corner.BottomLeft, Corner.BottomCenter, Corner.BottomRight };

    const string CustomScene = "custom";

    readonly AppSettings _settings;
    readonly Func<HardwareSnapshot?> _realHardware;
    readonly Func<bool?> _hotspotSupported;
    readonly Func<bool> _overlayRunning;
    readonly Func<IReadOnlyList<(string Process, string Name)>> _knownGames;
    readonly OverlayViewModel _preview = new();
    readonly PreviewFeed _feed = new();
    readonly DispatcherTimer _timer;
    readonly Dictionary<string, ImageSource> _images = new(), _huds = new();
    readonly List<ColorRow> _colorRows = new();
    ColorRow _numberColor = null!, _labelColor = null!;
    bool _editLabels; // the text panel's tab: labels & names, or numbers
    readonly Dictionary<string, RadioButton> _sceneChips = new();

    GameScene _scene;
    bool _loading = true;

    /// <summary>The look the editor changes: the default one (<see cref="_settings"/>) or a game's profile.</summary>
    OverlayStyle _style;
    GameProfile? Editing => _style as GameProfile;
    bool _confirmingDelete;

    /// <summary>Any overlay option changed — apply to the live overlay and save.</summary>
    public event Action? Changed;
    public event Action? LanguageChanged;
    public event Action? LaunchRequested;
    public event Action? QuitRequested;
    /// <summary>"Check now" found a newer release: the app shows the update dialog.</summary>
    public event Action<UpdateInfo>? UpdateFound;
    /// <summary>A saved game summary was clicked: the app opens it.</summary>
    public event Action<GameSession>? SummaryRequested;
    public event Action? LastSummaryRequested;

    public SettingsWindow(AppSettings settings, Func<HardwareSnapshot?> realHardware, Func<bool?> hotspotSupported, Func<bool> overlayRunning,
        Func<IReadOnlyList<(string Process, string Name)>> knownGames)
    {
        _settings = settings;
        _style = settings;
        _realHardware = realHardware;
        _hotspotSupported = hotspotSupported;
        _overlayRunning = overlayRunning;
        _knownGames = knownGames;
        _scene = GameScenes.Find(settings.PreviewScene);

        InitializeComponent();

        PreviewCard.DataContext = _preview;
        TextSample.DataContext = _preview; // same sample numbers, at full size, beside the text controls
        TextStage.SizeChanged += (_, _) =>
        {
            TextStage.Clip = new RectangleGeometry(new Rect(0, 0, TextStage.ActualWidth, TextStage.ActualHeight), 10, 10);
            RefreshSampleTag();
        };
        TextSample.SizeChanged += (_, _) => RefreshSampleTag();
        WireSamplePan();
        Screen.Width = SystemParameters.PrimaryScreenWidth;
        Screen.Height = SystemParameters.PrimaryScreenHeight;
        // corners in screen units: the Viewbox shrinks the whole screen ~3x
        Screen.Clip = new RectangleGeometry(new Rect(0, 0, Screen.Width, Screen.Height), 28, 28);
        var px = Forms.Screen.PrimaryScreen?.Bounds;
        ScreenInfo.Text = px is { } b ? $"{b.Width} × {b.Height}" : "";

        BuildPositions();
        BuildColorRows();
        BuildSceneChips();
        BuildSupporters();
        LoadValues();
        Wire();
        WireProfiles();
        SidebarVersion.Text = "v" + UpdateService.Current;
        NavOverlay.IsChecked = true;
        CollapseButton.Click += (_, _) => SetSidebar(true, animate: true);
        ExpandButton.Click += (_, _) => SetSidebar(false, animate: true);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == System.Windows.Input.Key.B && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
            {
                SetSidebar(!_settings.SidebarCollapsed, animate: true);
                e.Handled = true;
            }
        };
        SettingsGrid.SizeChanged += (_, _) => LayoutSettingsColumns();
        SetSidebar(_settings.SidebarCollapsed, animate: false);

        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += (_, _) => Tick();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { RefreshLaunchText(); Tick(); _timer.Start(); }
            else _timer.Stop();
        };

        ApplyScene();
        RefreshPreview();
        _loading = false;
    }

    // ───────────────────────── build ─────────────────────────

    void BuildPositions()
    {
        foreach (var corner in Corners)
        {
            string name = corner.ToString();
            var slot = new RadioButton
            {
                Style = (Style)FindResource("PositionSlot"),
                GroupName = "position",
                Tag = corner,
                HorizontalContentAlignment = name.EndsWith("Left") ? HorizontalAlignment.Left
                    : name.EndsWith("Right") ? HorizontalAlignment.Right : HorizontalAlignment.Center,
                VerticalContentAlignment = name.StartsWith("Top") ? VerticalAlignment.Top : VerticalAlignment.Bottom,
            };
            slot.SetBinding(ToolTipProperty, new System.Windows.Data.Binding($"[{name}]") { Source = Loc.Instance });
            slot.SetBinding(System.Windows.Automation.AutomationProperties.NameProperty, new System.Windows.Data.Binding($"[{name}]") { Source = Loc.Instance });
            slot.Checked += (_, _) =>
            {
                if (_loading) return;
                _style.Corner = corner;
                RefreshPositionName();
                PlaceMarker();
                Commit();
            };
            PositionGrid.Children.Add(slot);
        }
    }

    void BuildColorRows()
    {
        _colorRows.Add(new ColorRow(this, "FPS", () => _style.FpsColor, v => _style.FpsColor = v, AppSettings.DefaultFpsColor));
        _colorRows.Add(new ColorRow(this, "GPU", () => _style.GpuColor, v => _style.GpuColor = v, AppSettings.DefaultGpuColor));
        _colorRows.Add(new ColorRow(this, "CPU", () => _style.CpuColor, v => _style.CpuColor = v, AppSettings.DefaultCpuColor));
        _colorRows.Add(new ColorRow(this, "RAM", () => _style.RamColor, v => _style.RamColor = v, AppSettings.DefaultRamColor));
        foreach (var row in _colorRows) ColorRows.Children.Add(row.Root);

        _numberColor = new ColorRow(this, "123", () => _style.NumberColor, v => _style.NumberColor = v, OverlayStyle.DefaultNumberColor);
        _labelColor = new ColorRow(this, "Aa", () => _style.LabelColor, v => _style.LabelColor = v, OverlayStyle.DefaultLabelColor, LabelSwatches);
        TextColorRows.Children.Add(_numberColor.Root);
        TextColorRows.Children.Add(_labelColor.Root);
    }

    void BuildSceneChips()
    {
        foreach (var scene in GameScenes.All)
        {
            var chip = new RadioButton
            {
                Style = (Style)FindResource("Chip"),
                GroupName = "scene",
                Content = scene.Title,
                Background = ColorUtil.Solid(scene.Accent),
                ToolTip = scene.Place,
            };
            chip.Checked += (_, _) =>
            {
                if (_loading) return;
                _settings.PreviewScene = scene.Id;
                ApplyScene();
                Save();
            };
            _sceneChips[scene.Id] = chip;
            SceneChips.Children.Add(chip);
        }

        var own = new RadioButton
        {
            Style = (Style)FindResource("Chip"),
            GroupName = "scene",
            Background = (Brush)FindResource("Muted"),
        };
        own.SetBinding(ContentProperty, new System.Windows.Data.Binding("[OwnScreenshot]") { Source = Loc.Instance });
        own.Click += (_, _) => PickScreenshot();
        _sceneChips[CustomScene] = own;
        SceneChips.Children.Add(own);
    }

    /// <summary>One clickable card per channel: avatar, name, @handle → opens their post.</summary>
    void BuildSupporters()
    {
        SupportersPanel.Visibility = Supporters.All.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var s in Supporters.All)
        {
            var avatar = new Border
            {
                Width = 38,
                Height = 38,
                CornerRadius = new CornerRadius(19),
                Background = ColorUtil.Frozen(new LinearGradientBrush(
                    Color.FromRgb(0x3C, 0xB4, 0xEE), Color.FromRgb(0x1D, 0x8C, 0xC8), 90)), // Telegram blue
                Child = new TextBlock
                {
                    Text = s.Initials,
                    Foreground = Brushes.White,
                    FontFamily = new FontFamily("Bahnschrift, Segoe UI"),
                    FontWeight = FontWeights.SemiBold,
                    FontSize = 13,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };

            var text = new StackPanel { Margin = new Thickness(12, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock
            {
                Text = s.Name,
                FontWeight = FontWeights.SemiBold,
                Foreground = (Brush)FindResource("Text"),
                TextTrimming = TextTrimming.CharacterEllipsis,
            });
            text.Children.Add(new TextBlock
            {
                Text = $"@{s.Handle}  ·  Telegram",
                FontSize = 11.5,
                Foreground = (Brush)FindResource("Muted"),
                Margin = new Thickness(0, 1, 0, 0),
                FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Left,
            });

            var arrow = new TextBlock
            {
                Text = ((char)0xE8A7).ToString(), // OpenInNewWindow
                FlowDirection = FlowDirection.LeftToRight, // the glyph must not mirror in Persian
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = (Brush)FindResource("Muted"),
                VerticalAlignment = VerticalAlignment.Center,
            };

            var row = new DockPanel();
            DockPanel.SetDock(avatar, Dock.Left);
            DockPanel.SetDock(arrow, Dock.Right);
            row.Children.Add(avatar);
            row.Children.Add(arrow);
            row.Children.Add(text);

            var card = new Button
            {
                Style = (Style)FindResource("Card"),
                Content = row,
                ToolTip = s.PostUrl,
                Margin = new Thickness(0, SupporterList.Children.Count == 0 ? 0 : 8, 0, 0),
            };
            System.Windows.Automation.AutomationProperties.SetName(card, s.Name);
            card.Click += (_, _) => OpenUrl(s.PostUrl);
            SupporterList.Children.Add(card);
        }
    }

    static void OpenUrl(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* no browser / blocked: nothing useful to do */ }
    }

    void LoadValues()
    {
        (_settings.Language == AppLanguage.Persian ? LangFa : LangEn).IsChecked = true;
        LoadStyleValues();

        ShowOnLaunch.IsChecked = _settings.ShowSettingsOnLaunch;
        ShowSummary.IsChecked = _settings.ShowSessionSummary;
        AutoUpdate.IsChecked = _settings.CheckForUpdates;
        RefreshUpdateCard();
        HudToggle.IsChecked = _settings.PreviewHud;

        string sceneId = _settings.PreviewScene == CustomScene && File.Exists(_settings.PreviewImage) ? CustomScene : _scene.Id;
        _sceneChips[sceneId].IsChecked = true;
    }

    /// <summary>The editor's controls from the look being edited.</summary>
    void LoadStyleValues()
    {
        (_style.Compact ? LayoutCompact : LayoutFull).IsChecked = true;

        foreach (var (box, get, _) in Metrics) box.IsChecked = get();
        FpsBottleneck.IsChecked = _style.ShowBottleneck;
        GpuHotspot.IsChecked = GpuHotspot.IsEnabled && _style.ShowGpuHotspot;
        CompactVram.IsChecked = _style.ShowCompactVram;
        RefreshMetricLocks();

        RefreshPositionSlots();

        foreach (var row in _colorRows) row.Refresh();
        FpsWarnings.IsChecked = _style.FpsWarnings;
        LoadTextValues();

        ScaleSlider.Value = Math.Clamp(_style.Scale, ScaleSlider.Minimum, ScaleSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(_style.BackgroundOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        RefreshSliderLabels();
    }

    /// <summary>Point the editor at another look: the default one, or a game's profile.</summary>
    void EditStyle(OverlayStyle style)
    {
        _style = style;
        bool loading = _loading;
        _loading = true; // the controls follow the look; nothing to save
        LoadStyleValues();
        _loading = loading;
        RefreshPreview();
        if (IsVisible) Tick();
    }

    void Wire()
    {
        LangEn.Checked += (_, _) => SetLanguage(AppLanguage.English);
        LangFa.Checked += (_, _) => SetLanguage(AppLanguage.Persian);

        LayoutCompact.Checked += (_, _) => { _style.Compact = true; Commit(); };
        LayoutFull.Checked += (_, _) => { _style.Compact = false; Commit(); };

        foreach (var (box, _, set) in Metrics)
            OnSwitch(box, on => { set(on); RefreshMetricLocks(); Commit(); });
        OnSwitch(FpsBottleneck, on => { _style.ShowBottleneck = on; Commit(); });
        BottleneckGuideButton.Click += (_, _) => new BottleneckGuideWindow(_style) { Owner = this }.ShowDialog();
        OnSwitch(GpuHotspot, on =>
        {
            if (!GpuHotspot.IsEnabled) return; // shown off for this GPU, not switched off: keep the choice
            _style.ShowGpuHotspot = on;
            Commit();
        });
        OnSwitch(CompactVram, on => { _style.ShowCompactVram = on; Commit(); });

        OnSwitch(FpsWarnings, on => { _style.FpsWarnings = on; Commit(); });
        ResetColors.Click += (_, _) =>
        {
            foreach (var row in _colorRows) row.Set(row.Default);
            _style.FpsWarnings = true;
            FpsWarnings.IsChecked = true;
            Commit();
        };

        TextNumbersTab.Checked += (_, _) => { _editLabels = false; LoadTextValues(); };
        TextLabelsTab.Checked += (_, _) => { _editLabels = true; LoadTextValues(); };
        TextSizeSlider.ValueChanged += (_, e) =>
        {
            double size = Math.Round(e.NewValue, 2);
            if (_editLabels) _style.LabelSize = size; else _style.NumberSize = size;
            RefreshTextSizeLabel();
            Commit();
        };
        OnSwitch(HeatColors, on => { _style.HeatColors = on; Commit(); });
        ResetText.Click += (_, _) =>
        {
            _style.ResetText();
            bool loading = _loading;
            _loading = true;
            LoadTextValues();
            _loading = loading;
            Commit();
        };

        ScaleSlider.ValueChanged += (_, e) => { _style.Scale = Math.Round(e.NewValue, 2); RefreshSliderLabels(); Commit(); };
        OpacitySlider.ValueChanged += (_, e) => { _style.BackgroundOpacity = Math.Round(e.NewValue, 2); RefreshSliderLabels(); Commit(); };

        OnSwitch(ShowOnLaunch, on => { _settings.ShowSettingsOnLaunch = on; Save(); });
        OnSwitch(ShowSummary, on => { _settings.ShowSessionSummary = on; Save(); });
        OnSwitch(AutoUpdate, on => { _settings.CheckForUpdates = on; Save(); RefreshUpdateCard(); });
        UpdateAction.Click += async (_, _) =>
        {
            if (_updateState == UpdateState.Available && _availableUpdate is { } update) UpdateFound?.Invoke(update);
            else await CheckForUpdate();
        };
        WhatsNewLink.Click += (_, _) =>
        {
            if (WhatsNew.For(UpdateService.Current) is { Count: > 0 } notes) new WhatsNewWindow(notes) { Owner = this }.Show();
        };
        SavedList.Open += session => SummaryRequested?.Invoke(session);
        LastSummaryButton.Click += (_, _) => LastSummaryRequested?.Invoke();
        StartWithWindows.Click += async (_, _) =>
        {
            bool on = StartWithWindows.IsChecked == true;
            StartWithWindows.IsEnabled = false;
            bool ok = await Task.Run(() => on ? StartupTask.Enable() : StartupTask.Disable());
            if (!ok) StartWithWindows.IsChecked = !on;
            StartWithWindows.IsEnabled = true;
        };

        OnSwitch(HudToggle, on => { _settings.PreviewHud = on; ApplyScene(); Save(); });

        PreviewCard.SizeChanged += (_, _) => PlaceCard();
        PositionMarkerLayer.SizeChanged += (_, _) => PlaceMarker();
        WireCardDrag();
        OnSwitch(ZoomToggle, _ => UpdateZoom());

        LaunchButton.Click += (_, _) => LaunchRequested?.Invoke();
        QuitButton.Click += (_, _) => QuitRequested?.Invoke();

        Loaded += async (_, _) =>
        {
            Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
            StartWithWindows.IsChecked = await Task.Run(StartupTask.IsEnabled); // schtasks is slow: keep it off the UI thread
            StartWithWindows.IsEnabled = true;
        };
    }

    // Checked/Unchecked, not Click: keyboard, UI Automation and code all land here.
    static void OnSwitch(CheckBox box, Action<bool> changed)
    {
        box.Checked += (_, _) => changed(true);
        box.Unchecked += (_, _) => changed(false);
    }

    // ───────────────────────── behaviour ─────────────────────────

    (CheckBox Box, Func<bool> Get, Action<bool> Set)[] Metrics =>
    [
        (MetricFps, () => _style.ShowFps, v => _style.ShowFps = v),
        (MetricGpu, () => _style.ShowGpu, v => _style.ShowGpu = v),
        (MetricCpu, () => _style.ShowCpu, v => _style.ShowCpu = v),
        (MetricRam, () => _style.ShowRam, v => _style.ShowRam = v),
    ];

    /// <summary>Down to the minimum: the switches still on can't be turned off.</summary>
    void RefreshMetricLocks()
    {
        bool atMin = _style.MetricCount <= OverlayStyle.MinMetrics;
        foreach (var (box, get, _) in Metrics)
        {
            bool locked = atMin && get();
            box.IsEnabled = !locked;
            if (locked) box.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[MetricsLocked]") { Source = Loc.Instance });
            else box.ClearValue(ToolTipProperty);
        }
        FpsExtras.Visibility = _style.ShowFps ? Visibility.Visible : Visibility.Collapsed;
        GpuExtras.Visibility = _style.ShowGpu ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// A GPU without a hot spot sensor: the switch shows off, can't be turned on, and says why.
    /// The saved choice stays as it was, for when Pulse runs on another card.
    /// </summary>
    void RefreshHotspotSupport()
    {
        bool supported = _hotspotSupported() != false; // not known yet (sensors starting): leave it usable
        if (GpuHotspot.IsEnabled == supported) return;

        bool loading = _loading;
        _loading = true;
        GpuHotspot.IsEnabled = supported;
        GpuHotspot.IsChecked = supported && _style.ShowGpuHotspot;
        _loading = loading;

        GpuHotspotNote.SetBinding(System.Windows.Documents.Run.TextProperty,
            new System.Windows.Data.Binding(supported ? "[GpuHotspotHint]" : "[GpuHotspotUnsupported]") { Source = Loc.Instance });
        if (supported) GpuHotspot.ClearValue(ToolTipProperty);
        else
        {
            var why = new TextBlock { TextWrapping = TextWrapping.Wrap, MaxWidth = 320 }; // a sentence or two: wrap it
            why.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("[GpuHotspotUnsupportedTip]") { Source = Loc.Instance });
            GpuHotspot.ToolTip = why;
        }
    }

    // ───────────────────────── updates card ─────────────────────────

    enum UpdateState { Idle, Checking, Current, Available, Failed }

    UpdateState _updateState;
    UpdateInfo? _availableUpdate;

    async Task CheckForUpdate()
    {
        SetUpdateState(UpdateState.Checking);
        try
        {
            var update = await UpdateService.CheckAsync();
            _settings.LastUpdateCheck = DateTime.Now;
            Save();
            _availableUpdate = update;
            SetUpdateState(update is null ? UpdateState.Current : UpdateState.Available);
            if (update != null) UpdateFound?.Invoke(update); // asked for by hand: offer it even if it was skipped
        }
        catch { SetUpdateState(UpdateState.Failed); }
    }

    /// <summary>The app's own background check finished: the card shows what it found.</summary>
    public void ReportUpdate(UpdateInfo? update)
    {
        if (_updateState == UpdateState.Checking) return; // a check by hand is on its way
        if (update != null)
        {
            _availableUpdate = update;
            SetUpdateState(UpdateState.Available);
        }
        else if (_updateState != UpdateState.Available) SetUpdateState(UpdateState.Current);
    }

    void SetUpdateState(UpdateState state)
    {
        _updateState = state;
        RefreshUpdateCard();
    }

    /// <summary>
    /// Tile, version, a status line with a coloured dot, and one button:
    /// check → (checking…) → up to date / update to X / try again.
    /// </summary>
    void RefreshUpdateCard()
    {
        var accent = (Brush)FindResource("Accent");
        var (glyph, hue, dot, status, action, primary) = _updateState switch
        {
            UpdateState.Checking => (0xE895, accent, accent, Loc.T("Checking"), Loc.T("UpdatesChecking"), false),
            UpdateState.Current => (0xE73E, Palette.Cool, Palette.Cool, Stamp("UpdatesCurrent"), Loc.T("CheckNow"), false),
            UpdateState.Available when _availableUpdate is { } u =>
                (0xE896, accent, accent, string.Format(Loc.T("UpdateFound"), u.Version), string.Format(Loc.T("UpdatesInstall"), u.Version), true),
            UpdateState.Failed => (0xE7BA, Palette.Warm, Palette.Warm, Loc.T("CheckFailed"), Loc.T("UpdateRetry"), false),
            _ => (0xE895, accent, (Brush)FindResource("Faint"),
                  _settings.LastUpdateCheck is not null ? Stamp("UpdatesLastChecked")
                      : Loc.T(_settings.CheckForUpdates ? "UpdatesAuto" : "UpdatesManual"),
                  Loc.T("UpdatesCheck"), false),
        };

        UpdateVersion.Text = "Pulse " + UpdateService.Current;
        UpdateGlyph.Text = ((char)glyph).ToString();
        UpdateGlyph.Foreground = hue;
        UpdateTile.Background = Wash(hue);
        UpdateDot.Fill = dot;
        UpdateStatus.Text = status;
        UpdateStatus.Foreground = _updateState == UpdateState.Idle ? (Brush)FindResource("Muted") : (Brush)FindResource("Soft");

        UpdateAction.Content = action;
        UpdateAction.Style = (Style)FindResource(primary ? "Primary" : "Ghost");
        UpdateAction.IsEnabled = _updateState != UpdateState.Checking;

        // a slow turn while checking: a few pixels, cheap even drawn on the CPU
        UpdateSpin.BeginAnimation(RotateTransform.AngleProperty, _updateState == UpdateState.Checking
            ? new DoubleAnimation(0, 360, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever }
            : null);

        WhatsNewLink.Visibility = WhatsNew.For(UpdateService.Current).Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        string Stamp(string key) => _settings.LastUpdateCheck is { } at
            ? Loc.T(key) + "  ·  " + SessionSummaryWindow.When(at)
            : Loc.T(key);
    }

    /// <summary>The hue at low strength: the tile behind the update glyph.</summary>
    static Brush Wash(Brush hue) => hue is SolidColorBrush { Color: var c }
        ? ColorUtil.Solid(Color.FromArgb(0x24, c.R, c.G, c.B))
        : hue;

    /// <summary>Two columns when there's room, one under the other when not.</summary>
    void LayoutSettingsColumns()
    {
        bool one = SettingsGrid.ActualWidth < 820;
        Grid.SetColumn(SettingsSide, one ? 0 : 2);
        Grid.SetRow(SettingsSide, one ? 1 : 0);
        SettingsGrid.ColumnDefinitions[1].Width = new GridLength(one ? 0 : 16);
        SettingsGrid.ColumnDefinitions[2].Width = one ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
    }

    void SetLanguage(AppLanguage language)
    {
        if (_loading && language == _settings.Language) return;
        _settings.Language = language;
        Loc.Instance.Language = language;
        RefreshPositionName();
        RefreshLaunchText();
        RefreshProfilesPage();
        ApplySidebarContent(_settings.SidebarCollapsed);
        RefreshUpdateCard();
        Save();
        RefreshSampleTag();
        LanguageChanged?.Invoke();
    }

    /// <summary>"Show after every game" was flipped in a summary window.</summary>
    public void SyncSummarySwitch() => ShowSummary.IsChecked = _settings.ShowSessionSummary;

    void Commit()
    {
        if (_loading) return;
        RefreshPreview();
        Changed?.Invoke();
    }

    void Save()
    {
        if (!_loading) _settings.Save();
    }

    void RefreshPreview()
    {
        PreviewCard.ApplySettings(_style);
        TextSample.ApplySettings(_style);
        _preview.ApplyStyle(_style);
        PlaceCard();
    }

    void PlaceCard()
    {
        double w = PreviewCard.ActualWidth, h = PreviewCard.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var (left, top) = OverlayWindow.Place(_style, Screen.Width, Screen.Height, w, h);
        Canvas.SetLeft(PreviewCard, left);
        Canvas.SetTop(PreviewCard, top);
        if (!_dragging) UpdateZoom(); // following the card mid-drag would pull the view from under the pointer
    }

    /// <summary>Zoom: look at the overlay up close, the way you'd notice it mid-game.</summary>
    void UpdateZoom()
    {
        const double z = 2.6;
        if (ZoomToggle.IsChecked != true || PreviewCard.ActualWidth <= 0)
        {
            Stage.RenderTransform = Transform.Identity;
            return;
        }
        double sw = Screen.Width, sh = Screen.Height, vw = sw / z, vh = sh / z;
        double cx = Canvas.GetLeft(PreviewCard) + PreviewCard.ActualWidth / 2;
        double cy = Canvas.GetTop(PreviewCard) + PreviewCard.ActualHeight / 2;
        double x0 = Math.Clamp(cx - vw / 2, 0, sw - vw), y0 = Math.Clamp(cy - vh / 2, 0, sh - vh);
        Stage.RenderTransform = new MatrixTransform(z, 0, 0, z, -x0 * z, -y0 * z);
    }

    void Tick()
    {
        RefreshHotspotSupport();
        if (!Editor.IsVisible || _sliding) return; // another page is open, or the sidebar is sliding: nothing to animate
        var (hw, fps, target) = _feed.Next(_scene, _realHardware());
        if (Editing is { } game) target = game.Process + ".exe";
        else if (IsCustomScene) target = "YourGame.exe";
        _preview.Apply(hw, fps, target, null);
    }

    bool IsCustomScene => _settings.PreviewScene == CustomScene;

    void ApplyScene()
    {
        if (IsCustomScene && LoadImage(_settings.PreviewImage) is { } shot)
        {
            SceneImage.Source = shot;
            HudImage.Source = null; // their screenshot already has the game's HUD
            SceneCaption.Text = Path.GetFileName(_settings.PreviewImage);
            _scene = GameScenes.Find(null);
        }
        else
        {
            if (IsCustomScene) _settings.PreviewScene = GameScenes.All[0].Id;
            _scene = GameScenes.Find(_settings.PreviewScene);
            SceneImage.Source = Picture(_scene);
            HudImage.Source = _settings.PreviewHud ? Hud(_scene) : null;
            SceneCaption.Text = $"{_scene.Title}  ·  {_scene.Place}";
        }
        HudToggle.IsEnabled = !IsCustomScene;
        TextStageScene.ImageSource = SceneImage.Source;
        _feed.Reset(_scene);
        if (IsVisible) Tick();
    }

    ImageSource Picture(GameScene scene)
    {
        if (!_images.TryGetValue(scene.Id, out var image))
            _images[scene.Id] = image = scene.RenderImage();
        return image;
    }

    ImageSource Hud(GameScene scene)
    {
        if (!_huds.TryGetValue(scene.Id, out var hud))
            _huds[scene.Id] = hud = scene.RenderHud(Screen.Width, Screen.Height);
        return hud;
    }

    void PickScreenshot()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"{Loc.T("ImageFilter")}|*.png;*.jpg;*.jpeg;*.bmp;*.webp",
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(_settings.PreviewImage) ?? "")
                ? Path.GetDirectoryName(_settings.PreviewImage)
                : Environment.GetFolderPath(Environment.SpecialFolder.MyPictures),
        };

        if (dlg.ShowDialog(this) == true && LoadImage(dlg.FileName) is not null)
        {
            _settings.PreviewImage = dlg.FileName;
            _settings.PreviewScene = CustomScene;
        }
        else if (!IsCustomScene)
        {
            _sceneChips[_scene.Id].IsChecked = true; // cancelled: stay on the game scene
            return;
        }
        ApplyScene();
        Save();
    }

    static BitmapImage? LoadImage(string? path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad; // don't keep the file locked
            bmp.DecodePixelWidth = 2560;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }

    void RefreshPositionName() => PositionName.Text = Loc.T(_style.Corner.ToString());

    // ───────────────────────── anywhere on screen ─────────────────────────


    bool _dragging;
    Point _grab; // where on the card the pointer took hold

    /// <summary>Drag the card in the preview to put the overlay anywhere, right up to the screen edges.</summary>
    void WireCardDrag()
    {
        PreviewCard.Cursor = System.Windows.Input.Cursors.SizeAll;
        PreviewCard.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[PositionDragTip]") { Source = Loc.Instance });

        PreviewCard.MouseLeftButtonDown += (_, e) =>
        {
            var p = e.GetPosition(CardCanvas);
            _grab = new Point(p.X - Canvas.GetLeft(PreviewCard), p.Y - Canvas.GetTop(PreviewCard));
            _dragging = PreviewCard.CaptureMouse();
            PreviewCard.ToolTip = null; // no tooltip trailing the card
            e.Handled = true;
        };
        PreviewCard.MouseMove += (_, e) =>
        {
            if (!_dragging) return;
            var p = e.GetPosition(CardCanvas);
            DragCardTo(p.X - _grab.X, p.Y - _grab.Y);
        };
        PreviewCard.MouseLeftButtonUp += (_, _) => PreviewCard.ReleaseMouseCapture();
        PreviewCard.LostMouseCapture += (_, _) =>
        {
            if (!_dragging) return;
            _dragging = false;
            PreviewCard.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[PositionDragTip]") { Source = Loc.Instance });
            UpdateZoom();
            Commit(); // the live overlay moves, and it's saved, once: when the card is let go
        };
    }

    void DragCardTo(double left, double top)
    {
        double sw = Screen.Width, sh = Screen.Height, w = PreviewCard.ActualWidth, h = PreviewCard.ActualHeight;
        // Free: no pull toward the six spots (those sit inset from the edges; the buttons still pick them).
        // Pushed past an edge it stops right on it, so a corner is reachable flush, with no gap at all.
        left = Math.Clamp(left, 0, Math.Max(0, sw - w));
        top = Math.Clamp(top, 0, Math.Max(0, sh - h));

        _style.Corner = Corner.Custom;
        _style.CustomX = sw > w ? left / (sw - w) : 0;
        _style.CustomY = sh > h ? top / (sh - h) : 0;
        PlaceCard();
        RefreshPositionSlots();
    }

    /// <summary>The six slots show the spot in use; a spot of their own shows as a marker among them.</summary>
    void RefreshPositionSlots()
    {
        bool loading = _loading;
        _loading = true; // checking a slot here must not commit
        foreach (RadioButton slot in PositionGrid.Children)
            slot.IsChecked = (Corner)slot.Tag == _style.Corner;
        _loading = loading;
        RefreshPositionName();
        PlaceMarker();
    }

    void PlaceMarker()
    {
        bool custom = _style.Corner == Corner.Custom;
        PositionMarker.Visibility = custom ? Visibility.Visible : Visibility.Collapsed;
        if (!custom) return;
        // the same inset as the slots' own bars (slot margin + border + padding), so it lines up with them
        const double inset = 10;
        double roomX = Math.Max(0, PositionMarkerLayer.ActualWidth - 2 * inset - PositionMarker.Width);
        double roomY = Math.Max(0, PositionMarkerLayer.ActualHeight - 2 * inset - PositionMarker.Height);
        Canvas.SetLeft(PositionMarker, inset + Math.Clamp(_style.CustomX, 0, 1) * roomX);
        Canvas.SetTop(PositionMarker, inset + Math.Clamp(_style.CustomY, 0, 1) * roomY);
    }

    void RefreshLaunchText()
    {
        string text = Loc.T(_overlayRunning() ? "Apply" : "Launch");
        if (_settings.SidebarCollapsed)
        {
            LaunchButton.Content = Glyph(_overlayRunning() ? 0xE73E : 0xE768, 14); // CheckMark / Play
            LaunchButton.ToolTip = text;
        }
        else
        {
            LaunchButton.Content = text;
            LaunchButton.ClearValue(ToolTipProperty);
        }
        System.Windows.Automation.AutomationProperties.SetName(LaunchButton, text);
    }

    // ───────────────────────── sidebar ─────────────────────────

    const double SidebarWide = 232, SidebarNarrow = 76; // narrow: 16 + a 44 px icon + 16

    int _slide;       // the latest sidebar slide; an older one finishing does nothing
    bool _sliding;

    /// <summary>
    /// Collapse to icons or expand to labels; remembered across launches. The pages shrink and grow with it, in step.
    /// Pulse draws on the CPU, so each frame must stay cheap: the sidebar's insides keep one layout and are only
    /// clipped, and the preview (a full-size game scene) is swapped for a small still until the slide ends.
    /// </summary>
    void SetSidebar(bool collapsed, bool animate)
    {
        if (collapsed != _settings.SidebarCollapsed)
        {
            _settings.SidebarCollapsed = collapsed;
            Save();
        }

        double to = collapsed ? SidebarNarrow : SidebarWide;
        double from = Sidebar.ActualWidth;
        Thickness fromMargin = PagesHost.Margin; // mid-slide: where it is now
        int slide = ++_slide;

        Sidebar.BeginAnimation(WidthProperty, null);
        PagesHost.BeginAnimation(MarginProperty, null);
        Sidebar.Width = to;
        PlacePages(to);

        if (!animate || from <= 0)
        {
            SidebarInner.Width = double.NaN;
            ApplySidebarContent(collapsed);
            EndSlide();
            return;
        }

        FreezePreview();
        SidebarInner.Width = SidebarWide - 32;   // the full layout, clipped as the edge moves
        if (!collapsed) ApplySidebarContent(false); // labels are revealed as it widens; they go once it's narrow

        var duration = TimeSpan.FromMilliseconds(260);
        var ease = new CubicEase { EasingMode = EasingMode.EaseInOut };
        ease.Freeze();
        var width = new DoubleAnimation(from, to, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        var margin = new ThicknessAnimation(fromMargin, PagesHost.Margin, duration) { EasingFunction = ease, FillBehavior = FillBehavior.Stop };
        width.Completed += (_, _) =>
        {
            if (slide != _slide) return; // toggled again meanwhile: that slide finishes the job
            SidebarInner.Width = double.NaN;
            if (collapsed) ApplySidebarContent(true);
            EndSlide();
        };

        // same clock tick, same curve: the edge and the pages move as one
        Sidebar.BeginAnimation(WidthProperty, width);
        PagesHost.BeginAnimation(MarginProperty, margin);
    }

    /// <summary>A still of the preview at its current size, shown instead of the live one while the sidebar slides.</summary>
    void FreezePreview()
    {
        _sliding = true;
        if (PreviewSnapshot.Visibility == Visibility.Visible || !Editor.IsVisible) return;
        double w = PreviewBox.ActualWidth, h = PreviewBox.ActualHeight;
        if (w < 1 || h < 1) return;

        var dpi = VisualTreeHelper.GetDpi(PreviewBox);
        var still = new RenderTargetBitmap((int)Math.Ceiling(w * dpi.DpiScaleX), (int)Math.Ceiling(h * dpi.DpiScaleY),
            dpi.PixelsPerInchX, dpi.PixelsPerInchY, PixelFormats.Pbgra32);
        var frame = new DrawingVisual(); // through a brush: Render(PreviewBox) would add its offset in the page
        using (var dc = frame.RenderOpen())
            dc.DrawRectangle(new VisualBrush(PreviewBox), null, new Rect(0, 0, w, h));
        still.Render(frame);
        still.Freeze();

        PreviewSnapshot.Source = still;
        PreviewSnapshot.Visibility = Visibility.Visible;
        PreviewBox.Visibility = Visibility.Hidden; // still laid out, never drawn
    }

    void EndSlide()
    {
        _sliding = false;
        if (PreviewSnapshot.Visibility != Visibility.Visible) return;
        PreviewBox.Visibility = Visibility.Visible;
        PreviewSnapshot.Visibility = Visibility.Collapsed;
        PreviewSnapshot.Source = null;
        if (IsVisible) Tick();
    }

    void PlacePages(double sidebarWidth) => PagesHost.Margin = new Thickness(sidebarWidth + 28, 22, 28, 24);

    void ApplySidebarContent(bool collapsed)
    {
        Resources["NavLabelVisibility"] = collapsed ? Visibility.Collapsed : Visibility.Visible;
        BrandText.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

        // icons only: each entry says what it is on hover
        foreach (var nav in new[] { NavOverlay, NavProfiles, NavSummaries, NavSettings })
        {
            if (collapsed) nav.ToolTip = nav.Content;
            else nav.ClearValue(ToolTipProperty);
        }

        // expanded: a pane button beside the name; collapsed: the logo becomes "expand" on hover
        bool rtl = Loc.Instance.Language == AppLanguage.Persian;
        CollapseButton.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
        ExpandButton.Visibility = collapsed ? Visibility.Visible : Visibility.Collapsed;
        CollapseGlyph.Text = ((char)(rtl ? 0xEA49 : 0xE89F)).ToString(); // ClosePane (mirrored in Persian)
        ExpandGlyph.Text = ((char)(rtl ? 0xEA5B : 0xE8A0)).ToString();   // OpenPane
        SetHint(CollapseButton, Loc.T("SidebarCollapse"));
        SetHint(ExpandButton, Loc.T("SidebarExpand"));

        LaunchButton.Style = (Style)FindResource(collapsed ? "PrimaryIcon" : "Primary");
        QuitButton.Style = (Style)FindResource(collapsed ? "IconButton" : "Ghost");
        if (collapsed)
        {
            QuitButton.Width = double.NaN; // as wide as the rail, not IconButton's square
            QuitButton.Content = Glyph(0xE7E8, 14); // PowerButton
            QuitButton.ToolTip = Loc.T("Quit");
        }
        else
        {
            QuitButton.Content = Loc.T("Quit");
            QuitButton.ClearValue(ToolTipProperty);
        }
        System.Windows.Automation.AutomationProperties.SetName(QuitButton, Loc.T("Quit"));
        RefreshLaunchText();
    }

    /// <summary>Tooltip with the shortcut; the screen-reader name without it.</summary>
    static void SetHint(Button button, string text)
    {
        button.ToolTip = text + "   Ctrl+B";
        System.Windows.Automation.AutomationProperties.SetName(button, text);
    }

    static TextBlock Glyph(int code, double size) => new()
    {
        Text = ((char)code).ToString(),
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = size,
        FlowDirection = FlowDirection.LeftToRight,
    };

    /// <summary>The text panel shows the tab's part: numbers or labels. Both share one size slider.</summary>
    void LoadTextValues()
    {
        bool loading = _loading;
        _loading = true; // showing values, not changing them

        double size = _editLabels ? _style.LabelSize : _style.NumberSize;
        TextSizeSlider.Value = Math.Clamp(size, TextSizeSlider.Minimum, TextSizeSlider.Maximum);
        RefreshTextSizeLabel();

        _numberColor.Refresh();
        _labelColor.Refresh();
        _numberColor.Root.Visibility = _editLabels ? Visibility.Collapsed : Visibility.Visible;
        _labelColor.Root.Visibility = _editLabels ? Visibility.Visible : Visibility.Collapsed;

        HeatColors.IsChecked = _style.HeatColors;
        HeatColors.Visibility = _editLabels ? Visibility.Collapsed : Visibility.Visible;
        TextNote.SetBinding(TextBlock.TextProperty,
            new System.Windows.Data.Binding(_editLabels ? "[TextLabelsNote]" : "[TextNumbersNote]") { Source = Loc.Instance });

        _loading = loading;
    }

    /// <summary>The tag says so when the card is wider than the panel: drag it, or use the scrollbar.</summary>
    void RefreshSampleTag()
    {
        bool wide = TextSample.ActualWidth + 32 > TextStageScroll.ViewportWidth + 0.5;
        TextSampleTag.Text = Loc.T(wide ? "TextSampleScroll" : "TextSampleTag");
        TextSampleScrollHint.Visibility = wide ? Visibility.Visible : Visibility.Collapsed;
        TextStagePan.Cursor = wide ? System.Windows.Input.Cursors.SizeWE : null;
    }

    /// <summary>Drag the sample sideways to see the rest of a wide card.</summary>
    void WireSamplePan()
    {
        Point? from = null;
        double offset = 0;
        TextStagePan.MouseLeftButtonDown += (_, e) =>
        {
            if (TextStageScroll.ScrollableWidth <= 0) return;
            from = e.GetPosition(TextStage);
            offset = TextStageScroll.HorizontalOffset;
            TextStagePan.CaptureMouse();
            e.Handled = true;
        };
        TextStagePan.MouseMove += (_, e) =>
        {
            if (from is { } start) TextStageScroll.ScrollToHorizontalOffset(offset - (e.GetPosition(TextStage).X - start.X));
        };
        TextStagePan.MouseLeftButtonUp += (_, _) => { from = null; TextStagePan.ReleaseMouseCapture(); };
        TextStagePan.LostMouseCapture += (_, _) => from = null;
    }

    void RefreshTextSizeLabel() =>
        TextSizeValue.Text = ((_editLabels ? _style.LabelSize : _style.NumberSize) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    void RefreshSliderLabels()
    {
        ScaleValue.Text = (_style.Scale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        OpacityValue.Text = _style.BackgroundOpacity < 0.01
            ? Loc.T("GlassNone")
            : (_style.BackgroundOpacity * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
    }

    // ───────────────────────── pages ─────────────────────────

    void WireProfiles()
    {
        NavOverlay.Checked += (_, _) => ShowPage(OverlayPage);
        NavProfiles.Checked += (_, _) => { ShowProfileList(); ShowPage(ProfilesPage); };
        NavSummaries.Checked += (_, _) => ShowPage(SummariesPage);
        NavSettings.Checked += (_, _) => ShowPage(SettingsPage);

        BrowseGame.Click += (_, _) => BrowseForGame();
        ProfileIconButton.Click += (_, _) => { if (Editing is { } game) PickExeFor(game); };
        ProfileBack.Click += (_, _) => ShowProfileList();

        OnSwitch(ProfileEnabled, on =>
        {
            if (_loading || Editing is not { } game) return;
            game.Enabled = on;
            RefreshProfileHeader();
            Changed?.Invoke();
        });
        ProfileReset.Click += (_, _) =>
        {
            if (Editing is not { } game) return;
            game.CopyStyleFrom(_settings);
            EditStyle(game);
            Commit();
        };
        ProfileDelete.Click += (_, _) =>
        {
            if (Editing is not { } game) return;
            if (!_confirmingDelete)
            {
                _confirmingDelete = true; // a second click deletes
                RefreshProfileHeader();
                return;
            }
            _settings.Profiles.Remove(game);
            Changed?.Invoke();
            ShowProfileList();
        };
    }

    /// <summary>One page at a time. The editor lives on the overlay page unless a game's profile is open.</summary>
    void ShowPage(FrameworkElement page)
    {
        foreach (var p in new FrameworkElement[] { OverlayPage, ProfilesPage, SummariesPage, SettingsPage })
            p.Visibility = p == page ? Visibility.Visible : Visibility.Collapsed;

        if (page == OverlayPage)
        {
            MoveEditor(OverlayEditorHost);
            EditStyle(_settings);
        }
        if (IsVisible) Tick();
    }

    void MoveEditor(ContentControl host)
    {
        if (host.Content == Editor) return;
        OverlayEditorHost.Content = null;
        ProfileEditorHost.Content = null;
        host.Content = Editor;
        EditorScroll.ScrollToTop();
    }

    void ShowProfileList()
    {
        ProfileDetail.Visibility = Visibility.Collapsed;
        ProfileList.Visibility = Visibility.Visible;
        BuildProfileCards();
        BuildRecentGames();
    }

    void OpenProfile(GameProfile game)
    {
        NavProfiles.IsChecked = true; // already there, normally
        _confirmingDelete = false;
        MoveEditor(ProfileEditorHost);
        EditStyle(game);
        RefreshProfileHeader();
        ProfileList.Visibility = Visibility.Collapsed;
        ProfileDetail.Visibility = Visibility.Visible;
        if (IsVisible) Tick();
    }

    void RefreshProfilesPage()
    {
        if (ProfileDetail.Visibility == Visibility.Visible) RefreshProfileHeader();
        else if (ProfilesPage.Visibility == Visibility.Visible) ShowProfileList();
    }

    void RefreshProfileHeader()
    {
        if (Editing is not { } game) return;
        ProfileTitle.Text = game.Name;
        EnsureExe(game);
        ProfileIconButton.Content = GameTile(game.Name, game.ExePath, game.GpuColor, game.FpsColor, 48);
        string iconTip = Loc.T(GameIcons.For(game.ExePath) != null ? "ProfileIconChange" : "ProfileIconPick");
        ProfileIconButton.ToolTip = iconTip;
        System.Windows.Automation.AutomationProperties.SetName(ProfileIconButton, iconTip);
        ProfileSubtitle.Text = string.Format(Loc.T(game.Enabled ? "ProfileAppliesTo" : "ProfileOff"), game.Process + ".exe");
        ProfileBackGlyph.Text = ((char)(Loc.Instance.Language == AppLanguage.Persian ? 0xE76C : 0xE76B)).ToString(); // chevron back, either reading direction
        bool loading = _loading;
        _loading = true;
        ProfileEnabled.IsChecked = game.Enabled;
        _loading = loading;
        ProfileDelete.Content = Loc.T(_confirmingDelete ? "SavedDeleteSure" : "ProfileDelete");
        ProfileDelete.Foreground = _confirmingDelete ? Palette.Hot : (Brush)FindResource("Muted");
    }

    /// <summary>A card per game with a profile: its initial in its own colours, the name, the exe and its look at a glance.</summary>
    void BuildProfileCards()
    {
        ProfileCards.Children.Clear();
        ProfilesEmpty.Visibility = _settings.Profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        foreach (var game in _settings.Profiles)
        {
            EnsureExe(game);
            var tile = GameTile(game.Name, game.ExePath, game.GpuColor, game.FpsColor, 42);
            DockPanel.SetDock(tile, Dock.Left);

            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(GameName(game.Name));
            text.Children.Add(new TextBlock
            {
                Text = game.Process + ".exe",
                FontSize = 11.5,
                Foreground = (Brush)FindResource("Muted"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 1, 0, 0),
            });
            text.Children.Add(new TextBlock
            {
                Text = game.Enabled ? LookSummary(game) : Loc.T("ProfileOffShort"),
                FontSize = 11.5,
                Foreground = game.Enabled ? (Brush)FindResource("Soft") : Palette.Warm,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Margin = new Thickness(0, 5, 0, 0),
            });

            var row = new DockPanel();
            row.Children.Add(tile);
            row.Children.Add(text);

            var card = new Button
            {
                Style = (Style)FindResource("Card"),
                Content = row,
                Foreground = (Brush)FindResource("Text"),
                Width = 300,
                Margin = new Thickness(0, 0, 10, 10),
                Opacity = game.Enabled ? 1 : 0.65,
                ToolTip = Loc.T("ProfileOpen"),
            };
            System.Windows.Automation.AutomationProperties.SetName(card, game.Name);
            card.Click += (_, _) => OpenProfile(game);
            ProfileCards.Children.Add(card);
        }
    }

    /// <summary>Game names are nearly always Latin: laid out left to right so "(Demo)" keeps its brackets in Persian.</summary>
    static TextBlock GameName(string name) => new()
    {
        Text = name,
        FontWeight = FontWeights.SemiBold,
        TextTrimming = TextTrimming.CharacterEllipsis,
        FlowDirection = FlowDirection.LeftToRight,
        HorizontalAlignment = HorizontalAlignment.Left,
    };

    /// <summary>
    /// The game's avatar: its own icon on a dark tile when Pulse knows the .exe,
    /// else its initial in the profile's colours.
    /// </summary>
    FrameworkElement GameTile(string name, string? exePath, string hueHex, string letterHex, double size)
    {
        var tile = new Border { Width = size, Height = size, CornerRadius = new CornerRadius(Math.Round(size * 0.26)) };
        if (GameIcons.For(exePath) is { } icon)
        {
            tile.Background = IconTileBrush;
            var image = new Image { Source = icon, Width = Math.Round(size * 0.72), Height = Math.Round(size * 0.72), Stretch = Stretch.Uniform };
            RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
            tile.Child = image;
            return tile;
        }

        var hue = ColorUtil.Parse(hueHex, OverlayStyle.DefaultGpuColor);
        tile.Background = ColorUtil.Frozen(new LinearGradientBrush(
            ColorUtil.Mix(hue, Colors.Black, 0.45), ColorUtil.Mix(hue, Colors.Black, 0.7), 90));
        tile.Child = new TextBlock
        {
            Text = Initial(name),
            Foreground = ColorUtil.Solid(ColorUtil.Parse(letterHex, OverlayStyle.DefaultFpsColor)),
            FontFamily = new FontFamily("Bahnschrift, Segoe UI"),
            FontWeight = FontWeights.SemiBold,
            FontSize = Math.Round(size * 0.43),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        return tile;
    }

    /// <summary>The same deep tile as Pulse's own logo, so any game's icon sits well on it.</summary>
    static readonly Brush IconTileBrush = ColorUtil.Frozen(new LinearGradientBrush(
        Color.FromRgb(0x2A, 0x31, 0x4E), Color.FromRgb(0x13, 0x17, 0x27), 90));

    /// <summary>A profile without a known .exe: look again (the game may have been played or be running since).</summary>
    void EnsureExe(GameProfile game)
    {
        if (!string.IsNullOrEmpty(game.ExePath) && File.Exists(game.ExePath)) return;
        if (GameIcons.FindExe(game.Process) is not { } found) return;
        game.ExePath = found;
        Save();
    }

    /// <summary>Point a profile at its game's .exe by hand: its icon shows from then on.</summary>
    void PickExeFor(GameProfile game)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"{Loc.T("ExeFilter")}|*.exe",
            FileName = game.Process + ".exe",
            InitialDirectory = Directory.Exists(Path.GetDirectoryName(game.ExePath) ?? "")
                ? Path.GetDirectoryName(game.ExePath)
                : Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dlg.ShowDialog(this) != true) return;
        game.ExePath = dlg.FileName;
        Save();
        RefreshProfileHeader();
    }

    /// <summary>"Full · Top right · 120%": the look in a line.</summary>
    static string LookSummary(OverlayStyle look) =>
        $"{Loc.T(look.Compact ? "Compact" : "Full")}  ·  {Loc.T(look.Corner.ToString())}  ·  " +
        (look.Scale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    static string Initial(string name)
    {
        foreach (char c in name)
            if (char.IsLetterOrDigit(c)) return char.ToUpperInvariant(c).ToString();
        return "?";
    }

    /// <summary>Games played lately (and the one running now) without a profile yet: one click adds one.</summary>
    void BuildRecentGames()
    {
        RecentGames.Children.Clear();
        const int Max = 12;
        foreach (var (process, name) in _knownGames())
        {
            if (RecentGames.Children.Count >= Max) break;
            if (_settings.Profiles.Exists(p => p.Process.Equals(process, StringComparison.OrdinalIgnoreCase))) continue;

            var plus = new TextBlock
            {
                Text = ((char)0xE710).ToString(), // Add
                FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
                FontSize = 12,
                Foreground = (Brush)FindResource("Accent"),
                VerticalAlignment = VerticalAlignment.Center,
                FlowDirection = FlowDirection.LeftToRight,
            };
            string? exe = GameIcons.FindExe(process, lookAtRunning: false);
            FrameworkElement lead = GameIcons.For(exe) != null
                ? GameTile(name, exe, OverlayStyle.DefaultGpuColor, OverlayStyle.DefaultFpsColor, 30)
                : plus;
            DockPanel.SetDock(lead, Dock.Left);

            var text = new StackPanel { Margin = new Thickness(10, 0, 0, 0) };
            text.Children.Add(GameName(name));
            text.Children.Add(new TextBlock
            {
                Text = process + ".exe",
                FontSize = 11.5,
                Foreground = (Brush)FindResource("Muted"),
                TextTrimming = TextTrimming.CharacterEllipsis,
                FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Left,
            });

            var row = new DockPanel();
            row.Children.Add(lead);
            row.Children.Add(text);

            var card = new Button
            {
                Style = (Style)FindResource("Card"),
                Content = row,
                Foreground = (Brush)FindResource("Text"),
                Width = 240,
                Margin = new Thickness(0, 0, 10, 10),
                ToolTip = Loc.T("ProfileCreate"),
            };
            System.Windows.Automation.AutomationProperties.SetName(card, name);
            card.Click += (_, _) => AddProfile(process, name, exe);
            RecentGames.Children.Add(card);
        }
        RecentEmpty.Visibility = RecentGames.Children.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Any game, by its .exe: the process name is what Pulse matches while it's in front.</summary>
    void BrowseForGame()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = $"{Loc.T("ExeFilter")}|*.exe",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        };
        if (dlg.ShowDialog(this) != true) return;

        string process = Path.GetFileNameWithoutExtension(dlg.FileName);
        string name = process;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dlg.FileName);
            string? pretty = !string.IsNullOrWhiteSpace(info.ProductName) ? info.ProductName : info.FileDescription;
            if (!string.IsNullOrWhiteSpace(pretty)) name = pretty.Trim();
        }
        catch { /* no version info: the exe name will do */ }
        AddProfile(process, name, dlg.FileName);
    }

    /// <summary>A new profile starts from the default look, so only what differs needs changing. An existing one just opens.</summary>
    void AddProfile(string process, string name, string? exePath = null)
    {
        var game = _settings.Profiles.Find(p => p.Process.Equals(process, StringComparison.OrdinalIgnoreCase));
        if (game is null)
        {
            game = new GameProfile { Process = process, Name = name, ExePath = exePath ?? GameIcons.FindExe(process) };
            game.CopyStyleFrom(_settings);
            _settings.Profiles.Add(game);
            Changed?.Invoke();
        }
        OpenProfile(game);
    }

    // ───────────────────────── colour row ─────────────────────────

    /// <summary>"GPU  ● ● ● ● ● ● ●  ◍  #B39DFF" — swatches, any-colour wheel, hex.</summary>
    sealed class ColorRow
    {
        readonly SettingsWindow _owner;
        readonly Func<string> _get;
        readonly Action<string> _set;
        readonly TextBlock _label;
        readonly List<RadioButton> _swatches = new();
        readonly Button _wheel;
        readonly TextBox _hex;
        bool _updating;

        public string Default { get; }
        public FrameworkElement Root { get; }

        public ColorRow(SettingsWindow owner, string name, Func<string> get, Action<string> set, string defaultHex, string[]? swatchHexes = null)
        {
            _owner = owner;
            _get = get;
            _set = set;
            Default = defaultHex;

            var grid = new Grid { Margin = new Thickness(0, 0, 0, 10), FlowDirection = FlowDirection.LeftToRight };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(46) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _label = new TextBlock
            {
                Text = name,
                FontFamily = new FontFamily("Bahnschrift, Segoe UI"),
                FontWeight = FontWeights.SemiBold,
                FontSize = 13,
                VerticalAlignment = VerticalAlignment.Center,
            };
            grid.Children.Add(_label);

            var swatches = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            string group = "color-" + name;
            foreach (string hex in swatchHexes ?? Swatches)
            {
                var sw = new RadioButton
                {
                    Style = (Style)owner.FindResource("Swatch"),
                    GroupName = group,
                    Background = ColorUtil.Solid(ColorUtil.Parse(hex, defaultHex)),
                    Tag = hex,
                    ToolTip = hex,
                };
                System.Windows.Automation.AutomationProperties.SetName(sw, $"{name} {hex}");
                sw.Checked += (_, _) => { if (!_updating) Set(hex); };
                _swatches.Add(sw);
                swatches.Children.Add(sw);
            }

            _wheel = new Button { Style = (Style)owner.FindResource("WheelButton") };
            _wheel.SetBinding(ToolTipProperty, new System.Windows.Data.Binding("[PickColor]") { Source = Loc.Instance });
            _wheel.Click += (_, _) => PickAny();
            swatches.Children.Add(_wheel);
            Grid.SetColumn(swatches, 1);
            grid.Children.Add(swatches);

            _hex = new TextBox { Style = (Style)owner.FindResource("HexBox"), VerticalAlignment = VerticalAlignment.Center };
            _hex.TextChanged += (_, _) =>
            {
                if (_updating) return;
                string t = _hex.Text.Trim();
                if (t.Length >= 6 && ColorUtil.TryParse(t, out var c)) Set(ColorUtil.ToHex(c), fromHexBox: true);
            };
            _hex.LostKeyboardFocus += (_, _) => Refresh();
            Grid.SetColumn(_hex, 2);
            grid.Children.Add(_hex);

            Root = grid;
        }

        public void Set(string hex, bool fromHexBox = false)
        {
            _set(hex.ToUpperInvariant());
            Refresh(keepHexText: fromHexBox);
            _owner.Commit();
        }

        public void Refresh(bool keepHexText = false)
        {
            _updating = true;
            string current = _get();
            var color = ColorUtil.Parse(current, Default);
            string hex = ColorUtil.ToHex(color);

            _label.Foreground = ColorUtil.Solid(color);
            bool matched = false;
            foreach (var sw in _swatches)
            {
                bool on = string.Equals((string)sw.Tag, hex, StringComparison.OrdinalIgnoreCase);
                sw.IsChecked = on;
                matched |= on;
            }
            _wheel.Tag = matched ? null : "checked";
            if (!keepHexText) _hex.Text = hex;
            _updating = false;
        }

        void PickAny()
        {
            var c = ColorUtil.Parse(_get(), Default);
            using var dlg = new Forms.ColorDialog
            {
                FullOpen = true,
                AnyColor = true,
                Color = System.Drawing.Color.FromArgb(c.R, c.G, c.B),
            };
            var owner = new Forms.NativeWindow();
            owner.AssignHandle(new WindowInteropHelper(_owner).Handle);
            try
            {
                if (dlg.ShowDialog(owner) == Forms.DialogResult.OK)
                    Set(ColorUtil.ToHex(Color.FromRgb(dlg.Color.R, dlg.Color.G, dlg.Color.B)));
            }
            finally { owner.ReleaseHandle(); }
        }
    }
}
