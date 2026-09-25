using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulse;

public enum Corner { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight }

public enum AppLanguage { English, Persian }

public sealed class AppSettings
{
    public const string DefaultFpsColor = "#F6F1E7";
    public const string DefaultGpuColor = "#B39DFF";
    public const string DefaultCpuColor = "#7CC8FF";
    public const string DefaultRamColor = "#FF9A85";

    public Corner Corner { get; set; } = Corner.TopLeft;
    public bool Compact { get; set; } = true;
    public double Scale { get; set; } = 1.0;
    public double BackgroundOpacity { get; set; } = 0.92;

    public AppLanguage Language { get; set; } = AppLanguage.English;

    // ── what the overlay shows (at least MinMetrics stay on) ──
    public const int MinMetrics = 2;
    public bool ShowFps { get; set; } = true;
    public bool ShowGpu { get; set; } = true;
    public bool ShowCpu { get; set; } = true;
    public bool ShowRam { get; set; } = true;

    /// <summary>GPU hot spot (junction) temp next to the core temp. Opt-in; hidden anyway on GPUs that don't report it.</summary>
    public bool ShowGpuHotspot { get; set; }

    /// <summary>VRAM used in the compact line (the full layout always shows it). Opt-in.</summary>
    public bool ShowCompactVram { get; set; }

    [JsonIgnore]
    public int MetricCount => (ShowFps ? 1 : 0) + (ShowGpu ? 1 : 0) + (ShowCpu ? 1 : 0) + (ShowRam ? 1 : 0);

    /// <summary>Open the customise window on launch (logon starts skip it: see <c>--tray</c>).</summary>
    public bool ShowSettingsOnLaunch { get; set; } = true;

    // ── updates (GitHub Releases) ──
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>A version the user chose "Skip this version" for: never offered again automatically.</summary>
    public string? SkippedVersion { get; set; }

    // ── colours (#RRGGBB) ──
    public string FpsColor { get; set; } = DefaultFpsColor;
    public string GpuColor { get; set; } = DefaultGpuColor;
    public string CpuColor { get; set; } = DefaultCpuColor;
    public string RamColor { get; set; } = DefaultRamColor;

    /// <summary>FPS turns amber below 60 and red below 30, whatever its own colour.</summary>
    public bool FpsWarnings { get; set; } = true;

    // ── preview only ──
    public string PreviewScene { get; set; } = "tlou1";
    public string? PreviewImage { get; set; }
    public bool PreviewHud { get; set; } = true;

    /// <summary>Relative paths are resolved next to Pulse.exe.</summary>
    public string PresentMonPath { get; set; } = @"Tools\PresentMon.exe";

    /// <summary>
    /// Captures every process (we filter to the foreground window ourselves), minus DWM.
    /// Keep "--session_name PulseOverlay" — Pulse stops that ETW session on exit.
    /// </summary>
    public string PresentMonArgs { get; set; } =
        "--output_stdout --stop_existing_session --session_name PulseOverlay --exclude dwm.exe";

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pulse");

    static string FilePath => Path.Combine(Folder, "settings.json");

    static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) is { } s)
            {
                // A hand-edited file with too few metrics: bring them all back.
                if (s.MetricCount < MinMetrics) s.ShowFps = s.ShowGpu = s.ShowCpu = s.ShowRam = true;
                return s;
            }
        }
        catch { /* corrupt file → defaults */ }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Folder);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch { /* settings are best-effort */ }
    }

    public static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppContext.BaseDirectory, path);
}
