using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pulse;

public enum Corner { TopLeft, TopRight, BottomLeft, BottomRight }

public sealed class AppSettings
{
    public Corner Corner { get; set; } = Corner.TopLeft;
    public bool Compact { get; set; }
    public double Scale { get; set; } = 1.0;
    public double BackgroundOpacity { get; set; } = 0.92;

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
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
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
