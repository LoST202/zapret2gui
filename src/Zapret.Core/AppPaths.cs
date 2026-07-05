namespace Zapret.Core;

public sealed class AppPaths
{
    public string Root { get; }
    public string BinDir { get; }
    public string ListsDir { get; }
    public string StateDir { get; }
    public string StateFile { get; }
    public string StrategiesFile { get; }
    public string WinwsExe { get; }

    public AppPaths(string root)
    {
        Root = Path.GetFullPath(root);
        BinDir = Path.Combine(Root, "bin");
        ListsDir = Path.Combine(Root, "lists");
        StateDir = Path.Combine(Root, "state");
        StateFile = Path.Combine(StateDir, "state.json");
        StrategiesFile = Path.Combine(StateDir, "strategies.json");
        WinwsExe = Path.Combine(BinDir, "winws2.exe");
    }

    public string List(string file) => Path.Combine(ListsDir, file);

    public static AppPaths Discover()
    {
        var root = Environment.GetEnvironmentVariable("ZAPRET_ROOT");
        if (string.IsNullOrWhiteSpace(root))
            root = AppContext.BaseDirectory;

        var paths = new AppPaths(root);
        Directory.CreateDirectory(paths.StateDir);
        paths.SeedDefaults();
        return paths;
    }

    public void SeedDefaults()
    {
        Seed("strategies.json", StrategiesFile);
        Seed("state.json", StateFile);

        static void Seed(string name, string dest)
        {
            if (File.Exists(dest))
                return;
            var src = Path.Combine(AppContext.BaseDirectory, "defaults", name);
            if (!File.Exists(src))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest);
        }
    }
}
