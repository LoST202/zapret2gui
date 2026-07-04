using System.Text.Json.Serialization;

namespace Zapret.Core;

public sealed record Strategy
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public string Group { get; init; } = "";
    public string Description { get; init; } = "";
    public string Command { get; init; } = "";

    public string[] Dg { get; init; } = Array.Empty<string>();
    public string[] Dgen { get; init; } = Array.Empty<string>();
    public bool AutoHostlist { get; init; }
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
