using System.Collections.Immutable;

namespace PoE2Overlay.Models;

public enum ItemRarity
{
    Normal,
    Magic,
    Rare,
    Unique,
    Currency,
    Gem,
    DivinationCard,
    Unknown
}

public sealed record ItemData
{
    public string ItemClass { get; init; } = string.Empty;
    public ItemRarity Rarity { get; init; } = ItemRarity.Unknown;
    public string Name { get; init; } = string.Empty;
    public string BaseType { get; init; } = string.Empty;
    public int ItemLevel { get; init; }
    public int Quality { get; init; }
    public int Armour { get; init; }
    public int Evasion { get; init; }
    public int EnergyShield { get; init; }
    public double PhysicalDps { get; init; }
    public double ElementalDps { get; init; }
    public double TotalDps { get; init; }
    public ImmutableList<string> ImplicitMods { get; init; } = ImmutableList<string>.Empty;
    public ImmutableList<string> ExplicitMods { get; init; } = ImmutableList<string>.Empty;
    public string RawText { get; init; } = string.Empty;
}
