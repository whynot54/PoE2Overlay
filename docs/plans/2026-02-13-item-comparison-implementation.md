# Item Comparison & Weighted Scoring — Implementation Plan

> **For Claude:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Add a weighted item comparison system to PoE2Overlay that scores dropped items against equipped gear and dream build targets.

**Architecture:** Data-driven scoring engine embedded in the WPF app. Stat definitions loaded from JSON (updatable). Profiles stored as JSON files. Character API integration via POESESSID. Toggle between Price Check and Compare modes on the overlay.

**Tech Stack:** C# / .NET 9.0 / WPF, System.Text.Json, Win32 P/Invoke (existing), HttpClient (existing)

---

## Phase A: Foundation

### Task 1: Create EquipmentSlot Enum and StatCategory Enum

**Files:**
- Create: `Models/EquipmentSlot.cs`
- Create: `Models/StatCategory.cs`

**Step 1: Create EquipmentSlot enum**

Create `Models/EquipmentSlot.cs`:
```csharp
namespace PoE2Overlay.Models;

public enum EquipmentSlot
{
    Helmet,
    BodyArmour,
    Gloves,
    Boots,
    Belt,
    Amulet,
    Ring1,
    Ring2,
    Weapon,
    Offhand
}
```

**Step 2: Create StatCategory enum**

Create `Models/StatCategory.cs`:
```csharp
namespace PoE2Overlay.Models;

public enum StatCategory
{
    LifeAndES,
    EleResist,
    ChaosResist,
    PhysicalDefense,
    SpellDamage,
    AttackDamage,
    Speed,
    Critical,
    ManaAndRegen,
    Utility
}
```

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add Models/EquipmentSlot.cs Models/StatCategory.cs
git commit -m "feat: add EquipmentSlot and StatCategory enums"
```

---

### Task 2: Create StatDefinition Model and Default JSON

**Files:**
- Create: `Models/StatDefinition.cs`
- Create: `Data/stat-definitions.json`

**Step 1: Create StatDefinition model**

Create `Models/StatDefinition.cs`:
```csharp
using System.Collections.Immutable;

namespace PoE2Overlay.Models;

public sealed record StatPattern
{
    public string Regex { get; init; } = string.Empty;
    public string Type { get; init; } = "flat"; // "flat" or "percent"
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
```

**Step 2: Create default stat-definitions.json**

Create `Data/stat-definitions.json` with initial PoE2 mod patterns. This file will be embedded as content in the build.

```json
{
  "version": "1.0.0",
  "lastUpdated": "2026-02-13",
  "categories": {
    "LifeAndES": {
      "displayName": "Life & ES",
      "patterns": [
        { "regex": "\\+\\d+ to maximum Life", "type": "flat" },
        { "regex": "\\d+% increased maximum Life", "type": "percent" },
        { "regex": "\\+\\d+ to maximum Energy Shield", "type": "flat" },
        { "regex": "\\d+% increased maximum Energy Shield", "type": "percent" },
        { "regex": "Regenerate \\d+\\.?\\d* Life per second", "type": "flat" }
      ]
    },
    "EleResist": {
      "displayName": "Elemental Resists",
      "patterns": [
        { "regex": "\\+\\d+% to Fire Resistance", "type": "flat" },
        { "regex": "\\+\\d+% to Cold Resistance", "type": "flat" },
        { "regex": "\\+\\d+% to Lightning Resistance", "type": "flat" },
        { "regex": "\\+\\d+% to all Elemental Resistances", "type": "flat" }
      ]
    },
    "ChaosResist": {
      "displayName": "Chaos Resist",
      "patterns": [
        { "regex": "\\+\\d+% to Chaos Resistance", "type": "flat" }
      ]
    },
    "PhysicalDefense": {
      "displayName": "Physical Defense",
      "patterns": [
        { "regex": "\\+\\d+ to Armour", "type": "flat" },
        { "regex": "\\d+% increased Armour", "type": "percent" },
        { "regex": "\\+\\d+ to Evasion Rating", "type": "flat" },
        { "regex": "\\d+% increased Evasion Rating", "type": "percent" }
      ]
    },
    "SpellDamage": {
      "displayName": "Spell Damage",
      "patterns": [
        { "regex": "\\d+% increased Spell Damage", "type": "percent" },
        { "regex": "\\d+% increased Elemental Damage", "type": "percent" },
        { "regex": "\\+\\d+ to Level of all .* Spell Skill Gems", "type": "flat" },
        { "regex": "Adds \\d+ to \\d+ .* Damage to Spells", "type": "flat" }
      ]
    },
    "AttackDamage": {
      "displayName": "Attack Damage",
      "patterns": [
        { "regex": "\\d+% increased Physical Damage", "type": "percent" },
        { "regex": "Adds \\d+ to \\d+ Physical Damage", "type": "flat" },
        { "regex": "Adds \\d+ to \\d+ .* Damage", "type": "flat" },
        { "regex": "\\d+% increased Melee Damage", "type": "percent" }
      ]
    },
    "Speed": {
      "displayName": "Speed",
      "patterns": [
        { "regex": "\\d+% increased Attack Speed", "type": "percent" },
        { "regex": "\\d+% increased Cast Speed", "type": "percent" },
        { "regex": "\\d+% increased Movement Speed", "type": "percent" }
      ]
    },
    "Critical": {
      "displayName": "Critical",
      "patterns": [
        { "regex": "\\d+% increased Critical Hit Chance", "type": "percent" },
        { "regex": "\\+\\d+% to Critical Hit Multiplier", "type": "flat" },
        { "regex": "\\+\\d+\\.?\\d*% to Critical Hit Chance", "type": "flat" }
      ]
    },
    "ManaAndRegen": {
      "displayName": "Mana & Regen",
      "patterns": [
        { "regex": "\\+\\d+ to maximum Mana", "type": "flat" },
        { "regex": "\\d+% increased maximum Mana", "type": "percent" },
        { "regex": "\\d+% increased Mana Regeneration Rate", "type": "percent" },
        { "regex": "\\-\\d+ to Total Mana Cost", "type": "flat" }
      ]
    },
    "Utility": {
      "displayName": "Utility",
      "patterns": [
        { "regex": "\\+\\d+ to Strength", "type": "flat" },
        { "regex": "\\+\\d+ to Dexterity", "type": "flat" },
        { "regex": "\\+\\d+ to Intelligence", "type": "flat" },
        { "regex": "\\+\\d+ to all Attributes", "type": "flat" },
        { "regex": "\\d+% increased Rarity of Items found", "type": "percent" }
      ]
    }
  }
}
```

**Step 3: Add JSON file to csproj as Content**

Modify `PoE2Overlay.csproj` — add inside `<Project>`:
```xml
<ItemGroup>
    <Content Include="Data\stat-definitions.json">
        <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
</ItemGroup>
```

**Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeded, `Data/stat-definitions.json` copied to output

**Step 5: Commit**

```bash
git add Models/StatDefinition.cs Data/stat-definitions.json PoE2Overlay.csproj
git commit -m "feat: add StatDefinition model and default stat-definitions.json"
```

---

### Task 3: Create StatDefinitionService

**Files:**
- Create: `Services/StatDefinitionService.cs`

**Step 1: Implement StatDefinitionService**

Create `Services/StatDefinitionService.cs`:
```csharp
using System.Collections.Immutable;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoE2Overlay.Models;

namespace PoE2Overlay.Services;

public sealed class StatDefinitionService
{
    private const string LocalPath = "Data/stat-definitions.json";
    private const string RemoteUrl = "https://raw.githubusercontent.com/whynot54/PoE2Overlay/main/PoE2Overlay/Data/stat-definitions.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private StatDefinitions _definitions = new();
    private ImmutableDictionary<string, List<(Regex Pattern, string Type)>> _compiledPatterns
        = ImmutableDictionary<string, List<(Regex, string)>>.Empty;

