using System.Text;
using System.Text.Json;
using System.Threading;

namespace Zapret.Core;

/// <summary>Shared robust read/backup helpers so a locked or corrupt state file never silently
/// costs the user their data (a plain <c>catch { return default; }</c> would let the next Save
/// overwrite an intact-but-temporarily-unreadable file).</summary>
internal static class StateIo
{
    /// <summary>Reads an existing file, retrying a few times through transient locks (AV / cloud-sync /
    /// a second instance). Returns null only if the file stays unreadable — the caller must then NOT
    /// overwrite it.</summary>
    public static string? ReadExisting(string path)
    {
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try { return File.ReadAllText(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (attempt < 2)
                Thread.Sleep(120);
        }
        return null;
    }

    /// <summary>Best-effort: preserve a corrupt/unparseable file next to itself before we replace it.</summary>
    public static void BackupCorrupt(string path)
    {
        try { File.Copy(path, path + ".corrupt", overwrite: true); }
        catch { }
    }
}

public sealed class AppState
{
    public AppConfig Config { get; }
    public string StateFile { get; }

    /// <summary>True when an existing state file could not be read (locked). Save() refuses to run so the
    /// real file — with the user's profiles/notes/settings — is not overwritten by defaults.</summary>
    public bool LoadFailed { get; }

    private AppState(AppConfig config, string stateFile, bool loadFailed)
    {
        Config = config;
        StateFile = stateFile;
        LoadFailed = loadFailed;
    }

    public static AppState Load(string stateFile)
    {
        if (!File.Exists(stateFile))
            return new AppState(new AppConfig(), stateFile, loadFailed: false);

        var text = StateIo.ReadExisting(stateFile);
        if (text is null)
            // File exists but is locked — fall back to defaults for this session but block Save so we
            // don't destroy the user's real config.
            return new AppState(new AppConfig(), stateFile, loadFailed: true);

        try
        {
            var cfg = JsonSerializer.Deserialize(text, ZapretJson.Default.AppConfig) ?? new AppConfig();
            // STJ applies field initializers only when a JSON key is ABSENT; an explicit "profiles": null
            // (hand-edited / partially-written file) yields null. Coalesce so the UI never NREs on startup.
            cfg.Profiles ??= new();
            cfg.DashboardCards ??= new() { "lists", "actions", "profiles" };
            cfg.ListNotes ??= new();
            return new AppState(cfg, stateFile, loadFailed: false);
        }
        catch
        {
            // Corrupt/unparseable: keep the bad file aside, then it is safe to start (and later save) fresh.
            StateIo.BackupCorrupt(stateFile);
            return new AppState(new AppConfig(), stateFile, loadFailed: false);
        }
    }

    public void Save()
    {
        if (LoadFailed)
            return;

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

    /// <summary>True when an existing strategies file could not be read (locked). Callers must not
    /// Save over it, or every saved strategy would be lost.</summary>
    public bool LoadFailed { get; }

    private readonly Dictionary<string, Strategy> _byId;

    private StrategyCatalog(IReadOnlyList<Strategy> strategies, bool loadFailed = false)
    {
        LoadFailed = loadFailed;
        // Id is `required` but STJ does not enforce non-null at runtime; a hand-edited "id": null would
        // otherwise throw on the Dictionary indexer. Drop entries without a usable Id instead of crashing.
        var valid = strategies.Where(s => !string.IsNullOrEmpty(s.Id)).ToList();
        Strategies = valid;
        _byId = new Dictionary<string, Strategy>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in valid)
            _byId[s.Id] = s;
    }

    public static StrategyCatalog Load(string strategiesFile)
    {
        if (!File.Exists(strategiesFile))
            return new StrategyCatalog(new List<Strategy>());

        var text = StateIo.ReadExisting(strategiesFile);
        if (text is null)
            return new StrategyCatalog(new List<Strategy>(), loadFailed: true);

        try
        {
            var list = JsonSerializer.Deserialize(text, ZapretJson.Default.ListStrategy) ?? new List<Strategy>();
            return new StrategyCatalog(list);
        }
        catch
        {
            // One malformed entry (e.g. a hand-edit that drops a required "label") would otherwise
            // discard the entire catalog. Preserve the file, then recover element-by-element.
            StateIo.BackupCorrupt(strategiesFile);
            return new StrategyCatalog(RecoverElements(text));
        }
    }

    private static List<Strategy> RecoverElements(string json)
    {
        var result = new List<Strategy>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return result;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                try
                {
                    var s = el.Deserialize(ZapretJson.Default.Strategy);
                    if (s is not null && !string.IsNullOrEmpty(s.Id))
                        result.Add(s);
                }
                catch { }   // skip only the bad entries, keep the rest
            }
        }
        catch { }           // not even valid JSON syntax → nothing to recover
        return result;
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
