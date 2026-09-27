using System;

namespace Pulse.Services;

/// <summary>
/// Which side holds the frame rate back, judged from PresentMon's per-frame timings:
///  • Average FPS sitting on the monitor's refresh rate → V-Sync (or a cap at it). Checked first:
///    with V-Sync the GPU clocks down until it barely keeps up, so it looks ~90% busy.
///  • GPU busy for (nearly) the whole frame → the GPU is the limit.
///  • Frames paced like clockwork → something holds them back on purpose: V-Sync or a frame limiter.
///  • CPU parked inside Present() → not the CPU: a mostly busy GPU with gaps (still GPU),
///    or else the display / a limiter making it wait (capped).
///  • GPU left idle otherwise → it's waiting on the CPU.
/// The current verdict gets some slack, so a scene on the edge doesn't flicker.
/// </summary>
internal struct BoundWindow
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

    /// <param name="refreshHz">The game's monitor, 0 if unknown. Windows rounds 59.94 Hz down to 59, hence the slack.</param>
    public readonly Bottleneck Classify(Bottleneck? current, int refreshHz)
    {
        double mean = _frame / Frames;
        if (refreshHz > 0)
        {
            double off = Math.Abs(mean * refreshHz / 1000 - 1);
            if (off <= (current == Bottleneck.Capped ? 0.03 : 0.02)) return Bottleneck.Capped;
        }

        double gpuShare = _gpuBusy / _frame;
        if (gpuShare >= (current == Bottleneck.Gpu ? 0.80 : 0.90)) return Bottleneck.Gpu;

        // Frame-to-frame spread relative to the mean: V-Sync and limiters sit well under 1.5%.
        double spread = Math.Sqrt(Math.Max(0, _frameSq / Frames - mean * mean)) / mean;
        if (spread <= (current == Bottleneck.Capped ? 0.025 : 0.015)) return Bottleneck.Capped;

        if (_cpuWait / _frame >= 0.25) return gpuShare >= 0.75 ? Bottleneck.Gpu : Bottleneck.Capped;
        return Bottleneck.Cpu;
    }
}
