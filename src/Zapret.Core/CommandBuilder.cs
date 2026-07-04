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
        static string Gen() =>
            "--hostlist=" + Lst("list-general.txt") + " --hostlist=" + Lst("list-general-user.txt") + " " + Excl();

        var dg = string.Join(" ", s.Dg);
        var dgen = string.Join(" ", s.Dgen);

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
            "--filter-udp=443 --filter-l7=quic " + Gen() + " --payload=quic_initial --lua-desync=fake:blob=quic_google:repeats=6",
            "--new",
            "--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --payload=stun,discord_ip_discovery --lua-desync=fake:blob=quic_discord:repeats=6",
            "--new",
            "--filter-tcp=2053,2083,2087,2096,8443 --filter-l7=tls --hostlist-domains=discord.media --payload=tls_client_hello " + dg,
            "--new",
            "--filter-tcp=443 --filter-l7=tls --hostlist=" + Lst("list-google.txt") + " --payload=tls_client_hello " + dg,
            "--new",
            "--filter-tcp=80,443 --filter-l7=tls,http " + Gen() + " --payload=tls_client_hello,http_req " + dgen,
            "--new",
            "--filter-udp=443 --filter-l7=quic --ipset=" + Lst("ipset-all.txt") + " " + Excl() + " --payload=quic_initial --lua-desync=fake:blob=quic_google:repeats=6",
            "--new",
            "--filter-tcp=80,443,8443 --filter-l7=tls,http --ipset=" + Lst("ipset-all.txt") + " " + Excl() + " --payload=tls_client_hello,http_req " + dgen,
        };

        if (s.AutoHostlist)
        {
            lines.Add("--new");
            lines.Add("--filter-tcp=80,443 --filter-l7=tls,http --hostlist-auto=" + Lst("list-auto.txt") + " " + Excl() +
                      " --hostlist-auto-fail-threshold=3 --hostlist-auto-fail-time=60 --payload=tls_client_hello,http_req " + dgen);
        }

        return string.Join(Environment.NewLine, lines);
    }
}
