namespace Zapret.App;

/// <summary>One completion/reference entry (flag, desync method, parameter or value) with a short Russian hint.</summary>
internal sealed record WinwsItem(string Name, string Hint, bool TakesValue = false, string Group = "")
{
    public string Insert => TakesValue ? Name + "=" : Name;
}

/// <summary>
/// winws2 (zapret2 / NFQWS2) command grammar. Flags come from the upstream manual
/// (github.com/bol-van/zapret2, docs/manual.en.md); desync methods/params from the shipped
/// Lua library (zapret-antidpi.lua). Windows-relevant options only.
/// </summary>
internal static class WinwsSyntax
{
    public static readonly WinwsItem[] Flags =
    {
        // --- Профиль / цепочка стратегий ---
        new("--new", "начать новый профиль (разделитель стратегий)", Group: "Профиль"),
        new("--name", "имя профиля", TakesValue: true, Group: "Профиль"),
        new("--skip", "пропустить (отключить) текущий профиль", Group: "Профиль"),
        new("--template", "объявить профиль шаблоном для повторного использования", Group: "Профиль"),
        new("--import", "скопировать настройки шаблона в текущий профиль", TakesValue: true, Group: "Профиль"),
        new("--cookie", "задать desync.cookie для Lua", TakesValue: true, Group: "Профиль"),

        // --- Фильтры профиля L3/L4 ---
        new("--filter-l3", "фильтр по версии IP: ipv4 | ipv6", TakesValue: true, Group: "Фильтры"),
        new("--filter-tcp", "TCP-порты: [~]порт[-порт], *, список", TakesValue: true, Group: "Фильтры"),
        new("--filter-udp", "UDP-порты", TakesValue: true, Group: "Фильтры"),
        new("--filter-icmp", "ICMP тип[:код]", TakesValue: true, Group: "Фильтры"),
        new("--filter-ipp", "номера IP-протоколов", TakesValue: true, Group: "Фильтры"),
        new("--filter-l7", "протокол L7: tls, http, quic, discord, stun, dns…", TakesValue: true, Group: "Фильтры"),
        new("--payload", "тип нагрузки: tls_client_hello, http_req, quic_initial…", TakesValue: true, Group: "Фильтры"),
        new("--out-range", "диапазон счётчика conntrack, исходящий", TakesValue: true, Group: "Фильтры"),
        new("--in-range", "диапазон счётчика conntrack, входящий", TakesValue: true, Group: "Фильтры"),

        // --- Инициализация desync ---
        new("--lua-init", "загрузить Lua движка: @файл", TakesValue: true, Group: "Инициализация"),
        new("--blob", "именованный бинарный blob: имя:@файл или имя:0xHEX", TakesValue: true, Group: "Инициализация"),
        new("--lua-desync", "десинк: METHOD:параметр=значение", TakesValue: true, Group: "Инициализация"),

        // --- Списки ---
        new("--hostlist", "файл списка доменов (include)", TakesValue: true, Group: "Списки"),
        new("--hostlist-domains", "домены прямо в аргументе (include)", TakesValue: true, Group: "Списки"),
        new("--hostlist-exclude", "файл доменов-исключений", TakesValue: true, Group: "Списки"),
        new("--hostlist-exclude-domains", "домены-исключения в аргументе", TakesValue: true, Group: "Списки"),
        new("--hostlist-auto", "файл авто-хостлиста (пополняется сам)", TakesValue: true, Group: "Списки"),
        new("--ipset", "файл списка IP/подсетей (include)", TakesValue: true, Group: "Списки"),
        new("--ipset-ip", "IP/подсети прямо в аргументе", TakesValue: true, Group: "Списки"),
        new("--ipset-exclude", "файл IP-исключений", TakesValue: true, Group: "Списки"),
        new("--ipset-exclude-ip", "IP-исключения в аргументе", TakesValue: true, Group: "Списки"),

        // --- Авто-хостлист (обучение) ---
        new("--hostlist-auto-fail-threshold", "неудач подряд до добавления хоста (3)", TakesValue: true, Group: "Авто-хостлист"),
        new("--hostlist-auto-fail-time", "окно между неудачами, сек (60)", TakesValue: true, Group: "Авто-хостлист"),
        new("--hostlist-auto-retrans-threshold", "ретрансмиссий TCP как неудача (3)", TakesValue: true, Group: "Авто-хостлист"),
        new("--hostlist-auto-retrans-reset", "слать RST ретрансмиттеру: 0|1", TakesValue: true, Group: "Авто-хостлист"),
        new("--hostlist-auto-debug", "файл лога авто-хостлиста", TakesValue: true, Group: "Авто-хостлист"),

        // --- WinDivert (Windows) ---
        new("--wf-iface", "индекс сетевого интерфейса для WinDivert", TakesValue: true, Group: "WinDivert"),
        new("--wf-l3", "версия IP фильтра: ipv4 | ipv6", TakesValue: true, Group: "WinDivert"),
        new("--wf-tcp-out", "TCP-порты для перехвата (исходящие)", TakesValue: true, Group: "WinDivert"),
        new("--wf-tcp-in", "TCP-порты для перехвата (входящие)", TakesValue: true, Group: "WinDivert"),
        new("--wf-udp-out", "UDP-порты для перехвата (исходящие)", TakesValue: true, Group: "WinDivert"),
        new("--wf-udp-in", "UDP-порты для перехвата (входящие)", TakesValue: true, Group: "WinDivert"),
        new("--wf-raw", "полный сырой WinDivert-фильтр (переопределяет)", TakesValue: true, Group: "WinDivert"),
        new("--wf-filter-lan", "исключать не-глобальные IP: 0|1", TakesValue: true, Group: "WinDivert"),
        new("--wf-save", "записать собранный WinDivert-фильтр в файл", TakesValue: true, Group: "WinDivert"),

        // --- Сеть Windows: активация по сети ---
        new("--ssid-filter", "перехват только в перечисленных Wi-Fi SSID", TakesValue: true, Group: "Сеть (Win)"),
        new("--nlm-filter", "перехват только в перечисленных сетях (NLM GUID)", TakesValue: true, Group: "Сеть (Win)"),
        new("--nlm-list", "вывести GUID подключённых сетей и выйти", Group: "Сеть (Win)"),

        // --- Кэш / conntrack ---
        new("--ipcache-lifetime", "TTL записи IP-кэша, сек (0 = без предела)", TakesValue: true, Group: "Кэш"),
        new("--ipcache-hostname", "кэшировать hostname на IP: 0|1", TakesValue: true, Group: "Кэш"),
        new("--ctrack-timeouts", "таймауты conntrack: SYN:ESTAB:FIN[:UDP]", TakesValue: true, Group: "Кэш"),
        new("--ctrack-disable", "отключить отслеживание соединений: 0|1", TakesValue: true, Group: "Кэш"),
        new("--reasm-disable", "отключить сборку для типов (tls_client_hello…)", TakesValue: true, Group: "Кэш"),
        new("--payload-disable", "отключить определение payload для типов", TakesValue: true, Group: "Кэш"),

        // --- Процесс / отладка ---
        new("--dry-run", "проверить параметры и файлы, затем выйти", Group: "Процесс"),
        new("--debug", "лог: 0 | 1 | syslog | @файл", TakesValue: true, Group: "Процесс"),
        new("--version", "вывести версию и выйти", Group: "Процесс"),
        new("--comment", "игнорируемый текст (для читаемости)", TakesValue: true, Group: "Процесс"),
    };

