using System.Text.Json.Serialization;

namespace Pulse;

/// <summary>
/// How the overlay looks: layout, what it shows, where it sits, its colours and size.
/// <see cref="AppSettings"/> holds the default look; each <see cref="GameProfile"/> holds its own.
/// </summary>
public class OverlayStyle
{
    public const string DefaultFpsColor = "#F6F1E7";
    public const string DefaultGpuColor = "#B39DFF";
    public const string DefaultCpuColor = "#7CC8FF";
    public const string DefaultRamColor = "#FF9A85";

    // ── text: numbers (readings) and labels (tags, names, units) ──
    public const string DefaultNumberColor = "#F6F1E7";
    public const string DefaultLabelColor = "#A3AAC2";
    public const double MinTextSize = 0.8, MaxTextSize = 1.5;

    public Corner Corner { get; set; } = Corner.TopLeft;

    /// <summary>
    /// With <see cref="Corner.Custom"/>: the card's top-left as a share (0–1) of the room the screen leaves around it.
    /// A share, not pixels, so the spot survives another resolution or a bigger card and never leaves the screen.
    /// </summary>
    public double CustomX { get; set; } = 0.5;
    public double CustomY { get; set; } = 0.5;

    public bool Compact { get; set; } = true;
    public double Scale { get; set; } = 1.0;
    public double BackgroundOpacity { get; set; } = 0.92;

    // ── what the overlay shows (at least MinMetrics stay on) ──
    public const int MinMetrics = 2;
    public bool ShowFps { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;

    /// <summary>GPU-bound / CPU-bound / Capped next to the FPS. Opt-in; hidden while PresentMon can't tell.</summary>
    public bool ShowBottleneck { get; set; }

    /// <summary>GPU hot spot (junction) temp next to the core temp. Opt-in; hidden anyway on GPUs that don't report it.</summary>
    public bool ShowGpuHotspot { get; set; }

    /// <summary>VRAM used in the compact line (the full layout always shows it). Opt-in.</summary>
    public bool ShowCompactVram { get; set; }

    [JsonIgnore]
    public int MetricCount => (ShowFps ? 1 : 0) + (ShowGpu ? 1 : 0) + (ShowCpu ? 1 : 0) + (ShowRam ? 1 : 0);

    // ── colours (#RRGGBB) ──
    public string FpsColor { get; set; } = DefaultFpsColor;
    public string GpuColor { get; set; } = DefaultGpuColor;
    public string CpuColor { get; set; } = DefaultCpuColor;
    public string RamColor { get; set; } = DefaultRamColor;

    /// <summary>FPS turns amber below 60 and red below 30, whatever its own colour.</summary>
    public bool FpsWarnings { get; set; } = true;

    // ── text ──
    /// <summary>A share of the stock sizes (1 = as designed), so the hierarchy — big FPS, small notes — stays.</summary>
    public double NumberSize { get; set; } = 1.0;
    public string NumberColor { get; set; } = DefaultNumberColor;
    /// <summary>Temperatures speak the thermal colours (cool → warm → hot); off: they wear <see cref="NumberColor"/>.</summary>
    public bool HeatColors { get; set; } = true;

    public double LabelSize { get; set; } = 1.0;
    /// <summary>Names, units and notes. The GPU / CPU / RAM tags keep their device colours.</summary>
    public string LabelColor { get; set; } = DefaultLabelColor;

    /// <summary>Takes on every look setting of <paramref name="other"/>.</summary>
    public void CopyStyleFrom(OverlayStyle other)
    {
        Corner = other.Corner;
        CustomX = other.CustomX;
        CustomY = other.CustomY;
        Compact = other.Compact;
        Scale = other.Scale;
        BackgroundOpacity = other.BackgroundOpacity;
        ShowFps = other.ShowFps;
        ShowGpu = other.ShowGpu;
        ShowCpu = other.ShowCpu;
        ShowRam = other.ShowRam;
        ShowBottleneck = other.ShowBottleneck;
        ShowGpuHotspot = other.ShowGpuHotspot;
        ShowCompactVram = other.ShowCompactVram;
        FpsColor = other.FpsColor;
        GpuColor = other.GpuColor;
        CpuColor = other.CpuColor;
        RamColor = other.RamColor;
        FpsWarnings = other.FpsWarnings;
        NumberSize = other.NumberSize;
        NumberColor = other.NumberColor;
        HeatColors = other.HeatColors;
        LabelSize = other.LabelSize;
        LabelColor = other.LabelColor;
    }

    /// <summary>Every text setting back to the stock look.</summary>
    public void ResetText()
    {
        NumberSize = 1.0;
        NumberColor = DefaultNumberColor;
        HeatColors = true;
        LabelSize = 1.0;
        LabelColor = DefaultLabelColor;
    }

    /// <summary>A hand-edited file with too few metrics: bring them all back.</summary>
    internal void FixMetrics()
    {
        if (MetricCount < MinMetrics) ShowFps = ShowGpu = ShowCpu = ShowRam = true;
    }
}

/// <summary>An overlay look of its own for one game, picked by its process name while that game is in front.</summary>
public sealed class GameProfile : OverlayStyle
{
    /// <summary>Process name without ".exe", as PresentMon and Task Manager know it.</summary>
    public string Process { get; set; } = "";

    /// <summary>What the player sees: the game's window title, or the process name.</summary>
    public string Name { get; set; } = "";

    /// <summary>The game's .exe, for its icon. Found from a recorded session or the running game, or picked by the player.</summary>
    public string? ExePath { get; set; }

    /// <summary>Off: the game gets the default look, and the profile is kept for later.</summary>
    public bool Enabled { get; set; } = true;
}
