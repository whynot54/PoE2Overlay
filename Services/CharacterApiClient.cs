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
