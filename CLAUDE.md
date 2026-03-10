# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

PKHeX is a Pokémon core series save editor written in C# targeting .NET 10. It supports save files, individual Pokémon entity files (`.pk*`), Mystery Gift files, and cross-generation transfers.

## Build Commands

```bash
# Build the solution
dotnet build PKHeX.sln

# Build in Release mode
dotnet build PKHeX.sln -c Release

# Run tests (all)
dotnet test Tests/PKHeX.Core.Tests/PKHeX.Core.Tests.csproj

# Run a specific test class
dotnet test Tests/PKHeX.Core.Tests/ --filter "ClassName=PKHeX.Tests.Legality.LegalityTests"

# Run tests matching a name pattern
dotnet test Tests/PKHeX.Core.Tests/ --filter "Name~Legality"
```

The executable is `PKHeX.WinForms` (Windows Forms, requires Windows or compatible environment). The test project uses xUnit + FluentAssertions.

## Code Style

- Spaces instead of tabs (standard C# Visual Studio formatting)
- C# 14 language features enabled; nullable reference types enabled (`<Nullable>enable</Nullable>`)
- Debug builds treat warnings as errors (`<TreatWarningsAsErrors>true</TreatWarningsAsErrors>`)
- GUI logic must be kept separate from non-GUI logic (non-GUI in `PKHeX.Core`, GUI in `PKHeX.WinForms`)

## Project Structure

```
PKHeX.Core/          # Core library (no GUI dependencies) — also published as a NuGet package
PKHeX.WinForms/      # Windows Forms GUI application
PKHeX.Drawing/       # Image/color utilities
PKHeX.Drawing.PokeSprite/  # Pokémon sprite resources and builders
PKHeX.Drawing.Misc/  # QR code generation and misc drawing
Tests/PKHeX.Core.Tests/    # xUnit test suite for PKHeX.Core
```

## Architecture: PKHeX.Core

### PKM Entity Hierarchy

`PKM` (`PKHeX.Core/PKM/PKM.cs`) is the abstract base for all Pokémon entity formats. Concrete subclasses are named by format: `PK1`, `PK2`, `PK3`, ..., `PK9`, plus variants like `PB7` (Let's Go), `PA8` (Legends: Arceus), `PB8` (BDSP), `PK9`/`PA9` (Scarlet/Violet), `CK3`/`XK3` (GameCube), `BK4`/`RK4` (Battle Revolution), `SK2` (Stadium), etc.

Each `PKM` stores raw bytes in a `Memory<byte>` field and exposes typed properties over them.

### SaveFile Hierarchy

`SaveFile` (`PKHeX.Core/Saves/SaveFile.cs`) is the abstract base for all save file formats. Concrete subclasses are named by game: `SAV1`, `SAV2`, ..., `SAV9SV`, with variants per game version (e.g., `SAV4DP`, `SAV4Pt`, `SAV4HGSS`). Save block access is handled via `ISaveBlockAccessor<T>` interfaces and corresponding `SaveBlockAccessor*` implementations.

### Legality System

The legality system checks whether a Pokémon's data is legal/obtainable in-game:

- **`LegalityAnalysis`** (`Legality/LegalityAnalysis.cs`) — entry point; runs all verifiers against a `PKM` and stores `CheckResult` list
- **`LegalityAnalyzers`** (`Legality/LegalityAnalyzers.cs`) — static collection of all `Verifier` instances
- **Verifiers** (`Legality/Verifiers/`) — individual check classes (one per concern: `NicknameVerifier`, `BallVerifier`, `PIDVerifier`, `MoveVerifier`, etc.)
- **Encounter matching** — the system finds which in-game encounter the Pokémon could have originated from:
  - `EncounterGenerator*.cs` files (one per generation) in `Legality/Encounters/Generator/ByGeneration/`
  - Encounter templates in `Legality/Encounters/Templates/` (organized by `Gen1/`–`Gen9/`, `GO/`, `Shared/`)
  - Encounter data tables in `Legality/Encounters/Data/` (per generation)
- **RNG validation** (`Legality/RNG/`) — verifies PID/IV generation via Gen3–5 RNG algorithms
- **Move learn validation** (`Legality/LearnSource/`) — checks if a Pokémon can learn its moves via level-up, TM, tutor, egg, etc.
- **`Legal.cs`** — static constants for max species/move/item IDs per generation

### Mystery Gifts

Mystery Gift formats (`MysteryGifts/`) include `WC6`, `WC7`, `WC8`, `WC9`, `WA8`, `WA9`, `WB7`, `WB8`, `WC5Full`, `PGT`, `PGF`, `PCD`, etc. Each implements `MysteryGift` base and can be converted to a `PKM`.

### Game Strings & Localization

Game strings are in `Game/GameStrings/`. The `GameStrings` class loads localized string tables (species names, move names, item names, etc.) for a given language. UI translation uses external resource files.

### Key Constants

- `GameVersion` enum — all game versions (used throughout for game-specific logic)
- `EntityContext` enum — identifies the format/context of a PKM (e.g., `Gen9`, `Gen8a`)
- `Species` enum — Pokémon species IDs
- `Legal` static class — per-generation limits (max species, moves, items, balls)

## Key Conventions

- Encounter templates model game data directly to match how the game handles encounters
- When adding a new game generation's data, follow the pattern of existing `Gen*` folders in `Encounters/Templates/`, `Encounters/Data/`, `Encounters/Generator/ByGeneration/`, and `LearnSource/`
- PKM format classes directly map properties to byte offsets in `Data` span — no intermediate serialization layer
- `IEncounterable` is the interface for all encounter templates; `EncounterCriteria` filters valid encounters during generation
