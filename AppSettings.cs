using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulse;

/// <summary>Where the overlay sits: one of six spots, or <see cref="Custom"/> (anywhere, see <see cref="OverlayStyle.CustomX"/>).</summary>
public enum Corner { TopLeft, TopCenter, TopRight, BottomLeft, BottomCenter, BottomRight, Custom }

public enum AppLanguage { English, Persian }

/// <summary>Everything Pulse remembers. The overlay's default look comes from <see cref="OverlayStyle"/>.</summary>
public sealed class AppSettings : OverlayStyle
{
    public AppLanguage Language { get; set; } = AppLanguage.English;

    /// <summary>Games with an overlay look of their own.</summary>
    public List<GameProfile> Profiles { get; set; } = new();

    /// <summary>The enabled profile for <paramref name="process"/>, if there is one.</summary>
    public GameProfile? ProfileFor(string? process) =>
        string.IsNullOrEmpty(process) ? null
            : Profiles.Find(p => p.Enabled && p.Process.Equals(process, StringComparison.OrdinalIgnoreCase));

    /// <summary>The look the overlay wears while <paramref name="process"/> is in front.</summary>
    public OverlayStyle StyleFor(string? process) => (OverlayStyle?)ProfileFor(process) ?? this;

    /// <summary>The main window's sidebar shows icons only.</summary>
    public bool SidebarCollapsed { get; set; }

    /// <summary>When a game closes, show how the session went. Sessions are recorded either way (tray → last summary).</summary>
    public bool ShowSessionSummary { get; set; } = true;

    /// <summary>How long a benchmark (Ctrl+Shift+B) records, in seconds of play. 0: until the hotkey is pressed again.</summary>
    public int BenchmarkSeconds { get; set; } = 60;

    /// <summary>Open the customise window on launch (logon starts skip it: see <c>--tray</c>).</summary>
    public bool ShowSettingsOnLaunch { get; set; } = true;

    // ── updates (GitHub Releases) ──
    public bool CheckForUpdates { get; set; } = true;

    /// <summary>When Pulse last reached GitHub for a new version (by hand or in the background).</summary>
    public DateTime? LastUpdateCheck { get; set; }

    /// <summary>A version the user chose "Skip this version" for: never offered again automatically.</summary>
    public string? SkippedVersion { get; set; }

    /// <summary>The newest version whose "what's new" the user has seen; a newer Pulse shows its changes once.</summary>
    public string? LastSeenVersion { get; set; }

    /// <summary>No settings file yet: a fresh install, so there's nothing "new" to announce.</summary>
    [JsonIgnore]
    public bool FirstRun { get; private set; }

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
                s.FixMetrics();
                s.BenchmarkSeconds = Math.Clamp(s.BenchmarkSeconds, 0, 3600);
                s.Profiles ??= new();
                s.Profiles.RemoveAll(p => p is null || string.IsNullOrWhiteSpace(p.Process));
                foreach (var p in s.Profiles) p.FixMetrics();
                return s;
            }
        }
        catch { /* corrupt file → defaults */ }
        return new AppSettings { FirstRun = true };
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