    public StatDefinitions Definitions => _definitions;
    public string Version => _definitions.Version;
    public string LastUpdated => _definitions.LastUpdated;

    public void Load()
    {
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LocalPath);
        if (!File.Exists(path))
            return;

        var json = File.ReadAllText(path);
        var defs = JsonSerializer.Deserialize<StatDefinitions>(json, JsonOptions);
        if (defs != null)
        {
            _definitions = defs;
            CompilePatterns();
        }
    }

    public async Task<bool> UpdateFromRemoteAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient();
        http.DefaultRequestHeaders.UserAgent.ParseAdd("PoE2Overlay/1.0");

        var json = await http.GetStringAsync(RemoteUrl, ct);
        var defs = JsonSerializer.Deserialize<StatDefinitions>(json, JsonOptions);
        if (defs == null)
            return false;

        // Save locally
        var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, LocalPath);
        await File.WriteAllTextAsync(path, json, ct);

        _definitions = defs;
        CompilePatterns();
        return true;
    }

    /// <summary>
    /// Categorize a mod line and extract its numeric value.
    /// Returns null if the mod doesn't match any known pattern.
    /// </summary>
    public (StatCategory Category, double Value)? CategorizeAndExtract(string modLine)
    {
        foreach (var (categoryKey, patterns) in _compiledPatterns)
        {
            foreach (var (pattern, type) in patterns)
            {
                var match = pattern.Match(modLine);
                if (!match.Success) continue;

                var category = Enum.Parse<StatCategory>(categoryKey);
                var value = ExtractNumericValue(modLine);
                return (category, value);
            }
        }

        return null;
    }

    private void CompilePatterns()
    {
        var builder = ImmutableDictionary.CreateBuilder<string, List<(Regex, string)>>();

        foreach (var (key, categoryDef) in _definitions.Categories)
        {
            var list = new List<(Regex, string)>();
            foreach (var p in categoryDef.Patterns)
            {
                list.Add((new Regex(p.Regex, RegexOptions.Compiled | RegexOptions.IgnoreCase), p.Type));
            }
            builder[key] = list;
        }

        _compiledPatterns = builder.ToImmutable();
    }

    private static double ExtractNumericValue(string modLine)
    {
        // Handle range patterns like "Adds 10 to 20 Fire Damage" → average (15)
        var rangeMatch = Regex.Match(modLine, @"(\d+\.?\d*)\s+to\s+(\d+\.?\d*)");
        if (rangeMatch.Success)
        {
            var low = double.Parse(rangeMatch.Groups[1].Value);
            var high = double.Parse(rangeMatch.Groups[2].Value);
            return (low + high) / 2.0;
        }

        // Handle single numeric value: "+50 to maximum Life" → 50
        var singleMatch = Regex.Match(modLine, @"[+-]?(\d+\.?\d*)");
        if (singleMatch.Success)
        {
            var val = double.Parse(singleMatch.Groups[1].Value);
            // Preserve sign if negative
            if (modLine.Contains('-') && !modLine.Contains("to"))
                val = -val;
            return val;
        }

        return 0;
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add Services/StatDefinitionService.cs
git commit -m "feat: add StatDefinitionService with load, update, and categorization"
```

---

### Task 4: Create WeightProfile Model and ProfileManager

**Files:**
- Create: `Models/WeightProfile.cs`
- Create: `Services/ProfileManager.cs`

**Step 1: Create WeightProfile model**

Create `Models/WeightProfile.cs`:
```csharp
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
```

**Step 2: Create ProfileManager**

Create `Services/ProfileManager.cs`:
```csharp
using System.IO;
using System.Text.Json;
using PoE2Overlay.Models;

namespace PoE2Overlay.Services;

public sealed class ProfileManager
{
    private static readonly string ProfilesDir = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "profiles");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly Dictionary<Archetype, Dictionary<string, int>> ArchetypePresets = new()
    {
        [Archetype.Balanced] = new()
        {
            ["LifeAndES"] = 7, ["EleResist"] = 7, ["ChaosResist"] = 4,
            ["PhysicalDefense"] = 5, ["SpellDamage"] = 5, ["AttackDamage"] = 5,
            ["Speed"] = 6, ["Critical"] = 4, ["ManaAndRegen"] = 3, ["Utility"] = 2
        },
        [Archetype.SpellCaster] = new()
        {
            ["LifeAndES"] = 8, ["EleResist"] = 7, ["ChaosResist"] = 4,
            ["PhysicalDefense"] = 3, ["SpellDamage"] = 10, ["AttackDamage"] = 1,
            ["Speed"] = 8, ["Critical"] = 6, ["ManaAndRegen"] = 6, ["Utility"] = 3
        },
        [Archetype.AttackMelee] = new()
        {
            ["LifeAndES"] = 9, ["EleResist"] = 8, ["ChaosResist"] = 5,
            ["PhysicalDefense"] = 8, ["SpellDamage"] = 1, ["AttackDamage"] = 10,
            ["Speed"] = 6, ["Critical"] = 5, ["ManaAndRegen"] = 2, ["Utility"] = 4
        },
        [Archetype.AttackRanged] = new()
        {
            ["LifeAndES"] = 7, ["EleResist"] = 7, ["ChaosResist"] = 3,
            ["PhysicalDefense"] = 4, ["SpellDamage"] = 1, ["AttackDamage"] = 10,
            ["Speed"] = 7, ["Critical"] = 8, ["ManaAndRegen"] = 2, ["Utility"] = 3
        },
        [Archetype.Summoner] = new()
        {
            ["LifeAndES"] = 8, ["EleResist"] = 9, ["ChaosResist"] = 5,
            ["PhysicalDefense"] = 5, ["SpellDamage"] = 3, ["AttackDamage"] = 1,
            ["Speed"] = 4, ["Critical"] = 2, ["ManaAndRegen"] = 4, ["Utility"] = 5
        },
        [Archetype.Tank] = new()
        {
            ["LifeAndES"] = 10, ["EleResist"] = 10, ["ChaosResist"] = 7,
            ["PhysicalDefense"] = 10, ["SpellDamage"] = 1, ["AttackDamage"] = 2,
            ["Speed"] = 3, ["Critical"] = 1, ["ManaAndRegen"] = 3, ["Utility"] = 4
        }
    };

    public List<string> ListProfiles()
    {
        if (!Directory.Exists(ProfilesDir))
            return [];

        return Directory.GetFiles(ProfilesDir, "*.json")
            .Select(Path.GetFileNameWithoutExtension)
            .Where(n => n != null)
            .Cast<string>()
            .OrderBy(n => n)
            .ToList();
    }

    public WeightProfile Load(string profileName)
    {
        var path = GetProfilePath(profileName);
        if (!File.Exists(path))
            return CreateDefault(profileName);

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<WeightProfile>(json, JsonOptions) ?? CreateDefault(profileName);
        }
        catch
        {
            return CreateDefault(profileName);
        }
    }

    public void Save(WeightProfile profile)
    {
        Directory.CreateDirectory(ProfilesDir);
        var path = GetProfilePath(profile.ProfileName);
        var json = JsonSerializer.Serialize(profile, JsonOptions);
        File.WriteAllText(path, json);
    }

    public void Delete(string profileName)
    {
        var path = GetProfilePath(profileName);
        if (File.Exists(path))
            File.Delete(path);
    }

    public WeightProfile CreateFromArchetype(string profileName, Archetype archetype)
    {
        var profile = new WeightProfile
        {
            ProfileName = profileName,
            Archetype = archetype,
            Weights = new Dictionary<string, int>(GetPresetWeights(archetype)),
            DreamBuild = CreateEmptyDreamBuild()
        };
        Save(profile);
        return profile;
    }

    public static Dictionary<string, int> GetPresetWeights(Archetype archetype)
    {
        return ArchetypePresets.TryGetValue(archetype, out var preset)
            ? new Dictionary<string, int>(preset)
            : new Dictionary<string, int>(ArchetypePresets[Archetype.Balanced]);
    }

    private static WeightProfile CreateDefault(string profileName)
    {
        return new WeightProfile
        {
            ProfileName = profileName,
            Archetype = Archetype.Balanced,
            Weights = new Dictionary<string, int>(ArchetypePresets[Archetype.Balanced]),
            DreamBuild = CreateEmptyDreamBuild()
        };
    }

    private static Dictionary<string, ItemData?> CreateEmptyDreamBuild()
    {
        var build = new Dictionary<string, ItemData?>();
        foreach (var slot in Enum.GetNames<EquipmentSlot>())
            build[slot] = null;
        return build;
    }

    private static string GetProfilePath(string profileName)
    {
        // Sanitize filename
        var safe = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(ProfilesDir, $"{safe}.json");
    }
}
```

**Step 3: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 4: Commit**

```bash
git add Models/WeightProfile.cs Services/ProfileManager.cs
git commit -m "feat: add WeightProfile model and ProfileManager with archetype presets"
```

---

### Task 5: Extend AppConfig for Compare Mode Settings

**Files:**
- Modify: `Models/AppConfig.cs`

**Step 1: Add compare mode fields to AppConfig**

Add to `AppConfig.cs`:
```csharp
public string AccountName { get; set; } = string.Empty;
public string ActiveProfile { get; set; } = "Default";
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add Models/AppConfig.cs
git commit -m "feat: extend AppConfig with AccountName and ActiveProfile"
```

---

## Phase B: Character API + Compare Core

### Task 6: Add EquipmentSlot Detection to ItemParser

**Files:**
- Modify: `Services/ItemParser.cs`

**Step 1: Add slot detection method**

Add this method to `ItemParser.cs` after the `Parse` method:
```csharp
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
        "rings" => EquipmentSlot.Ring1, // Default to Ring1, user context needed for Ring2
        "one hand maces" or "one hand swords" or "one hand axes"
            or "claws" or "daggers" or "wands" or "sceptres"
            or "thrusting one hand swords" => EquipmentSlot.Weapon,
        "two hand maces" or "two hand swords" or "two hand axes"
            or "bows" or "staves" or "warstaves" => EquipmentSlot.Weapon,
        "shields" or "quivers" or "foci" => EquipmentSlot.Offhand,
        _ => null
    };
}
```

Add `using PoE2Overlay.Models;` if not already present (it is).

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add Services/ItemParser.cs
git commit -m "feat: add equipment slot detection to ItemParser"
```

