using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shapes;
using Pulse.Services;

namespace Pulse;

/// <summary>Shown once after an update: what changed since the version the user last saw.</summary>
public partial class WhatsNewWindow : Window
{
    readonly IReadOnlyList<WhatsNew.Release> _releases;

    internal WhatsNewWindow(IReadOnlyList<WhatsNew.Release> releases)
    {
        _releases = releases;
        InitializeComponent();

        VersionText.Text = "v" + UpdateService.Current;
        BuildChanges();

        OkButton.Click += (_, _) => Close();

        // the notes are plain text, not bindings: rebuild them when the language switches
        PropertyChangedEventHandler relabel = (_, _) => BuildChanges();
        Loc.Instance.PropertyChanged += relabel;
        Closed += (_, _) => Loc.Instance.PropertyChanged -= relabel;

        SourceInitialized += (_, _) => Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
    }

    void BuildChanges()
    {
        bool persian = Loc.Instance.Language == AppLanguage.Persian;
        var accent = (Brush)FindResource("Accent");
        var soft = (Brush)FindResource("Soft");
        var muted = (Brush)FindResource("Muted");

        Changes.Children.Clear();
        foreach (var release in _releases)
        {
            // skipped a few versions: label each one (the header already names the newest)
            if (_releases.Count > 1)
                Changes.Children.Add(new TextBlock
                {
                    Text = "v" + release.Version,
                    Foreground = muted,
                    FontSize = 11.5,
                    Margin = new Thickness(0, Changes.Children.Count == 0 ? 4 : 12, 0, 0),
                    FlowDirection = FlowDirection.LeftToRight,
                    HorizontalAlignment = HorizontalAlignment.Left,
                });

            foreach (var (en, fa) in release.Changes)
            {
                var dot = new Ellipse { Width = 5, Height = 5, Fill = accent, Margin = new Thickness(0, 8, 10, 0), VerticalAlignment = VerticalAlignment.Top };
                DockPanel.SetDock(dot, Dock.Left);

                var row = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
                row.Children.Add(dot);
                row.Children.Add(new TextBlock { Text = persian ? fa : en, TextWrapping = TextWrapping.Wrap, Foreground = soft, LineHeight = 20 });
                Changes.Children.Add(row);
            }
        }
    }
}
