using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.NetworkInformation;
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

        foreach (var svc in services)
        {
            try { svc.Dispose(); }
            catch { }
        }
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
            // Read BOTH streams asynchronously and gate on WaitForExit, not on ReadToEnd: a blocking
            // stdout read would never return for a hung child, so the timeout-kill below would be
            // unreachable and the whole call would hang. Draining both also avoids a pipe-buffer deadlock.
            var outTask = p.StandardOutput.ReadToEndAsync();
            var errTask = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(8000))
            {
                // A genuinely stuck child (e.g. a hung reg query) is otherwise orphaned by `using` —
                // Process.Dispose does not kill it. Killing it closes the pipes so the reads complete.
                try { p.Kill(entireProcessTree: true); }
                catch { }
            }
            var o = "";
            try { if (outTask.Wait(2000)) o = outTask.Result; }
            catch { }
            try { errTask.Wait(1000); }
            catch { }
            return o;
        }
        catch { return ""; }
    }
}

public sealed record SysNet(
    string PublicIp, string Asn, string Isp, string Location,
    string Os, string Machine, string Cpu, string Ram, string Uptime);

/// <summary>Collects public IP / ASN / ISP (via ipinfo.io) plus local system facts for the info panel.</summary>
public static class SysNetInfo
{
    public static async Task<SysNet> CollectAsync(CancellationToken ct = default)
    {
        var (ip, asn, isp, loc) = await FetchPublicAsync(ct).ConfigureAwait(false);
        return new SysNet(
            PublicIp: ip,
            Asn: asn,
            Isp: isp,
            Location: loc,
            Os: System.Runtime.InteropServices.RuntimeInformation.OSDescription,
            Machine: Environment.MachineName,
            Cpu: Cpu(),
            Ram: Ram(),
            Uptime: Uptime());
    }

    private static async Task<(string ip, string asn, string isp, string loc)> FetchPublicAsync(CancellationToken ct)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Zapret2/1.0");
            var json = await http.GetStringAsync("https://ipinfo.io/json", ct).ConfigureAwait(false);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            var ip = Str(root, "ip");
            var org = Str(root, "org");              // "AS15169 Google LLC"
            var asn = "";
            var isp = org;
            if (org.StartsWith("AS", StringComparison.OrdinalIgnoreCase))
            {
                var sp = org.IndexOf(' ');
                if (sp > 0) { asn = org[..sp]; isp = org[(sp + 1)..]; }
            }
            var loc = string.Join(", ",
                new[] { Str(root, "city"), Str(root, "region"), Str(root, "country") }.Where(s => s.Length > 0));

            return (Dash(ip), Dash(asn), Dash(isp), Dash(loc));
        }
        catch { return ("недоступно", "—", "—", "—"); }
    }

    private static string Dash(string s) => string.IsNullOrWhiteSpace(s) ? "—" : s;

    private static string Str(JsonElement e, string name)
        => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static string Cpu()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var name = (key?.GetValue("ProcessorNameString") as string)?.Trim();
            return string.IsNullOrWhiteSpace(name)
                ? $"{Environment.ProcessorCount} логич. потоков"
                : $"{name} · {Environment.ProcessorCount} потоков";
        }
        catch { return $"{Environment.ProcessorCount} логич. потоков"; }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MemStatus
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile,
                     ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MemStatus buffer);

    private static string Ram()
    {
        try
        {
            var s = new MemStatus { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MemStatus>() };
            if (GlobalMemoryStatusEx(ref s))
            {
                const double gb = 1024.0 * 1024 * 1024;
                var used = (s.ullTotalPhys - s.ullAvailPhys) / gb;
                var total = s.ullTotalPhys / gb;
                return $"{used:0.0} / {total:0.0} ГБ ({s.dwMemoryLoad}%)";
            }
        }
        catch { }
        return "—";
    }

    private static string Uptime()
    {
        var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return t.Days > 0 ? $"{t.Days} д {t.Hours} ч {t.Minutes} мин" : $"{t.Hours} ч {t.Minutes} мин";
    }
}

public enum ProbeKind { Https, Ping }

public sealed record TestTarget(string Name, ProbeKind Kind, string Host, string? Url = null);

public sealed record TestResult(string Name, bool Ok, string Detail);

public sealed class TestProgress
{
    public int Total { get; init; }
    public TestResult? Result { get; init; }
}