---

### Task 7: Create CharacterApiClient

**Files:**
- Create: `Services/CharacterApiClient.cs`

**Step 1: Implement CharacterApiClient**

Create `Services/CharacterApiClient.cs`:
```csharp
using System.Collections.Immutable;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using PoE2Overlay.Models;

namespace PoE2Overlay.Services;

public sealed class CharacterApiClient
{
    private const string BaseUrl = "https://www.pathofexile.com";

    private readonly HttpClient _http;

    public CharacterApiClient(string poeSessId)
    {
        var handler = new HttpClientHandler();
        if (!string.IsNullOrEmpty(poeSessId))
        {
            handler.CookieContainer = new CookieContainer();
            handler.CookieContainer.Add(new Uri(BaseUrl), new Cookie("POESESSID", poeSessId));
        }

        _http = new HttpClient(handler);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("PoE2Overlay/1.0 (contact: overlay@poe2helper.dev)");
    }

    /// <summary>
    /// Fetches all characters for an account. Returns list of (name, class, level, league).
    /// </summary>
    public async Task<List<CharacterSummary>> GetCharactersAsync(string accountName, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/character-window/get-characters?accountName={Uri.EscapeDataString(accountName)}&realm=poe2";
        var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Character API returned {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var characters = new List<CharacterSummary>();

        foreach (var elem in doc.RootElement.EnumerateArray())
        {
            characters.Add(new CharacterSummary
            {
                Name = elem.GetProperty("name").GetString() ?? string.Empty,
                Class = elem.GetProperty("class").GetString() ?? string.Empty,
                Level = elem.TryGetProperty("level", out var lvl) ? lvl.GetInt32() : 0,
                League = elem.TryGetProperty("league", out var league) ? league.GetString() ?? string.Empty : string.Empty
            });
        }

        return characters;
    }

    /// <summary>
    /// Fetches equipped items for a character. Returns items indexed by equipment slot.
    /// </summary>
    public async Task<ImmutableDictionary<EquipmentSlot, ItemData>> GetEquippedItemsAsync(
        string accountName, string characterName, CancellationToken ct = default)
    {
        var url = $"{BaseUrl}/character-window/get-items?accountName={Uri.EscapeDataString(accountName)}&character={Uri.EscapeDataString(characterName)}&realm=poe2";
        var response = await _http.GetAsync(url, ct);

        if (!response.IsSuccessStatusCode)
            throw new Exception($"Character API returned {(int)response.StatusCode}");

        var body = await response.Content.ReadAsStringAsync(ct);
        var doc = JsonDocument.Parse(body);
        var equipped = new Dictionary<EquipmentSlot, ItemData>();

        if (!doc.RootElement.TryGetProperty("items", out var items))
            return equipped.ToImmutableDictionary();

        foreach (var itemElem in items.EnumerateArray())
        {
            var inventoryId = itemElem.TryGetProperty("inventoryId", out var inv)
                ? inv.GetString() ?? string.Empty
                : string.Empty;

            var slot = MapInventoryIdToSlot(inventoryId);
            if (slot == null) continue;

            var itemData = ParseApiItem(itemElem);
            equipped[slot.Value] = itemData;
        }

        return equipped.ToImmutableDictionary();
    }

    private static EquipmentSlot? MapInventoryIdToSlot(string inventoryId) => inventoryId switch
    {
        "Helm" => EquipmentSlot.Helmet,
        "BodyArmour" => EquipmentSlot.BodyArmour,
        "Gloves" => EquipmentSlot.Gloves,
        "Boots" => EquipmentSlot.Boots,
        "Belt" => EquipmentSlot.Belt,
        "Amulet" => EquipmentSlot.Amulet,
        "Ring" => EquipmentSlot.Ring1,
        "Ring2" => EquipmentSlot.Ring2,
        "Weapon" => EquipmentSlot.Weapon,
        "Offhand" => EquipmentSlot.Offhand,
        _ => null
    };

    private static ItemData ParseApiItem(JsonElement elem)
    {
        var name = elem.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
        var baseType = elem.TryGetProperty("typeLine", out var tl) ? tl.GetString() ?? string.Empty : string.Empty;
        var itemLevel = elem.TryGetProperty("ilvl", out var ilvl) ? ilvl.GetInt32() : 0;
        var frameType = elem.TryGetProperty("frameType", out var ft) ? ft.GetInt32() : 0;

        var rarity = frameType switch
        {
            0 => ItemRarity.Normal,
            1 => ItemRarity.Magic,
            2 => ItemRarity.Rare,
            3 => ItemRarity.Unique,
            _ => ItemRarity.Unknown
        };

        // Clean up name (API includes markup tags sometimes)
        name = name.Replace("<<set:MS>><<set:M>><<set:S>>", "").Trim();

        var implicitMods = ImmutableList<string>.Empty;
        if (elem.TryGetProperty("implicitMods", out var implicits))
        {
            implicitMods = implicits.EnumerateArray()
                .Select(m => m.GetString() ?? string.Empty)
                .Where(m => m.Length > 0)
                .ToImmutableList();
        }

        var explicitMods = ImmutableList<string>.Empty;
        if (elem.TryGetProperty("explicitMods", out var explicits))
        {
            explicitMods = explicits.EnumerateArray()
                .Select(m => m.GetString() ?? string.Empty)
                .Where(m => m.Length > 0)
                .ToImmutableList();
        }

        // Extract defence stats from properties
        var armour = 0;
        var evasion = 0;
        var energyShield = 0;
        if (elem.TryGetProperty("properties", out var props))
        {
            foreach (var prop in props.EnumerateArray())
            {
                var propName = prop.TryGetProperty("name", out var pn) ? pn.GetString() ?? "" : "";
                var propValues = prop.TryGetProperty("values", out var pv) ? pv : default;

                if (propValues.ValueKind == JsonValueKind.Array)
                {
                    foreach (var val in propValues.EnumerateArray())
                    {
                        if (val.ValueKind != JsonValueKind.Array) continue;
                        var strVal = val[0].GetString() ?? "0";
                        if (int.TryParse(strVal, out var numVal))
                        {
                            switch (propName)
                            {
                                case "Armour": armour = numVal; break;
                                case "Evasion Rating": evasion = numVal; break;
                                case "Energy Shield": energyShield = numVal; break;
                            }
                        }
                    }
                }
            }
        }

        return new ItemData
        {
            Name = name,
            BaseType = baseType,
            Rarity = rarity,
            ItemLevel = itemLevel,
            Armour = armour,
            Evasion = evasion,
            EnergyShield = energyShield,
            ImplicitMods = implicitMods,
            ExplicitMods = explicitMods,
            RawText = string.Empty
        };
    }
}

public sealed record CharacterSummary
{
    public string Name { get; init; } = string.Empty;
    public string Class { get; init; } = string.Empty;
    public int Level { get; init; }
    public string League { get; init; } = string.Empty;
}
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add Services/CharacterApiClient.cs
git commit -m "feat: add CharacterApiClient for fetching equipped gear"
```

