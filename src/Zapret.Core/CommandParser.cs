using System.Text;

namespace Zapret.Core;

public static class CommandParser
{
    public static IReadOnlyList<string> ForStrategy(AppPaths paths, Strategy s) =>
        Parse(paths, s.Command is { Length: > 0 }
            ? string.Join("\n", s.Command)          // strategy is a full command (array of lines), like a .bat
            : CommandBuilder.ToText(s));             // legacy fallback (old Dg/Dgen strategies not yet migrated)

    public static IReadOnlyList<string> Parse(AppPaths paths, string? command)
    {
        var lines = (command ?? "")
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            // The editor greys out '#'/'::' lines as comments; drop them so they aren't passed to winws2.
            .Where(l => l.Length > 0 && !l.StartsWith('#') && !l.StartsWith("::"))
            .ToList();

        // Assemble the command honoring '^' line-continuation (cmd.exe semantics): a line ending in '^'
        // joins to the next with NO inserted separator, so a token split mid-way across a caret is kept
        // whole (e.g. "...quic_google:^" + "repeats=6" → "...quic_google:repeats=6").
        var sb = new StringBuilder();
        for (var k = 0; k < lines.Count; k++)
        {
            var l = lines[k];
            if (l.EndsWith('^'))
            {
                sb.Append(l, 0, l.Length - 1);
            }
            else
            {
                sb.Append(l);
                if (k < lines.Count - 1)
                    sb.Append(' ');
            }
        }
        var joined = sb.ToString();

        // An odd number of quotes means an unterminated "…" that would swallow subsequent arguments into
        // one giant token and silently corrupt the whole arg list. Fail loudly so the real cause is clear.
        if (joined.Count(c => c == '"') % 2 != 0)
            throw new InvalidOperationException(
                "В команде непарная кавычка (\") — проверьте пути в кавычках.");

        var tokens = Tokenize(joined);

        var start = 0;
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i].EndsWith("winws2.exe", StringComparison.OrdinalIgnoreCase) ||
                tokens[i].EndsWith("winws.exe", StringComparison.OrdinalIgnoreCase))
            {
                start = i + 1;
                break;
            }
        }

        var root = paths.Root.EndsWith(Path.DirectorySeparatorChar)
            ? paths.Root
            : paths.Root + Path.DirectorySeparatorChar;

        var args = new List<string>(tokens.Count);
        for (var i = start; i < tokens.Count; i++)
            args.Add(ReplaceCI(tokens[i], "%~dp0", root));

        // A non-empty command that yields no args (e.g. winws2.exe is the last token) would spawn
        // the engine with no filters — it exits instantly and is misreported as a crash, kicking off
        // the retry loop. Fail loudly instead so the real cause is visible.
        if (args.Count == 0 && joined.Length > 0)
            throw new InvalidOperationException(
                "Стратегия не содержит аргументов после winws — команда пуста или задан только путь к winws2.exe.");
        return args;
    }

    private static List<string> Tokenize(string s)
    {
        var tokens = new List<string>();
        var sb = new StringBuilder();
        var inQuotes = false;
        var has = false;

        foreach (var c in s)
        {
            if (c == '"')
            {
                inQuotes = !inQuotes;
                has = true;
            }
            else if (!inQuotes && char.IsWhiteSpace(c))
            {
                if (has)
                {
                    tokens.Add(sb.ToString());
                    sb.Clear();
                    has = false;
                }
            }
            else
            {
                sb.Append(c);
                has = true;
            }
        }
        if (has)
            tokens.Add(sb.ToString());
        return tokens;
    }

    private static string ReplaceCI(string input, string search, string replace)
    {
        var idx = input.IndexOf(search, StringComparison.OrdinalIgnoreCase);
        if (idx < 0)
            return input;

        var sb = new StringBuilder();
        var pos = 0;
        while (idx >= 0)
        {
            sb.Append(input, pos, idx - pos).Append(replace);
            pos = idx + search.Length;
            idx = input.IndexOf(search, pos, StringComparison.OrdinalIgnoreCase);
        }
        sb.Append(input, pos, input.Length - pos);
        return sb.ToString();
    }
}
