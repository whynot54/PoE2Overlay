# PoE2Overlay

A lightweight, transparent overlay for **Path of Exile 2** that provides real-time price checking using the official trade API. Built with WPF (.NET 9) for minimal resource usage alongside the game.

## Features

- **Ghost Window** — Transparent, always-on-top overlay that doesn't steal focus or interfere with gameplay
- **Click-Through** — Mouse clicks pass through to the game; overlay becomes interactive only when showing results
- **Clipboard Price Check** — Ctrl+C an item in-game and instantly see its market price
- **Trade API Integration** — Queries the official PoE 2 trade site with rate limiting
- **Expandable Listings** — View individual listings with price, seller, and time posted
- **Open on Trade Site** — One-click to open the full search on pathofexile.com
- **Auto-Dismiss** — Tooltip disappears after a configurable timer (pauses when expanded)
- **Settings Panel** — Configure league, opacity, auto-dismiss timer, and POESESSID via gear icon
- **Game-Aware** — Only monitors clipboard when Path of Exile 2 is the foreground window

## Requirements

- Windows 10/11
- [.NET 9 SDK](https://dotnet.microsoft.com/download/dotnet/9.0)
- A Path of Exile account (for POESESSID)

## Setup

1. Clone the repository:
   ```
   git clone https://github.com/whynot54/PoE2Overlay.git
   cd PoE2Overlay
   ```

2. Build:
   ```
   dotnet build
   ```

3. Get your POESESSID:
   - Log in to [pathofexile.com](https://www.pathofexile.com)
   - Open browser DevTools (F12) → Application → Cookies → `www.pathofexile.com`
   - Copy the `POESESSID` cookie value

4. Configure:
   - Run the app once to generate `config.json` in the build output directory
   - Or copy `config.example.json` to `bin/Debug/net9.0-windows/config.json`
   - Set your `poeSessId` value

## Usage

Run the overlay:
```
dotnet run
```

Run in debug mode (monitors clipboard from any app, not just PoE 2):
```
dotnet run -- --debug
```

### In-Game
1. Hover over an item in Path of Exile 2
2. Press **Ctrl+C** to copy the item
3. The overlay shows the item details and fetches the price
4. Click **Expand Listings** to see individual trade listings
5. Click **Open on Trade Site** to view on pathofexile.com
6. Press **Escape** or wait for auto-dismiss to close

### Settings
Click the gear icon (bottom-right) to access:
- **League** — Auto-detect or manually select
- **Auto-dismiss** — Timer duration (5-60 seconds)
- **Opacity** — Tooltip transparency (50-100%)
- **POESESSID** — Update your session ID
- **Exit** — Close the application

## Configuration

`config.json` (located in the build output directory):
```json
{
  "poeSessId": "your_session_id_here",
  "leagueOverride": "",
  "autoDismissSeconds": 15,
  "overlayOpacity": 0.93
}
```

| Field | Description | Default |
|-------|-------------|---------|
| `poeSessId` | Your pathofexile.com session cookie | `""` |
| `leagueOverride` | Force a specific league (empty = auto-detect) | `""` |
| `autoDismissSeconds` | Seconds before tooltip auto-closes | `15` |
| `overlayOpacity` | Tooltip panel opacity (0.5 - 1.0) | `0.93` |

## Project Structure

```
PoE2Overlay/
├── App.xaml / App.xaml.cs           # Application entry, --debug flag handling
├── MainWindow.xaml / .xaml.cs       # Overlay window, UI, event handling
├── Models/
│   ├── AppConfig.cs                 # Configuration model
│   ├── ItemData.cs                  # Parsed item data model
│   └── PriceResult.cs               # Price check result model
├── Services/
│   ├── ClipboardMonitor.cs          # Win32 clipboard listener
│   ├── ConfigService.cs             # JSON config load/save
│   ├── GameWindowDetector.cs        # Detects if PoE 2 is foreground
│   ├── ItemParser.cs                # Parses PoE 2 clipboard item text
│   ├── LeagueService.cs             # Fetches and manages league data
│   ├── NativeMethods.cs             # Win32 P/Invoke declarations
│   ├── RateLimiter.cs               # Trade API rate limiter
│   ├── TradeApiClient.cs            # PoE 2 trade API client
│   └── TradeQueryBuilder.cs         # Builds trade search queries
├── config.example.json              # Example configuration
└── docs/plans/                      # Design documents
```

## Tech Stack

- **WPF (.NET 9)** — Native Windows UI with low overhead
- **Win32 Interop** — Ghost window via `WS_EX_TRANSPARENT`, `WS_EX_TOOLWINDOW`, `WS_EX_NOACTIVATE`
- **PoE 2 Trade API** — Official GGG trade endpoints with rate limiting

## Security

- `config.json` is gitignored — your POESESSID is never committed
- POESESSID is masked in the settings UI
- The overlay only reads the clipboard; it never modifies game files or memory

## License

MIT
