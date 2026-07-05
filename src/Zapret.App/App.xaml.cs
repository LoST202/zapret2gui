using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;

namespace Zapret.App;

public partial class App : Application
{
    private static Mutex? _instanceMutex;
    private static bool _ownsMutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        // A fault in one UI handler should not kill a resident tray app; log it and keep running.
        DispatcherUnhandledException += (_, args) =>
        {
            LogCrash(args.Exception);
            args.Handled = true;
        };

        _instanceMutex = new Mutex(initiallyOwned: true, @"Local\Zapret_SingleInstance", out var createdNew);
        _ownsMutex = createdNew;   // a second instance does NOT own the named mutex; it must never release it
        if (!createdNew && Environment.GetEnvironmentVariable("ZAPRET_ALLOW_MULTI") != "1")
        {
            _instanceMutex.Dispose();
            _instanceMutex = null;
            Shutdown();
            return;
        }

        ShutdownMode = ShutdownMode.OnExplicitShutdown;

        base.OnStartup(e);

        new MainWindow().Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_ownsMutex)
        {
            try { _instanceMutex?.ReleaseMutex(); }
            catch { }
        }
        _instanceMutex?.Dispose();
        base.OnExit(e);
    }

    private static void LogCrash(Exception ex)
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "zapret-error.log");
            File.AppendAllText(path, $"[{DateTime.Now:u}] {ex}\n\n");
        }
        catch { }
    }
}

internal static class MemoryTrimmer
{
    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetProcessWorkingSetSize(nint hProcess, nint dwMinimumWorkingSetSize, nint dwMaximumWorkingSetSize);

    public static void Trim()
    {
        try
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Aggressive, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();

            using var current = Process.GetCurrentProcess();
            SetProcessWorkingSetSize(current.Handle, -1, -1);
        }
        catch
        {
        }
    }
}
