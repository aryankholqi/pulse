using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using Pulse.Services;

namespace Pulse;

/// <summary>
/// The latest game sessions, kept in %AppData%\Pulse\sessions.json: the tray reopens the last
/// summary from it, and each summary compares itself with the previous session of the same game.
/// Saved sessions are kept on top of the latest <see cref="Keep"/>, until the player deletes them.
/// Benchmark runs are saved from the start, and compare only with other runs of the same game.
/// </summary>
internal static class SessionHistory
{
    const int Keep = 100;

    /// <summary>A session was saved, unsaved or deleted (UI thread): open windows refresh.</summary>
    public static event Action? Changed;

    public static bool IsSaved(GameSession session) => IndexOf(session) is int i and >= 0 && Sessions[i].Saved;

    /// <summary>Saved summaries, newest first.</summary>
    public static List<GameSession> SavedSessions
    {
        get
        {
            var saved = Sessions.FindAll(s => s.Saved);
            saved.Reverse();
            return saved;
        }
    }

    /// <summary>Saves or unsaves <paramref name="session"/>; returns the stored copy.</summary>
    public static GameSession SetSaved(GameSession session, bool saved)
    {
        var updated = session with { Saved = saved };
        int i = IndexOf(session);
        if (i >= 0) Sessions[i] = updated;
        else Sessions.Add(updated); // trimmed out of the history meanwhile: saving brings it back
        Trim();
        Save();
        Changed?.Invoke();
        return updated;
    }

    /// <summary>Names a benchmark run ("DLSS Quality"); blank clears it. Returns the stored copy.</summary>
    public static GameSession SetLabel(GameSession session, string? label)
    {
        label = string.IsNullOrWhiteSpace(label) ? null : label.Trim();
        int i = IndexOf(session);
        if (i < 0 || Sessions[i].Label == label) return i < 0 ? session : Sessions[i];
        Sessions[i] = Sessions[i] with { Label = label };
        Save();
        Changed?.Invoke();
        return Sessions[i];
    }

    /// <summary>Removes the session from the records for good.</summary>
    public static void Delete(GameSession session)
    {
        int i = IndexOf(session);
        if (i < 0) return;
        Sessions.RemoveAt(i);
        Save();
        Changed?.Invoke();
    }

    /// <summary>A session is one game process started at one moment.</summary>
    static int IndexOf(GameSession session) =>
        Sessions.FindIndex(s => s.Started == session.Started && s.Process.Equals(session.Process, StringComparison.OrdinalIgnoreCase));

    /// <summary>Drops the oldest unsaved sessions beyond <see cref="Keep"/>.</summary>
    static void Trim()
    {
        int extra = Sessions.FindAll(s => !s.Saved).Count - Keep;
        for (int i = 0; i < Sessions.Count && extra > 0;)
        {
            if (!Sessions[i].Saved) { Sessions.RemoveAt(i); extra--; }
            else i++;
        }
    }

    static string FilePath => Path.Combine(AppSettings.Folder, "sessions.json");
    static List<GameSession>? _sessions;

    static List<GameSession> Sessions => _sessions ??= Load();

    public static GameSession? Latest => Sessions.Count > 0 ? Sessions[^1] : null;

    /// <summary>Every recorded session, newest first.</summary>
    public static IEnumerable<GameSession> Recent
    {
        get
        {
            for (int i = Sessions.Count - 1; i >= 0; i--) yield return Sessions[i];
        }
    }

    /// <summary>
    /// The session of the same game played before <paramref name="session"/>, if any: a benchmark
    /// run compares with the run before it, a session with the session before it.
    /// </summary>
    public static GameSession? Previous(GameSession session)
    {
        for (int i = Sessions.Count - 1; i >= 0; i--)
        {
            var s = Sessions[i];
            if (s.Ended <= session.Started && s.Benchmark == session.Benchmark
                && s.Process.Equals(session.Process, StringComparison.OrdinalIgnoreCase))
                return s;
        }
        return null;
    }

    /// <summary>The other benchmark runs of the same game as <paramref name="run"/>, newest first.</summary>
    public static List<GameSession> OtherRuns(GameSession run)
    {
        var runs = new List<GameSession>();
        foreach (var s in Recent)
            if (s.Benchmark && s.Process.Equals(run.Process, StringComparison.OrdinalIgnoreCase) && s.Started != run.Started)
                runs.Add(s);
        return runs;
    }

    public static void Add(GameSession session)
    {
        Sessions.Add(session);
        Trim();
        Save();
    }

    static List<GameSession> Load()
    {
        try
        {
            if (File.Exists(FilePath) && JsonSerializer.Deserialize<List<GameSession>>(File.ReadAllText(FilePath)) is { } list)
            {
                list.RemoveAll(s => s is null || s.Timeline is null);
                return list;
            }
        }
        catch { /* corrupt file → start over */ }
        return new List<GameSession>();
    }

    static void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Folder);
            // write aside, then swap: a crash mid-write never leaves a half file behind
            string temp = FilePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Sessions));
            File.Move(temp, FilePath, overwrite: true);
        }
        catch { /* history is best-effort */ }
    }
}
