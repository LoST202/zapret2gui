using System.Net;

namespace Zapret.Core;

public enum ListIssueLevel { Warning, Error }

public sealed record ListIssue(ListIssueLevel Level, string Text);

public static class ListsManager
{
    private static readonly string[] RequiredLists =
    {
        "list-general.txt", "list-general-user.txt", "list-google.txt",
        "list-exclude.txt", "list-exclude-user.txt",
        "ipset-all.txt", "ipset-exclude.txt", "ipset-exclude-user.txt",
    };

    public static bool IsIpsetFile(string fileName) =>
        fileName.StartsWith("ipset", StringComparison.OrdinalIgnoreCase);

    public static bool IsPlausibleDomain(string s)
    {
        s = s.Trim();
        if (s.Length == 0)
            return true;
        if (!s.Contains('.'))
            return false;
        foreach (var c in s)
            if (!(char.IsLetterOrDigit(c) || c is '.' or '-' or '_' or '*'))
                return false;
        return true;
    }

    public static int CountBadLines(AppPaths paths, string fileName)
    {
        var path = paths.List(fileName);
        if (!File.Exists(path))
            return 0;
        var ipset = IsIpsetFile(fileName);
        var bad = 0;
        try
        {
            foreach (var raw in File.ReadLines(path))
            {
                var l = raw.Trim();
                if (l.Length == 0 || l.StartsWith('#'))
                    continue;
                var ok = ipset ? IsValidIpLine(l) : IsPlausibleDomain(l);
                if (!ok)
                    bad++;
            }
        }
        catch { }
        return bad;
    }

    public static IReadOnlyList<string> Enumerate(AppPaths paths)
    {
        if (!Directory.Exists(paths.ListsDir))
            return Array.Empty<string>();
        return Directory.EnumerateFiles(paths.ListsDir, "*.txt")
            .Select(f => Path.GetFileName(f)!)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static int CountEntries(string path)
    {
        if (!File.Exists(path))
            return 0;
        var n = 0;
        foreach (var raw in File.ReadLines(path))
        {
            var l = raw.Trim();
            if (l.Length > 0 && !l.StartsWith('#'))
                n++;
        }
        return n;
    }

    public static bool IsValidIpLine(string s)
    {
        s = s.Trim();
        if (s.Length == 0)
            return false;
        var slash = s.IndexOf('/');
        var addr = slash >= 0 ? s[..slash] : s;
        if (!IPAddress.TryParse(addr, out _))
            return false;
        if (slash >= 0 && (!int.TryParse(s[(slash + 1)..], out var mask) || mask < 0 || mask > 128))
            return false;
        return true;
    }

    public static IReadOnlyList<ListIssue> Validate(AppPaths paths)
    {
        var issues = new List<ListIssue>();

        if (!File.Exists(paths.WinwsExe))
            issues.Add(new ListIssue(ListIssueLevel.Error, "Не найден bin\\winws2.exe — обход не запустится."));

        foreach (var f in RequiredLists)
            if (!File.Exists(paths.List(f)))
                issues.Add(new ListIssue(ListIssueLevel.Error,
                    $"Отсутствует список «{f}» — обход может не запуститься."));

        return issues;
    }
}
