using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Pulse.Services;

/// <summary>
/// The few NVIDIA readings Pulse shows every second (temperature, load, VRAM), straight from NVML.
/// Each call takes well under a millisecond. LibreHardwareMonitor's full GPU update also asks NVML for
/// PCIe throughput, which samples for ~30 ms per direction: ~85 ms of driver calls every second,
/// the kind of poll that can hitch a game's frames. Not thread-safe: the sensor thread owns it.
/// </summary>
internal sealed class Nvml : IDisposable
{
    const string Dll = "nvml.dll";
    const int Success = 0;

    IntPtr _device;

    Nvml(IntPtr device) => _device = device;

    /// <summary>The NVML device named <paramref name="gpuName"/> (the only one, if names don't match), or null without NVML.</summary>
    public static Nvml? Open(string gpuName)
    {
        try
        {
            if (nvmlInit_v2() != Success) return null;
            if (nvmlDeviceGetCount_v2(out uint count) == Success)
            {
                IntPtr only = IntPtr.Zero;
                for (uint i = 0; i < count; i++)
                {
                    if (nvmlDeviceGetHandleByIndex_v2(i, out var device) != Success) continue;
                    var name = new StringBuilder(96);
                    if (nvmlDeviceGetName(device, name, (uint)name.Capacity) == Success
                        && gpuName.EndsWith(name.ToString().Trim(), StringComparison.OrdinalIgnoreCase))
                        return new Nvml(device);
                    if (count == 1) only = device;
                }
                if (only != IntPtr.Zero) return new Nvml(only);
            }
            nvmlShutdown();
        }
        catch { /* no NVIDIA driver / nvml.dll: the caller keeps LibreHardwareMonitor */ }
        return null;
    }

    public float? Temperature =>
        Try(() => nvmlDeviceGetTemperature(_device, 0 /* NVML_TEMPERATURE_GPU */, out uint t) == Success ? t : (float?)null);

    public float? Load =>
        Try(() => nvmlDeviceGetUtilizationRates(_device, out var u) == Success ? u.Gpu : (float?)null);

    /// <summary>Used and total VRAM in MB.</summary>
    public (float? Used, float? Total) Memory
    {
        get
        {
            try
            {
                if (nvmlDeviceGetMemoryInfo(_device, out var m) == Success && m.Total > 0)
                    return (m.Used / 1048576f, m.Total / 1048576f);
            }
            catch { }
            return (null, null);
        }
    }

    /// <summary>Why the clocks are below full boost right now (nvmlClocksThrottleReason* bits), or null.</summary>
    public ulong? ThrottleReasons
    {
        get
        {
            try { return nvmlDeviceGetCurrentClocksThrottleReasons(_device, out ulong r) == Success ? r : null; }
            catch { return null; }
        }
    }

    /// <summary>Board power draw in watts.</summary>
    public float? PowerWatts =>
        Try(() => nvmlDeviceGetPowerUsage(_device, out uint mw) == Success ? mw / 1000f : (float?)null);

    /// <summary>The power limit the driver allows (min, max), ships with (stock) and enforces now, in watts. Null if not adjustable.</summary>
    public (float Min, float Max, float Stock, float Now)? PowerLimits
    {
        get
        {
            try
            {
                if (nvmlDeviceGetPowerManagementLimitConstraints(_device, out uint min, out uint max) == Success
                    && nvmlDeviceGetPowerManagementDefaultLimit(_device, out uint stock) == Success
                    && nvmlDeviceGetPowerManagementLimit(_device, out uint now) == Success
                    && min < max && stock > 0)
                    return (min / 1000f, max / 1000f, stock / 1000f, now / 1000f);
            }
            catch { }
            return null;
        }
    }

    /// <summary>Needs admin (Pulse runs elevated). Resets to stock when the driver reloads or Windows restarts.</summary>
    public bool SetPowerLimit(float watts)
    {
        try { return nvmlDeviceSetPowerManagementLimit(_device, (uint)Math.Round(watts * 1000)) == Success; }
        catch { return false; }
    }

