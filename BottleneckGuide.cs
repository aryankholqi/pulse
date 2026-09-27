using Pulse.Services;

namespace Pulse;

/// <summary>
/// What GPU-bound, CPU-bound and Capped mean, in plain words, and what the player can do
/// about each one. Shown under the "What limits your FPS" switch in the settings window.
/// </summary>
internal static class BottleneckGuide
{
    public sealed record Mode(Bottleneck Kind, string Tag, (string En, string Fa) Meaning, (string En, string Fa)[] Tips);

    public static readonly Mode[] Modes =
    {
        new(Bottleneck.Gpu, "GPU-bound",
            ("Your graphics card is running flat out, so it decides your FPS. That's normal, and usually what you want: " +
             "you're getting everything your GPU has.",
             "کارت گرافیک با تمام توان کار می‌کند و همین است که FPS را تعیین می‌کند. این حالت عادی است و معمولاً همان چیزی است که می‌خواهی: " +
             "داری از تمام توان کارت گرافیکت استفاده می‌کنی."),
            new[]
            {
                ("Turn on DLSS, FSR or XeSS on Quality or Balanced. It's the biggest boost for the smallest loss in image quality.",
                 "DLSS، FSR یا XeSS را روی Quality یا Balanced روشن کن. بیشترین افزایش FPS را با کمترین افت کیفیت تصویر می‌دهد."),
                ("Lower the heaviest settings first: ray tracing, shadows, volumetric fog and clouds, and anti-aliasing.",
                 "اول سنگین‌ترین تنظیمات را پایین بیاور: Ray Tracing، سایه‌ها، مه و ابرهای حجمی و Anti-Aliasing."),
                ("Keep texture quality high as long as VRAM isn't full. It barely costs FPS.",
                 "تا وقتی VRAM پر نشده، کیفیت تکسچر را بالا نگه دار. تقریباً روی FPS اثری ندارد."),
                ("Happy with your FPS? There's nothing to fix.",
                 "از FPS راضی هستی؟ چیزی برای درست کردن نیست."),
            }),

        new(Bottleneck.Cpu, "CPU-bound",
            ("Your processor can't prepare frames fast enough, so the graphics card sits waiting. Common in busy multiplayer, " +
             "strategy and open-world games, and at 1080p with a strong graphics card.",
             "پردازنده نمی‌تواند فریم‌ها را به‌اندازه‌ی کافی سریع آماده کند و کارت گرافیک منتظر می‌ماند. در بازی‌های آنلاین شلوغ، " +
             "استراتژی و جهان‌باز، و در رزولوشن 1080p با کارت گرافیک قوی زیاد پیش می‌آید."),
            new[]
            {
                ("Lowering graphics or resolution won't help much here. You can even raise them without losing FPS.",
                 "پایین آوردن گرافیک یا رزولوشن اینجا کمک زیادی نمی‌کند. حتی می‌توانی بدون افت FPS آن‌ها را بالاتر ببری."),
                ("Lower the settings that load the processor instead: view distance, crowd and NPC density, physics and simulation.",
                 "به‌جایش تنظیماتی را که به پردازنده فشار می‌آورند پایین بیاور: فاصله‌ی دید، تراکم جمعیت و NPCها، فیزیک و شبیه‌سازی."),
                ("Close apps running in the background, like browsers, launchers and recording software.",
                 "برنامه‌های پس‌زمینه مثل مرورگر، لانچرها و نرم‌افزار ضبط را ببند."),
                ("Set the Windows power mode to Best performance, and keep a laptop plugged in.",
                 "حالت پاور ویندوز را روی Best performance بگذار و لپ‌تاپ را به شارژ وصل نگه دار."),
                ("Turn on XMP or EXPO in the BIOS so your RAM runs at its rated speed.",
                 "XMP یا EXPO را در BIOS روشن کن تا رم با سرعت واقعی‌اش کار کند."),
                ("Frame generation (DLSS or FSR) makes the game look smoother even when the processor is the limit.",
                 "Frame Generation (در DLSS یا FSR) حتی وقتی پردازنده محدودکننده است، بازی را روان‌تر نشان می‌دهد."),
            }),

        new(Bottleneck.Capped, "Capped",
            ("Something is holding FPS at a set number on purpose: V-Sync, an FPS limit in the game, or one in the NVIDIA or AMD app. " +
             "Your PC has power to spare.",
             "چیزی عمداً FPS را روی یک عدد ثابت نگه داشته: V-Sync، محدودیت FPS داخل بازی، یا محدودیت در برنامه‌ی NVIDIA یا AMD. " +
             "سیستمت هنوز توان اضافه دارد."),
            new[]
            {
                ("This is fine. Steady FPS feels smooth, and your PC runs cooler and quieter.",
                 "مشکلی نیست. FPS ثابت حس روان‌تری دارد و سیستم خنک‌تر و بی‌صداتر کار می‌کند."),
                ("Want more FPS? Turn off V-Sync or raise the frame limit, in the game and in NVIDIA Control Panel or AMD Software.",
                 "FPS بیشتر می‌خواهی؟ V-Sync را خاموش کن یا سقف فریم را بالاتر ببر؛ هم داخل بازی، هم در NVIDIA Control Panel یا AMD Software."),
                ("With G-Sync or FreeSync, a limit a few FPS below your monitor's refresh rate is the smoothest setup " +
                 "(for example 141 on a 144 Hz screen).",
                 "اگر G-Sync یا FreeSync داری، سقفی چند فریم کمتر از رفرش‌ریت مانیتور روان‌ترین حالت است " +
                 "(مثلاً 141 برای مانیتور 144 هرتز)."),
                ("Stuck at 30 or 60 for no reason? Check battery saver and the laptop's power mode.",
                 "بی‌دلیل روی 30 یا 60 گیر کرده؟ Battery saver و حالت پاور لپ‌تاپ را چک کن."),
            }),
    };

    public static readonly (string En, string Fa) Footnote =
        ("The tag can change from scene to scene. Go by what it shows most of the time.",
         "این برچسب ممکن است از صحنه‌ای به صحنه‌ی دیگر عوض شود. به چیزی نگاه کن که بیشتر وقت‌ها نشان می‌دهد.");
}
