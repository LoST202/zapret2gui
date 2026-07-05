using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text;
using System.Text.Json;

namespace Zapret.Core;

public enum IpFamily { Both, V4, V6 }

public sealed record IpsetUpdateProgress(int Done, int Total, string Provider, int V4, int V6);

public sealed record IpsetUpdateResult(
    bool Written, int V4Count, int V6Count, int AsnOk, int AsnFailed, int Added, int Removed, string? Error);

/// <summary>
/// Rebuilds <c>lists/ipset-all.txt</c> from the announced IPv4/IPv6 prefixes of a curated set of
/// hosting/CDN autonomous systems, fetched live from the RIPEstat API. This mirrors the well-known
/// Python AS-parser: fetch per ASN, keep global prefixes, collapse (merge nested/adjacent CIDRs into
/// a minimal set), sort, and write IPv4 then IPv6. The user's own additions live in ipset-user.txt
/// and are never touched.
/// </summary>
public static class IpsetUpdater
{
    private const string ApiUrl = "https://stat.ripe.net/data/announced-prefixes/data.json";
    private const string SourceApp = "zapret2gui";
    private const int Concurrency = 6;

    // Safety guard: never overwrite the live ipset with a suspiciously small result (mass API
    // failure, blackholed network, etc.). A healthy run yields tens of thousands of prefixes.
    private const int MinAcceptablePrefixes = 1000;

    public const string FileName = "ipset-all.txt";

    // Curated hosting/CDN autonomous systems (mirrors the reference AS-parser list). Order only
    // affects progress display — the output is sorted by address.
    public static readonly (string Name, string Asn)[] Providers =
    {
        ("Scaleway", "AS12876"), ("Hetzner", "AS24940"), ("Hetzner 2", "AS213230"),
        ("Hetzner 3", "AS212317"), ("Hetzner 4", "AS215859"), ("Akamai", "AS20940"),
        ("Akamai 2", "AS16625"), ("Akamai 3", "AS12222"), ("Akamai 4", "AS33905"),
        ("Akamai 5", "AS21342"), ("Akamai 6", "AS32787"), ("Akamai 7", "AS35994"),
        ("Akamai 8", "AS12400"), ("Akamai 9", "AS15802"), ("Akamai 10", "AS18209"),
        ("Akamai 11", "AS24319"), ("Akamai 12", "AS25019"), ("Akamai 13", "AS26008"),
        ("Akamai 14", "AS31108"), ("Akamai 15", "AS34164"), ("Akamai 16", "AS49846"),
        ("Akamai 17", "AS17204"), ("Akamai 18", "AS213120"), ("Akamai 19", "AS393234"),
        ("Akamai 20", "AS393560"), ("Akamai Cloud (Linode)", "AS63949"), ("DigitalOcean", "AS14061"),
        ("DigitalOcean 2", "AS46652"), ("DigitalOcean 3", "AS393406"), ("Datacamp, CDN77", "AS60068"),
        ("Datacamp, CDN77 2", "AS212238"), ("Contabo", "AS51167"), ("Contabo 2", "AS141995"),
        ("Contabo 3", "AS40021"), ("OVH", "AS16276"), ("OVH 2", "AS35540"),
        ("Vultr (Constant)", "AS20473"), ("Cloudflare", "AS13335"), ("Cloudflare 2", "AS14789"),
        ("Cloudflare 3", "AS132892"), ("Cloudflare 4", "AS395747"), ("Cloudflare 5", "AS209242"),
        ("Clouvider", "AS62240"), ("CreaNova", "AS51765"), ("Oracle Cloud", "AS31898"),
        ("Oracle 2", "AS1219"), ("Amazon", "AS16509"), ("Amazon 2", "AS14618"),
        ("Amazon 3", "AS8987"), ("G-Core", "AS199524"), ("G-Core 2", "AS202422"),
        ("Fellowship", "AS46461"), ("Fastly", "AS54113"), ("FranTech", "AS53667"),
        ("LogicForge", "AS208621"), ("Hostinger", "AS47583"), ("Hostinger 2", "AS204915"),
        ("Ionos", "AS8560"), ("Ionos 2", "AS15418"), ("DreamHost", "AS29873"),
        ("GoDaddy", "AS26496"), ("GoDaddy 2", "AS398101"), ("HostGator, BlueHost", "AS46606"),
        ("Cogent", "AS174"), ("Riot Games, Inc", "AS6507"), ("I3DNET (Discord)", "AS49544"),
        ("IOMART", "AS20860"), ("IOMART 2", "AS21130"), ("Google Cloud", "AS15169"),
        ("Microsoft Azure", "AS8075"), ("Melbicom", "AS8849"), ("Melbicom 2", "AS56630"),
        ("M247 Europe SRL", "AS9009"), ("M247 Europe SRL 2", "AS39675"),
        ("HostPapa, ColoCrossing", "AS36352"), ("Hurricane Electric", "AS6939"),
        ("GTT Communications", "AS3257"), ("NTT Global", "AS2914"), ("Telia Carrier", "AS1299"),
        ("Firstcolo", "AS44066"), ("Hosteur", "AS20773"), ("ITL DC", "AS210403"),
        ("TELECOM ITALIA SPARKLE S.p.A", "AS6762"), ("Orange (FTRSI)", "AS5511"),
        ("GlobeNet", "AS52320"), ("Lumen", "AS3356"), ("Tata Communications", "AS6453"),
        ("Verizon Business", "AS701"), ("Scalaxy", "AS58061"), ("Zenlayer", "AS21859"),
        ("BunnyCDN", "AS5065"), ("Edgio", "AS15133"), ("Edgio 2", "AS22843"),
        ("StackPath", "AS33438"), ("StackPath 2", "AS202384"), ("KeyCDN", "AS199653"),
        ("CacheFly", "AS30081"), ("Imperva_Incapsula", "AS19551"), ("Facebook", "AS32934"),
    };

