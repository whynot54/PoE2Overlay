using System.Collections.Immutable;

namespace PoE2Overlay.Models;

public enum Archetype
{
    Balanced,
    SpellCaster,
    AttackMelee,
    AttackRanged,
    Summoner,
    Tank
}

public enum CompareDisplayMode
{
    Simple,
    CategoryBreakdown,
    FullStatDiff
}

public sealed class WeightProfile
{
    public string ProfileName { get; set; } = "Default";
    public Archetype Archetype { get; set; } = Archetype.Balanced;
    public Dictionary<string, int> Weights { get; set; } = new();
    public Dictionary<string, ItemData?> DreamBuild { get; set; } = new();
    public CompareDisplayMode DisplayMode { get; set; } = CompareDisplayMode.CategoryBreakdown;
}
