# Item Comparison & Weighted Scoring System — Design Document

**Date:** 2026-02-13
**Branch:** `feature/item-comparison-scoring`
**Status:** Approved

---

## Overview

A weighted scoring system integrated into the existing PoE2Overlay that helps players determine if a dropped item is better than their currently equipped gear. Supports configurable weight profiles, archetype presets, dream build targets, and multiple display modes.

---

## Core Concepts

### Toggle Mode
The overlay switches between **Price Check** mode (existing) and **Compare** mode via a toggle button. Only one mode is active at a time.

### Comparison Targets
When in Compare mode, a Ctrl+C'd item is scored against:
1. **Currently equipped item** in the same slot (fetched from Character API)
2. **Dream build target** for that slot (if the player has set one)

### Weighted Scoring
Each stat mod is categorized and scored. The player assigns weight (0-10) to each category via sliders. The final score is:
```
ItemScore = Σ (normalized_stat_value × category_weight)
```

---

## Architecture

### New Components

```
Services/
├── CharacterApiClient.cs      # Fetch equipped gear via POESESSID
├── ItemScoringService.cs      # Scoring engine: normalize, weight, compare
├── ProfileManager.cs          # CRUD for weight profiles + persistence
├── StatDefinitionService.cs   # Load/update stat definitions JSON
└── DreamBuildManager.cs       # Manage dream build targets per profile

Models/
├── WeightProfile.cs           # Profile: name, archetype, weights, dream build, display mode
├── StatDefinition.cs          # Stat pattern → category mapping
├── ComparisonResult.cs        # Scored comparison output
└── EquipmentSlot.cs           # Enum for equipment slot types
```

### Data Flow

**Entering Compare mode:**
```
Toggle Compare → CharacterApiClient fetches equipped gear
  → ItemParser parses each item into ItemData
  → Cached in memory, indexed by slot
  → Status: "Compare ready - N slots loaded"
```

**Comparing an item:**
```
Ctrl+C item → ItemParser parses clipboard
  → Detect slot from Item Class
  → Fetch equipped item + dream target for that slot
  → ItemScoringService.Compare(dropped, equipped, dreamTarget, weights)
  → ComparisonResult rendered in chosen display mode
```

---

## Stat Categories & Weights

| Category | Example Stats | Default (Balanced) |
|----------|--------------|-------------------|
| Life & ES | +max life, +max ES, %increased life | 7 |
| Elemental Resists | +fire/cold/lightning res, +all res | 7 |
| Chaos Resist | +chaos res | 4 |
| Physical Defense | +armour, +evasion, %increased armour | 5 |
| Spell Damage | %spell damage, %ele damage, +gem levels | 5 |
| Attack Damage | %physical damage, +flat phys, %melee damage | 5 |
| Speed | %cast speed, %attack speed, %movement speed | 6 |
| Critical | %crit chance, %crit multiplier | 4 |
| Mana & Regen | +mana, %mana regen, -mana cost | 3 |
| Utility | +attributes (str/dex/int), item rarity/quantity | 2 |

### Archetype Presets

| Category | Spell Caster | Attack Melee | Attack Ranged | Summoner | Tank | Balanced |
|----------|-------------|-------------|--------------|----------|------|----------|
| Life & ES | 8 | 9 | 7 | 8 | 10 | 7 |
| Ele Resists | 7 | 8 | 7 | 9 | 10 | 7 |
| Chaos Resist | 4 | 5 | 3 | 5 | 7 | 4 |
| Physical Defense | 3 | 8 | 4 | 5 | 10 | 5 |
| Spell Damage | 10 | 1 | 1 | 3 | 1 | 5 |
| Attack Damage | 1 | 10 | 10 | 1 | 2 | 5 |
| Speed | 8 | 6 | 7 | 4 | 3 | 6 |
| Critical | 6 | 5 | 8 | 2 | 1 | 4 |
| Mana & Regen | 6 | 2 | 2 | 4 | 3 | 3 |
| Utility | 3 | 4 | 3 | 5 | 4 | 2 |

---

## Stat Definitions (Data-Driven)