---

### Task 8: Create ComparisonResult Model

**Files:**
- Create: `Models/ComparisonResult.cs`

**Step 1: Create ComparisonResult model**

Create `Models/ComparisonResult.cs`:
```csharp
using System.Collections.Immutable;

namespace PoE2Overlay.Models;

public enum ComparisonVerdict
{
    Upgrade,
    Downgrade,
    Sidegrade
}

public sealed record CategoryScore
{
    public StatCategory Category { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public double DroppedScore { get; init; }
    public double EquippedScore { get; init; }
    public double Difference { get; init; }
}

public sealed record StatDiff
{
    public string ModLine { get; init; } = string.Empty;
    public double Value { get; init; }
    public StatCategory? Category { get; init; }
    public bool IsUnscored { get; init; }
    public bool IsGain { get; init; }
}

public sealed record ComparisonResult
{
    public double DroppedTotalScore { get; init; }
    public double EquippedTotalScore { get; init; }
    public ComparisonVerdict Verdict { get; init; }
    public double PercentChange { get; init; }
    public ImmutableList<CategoryScore> CategoryScores { get; init; } = ImmutableList<CategoryScore>.Empty;
    public ImmutableList<StatDiff> StatDiffs { get; init; } = ImmutableList<StatDiff>.Empty;
    public ImmutableList<string> UnscoredMods { get; init; } = ImmutableList<string>.Empty;

    // Dream build comparison (null if no dream target set)
    public double? DreamTargetScore { get; init; }
    public double? DreamPercentOfTarget { get; init; }
}
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add Models/ComparisonResult.cs
git commit -m "feat: add ComparisonResult model with verdict, category scores, and stat diffs"
```

---

### Task 9: Create ItemScoringService

**Files:**
- Create: `Services/ItemScoringService.cs`

**Step 1: Implement ItemScoringService**

