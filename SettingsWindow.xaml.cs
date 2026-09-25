using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
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

    static readonly Corner[] Corners =
        { Corner.TopLeft, Corner.TopCenter, Corner.TopRight, Corner.BottomLeft, Corner.BottomCenter, Corner.BottomRight };

    const string CustomScene = "custom";

    readonly AppSettings _settings;
    readonly Func<HardwareSnapshot?> _realHardware;
    readonly Func<bool> _overlayRunning;
    readonly OverlayViewModel _preview = new();
    readonly PreviewFeed _feed = new();
    readonly DispatcherTimer _timer;
    readonly Dictionary<string, ImageSource> _images = new(), _huds = new();
    readonly List<ColorRow> _colorRows = new();
    readonly Dictionary<string, RadioButton> _sceneChips = new();

    GameScene _scene;
    bool _loading = true;

    /// <summary>Any overlay option changed — apply to the live overlay and save.</summary>
    public event Action? Changed;
    public event Action? LanguageChanged;
    public event Action? LaunchRequested;
    public event Action? QuitRequested;

    public SettingsWindow(AppSettings settings, Func<HardwareSnapshot?> realHardware, Func<bool> overlayRunning)
    {
        _settings = settings;
        _realHardware = realHardware;
        _overlayRunning = overlayRunning;
        _scene = GameScenes.Find(settings.PreviewScene);

        InitializeComponent();

        PreviewCard.DataContext = _preview;
        Screen.Width = SystemParameters.PrimaryScreenWidth;
        Screen.Height = SystemParameters.PrimaryScreenHeight;
        // corners in screen units: the Viewbox shrinks the whole screen ~3x
        Screen.Clip = new RectangleGeometry(new Rect(0, 0, Screen.Width, Screen.Height), 28, 28);
        var px = Forms.Screen.PrimaryScreen?.Bounds;
        ScreenInfo.Text = px is { } b ? $"{b.Width} × {b.Height}" : "";

        BuildPositions();
        BuildColorRows();
        BuildSceneChips();
        LoadValues();
        Wire();

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
            slot.Checked += (_, _) => { _settings.Corner = corner; RefreshPositionName(); Commit(); };
            PositionGrid.Children.Add(slot);
        }
    }

    void BuildColorRows()
    {
        _colorRows.Add(new ColorRow(this, "FPS", () => _settings.FpsColor, v => _settings.FpsColor = v, AppSettings.DefaultFpsColor));
        _colorRows.Add(new ColorRow(this, "GPU", () => _settings.GpuColor, v => _settings.GpuColor = v, AppSettings.DefaultGpuColor));
        _colorRows.Add(new ColorRow(this, "CPU", () => _settings.CpuColor, v => _settings.CpuColor = v, AppSettings.DefaultCpuColor));
        _colorRows.Add(new ColorRow(this, "RAM", () => _settings.RamColor, v => _settings.RamColor = v, AppSettings.DefaultRamColor));
        foreach (var row in _colorRows) ColorRows.Children.Add(row.Root);
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

    void LoadValues()
    {
        (_settings.Language == AppLanguage.Persian ? LangFa : LangEn).IsChecked = true;
        (_settings.Compact ? LayoutCompact : LayoutFull).IsChecked = true;

        foreach (RadioButton slot in PositionGrid.Children)
            slot.IsChecked = (Corner)slot.Tag == _settings.Corner;
        RefreshPositionName();

        foreach (var row in _colorRows) row.Refresh();
        FpsWarnings.IsChecked = _settings.FpsWarnings;

        ScaleSlider.Value = Math.Clamp(_settings.Scale, ScaleSlider.Minimum, ScaleSlider.Maximum);
        OpacitySlider.Value = Math.Clamp(_settings.BackgroundOpacity, OpacitySlider.Minimum, OpacitySlider.Maximum);
        RefreshSliderLabels();

        ShowOnLaunch.IsChecked = _settings.ShowSettingsOnLaunch;
        HudToggle.IsChecked = _settings.PreviewHud;

        string sceneId = _settings.PreviewScene == CustomScene && File.Exists(_settings.PreviewImage) ? CustomScene : _scene.Id;
        _sceneChips[sceneId].IsChecked = true;
    }

    void Wire()
    {
        LangEn.Checked += (_, _) => SetLanguage(AppLanguage.English);
        LangFa.Checked += (_, _) => SetLanguage(AppLanguage.Persian);

        LayoutCompact.Checked += (_, _) => { _settings.Compact = true; Commit(); };
        LayoutFull.Checked += (_, _) => { _settings.Compact = false; Commit(); };

        OnSwitch(FpsWarnings, on => { _settings.FpsWarnings = on; Commit(); });
        ResetColors.Click += (_, _) =>
        {
            foreach (var row in _colorRows) row.Set(row.Default);
            _settings.FpsWarnings = true;
            FpsWarnings.IsChecked = true;
            Commit();
        };

        ScaleSlider.ValueChanged += (_, e) => { _settings.Scale = Math.Round(e.NewValue, 2); RefreshSliderLabels(); Commit(); };
        OpacitySlider.ValueChanged += (_, e) => { _settings.BackgroundOpacity = Math.Round(e.NewValue, 2); RefreshSliderLabels(); Commit(); };

        OnSwitch(ShowOnLaunch, on => { _settings.ShowSettingsOnLaunch = on; Save(); });
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

    void SetLanguage(AppLanguage language)
    {
        if (_loading && language == _settings.Language) return;
        _settings.Language = language;
        Loc.Instance.Language = language;
        RefreshPositionName();
        RefreshLaunchText();
        Save();
        LanguageChanged?.Invoke();
    }

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
        PreviewCard.ApplySettings(_settings);
        _preview.ApplyStyle(_settings);
        PlaceCard();
    }

    void PlaceCard()
    {
        double w = PreviewCard.ActualWidth, h = PreviewCard.ActualHeight;
        if (w <= 0 || h <= 0) return;
        var (left, top) = OverlayWindow.Place(_settings.Corner, Screen.Width, Screen.Height, w, h);
        Canvas.SetLeft(PreviewCard, left);
        Canvas.SetTop(PreviewCard, top);
        UpdateZoom();
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
        var (hw, fps, target) = _feed.Next(_scene, _realHardware());
        _preview.Apply(hw, fps, IsCustomScene ? "YourGame.exe" : target, null);
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

    void RefreshPositionName() => PositionName.Text = Loc.T(_settings.Corner.ToString());

    void RefreshLaunchText() => LaunchButton.Content = Loc.T(_overlayRunning() ? "Apply" : "Launch");

    void RefreshSliderLabels()
    {
        ScaleValue.Text = (_settings.Scale * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
        OpacityValue.Text = (_settings.BackgroundOpacity * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        base.OnClosed(e);
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

        public ColorRow(SettingsWindow owner, string name, Func<string> get, Action<string> set, string defaultHex)
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
            foreach (string hex in Swatches)
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
