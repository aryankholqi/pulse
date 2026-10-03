using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Pulse.Services;

/// <summary>One finished game session: what the summary window shows and the history file keeps.</summary>
public sealed record GameSession
{
    public string Game { get; init; } = "";
    public string Process { get; init; } = "";

    /// <summary>Where the game's .exe is: its icon on the game profiles page. Null in sessions recorded before 2.7.</summary>
    public string? ExePath { get; init; }
    public DateTime Started { get; init; }
    public DateTime Ended { get; init; }

    /// <summary>The player kept this summary: it stays in the history until they delete it.</summary>
    public bool Saved { get; init; }

    /// <summary>A benchmark run (Ctrl+Shift+B): a stretch of play the player timed, not a whole session.</summary>
    public bool Benchmark { get; init; }

    /// <summary>The player's name for a benchmark run ("DLSS Quality"), so runs can be told apart.</summary>
    public string? Label { get; init; }

    /// <summary>Time the game was in front and drawing: alt-tabbed and paused time left out.</summary>
    public double PlaySeconds { get; init; }
    public long Frames { get; init; }
    public double AvgFps { get; init; }
    public double? Low1 { get; init; }
    public double? Low01 { get; init; }
    public int Stutters { get; init; }

    /// <summary>Share of the judged play time (0–1). Null when PresentMon reported no GPU timings.</summary>
    public double? GpuBound { get; init; }
    public double? CpuBound { get; init; }
    public double? Capped { get; init; }

    // hardware: only sampled while the overlay or the settings window keeps the sensors running
    public string? GpuName { get; init; }
    public string? CpuName { get; init; }
    public float? GpuTempAvg { get; init; }
    public float? GpuTempMax { get; init; }
    public float? CpuTempAvg { get; init; }
    public float? CpuTempMax { get; init; }
    public float? GpuLoadAvg { get; init; }
    public float? CpuLoadAvg { get; init; }
    public float? VramPeakMb { get; init; }
    public float? VramTotalMb { get; init; }
    public float? RamAvgGb { get; init; }
    public float? RamPeakGb { get; init; }
    public float? RamTotalGb { get; init; }
    /// <summary>The sticks installed when the session was played; null if Windows wouldn't say.</summary>
    public MemoryModules? RamModules { get; init; }

    /// <summary>Average FPS per <see cref="TimelineSeconds"/> of play, oldest first.</summary>
    public float[] Timeline { get; init; } = Array.Empty<float>();
    public double TimelineSeconds { get; init; }
}

/// <summary>
/// Follows every game the player brings to the front and adds up its whole session, frame by
/// frame: fed from the PresentMon reader, so it sees every frame, overlay shown or not.
/// A session ends when the game's process exits.
/// </summary>
public sealed class SessionTracker
{
    /// <summary>
    /// A game that filled its monitor gets a summary after even a short run: every launch counts.
    /// A window that never did might be any app that draws, so it needs longer to count as a game.
    /// </summary>
    public const double MinFullscreenPlaySeconds = 10;
    public const double MinPlaySeconds = 120;
    /// <summary>A benchmark stopped sooner than this has too few frames to say anything.</summary>
    public const double MinBenchmarkSeconds = 5;
    const int MaxLive = 8;
    const double PlayingSeconds = 2;   // "is it drawing right now?" look-back
    // "is the player still in it?" look-back: a loading screen or a hitch can stop a game drawing for seconds,
    // and a window popped over it then takes its focus, which some games (The Last of Us Part II) don't survive
    const double InGameSeconds = 30;

