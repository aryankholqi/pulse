using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Pulse.Services;

/// <summary>
/// The Boost page's work. While a game fills the screen: a faster power plan, high priority for the game,
/// quieter background apps, and (dual-CCD Ryzen X3D) the game kept on its V-cache cores. Each is undone when
/// the game closes, its switch goes off, or Pulse quits. Also the NVIDIA power limit, and why the GPU's clocks
/// dropped. UI thread only.
/// </summary>
internal sealed class GameBoost : IDisposable
{
    public enum Tweak { PowerPlan, Priority, Background, VCache }
    static readonly Tweak[] AllTweaks = { Tweak.PowerPlan, Tweak.Priority, Tweak.Background, Tweak.VCache };

    static readonly Guid HighPerformance = new("8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c");
    static readonly Guid UltimatePerformance = new("e9a42b02-d5df-448d-aa00-03f14749eb61");
    // Settings → Power mode → "Best performance": all a Modern Standby laptop has instead of High performance.
    static readonly Guid BestPerformanceOverlay = new("ded574b5-45a0-4f42-8737-46345c09c238");

    // Lowered while a game runs. No chat apps: a starved voice call crackles.
    static readonly HashSet<string> BackgroundApps = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi",
        "steamwebhelper", "epicgameslauncher", "epicwebhelper", "battle.net", "eadesktop", "upc", "galaxyclient",
        "onedrive", "spotify",
    };

    // nvmlClocksThrottleReason* bits
    public const ulong ReasonIdle = 0x1, ReasonPower = 0x4, ReasonThermal = 0x20 | 0x40, ReasonHardware = 0x8 | 0x80;
    const int ThrottleWindow = 30; // samples, one per ~2 s: the last minute

    /// <summary>Highest curve offset offered, in MHz: enough for an undervolt, short of a risky overclock.</summary>
    public const int MaxOffset = 150;

    // other apps that write the same NVIDIA clock settings (process name, shown name)
    static readonly (string Process, string Name)[] OtherTuners =
    {
        ("MSIAfterburner", "MSI Afterburner"),
        ("PrecisionX_x64", "EVGA Precision X1"),
    };

    readonly AppSettings _settings;
    readonly Task<Nvml?> _nvml = Task.Run(() => Nvml.Open(""));   // "": the only NVIDIA card (NVML lists no others)
    bool _powerPending;

    SessionTracker? _sessions;
    int _pid;
    readonly HashSet<Tweak> _on = new(), _blocked = new();
    ProcessPriorityClass _oldPriority;
    IntPtr _oldAffinity;
    readonly Dictionary<int, string> _calmed = new();
    int _calmTick;

    readonly Queue<(ulong Reasons, float Clock)> _samples = new();

    // undervolt: offset + clock cap, on trial until the player keeps it
    bool _tuningPending, _tuningApplied;
    DateTime _tuningSince;
    int _watchTick;

    public GameBoost(AppSettings settings)
    {
        _settings = settings;
        _powerPending = settings.GpuPowerLimit != null;
        RestorePlan(); // Pulse closed mid-game last time (crash, shutdown): put the user's plan back

        // clocks never tested in a game: a restart (maybe the crash they caused) drops them
        if (!settings.GpuTuningTested && (settings.GpuClockOffset != 0 || settings.GpuClockCap != null))
        {
            settings.GpuClockOffset = 0;
            settings.GpuClockCap = null;
            settings.Save();
            TuningDroppedAtStart = true;
        }
        // dropped ones may still be on the card (Pulse itself crashed, not the driver): put it back to stock too
        _tuningPending = TuningDroppedAtStart || settings.GpuClockOffset != 0 || settings.GpuClockCap != null;
    }

    /// <summary>The name of another clock tool that's running (it and Pulse would overwrite each other), or null.</summary>
    public static string? OtherTunerRunning()
    {
        foreach (var (process, name) in OtherTuners)
        {
            var found = Process.GetProcessesByName(process);
            foreach (var p in found) p.Dispose();
            if (found.Length > 0) return name;
        }
        return null;
    }

    // ───────────────────────── status, for the page ─────────────────────────

    /// <summary>The boosted game's process name (no ".exe"), or null while no game runs.</summary>
    public string? Game { get; private set; }
    public DateTime Since { get; private set; }
    public IReadOnlyCollection<Tweak> Active => _on;
    /// <summary>Switched on but refused, e.g. an anti-cheat that won't let its game be touched.</summary>
    public IReadOnlyCollection<Tweak> Blocked => _blocked;
    public int CalmedApps => _calmed.Count;

    /// <summary>The V-cache die's threads on a CPU with two different L3 caches, or null (one die, or equal dies).</summary>
    public static VCacheInfo? VCache { get; } = FindVCache();

    public Nvml? Gpu => _nvml.IsCompletedSuccessfully ? _nvml.Result : null;
    /// <summary>Still opening NVML: the GPU panels wait instead of saying "not supported".</summary>
    public bool GpuPending => !_nvml.IsCompleted;

    // ───────────────────────── tick (every ~2 s) ─────────────────────────

    public void Tick(SessionTracker sessions)
    {
        _sessions = sessions;
        Apply();
        SampleThrottle();
        if (_powerPending && !GpuPending)
        {
            _powerPending = false;
            if (_settings.GpuPowerLimit is { } pct) SetPowerLimit(pct);
        }
        if (_tuningPending && !GpuPending)
        {
            _tuningPending = false;
            ApplyTuning();
        }
        // every ~10 s on custom clocks: did the driver crash and recover?
        if (_tuningApplied && ++_watchTick % 5 == 0 && DriverCrashedSince(_tuningSince))
        {
            ResetTuning();
            TuningCrashedAt = DateTime.Now;
        }
    }

    /// <summary>Follow the game and the switches. Also called right after a switch flips.</summary>
    public void Apply()
    {
        if (_sessions is not { } s) return;

        // a boosted game stays boosted while it runs, alt-tabbed or not; a new one must fill the screen
        int pid = _pid != 0 && s.IsLive(_pid) ? _pid : s.GameInFront ? s.GameInFrontPid : 0;
        if (pid != _pid)
        {
            End();
            if (pid != 0) Begin(pid);
        }
        if (_pid == 0) return;

        foreach (var t in AllTweaks)
        {
            bool want = Wanted(t);
            if (want && !_on.Contains(t) && !_blocked.Contains(t))
            {
                if (Start(t)) _on.Add(t);
                else _blocked.Add(t);
            }
            else if (!want)
            {
                if (_on.Remove(t)) Stop(t);
                _blocked.Remove(t);
            }
        }

        // browsers opened mid-game get lowered too, every ~30 s
        if (_on.Contains(Tweak.Background) && ++_calmTick % 15 == 0) Calm();
    }

    bool Wanted(Tweak t) => t switch
    {
        Tweak.PowerPlan => _settings.BoostPowerPlan,
        Tweak.Priority => _settings.BoostPriority,
        Tweak.Background => _settings.BoostBackground,
        Tweak.VCache => _settings.BoostVCache && VCache != null,
        _ => false,
    };

    void Begin(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            Game = p.ProcessName;
        }
        catch { return; } // gone already
        _pid = pid;
        Since = DateTime.Now;
        _calmTick = 0;
    }

    void End()
    {
        foreach (var t in _on) Stop(t);
        _on.Clear();
        _blocked.Clear();
        _pid = 0;
        Game = null;
    }

    bool Start(Tweak t)
    {
        switch (t)
        {
            case Tweak.PowerPlan: return RaisePlan();
            case Tweak.Background: Calm(); return true;
            case Tweak.Priority:
                return WithGame(p => { _oldPriority = p.PriorityClass; p.PriorityClass = ProcessPriorityClass.High; });
            case Tweak.VCache:
                return WithGame(p => { _oldAffinity = p.ProcessorAffinity; p.ProcessorAffinity = (IntPtr)(long)VCache!.Mask; });
        }
        return false;
    }

    void Stop(Tweak t)
    {
        switch (t)
        {
            case Tweak.PowerPlan: RestorePlan(); break;
            case Tweak.Background: Uncalm(); break;
            case Tweak.Priority: WithGame(p => p.PriorityClass = _oldPriority); break;   // the game may have closed: fine
            case Tweak.VCache: WithGame(p => p.ProcessorAffinity = _oldAffinity); break;
        }
    }

    bool WithGame(Action<Process> act)
    {
        try
        {
            using var p = Process.GetProcessById(_pid);
            act(p);
            return true;
        }
        catch { return false; } // closed, or protected by its anti-cheat
    }

    // ── power plan ──

    bool RaisePlan()
    {
        if (ActiveScheme() is not { } active) return false;
        if (active == HighPerformance || active == UltimatePerformance) return true; // already fast: nothing to undo

        var high = HighPerformance;
        if (PowerSetActiveScheme(IntPtr.Zero, ref high) == 0)
            _settings.BoostRestorePlan = active;
        else
        {
            // no High performance plan (Modern Standby): the "Best performance" power mode instead
            try
            {
                if (PowerGetEffectiveOverlayScheme(out var mode) != 0) return false;
                if (mode == BestPerformanceOverlay) return true;
                if (PowerSetActiveOverlayScheme(BestPerformanceOverlay) != 0) return false;
                _settings.BoostRestoreMode = mode;
            }
            catch { return false; } // Windows without power modes
        }
        _settings.Save(); // written now: a crash mid-game still finds the plan to put back
        return true;
    }

    void RestorePlan()
    {
        if (_settings.BoostRestorePlan is null && _settings.BoostRestoreMode is null) return;
        if (_settings.BoostRestorePlan is { } plan) PowerSetActiveScheme(IntPtr.Zero, ref plan);
        if (_settings.BoostRestoreMode is { } mode) try { PowerSetActiveOverlayScheme(mode); } catch { }
        _settings.BoostRestorePlan = null;
        _settings.BoostRestoreMode = null;
        _settings.Save();
    }

    static Guid? ActiveScheme()
    {
        if (PowerGetActiveScheme(IntPtr.Zero, out var ptr) != 0) return null;
        try { return Marshal.PtrToStructure<Guid>(ptr); }
        finally { LocalFree(ptr); }
    }

    // ── background apps ──

    void Calm()
    {
        foreach (var p in Process.GetProcesses())
        {
            using (p)
            {
                if (_calmed.ContainsKey(p.Id) || !BackgroundApps.Contains(p.ProcessName)) continue;
                try
                {
                    // only apps at the default: one the user raised on purpose stays raised
                    if (p.PriorityClass != ProcessPriorityClass.Normal) continue;
                    p.PriorityClass = ProcessPriorityClass.BelowNormal;
                    _calmed[p.Id] = p.ProcessName;
                }
                catch { /* exited, or not ours to touch */ }
            }
        }
    }

    void Uncalm()
    {
        foreach (var (pid, name) in _calmed)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                // the id may belong to another app by now
                if (p.ProcessName == name && p.PriorityClass == ProcessPriorityClass.BelowNormal)
                    p.PriorityClass = ProcessPriorityClass.Normal;
            }
            catch { }
        }
        _calmed.Clear();
    }

    // ───────────────────────── GPU ─────────────────────────

    void SampleThrottle()
    {
        if (Gpu is not { } gpu || gpu.ThrottleReasons is not { } r) return;
        _samples.Enqueue((r, gpu.GraphicsClock ?? 0));
        while (_samples.Count > ThrottleWindow) _samples.Dequeue();
    }

    /// <summary>Why the clocks are down right now, or null without NVML.</summary>
    public ulong? ReadReasons() => Gpu?.ThrottleReasons;

    /// <summary>Share (0–1) of the last minute's busy samples with any of <paramref name="bits"/>; null if the GPU sat idle.</summary>
    public double? ThrottleShare(ulong bits)
    {
        int busy = 0, hit = 0;
        foreach (var (r, _) in _samples)
        {
            if ((r & ReasonIdle) != 0) continue;
            busy++;
            if ((r & bits) != 0) hit++;
        }
        return busy == 0 ? null : (double)hit / busy;
    }

    /// <summary>Percent of the card's stock limit, clamped to what the driver allows. False if the driver refused.</summary>
    public bool SetPowerLimit(int percent)
    {
        if (Gpu is not { } gpu || gpu.PowerLimits is not { } l) return false;
        return gpu.SetPowerLimit(Math.Clamp(l.Stock * percent / 100f, l.Min, l.Max));
    }

    // ───────────────────────── undervolt ─────────────────────────

    /// <summary>Untested clocks from last time were dropped at start (a crash or restart came first).</summary>
    public bool TuningDroppedAtStart { get; private set; }
    /// <summary>When the driver crashed on custom clocks and Pulse put the card back to stock.</summary>
    public DateTime? TuningCrashedAt { get; private set; }
    public bool TuningApplied => _tuningApplied;
    public DateTime TuningSince => _tuningSince;

    /// <summary>The card's usual clock under load in the last minute (median), or null if it sat idle.</summary>
    public float? GameClock
    {
        get
        {
            var busy = _samples.Where(s => (s.Reasons & ReasonIdle) == 0 && s.Clock > 0).Select(s => s.Clock).OrderBy(c => c).ToList();
            return busy.Count == 0 ? null : busy[busy.Count / 2];
        }
    }

    /// <summary>Puts the settings' offset and cap on the card. False if the driver refused part of it.</summary>
    public bool ApplyTuning()
    {
        if (Gpu is not { } gpu) return false;
        int offset = _settings.GpuClockCap == null ? 0 : Math.Min(_settings.GpuClockOffset, MaxOffset); // no cap: no offset
        bool ok = gpu.ClockOffsetRange is null ? offset == 0 : gpu.SetClockOffset(offset);
        ok &= gpu.CapClock(_settings.GpuClockCap);

        bool custom = offset != 0 || _settings.GpuClockCap != null;
        if (custom && !_tuningApplied) _watchTick = 0;
        if (custom) _tuningSince = DateTime.Now; // each change is a new trial, watched from now
        _tuningApplied = custom;
        TuningCrashedAt = null;
        return ok;
    }

    /// <summary>Stock clocks, now and from now on.</summary>
    public void ResetTuning()
    {
        _settings.GpuClockOffset = 0;
        _settings.GpuClockCap = null;
        _settings.GpuTuningTested = false;
        _settings.Save();
        if (Gpu is { } gpu)
        {
            if (gpu.ClockOffsetRange != null) gpu.SetClockOffset(0);
            gpu.CapClock(null);
        }
        _tuningApplied = false;
    }

    static bool DriverCrashedSince(DateTime since)
    {
        try
        {
            // "Display driver nvlddmkm stopped responding and has successfully recovered"
            var query = new EventLogQuery("System", PathType.LogName,
                $"*[System[Provider[@Name='Display'] and EventID=4101 and TimeCreated[@SystemTime>='{since.ToUniversalTime():yyyy-MM-ddTHH:mm:ss.fffZ}']]]");
            using var reader = new EventLogReader(query);
            using var e = reader.ReadEvent();
            return e != null;
        }
        catch { return false; }
    }

    // ───────────────────────── quit ─────────────────────────

    public void Dispose()
    {
        End();
        RestorePlan();
        // Pulse off = stock card: the limit only holds while Pulse runs
        if (_settings.GpuPowerLimit != null && Gpu is { } gpu && gpu.PowerLimits is { } l) gpu.SetPowerLimit(l.Stock);
        if (_tuningApplied && Gpu is { } card)
        {
            if (card.ClockOffsetRange != null) card.SetClockOffset(0);
            card.CapClock(null);
        }
        Gpu?.Dispose();
    }

    // ───────────────────────── V-cache ─────────────────────────

    public sealed record VCacheInfo(ulong Mask, uint CacheMb, uint OtherCacheMb)
    {
        public int Threads => BitOperations.PopCount(Mask);
        public int FirstThread => BitOperations.TrailingZeroCount(Mask);
    }

    static VCacheInfo? FindVCache()
    {
        try
        {
            uint len = 0;
            GetLogicalProcessorInformationEx(RelationCache, IntPtr.Zero, ref len);
            if (len == 0) return null;
            var buf = Marshal.AllocHGlobal((int)len);
            try
            {
                if (!GetLogicalProcessorInformationEx(RelationCache, buf, ref len)) return null;
                var l3 = new List<(uint Size, ulong Mask)>();
                // SYSTEM_LOGICAL_PROCESSOR_INFORMATION_EX: Relationship @0, Size @4, CACHE_RELATIONSHIP @8
                // (Level @8, CacheSize @12, GroupMask.Mask @40, GroupMask.Group @48)
                for (int off = 0; off < len; off += Marshal.ReadInt32(buf, off + 4))
                {
                    if (Marshal.ReadByte(buf, off + 8) == 3 && Marshal.ReadInt16(buf, off + 48) == 0)
                        l3.Add(((uint)Marshal.ReadInt32(buf, off + 12), (ulong)Marshal.ReadInt64(buf, off + 40)));
                }
                return PickVCache(l3);
            }
            finally { Marshal.FreeHGlobal(buf); }
        }
        catch { return null; }
    }

    /// <summary>The L3 that is bigger than the others: the V-cache die. Equal dies (a 7950X) have none to prefer.</summary>
    internal static VCacheInfo? PickVCache(List<(uint Size, ulong Mask)> l3)
    {
        if (l3.Count < 2) return null;
        l3.Sort((a, b) => b.Size.CompareTo(a.Size));
        if (l3[0].Size <= l3[1].Size || l3[0].Mask == 0) return null;
        return new VCacheInfo(l3[0].Mask, l3[0].Size >> 20, l3[1].Size >> 20);
    }

    [Conditional("DEBUG")]
    internal static void SelfCheck()
    {
        const uint MB = 1 << 20;
        Debug.Assert(PickVCache(new() { (32 * MB, 0xFFFF), (32 * MB, 0xFFFF0000) }) is null, "7950X: no V-cache die");
        Debug.Assert(PickVCache(new() { (96 * MB, 0xFFFF) }) is null, "7800X3D: one die, nothing to pin");
        Debug.Assert(PickVCache(new() { (32 * MB, 0xFFFF0000), (96 * MB, 0xFFFF) }) is { Mask: 0xFFFF, CacheMb: 96, Threads: 16 }, "7950X3D");
    }

    const int RelationCache = 2;

    [DllImport("kernel32.dll", SetLastError = true)]
    static extern bool GetLogicalProcessorInformationEx(int relationship, IntPtr buffer, ref uint length);
    [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr mem);
    [DllImport("powrprof.dll")] static extern uint PowerGetActiveScheme(IntPtr root, out IntPtr scheme);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveScheme(IntPtr root, ref Guid scheme);
    [DllImport("powrprof.dll")] static extern uint PowerGetEffectiveOverlayScheme(out Guid mode);
    [DllImport("powrprof.dll")] static extern uint PowerSetActiveOverlayScheme(Guid mode);
}
