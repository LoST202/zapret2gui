using System.Text;

namespace Zapret.Core;

public static class CommandParser
{
    public static IReadOnlyList<string> ForStrategy(AppPaths paths, Strategy s) =>
        Parse(paths, string.IsNullOrWhiteSpace(s.Command) ? CommandBuilder.ToText(s) : s.Command);

    public static IReadOnlyList<string> Parse(AppPaths paths, string? command)
    {
        var joined = string.Join(" ", (command ?? "")
            .Replace("\r\n", "\n").Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Select(l => l.EndsWith('^') ? l[..^1].TrimEnd() : l)
            .Where(l => l.Length > 0));

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
