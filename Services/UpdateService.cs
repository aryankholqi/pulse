using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Pulse.Services;

public sealed record UpdateInfo(Version Version, string Notes, string InstallerUrl, string? ChecksumUrl, long Size);

/// <summary>
/// Looks for a newer Pulse on GitHub Releases, downloads its installer and runs it.
/// The installer (Inno Setup) does the actual upgrade: it closes Pulse, keeps settings and,
/// with /RELAUNCH, starts the new version when it's done.
/// </summary>
public static class UpdateService
{
    const string Repo = "aryankholqi/pulse";
    const string LatestUrl = $"https://api.github.com/repos/{Repo}/releases/latest";

    static readonly Regex InstallerName = new(@"^Pulse-Setup-[\d.]+\.exe$", RegexOptions.IgnoreCase);

    // Declared before Http: static initialisers run top to bottom, and the client's User-Agent needs it.
    public static Version Current { get; } = Normalize(Assembly.GetEntryAssembly()?.GetName().Version ?? new Version(0, 0, 0));

    static readonly HttpClient Http = CreateClient();

    static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"Pulse/{Current}"); // GitHub's API rejects requests without one
        http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return http;
    }

    /// <summary>The latest release if it's newer than this build, else null. Throws when GitHub can't be reached.</summary>
    public static async Task<UpdateInfo?> CheckAsync(CancellationToken token = default)
    {
        using var response = await Http.GetAsync(LatestUrl, token).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null; // no releases yet
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
        using var json = await JsonDocument.ParseAsync(stream, cancellationToken: token).ConfigureAwait(false);
        var root = json.RootElement;

        if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
        if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;
        if (!TryParseVersion(root.GetProperty("tag_name").GetString(), out var version) || version <= Current) return null;

        string? installerUrl = null, installerName = null, checksumUrl = null;
        long size = 0;
        foreach (var asset in root.GetProperty("assets").EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (InstallerName.IsMatch(name))
            {
                installerName = name;
                installerUrl = asset.GetProperty("browser_download_url").GetString();
                size = asset.GetProperty("size").GetInt64();
            }
        }
        if (installerUrl is null) return null; // a release without an installer isn't something we can apply

        foreach (var asset in root.GetProperty("assets").EnumerateArray())
            if (string.Equals(asset.GetProperty("name").GetString(), installerName + ".sha256", StringComparison.OrdinalIgnoreCase))
                checksumUrl = asset.GetProperty("browser_download_url").GetString();

        string notes = root.TryGetProperty("body", out var body) ? CleanNotes(body.GetString()) : "";
        return new UpdateInfo(version, notes, installerUrl, checksumUrl, size);
    }

    /// <summary>Downloads the installer to %TEMP% and checks its SHA-256 when the release publishes one.</summary>
    public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken token = default)
    {
        if (!IsGitHub(update.InstallerUrl)) throw new InvalidOperationException("Unexpected download location.");

        string folder = Path.Combine(Path.GetTempPath(), "Pulse", "update");
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, $"Pulse-Setup-{update.Version}.exe");

        using (var response = await Http.GetAsync(update.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            long total = response.Content.Headers.ContentLength ?? update.Size;

            await using var source = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            await using var target = File.Create(path);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, token).ConfigureAwait(false)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), token).ConfigureAwait(false);
                done += read;
                if (total > 0) progress?.Report((double)done / total);
            }
        }

        if (update.ChecksumUrl is { } sumUrl && IsGitHub(sumUrl))
        {
            string expected = (await Http.GetStringAsync(sumUrl, token).ConfigureAwait(false)).Trim().Split(' ', '\t')[0];
            string actual;
            await using (var file = File.OpenRead(path))
                actual = Convert.ToHexString(await SHA256.HashDataAsync(file, token).ConfigureAwait(false));

            if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidDataException("The download is damaged (checksum mismatch).");
            }
        }

        return path;
    }

    /// <summary>
    /// Starts the installer silently (it shows only a progress bar). Pulse is already elevated,
    /// so there's no UAC prompt. The caller should exit right after.
    /// </summary>
    public static void Install(string installerPath) =>
        Process.Start(new ProcessStartInfo(installerPath, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH") { UseShellExecute = true });

    static bool IsGitHub(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps && uri.Host == "github.com";

    static bool TryParseVersion(string? tag, out Version version)
    {
        version = new Version(0, 0, 0);
        if (tag is null || !Version.TryParse(tag.Trim().TrimStart('v', 'V'), out var v)) return false;
        version = Normalize(v);
        return true;
    }

    // 1.2 → 1.2.0 and 1.2.3.0 → 1.2.3, so "1.2.3" from a tag equals the assembly's 1.2.3.0
    static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    /// <summary>Release notes are Markdown; the dialog shows plain text.</summary>
    static string CleanNotes(string? markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        string text = markdown.Replace("\r\n", "\n");
        text = Regex.Replace(text, @"^#{1,6}\s*", "", RegexOptions.Multiline);            // headings
        text = Regex.Replace(text, @"^\s*[-*]\s+", "•  ", RegexOptions.Multiline);          // bullets
        text = Regex.Replace(text, @"\[([^\]]+)\]\([^)]+\)", "$1");                         // links → their text
        text = Regex.Replace(text, @"(\*\*|__|`)", "");                                    // bold / code marks
        text = Regex.Replace(text, @"\n{3,}", "\n\n");
        return text.Trim();
    }
}
