using System.Net.Http;

namespace Zapret.Core;

public enum AutoPickPhase { Started, Testing, Passed, Failed, Done }

public readonly record struct AutoPickProgress(
    AutoPickPhase Phase, string Label, int Index, int Total, Strategy? Result);

public sealed class AutoPicker
{
    private readonly WinwsRunner _runner;
    private readonly string[] _targets;
    private readonly int _settleMs;
    private readonly int _timeoutMs;

    public AutoPicker(WinwsRunner runner, string[]? targets = null, int settleMs = 2500, int timeoutMs = 5000)
    {
        _runner = runner;
        _targets = targets ?? new[]
        {
            "https://www.youtube.com",
            "https://discord.com",
            "https://www.instagram.com",
        };
        _settleMs = settleMs;
        _timeoutMs = timeoutMs;
    }

    public async Task<Strategy?> RunAsync(IReadOnlyList<Strategy> candidates,
        IProgress<AutoPickProgress>? progress = null, CancellationToken ct = default)
    {
        var total = candidates.Count;
        progress?.Report(new AutoPickProgress(AutoPickPhase.Started, "", 0, total, null));

        using var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromMilliseconds(_timeoutMs),
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(_timeoutMs) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0");

        var i = 0;
        foreach (var s in candidates)
        {
            ct.ThrowIfCancellationRequested();
            i++;
            progress?.Report(new AutoPickProgress(AutoPickPhase.Testing, s.Label, i, total, null));

            try
            {
                await _runner.StartAsync(s, ct).ConfigureAwait(false);
            }
            catch
            {
                progress?.Report(new AutoPickProgress(AutoPickPhase.Failed, s.Label, i, total, null));
                continue;
            }

            await Task.Delay(_settleMs, ct).ConfigureAwait(false);

            // If the engine died on start-up, this strategy can't be the working one.
            if (!_runner.IsRunning)
            {
                progress?.Report(new AutoPickProgress(AutoPickPhase.Failed, s.Label, i, total, null));
                continue;
            }

            var ok = await ProbeAsync(http, ct).ConfigureAwait(false);
            progress?.Report(new AutoPickProgress(
                ok ? AutoPickPhase.Passed : AutoPickPhase.Failed, s.Label, i, total, ok ? s : null));

            if (ok)
            {
                progress?.Report(new AutoPickProgress(AutoPickPhase.Done, s.Label, i, total, s));
                return s;
            }
        }

        progress?.Report(new AutoPickProgress(AutoPickPhase.Done, "", total, total, null));
        return null;
    }

    private async Task<bool> ProbeAsync(HttpClient http, CancellationToken ct)
    {
        foreach (var target in _targets)
        {
            using var probeCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            probeCts.CancelAfter(_timeoutMs);
            try
            {
                using var resp = await http.GetAsync(target, HttpCompletionOption.ResponseHeadersRead, probeCts.Token)
                    .ConfigureAwait(false);
                // A completed handshake usually means the bypass works — BUT a DPI box often finishes the
                // handshake and then injects a 403/451 block page. Treat those as "still blocked" (matching
                // RestrictionTester) and try the next target instead of declaring victory.
                if ((int)resp.StatusCode is 403 or 451)
                    continue;
                return true;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch
            {
            }
        }
        return false;
    }
}
