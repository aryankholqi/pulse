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
/// frame times only for the process that owns the foreground window.
/// </summary>
public sealed class FpsService : IDisposable
{
    const int Capacity = 8192;          // ~16 s of frames at 500 fps
    const int GraphPoints = 90;
    const string SessionName = "PulseOverlay";

    readonly string _exePath;
    readonly string _args;
    readonly int _selfPid = Environment.ProcessId;

    readonly object _gate = new();
    readonly double[] _frameMs = new double[Capacity];
    readonly long[] _stamp = new long[Capacity];
    int _head, _count;

    volatile int _targetPid;
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

                int target = _targetPid;
                if (target == 0) continue;

                if (!TryField(line, pidCol, out var pidSpan)
                    || !int.TryParse(pidSpan, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pid)
                    || pid != target)
                    continue;

                if (!TryField(line, frameCol, out var msSpan)
                    || !double.TryParse(msSpan, NumberStyles.Float, CultureInfo.InvariantCulture, out double ms)
                    || ms <= 0 || ms > 5000)
                    continue;

                long now = Stopwatch.GetTimestamp();
                lock (_gate)
                {
                    if (pid != _targetPid) continue; // foreground changed while we parsed
                    _frameMs[_head] = ms;
                    _stamp[_head] = now;
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

    /// <summary>Call from the UI tick: follows whatever window the player is in.</summary>
    public void UpdateTarget()
    {
        IntPtr hwnd = Native.GetForegroundWindow();
        if (hwnd == IntPtr.Zero) return;
        Native.GetWindowThreadProcessId(hwnd, out uint raw);
        int pid = (int)raw;
        if (pid == _targetPid || pid == _selfPid) return;

        lock (_gate)
        {
            _head = 0;
            _count = 0;
            _targetPid = pid;
        }

        string name = "";
        try { using var p = Process.GetProcessById(pid); name = p.ProcessName; } catch { }
        TargetName = name;
    }

    public FpsStats GetStats()
    {
        lock (_gate)
        {
            if (_count == 0) return FpsStats.Empty;

            long now = Stopwatch.GetTimestamp();
            long freq = Stopwatch.Frequency;
            int newest = (_head - 1 + Capacity) % Capacity;

            // No frames for 1.5 s → not a game (or paused/minimised).
            if (now - _stamp[newest] > freq * 3 / 2) return FpsStats.Empty;

            long oneSecAgo = now - freq;
            long tenSecAgo = now - freq * 10;

            double sum1 = 0;
            int n1 = 0, n10 = 0;
            double[] window = ArrayPool<double>.Shared.Rent(_count);
            try
            {
                for (int k = 0; k < _count; k++)
                {
                    int i = (newest - k + Capacity) % Capacity;
                    long t = _stamp[i];
                    if (t < tenSecAgo) break;
                    double ms = _frameMs[i];
                    window[n10++] = ms;
                    if (t >= oneSecAgo) { sum1 += ms; n1++; }
                }

                double? fps = n1 > 0 && sum1 > 0 ? n1 * 1000.0 / sum1 : null;
                double? frameTime = n1 > 0 ? sum1 / n1 : null;

                // "1% low" = FPS at the 99th-percentile frame time over the last 10 s.
                double? low = null;
                if (n10 >= 20)
                {
                    Array.Sort(window, 0, n10);
                    int idx = Math.Clamp((int)Math.Ceiling(n10 * 0.99) - 1, 0, n10 - 1);
                    low = 1000.0 / window[idx];
                }

                int g = Math.Min(GraphPoints, _count);
                var graph = new double[g];
                for (int k = 0; k < g; k++)
                    graph[k] = _frameMs[(newest - (g - 1 - k) + Capacity) % Capacity];

                return new FpsStats(fps, low, frameTime, graph);
            }
            finally
            {
                ArrayPool<double>.Shared.Return(window);
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