Create `Services/ItemScoringService.cs`:
```csharp
using System.Collections.Immutable;
using PoE2Overlay.Models;

namespace PoE2Overlay.Services;

public sealed class ItemScoringService
{
    private readonly StatDefinitionService _statDefs;

    // Sidegrade threshold: if percent change is within ±5%, consider it a sidegrade
    private const double SidegradeThreshold = 5.0;

    public ItemScoringService(StatDefinitionService statDefs)
    {
        _statDefs = statDefs;
    }

    public ComparisonResult Compare(ItemData dropped, ItemData equipped, ItemData? dreamTarget, Dictionary<string, int> weights)
    {
        var droppedStats = ExtractCategoryValues(dropped);
        var equippedStats = ExtractCategoryValues(equipped);

        var droppedTotal = 0.0;
        var equippedTotal = 0.0;
        var categoryScores = new List<CategoryScore>();

        foreach (var category in Enum.GetValues<StatCategory>())
        {
            var key = category.ToString();
            var weight = weights.TryGetValue(key, out var w) ? w : 0;

            var droppedVal = droppedStats.TryGetValue(category, out var dv) ? dv : 0;
            var equippedVal = equippedStats.TryGetValue(category, out var ev) ? ev : 0;

            var droppedWeighted = droppedVal * weight;
            var equippedWeighted = equippedVal * weight;

            droppedTotal += droppedWeighted;
            equippedTotal += equippedWeighted;

            // Get display name from stat definitions
            var displayName = _statDefs.Definitions.Categories.TryGetValue(key, out var catDef)
                ? catDef.DisplayName
                : key;

            categoryScores.Add(new CategoryScore
            {
                Category = category,
                DisplayName = displayName,
                DroppedScore = droppedWeighted,
                EquippedScore = equippedWeighted,
                Difference = droppedWeighted - equippedWeighted
            });
        }

        // Calculate stat diffs (individual mod line comparison)
        var statDiffs = BuildStatDiffs(dropped, equipped);
        var unscoredMods = statDiffs
            .Where(d => d.IsUnscored)
            .Select(d => d.ModLine)
            .ToImmutableList();

        // Determine verdict
        var percentChange = equippedTotal > 0
            ? ((droppedTotal - equippedTotal) / equippedTotal) * 100.0
            : (droppedTotal > 0 ? 100.0 : 0.0);

        var verdict = percentChange switch
        {
            > SidegradeThreshold => ComparisonVerdict.Upgrade,
            < -SidegradeThreshold => ComparisonVerdict.Downgrade,
            _ => ComparisonVerdict.Sidegrade
        };

        // Dream build comparison
        double? dreamScore = null;
        double? dreamPercent = null;
        if (dreamTarget != null)
        {
            var dreamStats = ExtractCategoryValues(dreamTarget);
            var dreamTotal = 0.0;
            foreach (var category in Enum.GetValues<StatCategory>())
            {
                var key = category.ToString();
                var weight = weights.TryGetValue(key, out var w) ? w : 0;
                var val = dreamStats.TryGetValue(category, out var v) ? v : 0;
                dreamTotal += val * weight;
            }
            dreamScore = dreamTotal;
            dreamPercent = dreamTotal > 0 ? (droppedTotal / dreamTotal) * 100.0 : null;
        }

        return new ComparisonResult
        {
            DroppedTotalScore = Math.Round(droppedTotal, 1),
            EquippedTotalScore = Math.Round(equippedTotal, 1),
            Verdict = verdict,
            PercentChange = Math.Round(percentChange, 1),
            CategoryScores = categoryScores.ToImmutableList(),
            StatDiffs = statDiffs,
            UnscoredMods = unscoredMods,
            DreamTargetScore = dreamScore.HasValue ? Math.Round(dreamScore.Value, 1) : null,
            DreamPercentOfTarget = dreamPercent.HasValue ? Math.Round(dreamPercent.Value, 1) : null
        };
    }

    private Dictionary<StatCategory, double> ExtractCategoryValues(ItemData item)
    {
        var values = new Dictionary<StatCategory, double>();

        // Score all mods (implicit + explicit)
        var allMods = item.ImplicitMods.AddRange(item.ExplicitMods);
        foreach (var mod in allMods)
        {
            var result = _statDefs.CategorizeAndExtract(mod);
            if (result == null) continue;

            var (category, value) = result.Value;
            values[category] = values.TryGetValue(category, out var existing) ? existing + value : value;
        }

        // Include base defensive stats in PhysicalDefense category
        if (item.Armour > 0)
            values[StatCategory.PhysicalDefense] = values.GetValueOrDefault(StatCategory.PhysicalDefense) + item.Armour;
        if (item.Evasion > 0)
            values[StatCategory.PhysicalDefense] = values.GetValueOrDefault(StatCategory.PhysicalDefense) + item.Evasion;
        if (item.EnergyShield > 0)
            values[StatCategory.LifeAndES] = values.GetValueOrDefault(StatCategory.LifeAndES) + item.EnergyShield;

        return values;
    }

    private ImmutableList<StatDiff> BuildStatDiffs(ItemData dropped, ItemData equipped)
    {
        var diffs = new List<StatDiff>();

        // Compare dropped mods against equipped
        var allDroppedMods = dropped.ImplicitMods.AddRange(dropped.ExplicitMods);
        foreach (var mod in allDroppedMods)
        {
            var result = _statDefs.CategorizeAndExtract(mod);
            diffs.Add(new StatDiff
            {
                ModLine = mod,
                Value = result?.Value ?? 0,
                Category = result?.Category,
                IsUnscored = result == null,
                IsGain = true
            });
        }

        // Show equipped mods that the dropped item is missing (as losses)
        var allEquippedMods = equipped.ImplicitMods.AddRange(equipped.ExplicitMods);
        foreach (var mod in allEquippedMods)
        {
            var result = _statDefs.CategorizeAndExtract(mod);
            // Only show as loss if dropped item doesn't have a similar mod in same category
            if (result != null)
            {
                var hasDroppedMod = allDroppedMods.Any(dm =>
                {
                    var dr = _statDefs.CategorizeAndExtract(dm);
                    return dr?.Category == result.Value.Category;
                });
                if (!hasDroppedMod)
                {
                    diffs.Add(new StatDiff
                    {
                        ModLine = mod,
                        Value = -(result.Value.Value),
                        Category = result.Value.Category,
                        IsUnscored = false,
                        IsGain = false
                    });
                }
            }
        }

        return diffs.ToImmutableList();
    }
}
```

**Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 3: Commit**

```bash
git add Services/ItemScoringService.cs
git commit -m "feat: add ItemScoringService with weighted scoring and comparison"
```

---

### Task 10: Add Compare Mode Toggle to Overlay UI

**Files:**
- Modify: `MainWindow.xaml` — add compare toggle button to control bar
- Modify: `MainWindow.xaml.cs` — add compare mode state and toggle handler

**Step 1: Add compare toggle button to XAML control bar**

In `MainWindow.xaml`, find the control bar DockPanel (line ~200). Add a compare toggle button after the existing ToggleMonitorButton. Modify the control bar section to add the new button between the start/stop button and the status text:

After the `ToggleMonitorButton` closing tag and before the `StatusText` TextBlock, add:
```xml
<!-- Compare mode toggle -->
<Button x:Name="CompareModeButton"
        DockPanel.Dock="Left"
        Click="CompareModeButton_OnClick"
        Content="&#x2696;"
        Width="28" Height="28"
        Cursor="Hand" ToolTip="Switch to Compare mode"
        Margin="4,0,0,0">
    <Button.Template>
        <ControlTemplate TargetType="Button">
            <Grid>
                <Ellipse x:Name="BgEllipse" Fill="Transparent" Width="28" Height="28" />
                <ContentPresenter HorizontalAlignment="Center" VerticalAlignment="Center" />
            </Grid>
            <ControlTemplate.Triggers>
                <Trigger Property="IsMouseOver" Value="True">
                    <Setter TargetName="BgEllipse" Property="Fill" Value="#33FFFFFF" />
                </Trigger>
            </ControlTemplate.Triggers>
        </ControlTemplate>
    </Button.Template>
</Button>
```

**Step 2: Add compare mode state to MainWindow.xaml.cs**

Add these fields near the top of `MainWindow` class (after existing fields):
```csharp
private bool _compareMode;
private CharacterApiClient? _characterApi;
private ImmutableDictionary<EquipmentSlot, ItemData> _equippedItems = ImmutableDictionary<EquipmentSlot, ItemData>.Empty;
private readonly StatDefinitionService _statDefService = new();
private ItemScoringService? _scoringService;
private readonly ProfileManager _profileManager = new();
private WeightProfile? _activeProfile;
```

**Step 3: Initialize services in OnSourceInitialized**

In `OnSourceInitialized`, after `InitializeSettings()`, add:
```csharp
_statDefService.Load();
_scoringService = new ItemScoringService(_statDefService);
LoadActiveProfile();
```

Add the `LoadActiveProfile` method:
```csharp
private void LoadActiveProfile()
{
    var profileName = _config.ActiveProfile;
    if (string.IsNullOrEmpty(profileName))
        profileName = "Default";

    _activeProfile = _profileManager.Load(profileName);
}
```

**Step 4: Add CompareModeButton click handler**