    // Desync methods for --lua-desync=METHOD:...
    public static readonly WinwsItem[] Methods =
    {
        new("fake", "отправить поддельный пакет (нужен blob)"),
        new("multisplit", "разрезать payload в нескольких позициях (pos)"),
        new("multidisorder", "разрезать и перемешать сегменты (pos)"),
        new("fakedsplit", "split с поддельными пакетами между частями"),
        new("fakeddisorder", "disorder с поддельными пакетами"),
        new("hostfakesplit", "split HTTP с подменой Host"),
        new("tcpseg", "отправка TCP-сегментов (pos = диапазон)"),
        new("oob", "вставка out-of-band байта (char/byte/urp)"),
        new("udplen", "менять длину UDP payload (min/max/increment)"),
        new("syndata", "фейковый payload в SYN"),
        new("rst", "отправить TCP RST"),
        new("synack", "ответ ACK на SYN"),
        new("synack_split", "разбить SYN-ACK (mode)"),
        new("wsize", "менять TCP window на SYN-ACK"),
        new("wssize", "менять TCP window с cutoff"),
        new("tls_client_hello_clone", "клонировать TLS ClientHello в blob"),
        new("http_domcase", "рандомный регистр HTTP Host"),
        new("http_hostcase", "менять написание HTTP Host (spell)"),
        new("http_methodeol", "портить HTTP метод / EOL"),
        new("http_unixeol", "Unix-переводы строк в HTTP"),
        new("pktmod", "модифицировать пакет на месте"),
        new("drop", "дропнуть пакет"),
        new("send", "отправить дубликат (delay)"),
        new("dht_dn", "портить DHT"),
    };

