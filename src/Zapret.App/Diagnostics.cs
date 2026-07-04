using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.ServiceProcess;
using System.Text.Json;
using Zapret.Core;

namespace Zapret.App;

public enum DiagLevel { Ok, Warn, Fail }

public sealed record DiagItem(string Title, DiagLevel Level, string Detail = "");

public static class Diagnostics
{
    public static List<DiagItem> Run(AppPaths paths)
    {
        var items = new List<DiagItem>();
        var services = SafeGetServices();

        items.Add(IsRunning(services, "BFE")
            ? new DiagItem("Base Filtering Engine", DiagLevel.Ok, "служба работает")
            : new DiagItem("Base Filtering Engine", DiagLevel.Fail, "не запущена — обход работать не будет"));

        var (proxyOn, proxyServer) = ProxyState();
        items.Add(proxyOn
            ? new DiagItem("Системный прокси", DiagLevel.Warn, $"включён: {proxyServer} — отключите, если не пользуетесь")
            : new DiagItem("Системный прокси", DiagLevel.Ok, "выключен"));

        items.Add(HasSysDriver(paths.BinDir)
            ? new DiagItem("Драйвер WinDivert", DiagLevel.Ok, @"bin\WinDivert64.sys на месте")
            : new DiagItem("Драйвер WinDivert", DiagLevel.Fail, @"файл bin\WinDivert64.sys не найден"));

        var bypasses = FindServices(services, "GoodbyeDPI", "zapret", "discordfix_zapret", "winws1", "windivert14");
        items.Add(bypasses.Count == 0
            ? new DiagItem("Другие обходы DPI", DiagLevel.Ok, "конфликтующих служб не найдено")
            : new DiagItem("Другие обходы DPI", DiagLevel.Fail, "найдено: " + string.Join(", ", bypasses) + " — удалите их"));

        if (!ProcessRunning("winws2", "winws") && IsRunning(services, "WinDivert"))
            items.Add(new DiagItem("Служба WinDivert", DiagLevel.Warn, "активна, хотя движок не запущен — снимется при следующем запуске"));

        AddConflict(items, "Adguard", ProcessRunning("AdguardSvc"), "процесс AdguardSvc найден — может мешать Discord");
        AddConflict(items, "Killer (сеть)", FindServices(services, "Killer").Count > 0, "службы Killer конфликтуют с обходом");
        AddConflict(items, "Intel Connectivity Network", HasIntelConnectivity(services), "конфликтует с обходом");
        AddConflict(items, "Check Point", FindServices(services, "TracSrvWrapper", "EPWD").Count > 0, "конфликтует с обходом");
        AddConflict(items, "SmartByte", FindServices(services, "SmartByte").Count > 0, "конфликтует с обходом");

        var vpns = FindServices(services, "VPN");
        items.Add(vpns.Count > 0
            ? new DiagItem("VPN", DiagLevel.Warn, "найдены службы: " + string.Join(", ", vpns) + " — отключите на время")
            : new DiagItem("VPN", DiagLevel.Ok, "активных VPN-служб не найдено"));

        items.Add(IsDohConfigured()
            ? new DiagItem("Защищённый DNS", DiagLevel.Ok, "DoH настроен")
            : new DiagItem("Защищённый DNS", DiagLevel.Warn, "не настроен — задайте DNS-over-HTTPS в браузере или Windows 11"));

        if (HostsHasYouTube())
            items.Add(new DiagItem("Файл hosts", DiagLevel.Warn, "содержит записи youtube.com/youtu.be — могут мешать доступу"));

        return items;
    }

    private static void AddConflict(List<DiagItem> items, string title, bool found, string badDetail)
        => items.Add(found ? new DiagItem(title, DiagLevel.Fail, badDetail) : new DiagItem(title, DiagLevel.Ok, "не обнаружено"));

    private static ServiceController[] SafeGetServices()
    {
        try { return ServiceController.GetServices(); }
        catch { return Array.Empty<ServiceController>(); }
    }

    private static bool IsRunning(ServiceController[] services, string name)
    {
        foreach (var s in services)
        {
            if (!string.Equals(s.ServiceName, name, StringComparison.OrdinalIgnoreCase))
                continue;
            try { return s.Status is ServiceControllerStatus.Running or ServiceControllerStatus.StartPending; }
            catch { return false; }
        }
        return false;
    }

    private static List<string> FindServices(ServiceController[] services, params string[] needles)
    {
        var found = new List<string>();
        foreach (var s in services)
        {
            string name, display;
            try { name = s.ServiceName; display = s.DisplayName; }
            catch { continue; }
            foreach (var n in needles)
            {
                if (name.Contains(n, StringComparison.OrdinalIgnoreCase) ||
                    display.Contains(n, StringComparison.OrdinalIgnoreCase))
                {
                    found.Add(name);
                    break;
                }
            }
        }
        return found;
    }

