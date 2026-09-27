using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Pulse.Services;

namespace Pulse;

/// <summary>What GPU-bound, CPU-bound and Capped mean, and what to do about each. Opened from the settings window.</summary>
public partial class BottleneckGuideWindow : Window
{
    readonly Brush _gpuHue, _cpuHue;

    internal BottleneckGuideWindow(AppSettings settings)
    {
        // the tags in the colors the overlay draws them in
        _gpuHue = ColorUtil.Solid(ColorUtil.Parse(settings.GpuColor, AppSettings.DefaultGpuColor));
        _cpuHue = ColorUtil.Solid(ColorUtil.Parse(settings.CpuColor, AppSettings.DefaultCpuColor));

        InitializeComponent();
        Build();

        OkButton.Click += (_, _) => Close();

        // the guide is plain text, not bindings: rebuild it when the language switches
        PropertyChangedEventHandler relabel = (_, _) => Build();
        Loc.Instance.PropertyChanged += relabel;
        Closed += (_, _) => Loc.Instance.PropertyChanged -= relabel;

        SourceInitialized += (_, _) => Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    void Build()
    {
        bool persian = Loc.Instance.Language == AppLanguage.Persian;
        var soft = (Brush)FindResource("Soft");
        var muted = (Brush)FindResource("Muted");

        Modes.Children.Clear();
        foreach (var mode in BottleneckGuide.Modes)
        {
            var hue = mode.Kind switch { Bottleneck.Gpu => _gpuHue, Bottleneck.Cpu => _cpuHue, _ => soft };
            var card = new StackPanel();

            card.Children.Add(new TextBlock
            {
                Text = mode.Tag,
                Foreground = hue,
                FontSize = 14,
                FontWeight = FontWeights.SemiBold,
                FlowDirection = FlowDirection.LeftToRight,
                HorizontalAlignment = HorizontalAlignment.Left, // mirrored in Persian by the parent
            });
            card.Children.Add(new TextBlock
            {
                Text = persian ? mode.Meaning.Fa : mode.Meaning.En,
                Foreground = soft,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
                Margin = new Thickness(0, 4, 0, 0),
            });

            var todo = new TextBlock { Foreground = muted, FontSize = 11.5, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 0) };
            todo.SetBinding(TextBlock.TextProperty, new Binding("[BottleneckGuideDo]") { Source = Loc.Instance });
            card.Children.Add(todo);

            foreach (var (en, fa) in mode.Tips)
            {
                var dot = new Ellipse { Width = 5, Height = 5, Fill = hue, Margin = new Thickness(0, 8, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(dot, Dock.Left);
                var row = new DockPanel { Margin = new Thickness(0, 5, 0, 0) };
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = persian ? fa : en, Foreground = soft, FontSize = 12.5, TextWrapping = TextWrapping.Wrap, LineHeight = 19 });
                card.Children.Add(row);
            }

            Modes.Children.Add(new Border
            {
                Background = (Brush)FindResource("Surface"),
                BorderBrush = (Brush)FindResource("Line"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(16, 12, 16, 14),
                Margin = new Thickness(0, Modes.Children.Count == 0 ? 0 : 10, 0, 0),
                Child = card,
            });
        }

        Footnote.Text = persian ? BottleneckGuide.Footnote.Fa : BottleneckGuide.Footnote.En;
    }
}
