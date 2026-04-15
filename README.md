<h1><img src="PKHeX.Mobile/Resources/AppIcon/appicon.png" width="48" valign="middle" style="margin-right:10px"/> PKSafe</h1>

**PKSafe** is a Pokémon save editor for Android, built on top of [PKHeX](https://github.com/kwsch/PKHeX) — the most complete Pokémon core-series save editor available.

![License](https://img.shields.io/badge/License-GPLv3-blue.svg)

> **Download:** see the [Releases](../../releases) page for the latest APK.

---

## Features

### Save Management
- Load save files from any folder or individual file on your device via Android's Storage Access Framework (SAF)
- Write changes back to the original file in-place — no copy-export needed
- Manage multiple watched folders and individual files from the folder manager
- Supports all major save formats: GB/GBC (Gen 1–2), GBA (Gen 3), NDS (Gen 4–5), 3DS (Gen 6–7), Switch (Gen 8–9), and more
- Cross-fade hero card animation when scrolling between saves on the home screen
- **Emulator save auto-detection** — automatically finds Pokémon saves from:
  - **Eden** (Switch Gen 8–9): grant access to your Eden folder and the app scans `nand/user/save/` automatically
  - **MelonDS** (DS Gen 4–5): point to your ROM or saves folder; `.sav` files are picked up automatically
  - **Azahar** (3DS Gen 6–7): scans `sdmc/Nintendo 3DS/` automatically for save files
  - **RetroArch** (GBA/GBC Gen 1–3): point to your saves folder; `.srm` files are picked up automatically

### Box Browser
- Full box grid rendered with SkiaSharp — fast and smooth on any Android device
- High-quality Pokémon HOME sprites loaded on demand and cached for offline use — form-aware (Vivillon patterns, Rotom appliances, alternate formes, gender variants, and more)
- Animated Pokémon Showdown sprites, also cached for offline use
- Bounce/spring animation when the cursor lands on a new slot
- Slide transition between boxes (left/right)
- Navigate boxes with on-screen buttons or a connected gamepad (L/R shoulder buttons)
- Cursor highlight and slot selection fully gamepad-driven
- Optional legality badge — green/red dot on each slot showing at a glance whether a Pokémon is legal
- Game-specific icons for each save (Ruby, Sapphire, Emerald, FireRed, LeafGreen, Platinum, and more)

### Pokémon Preview
- Animated 3D sprite display pulled from the Pokémon Showdown CDN — form-aware (e.g. Giratina-Origin, Meloetta-Pirouette, Vivillon variants)
- Sprites cached to disk after first load for fully offline reuse
- Shiny variants displayed automatically
- Hexagonal stat radar chart with smooth morph animation when switching Pokémon
- Species name, level, types, ability, held item, and moves shown at a glance
- Legality indicator — instantly see if a Pokémon is legal

### Pokémon Editor
- Full PKM editor: species, nickname, OT, moves, stats, IVs, EVs, nature, ability, ball, and more
- Ten display languages: Japanese, English, French, Italian, German, Spanish, Spanish (LATAM), Korean, Simplified Chinese, Traditional Chinese

### Pokémon Database
- Search across all boxes in the active save by species name
- Filter by shiny
- Tap any result to open it directly in the editor

### Mystery Gifts
- Browse the full built-in Mystery Gift database
- Filter by title, species, or compatibility with the active save
- Inject any compatible gift into the first available box slot

### Universal Pokédex
- Grid of all 1,025 base species rendered with SkiaSharp — virtual scroll keeps it smooth regardless of dex size
- Species appear as silhouettes until unlocked; scan any loaded save to mark every Pokémon you own as collected (additive — unlocks are never revoked)
- Each slot shows a type-tinted card with the species' type colour, Pokémon HOME sprite, and dex number
- Footer bar shows the selected species' name, number, and type pill(s)
- Completion stats per generation displayed on the second screen (AYN Thor)
- L1/R1 jumps between generations (Kanto → Johto → … → Paldea)
- Full gamepad navigation; Select scans the active save

### In-App Pokémon Bank *(in progress)*
- Cross-save storage for transferring Pokémon between different saves
- Drag-and-deposit workflow from the box browser

### First-Run Welcome Experience
- Animated intro reel plays on first launch with a floating Pokémon sprite per feature slide
- Per-slide radial glow, staggered bullet points, and an animated progress bar
- Finale: Pokémon HOME sprites fly in from the screen edges and form a 5×4 grid, which zooms out to reveal the PKSafe logo card
- Background orchestral Pokémon theme music fades out as the wizard begins
- Tap anywhere or hit Skip to jump straight to setup

### Settings
- **Display Language** — affects species, move, and ability names throughout the app
- **Shiny Sprites** — toggle alternate-colour sprites for shiny Pokémon
- **Adaptive Radar Scale** — scale the stat chart to the Pokémon's best stat so weaker Pokémon fill more of the graph (off = fixed 0–255 scale)
- **Legality Badges** — show a green/red dot on each Pokémon in the box view
- **Light / Dark Theme** — instant live switching; no restart required
- **Manage Save Folders/Files** — add or remove folders and individual files scanned on startup
- **Download HOME Sprites** — bulk-download and cache all Pokémon HOME-style sprites (~120 MB) for offline use
- **Download Animated Sprites** — bulk-download and cache all animated GIF sprites (~200 MB) for offline use
- **Emulator Save Finders** — one-tap auto-detection for Eden, MelonDS, Azahar, and RetroArch saves

### Gamepad Support
- Full D-pad navigation on the home screen, box browser, box editor, settings, database, and mystery gift pages
- A/B/X/Y and shoulder buttons mapped throughout the app
- L1/R1 to switch boxes; R3 to play the Pokémon cry for the current slot
- Animated dot-grid background on the hero card panel

### Dual-Screen Support *(AYN Thor and compatible devices)*
- Second display shows the box grid independently
- Cursor position and box state kept in sync across both screens

---

## Screenshots

*Coming soon.*

---

## Building

PKSafe is a .NET MAUI application targeting Android, built with .NET 10.

```bash
# Build the Android APK
dotnet build PKHeX.Mobile/PKHeX.Mobile.csproj -f net10.0-android -c Release

# Or build the full solution (Core + Mobile)
dotnet build PKHeX.sln
```

CI automatically publishes a signed APK to [GitHub Releases](../../releases) on every push to `master`.

### Requirements
- .NET 10 SDK with MAUI workload (`dotnet workload install maui-android`)
- Android SDK (API 26+)

---

## Architecture

```
PKHeX.Core/           # Core library — all game logic, legality, save parsing (no GUI deps)
PKHeX.Mobile/         # .NET MAUI Android app (PKSafe)
  Pages/              # ContentPages: MainPage, GamePage, SettingsPage, FolderManagerPage, ...
  Services/           # IFileService, SaveDirectoryService, SpriteCacheService, HomeSpriteCacheService, EmulatorSaveFinderService, ...
  Platforms/Android/  # Android-specific: MainActivity, AndroidFilePicker, GamepadRouter
PKHeX.Drawing.Mobile/ # SkiaSharp sprite rendering helpers
PKHeX.WinForms/       # Original Windows desktop app (unchanged)
```

PKHeX.Core is used directly as a project reference — no modifications. All Android-specific code lives in `PKHeX.Mobile`.

---

## Disclaimer

**We do not support or condone cheating at the expense of others. Do not use significantly hacked Pokémon in battle or in trades with those who are unaware hacked Pokémon are in use.**

This project is not affiliated with Nintendo, Game Freak, or The Pokémon Company.

---

## Credits

- **[PKHeX](https://github.com/kwsch/PKHeX)** by kwsch — core save editing engine (GPLv3)
- **[PokeAPI Sprites](https://github.com/PokeAPI/sprites)** — Pokémon HOME-style sprites (CC0)
- **[Pokémon Showdown](https://pokemonshowdown.com)** — animated sprite CDN
- **[pokesprite](https://github.com/msikma/pokesprite)** — shiny sprite collection (MIT)
- **[QRCoder](https://github.com/codebude/QRCoder)** — QR code generation (MIT)
- **[SkiaSharp](https://github.com/mono/SkiaSharp)** — 2D graphics for MAUI
- **[Phosphor Icons](https://phosphoricons.com)** — icon font used throughout the UI (MIT)
- Pokémon Legends: Arceus sprites from the [National Pokédex Icon Dex](https://www.deviantart.com/pikafan2000/art/National-Pokedex-Version-Delta-Icon-Dex-824897934) project
- **Welcome screen music:** "Pokémon: Red & Blue Title Theme (Fanmade Orchestration)" by [MawsMash](https://www.youtube.com/watch?v=qSGsAJFeLas) — used for atmosphere in the first-run welcome screen. Original composition © Game Freak / Nintendo / The Pokémon Company.