    private static bool HasIntelConnectivity(ServiceController[] services)
    {
        foreach (var s in services)
        {
            string d;
            try { d = s.DisplayName; }
            catch { continue; }
            if (d.Contains("Intel", StringComparison.OrdinalIgnoreCase) &&
                d.Contains("Connectivity", StringComparison.OrdinalIgnoreCase) &&
                d.Contains("Network", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool ProcessRunning(params string[] names)
    {
        foreach (var n in names)
        {
            try
            {
                var procs = Process.GetProcessesByName(n);
                var any = procs.Length > 0;
                foreach (var p in procs) p.Dispose();
                if (any) return true;
            }
            catch { }
        }
        return false;
    }

    private static bool HasSysDriver(string binDir)
    {
        try { return Directory.Exists(binDir) && Directory.EnumerateFiles(binDir, "*.sys").Any(); }
        catch { return false; }
    }

    private static bool HostsHasYouTube()
    {
        try
        {
            var hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers", "etc", "hosts");
            if (!File.Exists(hosts))
                return false;
            foreach (var raw in File.ReadLines(hosts))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                    continue;
                if (line.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                    line.Contains("youtu.be", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
        }
        catch { }
        return false;
    }

    private static (bool enabled, string server) ProxyState()
    {
        var o = RunCapture("reg", "query", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "/v", "ProxyEnable");
        var enabled = o.Contains("0x1");
        var server = "";
        if (enabled)
        {
            var so = RunCapture("reg", "query", @"HKCU\Software\Microsoft\Windows\CurrentVersion\Internet Settings", "/v", "ProxyServer");
            foreach (var line in so.Split('\n'))
                if (line.Contains("ProxyServer", StringComparison.OrdinalIgnoreCase))
                {
                    var parts = line.Split(new[] { "REG_SZ" }, StringSplitOptions.None);
                    if (parts.Length > 1) server = parts[1].Trim();
                }
        }
        return (enabled, server);
    }

    private static bool IsDohConfigured()
    {
        var o = RunCapture("reg", "query",
            @"HKLM\SYSTEM\CurrentControlSet\Services\Dnscache\InterfaceSpecificParameters", "/s", "/v", "DohFlags");
        foreach (var line in o.Split('\n'))
        {
            if (!line.Contains("DohFlags", StringComparison.OrdinalIgnoreCase))
                continue;
            var idx = line.IndexOf("0x", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0 && long.TryParse(line.AsSpan(idx + 2), System.Globalization.NumberStyles.HexNumber, null, out var v) && v > 0)
                return true;
        }
        return false;
    }

    private static string RunCapture(string exe, params string[] args)
    {
        try
        {
            var psi = new ProcessStartInfo(exe)
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
                return "";
            var o = p.StandardOutput.ReadToEnd();
            p.WaitForExit(8000);
            return o;
        }
        catch { return ""; }
    }
}

public enum ProbeKind { Https, Ping }

public sealed record TestTarget(string Name, ProbeKind Kind, string Host);

public sealed record TestResult(string Name, bool Ok, string Detail);

public enum ProbeState { Ok, Err, Na }

public sealed record ProbeCell(ProbeState State, string Detail);

public sealed record SiteAvailability(
    string Name, bool IsSite, ProbeCell? Http, ProbeCell? Tls12, ProbeCell? Tls13, ProbeCell Ping);

public sealed class TestProgress
{
    public int Done { get; init; }
    public int Total { get; init; }
    public TestResult? Result { get; init; }
}

public static class RestrictionTester
{
    public static readonly IReadOnlyList<TestTarget> DefaultTargets = new List<TestTarget>
    {
        new("Discord",            ProbeKind.Https, "discord.com"),
        new("Discord Gateway",    ProbeKind.Https, "gateway.discord.gg"),
        new("Discord CDN",        ProbeKind.Https, "cdn.discordapp.com"),
        new("YouTube",            ProbeKind.Https, "www.youtube.com"),
        new("YouTube (картинки)", ProbeKind.Https, "i.ytimg.com"),
        new("YouTube (видео)",    ProbeKind.Https, "redirector.googlevideo.com"),
        new("Google",             ProbeKind.Https, "www.google.com"),
        new("Cloudflare",         ProbeKind.Https, "www.cloudflare.com"),
        new("DNS Cloudflare",     ProbeKind.Ping,  "1.1.1.1"),
        new("DNS Google",         ProbeKind.Ping,  "8.8.8.8"),
        new("DNS Quad9",          ProbeKind.Ping,  "9.9.9.9"),
    };

    private static readonly bool Tls13Supported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000);

    public static async Task<List<SiteAvailability>> RunAsync(IProgress<TestProgress> progress, CancellationToken ct)
    {
        var targets = DefaultTargets;
        var results = new SiteAvailability[targets.Count];
        var done = 0;

        using var httpPlain = MakeClient(null);
        using var httpTls12 = MakeClient(SslProtocols.Tls12);
        using HttpClient? httpTls13 = Tls13Supported ? MakeClient(SslProtocols.Tls13) : null;

        var tasks = targets.Select(async (t, i) =>
        {
            SiteAvailability sa;
            if (t.Kind == ProbeKind.Https)
            {
                var httpTask = ProbeHttpAsync(httpPlain, $"http://{t.Host}", ct);
                var t12Task = ProbeHttpAsync(httpTls12, $"https://{t.Host}", ct);
                var t13Task = httpTls13 is null
                    ? Task.FromResult(new ProbeCell(ProbeState.Na, "TLS 1.3 не поддерживается ОС"))
                    : ProbeHttpAsync(httpTls13, $"https://{t.Host}", ct);
                var pingTask = ProbePingCellAsync(t.Host, ct);
                await Task.WhenAll(httpTask, t12Task, t13Task, pingTask).ConfigureAwait(false);
                sa = new SiteAvailability(t.Name, true, httpTask.Result, t12Task.Result, t13Task.Result, pingTask.Result);
            }
            else
            {
                var ping = await ProbePingCellAsync(t.Host, ct).ConfigureAwait(false);
                sa = new SiteAvailability(t.Name, false, null, null, null, ping);
            }

            results[i] = sa;
            var d = Interlocked.Increment(ref done);
            progress.Report(new TestProgress { Done = d, Total = targets.Count });
            return sa;
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.ToList();
    }

    private static HttpClient MakeClient(SslProtocols? proto)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        };
        if (proto is not null)
            handler.SslOptions.EnabledSslProtocols = proto.Value;
        var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Zapret2/1.0");
        return http;
    }

    private static async Task<ProbeCell> ProbeHttpAsync(HttpClient http, string url, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return new ProbeCell(ProbeState.Ok, $"код {(int)resp.StatusCode}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (PlatformNotSupportedException) { return new ProbeCell(ProbeState.Na, "не поддерживается ОС"); }
        catch (NotSupportedException) { return new ProbeCell(ProbeState.Na, "не поддерживается ОС"); }
        catch (Exception ex)
        {
            var reason = ex is TaskCanceledException or System.TimeoutException ? "таймаут" : "сброс / ошибка";
            return new ProbeCell(ProbeState.Err, reason);
        }
    }

    private static async Task<ProbeCell> ProbePingCellAsync(string host, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(host, TimeSpan.FromSeconds(3), cancellationToken: ct).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new ProbeCell(ProbeState.Ok, $"{reply.RoundtripTime} мс")
                : new ProbeCell(ProbeState.Err, "таймаут");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new ProbeCell(ProbeState.Err, "нет ответа"); }
    }
}

public static class DpiChecker
{
    private const string SuiteUrl = "https://hyperion-cs.github.io/dpi-checkers/ru/tcp-16-20/suite.v2.json";
    private const int PayloadBytes = 64 * 1024;

    private static List<(string Id, string Provider, string Host)>? _suite;

    private sealed class SuiteEntry
    {
        public string? Id { get; set; }
        public string? Provider { get; set; }
        public string? Country { get; set; }
        public string? Host { get; set; }
    }

    public static async Task<List<TestResult>> RunAsync(IProgress<TestProgress> progress, CancellationToken ct)
    {
        var suite = await GetSuiteAsync(ct).ConfigureAwait(false);
        var results = new List<TestResult>(suite.Count);

        if (suite.Count == 0)
        {
            results.Add(new TestResult("Список DPI-целей", false, "не удалось получить (нет сети?)"));
            return results;
        }

        var payload = new byte[PayloadBytes];
        RandomNumberGenerator.Fill(payload);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };

        var done = 0;
        var tasks = suite.Select(async e =>
        {
            var name = string.IsNullOrWhiteSpace(e.Provider) ? e.Host : e.Provider;
            var r = await ProbeAsync(http, name, e.Host, payload, ct).ConfigureAwait(false);
            lock (results)
            {
                results.Add(r);
                done++;
                progress.Report(new TestProgress { Done = done, Total = suite.Count, Result = r });
            }
            return r;
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return results.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static async Task<TestResult> ProbeAsync(HttpClient http, string name, string host, byte[] payload, CancellationToken ct)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, $"https://{host}") { Content = new ByteArrayContent(payload) };
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            return new TestResult(name, true, $"нет фриза · {(int)resp.StatusCode}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (TaskCanceledException) { return new TestResult(name, false, "фриз 16-20КБ · вероятно режется"); }
        catch (Exception) { return new TestResult(name, false, "сброс / ошибка соединения"); }
    }

    private static async Task<List<(string Id, string Provider, string Host)>> GetSuiteAsync(CancellationToken ct)
    {
        if (_suite is not null)
            return _suite;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var json = await http.GetStringAsync(SuiteUrl, ct).ConfigureAwait(false);
            var entries = JsonSerializer.Deserialize<List<SuiteEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            _suite = entries?
                .Where(e => !string.IsNullOrWhiteSpace(e.Host))
                .Select(e => (e.Id ?? "", e.Provider ?? "", e.Host!))
                .ToList() ?? new List<(string, string, string)>();
        }
        catch { _suite = new List<(string, string, string)>(); }

        return _suite;
    }
}
