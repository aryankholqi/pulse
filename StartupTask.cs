using System;
using System.Diagnostics;
using System.IO;
using System.Security;
using System.Security.Principal;
using System.Text;

namespace Pulse;

/// <summary>
/// "Start with Windows" through Task Scheduler instead of the Run key:
/// Pulse needs admin, and a scheduled task with RunLevel=Highest starts
/// elevated at sign-in without a UAC prompt every time.
/// </summary>
internal static class StartupTask
{
    const string TaskName = "Pulse Overlay";

    public static bool IsEnabled() => Schtasks($"/query /tn \"{TaskName}\"") == 0;

    public static bool Disable() => Schtasks($"/delete /tn \"{TaskName}\" /f") == 0;

    public static bool Enable()
    {
        string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "Pulse.exe");
        string dir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        string user = WindowsIdentity.GetCurrent().Name;

        string xml = $"""
            <?xml version="1.0" encoding="UTF-16"?>
            <Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
              <RegistrationInfo>
                <Description>Starts the Pulse game overlay when you sign in.</Description>
              </RegistrationInfo>
              <Triggers>
                <LogonTrigger>
                  <Enabled>true</Enabled>
                  <UserId>{Esc(user)}</UserId>
                  <Delay>PT10S</Delay>
                </LogonTrigger>
              </Triggers>
              <Principals>
                <Principal id="Author">
                  <UserId>{Esc(user)}</UserId>
                  <LogonType>InteractiveToken</LogonType>
                  <RunLevel>HighestAvailable</RunLevel>
                </Principal>
              </Principals>
              <Settings>
                <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
                <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
                <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
                <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>
                <Priority>7</Priority>
              </Settings>
              <Actions Context="Author">
                <Exec>
                  <Command>{Esc(exe)}</Command>
                  <Arguments>--tray</Arguments>
                  <WorkingDirectory>{Esc(dir)}</WorkingDirectory>
                </Exec>
              </Actions>
            </Task>
            """;

        string tmp = Path.Combine(Path.GetTempPath(), "pulse-startup-task.xml");
        try
        {
            File.WriteAllText(tmp, xml, Encoding.Unicode); // schtasks wants UTF-16
            return Schtasks($"/create /tn \"{TaskName}\" /xml \"{tmp}\" /f") == 0;
        }
        catch { return false; }
        finally { try { File.Delete(tmp); } catch { } }
    }

    static string Esc(string s) => SecurityElement.Escape(s) ?? s;

    static int Schtasks(string args)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
            });
            if (p is null) return -1;
            p.WaitForExit(10_000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch { return -1; }
    }
}
