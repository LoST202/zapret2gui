using System.Diagnostics;
using CliWrap;

namespace Zapret.Core;

public sealed class WinwsRunner : IDisposable
{
    private readonly AppPaths _paths;
    private readonly object _gate = new();
    private Process? _process;
    private TimeSpan _lastCpuTotal;
    private long _lastCpuTick;
    private DateTime? _startedAt;
    private Task _driverRemoval = Task.CompletedTask;
    private volatile bool _disposed;

    public WinwsRunner(AppPaths paths) => _paths = paths;

    public bool IsRunning
    {
        get { lock (_gate) { return _process is { HasExited: false }; } }
    }

    public Strategy? Current { get; private set; }

    // Nullable<DateTime> is not read/written atomically; guard it against a torn cross-thread read
    // (OnExited writes on the threadpool while the uptime timer reads on the UI thread).
    public DateTime? StartedAt
    {
        get { lock (_gate) { return _startedAt; } }
        private set { lock (_gate) { _startedAt = value; } }
    }

    public event EventHandler? StateChanged;

    public event EventHandler<string>? Output;

    /// <summary>Raised when the engine exits on its own (crash), not via <see cref="StopAsync"/>. Carries the strategy that was running.</summary>
    public event EventHandler<Strategy>? Crashed;

    public async Task StartAsync(Strategy strategy, CancellationToken ct = default)
    {
        if (_disposed)
            return;   // app is shutting down — a late crash-restart/test tick must not relaunch the engine

        if (IsRunning)
            await StopAsync(ct).ConfigureAwait(false);

        if (!File.Exists(_paths.WinwsExe))
            throw new FileNotFoundException(
                "winws2.exe not found. Place Zapret.exe in the zapret root (next to bin/), " +
                "or set the ZAPRET_ROOT environment variable.", _paths.WinwsExe);

        KillStrayEngines();
        await EnableTcpTimestampsAsync().ConfigureAwait(false);

        // Wait out any WinDivert teardown still running from a previous stop/crash before we
        // launch the new engine (which loads the WinDivert driver). Otherwise a slow
        // `sc delete WinDivert` from the old teardown could delete the driver this engine just
        // loaded and kill it — spawning a crash-restart loop. Read under the gate: the field is
        // reassigned from both the UI thread (StopAsync) and the threadpool (OnExited).
        Task pendingRemoval;
        lock (_gate) { pendingRemoval = _driverRemoval; }
        await pendingRemoval.ConfigureAwait(false);

        var args = CommandParser.ForStrategy(_paths, strategy);

        if (args.Any(a => a.Contains("list-auto", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                var autoFile = _paths.List("list-auto.txt");
                if (!File.Exists(autoFile))
                {
                    Directory.CreateDirectory(_paths.ListsDir);
                    File.WriteAllText(autoFile, "");
                }
            }
            catch { }
        }

        var psi = new ProcessStartInfo(_paths.WinwsExe)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = _paths.BinDir,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.OutputDataReceived += OnOutput;
        process.ErrorDataReceived += OnOutput;
        process.Exited += OnExited;

        lock (_gate)
        {
            _process = process;
            Current = strategy;
        }

        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }
        catch
        {
            lock (_gate) { _process = null; Current = null; }
            process.Dispose();
            throw;
        }

        StartedAt = DateTime.Now;
        lock (_gate) { _lastCpuTick = 0; _lastCpuTotal = TimeSpan.Zero; }
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Working-set bytes and CPU% of the engine process since the previous call. (0, 0) when not running.</summary>
    public (long RamBytes, double CpuPercent) SampleUsage()
    {
        lock (_gate)
        {
            var p = _process;
            if (p is null)
                return (0, 0);
            try
            {
                if (p.HasExited)
                    return (0, 0);
                p.Refresh();
                var ram = p.WorkingSet64;
                var tick = Environment.TickCount64;
                var cpu = p.TotalProcessorTime;
                double pct = 0;
                if (_lastCpuTick != 0)
                {
                    var dt = tick - _lastCpuTick;
                    if (dt > 0)
                    {
                        pct = (cpu - _lastCpuTotal).TotalMilliseconds / (dt * Environment.ProcessorCount) * 100.0;
                        pct = Math.Clamp(pct, 0, 100);
                    }
                }
                _lastCpuTick = tick;
                _lastCpuTotal = cpu;
                return (ram, pct);
            }
            catch { return (0, 0); }
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        Process? process;
        lock (_gate) { process = _process; _process = null; }

        if (process is not null)
        {
            try
            {
                process.Exited -= OnExited;
                if (!process.HasExited)
                {
                    process.Kill(entireProcessTree: true);
                    await process.WaitForExitAsync(ct).ConfigureAwait(false);
                }
            }
            catch { }
            finally { process.Dispose(); }
        }

        Current = null;
        StartedAt = null;
        await RemoveDriverChained().ConfigureAwait(false);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private void OnOutput(object? sender, DataReceivedEventArgs e)
    {
        if (e.Data is not null)
            Output?.Invoke(this, e.Data);
    }

    private void OnExited(object? sender, EventArgs e)
    {
        Strategy? crashed;
        lock (_gate)
        {
            // A stale Exited callback can arrive after the process was superseded: StartAsync only
            // detaches OnExited (via StopAsync) when IsRunning was true, so a spontaneously-crashed
            // process is never detached. Ignore anything that isn't the process we currently track,
            // otherwise we'd null the NEW Current and raise Crashed for the wrong strategy.
            if (sender is not Process p || !ReferenceEquals(p, _process))
                return;
            p.Dispose();
            _process = null;
            crashed = Current;
            Current = null;
        }
        StartedAt = null;
        StateChanged?.Invoke(this, EventArgs.Empty);
        // Chain (never overwrite) the teardown so the crash-recovery StartAsync awaits it and won't
        // delete the WinDivert driver the restarted engine just loaded.
        RemoveDriverChained();
        if (crashed is not null)
            Crashed?.Invoke(this, crashed);
    }

    private static void KillStrayEngines()
    {
        foreach (var pr in Process.GetProcessesByName("winws2"))
        {
            try { pr.Kill(entireProcessTree: true); pr.WaitForExit(2000); }
            catch { }
            finally { pr.Dispose(); }
        }
    }

    // TCP timestamps (RFC 1323) are required by ts-based desync strategies.
    private static Task EnableTcpTimestampsAsync() =>
        RunQuietAsync("netsh", "interface", "tcp", "set", "global", "timestamps=enabled");

    private static async Task RemoveDriverAsync()
    {
        await RunQuietAsync("sc", "stop", "WinDivert").ConfigureAwait(false);
        await RunQuietAsync("sc", "delete", "WinDivert").ConfigureAwait(false);
    }

    // Serialize WinDivert teardowns: each removal chains after the previous one and replaces the
    // tracked task under the gate, so a crash-teardown racing a Stop-teardown never leaves one
    // untracked (which the next StartAsync could then fail to await).
    private Task RemoveDriverChained()
    {
        lock (_gate)
        {
            _driverRemoval = RemoveAfter(_driverRemoval);
            return _driverRemoval;
        }
    }

    private static async Task RemoveAfter(Task previous)
    {
        try { await previous.ConfigureAwait(false); }
        catch { }
        await RemoveDriverAsync().ConfigureAwait(false);
    }

    // Fire a short-lived hidden CLI command, discard its output, never throw. Bounded so a
    // stuck sc/netsh can't hang engine start/stop.
    private static async Task RunQuietAsync(string exe, params string[] args)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            await Cli.Wrap(exe)
                .WithArguments(args)
                .WithValidation(CommandResultValidation.None)
                .ExecuteAsync(cts.Token)
                .ConfigureAwait(false);
        }
        catch { }
    }

    public void Dispose()
    {
        _disposed = true;   // block any late StartAsync (pending crash-restart / test tick) from relaunching after teardown
        // Bounded: never hang the UI thread on exit if the WinDivert teardown stalls.
        try { StopAsync().Wait(TimeSpan.FromSeconds(5)); }
        catch { }
    }
}
