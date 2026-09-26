using System;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Pulse.Services;

/// <summary>What holds the frame rate back.</summary>
public enum Bottleneck { Gpu, Cpu, Capped }

public readonly record struct FpsStats(double? Fps, double? Low1, double? FrameTimeMs, double[]? Graph, Bottleneck? Bound)
{
    public static readonly FpsStats Empty = new(null, null, null, null, null);
}

/// <summary>
/// Runs Intel PresentMon (ETW, no injection into the game → anti-cheat safe) and keeps
/// recent frames of every presenting process, tagged by PID. The overlay reads the one
/// the player is in.
///
/// Two rules keep the counter from dropping to "–" mid-game:
///  • Rates come from the frames' own durations, never from when lines reach us —
///    PresentMon's output arrives in bursts (pipe buffering, ETW flushes).
///  • Switching focus never clears anything, and a window that isn't drawing
///    (notification, launcher, chat app) doesn't take the counter away from the game.
/// </summary>
public sealed class FpsService : IDisposable
{
    const int Capacity = 16384;         // all presenting processes together: ~20 s at 800 fps
    const int GraphPoints = 90;
    const string SessionName = "PulseOverlay";

    const double StaleSeconds = 3;      // nothing from the target this long → paused / loading / minimised
    const double PresentingSeconds = 2; // "is this process drawing?" look-back
    const double MaxHistorySeconds = 15;
    const double BoundWindowMs = 2000;  // bottleneck verdict: latest ~2 s of frames
    const int BoundMinFrames = 10;

    readonly string _exePath;
    readonly string _args;
    readonly int _selfPid = Environment.ProcessId;

    readonly object _gate = new();
    readonly double[] _frameMs = new double[Capacity];
    readonly long[] _stamp = new long[Capacity];   // when the line reached us (staleness only)
    readonly int[] _pid = new int[Capacity];
    readonly double[] _gpuBusyMs = new double[Capacity];  // NaN: PresentMon didn't report it
    readonly double[] _cpuWaitMs = new double[Capacity];  // time blocked in Present(); NaN: not reported
    int _head, _count;

    int _foregroundPid;
    int _targetPid;                                // what the overlay shows; guarded by _gate
    Bottleneck? _bound;                            // last verdict, for hysteresis; guarded by _gate
    int _boundPid;
    Process? _proc;
    IntPtr _job;
    volatile bool _disposed;

    public string TargetName { get; private set; } = "";
    public string? Error { get; private set; }

    public FpsService(string exePath, string args)
    {
        _exePath = exePath;
        _args = args;
    }

    public void Start()
    {
        if (!File.Exists(_exePath))
        {
            Error = "FPS off: put PresentMon.exe in the Tools folder";
            return;
        }

        try
        {
            var psi = new ProcessStartInfo(_exePath, _args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                StandardOutputEncoding = Encoding.UTF8,
            };
            _proc = Process.Start(psi) ?? throw new InvalidOperationException("could not start");

            _job = Native.CreateKillOnCloseJob();
            if (_job != IntPtr.Zero) Native.AssignProcessToJobObject(_job, _proc.Handle);

            // We run below-normal; PresentMon must not, or it may drop ETW events under load.
            try { _proc.PriorityClass = ProcessPriorityClass.Normal; } catch { }

            new Thread(ReadLoop) { IsBackground = true, Name = "PresentMon reader" }.Start();
        }
        catch (Exception ex)
        {
            Error = "FPS off: " + ex.Message;
        }
    }

