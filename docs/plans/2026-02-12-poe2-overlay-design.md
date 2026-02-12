# PoE2 Overlay — Design Document

**Date:** 2026-02-12
**Stack:** WPF (.NET 9) — Windows only
**IDE:** JetBrains Rider

## Core Requirements

- Transparent, frameless, always-on-top ghost window over PoE 2
- Click-through (ignores mouse unless interactive content is shown)
- Clipboard listener for Ctrl+C item capture (configurable hotkey)
- PoE 2 Trade API integration for price checking
- Auto-detect league with manual override
- Settings accessible via gear icon on overlay

## Architecture

### Layers

1. **Overlay Window** — WPF ghost window (transparent, click-through, topmost)
2. **Core Services** — Clipboard monitor, hotkey manager, item parser, settings service
3. **External Integration** — PoE 2 Trade API client, league detection

### Ghost Window (Win32 Interop)

- `WindowStyle=None`, `AllowsTransparency=True`, `Topmost=True`
- `WS_EX_TRANSPARENT` — click-through
- `WS_EX_TOOLWINDOW` — hidden from Alt+Tab
- `WS_EX_NOACTIVATE` — no focus stealing
- Click-through toggled off when tooltip/panel is shown, re-enabled on dismiss

### Item Parsing

Parses PoE 2 clipboard text format into structured data:
- Item class, rarity, name, base type
- Stats (armour, evasion, ES, DPS)
- Item level, explicit/implicit mods
- Unique identification by name

### Trade API Flow

1. Clipboard change detected
2. Item parsed into `ItemData`
3. Query builder creates search payload
4. `POST /api/trade2/search/poe2/{league}` → result IDs
5. Rate limiter checks `X-Rate-Limit` headers
6. `GET /api/trade2/fetch/{ids}` → listings
7. Price aggregation (min, median, average)
8. Tooltip displayed near cursor

### Rate Limiting

- Sliding window tracker per `X-Rate-Limit-Ip` rule
- Pre-emptive delay before hitting limit
- User-visible "rate limited" indicator when throttled

## UI Design

### Theme: Minimal Dark Glass

- Semi-transparent dark panels
- Clean modern typography
- No parchment/game-styled elements

### Tooltip (Default View)

- Item name + base type
- Price range (median)
- Listing count
- Expand button, dismiss button
- Auto-dismiss after 15s (configurable)

### Expanded Panel

- Full price breakdown (min, median, average)
- Individual listings (price, seller, time)
- "Open on Trade Site" button
- Scrollable

### Settings Panel (Gear Icon)

- Hotkey configuration (default: Ctrl+C)
- League selector (auto-detect + override)
- Auto-dismiss timer
- Opacity slider

## Project Roadmap

### Phase 1 — Ghost Window
- Transparent, frameless, always-on-top WPF window
- Click-through via Win32 P/Invoke
- Gear icon visible in corner
- Verify over PoE 2

### Phase 2 — Clipboard & Item Parsing
- Global hotkey registration (configurable)
- Clipboard monitor (`AddClipboardFormatListener`)
- Item text parser
- Basic tooltip showing parsed item info

### Phase 3 — Trade API Integration
- HttpClient wrapper with rate limiting
- League auto-detection + override
- Search query builder
- Price aggregation

### Phase 4 — Full UI
- Dark glass styled tooltip
- Expandable panel with listings
- Settings panel with persistence
- "Open on Trade Site" button

### Phase 5 — Polish & Edge Cases
- Unpriced/unique item handling
- Currency conversions
- Error states (API down, rate limited, no results)
- Auto-update league list
- Optional whisper copy button
