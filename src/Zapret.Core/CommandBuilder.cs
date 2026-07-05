namespace Zapret.Core;

public static class CommandBuilder
{
    public static string ToText(Strategy s)
    {
        static string Bin(string f) => "\"%~dp0bin\\" + f + "\"";
        static string Lua(string f) => "\"%~dp0lua\\" + f + "\"";
        static string Lst(string f) => "\"%~dp0lists\\" + f + "\"";
        static string Excl() =>
            "--hostlist-exclude=" + Lst("list-exclude.txt") + " --hostlist-exclude=" + Lst("list-exclude-user.txt") +
            " --ipset-exclude=" + Lst("ipset-exclude.txt") + " --ipset-exclude=" + Lst("ipset-exclude-user.txt");
        // Pinned "default" list: Cloudflare + Discord (TLS/HTTP). Google is pinned separately (own desync).
        static string Def() =>
            "--hostlist=" + Lst("default.txt") + " --hostlist=" + Lst("default-user.txt") + " " + Excl();

        var dg = string.Join(" ", s.Dg ?? Array.Empty<string>());      // desync for Google / YouTube
        var dgen = string.Join(" ", s.Dgen ?? Array.Empty<string>());  // desync for everything else (default + autohostlist + ipset)

        var lines = new List<string>
        {
            "--wf-tcp-out=80,443,2053,2083,2087,2096,8443 --wf-udp-out=443,19294-19344,50000-50100",
            "--lua-init=@" + Lua("zapret-lib.lua") + " --lua-init=@" + Lua("zapret-antidpi.lua"),
            "--blob=quic_google:@" + Bin("quic_initial_www_google_com.bin") +
                " --blob=quic_discord:@" + Bin("quic_initial_dbankcloud_ru.bin") +
                " --blob=tls_google:@" + Bin("tls_clienthello_www_google_com.bin") +
                " --blob=tls_4pda:@" + Bin("tls_clienthello_4pda_to.bin") +
                " --blob=tls_max:@" + Bin("tls_clienthello_max_ru.bin") +
                " --blob=blob_stun:@" + Bin("stun.bin"),
            // QUIC / UDP: Discord voice + STUN, and QUIC over the pinned list.
            "--filter-udp=443 --filter-l7=quic " + Def() + " --payload=quic_initial --lua-desync=fake:blob=quic_google:repeats=6",
            "--new",
            "--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --payload=stun,discord_ip_discovery --lua-desync=fake:blob=quic_discord:repeats=6",
            "--new",
            // Уровень 1 — Google / YouTube (собственный десинк).
            "--filter-tcp=443 --filter-l7=tls --hostlist=" + Lst("list-google.txt") + " --payload=tls_client_hello " + dg,
            "--new",
            // Уровень 2 — default.txt (Cloudflare + Discord; включая Cloudflare-порты Discord 2053-8443).
            "--filter-tcp=80,443,2053,2083,2087,2096,8443 --filter-l7=tls,http " + Def() + " --payload=tls_client_hello,http_req " + dgen,
            "--new",
            // IP-сеты — покрытие хостов, распознаваемых только по IP.
            "--filter-udp=443 --filter-l7=quic --ipset=" + Lst("ipset-all.txt") + " --ipset=" + Lst("ipset-user.txt") + " " + Excl() + " --payload=quic_initial --lua-desync=fake:blob=quic_google:repeats=6",
            "--new",
            "--filter-tcp=80,443,8443 --filter-l7=tls,http --ipset=" + Lst("ipset-all.txt") + " --ipset=" + Lst("ipset-user.txt") + " " + Excl() + " --payload=tls_client_hello,http_req " + dgen,
        };

        // Auto-hostlist is always part of every strategy (no on/off flag — it can't be "baked away").
        lines.Add("--new");
        lines.Add("--filter-tcp=80,443 --filter-l7=tls,http --hostlist-auto=" + Lst("list-auto.txt") + " " + Excl() +
                  " --hostlist-auto-fail-threshold=3 --hostlist-auto-fail-time=60 --payload=tls_client_hello,http_req " + dgen);

        return string.Join(Environment.NewLine, lines);
    }
}
