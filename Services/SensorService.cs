using System;
using System.Collections.Generic;
using System.Threading;
using LibreHardwareMonitor.Hardware;

namespace Pulse.Services;

public sealed record HardwareSnapshot(
    string? GpuName, float? GpuTemp, float? GpuHotspot, float? GpuLoad, float? VramUsedMb, float? VramTotalMb,
    string? CpuName, float? CpuTemp, float? CpuLoad,
    float? RamUsedGb, float? RamTotalGb, float? RamLoad);

/// <summary>
/// Polls CPU / GPU / RAM once per second on its own low-priority thread.
/// Only the three chosen devices are updated — never the whole machine. An NVIDIA card is read
/// through <see cref="Nvml"/> instead: LibreHardwareMonitor's full update of it blocks ~85 ms.
/// </summary>
public sealed class SensorService : IDisposable
{
    readonly CancellationTokenSource _cts = new();
    Thread? _thread;
    Computer? _computer;
    IHardware? _cpu, _gpu;
    readonly List<IHardware> _ram = new();
    HardwareSnapshot? _latest;
    volatile bool _paused;

    // RTX 50 (Blackwell) dropped the hot spot from NVAPI. LHM 0.9.4 still fills "GPU Hot Spot" from the old
    // NVAPI slot, which on these cards is the core sensor itself at 1/256° resolution: a finer core reading,
    // not a hot spot. (LHM builds that read the real one through the PawnIO driver also expose
    // "GPU Hot Spot #1…"; that value is genuine and left alone.)
    bool _gpuHotspotEchoesCore;

    // NVIDIA: temperature, load and VRAM from NVML every second. The hot spot only comes through
    // LibreHardwareMonitor's full (slow) update, so that runs every HotspotEvery polls, and never on RTX 50.
    const int HotspotEvery = 10;
    Nvml? _nvml;
    int _hotspotCountdown;
    float? _nvHotspot;

    public HardwareSnapshot? Latest => Volatile.Read(ref _latest);
    public string? Error { get; private set; }
    public bool Paused { get => _paused; set => _paused = value; }

    public void Start()
    {
        _thread = new Thread(Run) { IsBackground = true, Name = "Pulse sensors", Priority = ThreadPriority.BelowNormal };
        _thread.Start();
    }

    void Run()
    {
        try
        {
            _computer = new Computer { IsCpuEnabled = true, IsGpuEnabled = true, IsMemoryEnabled = true };
            _computer.Open();
            SelectHardware(_computer);
            if (_gpu is { HardwareType: HardwareType.GpuNvidia }) _nvml = Nvml.Open(_gpu.Name);
        }
        catch (Exception ex)
        {
            Error = "Sensors unavailable: " + ex.Message;
            return;
        }

        var token = _cts.Token;
        while (!token.IsCancellationRequested)
        {
            if (!_paused)
            {
                try { Volatile.Write(ref _latest, Poll()); }
                catch { /* a flaky sensor read shouldn't kill the loop */ }
            }
            token.WaitHandle.WaitOne(1000);
        }
    }

    void SelectHardware(Computer computer)
    {
        double bestGpuScore = -1;
        foreach (var hw in computer.Hardware)
        {
            switch (hw.HardwareType)
            {
                case HardwareType.Cpu:
                    _cpu ??= hw;
                    break;

                case HardwareType.Memory:
                    _ram.Add(hw);
                    break;

                case HardwareType.GpuNvidia:
                case HardwareType.GpuAmd:
                case HardwareType.GpuIntel:
                    // Prefer the discrete card over an iGPU (e.g. Ryzen 7000's Radeon iGPU + a GeForce).
                    hw.Update();
                    int vendorRank = hw.HardwareType switch
                    {
                        HardwareType.GpuNvidia => 3,
                        HardwareType.GpuAmd => 2,
                        _ => 1,
                    };
                    double vram = Find(hw, SensorType.SmallData, "GPU Memory Total") ?? 0;
                    double score = vendorRank * 1_000_000 + vram;
                    if (score > bestGpuScore) { bestGpuScore = score; _gpu = hw; }
                    break;
            }
        }

        _gpuHotspotEchoesCore = _gpu is { HardwareType: HardwareType.GpuNvidia } nv
            && (nv.Name.StartsWith("NVIDIA GeForce RTX 50", StringComparison.OrdinalIgnoreCase)
                || nv.Name.Contains("Blackwell", StringComparison.OrdinalIgnoreCase));
    }

