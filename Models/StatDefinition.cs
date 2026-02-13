using System.Collections.Immutable;

namespace PoE2Overlay.Models;

public sealed record StatPattern
{
    public string Regex { get; init; } = string.Empty;
    public string Type { get; init; } = "flat";
}

public sealed record StatCategoryDefinition
{
    public string DisplayName { get; init; } = string.Empty;
    public ImmutableList<StatPattern> Patterns { get; init; } = ImmutableList<StatPattern>.Empty;
}

public sealed record StatDefinitions
{
    public string Version { get; init; } = "1.0.0";
    public string LastUpdated { get; init; } = string.Empty;
    public ImmutableDictionary<string, StatCategoryDefinition> Categories { get; init; }
        = ImmutableDictionary<string, StatCategoryDefinition>.Empty;
}
