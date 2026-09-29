using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Pulse;

/// <summary>
/// A game's own icon, read from its .exe, for the avatars on the game profiles page.
/// Icons are cached for the whole run: each .exe is read once.
/// </summary>
internal static class GameIcons
{
    const int Size = 64; // px: sharp on a ~44 DIP tile up to 150% scaling

    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The icon inside <paramref name="exePath"/>, or null (no path, file gone, or the .exe has none).</summary>
    public static ImageSource? For(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        if (Cache.TryGetValue(exePath, out var cached)) return cached;

        ImageSource? icon = null;
        try
        {
            if (File.Exists(exePath))
            {
                using var extracted = System.Drawing.Icon.ExtractIcon(exePath, 0, Size);
                if (extracted != null)
                {
                    var source = Imaging.CreateBitmapSourceFromHIcon(extracted.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    source.Freeze();
                    icon = source;
                }
            }
        }
        catch { /* unreadable .exe: the letter tile it is */ }

        Cache[exePath] = icon;
        return icon;
    }

    /// <summary>Where <paramref name="process"/>'s .exe lives: a recorded session knows, or the game is running now.</summary>
    public static string? FindExe(string process, bool lookAtRunning = true)
    {
        foreach (var session in SessionHistory.Recent)
            if (session.Process.Equals(process, StringComparison.OrdinalIgnoreCase) && session.ExePath is { } path && File.Exists(path))
                return path;

        if (!lookAtRunning) return null;
        Process[] running;
        try { running = Process.GetProcessesByName(process); }
        catch { return null; }

        string? found = null;
        foreach (var p in running)
        {
            using (p) found ??= Native.ExePathOf(p.Id);
        }
        return found;
    }
}