    // Apps that draw frames in front of the player but aren't games.
    static readonly HashSet<string> NotGames = new(StringComparer.OrdinalIgnoreCase)
    {
        // browsers
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "arc", "iexplore", "msedgewebview2",
        // chat and calls
        "discord", "slack", "teams", "ms-teams", "telegram", "whatsapp", "zoom", "skype",
        // launchers and stores
        "steam", "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "eadesktop", "origin",
        "upc", "ubisoftconnect", "galaxyclient", "riotclientservices", "riotclientux", "xboxpcapp", "gamingservices",
        "gamebar", "playnite.desktopapp", "playnite.fullscreenapp",
        // media, streaming, creative
        "spotify", "vlc", "mpc-hc64", "mpc-be64", "potplayermini64", "obs64", "obs32", "streamlabs obs", "photoshop",
        "premiere pro", "resolve", "afterfx",
        // dev tools and Windows itself
        "code", "devenv", "rider64", "idea64", "explorer", "searchhost", "startmenuexperiencehost",
        "shellexperiencehost", "textinputhost", "applicationframehost", "systemsettings", "lockapp", "taskmgr",
        "nvcontainer", "nvidia overlay", "radeonsoftware", "amdrsserv",
    };

    readonly object _gate = new();
    readonly Dictionary<int, SessionRecorder> _live = new();
    readonly HashSet<int> _ignored = new();      // pids checked once and found not to be games
    volatile int _foreground;
    IntPtr _foregroundWindow;                    // UI thread only
    volatile int _refreshHz;

    // A benchmark: a second recorder on one game, fed alongside its session. Guarded by _gate.
    SessionRecorder? _bench;
    int _benchPid;

    /// <summary>Refresh rate of the game's monitor (0 = unknown), set from the UI tick; tells V-Sync apart.</summary>
    internal int RefreshHz { set => _refreshHz = value; }

    /// <summary>PresentMon reader thread: one presented frame.</summary>
    internal void OnFrame(int pid, double ms, double gpuBusyMs, double cpuWaitMs)
    {
        lock (_gate)
        {
            // only what the player sees: frames drawn while the game is in front
            bool front = pid == _foreground;
            if (_live.TryGetValue(pid, out var rec))
            {
                if (front) rec.Add(ms, gpuBusyMs, cpuWaitMs, _refreshHz);
                else rec.Gap();
            }
            if (_bench != null && pid == _benchPid)
            {
                if (front) _bench.Add(ms, gpuBusyMs, cpuWaitMs, _refreshHz);
                else _bench.Gap();
            }
        }
    }

    /// <summary>
    /// UI tick: <paramref name="target"/> is the process the overlay follows. Once it's also the
    /// foreground window, it becomes a session (unless it's a known non-game).
    /// </summary>
    internal void Follow(int foreground, int target, IntPtr window)
    {
        _foreground = foreground;
        _foregroundWindow = window;
        if (target == 0 || target != foreground) return;

        SessionRecorder? live;
        lock (_gate)
        {
            if (_ignored.Contains(target)) return;
            _live.TryGetValue(target, out live);
        }
        if (live != null)
        {
            if (!live.Fullscreen && Native.CoversMonitor(window)) live.Fullscreen = true;

            // A new window of the same game (a splash screen gives way to the game itself): take its title.
            if (window != IntPtr.Zero && window != live.Window)
            {
                live.Window = window;
                if (TitleOf(window) is { } newTitle) live.Game = newTitle;
            }
            return;
        }

        string name;
        try { using var p = System.Diagnostics.Process.GetProcessById(target); name = p.ProcessName; }
        catch { return; }

        if (NotGames.Contains(name))
        {
            lock (_gate)
            {
                if (_ignored.Count > 256) _ignored.Clear();
                _ignored.Add(target);
            }
            return;
        }

        // The window title is the name players know ("Red Dead Redemption 2", not "RDR2").
        var rec = new SessionRecorder(name, TitleOf(window) ?? name)
        {
            Window = window,
            Fullscreen = Native.CoversMonitor(window),
            ExePath = Native.ExePathOf(target),
        };

        lock (_gate)
        {
            if (_live.ContainsKey(target)) return;
            if (_live.Count >= MaxLive) EvictLeastPlayed();
            _live[target] = rec;
        }
    }

    /// <summary>UI tick, while the sensors run: temperatures and loads count toward the game in front.</summary>
    public void Sample(HardwareSnapshot hw)
    {
        lock (_gate)
        {
            if (_live.TryGetValue(_foreground, out var rec) && rec.IsPlaying(PlayingSeconds))
                rec.Sample(hw);
            if (_bench != null && _benchPid == _foreground && _bench.IsPlaying(PlayingSeconds))
                _bench.Sample(hw);
        }
    }