    /// <summary>Graphics clock right now, in MHz.</summary>
    public float? GraphicsClock =>
        Try(() => nvmlDeviceGetClockInfo(_device, 0 /* NVML_CLOCK_GRAPHICS */, out uint mhz) == Success ? mhz : (float?)null);

    /// <summary>The highest graphics clock the card can boost to, in MHz.</summary>
    public float? MaxGraphicsClock =>
        Try(() => nvmlDeviceGetMaxClockInfo(_device, 0, out uint mhz) == Success ? mhz : (float?)null);

    /// <summary>
    /// The range the driver allows for shifting the whole voltage/frequency curve, in MHz (Afterburner's "core clock").
    /// Null where the driver has no such call (older than R510) or the card won't allow it.
    /// </summary>
    public (int Min, int Max)? ClockOffsetRange
    {
        get
        {
            try
            {
                if (nvmlDeviceGetGpcClkMinMaxVfOffset(_device, out int min, out int max) == Success && max > 0) return (min, max);
            }
            catch { /* EntryPointNotFound: an older driver */ }
            return null;
        }
    }

    public bool SetClockOffset(int mhz)
    {
        try { return nvmlDeviceSetGpcClkVfOffset(_device, mhz) == Success; }
        catch { return false; }
    }

    /// <summary>Keeps the graphics clock at or under <paramref name="maxMhz"/>; null lifts the cap.</summary>
    public bool CapClock(int? maxMhz)
    {
        try
        {
            if (maxMhz is not { } max) return nvmlDeviceResetGpuLockedClocks(_device) == Success;
            // 0 = no floor: the card still idles down
            return nvmlDeviceSetGpuLockedClocks(_device, 0, (uint)max) == Success;
        }
        catch { return false; }
    }

    static float? Try(Func<float?> read)
    {
        try { return read(); } catch { return null; }
    }

    public void Dispose()
    {
        if (_device == IntPtr.Zero) return;
        _device = IntPtr.Zero;
        try { nvmlShutdown(); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)] struct Utilization { public uint Gpu, MemoryBus; }
    [StructLayout(LayoutKind.Sequential)] struct MemoryInfo { public ulong Total, Free, Used; }

    [DllImport(Dll)] static extern int nvmlInit_v2();
    [DllImport(Dll)] static extern int nvmlShutdown();
    [DllImport(Dll)] static extern int nvmlDeviceGetCount_v2(out uint count);
    [DllImport(Dll)] static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport(Dll, CharSet = CharSet.Ansi)] static extern int nvmlDeviceGetName(IntPtr device, StringBuilder name, uint length);
    [DllImport(Dll)] static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
    [DllImport(Dll)] static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization utilization);
    [DllImport(Dll)] static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out MemoryInfo memory);
    [DllImport(Dll)] static extern int nvmlDeviceGetCurrentClocksThrottleReasons(IntPtr device, out ulong reasons);
    [DllImport(Dll)] static extern int nvmlDeviceGetPowerUsage(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] static extern int nvmlDeviceGetPowerManagementLimitConstraints(IntPtr device, out uint minMw, out uint maxMw);
    [DllImport(Dll)] static extern int nvmlDeviceGetPowerManagementDefaultLimit(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] static extern int nvmlDeviceGetPowerManagementLimit(IntPtr device, out uint milliwatts);
    [DllImport(Dll)] static extern int nvmlDeviceSetPowerManagementLimit(IntPtr device, uint milliwatts);
    [DllImport(Dll)] static extern int nvmlDeviceGetClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Dll)] static extern int nvmlDeviceGetMaxClockInfo(IntPtr device, int type, out uint mhz);
    [DllImport(Dll)] static extern int nvmlDeviceGetGpcClkMinMaxVfOffset(IntPtr device, out int minMhz, out int maxMhz);
    [DllImport(Dll)] static extern int nvmlDeviceSetGpcClkVfOffset(IntPtr device, int mhz);
    [DllImport(Dll)] static extern int nvmlDeviceSetGpuLockedClocks(IntPtr device, uint minMhz, uint maxMhz);
    [DllImport(Dll)] static extern int nvmlDeviceResetGpuLockedClocks(IntPtr device);
}
