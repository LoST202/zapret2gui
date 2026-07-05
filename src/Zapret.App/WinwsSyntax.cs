using System.Collections.Frozen;

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
        new("--new", "начать новый профиль (разделитель стратегий); опц. имя", Group: "Профиль"),
        new("--name", "задать имя профиля", TakesValue: true, Group: "Профиль"),
        new("--skip", "игнорировать (отключить) профиль", Group: "Профиль"),
        new("--template", "использовать профиль как шаблон многоразовых параметров/фильтров", Group: "Профиль"),
        new("--import", "скопировать настройки шаблона в текущий профиль, перезаписав всё", TakesValue: true, Group: "Профиль"),
        new("--cookie", "задать Lua-переменную desync.cookie для инстансов профиля", TakesValue: true, Group: "Профиль"),

        // --- Фильтры профиля L3/L4 ---
        new("--filter-l3", "фильтр по версии IP: ipv4 | ipv6", TakesValue: true, Group: "Фильтры"),
        new("--filter-tcp", "TCP-порты/диапазоны; ~ отрицание, * любой", TakesValue: true, Group: "Фильтры"),
        new("--filter-udp", "UDP-порты/диапазоны; ~ отрицание, * любой", TakesValue: true, Group: "Фильтры"),
        new("--filter-icmp", "ICMP тип[:код] через запятую", TakesValue: true, Group: "Фильтры"),
        new("--filter-ipp", "сырые номера IP-протоколов через запятую", TakesValue: true, Group: "Фильтры"),
        new("--filter-l7", "список протоколов уровня приложения (http, tls, quic…)", TakesValue: true, Group: "Фильтры"),
        new("--payload", "тип payload для инстансов профиля; спец. all, known", TakesValue: true, Group: "Фильтры"),
        new("--out-range", "диапазон conntrack-счётчика, исходящее направление", TakesValue: true, Group: "Фильтры"),
        new("--in-range", "диапазон conntrack-счётчика, входящее направление", TakesValue: true, Group: "Фильтры"),

        // --- Инициализация desync ---
        new("--lua-init", "выполнить Lua из строки/файла один раз при старте (@файл)", TakesValue: true, Group: "Инициализация"),
        new("--blob", "загрузить бинарный файл/hex в Lua-переменную: имя:@файл | 0xHEX", TakesValue: true, Group: "Инициализация"),
        new("--lua-desync", "вызвать Lua-стратегию десинка: функция[:пар=знач]", TakesValue: true, Group: "Инициализация"),

        // --- Списки ---
        new("--hostlist", "include-список доменов из файла (субдомены авто, gzip)", TakesValue: true, Group: "Списки"),
        new("--hostlist-domains", "фиксированный include-список доменов в аргументе", TakesValue: true, Group: "Списки"),
        new("--hostlist-exclude", "exclude-список доменов из файла", TakesValue: true, Group: "Списки"),
        new("--hostlist-exclude-domains", "фиксированный exclude-список доменов", TakesValue: true, Group: "Списки"),
        new("--hostlist-auto", "авто-пополняемый include-фильтр по обратной связи", TakesValue: true, Group: "Списки"),
        new("--ipset", "include-список IP/подсетей из файла (IPv4/IPv6)", TakesValue: true, Group: "Списки"),
        new("--ipset-ip", "фиксированный include-список IP/подсетей", TakesValue: true, Group: "Списки"),
        new("--ipset-exclude", "exclude-список IP/подсетей из файла", TakesValue: true, Group: "Списки"),
        new("--ipset-exclude-ip", "фиксированный exclude-список IP/подсетей", TakesValue: true, Group: "Списки"),

        // --- Авто-хостлист (обучение) ---
        new("--hostlist-auto-fail-threshold", "число неудач подряд до добавления в список (3)", TakesValue: true, Group: "Авто-хостлист"),
        new("--hostlist-auto-fail-time", "макс. время между неудачами без сброса счётчика, с (60)", TakesValue: true, Group: "Авто-хостлист"),
        new("--hostlist-auto-retrans-threshold", "число TCP-ретрансмиссий в сессии для неудачи (3)", TakesValue: true, Group: "Авто-хостлист"),
        new("--hostlist-auto-retrans-reset", "слать RST ретрансмиттеру для прерывания ожиданий (1)", TakesValue: true, Group: "Авто-хостлист"),
        new("--hostlist-auto-debug", "файл лога авто-хостлиста", TakesValue: true, Group: "Авто-хостлист"),

        // --- WinDivert (Windows) ---
        new("--wf-iface", "номер сетевого интерфейса для WinDivert", TakesValue: true, Group: "WinDivert"),
        new("--wf-l3", "версия IP фильтра: ipv4 | ipv6", TakesValue: true, Group: "WinDivert"),
        new("--wf-tcp-out", "TCP-порты для перехвата в исходящем направлении", TakesValue: true, Group: "WinDivert"),
        new("--wf-tcp-in", "TCP-порты для перехвата во входящем направлении", TakesValue: true, Group: "WinDivert"),
        new("--wf-udp-out", "UDP-порты для перехвата в исходящем направлении", TakesValue: true, Group: "WinDivert"),
        new("--wf-udp-in", "UDP-порты для перехвата во входящем направлении", TakesValue: true, Group: "WinDivert"),
        new("--wf-raw", "полный сырой WinDivert-фильтр (переопределяет конструктор)", TakesValue: true, Group: "WinDivert"),
        new("--wf-filter-lan", "отфильтровывать не-глобальные IP: 0|1 (по умолч. да)", TakesValue: true, Group: "WinDivert"),
        new("--wf-save", "сохранить итоговый WinDivert-фильтр в файл", TakesValue: true, Group: "WinDivert"),

        // --- Сеть Windows: активация по сети ---
        new("--ssid-filter", "перехват только при подключении к этим Wi-Fi сетям", TakesValue: true, Group: "Сеть (Win)"),
        new("--nlm-filter", "перехват только при подключении к этим сетям NLM", TakesValue: true, Group: "Сеть (Win)"),
        new("--nlm-list", "показать список подключённых NLM-сетей (=all — все)", Group: "Сеть (Win)"),

        // --- Кэш / conntrack ---
        new("--ipcache-lifetime", "время жизни записи IP-кэша, сек (0 = без предела)", TakesValue: true, Group: "Кэш"),
        new("--ipcache-hostname", "кэшировать hostname для zero-phase стратегий: 0|1", TakesValue: true, Group: "Кэш"),
        new("--ctrack-timeouts", "таймауты conntrack TCP и UDP: SYN:ESTAB:FIN[:UDP]", TakesValue: true, Group: "Кэш"),
        new("--ctrack-disable", "отключить conntrack: 0|1", TakesValue: true, Group: "Кэш"),
        new("--reasm-disable", "отключить сборку фрагментов для payload (tls_client_hello…)", TakesValue: true, Group: "Кэш"),
        new("--payload-disable", "не определять указанные типы payload (без арг. — все)", TakesValue: true, Group: "Кэш"),

        // --- Процесс / отладка ---
        new("--dry-run", "проверить параметры и наличие файлов и выйти", Group: "Процесс"),
        new("--debug", "писать debug-лог: 0 | 1 | syslog | android | @файл", TakesValue: true, Group: "Процесс"),
        new("--version", "показать версию и выйти", Group: "Процесс"),
        new("--comment", "любой текст; игнорируется", TakesValue: true, Group: "Процесс"),
    };

    // Desync methods for --lua-desync=METHOD:...
    public static readonly WinwsItem[] Methods =
    {
        new("fake", "отправить поддельный payload-пакет (по умолч. исходящий known)"),
        new("multisplit", "разрезать payload на несколько TCP-сегментов в позициях (pos)"),
        new("multidisorder", "разрезать payload на сегменты и отправить в обратном порядке"),
        new("fakedsplit", "разрезать и переслать реальные части, чередуя с поддельными"),
        new("fakeddisorder", "разрезать, отправить части disorder'ом, чередуя с поддельными"),
        new("hostfakesplit", "резать вокруг host: слать поддельные и реальные host-сегменты"),
        new("tcpseg", "отправить TCP-сегмент, покрывающий диапазон payload (pos = 2 маркера)"),
        new("oob", "отправить out-of-band (URG) байт в позиции-маркере"),
        new("udplen", "изменить длину UDP-payload (min/max/increment)"),
        new("syndata", "отправить фейковый payload в SYN-пакете (в один пакет)"),
        new("rst", "отправить RST (или RST,ACK) пакет"),
        new("synack", "отправить SYN+ACK в ответ"),
        new("synack_split", "разбить/переупорядочить SYN+ACK на SYN и ACK (mode)"),
        new("wsize", "переписать размер TCP-окна (на SYN+ACK)"),
        new("wssize", "переписать размер TCP-окна с cutoff на payload"),
        new("tls_client_hello_clone", "клонировать и модифицировать TLS ClientHello в blob"),
        new("http_domcase", "чередующийся регистр host в HTTP-запросе (upper/lower)"),
        new("http_hostcase", "менять написание HTTP-заголовка Host (spell)"),
        new("http_methodeol", "вставить конец строки ПЕРЕД HTTP-методом"),
        new("http_unixeol", "преобразовать HTTP-переводы строк в unix EOL"),
        new("pktmod", "применить модификацию к текущему пакету"),
        new("drop", "дропнуть пакет"),
        new("send", "дропнуть пакет и отправить его копию (delay — с задержкой)"),
        new("dht_dn", "тамперинг DHT-сообщения, чтобы оно начиналось с dN (dn)"),
    };

    // Sub-parameters after a method: METHOD:pos=…:seqovl=…
    public static readonly WinwsItem[] Params =
    {
        new("pos", "позиции разреза payload: 1, host, midsld+1, -10", TakesValue: true),
        new("seqovl", "уменьшить seq первого сегмента на N и заполнить N байт", TakesValue: true),
        new("seqovl_pattern", "паттерн заполнения для seqovl", TakesValue: true),
        new("blob", "данные вместо стандартного payload (0xHEX / имя / глоб. var)", TakesValue: true),
        new("optional", "пропустить, если blob отсутствует"),
        new("nodrop", "не дропать текущий dissect (оригинальный разбор)"),
        new("badsum", "сделать L4-контрольную сумму невалидной (fooling)"),
        new("tcp_seq", "прибавить N к tcp.th_seq (fooling)", TakesValue: true),
        new("tcp_md5", "добавить TCP MD5-заголовок с опц. 16-байт данными (fooling)", TakesValue: true),
        new("repeats", "сколько раз отправить пакет (rawsend)", TakesValue: true),
        new("delay", "дроп и отправка с задержкой, мс (метод send)", TakesValue: true),
        new("fool", "кастомная функция обмана DPI (глобальная функция)", TakesValue: true),
        new("mode", "режим synack_split: syn | synack | acksyn", TakesValue: true),
        new("wsize", "размер TCP-окна (wsize/wssize)", TakesValue: true),
        new("scale", "коэффициент масштаба TCP-опции окна", TakesValue: true),
        new("forced_cutoff", "payload, вызывающие принудительный wssize-cutoff", TakesValue: true),
        new("tls_mod", "модификации TLS: rnd, rndsni, sni=…, dupsid, padencap", TakesValue: true),
        new("rstack", "слать RST,ACK вместо RST (метод rst)"),
        new("host", "шаблон hostname: генерирует random.template (hostfakesplit)", TakesValue: true),
        new("midhost", "доп. разрез сегмента с host в маркере (host+1..endhost-1)", TakesValue: true),
        new("disorder_after", "слать часть after_host двумя disorder-сегментами", TakesValue: true),
        new("nofake1", "не слать отдельный фейк №1"),
        new("nofake2", "не слать отдельный фейк №2"),
        new("nofake3", "не слать отдельный фейк №3 (fakedsplit/fakeddisorder)"),
        new("nofake4", "не слать отдельный фейк №4 (fakedsplit/fakeddisorder)"),
        new("pattern", "паттерн для поддельных частей / добавленных байт (udplen)", TakesValue: true),
        new("pattern_offset", "смещение в паттерне (udplen, по умолч. 0)", TakesValue: true),
        new("char", "OOB-символ (oob)", TakesValue: true),
        new("byte", "OOB-байт (oob)", TakesValue: true),
        new("urp", "маркер позиции urgent pointer: b | e (по умолч. 0)", TakesValue: true),
        new("min", "не действовать на payload меньше N байт (udplen)", TakesValue: true),
        new("max", "не действовать на payload больше N байт (udplen)", TakesValue: true),
        new("increment", "шаг изменения длины UDP: <0 сжимает, >0 растит (2)", TakesValue: true),
        new("ip_ttl", "установить IPv4 TTL в N (fooling)", TakesValue: true),
        new("ip6_ttl", "установить IPv6 hop limit в N (fooling)", TakesValue: true),
        new("dir", "направление пакета: in | out | any", TakesValue: true),
        new("ip_id", "политика IPv4 ip_id: seq | rnd | zero | none", TakesValue: true),
        new("ipfrag", "функция IP-фрагментации (по умолч. ipfrag2)", TakesValue: true),
        new("spell", "написание заголовка Host, ровно 4 символа (http_hostcase)", TakesValue: true),
        new("dn", "сколько байт dN в начале DHT-сообщения (dht_dn, по умолч. 3)", TakesValue: true),
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
        new("host", "начало host"), new("endhost", "конец host"), new("method", "начало HTTP-метода"),
    };

    public static readonly WinwsItem[] TlsMods =
    {
        new("rnd", "рандомизировать ClientHello"), new("rndsni", "рандомизировать SNI"),
        new("dupsid", "дублировать session id (fake)"), new("padencap", "паддинг/инкапсуляция (fake)"),
    };

    public static readonly WinwsItem[] IpId = { new("seq", ""), new("rnd", ""), new("zero", ""), new("none", "") };
    public static readonly WinwsItem[] SynackModes = { new("syn", ""), new("synack", ""), new("acksyn", "") };
    public static readonly WinwsItem[] Dir = { new("in", ""), new("out", ""), new("any", "") };
    public static readonly WinwsItem[] UrpMarkers = { new("b", "перед payload"), new("e", "после payload") };

    public static WinwsItem[]? ValuesForParam(string p) => p switch
    {
        "pos" or "midhost" => PosMarkers,
        "urp" => UrpMarkers,                 // urgent-pointer marker: b | e (не позиции домена)
        // disorder_after — числовой счётчик сегментов, свободное значение (без списка подсказок)
        "ip_id" => IpId,
        "mode" => SynackModes,
        "dir" => Dir,
        "tls_mod" => TlsMods,
        _ => null,
    };

    // Prebuilt O(1) indexes — the editor's autocomplete + hover hit these on every keystroke,
    // so an Array.Find scan per lookup is wasteful. FrozenDictionary is optimized for read-heavy,
    // build-once maps (.NET 8+).
    private static readonly FrozenDictionary<string, WinwsItem> FlagByName =
        Flags.ToFrozenDictionary(f => f.Name, StringComparer.OrdinalIgnoreCase);

    private static readonly FrozenDictionary<string, WinwsItem> MethodByName =
        Methods.ToFrozenDictionary(m => m.Name, StringComparer.OrdinalIgnoreCase);

    // Any named token (flags, methods, params, values) for hover lookup. First writer wins, so a
    // name shared by a param and a value resolves to the param (added first).
    private static readonly FrozenDictionary<string, WinwsItem> AnyByName = BuildAnyIndex();

    public static WinwsItem? FindFlag(string name) => FlagByName.GetValueOrDefault(name);
    public static WinwsItem? FindMethod(string name) => MethodByName.GetValueOrDefault(name);
    public static WinwsItem? Lookup(string token) => AnyByName.GetValueOrDefault(token);

    private static FrozenDictionary<string, WinwsItem> BuildAnyIndex()
    {
        var d = new Dictionary<string, WinwsItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var arr in new[] { Flags, Methods, Params, L7, Payloads, PosMarkers, TlsMods })
            foreach (var it in arr)
                d.TryAdd(it.Name, it);
        return d.ToFrozenDictionary(StringComparer.OrdinalIgnoreCase);
    }
}