    // ===== Editable ASN list (state/asn-list.txt), falling back to the built-in defaults =====

    public static string AsnFile(AppPaths paths) => Path.Combine(paths.StateDir, "asn-list.txt");

    public static IReadOnlyList<(string Name, string Asn)> LoadProviders(AppPaths paths)
    {
        var file = AsnFile(paths);
        if (File.Exists(file))
        {
            try
            {
                var parsed = ParseProviders(File.ReadAllText(file));
                if (parsed.Count > 0)
                    return parsed;
            }
            catch { }
        }
        return Providers;
    }

    /// <summary>Persists the edited list. An empty / parseless text deletes the override so the
    /// built-in defaults apply again. Returns the number of valid ASNs parsed.</summary>
    public static int SaveProviders(AppPaths paths, string text)
    {
        var parsed = ParseProviders(text);
        var file = AsnFile(paths);
        Directory.CreateDirectory(paths.StateDir);
        if (parsed.Count == 0)
        {
            try { File.Delete(file); } catch { }
            return 0;
        }
        File.WriteAllText(file, text.Replace("\r\n", "\n").TrimEnd() + "\n");
        return parsed.Count;
    }

    public static string FormatProviders(IEnumerable<(string Name, string Asn)> providers)
    {
        var sb = new StringBuilder();
        foreach (var (name, asn) in providers)
            sb.Append(asn).Append("  ").Append(name).Append('\n');
        return sb.ToString();
    }

