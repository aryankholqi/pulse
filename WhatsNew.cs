using System;
using System.Collections.Generic;

namespace Pulse;

/// <summary>
/// What each release changed, in plain words and both languages. Shown once, the first time
/// the user opens Pulse after an update. Add an entry for every release, newest first; its
/// version must match the release tag (v2.3.1 → 2.3.1).
/// </summary>
internal static class WhatsNew
{
    public sealed record Release(Version Version, (string En, string Fa)[] Changes);

    static readonly Release[] Releases =
    {
        new(new Version(2, 3, 1), new[]
        {
            ("GPU temperature on GeForce RTX 50 cards is now more precise.",
             "دمای کارت گرافیک روی سری GeForce RTX 50 حالا دقیق‌تر است."),
            ("Hot spot temperature is no longer shown on RTX 50 cards. NVIDIA doesn't report it for these cards, " +
             "so the number Pulse showed there was really just the normal GPU temperature.",
             "دمای هات‌اسپات روی کارت‌های RTX 50 دیگر نمایش داده نمی‌شود. انویدیا این دما را برای این کارت‌ها گزارش نمی‌کند " +
             "و عددی که Pulse قبلاً آنجا نشان می‌داد، در واقع همان دمای معمولی کارت بود."),
            ("Nothing changes for other graphics cards.",
             "برای بقیه‌ی کارت‌های گرافیک چیزی تغییر نکرده."),
        }),
    };

    /// <summary>The last release without this popup: upgrades from it have no LastSeenVersion yet.</summary>
    static readonly Version Baseline = new(2, 3, 0);

    /// <summary>Releases after <paramref name="lastSeen"/>, up to <paramref name="current"/>. Newest first.</summary>
    public static IReadOnlyList<Release> Since(string? lastSeen, Version current)
    {
        var from = Version.TryParse(lastSeen, out var v) ? v : Baseline;
        var list = new List<Release>();
        foreach (var release in Releases)
            if (release.Version > from && release.Version <= current) list.Add(release);
        return list;
    }
}