    // ───────────────────────── benchmark ─────────────────────────

    /// <summary>
    /// UI thread: start a benchmark on the game in front. False when there's no game in front
    /// drawing frames, or a benchmark is already running.
    /// </summary>
    public bool StartBenchmark()
    {
        lock (_gate)
        {
            if (_bench != null) return false;
            if (!_live.TryGetValue(_foreground, out var game) || !game.IsPlaying(PlayingSeconds)) return false;

            _bench = new SessionRecorder(game.Process, game.Game, benchmark: true)
            {
                Window = game.Window,
                Fullscreen = game.Fullscreen,
                ExePath = game.ExePath,
            };
            _benchPid = _foreground;
            return true;
        }
    }

    public bool BenchmarkRunning
    {
        get { lock (_gate) return _bench != null; }
    }

    /// <summary>Play time the running benchmark has recorded: time away from the game doesn't count.</summary>
    public double BenchmarkSeconds
    {
        get { lock (_gate) return _bench is null ? 0 : _bench.PlayMs / 1000; }
    }

    /// <summary>UI thread: end the benchmark. Null when none was running or it was too short to judge.</summary>
    public GameSession? StopBenchmark()
    {
        lock (_gate) return FinishBenchmark();
    }

    // Caller holds _gate.
    GameSession? FinishBenchmark()
    {
        var bench = _bench;
        _bench = null;
        _benchPid = 0;
        return bench?.Finish();
    }

    /// <summary>
    /// UI thread: a game other than the ones that just ended is in front and drawing — don't pop a window over it.
    /// Only a window that fills its monitor counts: an app that merely draws (a terminal, an editor, a chat
    /// app not in <see cref="NotGames"/>) mustn't hold a summary back.
    /// </summary>
    public bool GameInFront
    {
        get
        {
            lock (_gate)
            {
                if (!_live.TryGetValue(_foreground, out var rec) || !rec.IsPlaying(InGameSeconds)) return false;
            }
            return Native.CoversMonitor(_foregroundWindow);
        }
    }

    /// <summary>
    /// UI thread: a game is in front and drawing, windowed or not. A benchmark's result waits for the
    /// player to leave even a windowed game: they started the run from inside it, so they're playing.
    /// </summary>
    public bool PlayingInFront
    {
        get
        {
            lock (_gate) return _live.TryGetValue(_foreground, out var rec) && rec.IsPlaying(InGameSeconds);
        }
    }

    /// <summary>UI thread: the pid of the game in front (0 = none), drawing or not.</summary>
    public int GameInFrontPid
    {
        get { lock (_gate) return _live.ContainsKey(_foreground) ? _foreground : 0; }
    }

    /// <summary>Is this game still running? Alt-tabbed out of counts: its session isn't over.</summary>
    public bool IsLive(int pid)
    {
        lock (_gate) return pid != 0 && _live.ContainsKey(pid);
    }

    /// <summary>Sessions whose game has exited, long enough to be worth a summary. Oldest first.</summary>
    public List<GameSession> TakeEnded()
    {
        var ended = new List<GameSession>();
        lock (_gate)
        {
            if (_live.Count == 0 && _ignored.Count == 0 && _bench is null) return ended;

            var running = Native.RunningProcessIds();
            if (running is null) return ended; // couldn't tell: ask again next time

            // Windows hands out ids of closed processes again: a relaunched game may get the id of
            // an app ignored earlier, and must not be ignored along with it.
            _ignored.RemoveWhere(pid => !running.Contains(pid));

            // the game closed mid-benchmark: the run ends with it, with whatever it recorded
            if (_bench != null && !running.Contains(_benchPid) && FinishBenchmark() is { } run) ended.Add(run);

            List<int>? gone = null;
            foreach (var pid in _live.Keys)
                if (!running.Contains(pid)) (gone ??= new()).Add(pid);
            if (gone is null) return ended; // (a benchmark that just ended is in it)

            foreach (var pid in gone)
            {
                if (_live.Remove(pid, out var rec) && rec.Finish() is { } session) ended.Add(session);
            }
        }
        ended.Sort((a, b) => a.Ended.CompareTo(b.Ended));
        return ended;
    }

