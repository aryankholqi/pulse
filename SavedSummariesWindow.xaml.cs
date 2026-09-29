using System;
using System.Windows;
using System.Windows.Interop;
using Pulse.Services;

namespace Pulse;

/// <summary>The game summaries the player saved (from the tray): click one to open it, or delete it from the records.</summary>
public partial class SavedSummariesWindow : Window
{
    internal SavedSummariesWindow(Action<GameSession> open)
    {
        InitializeComponent();
        List.Open += open;
        OkButton.Click += (_, _) => Close();

        SourceInitialized += (_, _) =>
        {
            Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
            // grows with the list up to most of the screen, then the list scrolls
            MaxHeight = SystemParameters.WorkArea.Height * 0.85;
        };
    }
}