```csharp
private async void CompareModeButton_OnClick(object sender, RoutedEventArgs e)
{
    if (_compareMode)
    {
        // Switch back to Price Check mode
        _compareMode = false;
        CompareModeButton.ToolTip = "Switch to Compare mode";
        StatusText.Text = _monitoringActive ? "Monitoring" : "Stopped";
        StatusText.Foreground = _monitoringActive
            ? (Brush)FindResource("AccentGreen")
            : (Brush)FindResource("DimText");
    }
    else
    {
        // Switch to Compare mode — fetch equipped gear
        if (string.IsNullOrEmpty(_config.AccountName))
        {
            StatusText.Text = "Set account name in settings";
            StatusText.Foreground = (Brush)FindResource("ErrorRed");
            return;
        }

        StatusText.Text = "Fetching gear...";
        StatusText.Foreground = (Brush)FindResource("AccentGold");

        try
        {
            _characterApi = new CharacterApiClient(_config.PoeSessId);
            var characters = await _characterApi.GetCharactersAsync(_config.AccountName);

            if (characters.Count == 0)
            {
                StatusText.Text = "No characters found";
                StatusText.Foreground = (Brush)FindResource("ErrorRed");
                return;
            }

            // Use first character in current league, or first character overall
            var league = _leagueService.CurrentLeague;
            var character = characters.FirstOrDefault(c =>
                c.League.Equals(league, StringComparison.OrdinalIgnoreCase))
                ?? characters[0];

            _equippedItems = await _characterApi.GetEquippedItemsAsync(_config.AccountName, character.Name);

            _compareMode = true;
            CompareModeButton.ToolTip = "Switch to Price Check mode";
            StatusText.Text = $"Compare: {character.Name} ({_equippedItems.Count} slots)";
            StatusText.Foreground = (Brush)FindResource("AccentGold");
        }
        catch (Exception ex)
        {
            StatusText.Text = $"API error: {ex.Message}";
            StatusText.Foreground = (Brush)FindResource("ErrorRed");
        }
    }

    Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
}
```

**Step 5: Modify OnClipboardChanged to route between modes**

Replace the existing `OnClipboardChanged` method:
```csharp
private void OnClipboardChanged(object? sender, string clipboardText)
{
    Dispatcher.Invoke(() =>
    {
        var item = ItemParser.Parse(clipboardText);

        if (_compareMode)
        {
            ShowComparisonTooltip(item);
        }
        else
        {
            ShowItemTooltip(item);
            _ = FetchPriceAsync(item);
        }
    });
}
```

**Step 6: Add ShowComparisonTooltip method**

```csharp
private void ShowComparisonTooltip(ItemData droppedItem)
{
    var slot = ItemParser.DetectSlot(droppedItem);
    if (slot == null)
    {
        ShowItemTooltip(droppedItem);
        PriceLoadingText.Visibility = Visibility.Collapsed;
        PriceErrorText.Text = "Cannot determine equipment slot";
        PriceErrorText.Visibility = Visibility.Visible;
        return;
    }

    if (!_equippedItems.TryGetValue(slot.Value, out var equippedItem))
    {
        ShowItemTooltip(droppedItem);
        PriceLoadingText.Visibility = Visibility.Collapsed;
        PriceErrorText.Text = "No equipped item in this slot";
        PriceErrorText.Visibility = Visibility.Visible;
        return;
    }

    if (_scoringService == null || _activeProfile == null)
    {
        ShowItemTooltip(droppedItem);
        PriceLoadingText.Visibility = Visibility.Collapsed;
        PriceErrorText.Text = "Scoring service not ready";
        PriceErrorText.Visibility = Visibility.Visible;
        return;
    }

    // Get dream target for this slot
    var slotKey = slot.Value.ToString();
    ItemData? dreamTarget = null;
    if (_activeProfile.DreamBuild.TryGetValue(slotKey, out var dt))
        dreamTarget = dt;

    var result = _scoringService.Compare(droppedItem, equippedItem, dreamTarget, _activeProfile.Weights);

    // Show the item info
    ShowItemTooltip(droppedItem);

    // Replace price section with comparison result
    PriceLoadingText.Visibility = Visibility.Collapsed;
    ActionButtonsPanel.Visibility = Visibility.Collapsed;

    var verdictSymbol = result.Verdict switch
    {
        ComparisonVerdict.Upgrade => "\u25B2",   // ▲
        ComparisonVerdict.Downgrade => "\u25BC", // ▼
        _ => "\u2501"                             // ━
    };

    var verdictColor = result.Verdict switch
    {
        ComparisonVerdict.Upgrade => FindResource("AccentGreen") as Brush,
        ComparisonVerdict.Downgrade => FindResource("ErrorRed") as Brush,
        _ => FindResource("AccentGold") as Brush
    };

    var verdictText = result.Verdict switch
    {
        ComparisonVerdict.Upgrade => "UPGRADE",
        ComparisonVerdict.Downgrade => "DOWNGRADE",
        _ => "SIDEGRADE"
    };

    PriceText.Text = $"{verdictSymbol} {verdictText}  Score: {result.DroppedTotalScore:F0} vs {result.EquippedTotalScore:F0}";
    PriceText.Foreground = verdictColor;
    PriceText.Visibility = Visibility.Visible;

    // Show category breakdown or full diff based on display mode
    var detailText = string.Empty;
    if (_activeProfile.DisplayMode == CompareDisplayMode.CategoryBreakdown
        || _activeProfile.DisplayMode == CompareDisplayMode.FullStatDiff)
    {
        var lines = result.CategoryScores
            .Where(c => Math.Abs(c.Difference) > 0.1)
            .Select(c =>
            {
                var sign = c.Difference > 0 ? "+" : "";
                return $"{c.DisplayName}: {sign}{c.Difference:F0}";
            });
        detailText = string.Join("\n", lines);
    }

    if (_activeProfile.DisplayMode == CompareDisplayMode.FullStatDiff && result.StatDiffs.Count > 0)
    {
        if (detailText.Length > 0)
            detailText += "\n───────────────────";

        var diffLines = result.StatDiffs
            .Where(d => !d.IsUnscored)
            .Select(d =>
            {
                var prefix = d.IsGain ? "+" : "-";
                return $"{prefix} {d.ModLine}";
            });
        detailText += "\n" + string.Join("\n", diffLines);
    }

    if (result.UnscoredMods.Count > 0)
    {
        detailText += $"\n\n? {result.UnscoredMods.Count} unscored mod(s)";
    }

    ListingCountText.Text = detailText;
    ListingCountText.Foreground = (Brush)FindResource("SubText");
    ListingCountText.Visibility = string.IsNullOrEmpty(detailText) ? Visibility.Collapsed : Visibility.Visible;

    // Dream build comparison
    if (result.DreamPercentOfTarget.HasValue)
    {
        PriceErrorText.Text = $"vs Dream: {result.DreamPercentOfTarget:F0}% of target";
        PriceErrorText.Foreground = result.DreamPercentOfTarget >= 100
            ? (Brush)FindResource("AccentGreen")
            : (Brush)FindResource("AccentGold");
        PriceErrorText.Visibility = Visibility.Visible;
    }

    Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
}
```

**Step 7: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 8: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add compare mode toggle with scoring display on overlay"
```

---

### Task 11: Add Account Name Field to Settings UI

**Files:**
- Modify: `MainWindow.xaml` — add account name field to settings panel
- Modify: `MainWindow.xaml.cs` — wire up account name save

**Step 1: Add account name field in XAML settings**

In `MainWindow.xaml`, in the settings panel StackPanel, add before the POESESSID section (before line 303 `<!-- POESESSID -->`):

```xml
<!-- Account Name -->
<TextBlock Text="Account Name" FontSize="11" Foreground="{StaticResource DimText}"
           Margin="0,12,0,4" />
