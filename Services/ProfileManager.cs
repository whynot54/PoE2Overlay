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
        var safe = string.Join("_", profileName.Split(Path.GetInvalidFileNameChars()));
        return Path.Combine(ProfilesDir, $"{safe}.json");
    }
}
