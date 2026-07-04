namespace Zapret.Core;

public static class UserLists
{
    public static string ReadText(string path) =>
        File.Exists(path) ? File.ReadAllText(path) : "";

    public static void SaveText(string path, string? text)
    {
        var lines = (text ?? "")
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0)
            .ToArray();

        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllLines(path, lines);
    }
}
