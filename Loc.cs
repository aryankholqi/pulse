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
        ["Subtitle"] = ("Make the overlay yours, then launch it into your game.", "اورلی را به سلیقه‌ی خودت بساز، بعد اجرایش کن."),
        ["Language"] = ("Language", "زبان"),
        ["Layout"] = ("Layout", "چیدمان"),
        ["Compact"] = ("Compact", "فشرده"),
        ["CompactHint"] = ("One slim line", "یک خط باریک"),
        ["Full"] = ("Full", "کامل"),
        ["FullHint"] = ("Graph and details", "نمودار و جزئیات"),
        ["Position"] = ("Position on screen", "جای اورلی در صفحه"),
        ["TopLeft"] = ("Top left", "بالا چپ"),
        ["TopCenter"] = ("Top center", "بالا وسط"),
        ["TopRight"] = ("Top right", "بالا راست"),
        ["BottomLeft"] = ("Bottom left", "پایین چپ"),
        ["BottomCenter"] = ("Bottom center", "پایین وسط"),
        ["BottomRight"] = ("Bottom right", "پایین راست"),
        ["Colors"] = ("Colors", "رنگ‌ها"),
        ["Custom"] = ("Custom…", "دلخواه…"),
        ["PickColor"] = ("Pick any color", "انتخاب رنگ دلخواه"),
        ["FpsWarnings"] = ("Turn FPS amber below 60 and red below 30", "FPS زیر ۶۰ نارنجی و زیر ۳۰ قرمز شود"),
        ["ResetColors"] = ("Reset colors", "رنگ‌های پیش‌فرض"),
        ["Appearance"] = ("Appearance", "ظاهر"),
        ["Size"] = ("Size", "اندازه"),
        ["Glass"] = ("Background", "پس‌زمینه"),
        ["General"] = ("General", "عمومی"),
        ["StartWithWindows"] = ("Start with Windows", "اجرا همراه ویندوز"),
        ["ShowOnLaunch"] = ("Show this window when Pulse starts", "نمایش این پنجره هنگام اجرای Pulse"),
        ["Preview"] = ("Preview", "پیش‌نمایش"),
        ["PreviewHint"] = ("Exactly how the overlay sits on your screen.", "دقیقاً همان‌طور که اورلی روی صفحه‌ی تو قرار می‌گیرد."),
        ["GameHud"] = ("Game HUD", "HUD بازی"),
        ["Zoom"] = ("Zoom in", "بزرگ‌نمایی"),
        ["OwnScreenshot"] = ("Your screenshot…", "اسکرین‌شات خودت…"),
        ["Hotkeys"] = ("Ctrl+Shift+O  show / hide     Ctrl+Shift+L  compact     Ctrl+Shift+P  move",
                       "Ctrl+Shift+O  نمایش/پنهان     Ctrl+Shift+L  فشرده     Ctrl+Shift+P  جابه‌جایی"),
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
        ["StillRunning"] = ("Pulse is still running in the tray. Ctrl+Shift+O shows the overlay.",
                            "Pulse در سینی سیستم در حال اجراست. Ctrl+Shift+O اورلی را نشان می‌دهد."),
        ["HotkeysTaken"] = ("These hotkeys are taken by another app: ", "این کلیدها را برنامه‌ی دیگری گرفته: "),
    };
}
