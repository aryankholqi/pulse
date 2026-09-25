using System;
using Pulse.Services;

namespace Pulse;

/// <summary>
/// Believable, gently moving numbers for the preview: frame times with the odd stutter,
/// loads that breathe, temperatures that drift. Device names come from the real machine
/// once the sensors have read them.
/// </summary>
internal sealed class PreviewFeed
{
    const int GraphPoints = 90;

    readonly Random _rng = new(7);
    readonly double[] _frames = new double[GraphPoints];
    double _phase;

    public void Reset(GameScene scene)
    {
        double ms = 1000 / scene.Fps;
        for (int i = 0; i < _frames.Length; i++) _frames[i] = Jitter(ms);
    }

    public (HardwareSnapshot Hw, FpsStats Fps, string Target) Next(GameScene scene, HardwareSnapshot? real)
    {
        _phase += 0.35;
        double ms = 1000 / scene.Fps;

        Array.Copy(_frames, 3, _frames, 0, _frames.Length - 3);
        for (int i = _frames.Length - 3; i < _frames.Length; i++) _frames[i] = Jitter(ms);

        double avg = 0, worst = 0;
        foreach (double f in _frames) { avg += f; worst = Math.Max(worst, f); }
        avg /= _frames.Length;

        var fps = new FpsStats(1000 / avg, 1000 / worst, avg, (double[])_frames.Clone());

        float Wave(float center, float swing, double speed) => center + swing * (float)Math.Sin(_phase * speed);

        float vram = real?.VramTotalMb is > 0 and float v ? v : 12_288;
        float ram = real?.RamTotalGb is > 0 and float t ? t : 32;
        var hw = new HardwareSnapshot(
            GpuName: real?.GpuName ?? "NVIDIA GeForce RTX 4070",
            GpuTemp: Wave(66, 2, 0.4), GpuLoad: Math.Min(100, Wave(scene.GpuLoad, 2, 1.3)),
            VramUsedMb: vram * 0.64f, VramTotalMb: vram,
            CpuName: real?.CpuName ?? "AMD Ryzen 7 7800X3D",
            CpuTemp: Wave(71, 3, 0.5), CpuLoad: Wave(scene.CpuLoad, 6, 0.9),
            RamUsedGb: ram * Wave(0.46f, 0.006f, 0.2), RamTotalGb: ram,
            RamLoad: Wave(46, 0.6f, 0.2));

        return (hw, fps, scene.Title);
    }

    // Mostly steady, with a rare hitch so the trace looks alive.
    double Jitter(double ms) =>
        _rng.NextDouble() < 0.03 ? ms * (1.25 + _rng.NextDouble() * 0.35) : ms * (0.93 + _rng.NextDouble() * 0.12);
}