    /// <summary>Pulse is closing: wrap up whatever is still being played.</summary>
    public List<GameSession> FinishAll()
    {
        var ended = new List<GameSession>();
        lock (_gate)
        {
            if (FinishBenchmark() is { } run) ended.Add(run);
            foreach (var rec in _live.Values)
                if (rec.Finish() is { } session) ended.Add(session);
            _live.Clear();
        }
        return ended;
    }

    static string? TitleOf(IntPtr window)
    {
        string title = window != IntPtr.Zero ? Native.GetWindowTitle(window).Trim() : "";
        return title.Length is > 0 and <= 80 ? title : null;
    }

    // Caller holds _gate.
    void EvictLeastPlayed()
    {
        int victim = 0;
        double least = double.MaxValue;
        foreach (var (pid, rec) in _live)
            if (rec.PlayMs < least) { least = rec.PlayMs; victim = pid; }
        _live.Remove(victim);
    }
}

/// <summary>
/// Adds up one game's session in constant memory: a log-scale histogram of frame times (for the
/// 1% / 0.1% lows over hours of play), stutters, the bottleneck verdict per ~2 s, an FPS timeline
/// and hardware samples. Not thread-safe: <see cref="SessionTracker"/> holds its lock.
/// </summary>
internal sealed class SessionRecorder
{
    // histogram: 0.25–5000 ms in 2048 log steps, ~0.5% apart
    const int Buckets = 2048;
    const double MinMs = 0.25, MaxMs = 5000;
    static readonly double LogMin = Math.Log(MinMs);
    static readonly double Scale = Buckets / (Math.Log(MaxMs) - LogMin);

    const double TimelineBucketMs = 5000;
    const double BenchTimelineBucketMs = 1000; // a run lasts a minute or two: every second is worth a point
    const int TimelinePoints = 240;
    const double BoundChunkMs = 2000;   // same window the overlay judges over
    const int BoundMinFrames = 10;

    // A stutter: a frame far longer than the ones around it, and long enough to feel.
    const double StutterFactor = 2.5;
    const double StutterMinExtraMs = 8;
    const double StutterMinMs = 33;       // under 30 FPS for that frame: quick spikes at high FPS aren't felt
    const int StutterWarmup = 30;

    readonly string _process;
    readonly DateTime _started = DateTime.Now;
    long _lastFrame;                      // Stopwatch ticks

    readonly int[] _hist = new int[Buckets];
    long _frames;
    double _playMs;
    bool _gap = true;                     // the next frame follows a pause: its duration isn't play time

    double _avgMs;                        // recent frame time (EMA), for stutters
    int _warm;
    int _stutters;

    BoundWindow _chunk;
    double _chunkMs;
    Bottleneck? _verdict;
    double _gpuMs, _cpuMs, _cappedMs;

    readonly List<float> _timeline = new();
    double _bucketMs;
    int _bucketFrames;

    // hardware
    string? _gpuName, _cpuName;
    double _gpuTempSum, _cpuTempSum, _gpuLoadSum, _cpuLoadSum;
    int _gpuTempN, _cpuTempN, _gpuLoadN, _cpuLoadN;
    float? _gpuTempMax, _cpuTempMax, _vramPeak, _vramTotal;
    double _ramSum;
    int _ramN;
    float? _ramPeak, _ramTotal;

    readonly bool _benchmark;
    readonly double _timelineBucketMs;

    public SessionRecorder(string process, string game, bool benchmark = false)
    {
        _process = process;
        Game = game;
        _benchmark = benchmark;
        _timelineBucketMs = benchmark ? BenchTimelineBucketMs : TimelineBucketMs;
    }

    public string Process => _process;