    void ReadLoop()
    {
        int pidCol = -1, frameCol = -1, gpuBusyCol = -1, cpuWaitCol = -1;
        bool haveHeader = false;

        try
        {
            var reader = _proc!.StandardOutput;
            string? line;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.Length == 0) continue;

                if (line.StartsWith("Application,", StringComparison.Ordinal))
                {
                    haveHeader = TryParseHeader(line, out pidCol, out frameCol, out gpuBusyCol, out cpuWaitCol);
                    continue;
                }
                if (!haveHeader) continue;

                if (!TryField(line, pidCol, out var pidSpan)
                    || !int.TryParse(pidSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)
                    || pid == _selfPid)
                    continue;

                if (!TryField(line, frameCol, out var msSpan)
                    || !double.TryParse(msSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms)
                    || ms <= 0 || ms > 5000)
                    continue;

                double gpuBusy = OptionalMs(line, gpuBusyCol);
                double cpuWait = OptionalMs(line, cpuWaitCol);

                long now = Stopwatch.GetTimestamp();
                lock (_gate)
                {
                    _frameMs[_head] = ms;
                    _stamp[_head] = now;
                    _pid[_head] = pid;
                    _gpuBusyMs[_head] = gpuBusy;
                    _cpuWaitMs[_head] = cpuWait;
                    _head = (_head + 1) % Capacity;
                    if (_count < Capacity) _count++;
                }
            }
        }
        catch { /* stream closed */ }

        if (!_disposed)
        {
            int code = -1;
            try { code = _proc?.ExitCode ?? -1; } catch { }
            Error = $"FPS off: PresentMon exited ({code}). Run Pulse as admin.";
        }
    }

    /// <summary>
    /// Supports PresentMon 1.x (MsBetweenPresents) and 2.x (FrameTime, later MsBetweenAppStart) CSV headers.
    /// GPU busy and CPU wait feed the bottleneck verdict; -1 when this PresentMon doesn't write them.
    /// </summary>
    static bool TryParseHeader(string header, out int pidCol, out int frameCol, out int gpuBusyCol, out int cpuWaitCol)
    {
        string[] cols = header.Split(',');
        pidCol = Column(cols, "ProcessID");
        frameCol = Column(cols, "MsBetweenPresents", "FrameTime", "MsBetweenAppStart");
        gpuBusyCol = Column(cols, "MsGPUBusy", "GPUBusy", "msGPUActive");
        cpuWaitCol = Column(cols, "MsCPUWait", "CPUWait", "MsInPresentAPI");
        return pidCol >= 0 && frameCol >= 0;
    }

    /// <summary>Index of the first of <paramref name="names"/> (in priority order) the header has, or -1.</summary>
    static int Column(string[] cols, params string[] names)
    {
        foreach (var name in names)
            for (int i = 0; i < cols.Length; i++)
                if (cols[i].Equals(name, StringComparison.OrdinalIgnoreCase)) return i;
        return -1;
    }

    /// <summary>A millisecond field that may be missing or "NA" → NaN.</summary>
    static double OptionalMs(string line, int col) =>
        col >= 0 && TryField(line, col, out var span)
        && double.TryParse(span, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && v >= 0
            ? v
            : double.NaN;

    /// <summary>Allocation-free CSV field lookup.</summary>
    static bool TryField(ReadOnlySpan<char> line, int index, out ReadOnlySpan<char> field)
    {
        int start = 0;
        for (int i = 0; i < index; i++)
        {
            int comma = line[start..].IndexOf(',');
            if (comma < 0) { field = default; return false; }
            start += comma + 1;
        }
        var rest = line[start..];
        int end = rest.IndexOf(',');
        field = end < 0 ? rest : rest[..end];
        return true;
    }

    /// <summary>
    /// Call from the UI tick: follows the window the player is in — but only once that
    /// window's process is actually presenting frames. Until then the last game keeps the counter.
    /// </summary>
    public void UpdateTarget()
    {
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd != IntPtr.Zero)
        {
            Native.GetWindowThreadProcessId(hwnd, out uint raw);
            if ((int)raw != _selfPid && raw != 0) _foregroundPid = (int)raw;
        }

        int fg = _foregroundPid, previous;
        lock (_gate)
        {
            previous = _targetPid;
            if (fg == 0 || fg == _targetPid) return;

            long since = Stopwatch.GetTimestamp() - (long)(Stopwatch.Frequency * PresentingSeconds);
            if (IsPresenting(fg, since) || !IsPresenting(_targetPid, since))
                _targetPid = fg;
            if (_targetPid == previous) return;
        }

        string name = "";
        try { using var p = Process.GetProcessById(fg); name = p.ProcessName; } catch { }
        TargetName = name;
    }

    // Caller holds _gate.
    bool IsPresenting(int pid, long since)
    {
        if (pid == 0) return false;
        for (int k = 0; k < _count; k++)
        {
            int i = (_head - 1 - k + Capacity) % Capacity;
            if (_stamp[i] < since) return false;
            if (_pid[i] == pid) return true;
        }
        return false;
    }

    public FpsStats GetStats()
    {
        lock (_gate)
        {
            int target = _targetPid;
            if (_count == 0 || target == 0) return FpsStats.Empty;

            long now = Stopwatch.GetTimestamp();
            long freq = Stopwatch.Frequency;
            long oldest = now - (long)(freq * MaxHistorySeconds);

            // Newest first: the target's frames only, up to 10 s of *game* time.
            double[] frames = ArrayPool<double>.Shared.Rent(Math.Min(_count, Capacity));
            try
            {
                int n = 0;
                double total = 0;
                long newestStamp = 0;
                var window = new BoundWindow();
                for (int k = 0; k < _count && total < 10_000; k++)
                {
                    int i = (_head - 1 - k + Capacity) % Capacity;
                    if (_stamp[i] < oldest) break;
                    if (_pid[i] != target) continue;
                    if (n == 0) newestStamp = _stamp[i];
                    if (total < BoundWindowMs) window.Add(_frameMs[i], _gpuBusyMs[i], _cpuWaitMs[i]);
                    frames[n++] = _frameMs[i];
                    total += _frameMs[i];
                }

                // Nothing new for a while: paused, loading or minimised — not a hiccup in delivery.
                if (n == 0 || now - newestStamp > freq * StaleSeconds)
                {
                    _bound = null;
                    return FpsStats.Empty;
                }

                if (_boundPid != target) { _bound = null; _boundPid = target; }
                _bound = window.Frames >= BoundMinFrames ? window.Classify(_bound) : null;

                // FPS and frame time over the latest ~1 s of frames.
                double sum1 = 0;
                int n1 = 0;
                while (n1 < n && sum1 < 1000) sum1 += frames[n1++];
                double fps = n1 * 1000.0 / sum1;
                double frameTime = sum1 / n1;

                int g = Math.Min(GraphPoints, n);
                var graph = new double[g];
                for (int k = 0; k < g; k++) graph[k] = frames[g - 1 - k]; // oldest → newest

                // "1% low" = FPS at the 99th-percentile frame time over the last 10 s.
                double? low = null;
                if (n >= 20)
                {
                    Array.Sort(frames, 0, n);
                    int idx = Math.Clamp((int)Math.Ceiling(n * 0.99) - 1, 0, n - 1);
                    low = 1000.0 / frames[idx];
                }

                return new FpsStats(fps, low, frameTime, graph, _bound);
            }
            finally
            {
                ArrayPool<double>.Shared.Return(frames);
            }
        }
    }

    /// <summary>
    /// Which side holds the frame rate back, judged from PresentMon's per-frame timings:
    ///  • GPU busy for (nearly) the whole frame → the GPU is the limit.
    ///  • Frames paced like clockwork → something holds them back on purpose: V-Sync or a frame limiter.
    ///  • CPU parked inside Present() → not the CPU: a mostly busy GPU with gaps (still GPU),
    ///    or else the display / a limiter making it wait (capped).
    ///  • GPU left idle otherwise → it's waiting on the CPU.
    /// The current verdict gets some slack, so a scene on the edge doesn't flicker.
    /// </summary>
    struct BoundWindow
    {
        double _frame, _frameSq, _gpuBusy, _cpuWait;
        public int Frames { get; private set; }

        /// <summary>Frames without a GPU busy time (older PresentMon, "NA") are left out.</summary>
        public void Add(double frameMs, double gpuBusyMs, double cpuWaitMs)
        {
            if (double.IsNaN(gpuBusyMs)) return;
            _frame += frameMs;
            _frameSq += frameMs * frameMs;
            _gpuBusy += gpuBusyMs;
            if (!double.IsNaN(cpuWaitMs)) _cpuWait += cpuWaitMs;
            Frames++;
        }

        public readonly Bottleneck Classify(Bottleneck? current)
        {
            double gpuShare = _gpuBusy / _frame;
            if (gpuShare >= (current == Bottleneck.Gpu ? 0.80 : 0.90)) return Bottleneck.Gpu;

            // Frame-to-frame spread relative to the mean: V-Sync and limiters sit well under 1.5%.
            double mean = _frame / Frames;
            double spread = Math.Sqrt(Math.Max(0, _frameSq / Frames - mean * mean)) / mean;
            if (spread <= (current == Bottleneck.Capped ? 0.025 : 0.015)) return Bottleneck.Capped;

            if (_cpuWait / _frame >= 0.25) return gpuShare >= 0.75 ? Bottleneck.Gpu : Bottleneck.Capped;
            return Bottleneck.Cpu;
        }
    }

    public void Dispose()
    {
        _disposed = true;
        try { if (_proc is { HasExited: false }) _proc.Kill(); } catch { }
        _proc?.Dispose();
        if (_job != IntPtr.Zero) { Native.CloseHandle(_job); _job = IntPtr.Zero; }

        // A killed PresentMon leaves its ETW session behind — stop it cleanly.
        try
        {
            using var logman = Process.Start(new ProcessStartInfo("logman", $"stop {SessionName} -ets")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            logman?.WaitForExit(2000);
        }
        catch { }
    }
}
