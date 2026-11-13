using System.Diagnostics;
using System.IO;
using System.Security;
using Microsoft.Win32;

namespace StatsMonitor.Services;

/// <summary>
/// The app requires administrator rights, and Windows silently skips elevated apps listed in the registry Run key,
/// so autostart uses a Task Scheduler logon task that runs with highest privileges.
/// </summary>
public static class StartupService
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "StatsMonitor";
    private const string TaskName = "StatsMonitor";

    private static bool? _last;

    public static void SetEnabled(bool enabled)
    {
        if (_last == enabled) return;
        _last = enabled;
        RemoveLegacyRunEntry();
        try
        {
            if (enabled) CreateTask(); else Schtasks($"/Delete /TN \"{TaskName}\" /F");
        }
        catch
        {
            // Scheduler unavailable or access denied.
        }
    }

    private static void RemoveLegacyRunEntry()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            key?.DeleteValue(ValueName, throwOnMissingValue: false);
        }
        catch { }
    }

    private static void CreateTask()
    {
        var exe = Environment.ProcessPath;
        if (exe == null) return;
        string user = SecurityElement.Escape($"{Environment.UserDomainName}\\{Environment.UserName}");
        string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-16\"?>" +
            "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">" +
            $"<Triggers><LogonTrigger><Enabled>true</Enabled><UserId>{user}</UserId></LogonTrigger></Triggers>" +
            $"<Principals><Principal id=\"Author\"><UserId>{user}</UserId><LogonType>InteractiveToken</LogonType><RunLevel>HighestAvailable</RunLevel></Principal></Principals>" +
            "<Settings><MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>" +
            "<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><ExecutionTimeLimit>PT0S</ExecutionTimeLimit><StartWhenAvailable>true</StartWhenAvailable></Settings>" +
            $"<Actions Context=\"Author\"><Exec><Command>{SecurityElement.Escape(exe)}</Command><WorkingDirectory>{SecurityElement.Escape(Path.GetDirectoryName(exe) ?? "")}</WorkingDirectory></Exec></Actions>" +
            "</Task>";
        string file = Path.Combine(Path.GetTempPath(), "StatsMonitor_task.xml");
        File.WriteAllText(file, xml, System.Text.Encoding.Unicode);
        try { Schtasks($"/Create /TN \"{TaskName}\" /XML \"{file}\" /F"); }
        finally { try { File.Delete(file); } catch { } }
    }

    private static void Schtasks(string args)
    {
        using var p = Process.Start(new ProcessStartInfo("schtasks.exe", args) { CreateNoWindow = true, UseShellExecute = false });
        p?.WaitForExit(15000);
    }
}
