using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using Pulse.Services;
using Forms = System.Windows.Forms;

namespace Pulse;

public partial class App : Application
{
    /// <summary>A second launch sets this, and the running Pulse opens its window.</summary>
    const string ShowSignalName = "Pulse.Overlay.ShowSettings";

    Mutex? _mutex;
    bool _ownsMutex;
    EventWaitHandle? _showSignal;
    RegisteredWaitHandle? _showWait;
    AppSettings? _settings;
    SensorService? _sensors;
    FpsService? _fps;
    OverlayWindow? _window;
    SettingsWindow? _settingsWindow;
    HotkeyManager? _hotkeys;
    Forms.NotifyIcon? _tray;
    Forms.ContextMenuStrip? _trayMenu;
    DispatcherTimer? _timer;
    bool _hintedTray;
    DispatcherTimer? _updateTimer;
    DispatcherTimer? _saveTimer;
    UpdateInfo? _pendingUpdate;   // found automatically, not yet shown in the dialog
    UpdateWindow? _updateWindow;
    IReadOnlyList<WhatsNew.Release>? _whatsNew;   // just updated: shown once the user opens the window
    GameSession? _pendingSummary;                  // a game ended while another one was in front
    SessionSummaryWindow? _summaryWindow;
    SavedSummariesWindow? _savedWindow;
    bool _exiting;
    int _tick;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Switches used by the installer / uninstaller — do the job and quit, no UI.
        if (e.Args.Length > 0)
        {
            switch (e.Args[0].ToLowerInvariant())
            {
                case "--register-startup": StartupTask.Enable(); Shutdown(); return;
                case "--unregister-startup": StartupTask.Disable(); Shutdown(); return;
            }
        }

        // "--tray" = started at sign-in: straight to the overlay, no window.
        bool fromLogon = Array.Exists(e.Args, a => a.Equals("--tray", StringComparison.OrdinalIgnoreCase));

        _mutex = new Mutex(true, "Pulse.Overlay.SingleInstance", out _ownsMutex);
        if (!_ownsMutex)
        {
            // Already running: bring its window up instead of silently doing nothing.
            if (!fromLogon && EventWaitHandle.TryOpenExisting(ShowSignalName, out var signal))
                using (signal) signal.Set();
            Shutdown();
            return;
        }

