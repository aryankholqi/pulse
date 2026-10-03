using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Pulse;

internal static class Native
{
    // ── window styles ──
    const int GWL_EXSTYLE = -20;
    const long WS_EX_TRANSPARENT = 0x00000020; // mouse clicks pass through to the game
    const long WS_EX_TOOLWINDOW = 0x00000080;  // no Alt+Tab entry
    const long WS_EX_LAYERED = 0x00080000;
    const long WS_EX_NOACTIVATE = 0x08000000;  // never steals focus from the game

    static readonly IntPtr HWND_TOPMOST = new(-1);
    const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010, SWP_NOOWNERZORDER = 0x0200;

    public static void MakeOverlayWindow(IntPtr hwnd)
    {
        long ex = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
        ex |= WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE;
        SetWindowLongPtr(hwnd, GWL_EXSTYLE, new IntPtr(ex));
    }

    public static void SetTopmost(IntPtr hwnd) =>
        SetWindowPos(hwnd, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER);

    /// <summary>Dark caption bar for the settings window (Windows 10 20H1+ / 11; ignored elsewhere).</summary>
    public static void UseDarkTitleBar(IntPtr hwnd)
    {
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        int on = 1;
        try { DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int)); } catch { }
    }

    // ── liquid glass: Windows blurs whatever is behind the overlay ──
    [StructLayout(LayoutKind.Sequential)]
    struct ACCENT_POLICY
    {
        public int AccentState;
        public int AccentFlags;
        public uint GradientColor;
        public int AnimationId;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    const int WCA_ACCENT_POLICY = 19;
    const int ACCENT_DISABLED = 0, ACCENT_ENABLE_BLURBEHIND = 3;

    /// <summary>
    /// Blur what's behind the window wherever it isn't opaque (Windows 10 / 11; ignored elsewhere). The tint is the
    /// window's own: the blur itself is left clear.
    /// </summary>
    public static void SetBlurBehind(IntPtr hwnd, bool on)
    {
        var accent = new ACCENT_POLICY { AccentState = on ? ACCENT_ENABLE_BLURBEHIND : ACCENT_DISABLED };
        int size = Marshal.SizeOf<ACCENT_POLICY>();
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(accent, ptr, false);
            var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
            SetWindowCompositionAttribute(hwnd, ref data);
        }
        catch { }
        finally { Marshal.FreeHGlobal(ptr); }
    }

    const int DWMWA_WINDOW_CORNER_PREFERENCE = 33, DWMWA_BORDER_COLOR = 34;
    const int DWMWCP_DONOTROUND = 1, DWMWCP_ROUND = 2;
    const uint DWMWA_COLOR_NONE = 0xFFFFFFFE, DWMWA_COLOR_DEFAULT = 0xFFFFFFFF;

    /// <summary>
    /// Round the window's corners, blur included, with no border (Windows 11; ignored elsewhere). The accent blur
    /// fills the whole window rectangle and neither a window region nor a blur-behind region cuts it: only DWM's own
    /// corner rounding does. Its radius is fixed (8 DIPs), so the card matches it, not the other way round.
    /// </summary>
    public static void SetRoundCorners(IntPtr hwnd, bool on)
    {
        int corner = on ? DWMWCP_ROUND : DWMWCP_DONOTROUND;
        int border = unchecked((int)(on ? DWMWA_COLOR_NONE : DWMWA_COLOR_DEFAULT));
        try
        {
            DwmSetWindowAttribute(hwnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref corner, sizeof(int));
            DwmSetWindowAttribute(hwnd, DWMWA_BORDER_COLOR, ref border, sizeof(int));
        }
        catch { }
    }

    // ── low-impact process mode ──
    [StructLayout(LayoutKind.Sequential)]
    struct PROCESS_POWER_THROTTLING_STATE
    {
        public uint Version;
        public uint ControlMask;
        public uint StateMask;
    }

    const int ProcessPowerThrottling = 4;
    const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;

    public static void ApplyLowImpactMode()
    {
        try { Process.GetCurrentProcess().PriorityClass = ProcessPriorityClass.BelowNormal; } catch { }

        try
        {
            // EcoQoS: Windows schedules us on efficiency cores / low clocks when it can.
            var state = new PROCESS_POWER_THROTTLING_STATE
            {
                Version = 1,
                ControlMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
                StateMask = PROCESS_POWER_THROTTLING_EXECUTION_SPEED,
            };
            SetProcessInformation(GetCurrentProcess(), ProcessPowerThrottling, ref state,
                (uint)Marshal.SizeOf<PROCESS_POWER_THROTTLING_STATE>());
        }
        catch { /* older Windows builds: fine without it */ }
    }

    // ── job object: PresentMon dies with us, even on a crash ──
    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct IO_COUNTERS
    {
        public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
        public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    const int JobObjectExtendedLimitInformation = 9;
    const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    public static IntPtr CreateKillOnCloseJob()
    {
        IntPtr job = CreateJobObject(IntPtr.Zero, null);
        if (job == IntPtr.Zero) return IntPtr.Zero;

        var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
        info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

        int len = Marshal.SizeOf<JOBOBJECT_EXTENDED_LIMIT_INFORMATION>();
        IntPtr ptr = Marshal.AllocHGlobal(len);
        try
        {
            Marshal.StructureToPtr(info, ptr, false);
            SetInformationJobObject(job, JobObjectExtendedLimitInformation, ptr, (uint)len);
        }
        finally { Marshal.FreeHGlobal(ptr); }
        return job;
    }

    // ── processes and windows (no handles opened on the game: anti-cheat friendly) ──

    public static string GetWindowTitle(IntPtr hwnd)
    {
        var sb = new System.Text.StringBuilder(256);
        return GetWindowText(hwnd, sb, sb.Capacity) > 0 ? sb.ToString() : "";
    }

    /// <summary>Every running process ID, or null if Windows wouldn't say.</summary>
    public static HashSet<int>? RunningProcessIds()
    {
        var ids = new uint[1024];
        while (true)
        {
            if (!K32EnumProcesses(ids, (uint)(ids.Length * sizeof(uint)), out uint bytes)) return null;
            int count = (int)(bytes / sizeof(uint));
            if (count < ids.Length)
            {
                var set = new HashSet<int>(count);
                for (int i = 0; i < count; i++) set.Add((int)ids[i]);
                return set;
            }
            ids = new uint[ids.Length * 2]; // the buffer was full: there may be more
        }
    }

    // ── display ──
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct MONITORINFOEX
    {
        public int cbSize;
        public int rcMonitorL, rcMonitorT, rcMonitorR, rcMonitorB;
        public int rcWorkL, rcWorkT, rcWorkR, rcWorkB;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string szDevice;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct DEVMODE
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
        public ushort dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
        public uint dmFields;
        public int dmPositionX, dmPositionY;
        public uint dmDisplayOrientation, dmDisplayFixedOutput;
        public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
        public ushort dmLogPixels;
        public uint dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
        public uint dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
    }

    /// <summary>The monitor a window is on, for caching <see cref="RefreshRate"/>.</summary>
    public static IntPtr MonitorOf(IntPtr hwnd) => MonitorFromWindow(hwnd, 2 /* MONITOR_DEFAULTTONEAREST */);

    /// <summary>Refresh rate in Hz of that monitor's current mode, or 0 if Windows wouldn't say.</summary>
    public static int RefreshRate(IntPtr monitor)
    {
        try
        {
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(monitor, ref mi)) return 0;
            var dm = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettings(mi.szDevice, -1 /* ENUM_CURRENT_SETTINGS */, ref dm)) return 0;
            return dm.dmDisplayFrequency > 1 ? (int)dm.dmDisplayFrequency : 0; // 0 / 1: "hardware default"
        }
        catch { return 0; }
    }

    /// <summary>The window fills its whole monitor (taskbar included): fullscreen or borderless, the way games run.</summary>
    public static bool CoversMonitor(IntPtr hwnd)
    {
        try
        {
            if (hwnd == IntPtr.Zero || !GetWindowRect(hwnd, out var r)) return false;
            var mi = new MONITORINFOEX { cbSize = Marshal.SizeOf<MONITORINFOEX>() };
            if (!GetMonitorInfo(MonitorOf(hwnd), ref mi)) return false;
            return r.L <= mi.rcMonitorL && r.T <= mi.rcMonitorT && r.R >= mi.rcMonitorR && r.B >= mi.rcMonitorB;
        }
        catch { return false; }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RECT { public int L, T, R, B; }

    /// <summary>Full path of a process's .exe. Limited query rights: works without admin for most processes.</summary>
    public static string? ExePathOf(int pid)
    {
        IntPtr handle = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (handle == IntPtr.Zero) return null;
        try
        {
            var path = new System.Text.StringBuilder(1024);
            int size = path.Capacity;
            return QueryFullProcessImageName(handle, 0, path, ref size) ? path.ToString(0, size) : null;
        }
        finally { CloseHandle(handle); }
    }

    const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

    // ── P/Invoke ──
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr hwnd, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")] static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFOEX info);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, EntryPoint = "EnumDisplaySettingsW")] static extern bool EnumDisplaySettings(string device, int mode, ref DEVMODE devMode);
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr hWnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")] static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll")] public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
    [DllImport("kernel32.dll")] static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "QueryFullProcessImageNameW")]
    static extern bool QueryFullProcessImageName(IntPtr process, int flags, System.Text.StringBuilder path, ref int size);
    [DllImport("kernel32.dll")] static extern bool K32EnumProcesses([Out] uint[] processIds, uint size, out uint bytesReturned);
}