    HardwareSnapshot Poll()
    {
        _cpu?.Update();
        if (_nvml is null) _gpu?.Update();
        foreach (var r in _ram) r.Update();

        float? cpuTemp = null, cpuLoad = null;
        if (_cpu != null)
        {
            cpuTemp = Find(_cpu, SensorType.Temperature,
                          "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Tdie", "Core Average", "Core Max")
                      ?? FirstPositive(_cpu, SensorType.Temperature);
            cpuLoad = Find(_cpu, SensorType.Load, "CPU Total");
        }

        float? gpuTemp = null, gpuHotspot = null, gpuLoad = null, vramUsed = null, vramTotal = null;
        if (_nvml != null)
        {
            gpuTemp = Plausible(_nvml.Temperature);
            gpuLoad = _nvml.Load;
            (vramUsed, vramTotal) = _nvml.Memory;
            gpuHotspot = NvidiaHotspot();
        }
        else if (_gpu != null)
        {
            gpuTemp = Plausible(Find(_gpu, SensorType.Temperature, "GPU Core"));
            // AMD junction temp; NVIDIA through NVAPI up to RTX 40. Null (hidden) when not reported.
            gpuHotspot = Plausible(Find(_gpu, SensorType.Temperature, "GPU Hot Spot", "GPU Hotspot", "GPU Junction"));
            if (_gpuHotspotEchoesCore && gpuHotspot is float echo
                && Find(_gpu, SensorType.Temperature, "GPU Hot Spot #1") is null)
            {
                // Agreeing with the whole-degree core is the echo's signature; otherwise keep the stock reading.
                if (gpuTemp is not float core || Math.Abs(echo - core) < 1.5f) gpuTemp = echo;
                gpuHotspot = null;
            }
            gpuLoad = Find(_gpu, SensorType.Load, "GPU Core", "D3D 3D");
            vramUsed = Find(_gpu, SensorType.SmallData, "GPU Memory Used", "D3D Dedicated Memory Used");
            vramTotal = Find(_gpu, SensorType.SmallData, "GPU Memory Total");
        }

        float? ramLoad = null, ramUsed = null, ramAvail = null;
        foreach (var r in _ram)
        {
            ramLoad ??= Find(r, SensorType.Load, "Memory");
            ramUsed ??= Find(r, SensorType.Data, "Memory Used");
            ramAvail ??= Find(r, SensorType.Data, "Memory Available");
        }

        return new HardwareSnapshot(
            _gpu?.Name, gpuTemp, gpuHotspot, gpuLoad, vramUsed, vramTotal,
            _cpu?.Name, cpuTemp, cpuLoad,
            ramUsed, ramUsed + ramAvail, ramLoad);
    }

    /// <summary>The hot spot next to NVML's readings: refreshed every <see cref="HotspotEvery"/> polls. RTX 50 has none.</summary>
    float? NvidiaHotspot()
    {
        if (_gpu is null || _gpuHotspotEchoesCore) return null;
        if (--_hotspotCountdown > 0) return _nvHotspot;

        _hotspotCountdown = HotspotEvery;
        _gpu.Update();
        _nvHotspot = Plausible(Find(_gpu, SensorType.Temperature, "GPU Hot Spot", "GPU Hotspot", "GPU Junction"));
        return _nvHotspot;
    }

    static float? Find(IHardware hw, SensorType type, params string[] names)
    {
        foreach (var name in names)
            foreach (var s in hw.Sensors)
                if (s.SensorType == type
                    && string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase)
                    && s.Value is float v && !float.IsNaN(v))
                    return v;
        return null;
    }

    // 0 and 255 are what a missing sensor reads as, never a real GPU temperature.
    static float? Plausible(float? t) => t is > 0 and < 150 ? t : null;

    static float? FirstPositive(IHardware hw, SensorType type)
    {
        foreach (var s in hw.Sensors)
            if (s.SensorType == type && s.Value is float v && v > 0 && !float.IsNaN(v))
                return v;
        return null;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _thread?.Join(1500);
        _nvml?.Dispose();
        try { _computer?.Close(); } catch { }
        _cts.Dispose();
    }
}
