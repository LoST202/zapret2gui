using System.Diagnostics;

namespace Zapret.Core;

public static class Autostart
{
    private const string TaskName = "Zapret2-Autostart";

    public static bool IsEnabled() => Run("/Query", "/TN", TaskName) == 0;

    public static bool Enable(string exePath) =>
        // Quote the path inside the /TR value: schtasks stores the action string verbatim and
        // splits it on spaces at logon, so an unquoted "C:\Program Files\..." path never launches.
        Run("/Create", "/TN", TaskName, "/TR", "\"" + exePath + "\"", "/SC", "ONLOGON", "/RL", "HIGHEST", "/F") == 0;

    public static bool Disable() => Run("/Delete", "/TN", TaskName, "/F") == 0;

    private static int Run(params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo("schtasks.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            foreach (var a in args)
                psi.ArgumentList.Add(a);

            using var p = Process.Start(psi);
            if (p is null)
                return -1;
            p.WaitForExit(8000);
            return p.HasExited ? p.ExitCode : -1;
        }
        catch
        {
            return -1;
        }
    }
}
