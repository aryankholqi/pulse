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
}