    // Sub-parameters after a method: METHOD:pos=…:seqovl=…
    public static readonly WinwsItem[] Params =
    {
        new("pos", "позиция(и) разреза: 1, midsld, sniext, host, -10", TakesValue: true),
        new("seqovl", "перекрытие seq (число, меньше первой pos)", TakesValue: true),
        new("seqovl_pattern", "blob для заполнения перекрытия", TakesValue: true),
        new("blob", "имя blob", TakesValue: true),
        new("optional", "пропустить, если blob отсутствует"),
        new("nodrop", "не дропать оригинальный пакет"),
        new("badsum", "испортить L4-контрольную сумму (fooling)"),
        new("badseq", "неверный TCP seq (fooling)"),
        new("md5sig", "добавить TCP MD5 signature (fooling)"),
        new("repeats", "сколько раз повторить пакет", TakesValue: true),
        new("delay", "задержка отправки, мс", TakesValue: true),
        new("fool", "функция обмана DPI", TakesValue: true),
        new("strategy", "номер стратегии внутри метода", TakesValue: true),
        new("mode", "syn | synack | acksyn (synack_split)", TakesValue: true),
        new("wsize", "размер TCP-окна", TakesValue: true),
        new("scale", "коэффициент масштаба окна", TakesValue: true),
        new("forced_cutoff", "список payload для cutoff", TakesValue: true),
        new("tls_mod", "модификации TLS: rnd, rndsni, sni=…, dupsid, padencap", TakesValue: true),
        new("rstack", "слать RST,ACK вместо RST"),
        new("host", "шаблон хоста (hostfakesplit)", TakesValue: true),
        new("midhost", "маркер позиции внутри host", TakesValue: true),
        new("disorder_after", "маркер позиции для disorder", TakesValue: true),
        new("nofake1", "пропустить 1-й фейк"),
        new("nofake2", "пропустить 2-й фейк"),
        new("pattern", "blob для заполнения", TakesValue: true),
        new("pattern_offset", "смещение паттерна", TakesValue: true),
        new("char", "символ-байт (oob)", TakesValue: true),
        new("byte", "значение байта 0-255 (oob)", TakesValue: true),
        new("urp", "urgent pointer: b | e | позиция", TakesValue: true),
        new("min", "мин. длина payload (udplen)", TakesValue: true),
        new("max", "макс. длина payload (udplen)", TakesValue: true),
        new("increment", "шаг длины (udplen)", TakesValue: true),
        new("ip_ttl", "TTL для IPv4-фейков", TakesValue: true),
        new("ip6_ttl", "hop limit для IPv6-фейков", TakesValue: true),
        new("dir", "направление: in | out | any", TakesValue: true),
        new("ip_id", "seq | rnd | zero | none", TakesValue: true),
        new("ipfrag", "функция IP-фрагментации (ipfrag2)", TakesValue: true),
    };

    public static readonly WinwsItem[] L7 =
    {
        new("tls", "TLS/HTTPS"), new("http", "HTTP"), new("quic", "QUIC (UDP/443)"),
        new("discord", "Discord"), new("stun", "STUN"), new("dns", "DNS"), new("dtls", "DTLS"),
        new("wireguard", "WireGuard"), new("xmpp", "XMPP"), new("mtproto", "Telegram MTProto"),
        new("bt", "BitTorrent"), new("utp_bt", "uTP BitTorrent"), new("dht", "DHT"),
    };

    public static readonly WinwsItem[] Payloads =
    {
        new("tls_client_hello", "TLS ClientHello"), new("tls_server_hello", "TLS ServerHello"),
        new("http_req", "HTTP-запрос"), new("http_reply", "HTTP-ответ"),
        new("quic_initial", "QUIC Initial"), new("discord_ip_discovery", "Discord IP discovery"),
        new("stun", "STUN"), new("dht", "DHT"), new("dns_query", "DNS-запрос"), new("dns_response", "DNS-ответ"),
        new("dtls_client_hello", "DTLS ClientHello"), new("wireguard_initiation", "WireGuard init"),
        new("mtproto_initial", "MTProto initial"), new("bt_handshake", "BitTorrent handshake"),
        new("unknown", "нераспознанное"), new("all", "любой payload"),
    };

    public static readonly WinwsItem[] PosMarkers =
    {
        new("midsld", "середина домена второго уровня"), new("sniext", "позиция TLS SNI"),
        new("host", "начало HTTP Host"), new("endhost", "конец HTTP Host"), new("method", "начало HTTP-метода"),
    };

    public static readonly WinwsItem[] TlsMods =
    {
        new("rnd", "рандомизировать ClientHello"), new("rndsni", "рандомизировать SNI"),
        new("dupsid", "дублировать session id"), new("padencap", "паддинг/инкапсуляция"),
    };

    public static readonly WinwsItem[] IpId = { new("seq", ""), new("rnd", ""), new("zero", ""), new("none", "") };
    public static readonly WinwsItem[] SynackModes = { new("syn", ""), new("synack", ""), new("acksyn", "") };
    public static readonly WinwsItem[] Dir = { new("in", ""), new("out", ""), new("any", "") };

    public static WinwsItem[]? ValuesForParam(string p) => p switch
    {
        "pos" or "midhost" or "urp" or "disorder_after" => PosMarkers,
        "ip_id" => IpId,
        "mode" => SynackModes,
        "dir" => Dir,
        "tls_mod" => TlsMods,
        _ => null,
    };

    public static WinwsItem? FindFlag(string name) =>
        Array.Find(Flags, f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase));

    public static WinwsItem? FindMethod(string name) =>
        Array.Find(Methods, m => string.Equals(m.Name, name, StringComparison.OrdinalIgnoreCase));
}
