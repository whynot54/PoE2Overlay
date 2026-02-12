using System.Text.Json.Nodes;
using PoE2Overlay.Models;

namespace PoE2Overlay.Services;

public static class TradeQueryBuilder
{
    // Maps PoE 2 "Item Class" clipboard text to trade API category filters
    private static readonly Dictionary<string, string> ItemClassToCategory = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Body Armours"] = "armour.chest",
        ["Helmets"] = "armour.head",
        ["Gloves"] = "armour.gloves",
        ["Boots"] = "armour.boots",
        ["Shields"] = "armour.shield",
        ["Belts"] = "accessory.belt",
        ["Rings"] = "accessory.ring",
        ["Amulets"] = "accessory.amulet",
        ["Quivers"] = "armour.quiver",
        ["Foci"] = "armour.focus",
        ["One Hand Swords"] = "weapon.onesword",
        ["Two Hand Swords"] = "weapon.twosword",
        ["One Hand Axes"] = "weapon.oneaxe",
        ["Two Hand Axes"] = "weapon.twoaxe",
        ["One Hand Maces"] = "weapon.onemace",
        ["Two Hand Maces"] = "weapon.twomace",
        ["Bows"] = "weapon.bow",
        ["Crossbows"] = "weapon.crossbow",
        ["Staves"] = "weapon.staff",
        ["Quarterstaves"] = "weapon.quarterstaff",
        ["Wands"] = "weapon.wand",
        ["Sceptres"] = "weapon.sceptre",
        ["Daggers"] = "weapon.dagger",
        ["Claws"] = "weapon.claw",
        ["Spears"] = "weapon.spear",
        ["Flails"] = "weapon.flail",
        ["Traps"] = "weapon.trap",
        ["Jewels"] = "jewel",
        ["Flasks"] = "flask",
    };

    public static JsonObject Build(ItemData item)
    {
        var query = new JsonObject();

        // Status filter — search all available listings
        query["status"] = new JsonObject { ["option"] = "online" };

        // Always include stats array (API requires it)
        query["stats"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "and",
                ["filters"] = new JsonArray()
            }
        };

        if (item.Rarity == ItemRarity.Unique)
        {
            // For unique items, search by exact name
            query["name"] = item.Name;
            query["type"] = item.BaseType;
        }
        else if (item.Rarity == ItemRarity.Currency || item.ItemClass == "Currency")
        {
            query["type"] = item.Name;
        }
        else if (item.Rarity == ItemRarity.Gem)
        {
            query["type"] = item.BaseType;
        }
        else
        {
            // For rare/magic items, use category filter instead of base type name
            // This avoids "Unknown item base type" errors
            query["filters"] = BuildFilters(item);
        }

        var root = new JsonObject
        {
            ["query"] = query,
            ["sort"] = new JsonObject { ["price"] = "asc" }
        };

        return root;
    }

    private static JsonObject BuildFilters(ItemData item)
    {
        var filters = new JsonObject();

        // Type filters — category + item level
        var typeFilters = new JsonObject();

        if (ItemClassToCategory.TryGetValue(item.ItemClass, out var category))
        {
            typeFilters["category"] = new JsonObject { ["option"] = category };
        }

        if (item.ItemLevel > 0)
        {
            typeFilters["ilvl"] = new JsonObject
            {
                ["min"] = Math.Max(item.ItemLevel - 5, 1),
                ["max"] = item.ItemLevel + 5
            };
        }

        if (typeFilters.Count > 0)
        {
            filters["type_filters"] = new JsonObject { ["filters"] = typeFilters };
        }

        // Equipment filters — defensive stats
        var equipFilters = new JsonObject();
        if (item.Armour > 0)
            equipFilters["ar"] = new JsonObject { ["min"] = (int)(item.Armour * 0.7) };
        if (item.Evasion > 0)
            equipFilters["ev"] = new JsonObject { ["min"] = (int)(item.Evasion * 0.7) };
        if (item.EnergyShield > 0)
            equipFilters["es"] = new JsonObject { ["min"] = (int)(item.EnergyShield * 0.7) };

        if (equipFilters.Count > 0)
        {
            filters["equipment_filters"] = new JsonObject { ["filters"] = equipFilters };
        }

        return filters;
    }
}
