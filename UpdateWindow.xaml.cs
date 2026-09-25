using System;
using System.Globalization;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Pulse.Services;

namespace Pulse;

public enum UpdateChoice { Later, Skip, Installing }

/// <summary>
/// Asks whether to install a newer Pulse. On "Update now" it downloads the installer here,
/// with progress, and reports <see cref="UpdateChoice.Installing"/> once the installer has started.
/// </summary>
public partial class UpdateWindow : Window
{
    readonly UpdateInfo _update;
    CancellationTokenSource? _download;

    public UpdateChoice Choice { get; private set; } = UpdateChoice.Later;

    public UpdateWindow(UpdateInfo update)
    {
        _update = update;
        InitializeComponent();

        FromVersion.Text = "v" + UpdateService.Current;
        ToVersion.Text = "v" + update.Version;

        if (update.Notes.Length > 0) Notes.Text = update.Notes;
        else NotesPanel.Visibility = Visibility.Collapsed;

        UpdateButton.Click += async (_, _) => await DownloadAndInstall();
        LaterButton.Click += (_, _) => Close();
        SkipButton.Click += (_, _) => { Choice = UpdateChoice.Skip; Close(); };

        SourceInitialized += (_, _) => Native.UseDarkTitleBar(new WindowInteropHelper(this).Handle);
        Closing += (_, _) => _download?.Cancel(); // closing mid-download = "later"
    }

    async System.Threading.Tasks.Task DownloadAndInstall()
    {
        UpdateButton.IsEnabled = SkipButton.IsEnabled = false;
        Note.Visibility = ErrorText.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        ProgressText.Text = Loc.T("UpdateDownloading");
        SetProgress(0);

        _download = new CancellationTokenSource();
        try
        {
            var progress = new Progress<double>(SetProgress);
            string installer = await UpdateService.DownloadAsync(_update, progress, _download.Token);

            ProgressText.Text = Loc.T("UpdateInstalling");
            UpdateService.Install(installer);
            Choice = UpdateChoice.Installing;
            Close();
        }
        catch (OperationCanceledException) { /* window closed */ }
        catch (Exception ex)
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            ErrorText.Text = Loc.T("UpdateFailed") + " " + ex.Message;
            ErrorText.Visibility = Visibility.Visible;
            UpdateButton.Content = Loc.T("UpdateRetry");
            UpdateButton.IsEnabled = SkipButton.IsEnabled = true;
        }
    }

    void SetProgress(double fraction)
    {
        fraction = Math.Clamp(fraction, 0, 1);
        ProgressFill.Width = ProgressTrack.ActualWidth * fraction;
        ProgressPercent.Text = (fraction * 100).ToString("0", CultureInfo.InvariantCulture) + "%";
    }
}
