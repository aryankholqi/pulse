using System;
using System.ComponentModel;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Pulse.Services;

namespace Pulse.Controls;

/// <summary>
/// The game summaries the player saved, newest first: click one to open it, or delete it from the
/// records (a second click makes sure). Keeps itself current while it's on screen.
/// Used by the settings window and the tray's saved summaries window.
/// </summary>
public sealed class SavedSummaryList : StackPanel
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    GameSession? _confirming; // the one whose delete was clicked once: a second click deletes
    bool _listening;

    /// <summary>A saved summary was clicked.</summary>
    public event Action<GameSession>? Open;

    public SavedSummaryList()
    {
        // plain text, not bindings: rebuild when the language switches or a session is saved / deleted
        PropertyChangedEventHandler relabel = (_, _) => Build();
        Action changed = Build;
        Loaded += (_, _) =>
        {
            if (!_listening)
            {
                Loc.Instance.PropertyChanged += relabel;
                SessionHistory.Changed += changed;
                _listening = true;
            }
            Build();
        };
        Unloaded += (_, _) =>
        {
            Loc.Instance.PropertyChanged -= relabel;
            SessionHistory.Changed -= changed;
            _listening = false;
        };
    }

    void Build()
    {
        Children.Clear();
        var saved = SessionHistory.SavedSessions;
        foreach (var s in saved) Children.Add(Row(s));
        if (saved.Count == 0)
            Children.Add(new TextBlock
            {
                Text = Loc.T("SavedEmpty"),
                Foreground = (Brush)FindResource("Muted"),
                FontSize = 12.5,
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 20,
            });
    }

    /// <summary>The game, when and how long, its average FPS and verdict; delete beside it.</summary>
    FrameworkElement Row(GameSession s)
    {
        var verdict = SessionInsights.Judge(s) switch
        {
            Smoothness.Smooth => Palette.Cool,
            Smoothness.SomeHitches => Palette.Warm,
            _ => Palette.Hot,
        };

        var fps = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, FlowDirection = FlowDirection.LeftToRight };
        fps.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = verdict, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) });
        fps.Children.Add(new TextBlock
        {
            Text = Math.Round(s.AvgFps).ToString("0", Inv),
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Bahnschrift, Segoe UI"),
            VerticalAlignment = VerticalAlignment.Center,
        });
        fps.Children.Add(new TextBlock { Text = " FPS", Foreground = (Brush)FindResource("Muted"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(3, 3, 0, 0) });
        DockPanel.SetDock(fps, Dock.Right);

        var text = new StackPanel { Margin = new Thickness(0, 0, 12, 0) };
        text.Children.Add(new TextBlock { Text = s.Game, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        text.Children.Add(new TextBlock
        {
            Text = $"{SessionSummaryWindow.When(s.Ended)}  ·  {SessionSummaryWindow.Duration(s.PlaySeconds)}",
            Foreground = (Brush)FindResource("Muted"),
            FontSize = 11.5,
            Margin = new Thickness(0, 2, 0, 0),
        });

        var content = new DockPanel();
        content.Children.Add(fps);
        content.Children.Add(text);

        var card = new Button { Style = (Style)FindResource("Card"), Content = content, Foreground = (Brush)FindResource("Text"), ToolTip = Loc.T("SavedOpen") };
        card.Click += (_, _) => Open?.Invoke(s);

        bool confirming = _confirming is { } c && c.Started == s.Started && c.Process == s.Process;
        var delete = new Button
        {
            Style = (Style)FindResource("Link"),
            Content = Loc.T(confirming ? "SavedDeleteSure" : "SavedDelete"),
            Foreground = confirming ? Palette.Hot : (Brush)FindResource("Muted"),
            FontSize = 12,
            Margin = new Thickness(12, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            ToolTip = Loc.T("SavedDeleteHint"),
        };
        delete.Click += (_, _) =>
        {
            if (confirming)
            {
                _confirming = null;
                SessionHistory.Delete(s); // raises Changed → Build
            }
            else
            {
                _confirming = s;
                Build();
            }
        };
        DockPanel.SetDock(delete, Dock.Right);

        var row = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        row.Children.Add(delete);
        row.Children.Add(card);
        return row;
    }
}