        _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, ShowSignalName);
        _showWait = ThreadPool.RegisterWaitForSingleObject(_showSignal,
            (_, _) => Dispatcher.BeginInvoke(new Action(ShowSettings)), null, Timeout.Infinite, executeOnlyOnce: false);

        DispatcherUnhandledException += OnUnhandled;

        // The overlay is tiny, so draw it on the CPU. It never creates a D3D device,
        // never competes with the game for GPU time, and never shows up in PresentMon.
        RenderOptions.ProcessRenderMode = System.Windows.Interop.RenderMode.SoftwareOnly;

        // Below-normal priority + Windows "efficiency mode" (EcoQoS → E-cores on hybrid CPUs).
        Native.ApplyLowImpactMode();

        _settings = AppSettings.Load();
        Loc.Instance.Language = _settings.Language;
        PrepareWhatsNew();

        _sensors = new SensorService();
        _sensors.Start();
        MemoryModules.LoadInBackground(); // RAM type, speed and channels for the game summary

        _fps = new FpsService(AppSettings.Resolve(_settings.PresentMonPath), _settings.PresentMonArgs);
        _fps.Start();

        _window = new OverlayWindow(_settings);
        BuildTray();

        _hotkeys = new HotkeyManager(_window);
        var failed = new List<string>();
        if (!_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Shift, Key.O, ToggleOverlay)) failed.Add("Ctrl+Shift+O");
        if (!_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Shift, Key.L, ToggleCompact)) failed.Add("Ctrl+Shift+L");
        if (!_hotkeys.Register(ModifierKeys.Control | ModifierKeys.Shift, Key.P, CycleCorner)) failed.Add("Ctrl+Shift+P");

        // One UI tick every 500 ms. Background priority = it yields to everything else.
        _timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(500) };
        _timer.Tick += OnTick;
        _timer.Start();

        SystemEvents.DisplaySettingsChanged += OnDisplayChanged;

        // First update check shortly after start (not during it), then every 6 hours.
        _updateTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(10) };
        _updateTimer.Tick += async (_, _) =>
        {
            _updateTimer.Interval = TimeSpan.FromHours(6);
            await CheckForUpdateAsync();
        };
        _updateTimer.Start();

        if (fromLogon || !_settings.ShowSettingsOnLaunch) ShowOverlay();
        else ShowSettings();

        if (failed.Count > 0)
            _tray?.ShowBalloonTip(4000, "Pulse", Loc.T("HotkeysTaken") + string.Join(", ", failed), Forms.ToolTipIcon.Warning);
        else if (_fps.Error is { } err)
            _tray?.ShowBalloonTip(5000, "Pulse", err, Forms.ToolTipIcon.Info);
        else if (_whatsNew != null && _settingsWindow?.IsVisible != true)
            _tray?.ShowBalloonTip(6000, "Pulse", string.Format(Loc.T("TrayUpdated"), UpdateService.Current), Forms.ToolTipIcon.Info);
    }

    void OnTick(object? sender, EventArgs e)
    {
        if (_window is null || _fps is null || _sensors is null) return;
        _tick++;

        // Game sessions are followed even with the overlay hidden: PresentMon runs anyway,
        // and following the foreground window is a couple of cheap calls.
        _fps.UpdateTarget();
        if (_settings != null) _window.SetLook(_settings.StyleFor(_fps.TargetName)); // a game with its own profile
        // The hot spot is a slow read on NVIDIA: only while the overlay's look shows it, or the preview may.
        _sensors.WantHotspot = _window.Look.ShowGpuHotspot || _settingsWindow?.IsVisible == true;
        if (!_sensors.Paused && _sensors.Latest is { } hw) _fps.Sessions.Sample(hw);
        if (_tick % 4 == 0) CollectSessions();

        if (!_window.IsVisible) return;
        _window.ViewModel.Apply(_sensors.Latest, _fps.GetStats(), _fps.TargetName, _fps.Error);

        // Some games re-assert their own z-order; nudge ours back every ~3 s.
        if (_tick % 6 == 0) _window.EnsureTopmost();
    }

    // ───────────────────────── game sessions ─────────────────────────

    /// <summary>
    /// Every ~2 s: games that closed → history, and the summary for the latest one. One held back
    /// because another game was in front shows as soon as that game is closed or left.
    /// </summary>
    void CollectSessions()
    {
        if (_fps is null || _settings is null) return;
        var ended = _fps.Sessions.TakeEnded();
        foreach (var session in ended) SessionHistory.Add(session);
        if (!_settings.ShowSessionSummary) return;

        if (ended.Count == 0)
        {
            if (_pendingSummary is { } pending && !_fps.Sessions.GameInFront) ShowSummary(pending);
            return;
        }

        var latest = ended[^1];
        if (_fps.Sessions.GameInFront)
        {
            // Already in another game: never pop a window over it. A tray note it is, until that game is over.
            _pendingSummary = latest;
            _tray?.ShowBalloonTip(6000, "Pulse", string.Format(Loc.T("TraySummaryReady"), latest.Game), Forms.ToolTipIcon.Info);
        }
        else ShowSummary(latest);
    }

    void ShowSummary(GameSession session)
    {
        if (_settings is null) return;
        _pendingSummary = null;
        _summaryWindow?.Close(); // one at a time: the newest wins

        var window = new SessionSummaryWindow(session, SessionHistory.Previous(session), _settings);
        window.SettingChanged += () =>
        {
            _settings.Save();
            _settingsWindow?.SyncSummarySwitch();
        };
        window.Closed += (_, _) => { if (_summaryWindow == window) _summaryWindow = null; };
        _summaryWindow = window;

        // Pulse runs in the background, so Windows won't hand it the foreground: sit on top until shown.
        window.Topmost = true;
        window.ContentRendered += (_, _) => window.Topmost = false;
        window.Show();
        window.Activate();
    }

    void ShowLastSummary()
    {
        if (_pendingSummary is { } pending) { ShowSummary(pending); return; }
        if (SessionHistory.Latest is { } last) ShowSummary(last);
        else _tray?.ShowBalloonTip(5000, "Pulse", Loc.T("TrayNoSummary"), Forms.ToolTipIcon.Info);
    }

    void ShowSavedSummaries()
    {
        if (_savedWindow != null) { _savedWindow.Activate(); return; }

        var window = new SavedSummariesWindow(ShowSummary);
        window.Closed += (_, _) => _savedWindow = null;
        _savedWindow = window;
        window.Topmost = true; // same as the summary: Windows won't hand a background app the foreground
        window.ContentRendered += (_, _) => window.Topmost = false;
        window.Show();
        window.Activate();
    }

    // ───────────────────────── actions ─────────────────────────

    void ToggleOverlay()
    {
        if (_window is null) return;
        if (_window.IsVisible) HideOverlay();
        else ShowOverlay();
    }

    void ShowOverlay()
    {
        if (_window is null || _sensors is null) return;
        _sensors.Paused = false;
        _window.Show();
        _window.EnsureTopmost();
        OnTick(null, EventArgs.Empty);
    }

    void HideOverlay()
    {
        _window?.Hide();
        UpdateSensorPause();
    }

    // Hidden overlay and no window open = no sensor polling at all.
    void UpdateSensorPause()
    {
        if (_sensors != null)
            _sensors.Paused = _window?.IsVisible != true && _settingsWindow?.IsVisible != true;
    }

    void ShowSettings()
    {
        if (_settings is null || _sensors is null) return;
        _sensors.Paused = false; // the preview borrows the real CPU / GPU names

        if (_settingsWindow is null)
        {
            var sensors = _sensors;
            _settingsWindow = new SettingsWindow(_settings, () => sensors.Latest, () => sensors.HotspotSupported,
                () => _window?.IsVisible == true, KnownGames);
            _settingsWindow.Changed += Commit;
            _settingsWindow.LanguageChanged += UpdateTrayLanguage;
            _settingsWindow.LaunchRequested += () =>
            {
                ShowOverlay();
                _settingsWindow?.Close();
            };
            _settingsWindow.QuitRequested += Quit;
            _settingsWindow.UpdateFound += ShowUpdate;
            _settingsWindow.SummaryRequested += ShowSummary;
            _settingsWindow.LastSummaryRequested += ShowLastSummary;
            _settingsWindow.Closed += OnSettingsClosed;
        }

        if (_settingsWindow.WindowState == WindowState.Minimized) _settingsWindow.WindowState = WindowState.Normal;
        _settingsWindow.Show();
        _settingsWindow.Activate();

        // An update found while only the overlay was up: offer it now that the user is here.
        if (_pendingUpdate is { } update)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => ShowUpdate(update)));

        // Just updated: say what changed, once, now that the user is looking (never over a game).
        if (_whatsNew != null)
            Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(ShowWhatsNew));
    }

    /// <summary>Games to offer in "Add a game": the one in front now, then the ones played lately.</summary>
    IReadOnlyList<(string Process, string Name)> KnownGames()
    {
        var games = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (_fps is { TargetName.Length: > 0 } fps && fps.GetStats().Fps is not null && seen.Add(fps.TargetName))
            games.Add((fps.TargetName, fps.TargetName));
        foreach (var s in SessionHistory.Recent)
            if (s.Process.Length > 0 && seen.Add(s.Process)) games.Add((s.Process, s.Game.Length > 0 ? s.Game : s.Process));
        return games;
    }

    // ───────────────────────── updates ─────────────────────────

    async Task CheckForUpdateAsync()
    {
        if (_settings is not { CheckForUpdates: true } || _updateWindow != null) return;

        UpdateInfo? update;
        try { update = await UpdateService.CheckAsync(); }
        catch { return; } // offline / GitHub down: try again next time, quietly

        _settings.LastUpdateCheck = DateTime.Now;
        _settings.Save();
        _settingsWindow?.ReportUpdate(update);

        if (update is null || update.Version.ToString() == _settings.SkippedVersion) return;
        if (_pendingUpdate?.Version == update.Version) return; // already offered this session

        _pendingUpdate = update;
        if (_settingsWindow?.IsVisible == true)
        {
            ShowUpdate(update);
        }
        else
        {
            // Probably in a game: never pop a window over it. A tray note it is.
            _tray?.ShowBalloonTip(6000, "Pulse", string.Format(Loc.T("TrayUpdate"), update.Version), Forms.ToolTipIcon.Info);
        }
    }

    void ShowUpdate(UpdateInfo update)
    {
        _pendingUpdate = null;
        if (_updateWindow != null) { _updateWindow.Activate(); return; }

        _updateWindow = new UpdateWindow(update);
        if (_settingsWindow?.IsVisible == true) _updateWindow.Owner = _settingsWindow;
        else _updateWindow.WindowStartupLocation = WindowStartupLocation.CenterScreen;

        _updateWindow.Closed += (_, _) =>
        {
            var choice = _updateWindow.Choice;
            _updateWindow = null;
            switch (choice)
            {
                case UpdateChoice.Skip:
                    if (_settings != null) { _settings.SkippedVersion = update.Version.ToString(); _settings.Save(); }
                    break;
                case UpdateChoice.Installing:
                    Quit(); // the installer waits for nothing: it closes whatever is left and relaunches us
                    break;
            }
        };
        _updateWindow.Show();
        _updateWindow.Activate();
    }

    void PrepareWhatsNew()
    {
        if (_settings is null) return;

        // A fresh install has nothing "new" to announce: start counting from this version.
        if (_settings.FirstRun)
        {
            _settings.LastSeenVersion = UpdateService.Current.ToString();
            _settings.Save();
            return;
        }

        var releases = WhatsNew.Since(_settings.LastSeenVersion, UpdateService.Current);
        if (releases.Count > 0) _whatsNew = releases;
    }

    void ShowWhatsNew()
    {
        if (_whatsNew is not { } releases || _settings is null) return;
        _whatsNew = null;

        _settings.LastSeenVersion = UpdateService.Current.ToString();
        _settings.Save();

        var window = new WhatsNewWindow(releases);
        if (_settingsWindow?.IsVisible == true) window.Owner = _settingsWindow;
        else window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        window.Show();
        window.Activate();
    }

    // Closed, not hidden: the preview's bitmaps (tens of MB) must not live on next to a game.
    void OnSettingsClosed(object? sender, EventArgs e)
    {
        _settingsWindow = null;
        if (_exiting) return;

        UpdateSensorPause();

        // WPF lets go of a closed window a moment later (input / render bookkeeping),
        // so collect a few seconds on, once it is truly unreachable.
        var later = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromSeconds(4) };
        later.Tick += (_, _) =>
        {
            later.Stop();
            GCSettings.LargeObjectHeapCompactionMode = GCLargeObjectHeapCompactionMode.CompactOnce;
            GC.Collect();
            GC.WaitForPendingFinalizers(); // bitmaps free their native pixels in finalizers
            GC.Collect();
        };
        later.Start();

        if (_window?.IsVisible == true || _hintedTray) return;
        _hintedTray = true;
        _tray?.ShowBalloonTip(3000, "Pulse", Loc.T("StillRunning"), Forms.ToolTipIcon.Info);
    }

    void Quit()
    {
        _exiting = true;
        Shutdown();
    }

    // Hotkeys and the tray change the look on screen: the game's profile while it has one.
    void ToggleCompact()
    {
        if (_window is null) return;
        _window.Look.Compact = !_window.Look.Compact;
        Commit();
    }

    void CycleCorner()
    {
        if (_window is null) return;
        var look = _window.Look;
        look.Corner = (Corner)(((int)look.Corner + 1) % Enum.GetValues<Corner>().Length);
        Commit();
    }

    void Commit()
    {
        // a profile added or turned off for the game in front applies right away
        if (_window != null && _settings != null && _fps != null) _window.SetLook(_settings.StyleFor(_fps.TargetName));
        _window?.ApplySettings();
        SaveSoon();
    }

    /// <summary>
    /// Write settings.json once the changes settle: a slider drag or a run of colour clicks is one write,
    /// not one per step. <see cref="OnExit"/> writes anything still pending.
    /// </summary>
    void SaveSoon()
    {
        if (_saveTimer is null)
        {
            _saveTimer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(400) };
            _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); _settings?.Save(); };
        }
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ───────────────────────── tray ─────────────────────────

    void BuildTray()
    {
        _trayMenu = new Forms.ContextMenuStrip { ShowCheckMargin = true, ShowImageMargin = false };

        var miSettings = Item("TraySettings", ShowSettings);
        miSettings.Font = new System.Drawing.Font(miSettings.Font, System.Drawing.FontStyle.Bold);

        var miToggle = Item("TrayToggle", ToggleOverlay);
        miToggle.ShortcutKeyDisplayString = "Ctrl+Shift+O";

        var miCompact = Item("TrayCompact", ToggleCompact);
        miCompact.ShortcutKeyDisplayString = "Ctrl+Shift+L";

        var miCorner = Choice("TrayCorner", Array.ConvertAll(Enum.GetValues<Corner>(), c => (c.ToString(), c)),
            () => _window!.Look.Corner, v => { _window!.Look.Corner = v; Commit(); });

        var miSummary = Item("TrayLastSummary", ShowLastSummary);
        var miSaved = Item("TraySaved", ShowSavedSummaries);

        var miExit = Item("TrayExit", Quit);

        _trayMenu.Items.AddRange(new Forms.ToolStripItem[]
        {
            miSettings, new Forms.ToolStripSeparator(),
            miToggle, miCompact, miCorner, new Forms.ToolStripSeparator(),
            miSummary, miSaved, new Forms.ToolStripSeparator(),
            miExit,
        });
        _trayMenu.Opening += (_, _) =>
        {
            if (_window != null) miCompact.Checked = _window.Look.Compact;
            miToggle.Checked = _window?.IsVisible == true;
        };
        UpdateTrayLanguage();

        _tray = new Forms.NotifyIcon
        {
            Icon = TrayIconFactory.Create(),
            Text = "Pulse  (Ctrl+Shift+O)",
            ContextMenuStrip = _trayMenu,
            Visible = true,
        };
        _tray.MouseClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ToggleOverlay(); };
        _tray.MouseDoubleClick += (_, e) => { if (e.Button == Forms.MouseButtons.Left) ShowSettings(); };
        // the balloons worth clicking: a game summary, or "update available" / "updated" (the settings window shows those)
        _tray.BalloonTipClicked += (_, _) =>
        {
            if (_pendingSummary is { } summary) ShowSummary(summary);
            else if (_pendingUpdate != null || _whatsNew != null) ShowSettings();
        };
    }

    /// <summary>Every tray item keeps its string key in Tag; re-read them after a language switch.</summary>
    void UpdateTrayLanguage()
    {
        if (_trayMenu is null) return;
        _trayMenu.RightToLeft = Loc.Instance.Language == AppLanguage.Persian ? Forms.RightToLeft.Yes : Forms.RightToLeft.No;
        foreach (Forms.ToolStripItem item in _trayMenu.Items) Relabel(item);

        static void Relabel(Forms.ToolStripItem item)
        {
            if (item.Tag is string key) item.Text = Loc.T(key);
            if (item is Forms.ToolStripMenuItem mi)
                foreach (Forms.ToolStripItem sub in mi.DropDownItems) Relabel(sub);
        }
    }

    static Forms.ToolStripMenuItem Item(string key, Action onClick)
    {
        var item = new Forms.ToolStripMenuItem(Loc.T(key)) { Tag = key };
        item.Click += (_, _) => onClick();
        return item;
    }

    static Forms.ToolStripMenuItem Choice<T>(string key, (string Key, T Value)[] options, Func<T> current, Action<T> apply)
        where T : notnull
    {
        var parent = new Forms.ToolStripMenuItem(Loc.T(key)) { Tag = key };
        var values = new Dictionary<Forms.ToolStripMenuItem, T>();
        foreach (var (label, value) in options)
        {
            var item = Item(label, () => apply(value));
            values[item] = value;
            parent.DropDownItems.Add(item);
        }
        parent.DropDownOpening += (_, _) =>
        {
            foreach (var (item, value) in values) item.Checked = Equals(value, current());
        };
        return parent;
    }

    // ───────────────────────── plumbing ─────────────────────────

    void OnDisplayChanged(object? sender, EventArgs e) =>
        Dispatcher.InvokeAsync(() => _window?.Reposition());

    void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            File.AppendAllText(Path.Combine(AppSettings.Folder, "error.log"),
                $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {e.Exception}{Environment.NewLine}");
        }
        catch { /* never crash while logging a crash */ }
        e.Handled = true;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.DisplaySettingsChanged -= OnDisplayChanged;
        _showWait?.Unregister(null);
        _showSignal?.Dispose();
        _exiting = true;
        _timer?.Stop();
        _updateTimer?.Stop();
        _saveTimer?.Stop(); // the Save() below writes anything pending
        _hotkeys?.Dispose();

        if (_tray != null)
        {
            _tray.Visible = false;
            _tray.Icon?.Dispose();
            _tray.Dispose();
        }

        // a game still running: keep its session in the history (no window, we're leaving)
        if (_fps != null)
            foreach (var session in _fps.Sessions.FinishAll()) SessionHistory.Add(session);

        _fps?.Dispose();
        _sensors?.Dispose();
        _settings?.Save();

        if (_ownsMutex) _mutex?.ReleaseMutex();
        _mutex?.Dispose();

        base.OnExit(e);
    }
}