    /// <summary>The name shown in the summary: the title of the game's latest window. Set on the UI thread.</summary>
    public volatile string Game;
    /// <summary>The window <see cref="Game"/> was read from. UI thread only.</summary>
    public IntPtr Window;
    /// <summary>The game's .exe, read once when the session starts.</summary>
    public string? ExePath;
    /// <summary>The game filled its monitor at some point (fullscreen or borderless). Set on the UI thread.</summary>
    public volatile bool Fullscreen;

    public double PlayMs => _playMs;

    public bool IsPlaying(double seconds) =>
        _lastFrame != 0 && Stopwatch.GetTimestamp() - _lastFrame < Stopwatch.Frequency * seconds;

    /// <summary>The game left the foreground (or Pulse lost sight of it): the next frame starts fresh.</summary>
    public void Gap()
    {
        _gap = true;
        _chunk = default;
        _chunkMs = 0;
    }

    public void Add(double ms, double gpuBusyMs, double cpuWaitMs, int refreshHz)
    {
        _lastFrame = Stopwatch.GetTimestamp();

        // Back in front after a pause: this frame's duration covers the time away.
        if (_gap) { _gap = false; return; }

        _frames++;
        _playMs += ms;
        _hist[Bucket(ms)]++;

        // stutters
        if (_warm < StutterWarmup) { _avgMs = _warm == 0 ? ms : _avgMs + (ms - _avgMs) / (_warm + 1); _warm++; }
        else
        {
            if (ms >= _avgMs * StutterFactor && ms - _avgMs >= StutterMinExtraMs && ms >= StutterMinMs && ms < 1000) _stutters++;
            _avgMs += (Math.Min(ms, _avgMs * 4) - _avgMs) * 0.05; // a spike nudges the average, it doesn't reset it
        }

        // what limited the frame rate, judged every ~2 s of play
        _chunk.Add(ms, gpuBusyMs, cpuWaitMs);
        _chunkMs += ms;
        if (_chunkMs >= BoundChunkMs)
        {
            if (_chunk.Frames >= BoundMinFrames)
            {
                _verdict = _chunk.Classify(_verdict, refreshHz);
                switch (_verdict)
                {
                    case Bottleneck.Gpu: _gpuMs += _chunkMs; break;
                    case Bottleneck.Cpu: _cpuMs += _chunkMs; break;
                    case Bottleneck.Capped: _cappedMs += _chunkMs; break;
                }
            }
            _chunk = default;
            _chunkMs = 0;
        }

        // timeline: average FPS per 5 s of play (1 s in a benchmark)
        _bucketMs += ms;
        _bucketFrames++;
        if (_bucketMs >= _timelineBucketMs)
        {
            _timeline.Add((float)(_bucketFrames * 1000.0 / _bucketMs));
            _bucketMs = 0;
            _bucketFrames = 0;
        }
    }

    public void Sample(HardwareSnapshot hw)
    {
        _gpuName ??= hw.GpuName;
        _cpuName ??= hw.CpuName;
        if (hw.GpuTemp is float gt) { _gpuTempSum += gt; _gpuTempN++; _gpuTempMax = Math.Max(_gpuTempMax ?? gt, gt); }
        if (hw.CpuTemp is float ct) { _cpuTempSum += ct; _cpuTempN++; _cpuTempMax = Math.Max(_cpuTempMax ?? ct, ct); }
        if (hw.GpuLoad is float gl) { _gpuLoadSum += gl; _gpuLoadN++; }
        if (hw.CpuLoad is float cl) { _cpuLoadSum += cl; _cpuLoadN++; }
        if (hw.VramUsedMb is float vu) _vramPeak = Math.Max(_vramPeak ?? vu, vu);
        if (hw.VramTotalMb is float vt && vt > 0) _vramTotal = vt;
        if (hw.RamUsedGb is float ru) { _ramSum += ru; _ramN++; _ramPeak = Math.Max(_ramPeak ?? ru, ru); }
        if (hw.RamTotalGb is float rt && rt > 0) _ramTotal = rt;
    }

