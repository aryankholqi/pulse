using System;
using System.Collections.Generic;
using System.Globalization;
using Pulse.Services;

namespace Pulse;

public enum Smoothness { Smooth, SomeHitches, Stuttery }

/// <summary>
/// Reads a finished session like a friend who knows PCs would: one verdict, and the few
/// things worth changing before the next session, most important first.
/// </summary>
internal static class SessionInsights
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    public static double StuttersPerMinute(GameSession s) => s.PlaySeconds > 0 ? s.Stutters / (s.PlaySeconds / 60) : 0;

    /// <summary>1% low against the average, and how often it hitched.</summary>
    public static Smoothness Judge(GameSession s)
    {
        double ratio = s.Low1 is double low && s.AvgFps > 0 ? low / s.AvgFps : 1;
        double perMinute = StuttersPerMinute(s);
        // a rate needs enough stutters behind it: in a short session a handful already looks like a lot
        if (ratio < 0.4 || (perMinute >= 4 && s.Stutters >= 15)) return Smoothness.Stuttery;
        if (ratio < 0.6 || (perMinute >= 1 && s.Stutters >= 5)) return Smoothness.SomeHitches;
        return Smoothness.Smooth;
    }

    /// <summary>At most three tips, in plain words, in the current language.</summary>
    public static List<string> Tips(GameSession s)
    {
        bool fa = Loc.Instance.Language == AppLanguage.Persian;
        var tips = new List<string>();
        void Add(string en, string faText) { if (tips.Count < 3) tips.Add(fa ? faText : en); }

        // 1. VRAM running out is the most common hidden cause of stutter
        if (s.VramPeakMb is float peak && s.VramTotalMb is float total && total > 0 && peak / total >= 0.95)
        {
            string used = (peak / 1024).ToString("0.0", Inv), all = (total / 1024).ToString("0", Inv);
            Add($"Video memory was full at times ({used} of {all} GB). Lower texture quality one step to avoid stutter.",
                $"حافظه‌ی کارت گرافیک گاهی پر شد ({used} از {all} گیگ). کیفیت تکسچر را یک پله پایین بیاور تا افت ناگهانی نداشته باشی.");
        }

        // 2. heat
        if (s.GpuTempMax is float gpuHot && gpuHot >= 85)
        {
            string t = gpuHot.ToString("0", Inv);
            Add($"Your graphics card reached {t}°C, where it may slow itself down. Clean the dust, improve case airflow or raise the fan curve.",
                $"کارت گرافیک به {t} درجه رسید؛ در این دما ممکن است خودش را کند کند. گردوغبار را تمیز کن، جریان هوای کیس را بهتر کن یا دور فن را بالاتر ببر.");
        }
        if (s.CpuTempMax is float cpuHot && cpuHot >= 90)
        {
            string t = cpuHot.ToString("0", Inv);
            Add($"Your processor reached {t}°C. Check that the CPU cooler is seated well and its fans are clean.",
                $"پردازنده به {t} درجه رسید. بررسی کن خنک‌کننده‌ی CPU درست نصب شده و فن‌هایش تمیز است.");
        }

        // 3. RAM: one stick halves memory bandwidth; filling it up means swapping to disk
        if (s.RamModules is { Channels: 1 } ram && s.CpuBound >= 0.2)
        {
            int gb = Math.Max(1, ram.TotalGb / ram.Sticks);
            Add($"Your RAM runs in single channel (one stick). A second matching {gb} GB stick doubles its bandwidth, and CPU-bound scenes gain the most.",
                $"رمت تک‌کاناله است (فقط یک ماژول). یک رم {gb} گیگ دیگر از همان مدل پهنای باند را دو برابر می‌کند و صحنه‌های CPU-bound بیشترین سود را می‌برند.");
        }
        if (s.RamPeakGb is float ramPeak && s.RamTotalGb is float ramTotal && ramTotal > 0 && ramPeak / ramTotal >= 0.9)
        {
            string used = ramPeak.ToString("0.0", Inv), all = ramTotal.ToString("0", Inv);
            Add($"RAM was nearly full ({used} of {all} GB), which causes hitches. Close browsers and launchers while playing, or move up to more RAM.",
                $"رم تقریباً پر شد ({used} از {all} گیگ) و این باعث افت ناگهانی می‌شود. موقع بازی مرورگر و لانچرها را ببند یا رم بیشتری بگذار.");
        }

        // 4. stutter
        var smoothness = Judge(s);
        if (smoothness != Smoothness.Smooth && s.Capped is not > 0.6)
        {
            // a limit a bit under the average keeps frame times even
            int cap = (int)(Math.Floor(s.AvgFps * 0.85 / 5) * 5);
            if (cap >= 30)
                Add($"FPS jumped around a lot. An FPS limit of about {cap} (in the game or the NVIDIA/AMD app) makes it feel steadier.",
                    $"FPS زیاد بالا و پایین رفت. یک سقف FPS حدود {cap} (داخل بازی یا برنامه‌ی NVIDIA/AMD) بازی را یکنواخت‌تر می‌کند.");
        }

        // 5. what limited it, when one side clearly dominated
        if (s.GpuBound >= 0.6)
            Add("Mostly GPU-bound. For more FPS, DLSS, FSR or XeSS on Quality is the easiest win.",
                "بیشتر وقت GPU-bound بود. برای FPS بیشتر، ساده‌ترین راه روشن کردن DLSS، FSR یا XeSS روی Quality است.");
        else if (s.CpuBound >= 0.4)
            Add("Often CPU-bound. Lower view distance and crowd density; lowering graphics won't help much.",
                "خیلی وقت‌ها CPU-bound بود. فاصله‌ی دید و تراکم جمعیت را کم کن؛ پایین آوردن گرافیک کمک زیادی نمی‌کند.");
        else if (s.Capped >= 0.6)
            Add("Held by V-Sync or an FPS limit most of the time: your PC had power to spare. Raise the limit if you want more FPS.",
                "بیشتر وقت V-Sync یا سقف FPS جلویش را گرفته بود و سیستمت توان اضافه داشت. اگر FPS بیشتر می‌خواهی، سقف را بالاتر ببر.");

        if (tips.Count == 0)
            Add("A clean session. Nothing to fix.", "یک بازی تمیز. چیزی برای درست کردن نیست.");
        return tips;
    }
}