public static class RestrictionTester
{
    public static readonly IReadOnlyList<TestTarget> DefaultTargets = new List<TestTarget>
    {
        new("Discord",            ProbeKind.Https, "discord.com",              "https://discord.com"),
        new("Discord Gateway",    ProbeKind.Https, "gateway.discord.gg",       "https://gateway.discord.gg"),
        new("Discord CDN",        ProbeKind.Https, "cdn.discordapp.com",       "https://cdn.discordapp.com"),
        new("YouTube",            ProbeKind.Https, "www.youtube.com",          "https://www.youtube.com"),
        new("YouTube (картинки)", ProbeKind.Https, "i.ytimg.com",              "https://i.ytimg.com"),
        new("YouTube (видео)",    ProbeKind.Https, "redirector.googlevideo.com","https://redirector.googlevideo.com"),
        new("Google",             ProbeKind.Https, "www.google.com",           "https://www.google.com"),
        new("Cloudflare",         ProbeKind.Https, "www.cloudflare.com",       "https://www.cloudflare.com"),
        new("DNS Cloudflare",     ProbeKind.Ping,  "1.1.1.1"),
        new("DNS Google",         ProbeKind.Ping,  "8.8.8.8"),
        new("DNS Quad9",          ProbeKind.Ping,  "9.9.9.9"),
    };

    public static async Task<List<TestResult>> RunAsync(IProgress<TestProgress> progress, CancellationToken ct)
    {
        var targets = DefaultTargets;
        var results = new List<TestResult>(targets.Count);

        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            ConnectTimeout = TimeSpan.FromSeconds(5),
            AutomaticDecompression = System.Net.DecompressionMethods.None,
        };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(6) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Zapret2/1.0");

        var tasks = targets.Select(async t =>
        {
            var r = t.Kind == ProbeKind.Https
                ? await ProbeHttpsAsync(http, t, ct).ConfigureAwait(false)
                : await ProbePingAsync(t, ct).ConfigureAwait(false);

            lock (results)
            {
                results.Add(r);
                progress.Report(new TestProgress { Total = targets.Count, Result = r });
            }
            return r;
        });

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return targets.Select(t => results.First(r => r.Name == t.Name)).ToList();
    }

    private static async Task<TestResult> ProbeHttpsAsync(HttpClient http, TestTarget t, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, t.Url);
            using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            sw.Stop();
            var code = (int)resp.StatusCode;
            // AllowAutoRedirect=false, so 3xx are genuine redirects from a reachable host. A DPI box
            // often injects a 403/451 blockpage on a blocked site — that must not read as "доступно".
            if (code is 403 or 451)
                return new TestResult(t.Name, false, $"заблокировано · {code}");
            if (code >= 400)
                return new TestResult(t.Name, false, $"недоступно · {code}");
            return new TestResult(t.Name, true, $"доступно · {code} · {sw.ElapsedMilliseconds} мс");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            var reason = ex is TaskCanceledException or System.TimeoutException ? "таймаут" : "сброс соединения";
            return new TestResult(t.Name, false, $"недоступно · {reason}");
        }
    }

    private static async Task<TestResult> ProbePingAsync(TestTarget t, CancellationToken ct)
    {
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(t.Host, TimeSpan.FromSeconds(3), cancellationToken: ct).ConfigureAwait(false);
            return reply.Status == IPStatus.Success
                ? new TestResult(t.Name, true, $"пинг {reply.RoundtripTime} мс")
                : new TestResult(t.Name, false, $"нет ответа · {reply.Status}");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch { return new TestResult(t.Name, false, "нет ответа"); }
    }
}

public static class DpiChecker
{
    private const string SuiteUrl = "https://hyperion-cs.github.io/dpi-checkers/ru/tcp-16-20/suite.v2.json";
    private const int PayloadBytes = 64 * 1024;

    private static List<(string Provider, string Host)>? _suite;

    private sealed class SuiteEntry
    {
        public string? Provider { get; set; }
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

        var tasks = suite.Select(async e =>
        {
            var name = string.IsNullOrWhiteSpace(e.Provider) ? e.Host : e.Provider;
            var r = await ProbeAsync(http, name, e.Host, payload, ct).ConfigureAwait(false);
            lock (results)
            {
                results.Add(r);
                progress.Report(new TestProgress { Total = suite.Count, Result = r });
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

    private static async Task<List<(string Provider, string Host)>> GetSuiteAsync(CancellationToken ct)
    {
        if (_suite is not null)
            return _suite;

        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            var json = await http.GetStringAsync(SuiteUrl, ct).ConfigureAwait(false);
            var entries = JsonSerializer.Deserialize<List<SuiteEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            var list = entries?
                .Where(e => !string.IsNullOrWhiteSpace(e.Host))
                .Select(e => (e.Provider ?? "", e.Host!))
                .ToList() ?? new List<(string, string)>();

            // Only cache a real result. A transient network failure (or an empty payload) must NOT
            // poison the cache for the whole app lifetime — the DPI check runs precisely when the
            // network is flaky (during an engine toggle), so return it transiently and retry next time.
            if (list.Count > 0)
                _suite = list;
            return list;
        }
        catch { return new List<(string, string)>(); }
    }
}
