using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;

namespace Pulse;

/// <summary>
/// UI strings. XAML binds <c>{Binding [Key], Source={x:Static local:Loc.Instance}}</c>,
/// so switching language updates every label live. The overlay itself stays English:
/// "fps", "1% low", "GPU" read the same in every language.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public event PropertyChangedEventHandler? PropertyChanged;

    AppLanguage _language = AppLanguage.English;

    public AppLanguage Language
    {
        get => _language;
        set
        {
            if (_language == value) return;
            _language = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(FlowDirection)));
        }
    }

    public FlowDirection FlowDirection => _language == AppLanguage.Persian ? FlowDirection.RightToLeft : FlowDirection.LeftToRight;

    public string this[string key] =>
        Strings.TryGetValue(key, out var s) ? (_language == AppLanguage.Persian ? s.Fa : s.En) : key;

    public static string T(string key) => Instance[key];

    static readonly Dictionary<string, (string En, string Fa)> Strings = new()
    {
        // ── settings window ──
        // sidebar and pages
        ["NavOverlay"] = ("Overlay", "اورلی"),
        ["NavProfiles"] = ("Game profiles", "پروفایل بازی‌ها"),
        ["NavSummaries"] = ("Summaries", "خلاصه‌ها"),
        ["NavSettings"] = ("Settings", "تنظیمات"),
        ["SidebarCollapse"] = ("Collapse sidebar", "جمع کردن منو"),
        ["SidebarExpand"] = ("Expand sidebar", "باز کردن منو"),
        ["HotkeySidebar"] = ("Collapse or expand the sidebar", "جمع یا باز کردن منوی کناری"),
        ["UpdatesTitle"] = ("Updates", "به‌روزرسانی"),
        ["UpdatesCheck"] = ("Check for updates", "بررسی به‌روزرسانی"),
        ["UpdatesChecking"] = ("Checking…", "در حال بررسی…"),
        ["UpdatesInstall"] = ("Update to {0}", "به‌روزرسانی به {0}"),
        ["UpdatesCurrent"] = ("You're up to date", "آخرین نسخه را داری"),
        ["UpdatesLastChecked"] = ("Last checked", "آخرین بررسی"),
        ["UpdatesAuto"] = ("Pulse looks for new versions by itself", "Pulse خودش دنبال نسخه‌ی جدید می‌گردد"),
        ["UpdatesManual"] = ("Automatic checks are off", "بررسی خودکار خاموش است"),
        ["AutoUpdateHint"] = ("In the background every few hours. It never pops up over a game.",
                              "در پس‌زمینه، هر چند ساعت یک بار. هیچ‌وقت وسط بازی پنجره باز نمی‌کند."),
        ["UpdatesWhatsNew"] = ("What's new in this version", "تغییرات این نسخه"),
        ["OverlayPageHint"] = ("The default look, for every game without a profile of its own.", "ظاهر پیش‌فرض، برای هر بازی‌ای که پروفایل خودش را ندارد."),
        ["SettingsHint"] = ("Language, startup, updates and hotkeys.", "زبان، اجرای خودکار، به‌روزرسانی و کلیدهای میانبر."),
        ["Language"] = ("Language", "زبان"),
        ["Layout"] = ("Layout", "چیدمان"),
        ["Compact"] = ("Compact", "فشرده"),
        ["CompactHint"] = ("One slim line", "یک خط باریک"),
        ["Full"] = ("Full", "کامل"),
        ["FullHint"] = ("Graph and details", "نمودار و جزئیات"),
        ["Metrics"] = ("Show on overlay", "آیتم‌های اورلی"),
        ["MetricsMin"] = ("At least two", "حداقل دو مورد"),
        ["MetricsLocked"] = ("At least two items must stay on", "حداقل دو آیتم باید روشن بماند"),
        ["MetricFpsHint"] = ("Frame rate, 1% low, frame time", "نرخ فریم، 1% low، فریم‌تایم"),
        ["MetricGpuHint"] = ("Temperature, usage, VRAM", "دما، مصرف، VRAM"),
        ["MetricCpuHint"] = ("Temperature, usage", "دما، مصرف"),
        ["MetricRamHint"] = ("Memory used", "حافظه‌ی مصرفی"),
        ["Bottleneck"] = ("What limits your FPS", "چه چیزی FPS را محدود می‌کند"),
        ["BottleneckHint"] = ("GPU-bound, CPU-bound or capped", "GPU-bound، CPU-bound یا Capped"),
        ["BottleneckGuideOpen"] = ("Guide: what each one means and what to do", "راهنما: هر حالت یعنی چه و چه کار کنی"),
        ["BottleneckGuideTitle"] = ("FPS guide", "راهنمای FPS"),
        ["BottleneckGuideSubtitle"] = ("What each tag means, and how to get more FPS", "هر برچسب یعنی چه و چطور FPS بیشتری بگیری"),
        ["BottleneckGuideDo"] = ("What you can do", "چه کار می‌توانی بکنی"),
        ["GpuHotspot"] = ("Hot spot temperature", "دمای هات‌اسپات"),
        ["CompactVram"] = ("VRAM in compact layout", "VRAM در حالت فشرده"),
        ["CompactVramHint"] = ("The full layout always shows VRAM", "حالت کامل همیشه VRAM را نشان می‌دهد"),
        ["GpuHotspotHint"] = ("if your GPU reports it", "اگر کارت گرافیک پشتیبانی کند"),
        ["GpuHotspotUnsupported"] = ("your GPU doesn't report it", "کارت گرافیک شما آن را گزارش نمی‌دهد"),
        ["GpuHotspotUnsupportedTip"] = (
            "Your graphics card has no hot spot sensor, so there is nothing to show. Some cards, such as AMD's RX 400/500 series and NVIDIA's RTX 50 series, report only one GPU temperature, and Pulse already shows it.",
            "کارت گرافیک شما سنسور هات‌اسپات ندارد، پس چیزی برای نمایش نیست. بعضی کارت‌ها، مثل سری RX 400/500 از AMD و سری RTX 50 از NVIDIA، فقط یک دمای GPU گزارش می‌دهند که Pulse همان را نشان می‌دهد."),
        ["Position"] =("Position on screen", "جای اورلی در صفحه"),
        ["TopLeft"] = ("Top left", "بالا چپ"),
        ["TopCenter"] = ("Top center", "بالا وسط"),
        ["TopRight"] = ("Top right", "بالا راست"),
        ["BottomLeft"] = ("Bottom left", "پایین چپ"),
        ["BottomCenter"] = ("Bottom center", "پایین وسط"),
        ["BottomRight"] = ("Bottom right", "پایین راست"),
        ["Colors"] = ("Colors", "رنگ‌ها"),
        ["Custom"] = ("Custom spot", "جای دلخواه"),
        ["PositionDragHint"] = ("Or drag the overlay in the preview to put it anywhere.", "یا اورلی را در پیش‌نمایش بکش و هر جا خواستی بگذار."),
        ["PositionDragTip"] = ("Drag to put it anywhere", "بکش و هر جا خواستی بگذار"),
        ["GlassNone"] = ("None", "بدون"),
        ["PickColor"] = ("Pick any color", "انتخاب رنگ دلخواه"),
        ["FpsWarnings"] = ("Turn FPS amber below 60 and red below 30", "FPS زیر ۶۰ نارنجی و زیر ۳۰ قرمز شود"),
        ["ResetColors"] = ("Reset colors", "رنگ‌های پیش‌فرض"),
        ["Text"] = ("Text", "متن"),
        ["ResetText"] = ("Reset text", "متن پیش‌فرض"),
        ["TextNumbers"] = ("Numbers", "اعداد"),
        ["TextLabels"] = ("Labels & names", "برچسب‌ها و نام‌ها"),
        ["TextSampleTag"] = ("Sample numbers · actual size", "اعداد نمونه · اندازه واقعی"),
        ["TextSampleScroll"] = ("Sample numbers · actual size · drag to see all", "اعداد نمونه · اندازه واقعی · بکش تا همه را ببینی"),
        ["HeatColors"] = ("Temperatures change color with heat", "رنگ دماها با گرما تغییر کند"),
        ["TextNumbersNote"] = ("FPS keeps its own color from Colors above; with heat colors on, so do temperatures.",
                               "FPS رنگ خودش را از بخش رنگ‌ها می‌گیرد؛ با روشن بودن رنگ گرما، دماها هم همین‌طور."),
        ["TextLabelsNote"] = ("The GPU, CPU and RAM tags keep their colors from Colors above; their size follows here.",
                              "برچسب‌های GPU، CPU و RAM رنگشان را از بخش رنگ‌ها می‌گیرند؛ اندازه‌شان از همین‌جا."),
        ["Appearance"] = ("Appearance", "ظاهر"),
        ["Size"] = ("Size", "اندازه"),
        ["Glass"] = ("Background", "پس‌زمینه"),
        ["General"] = ("General", "عمومی"),
        ["StartWithWindows"] = ("Start with Windows", "اجرا همراه ویندوز"),
        ["ShowOnLaunch"] = ("Show this window when Pulse starts", "نمایش این پنجره هنگام اجرای Pulse"),
        ["ShowSummary"] = ("Show a summary after each game", "نمایش خلاصه بعد از هر بازی"),
        ["AutoUpdate"] = ("Check for updates automatically", "بررسی خودکار به‌روزرسانی"),
        ["CheckNow"] = ("Check now", "بررسی کن"),
        ["VersionLabel"] = ("Version {0}", "نسخه‌ی {0}"),
        ["Checking"] = ("Checking for updates…", "در حال بررسی…"),
        ["UpToDate"] = ("Version {0} · up to date", "نسخه‌ی {0} · به‌روز است"),
        ["UpdateFound"] = ("Version {0} is available", "نسخه‌ی {0} آماده است"),
        ["CheckFailed"] = ("Couldn't reach GitHub. Try again later.", "اتصال به GitHub ممکن نشد. بعداً دوباره امتحان کن."),
        ["Supporters"] = ("Thanks to", "با سپاس از"),
        ["SupportersHint"] = ("Channels that shared Pulse with their community.", "کانال‌هایی که Pulse را به مخاطبانشان معرفی کردند."),
        ["Preview"] =("Preview", "پیش‌نمایش"),
        ["PreviewHint"] = ("Exactly how the overlay sits on your screen.", "دقیقاً همان‌طور که اورلی روی صفحه‌ی تو قرار می‌گیرد."),
        ["GameHud"] = ("Game HUD", "HUD بازی"),
        ["Zoom"] = ("Zoom in", "بزرگ‌نمایی"),
        ["OwnScreenshot"] = ("Your screenshot…", "اسکرین‌شات خودت…"),
        ["HotkeysTitle"] = ("Hotkeys", "کلیدهای میانبر"),
        ["HotkeyToggle"] = ("Show or hide the overlay", "نمایش یا پنهان کردن اورلی"),
        ["HotkeyCompact"] = ("Switch between compact and full", "جابه‌جایی بین حالت فشرده و کامل"),
        ["HotkeyMove"] = ("Move it to the next spot", "بردن اورلی به جای بعدی"),
        ["HotkeysProfileHint"] = ("In a game with its own profile, these change that game's look.",
                                  "در بازی‌ای که پروفایل خودش را دارد، این کلیدها ظاهر همان بازی را تغییر می‌دهند."),

        // game profiles
        ["ProfilesHint"] = ("Give any game an overlay of its own. Pulse switches to it while that game is in front.",
                            "برای هر بازی یک اورلی مخصوص بساز. وقتی آن بازی جلوی صفحه است، Pulse خودش سراغش می‌رود."),
        ["ProfilesYours"] = ("Your games", "بازی‌های تو"),
        ["ProfilesEmpty"] = ("No game profiles yet. Add a game below: it starts with your default look, so you only change what's different.",
                             "هنوز پروفایلی نساخته‌ای. از پایین یک بازی اضافه کن: با ظاهر پیش‌فرضت شروع می‌شود، پس فقط چیزهایی را که فرق دارد عوض کن."),
        ["ProfilesAdd"] = ("Add a game", "افزودن بازی"),
        ["ProfilesAddHint"] = ("Games you've played lately with Pulse running. Not here? Pick its .exe.",
                               "بازی‌هایی که اخیراً با Pulse اجرا کرده‌ای. اینجا نیست؟ فایل exe آن را انتخاب کن."),
        ["ProfilesBrowse"] = ("Choose a game's .exe…", "انتخاب فایل exe بازی…"),
        ["ProfilesNoRecent"] = ("Play a game with Pulse running and it shows up here.", "یک بازی را با Pulse اجرا کن تا اینجا نشان داده شود."),
        ["ProfileOpen"] = ("Edit this game's overlay", "ویرایش اورلی این بازی"),
        ["ProfileCreate"] = ("Make a profile for this game", "ساخت پروفایل برای این بازی"),
        ["ProfileBack"] = ("All games", "همه‌ی بازی‌ها"),
        ["ProfileAppliesTo"] = ("Used while {0} is in front", "وقتی {0} جلوی صفحه است استفاده می‌شود"),
        ["ProfileOff"] = ("Off: {0} gets the default look", "خاموش: {0} ظاهر پیش‌فرض را می‌گیرد"),
        ["ProfileOffShort"] = ("Off: uses the default look", "خاموش: ظاهر پیش‌فرض"),
        ["ProfileEnabled"] = ("Use this profile", "استفاده از این پروفایل"),
        ["ProfileReset"] = ("Copy default look", "کپی ظاهر پیش‌فرض"),
        ["ProfileResetHint"] = ("Start this game over from your default look", "این بازی را از نو با ظاهر پیش‌فرضت شروع کن"),
        ["ProfileDelete"] = ("Delete profile", "حذف پروفایل"),
        ["ProfileDeleteHint"] = ("The game goes back to the default look", "بازی به ظاهر پیش‌فرض برمی‌گردد"),
        ["ExeFilter"] = ("Games", "بازی‌ها"),
        ["ProfileIconPick"] = ("Choose the game's .exe to show its icon", "فایل exe بازی را انتخاب کن تا آیکنش نشان داده شود"),
        ["ProfileIconChange"] = ("Change the game's .exe", "تغییر فایل exe بازی"),

        ["Launch"] = ("Launch overlay", "اجرای اورلی"),
        ["Apply"] = ("Done", "تمام"),
        ["Quit"] = ("Quit Pulse", "خروج از Pulse"),
        ["ImageFilter"] = ("Images", "تصاویر"),

        // ── tray ──
        ["TrayToggle"] = ("Show / hide overlay", "نمایش / پنهان"),
        ["TrayCompact"] = ("Compact mode", "حالت فشرده"),
        ["TrayCorner"] = ("Position", "جای اورلی"),
        ["TraySettings"] = ("Customize…", "شخصی‌سازی…"),
        ["TrayExit"] = ("Exit", "خروج"),
        ["TrayLastSummary"] = ("Last game summary…", "خلاصه‌ی آخرین بازی…"),
        ["TrayNoSummary"] = ("No game recorded yet. Play a game and close it to get a summary.",
                             "هنوز بازی‌ای ثبت نشده. یک بازی را اجرا کن و ببندش تا خلاصه‌اش را ببینی."),
        ["TraySummaryReady"] = ("Your {0} summary is ready. Click to open.", "خلاصه‌ی {0} آماده است. برای دیدن کلیک کن."),
        ["StillRunning"] = ("Pulse is still running in the tray. Ctrl+Shift+O shows the overlay.",
                            "Pulse در سینی سیستم در حال اجراست. Ctrl+Shift+O اورلی را نشان می‌دهد."),
        ["TrayUpdate"] = ("Pulse {0} is available. Click to update.", "Pulse {0} آماده است. برای به‌روزرسانی کلیک کن."),

        // ── update window ──
        ["UpdateTitle"] = ("Pulse update", "به‌روزرسانی Pulse"),
        ["UpdateHeadline"] = ("A new version of Pulse is ready", "نسخه‌ی جدید Pulse آماده است"),
        ["UpdateWhatsNew"] = ("What's new", "تغییرات"),
        ["UpdateNote"] = ("Pulse will close, update and reopen by itself. Your settings are kept.",
                          "Pulse بسته می‌شود، به‌روز می‌شود و دوباره باز می‌شود. تنظیماتت حفظ می‌شود."),
        ["UpdateNow"] = ("Update now", "به‌روزرسانی"),
        ["UpdateLater"] = ("Later", "بعداً"),
        ["UpdateSkip"] = ("Skip this version", "رد کردن این نسخه"),
        ["UpdateDownloading"] = ("Downloading…", "در حال دانلود…"),
        ["UpdateInstalling"] = ("Starting the installer…", "در حال اجرای نصب‌کننده…"),
        ["UpdateFailed"] = ("The update couldn't be downloaded.", "دانلود به‌روزرسانی انجام نشد."),
        ["UpdateRetry"] = ("Try again", "تلاش دوباره"),

        // ── what's new (after an update) ──
        ["WhatsNewTitle"] = ("What's new in Pulse", "تغییرات Pulse"),
        ["WhatsNewHeadline"] = ("Pulse has been updated", "Pulse به‌روز شد"),
        ["WhatsNewOk"] = ("Got it", "متوجه شدم"),
        ["TrayUpdated"] = ("Pulse was updated to {0}. Click to see what's new.",
                           "Pulse به نسخه‌ی {0} به‌روز شد. برای دیدن تغییرات کلیک کن."),
        // ── game summary (after a game closes) ──
        ["SumTitle"] = ("Game summary", "خلاصه‌ی بازی"),
        ["SumToday"] = ("Today, {0}", "امروز، {0}"),
        ["SumYesterday"] = ("Yesterday, {0}", "دیروز، {0}"),
        ["SumHours"] = ("{0} h {1} min", "{0} ساعت و {1} دقیقه"),
        ["SumMinutes"] = ("{0} min", "{0} دقیقه"),
        ["SumSmooth"] = ("Smooth", "روان"),
        ["SumSomeHitches"] = ("A few hitches", "چند افت کوچک"),
        ["SumStuttery"] = ("Stuttery", "پر از افت"),
        ["SumFirst"] = ("First session of this game in Pulse", "اولین بار است که Pulse این بازی را ثبت می‌کند"),
        ["SumVsLast"] = ("Compared with your last session ({0})", "در مقایسه با دفعه‌ی قبل ({0})"),
        ["SumSame"] = ("same", "بدون تغییر"),
        ["SumAvg"] = ("Average FPS", "میانگین FPS"),
        ["SumStutters"] = ("Stutters", "افت ناگهانی"),
        ["SumPerMinute"] = ("{0} per min", "{0} در دقیقه"),
        ["SumTimeline"] = ("FPS over the session", "FPS در طول بازی"),
        ["SumTimelineHint"] = ("Hover to see any moment", "برای دیدن هر لحظه، ماوس را رویش ببر"),
        ["SumBound"] = ("What limited your FPS", "چه چیزی FPS را محدود کرد"),
        ["SumBoundGuide"] = ("What do these mean?", "این‌ها یعنی چه؟"),
        ["SumHardware"] = ("Hardware", "سخت‌افزار"),
        ["SumTemp"] = ("Temperature", "دما"),
        ["SumAvgPeak"] = ("avg {0} · peak {1}", "میانگین {0} · بیشترین {1}"),
        ["SumLoad"] = ("Average usage", "میانگین مصرف"),
        ["SumVram"] = ("VRAM peak", "بیشترین VRAM"),
        ["SumRam"] = ("Memory (RAM)", "حافظه (رم)"),
        ["SumRamUsed"] = ("Used in game", "مصرف در بازی"),
        ["SumRamUsedValue"] = ("avg {0} · peak {1} of {2} GB", "میانگین {0} · بیشترین {1} از {2} گیگ"),
        ["SumRamChannels"] = ("Channels", "کانال"),
        ["SumRamSingle"] = ("Single channel", "تک‌کاناله"),
        ["SumRamDual"] = ("Dual channel", "دوکاناله"),
        ["SumRamMulti"] = ("{0} channels", "{0} کاناله"),
        ["SumRamUpgrade"] = ("With better RAM (estimated extra FPS in this session)", "با رم بهتر (تخمین FPS بیشتر در همین بازی)"),
        ["SumRamBasis"] = ("Estimated from the {0} of this session that was CPU-bound: faster RAM only speeds up those frames. GPU-bound and capped time wouldn't gain anything.",
                           "بر اساس {0} از این بازی که CPU-bound بود: رم سریع‌تر فقط همین فریم‌ها را سریع‌تر می‌کند. زمان‌هایی که GPU-bound یا Capped بود هیچ تغییری نمی‌کرد."),
        ["SumRamFine"] = ("Your RAM is already fast and dual channel: an upgrade wouldn't add FPS worth paying for.",
                          "رمت همین حالا سریع و دوکاناله است: ارتقا FPS قابل‌توجهی اضافه نمی‌کند."),
        ["SumRamNoBound"] = ("An FPS estimate needs GPU timings from PresentMon, which this session didn't have.",
                             "برای تخمین FPS زمان‌بندی GPU از PresentMon لازم است که در این بازی نبود."),
        ["SumNoSensors"] = ("Temperatures are recorded while the overlay is on.", "دماها فقط وقتی اورلی روشن است ثبت می‌شوند."),
        ["SumTips"] = ("For next time", "برای دفعه‌ی بعد"),
        ["SumSave"] = ("Save", "ذخیره"),
        ["SumSaved"] = ("✓ Saved", "✓ ذخیره شد"),
        ["SumSaveHint"] = ("Keep this summary to look at later (Pulse → Summaries)", "این خلاصه را نگه دار تا بعداً ببینی (Pulse ← خلاصه‌ها)"),
        ["SumSavedHint"] = ("Saved. Click to unsave", "ذخیره شده. برای برداشتن از ذخیره‌ها کلیک کن"),
        ["SavedTitle"] = ("Saved summaries", "خلاصه‌های ذخیره‌شده"),
        ["SavedSubtitle"] = ("Click a game to open its summary", "روی هر بازی کلیک کن تا خلاصه‌اش باز شود"),
        ["SavedEmpty"] = ("Nothing saved yet. Press Save in a game summary to keep it here.",
                          "هنوز چیزی ذخیره نکرده‌ای. در خلاصه‌ی هر بازی روی «ذخیره» بزن تا اینجا بماند."),
        ["SavedOpen"] = ("Open summary", "باز کردن خلاصه"),
        ["SavedAvg"] = ("Avg FPS", "میانگین FPS"),
        ["SavedDelete"] = ("Delete", "حذف"),
        ["SavedDeleteSure"] = ("Delete for good?", "قطعاً حذف شود؟"),
        ["SavedDeleteHint"] = ("Removes this session from your records", "این بازی را از رکوردهایت پاک می‌کند"),
        ["LastSummary"] = ("Last game summary", "خلاصه‌ی آخرین بازی"),
        ["TraySaved"] = ("Saved summaries…", "خلاصه‌های ذخیره‌شده…"),
        ["SumShowAfter"] = ("Show after every game", "بعد از هر بازی نشان بده"),

        ["HotkeysTaken"] = ("These hotkeys are taken by another app: ", "این کلیدها را برنامه‌ی دیگری گرفته: "),
    };
}