<TextBox x:Name="AccountNameTextBox" FontSize="11"
         Background="#22FFFFFF" Foreground="#CCCCCC"
         BorderBrush="#33FFFFFF" Padding="6,4"
         Height="26" />
```

**Step 2: Initialize account name in InitializeSettings**

In `MainWindow.xaml.cs`, in `InitializeSettings()`, add:
```csharp
AccountNameTextBox.Text = _config.AccountName;
```

**Step 3: Save account name in SaveSettingsButton_OnClick**

In `SaveSettingsButton_OnClick`, before the `ConfigService.Save` call, add:
```csharp
_config.AccountName = AccountNameTextBox.Text.Trim();
```

**Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add account name field to settings panel"
```

---

## Phase C: Dream Build

### Task 12: Add Dream Build Slot Grid and Trade Search to Settings

This is a larger task. It adds:
- A dream build section to the settings panel with clickable slot buttons
- A search popup that uses the Trade API to find items by name
- Saves selected items to the active profile

**Files:**
- Modify: `MainWindow.xaml` — add dream build grid UI
- Modify: `MainWindow.xaml.cs` — add dream build handlers

**Step 1: Add dream build section to XAML settings panel**

In `MainWindow.xaml`, in the settings panel, add after the account name section and before POESESSID:

```xml
<!-- Dream Build Section -->
<Rectangle Height="1" Fill="{StaticResource SeparatorBrush}" Margin="0,12,0,10" />
<TextBlock Text="Dream Build" FontSize="13" FontWeight="SemiBold"
           Foreground="{StaticResource BrightText}" Margin="0,0,0,8" />

<!-- Profile selector -->
<DockPanel Margin="0,0,0,8">
    <Button DockPanel.Dock="Right" Content="New" Style="{StaticResource ActionButton}"
            Click="NewProfileButton_OnClick" Margin="4,0,0,0" />
    <ComboBox x:Name="ProfileComboBox" FontSize="12"
              SelectionChanged="ProfileComboBox_OnSelectionChanged"
              Background="#22FFFFFF" Foreground="Black" Height="28" />
</DockPanel>

<!-- Archetype selector -->
<TextBlock Text="Archetype" FontSize="11" Foreground="{StaticResource DimText}" Margin="0,0,0,4" />
<ComboBox x:Name="ArchetypeComboBox" FontSize="12"
          SelectionChanged="ArchetypeComboBox_OnSelectionChanged"
          Background="#22FFFFFF" Foreground="Black" Height="28" />

<!-- Display Mode -->
<TextBlock Text="Display Mode" FontSize="11" Foreground="{StaticResource DimText}" Margin="0,8,0,4" />
<ComboBox x:Name="DisplayModeComboBox" FontSize="12"
          SelectionChanged="DisplayModeComboBox_OnSelectionChanged"
          Background="#22FFFFFF" Foreground="Black" Height="28" />

<!-- Equipment slot grid (2 columns x 5 rows) -->
<UniformGrid x:Name="DreamBuildGrid" Columns="2" Margin="0,8,0,0">
    <!-- Populated programmatically -->
</UniformGrid>

<!-- Dream build item search popup -->
<Border x:Name="DreamSearchPanel" Visibility="Collapsed"
        Margin="0,8,0,0" Padding="8" CornerRadius="6"
        Background="#22FFFFFF">
    <StackPanel>
        <TextBlock x:Name="DreamSearchSlotLabel" FontSize="11"
                   Foreground="{StaticResource AccentGold}" Margin="0,0,0,4" />
        <DockPanel>
            <Button DockPanel.Dock="Right" Content="Search" Style="{StaticResource ActionButton}"
                    Click="DreamSearchButton_OnClick" Margin="4,0,0,0" />
            <TextBox x:Name="DreamSearchTextBox" FontSize="11"
                     Background="#22FFFFFF" Foreground="#CCCCCC"
                     BorderBrush="#33FFFFFF" Padding="6,4" Height="26" />
        </DockPanel>
        <StackPanel x:Name="DreamSearchResults" Margin="0,4,0,0" />
        <Button x:Name="DreamClearButton" Content="Clear slot" Style="{StaticResource ActionButton}"
                Click="DreamClearButton_OnClick" Margin="0,4,0,0"
                HorizontalAlignment="Left" />
    </StackPanel>
</Border>
```

**Step 2: Add dream build code-behind**

Add these fields:
```csharp
private EquipmentSlot? _dreamSearchSlot;
```

Add these methods to `MainWindow.xaml.cs`:

