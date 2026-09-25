using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Pulse.Services;

namespace Pulse;

/// <summary>Status colours. All frozen → shared across threads, zero change tracking.</summary>
internal static class Palette
{
    public static readonly Brush Muted = Make(0x5E, 0x66, 0x80);
    public static readonly Brush Cool = Make(0x8F, 0xD8, 0xC8);   // thermal: comfortable
    public static readonly Brush Warm = Make(0xF3, 0xC1, 0x6E);   // thermal: working hard
    public static readonly Brush Hot = Make(0xFF, 0x74, 0x6C);    // thermal: throttling territory
    public static readonly Brush Smooth = Make(0xF6, 0xF1, 0xE7); // fps ≥ 60: calm ivory, not a traffic light
    public static readonly Brush Choppy = Make(0xF3, 0xC1, 0x6E);
    public static readonly Brush Poor = Make(0xFF, 0x74, 0x6C);

    static Brush Make(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }

    public static Brush ForTemp(float? t) => t switch
    {
        null => Muted,
        >= 85 => Hot,
        >= 72 => Warm,
        _ => Cool,
    };

    public static Brush ForFps(double? fps) => fps switch
    {
        null => Muted,
        >= 60 => Smooth,
        >= 30 => Choppy,
        _ => Poor,
    };
}

public sealed class OverlayViewModel : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const string Dash = "–";

    // ── fps ──
    string _targetName = "Waiting for a game";
    string _fpsText = Dash, _fpsLowText = Dash, _frameTimeText = "";
    Brush _fpsBrush = Palette.Muted;
    double[]? _frameTimes;

    public string TargetName { get => _targetName; private set => Set(ref _targetName, value); }
    public string FpsText { get => _fpsText; private set => Set(ref _fpsText, value); }
    public string FpsLowText { get => _fpsLowText; private set => Set(ref _fpsLowText, value); }
    public string FrameTimeText { get => _frameTimeText; private set => Set(ref _frameTimeText, value); }
    public Brush FpsBrush { get => _fpsBrush; private set => Set(ref _fpsBrush, value); }
    public double[]? FrameTimes { get => _frameTimes; private set => Set(ref _frameTimes, value); }

    // ── gpu ──
    string _gpuTempText = Dash, _gpuLoadText = Dash, _gpuName = "", _gpuVramText = "";
    double _gpuLoad;
    Brush _gpuTempBrush = Palette.Muted;

    public string GpuTempText { get => _gpuTempText; private set => Set(ref _gpuTempText, value); }
    public string GpuLoadText { get => _gpuLoadText; private set => Set(ref _gpuLoadText, value); }
    public string GpuName { get => _gpuName; private set => Set(ref _gpuName, value); }
    public string GpuVramText { get => _gpuVramText; private set => Set(ref _gpuVramText, value); }
    public double GpuLoad { get => _gpuLoad; private set => Set(ref _gpuLoad, value); }
    public Brush GpuTempBrush { get => _gpuTempBrush; private set => Set(ref _gpuTempBrush, value); }

    // ── cpu ──
    string _cpuTempText = Dash, _cpuLoadText = Dash, _cpuName = "";
    double _cpuLoad;
    Brush _cpuTempBrush = Palette.Muted;

    public string CpuTempText { get => _cpuTempText; private set => Set(ref _cpuTempText, value); }
    public string CpuLoadText { get => _cpuLoadText; private set => Set(ref _cpuLoadText, value); }
    public string CpuName { get => _cpuName; private set => Set(ref _cpuName, value); }
    public double CpuLoad { get => _cpuLoad; private set => Set(ref _cpuLoad, value); }
    public Brush CpuTempBrush { get => _cpuTempBrush; private set => Set(ref _cpuTempBrush, value); }

    // ── ram ──
    string _ramUsedText = Dash, _ramLoadText = Dash, _ramTotalText = "";
    double _ramLoad;

    public string RamUsedText { get => _ramUsedText; private set => Set(ref _ramUsedText, value); }
    public string RamLoadText { get => _ramLoadText; private set => Set(ref _ramLoadText, value); }
    public string RamTotalText { get => _ramTotalText; private set => Set(ref _ramTotalText, value); }
    public double RamLoad { get => _ramLoad; private set => Set(ref _ramLoad, value); }

    public void Apply(HardwareSnapshot? hw, FpsStats fps, string target, string? fpsError)
    {
        // fps
        bool live = fps.Fps is not null;
        FpsText = live ? Math.Round(fps.Fps!.Value).ToString("0", Inv) : Dash;
        FpsLowText = fps.Low1 is double low ? low.ToString("0", Inv) : Dash;
        FrameTimeText = fps.FrameTimeMs is double ft ? ft.ToString("0.0", Inv) + " ms" : "";
        FpsBrush = Palette.ForFps(fps.Fps);
        FrameTimes = fps.Graph;
        TargetName = live && target.Length > 0 ? target : fpsError ?? "Waiting for a game";

        if (hw is null) return;

        // gpu
        GpuTempText = Temp(hw.GpuTemp);
        GpuTempBrush = Palette.ForTemp(hw.GpuTemp);
        GpuLoad = hw.GpuLoad ?? 0;
        GpuLoadText = Pct(hw.GpuLoad);
        GpuName = ShortGpu(hw.GpuName);
        GpuVramText = hw.VramUsedMb is float used && hw.VramTotalMb is float total && total > 0
            ? $"{(used / 1024).ToString("0.0", Inv)} / {(total / 1024).ToString("0", Inv)} GB VRAM"
            : "";

        // cpu
        CpuTempText = Temp(hw.CpuTemp);
        CpuTempBrush = Palette.ForTemp(hw.CpuTemp);
        CpuLoad = hw.CpuLoad ?? 0;
        CpuLoadText = Pct(hw.CpuLoad);
        CpuName = ShortCpu(hw.CpuName);

        // ram
        RamLoad = hw.RamLoad ?? 0;
        RamLoadText = Pct(hw.RamLoad);
        RamUsedText = hw.RamUsedGb is float u ? u.ToString("0.0", Inv) + "G" : Dash;
        RamTotalText = hw.RamTotalGb is float t ? $"of {t.ToString("0", Inv)} GB" : "";
    }

    static string Temp(float? t) => t is float v ? v.ToString("0", Inv) + "°" : Dash;
    static string Pct(float? p) => p is float v ? v.ToString("0", Inv) + "%" : Dash;

    static string ShortGpu(string? name) => name is null ? "" :
        name.Replace("NVIDIA ", "").Replace("GeForce ", "").Replace("AMD ", "").Trim();

    static readonly Regex CpuNoise = new(@"\((R|TM)\)|\d+(st|nd|rd|th) Gen\s*|\s\d+-Core.*$|\sProcessor.*$|\swith Radeon.*$",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static string ShortCpu(string? name) => name is null ? "" : CpuNoise.Replace(name, "").Trim();

    void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return; // unchanged → no re-render
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }
}