    public static List<(string Name, string Asn)> ParseProviders(string text)
    {
        var result = new List<(string, string)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;
            var asn = ExtractAsn(line, out var idx, out var len);
            if (asn is null || !seen.Add(asn))
                continue;
            var name = line.Remove(idx, len).Trim().Trim('=', ':', '-', '\t', ' ').Trim();
            result.Add((name.Length == 0 ? asn : name, asn));
        }
        return result;
    }

    // First standalone "AS<digits>" token (case-insensitive) not glued to a preceding letter.
    private static string? ExtractAsn(string line, out int idx, out int len)
    {
        for (var i = 0; i + 2 < line.Length; i++)
        {
            if (line[i] is 'A' or 'a' && line[i + 1] is 'S' or 's' &&
                (i == 0 || !char.IsLetter(line[i - 1])))
            {
                var j = i + 2;
                while (j < line.Length && char.IsDigit(line[j]))
                    j++;
                if (j > i + 2)
                {
                    idx = i;
                    len = j - i;
                    return "AS" + line[(i + 2)..j];
                }
            }
        }
        idx = -1;
        len = 0;
        return null;
    }

    public static IpFamily ParseFamily(string? s) => s?.ToLowerInvariant() switch
    {
        "v4" => IpFamily.V4,
        "v6" => IpFamily.V6,
        _ => IpFamily.Both,
    };

    // ===== Public entry point =====

    public static async Task<IpsetUpdateResult> RunAsync(
        AppPaths paths, IpFamily family, IProgress<IpsetUpdateProgress>? progress, CancellationToken ct)
    {
        var providers = LoadProviders(paths);
        var prefixes = new List<string>(200_000);
        int ok = 0, failed = 0, done = 0;
        var sync = new object();

        using var gate = new SemaphoreSlim(Concurrency);
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(SourceApp);

        var tasks = providers.Select(async p =>
        {
            await gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                var got = await FetchAsnPrefixesAsync(http, p.Asn, ct).ConfigureAwait(false);
                lock (sync) { prefixes.AddRange(got); ok++; }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { lock (sync) { failed++; } }
            finally
            {
                gate.Release();
                int d;
                lock (sync) { d = ++done; }
                progress?.Report(new IpsetUpdateProgress(d, providers.Count, p.Name, 0, 0));
            }
        });
        await Task.WhenAll(tasks).ConfigureAwait(false);

        var (allV4, allV6) = Collapse(prefixes);
        var newV4 = family != IpFamily.V6 ? allV4 : new List<string>();
        var newV6 = family != IpFamily.V4 ? allV6 : new List<string>();
        var total = newV4.Count + newV6.Count;

        if (total < MinAcceptablePrefixes || ok < providers.Count / 2)
            return new IpsetUpdateResult(false, newV4.Count, newV6.Count, ok, failed, 0, 0,
                $"Получено слишком мало данных ({total} подсетей, {ok}/{providers.Count} ASN) — файл не изменён.");

        // Diff against the current file (exact CIDR lines) so the UI can report added / removed.
        var target = paths.List(FileName);
        var oldSet = ReadCidrLines(target);
        var newSet = new HashSet<string>(newV4.Concat(newV6), StringComparer.OrdinalIgnoreCase);
        var added = newSet.Count(l => !oldSet.Contains(l));
        var removed = oldSet.Count(l => !newSet.Contains(l));

        var tmp = target + ".tmp";
        Directory.CreateDirectory(paths.ListsDir);
        await File.WriteAllLinesAsync(tmp, newV4.Concat(newV6), ct).ConfigureAwait(false);
        try { if (File.Exists(target)) File.Copy(target, target + ".bak", overwrite: true); } catch { }
        File.Move(tmp, target, overwrite: true);

        return new IpsetUpdateResult(true, newV4.Count, newV6.Count, ok, failed, added, removed, null);
    }

    private static HashSet<string> ReadCidrLines(string path)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!File.Exists(path))
            return set;
        try
        {
            foreach (var raw in File.ReadLines(path))
            {
                var l = raw.Trim();
                if (l.Length > 0 && !l.StartsWith('#'))
                    set.Add(l);
            }
        }
        catch { }
        return set;
    }

    private static async Task<List<string>> FetchAsnPrefixesAsync(HttpClient http, string asn, CancellationToken ct)
    {
        var url = $"{ApiUrl}?resource={asn}&min_peers_seeing=1&sourceapp={SourceApp}";
        using var resp = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct).ConfigureAwait(false);

        var list = new List<string>();
        if (doc.RootElement.TryGetProperty("data", out var data) &&
            data.TryGetProperty("prefixes", out var prefixes) &&
            prefixes.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in prefixes.EnumerateArray())
                if (el.TryGetProperty("prefix", out var pfx) && pfx.GetString() is { } s)
                    list.Add(s);
        }
        return list;
    }

    // ===== Pure, testable core: prefixes -> collapsed, sorted CIDR lists =====

    /// <summary>
    /// Filters to global prefixes, collapses each family into a minimal set of CIDR blocks, and
    /// returns them sorted by address (IPv4 and IPv6 separately).
    /// </summary>
    public static (List<string> V4, List<string> V6) Collapse(IEnumerable<string> prefixes)
    {
        var v4 = new HashSet<(uint Start, uint End)>();
        var v6 = new HashSet<(UInt128 Start, UInt128 End)>();
        foreach (var p in prefixes)
            AddPrefix(p, v4, v6);

        var v4Out = new List<string>();
        foreach (var (s, e) in Merge(v4.ToList()))
            foreach (var (addr, prefix) in RangeToCidrs<uint>(s, e, 32))
                v4Out.Add(FormatV4(addr, prefix));

        var v6Out = new List<string>();
        foreach (var (s, e) in Merge(v6.ToList()))
            foreach (var (addr, prefix) in RangeToCidrs<UInt128>(s, e, 128))
                v6Out.Add(FormatV6(addr, prefix));

        return (v4Out, v6Out);
    }

    private static void AddPrefix(string prefix, HashSet<(uint, uint)> v4, HashSet<(UInt128, UInt128)> v6)
    {
        var slash = prefix.IndexOf('/');
        var addrPart = slash >= 0 ? prefix[..slash] : prefix;
        if (!IPAddress.TryParse(addrPart, out var ip))
            return;

        if (ip.AddressFamily == AddressFamily.InterNetwork)
        {
            var prefixLen = slash >= 0 && int.TryParse(prefix[(slash + 1)..], out var pl) ? pl : 32;
            if (prefixLen <= 0 || prefixLen > 32)
                return;
            Span<byte> b = stackalloc byte[4];
            ip.TryWriteBytes(b, out _);
            var val = BinaryPrimitives.ReadUInt32BigEndian(b);
            var hostBits = 32 - prefixLen;
            var span = hostBits == 0 ? 0u : (1u << hostBits) - 1u;
            var netmask = ~span;
            var start = val & netmask;
            var end = start | span;
            if (IsGlobalV4(start, end))
                v4.Add((start, end));
        }
        else if (ip.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var prefixLen = slash >= 0 && int.TryParse(prefix[(slash + 1)..], out var pl) ? pl : 128;
            if (prefixLen <= 0 || prefixLen > 128)
                return;
            Span<byte> b = stackalloc byte[16];
            ip.TryWriteBytes(b, out _);
            var val = BinaryPrimitives.ReadUInt128BigEndian(b);
            var hostBits = 128 - prefixLen;
            var span = hostBits == 0 ? UInt128.Zero : (UInt128.One << hostBits) - UInt128.One;
            var netmask = ~span;
            var start = val & netmask;
            var end = start | span;
            if (IsGlobalV6(start, end))
                v6.Add((start, end));
        }
    }

    // ===== Generic range merge + range->CIDR decomposition (the "collapse" of the Python parser) =====

    private static List<(T Start, T End)> Merge<T>(List<(T Start, T End)> ranges)
        where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        ranges.Sort((a, b) => a.Start.CompareTo(b.Start));
        var merged = new List<(T, T)>(ranges.Count);
        foreach (var (s, e) in ranges)
        {
            if (merged.Count == 0)
            {
                merged.Add((s, e));
                continue;
            }
            var (cs, ce) = merged[^1];
            // Overlapping or directly adjacent (s == ce + 1) ranges combine. The MaxValue guard
            // avoids ce + 1 overflowing back to zero.
            var touches = s <= ce || (ce != T.MaxValue && s <= ce + T.One);
            if (touches)
                merged[^1] = (cs, e > ce ? e : ce);
            else
                merged.Add((s, e));
        }
        return merged;
    }

    private static IEnumerable<(T Addr, int Prefix)> RangeToCidrs<T>(T start, T end, int width)
        where T : IBinaryInteger<T>, IMinMaxValue<T>
    {
        while (start <= end)
        {
            // Largest aligned power-of-two block that starts at `start` (all-zero start = full width).
            var alignBits = int.CreateChecked(T.TrailingZeroCount(start));
            if (alignBits > width)
                alignBits = width;

            // Largest block that still fits inside the remaining count.
            var count = end - start + T.One;         // wraps to 0 only for the whole-space range
            var countBits = count == T.Zero ? width : int.CreateChecked(T.Log2(count));

            var bits = Math.Min(alignBits, countBits);
            yield return (start, width - bits);

            if (bits >= width)
                yield break;
            var next = start + (T.One << bits);
            if (next < start)                        // advanced past MaxValue -> done
                yield break;
            start = next;
        }
    }

    private static string FormatV4(uint addr, int prefix)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, addr);
        return $"{new IPAddress(b)}/{prefix}";
    }

    private static string FormatV6(UInt128 addr, int prefix)
    {
        Span<byte> b = stackalloc byte[16];
        BinaryPrimitives.WriteUInt128BigEndian(b, addr);
        return $"{new IPAddress(b)}/{prefix}";
    }

    // ===== "is_global" filtering (skip private / reserved / bogon prefixes) =====

    private static readonly (uint Start, uint End)[] NonGlobalV4 = BuildV4Bogons();
    private static readonly (UInt128 Start, UInt128 End)[] NonGlobalV6 = BuildV6Bogons();

    private static bool IsGlobalV4(uint start, uint end)
    {
        foreach (var (bs, be) in NonGlobalV4)
            if (start <= be && end >= bs)     // overlaps a non-global range
                return false;
        return true;
    }

    private static bool IsGlobalV6(UInt128 start, UInt128 end)
    {
        foreach (var (bs, be) in NonGlobalV6)
            if (start <= be && end >= bs)
                return false;
        return true;
    }

    private static (uint, uint)[] BuildV4Bogons()
    {
        // RFC 1918 / special-use IPv4 that RIPE-announced global prefixes should never include.
        string[] cidrs =
        {
            "0.0.0.0/8", "10.0.0.0/8", "100.64.0.0/10", "127.0.0.0/8", "169.254.0.0/16",
            "172.16.0.0/12", "192.0.0.0/24", "192.0.2.0/24", "192.88.99.0/24", "192.168.0.0/16",
            "198.18.0.0/15", "198.51.100.0/24", "203.0.113.0/24", "224.0.0.0/4", "240.0.0.0/4",
        };
        return cidrs.Select(V4Range).ToArray();
    }

    private static (UInt128, UInt128)[] BuildV6Bogons()
    {
        // Special-use IPv6 blocks (loopback, ULA, link-local, multicast, documentation, v4-mapped).
        string[] cidrs =
        {
            "::/8", "100::/64", "2001:db8::/32", "fc00::/7", "fe80::/10", "ff00::/8", "::ffff:0:0/96",
        };
        return cidrs.Select(V6Range).ToArray();
    }

    private static (uint, uint) V4Range(string cidr)
    {
        var slash = cidr.IndexOf('/');
        var ip = IPAddress.Parse(cidr[..slash]);
        var prefixLen = int.Parse(cidr[(slash + 1)..]);
        Span<byte> b = stackalloc byte[4];
        ip.TryWriteBytes(b, out _);
        var val = BinaryPrimitives.ReadUInt32BigEndian(b);
        var hostBits = 32 - prefixLen;
        var span = hostBits == 0 ? 0u : (hostBits >= 32 ? uint.MaxValue : (1u << hostBits) - 1u);
        return (val & ~span, (val & ~span) | span);
    }

    private static (UInt128, UInt128) V6Range(string cidr)
    {
        var slash = cidr.IndexOf('/');
        var ip = IPAddress.Parse(cidr[..slash]);
        var prefixLen = int.Parse(cidr[(slash + 1)..]);
        Span<byte> b = stackalloc byte[16];
        ip.TryWriteBytes(b, out _);
        var val = BinaryPrimitives.ReadUInt128BigEndian(b);
        var hostBits = 128 - prefixLen;
        var span = hostBits == 0 ? UInt128.Zero : (UInt128.One << hostBits) - UInt128.One;
        return (val & ~span, (val & ~span) | span);
    }
}
