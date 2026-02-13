using System.Collections.Immutable;
using System.Text.RegularExpressions;
using PoE2Overlay.Models;

namespace PoE2Overlay.Services;

public static partial class ItemParser
{
    private const string SectionSeparator = "--------";

    public static bool IsPoEItem(string text)
    {
        return !string.IsNullOrWhiteSpace(text)
               && text.Contains("Item Class:", StringComparison.Ordinal);
    }

    public static ItemData Parse(string rawText)
    {
        var sections = rawText
            .Replace("\r\n", "\n")
            .Split(SectionSeparator, StringSplitOptions.None)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();

        if (sections.Count == 0)
            return new ItemData { RawText = rawText };

        var itemClass = string.Empty;
        var rarity = ItemRarity.Unknown;
        var name = string.Empty;
        var baseType = string.Empty;

        // First section: Item Class, Rarity, Name, Base Type
        var headerLines = sections[0].Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in headerLines)
        {
            if (line.StartsWith("Item Class:", StringComparison.Ordinal))
                itemClass = line["Item Class:".Length..].Trim();
            else if (line.StartsWith("Rarity:", StringComparison.Ordinal))
                rarity = ParseRarity(line["Rarity:".Length..].Trim());
        }

        // Name and base type come after Rarity line
        var rarityIndex = Array.FindIndex(headerLines, l => l.StartsWith("Rarity:", StringComparison.Ordinal));
        if (rarityIndex >= 0)
        {
            var remaining = headerLines.Skip(rarityIndex + 1).ToArray();
            if (remaining.Length >= 2)
            {
                name = remaining[0].Trim();
                baseType = remaining[1].Trim();
            }
            else if (remaining.Length == 1)
            {
                baseType = remaining[0].Trim();
                name = baseType;
            }
        }

        var itemLevel = 0;
        var quality = 0;
        var armour = 0;
        var evasion = 0;
        var energyShield = 0;
        var implicitMods = ImmutableList<string>.Empty;
        var explicitMods = ImmutableList<string>.Empty;

        // Parse remaining sections
        for (var i = 1; i < sections.Count; i++)
        {
            var lines = sections[i].Split('\n', StringSplitOptions.RemoveEmptyEntries);

            foreach (var line in lines)
            {
                if (line.StartsWith("Item Level:", StringComparison.Ordinal))
                    itemLevel = ParseIntValue(line);
                else if (line.StartsWith("Quality:", StringComparison.Ordinal))
                    quality = ParseIntValue(line);
                else if (line.StartsWith("Armour:", StringComparison.Ordinal))
                    armour = ParseIntValue(line);
                else if (line.StartsWith("Evasion Rating:", StringComparison.Ordinal))
                    evasion = ParseIntValue(line);
                else if (line.StartsWith("Energy Shield:", StringComparison.Ordinal))
                    energyShield = ParseIntValue(line);
            }
        }

        // Implicit mods: section right before explicit mods (usually has "(implicit)" tag)
        // Explicit mods: last section before any trailing info sections
        var modSections = FindModSections(sections);
        implicitMods = modSections.Implicits;
        explicitMods = modSections.Explicits;

        return new ItemData
        {
            ItemClass = itemClass,
            Rarity = rarity,
            Name = name,
            BaseType = baseType,
            ItemLevel = itemLevel,
            Quality = quality,
            Armour = armour,
            Evasion = evasion,
            EnergyShield = energyShield,
            ImplicitMods = implicitMods,
            ExplicitMods = explicitMods,
            RawText = rawText
        };
    }

    private static ItemRarity ParseRarity(string value) => value switch
    {
        "Normal" => ItemRarity.Normal,
        "Magic" => ItemRarity.Magic,
        "Rare" => ItemRarity.Rare,
        "Unique" => ItemRarity.Unique,
        "Currency" => ItemRarity.Currency,
        "Gem" => ItemRarity.Gem,
        "Divination Card" => ItemRarity.DivinationCard,
        _ => ItemRarity.Unknown
    };

    private static int ParseIntValue(string line)
    {
        var match = NumberPattern().Match(line);
        return match.Success ? int.Parse(match.Value) : 0;
    }

    private static (ImmutableList<string> Implicits, ImmutableList<string> Explicits) FindModSections(
        List<string> sections)
    {
        var implicits = ImmutableList<string>.Empty;
        var explicits = ImmutableList<string>.Empty;

        for (var i = 1; i < sections.Count; i++)
        {
            var lines = sections[i].Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // Skip sections that are just stats or info lines
            if (lines.Any(l => l.StartsWith("Item Level:", StringComparison.Ordinal)))
                continue;
            if (lines.Any(l => l.StartsWith("Armour:", StringComparison.Ordinal)))
                continue;
            if (lines.Any(l => l.StartsWith("Evasion Rating:", StringComparison.Ordinal)))
                continue;
            if (lines.Any(l => l.StartsWith("Energy Shield:", StringComparison.Ordinal)))
                continue;
            if (lines.Any(l => l.StartsWith("Quality:", StringComparison.Ordinal)))
                continue;
            if (lines.Any(l => l.StartsWith("Item Class:", StringComparison.Ordinal)))
                continue;
            if (lines.Any(l => l.StartsWith("Sockets:", StringComparison.Ordinal)))
                continue;

            // Check for implicit marker
            if (lines.Any(l => l.Contains("(implicit)", StringComparison.Ordinal)))
            {
                implicits = lines
                    .Select(l => l.Replace(" (implicit)", "").Trim())
                    .Where(l => l.Length > 0)
                    .ToImmutableList();
            }
            else if (IsModSection(lines))
            {
                explicits = lines
                    .Where(l => l.Length > 0)
                    .ToImmutableList();
            }
        }

        return (implicits, explicits);
    }

    private static bool IsModSection(string[] lines)
    {
        // Mod lines typically contain numbers with +/- or % signs
        return lines.Any(l => ModLinePattern().IsMatch(l));
    }

    [GeneratedRegex(@"\d+")]
    private static partial Regex NumberPattern();

    public static EquipmentSlot? DetectSlot(ItemData item)
    {
        return item.ItemClass.ToLowerInvariant() switch
        {
            "helmets" => EquipmentSlot.Helmet,
            "body armours" => EquipmentSlot.BodyArmour,
            "gloves" => EquipmentSlot.Gloves,
            "boots" => EquipmentSlot.Boots,
            "belts" => EquipmentSlot.Belt,
            "amulets" => EquipmentSlot.Amulet,
            "rings" => EquipmentSlot.Ring1,
            "one hand maces" or "one hand swords" or "one hand axes"
                or "claws" or "daggers" or "wands" or "sceptres"
                or "thrusting one hand swords" => EquipmentSlot.Weapon,
            "two hand maces" or "two hand swords" or "two hand axes"
                or "bows" or "staves" or "warstaves" => EquipmentSlot.Weapon,
            "shields" or "quivers" or "foci" => EquipmentSlot.Offhand,
            _ => null
        };
    }

    [GeneratedRegex(@"[+\-]?\d+[%]?|increased|reduced|to maximum")]
    private static partial Regex ModLinePattern();
}
