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
        new(new Version(2, 7, 0), new[]
        {
            ("New: choose the size and color of the overlay's numbers, and of its labels and names. " +
             "Find it under Text in the overlay editor, with a full-size sample of your overlay that shows every change as you make it.",
             "جدید: اندازه و رنگ اعداد اورلی و برچسب‌ها و نام‌هایش را خودت انتخاب کن. " +
             "در بخش «متن» ویرایشگر اورلی است، با یک نمونه از اورلی‌ات در اندازه‌ی واقعی که هر تغییر را همان لحظه نشان می‌دهد."),
            ("You can now turn off heat colors, so temperatures use the same color as the other numbers.",
             "حالا می‌توانی رنگ گرما را خاموش کنی تا دماها هم رنگ بقیه‌ی اعداد باشند."),
            ("Drag the overlay anywhere in the preview, right up to the edges of the screen. " +
             "It no longer jumps to the nearest preset spot, so a corner with no gap at all is easy. " +
             "The six preset spots are still one click away under Position on screen.",
             "اورلی را در پیش‌نمایش هر جا خواستی بکش، تا خودِ لبه‌های صفحه. " +
             "دیگر خودش به نزدیک‌ترین جای آماده نمی‌پرد، پس گذاشتنش در گوشه‌ی صفحه بدون هیچ فاصله‌ای ساده است. " +
             "شش جای آماده هنوز با یک کلیک در «جای اورلی در صفحه» در دسترس‌اند."),
            ("Changes in the overlay editor now show up instantly.",
             "تغییرات در ویرایشگر اورلی حالا بی‌درنگ دیده می‌شوند."),
            ("On graphics cards that don't report a hot spot temperature (such as the RX 580), that switch is now turned off and explains why.",
             "روی کارت‌های گرافیکی که دمای هات‌اسپات را گزارش نمی‌دهند (مثل RX 580)، این گزینه حالا غیرفعال است و دلیلش را توضیح می‌دهد."),
            ("NVIDIA cards: Pulse only reads the hot spot while you show it, which avoids a small hitch every 10 seconds in games.",
             "کارت‌های انویدیا: Pulse فقط وقتی هات‌اسپات را نمایش می‌دهی آن را می‌خواند، تا هر ۱۰ ثانیه یک افت کوچک در بازی پیش نیاید."),
        }),
        new(new Version(2, 6, 0), new[]
        {
            ("New: a summary after every game. Close a game and Pulse shows how it went: average FPS, 1% and 0.1% lows, " +
             "stutters, FPS over the session, what limited it, temperatures, and tips for next time. " +
             "It also compares with your last session of the same game.",
             "جدید: خلاصه بعد از هر بازی. وقتی بازی را ببندی، Pulse نشان می‌دهد بازی چطور گذشت: میانگین FPS، 1% و 0.1% low، " +
             "افت‌های ناگهانی، FPS در طول بازی، چه چیزی FPS را محدود کرد، دماها و پیشنهاد برای دفعه‌ی بعد. " +
             "با دفعه‌ی قبلی که همان بازی را اجرا کردی هم مقایسه می‌کند."),
            ("The summary also looks at your RAM: its speed, single or dual channel, and roughly how much FPS " +
             "better RAM would have added in that game.",
             "خلاصه رمت را هم بررسی می‌کند: سرعتش، تک‌کاناله یا دوکاناله بودنش، و اینکه رم بهتر تقریباً چقدر FPS " +
             "به همان بازی اضافه می‌کرد."),
            ("Save the summaries you want to keep and open them later from Saved summaries in the settings or the tray menu. " +
             "Last game summary… in the tray reopens the latest one. Don't want it after every game? Turn it off in General.",
             "خلاصه‌هایی را که می‌خواهی نگه داری ذخیره کن و بعداً از «خلاصه‌های ذخیره‌شده» در تنظیمات یا منوی سینی باز کن. " +
             "«خلاصه‌ی آخرین بازی…» در منوی سینی آخرین خلاصه را دوباره باز می‌کند. اگر نمی‌خواهی بعد از هر بازی باز شود، در «عمومی» خاموشش کن."),
            ("New: a short guide explains what GPU-bound, CPU-bound and Capped mean, and what you can do about each one " +
             "to get more FPS. Open it with the Guide button under Show on overlay → FPS.",
             "جدید: یک راهنمای کوتاه توضیح می‌دهد GPU-bound، CPU-bound و Capped یعنی چه و در هر حالت برای FPS بیشتر چه کار می‌توانی بکنی. " +
             "آن را با دکمه‌ی «راهنما» در «آیتم‌های اورلی ← FPS» باز کن."),
            ("With V-Sync on, Pulse now correctly shows Capped instead of GPU-bound.",
             "وقتی V-Sync روشن است، Pulse حالا به‌درستی Capped نشان می‌دهد، نه GPU-bound."),
            ("NVIDIA cards: Pulse now reads the GPU with far less work each second, which avoids small hitches it could cause in games.",
             "کارت‌های انویدیا: Pulse حالا هر ثانیه با کار خیلی کمتری کارت گرافیک را می‌خواند، تا باعث افت‌های کوچک در بازی نشود."),
        }),
        new(new Version(2, 5, 0), new[]
        {
            ("New: Pulse can show what limits your FPS: GPU-bound, CPU-bound or Capped (V-Sync or a frame limiter). " +
             "Turn it on under Show on overlay → FPS.",
             "جدید: Pulse می‌تواند نشان دهد چه چیزی FPS را محدود می‌کند: GPU-bound، CPU-bound یا Capped (V-Sync یا محدودکننده‌ی فریم). " +
             "آن را از «آیتم‌های اورلی ← FPS» روشن کن."),
        }),
        new(new Version(2, 4, 0), new[]
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

    /// <summary>The notes of <paramref name="version"/> itself: empty when it has none (a dev build).</summary>
    public static IReadOnlyList<Release> For(Version version) =>
        Array.FindAll(Releases, r => r.Version == version);

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
