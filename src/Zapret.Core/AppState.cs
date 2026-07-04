using System.Text;
using System.Text.Json;

namespace Zapret.Core;

public sealed class AppState
{
    public AppConfig Config { get; }
    public string StateFile { get; }

    private AppState(AppConfig config, string stateFile)
    {
        Config = config;
        StateFile = stateFile;
    }

    public static AppState Load(string stateFile)
    {
        AppConfig? config = null;
        try
        {
            if (File.Exists(stateFile))
                config = JsonSerializer.Deserialize(File.ReadAllText(stateFile), ZapretJson.Default.AppConfig);
        }
        catch { }

        return new AppState(config ?? new AppConfig(), stateFile);
    }

    public void Save()
    {
        var json = JsonSerializer.Serialize(Config, ZapretJson.Default.AppConfig);
        var dir = Path.GetDirectoryName(StateFile);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = StateFile + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, StateFile, overwrite: true);
    }
}

public sealed class StrategyCatalog
{
    public IReadOnlyList<Strategy> Strategies { get; }

    private readonly Dictionary<string, Strategy> _byId;

    private StrategyCatalog(IReadOnlyList<Strategy> strategies)
    {
        Strategies = strategies;
        _byId = new Dictionary<string, Strategy>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in strategies)
            _byId[s.Id] = s;
    }

    public static StrategyCatalog Load(string strategiesFile)
    {
        List<Strategy>? list = null;
        try
        {
            if (File.Exists(strategiesFile))
                list = JsonSerializer.Deserialize(File.ReadAllText(strategiesFile), ZapretJson.Default.ListStrategy);
        }
        catch { }

        return new StrategyCatalog(list ?? new List<Strategy>());
    }

    public Strategy? ById(string? id) =>
        id is not null && _byId.TryGetValue(id, out var s) ? s : null;

    public static void Save(string strategiesFile, IReadOnlyList<Strategy> strategies)
    {
        var json = JsonSerializer.Serialize(strategies.ToList(), ZapretJson.Default.ListStrategy);
        var dir = Path.GetDirectoryName(strategiesFile);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tmp = strategiesFile + ".tmp";
        File.WriteAllText(tmp, json, new UTF8Encoding(false));
        File.Move(tmp, strategiesFile, overwrite: true);
    }
}