    /// <summary>The summary, or null when the game wasn't played long enough.</summary>
    public GameSession? Finish()
    {
        // a benchmark was started by hand on a game: only too few frames rule it out
        double minSeconds = _benchmark ? SessionTracker.MinBenchmarkSeconds
                          : Fullscreen ? SessionTracker.MinFullscreenPlaySeconds : SessionTracker.MinPlaySeconds;
        if (_playMs < minSeconds * 1000 || _frames < 100) return null;

        double judged = _gpuMs + _cpuMs + _cappedMs;
        bool haveBound = judged >= _playMs * 0.2; // mostly unjudged (no GPU timings): don't guess

        // timeline: keep the partial last bucket if it's meaningful, then fit into TimelinePoints
        var points = new List<float>(_timeline);
        if (_bucketMs >= _timelineBucketMs / 2) points.Add((float)(_bucketFrames * 1000.0 / _bucketMs));
        int merge = Math.Max(1, (int)Math.Ceiling(points.Count / (double)TimelinePoints));
        var timeline = new float[(points.Count + merge - 1) / merge];
        for (int i = 0; i < timeline.Length; i++)
        {
            double sum = 0;
            int n = 0;
            for (int j = i * merge; j < Math.Min(points.Count, (i + 1) * merge); j++) { sum += points[j]; n++; }
            timeline[i] = (float)(sum / n);
        }

        return new GameSession
        {
            Game = Game,
            Process = _process,
            ExePath = ExePath,
            Started = _started,
            Ended = DateTime.Now,
            Benchmark = _benchmark,
            Saved = _benchmark, // runs are made to be compared later: kept until the player deletes them
            PlaySeconds = _playMs / 1000,
            Frames = _frames,
            AvgFps = _frames * 1000.0 / _playMs,
            Low1 = _frames >= 100 ? 1000.0 / Percentile(0.99) : null,
            Low01 = _frames >= 1000 ? 1000.0 / Percentile(0.999) : null,
            Stutters = _stutters,
            GpuBound = haveBound ? _gpuMs / judged : null,
            CpuBound = haveBound ? _cpuMs / judged : null,
            Capped = haveBound ? _cappedMs / judged : null,
            GpuName = _gpuName,
            CpuName = _cpuName,
            GpuTempAvg = _gpuTempN > 0 ? (float)(_gpuTempSum / _gpuTempN) : null,
            GpuTempMax = _gpuTempMax,
            CpuTempAvg = _cpuTempN > 0 ? (float)(_cpuTempSum / _cpuTempN) : null,
            CpuTempMax = _cpuTempMax,
            GpuLoadAvg = _gpuLoadN > 0 ? (float)(_gpuLoadSum / _gpuLoadN) : null,
            CpuLoadAvg = _cpuLoadN > 0 ? (float)(_cpuLoadSum / _cpuLoadN) : null,
            VramPeakMb = _vramPeak,
            VramTotalMb = _vramTotal,
            RamAvgGb = _ramN > 0 ? (float)(_ramSum / _ramN) : null,
            RamPeakGb = _ramPeak,
            RamTotalGb = _ramTotal,
            RamModules = MemoryModules.Current,
            Timeline = timeline,
            TimelineSeconds = _timelineBucketMs / 1000 * merge,
        };
    }

    static int Bucket(double ms) =>
        Math.Clamp((int)((Math.Log(Math.Max(ms, MinMs)) - LogMin) * Scale), 0, Buckets - 1);

    /// <summary>The frame time at quantile <paramref name="q"/> (0.99 → only 1% of frames were slower).</summary>
    double Percentile(double q)
    {
        long slower = (long)Math.Ceiling(_frames * (1 - q)); // this many frames are at or above the answer
        long seen = 0;
        for (int i = Buckets - 1; i >= 0; i--)
        {
            seen += _hist[i];
            if (seen >= slower) return Math.Exp(LogMin + (i + 0.5) / Scale);
        }
        return Math.Exp(LogMin + 0.5 / Scale);
    }
}
