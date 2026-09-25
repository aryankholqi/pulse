using System;
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

    // ── P/Invoke ──
    [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr value);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
    [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, uint vk);
    [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    [DllImport("user32.dll")] public static extern bool DestroyIcon(IntPtr hIcon);

    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("kernel32.dll")] static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] static extern bool SetProcessInformation(IntPtr hProcess, int infoClass, ref PROCESS_POWER_THROTTLING_STATE info, uint size);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr CreateJobObject(IntPtr attributes, string? name);
    [DllImport("kernel32.dll")] static extern bool SetInformationJobObject(IntPtr job, int infoClass, IntPtr info, uint length);
    [DllImport("kernel32.dll")] public static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
    [DllImport("kernel32.dll")] public static extern bool CloseHandle(IntPtr handle);
}