```csharp
// ═══════════════════════════════════════════════════
// PROFILE & DREAM BUILD
// ═══════════════════════════════════════════════════

private void InitializeProfileUI()
{
    // Populate archetype combo
    ArchetypeComboBox.Items.Clear();
    foreach (var archetype in Enum.GetValues<Archetype>())
        ArchetypeComboBox.Items.Add(archetype.ToString());

    // Populate display mode combo
    DisplayModeComboBox.Items.Clear();
    DisplayModeComboBox.Items.Add("Simple");
    DisplayModeComboBox.Items.Add("Category Breakdown");
    DisplayModeComboBox.Items.Add("Full Stat Diff");

    // Populate profile list
    RefreshProfileList();

    // Build dream build grid
    BuildDreamBuildGrid();
}

private void RefreshProfileList()
{
    ProfileComboBox.Items.Clear();
    var profiles = _profileManager.ListProfiles();
    if (profiles.Count == 0)
    {
        _profileManager.CreateFromArchetype("Default", Archetype.Balanced);
        profiles = _profileManager.ListProfiles();
    }

    foreach (var name in profiles)
        ProfileComboBox.Items.Add(name);

    ProfileComboBox.SelectedItem = _config.ActiveProfile;
}

private void BuildDreamBuildGrid()
{
    DreamBuildGrid.Children.Clear();
    foreach (var slot in Enum.GetValues<EquipmentSlot>())
    {
        var slotName = slot.ToString();
        var hasTarget = _activeProfile?.DreamBuild.TryGetValue(slotName, out var item) == true && item != null;
        var displayText = hasTarget ? $"{slotName}\n{item!.Name}" : slotName;

        var btn = new Button
        {
            Content = displayText,
            Tag = slot,
            Margin = new Thickness(2),
            Padding = new Thickness(4),
            FontSize = 10,
            Cursor = Cursors.Hand,
            Background = hasTarget
                ? new SolidColorBrush(Color.FromArgb(0x33, 0x44, 0xDD, 0x88))
                : new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)),
            Foreground = hasTarget
                ? (Brush)FindResource("AccentGreen")
                : (Brush)FindResource("SubText"),
            BorderThickness = new Thickness(0)
        };
        btn.Click += DreamSlotButton_OnClick;
        DreamBuildGrid.Children.Add(btn);
    }
}

private void DreamSlotButton_OnClick(object sender, RoutedEventArgs e)
{
    if (sender is Button btn && btn.Tag is EquipmentSlot slot)
    {
        _dreamSearchSlot = slot;
        DreamSearchSlotLabel.Text = $"Set target for: {slot}";
        DreamSearchTextBox.Text = string.Empty;
        DreamSearchResults.Children.Clear();
        DreamSearchPanel.Visibility = Visibility.Visible;
        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
    }
}

private async void DreamSearchButton_OnClick(object sender, RoutedEventArgs e)
{
    var searchTerm = DreamSearchTextBox.Text.Trim();
    if (string.IsNullOrEmpty(searchTerm) || _dreamSearchSlot == null)
        return;

    DreamSearchResults.Children.Clear();
    DreamSearchResults.Children.Add(new TextBlock
    {
        Text = "Searching...",
        FontSize = 10,
        Foreground = (Brush)FindResource("SubText"),
        FontStyle = FontStyles.Italic
    });

    try
    {
        // Search trade API for the item by name
        var league = _leagueService.CurrentLeague;
        var searchItem = new ItemData
        {
            Name = searchTerm,
            Rarity = ItemRarity.Unique,
            BaseType = searchTerm
        };

        var result = await _tradeApi.PriceCheckAsync(searchItem, league);
        DreamSearchResults.Children.Clear();

        if (result == null || result.Listings.Count == 0)
        {
            DreamSearchResults.Children.Add(new TextBlock
            {
                Text = "No items found",
                FontSize = 10,
                Foreground = (Brush)FindResource("ErrorRed")
            });
            return;
        }

        // For dream build, we take the first result's item data
        // In practice, we'd need the actual item mods from the fetch response
        // For now, show a "Set" button with the item name
        var setBtn = new Button
        {
            Content = $"Set \"{searchTerm}\" as target",
            Style = (Style)FindResource("ActionButton"),
            Tag = searchTerm,
            Margin = new Thickness(0, 4, 0, 0)
        };
        setBtn.Click += (_, _) =>
        {
            if (_activeProfile != null && _dreamSearchSlot != null)
            {
                // Store item data — for now create a placeholder with the search term
                // A full implementation would parse the trade API response for item mods
                var targetItem = new ItemData { Name = searchTerm, BaseType = searchTerm };
                _activeProfile.DreamBuild[_dreamSearchSlot.Value.ToString()] = targetItem;
                _profileManager.Save(_activeProfile);
                BuildDreamBuildGrid();
                DreamSearchPanel.Visibility = Visibility.Collapsed;
            }
        };
        DreamSearchResults.Children.Add(setBtn);
    }
    catch (Exception ex)
    {
        DreamSearchResults.Children.Clear();
        DreamSearchResults.Children.Add(new TextBlock
        {
            Text = $"Search failed: {ex.Message}",
            FontSize = 10,
            Foreground = (Brush)FindResource("ErrorRed"),
            TextWrapping = TextWrapping.Wrap
        });
    }
}

private void DreamClearButton_OnClick(object sender, RoutedEventArgs e)
{
    if (_activeProfile != null && _dreamSearchSlot != null)
    {
        _activeProfile.DreamBuild[_dreamSearchSlot.Value.ToString()] = null;
        _profileManager.Save(_activeProfile);
        BuildDreamBuildGrid();
        DreamSearchPanel.Visibility = Visibility.Collapsed;
        Dispatcher.InvokeAsync(PositionBottomRight, DispatcherPriority.Loaded);
    }
}

private void ProfileComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (ProfileComboBox.SelectedItem is not string selected) return;
    _config.ActiveProfile = selected;
    _activeProfile = _profileManager.Load(selected);
    ArchetypeComboBox.SelectedItem = _activeProfile.Archetype.ToString();
    DisplayModeComboBox.SelectedIndex = (int)_activeProfile.DisplayMode;
    BuildDreamBuildGrid();
}

private void ArchetypeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (!IsLoaded || _activeProfile == null) return;
    if (ArchetypeComboBox.SelectedItem is not string selected) return;
    if (!Enum.TryParse<Archetype>(selected, out var archetype)) return;

    _activeProfile.Archetype = archetype;
    _activeProfile.Weights = new Dictionary<string, int>(ProfileManager.GetPresetWeights(archetype));
    _profileManager.Save(_activeProfile);
}

private void DisplayModeComboBox_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
{
    if (!IsLoaded || _activeProfile == null) return;
    _activeProfile.DisplayMode = (CompareDisplayMode)DisplayModeComboBox.SelectedIndex;
    _profileManager.Save(_activeProfile);
}

private void NewProfileButton_OnClick(object sender, RoutedEventArgs e)
{
    var name = $"Profile {_profileManager.ListProfiles().Count + 1}";
    _profileManager.CreateFromArchetype(name, Archetype.Balanced);
    _config.ActiveProfile = name;
    RefreshProfileList();
}
```

**Step 3: Call InitializeProfileUI in InitializeSettings**

In `InitializeSettings()`, add at the end:
```csharp
InitializeProfileUI();
```

**Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add dream build slot grid, profile management, and item search UI"
```

---

## Phase D: Polish

### Task 13: Add Update Stat Definitions Button

**Files:**
- Modify: `MainWindow.xaml` — add update button to settings
- Modify: `MainWindow.xaml.cs` — add update handler

**Step 1: Add update button to XAML settings**

In `MainWindow.xaml`, in the settings panel, before the Exit button section, add:

```xml
<!-- Update Stat Definitions -->
<Rectangle Height="1" Fill="{StaticResource SeparatorBrush}" Margin="0,12,0,10" />
<DockPanel>
    <Button DockPanel.Dock="Right" Content="Update Now" Style="{StaticResource ActionButton}"
            Click="UpdateStatDefsButton_OnClick" />
    <StackPanel>
        <TextBlock Text="Stat Definitions" FontSize="11"
                   Foreground="{StaticResource BrightText}" />
        <TextBlock x:Name="StatDefsVersionText" FontSize="10"
                   Foreground="{StaticResource DimText}" />
    </StackPanel>
</DockPanel>
```

**Step 2: Initialize version text**

In `InitializeSettings()`, add:
```csharp
StatDefsVersionText.Text = $"v{_statDefService.Version} — Updated: {_statDefService.LastUpdated}";
```

**Step 3: Add update handler**

```csharp
private async void UpdateStatDefsButton_OnClick(object sender, RoutedEventArgs e)
{
    StatDefsVersionText.Text = "Updating...";
    try
    {
        var success = await _statDefService.UpdateFromRemoteAsync();
        if (success)
        {
            _scoringService = new ItemScoringService(_statDefService);
            StatDefsVersionText.Text = $"v{_statDefService.Version} — Updated: {_statDefService.LastUpdated}";
        }
        else
        {
            StatDefsVersionText.Text = "Update failed — using cached version";
        }
    }
    catch (Exception ex)
    {
        StatDefsVersionText.Text = $"Update failed: {ex.Message}";
    }
}
```

**Step 4: Build to verify**

Run: `dotnet build`
Expected: Build succeeded

**Step 5: Commit**

```bash
git add MainWindow.xaml MainWindow.xaml.cs
git commit -m "feat: add stat definitions update button with version display"
```

---

### Task 14: Final Integration Test and Cleanup

**Step 1: Build the entire project**

Run: `dotnet build`
Expected: Build succeeded with no warnings

**Step 2: Run the app in debug mode**

Run: `dotnet run -- --debug`
Expected: Overlay appears at bottom-right. Verify:
- Compare toggle button visible in control bar
- Settings panel shows Account Name, Profile, Archetype, Display Mode, Dream Build grid
- Can create profiles, switch archetypes
- Compare mode shows "Set account name" if account name is empty

**Step 3: Commit any final fixes**

```bash
git add -A
git commit -m "chore: final integration cleanup for item comparison feature"
```

**Step 4: Push to remote**

```bash
git push
```
