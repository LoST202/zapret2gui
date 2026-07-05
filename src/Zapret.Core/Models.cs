using System.Text.Json;
using System.Text.Json.Serialization;

namespace Zapret.Core;

public sealed record Strategy
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string? Description { get; init; } = "";

    /// <summary>Полная команда winws2 как массив строк (как в .bat-файле). Единственный источник истины.
    /// Nullable: source-gen оставляет null, если ключа "command" нет в JSON (старые / импортированные файлы).</summary>
    [JsonConverter(typeof(StrategyCommandConverter))]
    public string[]? Command { get; init; } = Array.Empty<string>();

    // Устаревшее: только для чтения старых файлов и разовой миграции в Command. В новом формате не пишутся.
    public string[]? Dg { get; init; }
    public string[]? Dgen { get; init; }
}

/// <summary>Читает Command и как массив строк (новый формат), и как одну строку (старая запечённая команда).</summary>
public sealed class StrategyCommandConverter : JsonConverter<string[]>
{
    public override string[] Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString() ?? "";
            return s.Length == 0 ? Array.Empty<string>() : s.Replace("\r\n", "\n").Split('\n');
        }

        if (reader.TokenType == JsonTokenType.StartArray)
        {
            var lines = new List<string>();
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                lines.Add(reader.GetString() ?? "");
            return lines.ToArray();
        }

        return Array.Empty<string>();
    }

    public override void Write(Utf8JsonWriter writer, string[] value, JsonSerializerOptions options)
    {
        writer.WriteStartArray();
        foreach (var line in value)
            writer.WriteStringValue(line);
        writer.WriteEndArray();
    }
}

public sealed class Profile
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string StrategyId { get; set; } = "";
}

public sealed class AppConfig
{
    public string Theme { get; set; } = "dark";
    public string? CurrentStrategyId { get; set; }
    public bool StartMinimized { get; set; }
    public bool AutoConnect { get; set; }
    public bool MaskInfo { get; set; }

    public List<Profile> Profiles { get; set; } = new();

    public List<string> DashboardCards { get; set; } = new() { "lists", "actions", "profiles" };

    public string AccentColor { get; set; } = "system";

    public string? FontFamily { get; set; }

    public double FontSize { get; set; }

    public Dictionary<string, string> ListNotes { get; set; } = new();

    /// <summary>Which IP families "Собрать из ASN" writes into ipset-all.txt: "both" | "v4" | "v6".</summary>
    public string IpsetFamily { get; set; } = "both";
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(AppConfig))]
[JsonSerializable(typeof(List<Strategy>))]
[JsonSerializable(typeof(Strategy))]
[JsonSerializable(typeof(Profile))]
public partial class ZapretJson : JsonSerializerContext
{
}