Stat-to-category mapping is stored in `stat-definitions.json`, loaded at startup, and updatable via a button in settings.

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
        { "regex": "\\+\\d+ to maximum Energy Shield", "type": "flat" }
      ]
    }
  }
}
```

**Update mechanism:**
- Hosted on GitHub (raw JSON URL)
- "Update Stat Definitions" button in settings fetches latest version
- Staleness indicator shows days since last update
- Falls back to bundled version if network unavailable

---

## Weight Profiles

### Structure

```json
{
  "profileName": "Fire Sorc League Start",
  "archetype": "SpellCaster",
  "weights": {
    "lifeAndES": 8,
    "eleResist": 9,
    "chaosResist": 4,
    "physicalDefense": 3,
    "spellDamage": 10,
    "attackDamage": 1,
    "speed": 8,
    "critical": 6,
    "manaAndRegen": 6,
    "utility": 3
  },
  "dreamBuild": {
    "helmet": { "name": "Crown of the Inward Eye", "baseType": "...", "mods": [...] },
    "chest": null,
    "gloves": null,
    "boots": null,
    "belt": null,
    "amulet": null,
    "ring1": null,
    "ring2": null,
    "weapon": null,
    "offhand": null
  },
  "displayMode": "CategoryBreakdown"
}
```

### Storage
- Profiles saved as individual JSON files in a `profiles/` directory
- Active profile name stored in `config.json`
- Multiple profiles supported — player names them, switches freely

---

## Dream Build System

### Setting Target Items
1. Player opens profile settings → dream build grid
2. Clicks an equipment slot → search popup appears
3. Types item name → Trade API search returns results
4. Player picks an item → full `ItemData` saved to profile
5. Slot can be cleared or overwritten at any time

### Comparison Output
- Only slots with a dream target show the dream comparison
- Displayed as percentage: "86% of target" with a progress bar
- Empty dream slots only compare vs equipped

---

## Display Modes

Configurable per profile in settings. Player can switch at any time.

### Simple (Score + Verdict)
```
▲ UPGRADE  Score: 82 vs 65
vs Dream: 86% of target
```

### Category Breakdown
```
▲ UPGRADE  Score: 82 vs 65
Life & ES:     +15  ████░░
Ele Resists:    -5  ███░░░
Spell Damage:  +20  █████░
vs Dream: 86% of target
```

### Full Stat Diff
```
▲ UPGRADE  Score: 82 vs 65
Life & ES:     +15  ████░░
Ele Resists:    -5  ███░░░
Spell Damage:  +20  █████░
───────────────────────────
+52 to max Life
-10% Cold Resistance
+28% Spell Damage
+15% Cast Speed
vs Dream: 86% of target
```

### Visual Indicators
- Green ▲ = Upgrade, Red ▼ = Downgrade, Yellow ━ = Sidegrade
- Green/red coloring on individual stat gain/loss lines
- "?" icon on unrecognized mods (not scored but displayed)

---

## UI Changes

### Overlay
- **Mode toggle** button next to existing Play/Stop (Price ↔ Compare)
- **Comparison tooltip** replaces price tooltip when in Compare mode
- Visual indicator showing which mode is active

### Settings Panel (new sections)
- **Account name** field (required for Character API)
- **Profile management**: dropdown (create/rename/delete)
- **Archetype selector**: sets default weights on profile creation
- **Weight sliders**: one per stat category (0-10 scale)
- **Dream build grid**: clickable slots → trade API search popup
- **Display mode selector**: Simple / Category / Full
- **Update Stat Definitions** button with last-updated indicator

---

## Authentication

### Phase 1: POESESSID (now)
- Reuse existing POESESSID from config
- Add account name field to settings
- Character API calls use POESESSID cookie header

### Phase 2: OAuth (future)
- Register developer app on pathofexile.com/developer
- OAuth2 flow: browser auth → callback → token exchange
- Access token + refresh token stored securely
- Scoped to `account:characters`
- Replaces POESESSID for character API (and optionally trade API)

---

## Error Handling

| Scenario | Handling |
|----------|----------|
| POESESSID expired/invalid | "Session expired - update in settings", fall back to Price Check |
| Character API rate limited | "API busy - try again shortly", keep cached gear if available |
| Item slot not detected | "Can't determine slot", display parsed stats without comparison |
| Unknown mods in item | Score known mods, flag unknown with "?" icon |
| No equipped item in slot | Compare against dream target only |
| No dream target for slot | Skip dream comparison, show vs equipped only |
| No profile selected | Prompt to create one in settings |
| Stat definitions outdated | Subtle "Last updated: N days ago" indicator |
| Network unavailable for stat update | Keep bundled/cached version, show error toast |

---

## Implementation Phases

### Phase A: Foundation
- `StatDefinitionService` — load/update stat definitions JSON
- `WeightProfile` model + `ProfileManager` (CRUD + persistence)
- Weight slider UI in settings panel
- Archetype presets

### Phase B: Character API + Compare Core
- `CharacterApiClient` — fetch equipped gear via POESESSID
- Account name field in settings
- Compare/Price toggle button on overlay
- `ItemScoringService` — scoring engine with normalization + weighted comparison
- Comparison tooltip (Simple display mode)

### Phase C: Dream Build
- Dream build slot grid in profile settings
- Trade API item search for selecting targets
- Store/load dream build items in profile
- Dream build comparison in tooltip (vs equipped + vs target)

### Phase D: Polish
- Category breakdown display mode
- Full stat diff display mode
- Display mode selector in settings
- Unknown mod flagging with "?" icon
- Stat definitions staleness indicator + update button

### Phase E: OAuth (future)
- GGG developer app registration
- OAuth2 flow (browser → token exchange → refresh)
- Replace POESESSID for character API
