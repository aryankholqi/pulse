using System;
using System.Buffers;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;

namespace Pulse.Services;

public readonly record struct FpsStats(double? Fps, double? Low1, double? FrameTimeMs, double[]? Graph)
{
    public static readonly FpsStats Empty = new(null, null, null, null);
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

    readonly string _exePath;
    readonly string _args;
    readonly int _selfPid = Environment.ProcessId;

    readonly object _gate = new();
    readonly double[] _frameMs = new double[Capacity];
    readonly long[] _stamp = new long[Capacity];   // when the line reached us (staleness only)
    readonly int[] _pid = new int[Capacity];
    int _head, _count;

    int _foregroundPid;
    int _targetPid;                                // what the overlay shows; guarded by _gate
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
        int pidCol = -1, frameCol = -1;
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
                    haveHeader = TryParseHeader(line, out pidCol, out frameCol);
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

                long now = Stopwatch.GetTimestamp();
                lock (_gate)
                {
                    _frameMs[_head] = ms;
                    _stamp[_head] = now;
                    _pid[_head] = pid;
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

    /// <summary>Supports both PresentMon 1.x (MsBetweenPresents) and 2.x (FrameTime) CSV headers.</summary>
    static bool TryParseHeader(string header, out int pidCol, out int frameCol)
    {
        pidCol = frameCol = -1;
        string[] cols = header.Split(',');
        string[] frameNames = { "MsBetweenPresents", "FrameTime", "MsBetweenAppStart" };

        for (int i = 0; i < cols.Length; i++)
            if (cols[i].Equals("ProcessID", StringComparison.OrdinalIgnoreCase)) { pidCol = i; break; }

        foreach (var name in frameNames)
        {
            for (int i = 0; i < cols.Length; i++)
                if (cols[i].Equals(name, StringComparison.OrdinalIgnoreCase)) { frameCol = i; break; }
            if (frameCol >= 0) break;
        }
        return pidCol >= 0 && frameCol >= 0;
    }

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
                for (int k = 0; k < _count && total < 10_000; k++)
                {
                    int i = (_head - 1 - k + Capacity) % Capacity;
                    if (_stamp[i] < oldest) break;
                    if (_pid[i] != target) continue;
                    if (n == 0) newestStamp = _stamp[i];
                    frames[n++] = _frameMs[i];
                    total += _frameMs[i];
                }

                // Nothing new for a while: paused, loading or minimised — not a hiccup in delivery.
                if (n == 0 || now - newestStamp > freq * StaleSeconds) return FpsStats.Empty;

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

                return new FpsStats(fps, low, frameTime, graph);
            }
            finally
            {
                ArrayPool<double>.Shared.Return(frames);
            }
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
