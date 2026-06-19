# My Radio Player

A simple Windows desktop radio player for online stations. Paste a station's web page URL, and the app automatically discovers the underlying audio stream and saves the station to your library. Playback is handled by VLC under the hood, so most common stream formats are supported out of the box.

![Main window](docs/screenshots/main-window.png)

---

## Table of Contents

- [Features](#features)
- [For users](#for-users)
  - [Installation](#installation)
  - [Adding a station](#adding-a-station)
  - [Playing, pausing, switching](#playing-pausing-switching)
  - [Editing and deleting stations](#editing-and-deleting-stations)
  - [Sorting the station list](#sorting-the-station-list)
  - [Reordering by drag & drop](#reordering-by-drag--drop)
- [For advanced users / developers](#for-advanced-users--developers)
  - [Architecture overview](#architecture-overview)
  - [Project layout](#project-layout)
  - [Building from source](#building-from-source)
  - [Building the Python parser exe](#building-the-python-parser-exe)
  - [Finding a Stream URL manually](#finding-a-stream-url-manually)
  - [Known limitations](#known-limitations)

---

## Features

- One-click station discovery from a regular radio page URL
- Automatic enrichment of station name, description, and genre from page metadata and ICY headers
- VLC-backed playback (LibVLCSharp) — no separate VLC install needed
- Persistent station library (SQLite) with play counts
- Drag & drop reordering, sort by name or play count
- Context menu (right-click) on stations for Play / Edit / Delete
- Pause / Resume / Next / Previous controls, volume slider

---

## For users

### Installation

Currently the easiest way is to build from source — see [Building from source](#building-from-source). Pre-built releases will be published on GitHub Releases later.

You don't need to install VLC separately. The app ships with the LibVLC native libraries it needs.

**Requirements:** Windows 10 or 11, .NET Framework 4.8 (already installed on most modern Windows systems).

### Adding a station

1. Click the **+** button at the bottom of the main window.
2. Paste the **Radio page URL** — the regular web page of the station in your browser. For example: `https://onlineradiobox.com/mx/radioranchito/`.
3. Click **Find stream URL**. The app will fetch the page, analyse it, and try to extract the direct audio stream.
4. When detection finishes, the form auto-fills:
   - **Title** — station name from page metadata or ICY headers
   - **Stream URL** — direct audio endpoint
   - **Description** — human-readable text from the page (Open Graph tags, `<meta name="description">`, etc.), plus technical parser info (content type, extraction method, alternative candidates if any)
5. Edit any field if needed, then click **Add**.

![Add station window with results](docs/screenshots/add-station-filled.png)

The **Timeout, s** field controls how long the parser is allowed to work. Default is 60 seconds; the maximum is **999 seconds**. Increase it for slow sites with heavy JavaScript or ad loading; reduce it if you just want a quick check.

If detection finds multiple stream candidates, the best one is placed in the Stream URL field and the rest are listed under "Also possible stream candidates:" in the Description. You can manually pick a different one if the first doesn't play.

If the parser fails or returns the wrong URL, see [Finding a Stream URL manually](#finding-a-stream-url-manually) — there's a built-in help button (**?**) next to the Stream URL field that opens the same guide.

### Playing, pausing, switching

- **Double-click** a station to start playing it.
- The **▶ / ⏸** button toggles play/pause for the currently selected station.
- **⏮ / ⏭** switch to the previous / next station in the list (and start playback automatically).
- The volume slider with the speaker icon controls volume from 0% to 100%.
- The "Now playing:" label shows the currently active station and switches to "Paused:" when paused.

### Editing and deleting stations

- Click the **pencil** button (or right-click → **Edit**) to open the same Add Station form pre-filled with the station's current values. Make changes and click **Add** to save.
- Click the **−** button (or right-click → **Delete**) to remove a station. The app asks for confirmation before deleting.

![Context menu](docs/screenshots/context-menu.png)

### Sorting the station list

Right-click anywhere in the list and choose **Sort by...** → **Name** or **Play count** → **Ascending** or **Descending**. The order is persisted in the database, so the next time you open the app the stations stay where you put them.

### Reordering by drag & drop

Click and hold a station, then drag it up or down to a new position. The new order is saved automatically.

---

## For advanced users / developers

### Architecture overview

The project is a Windows desktop app (WPF, .NET Framework 4.8) that delegates stream discovery to an external Python parser bundled as a standalone exe.

```
┌─────────────────────┐
│  RadioApp (C# WPF)  │
│                     │
│  ┌───────────────┐  │
│  │  MainWindow   │  │
│  │  AddStation   │  │
│  └───────┬───────┘  │
│          │          │
│  ┌───────▼───────┐  │      ┌──────────────────────┐
│  │ PythonStream  │──┼─────▶│  stream_parser.exe   │
│  │  Discovery    │  │ JSON │  (Python + Playwright)│
│  │  Service      │◀─┼──────│                      │
│  └───────────────┘  │      └──────────────────────┘
│                     │
│  ┌───────────────┐  │
│  │ VlcPlayback   │──┼─────▶  LibVLC (native)
│  │  Service      │  │
│  └───────────────┘  │
│                     │
│  ┌───────────────┐  │
│  │ RadioDatabase │──┼─────▶  SQLite (radio.db)
│  │  Service      │  │
│  └───────────────┘  │
└─────────────────────┘
```

**Why an external Python parser?** Modern radio sites are heavy on JavaScript, consent overlays, and dynamic stream URLs. A static C# HTTP scraper can't handle them. The Python parser uses Playwright (headless Chromium) to actually load the page, dismiss consent dialogs, click the play button, observe the network traffic, and capture the real stream URL. Bundling it as an exe means the C# app doesn't need a Python runtime.

**Key C# services in `RadioApp/Services/`:**

| Service | Purpose |
|---|---|
| `PythonStreamDiscoveryService` | Spawns `stream_parser.exe`, parses its JSON output, writes stderr to a log file next to the exe |
| `VlcPlaybackService` | Wraps LibVLCSharp. VLC handles HTTP redirects natively, so no extra URL resolution layer is needed |
| `RadioStreamInfoService` | ICY metadata fallback for enriching station info when the page itself doesn't expose it |
| `RadioDatabaseService` | SQLite + Entity Framework. Add/update/delete stations, track play counts, persist sort order |

**On the Python side**, the parser pipeline is:

1. `GenericStaticExtractor` — fast static HTML scan, looks for `<audio>` tags, JSON-LD, playlist files, common patterns. Sufficient for ~70% of sites.
2. `BrowserNetworkExtractor` — Playwright-based fallback. Loads the page in Chromium, dismisses consent overlays in 36+ languages, hooks fetch/XHR, scans iframes, clicks Play buttons identified by multilingual keywords ("play", "слушать", "reproducir", "écouter", etc.).
3. `StreamValidator` — HEAD-checks each candidate URL to verify it actually returns audio.

### Project layout

```
StreamURL_Parser/                       repository root
├── RadioApp/                           WPF desktop app
│   ├── Data/                           EF DbContext, SQLite migration
│   ├── Models/                         MediaItem, DiscoveredRadioStream, etc.
│   ├── Services/
│   │   ├── PythonStreamDiscoveryService.cs
│   │   ├── VlcPlaybackService.cs
│   │   ├── RadioDatabaseService.cs
│   │   └── RadioStreamInfoService.cs
│   ├── MainWindow.xaml(.cs)
│   ├── AddStationWindow.xaml(.cs)
│   └── bin/Release/
│       └── stream_parser/              the bundled Python exe + Playwright runtime
│           ├── stream_parser.exe
│           └── ms-playwright/
└── stream_parser/                      Python parser source
    ├── main.py                         CLI entry point
    ├── extractors/
    │   ├── generic_static.py
    │   ├── browser_network.py
    │   └── ...
    ├── validators/
    └── platforms/                      site-specific URL generators
```

### Building from source

**Requirements:**
- Visual Studio 2022 (or 2019) with the **.NET desktop development** workload
- .NET Framework 4.8 targeting pack
- Python 3.9+ with a virtual environment (only if you want to rebuild the parser exe)

**Steps:**

1. Clone the repo:
   ```
   git clone https://github.com/Vitali-Yelizarau/StreamURL_Parser.git
   cd StreamURL_Parser
   ```
2. Open `RadioApp/RadioApp.sln` in Visual Studio.
3. Restore NuGet packages (usually automatic on build).
4. Build the solution in **Release** configuration. The output goes to `RadioApp/bin/Release/`.
5. Make sure the `stream_parser/` subfolder (with `stream_parser.exe` and the bundled `ms-playwright/` Chromium) is present in `bin/Release/`. If you're cloning fresh, you'll need to build the parser exe first — see the next section.
6. Run `RadioApp.exe`.

### Building the Python parser exe

If you change anything in the `stream_parser/` Python sources you need to rebuild the bundled exe.

```powershell
cd D:\Projects\New\StreamURL_Parser

# Clean Python caches first
Get-ChildItem -Recurse -Filter "__pycache__" | Remove-Item -Recurse -Force
Get-ChildItem -Recurse -Filter "*.pyc" | Remove-Item -Force

# Build the exe with PyInstaller
.\.venv\Scripts\pyinstaller --onedir --name stream_parser --noconfirm --collect-all playwright stream_parser\main.py

# Bundle the Chromium runtime Playwright needs
Copy-Item "$env:LOCALAPPDATA\ms-playwright\chromium-1148" -Destination "dist\stream_parser\ms-playwright\chromium-1148" -Recurse

# Copy everything next to RadioApp.exe
Copy-Item "dist\stream_parser\*" -Destination "D:\Projects\New\RadioApp\bin\Release\stream_parser\" -Recurse -Force
```

If PowerShell blocks script execution, either bypass it for the session:
```powershell
Set-ExecutionPolicy -Scope Process -ExecutionPolicy Bypass
```
…or run the commands directly inline as shown above.

### Finding a Stream URL manually

If the parser can't extract a stream from a particular site (see [Known limitations](#known-limitations) below), you can find the stream URL yourself using browser DevTools. The same guide is built into the app — click the **?** button next to the Stream URL field on the Add Station window.

![Stream URL help dialog](docs/screenshots/stream-url-help.png)

1. Open the station's page in Chrome, Edge or Firefox.
2. Press **F12** to open DevTools and switch to the **Network** tab.
3. Enable the **Media** filter and tick **Preserve log**.
4. Click the **Play** button on the station's page.
5. Find the new network entry whose content type is `audio/mpeg`, `audio/aac`, `audio/aacp`, or whose URL ends with `.mp3`, `.aac`, `.m3u8`.
6. Right-click that entry → **Copy** → **Copy URL**.
7. Paste the URL into the **Stream URL** field in My Radio Player.

**Tips:**
- Some sites redirect through intermediate URLs. Use the final URL from the redirect chain (the one that actually serves the audio bytes).
- Icecast streams sometimes have trailing path suffixes like `/stream`, `/listen`, `/;stream.mp3` — keep these in the URL exactly as they appear.
- If you see HLS playlists (`.m3u8`), VLC will play them fine.

### Known limitations

The parser is universal and covers most online radio sites, but some platforms are designed in ways that make static or even browser-driven extraction unreliable. Currently known cases:

- **CMP-gated sites** (`radio.de`, `radio.net`) — heavy consent management combined with a Next.js SPA that generates stream URLs at runtime from the page slug. Sometimes works, sometimes the player doesn't initialise after consent dismissal.
- **SoCast-based stations** (e.g. `thezone.fm`) — stream URLs are fetched via an authenticated API call with per-session parameters. Cannot be discovered statically.
- **Dynamic JS Play buttons** (e.g. `jungletrain.net`) — Play button is rendered after JS execution and points to a proxy URL VLC won't accept.
- **AppMind + Publift + Zenolive stack** (e.g. `radio-en-vivo.mx`) — page loads dozens of ad iframes (Publift OpenRTB auctions, QuantCast Choice CMP, etc.) which slow Chromium so much that the actual stream request from Zenolive never completes within the timeout window.

For all of these, the recommended workaround is:

1. Try the same station on `onlineradiobox.com` — that aggregator works well with the parser.
2. Otherwise, find the stream URL manually via DevTools (see above).