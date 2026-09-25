# Cs2ExternalCheat
---

# FullyExternalCS2

External cheat for Counter-Strike 2 written in C#.

## Disclaimer

This project is **AI-generated code**. I used an AI assistant to write and debug the majority of this codebase. I do not guarantee it works correctly, safely, or at all. **I am not responsible for any bans, VAC detections, or consequences** resulting from the use of this software. Use at your own risk.

This was a learning experiment in reverse engineering and game memory manipulation. Nothing more.

## Features

### Combat
- **Aimbot** — Legit & Rage modes, multipoint, humanization, backtracking, FOV circle
- **TriggerBot** — Hitbox-based, FOV filter, configurable delay

### Visuals
- **ESP** — Box, skeleton, health bar, name, weapon, flags
- **Bullet Tracers** — 3D tracer lines with fade-out
- **Sound ESP** — Visual indicators for gunshot sounds
- **Hitmarker** — Customizable with sound, preview button
- **Aim Aura** — 5 styles (Ring, Glow, Crosshair, Box, Orbit)
- **Custom Crosshair** — Always-on crosshair with outline

### Interface
- **Speed Overlay** — Real-time speed graph, fully customizable
- **Status Panel** — Active module indicators
- **Customization** — Accent color picker, alignment modes, rounding, alpha, shadows, theme presets

## TODO / Needs Fixing

The following features were implemented but might be buggy, incomplete, or broken due to game updates and AI hallucinations. They require manual fixing and proper testing:

- [ ] **Bomb Timer** — Needs offset verification
- [ ] **Grenade Tracker** — Entity list parsing needs fixing
- [ ] **Offscreen Arrows** — Math needs verification
- [ ] **Damage Numbers** — Health tracking needs optimization
- [ ] **Seeded Trigger** — Needs proper weapon spread data from memory
- [ ] **RCS** — Punch angle reading needs fixing
- [ ] **Bhop** — Jump offset might be outdated
- [ ] **Anti-Flash** — Flash duration offset needs updating

## Tech Stack

- **C# / .NET 8**
- **ImGui** — Overlay rendering via ClickableTransparentOverlay
- **Process.NET** — Memory read/write via `ReadProcessMemory` / `WriteProcessMemory`
- **cs2-dumper** — Dynamic offset parsing from local dumper or GitHub

## How It Works

All features are **100% external**. No DLL injection, no hooks, no kernel drivers. The cheat reads game memory via Windows API and renders an transparent overlay window on top of the game.

Offsets are parsed dynamically from `cs2-dumper` output (local or GitHub fallback). No hardcoded offsets.

## Requirements

- Windows 10/11
- .NET 8 SDK
- Counter-Strike 2
- cs2-dumper (included in `Dumper/` folder)

## Build

```
dotnet build -c Release
```

## Usage

1. Launch CS2
2. Run `cs2-dumper.exe` (or let the cheat auto-run it)
3. Launch the cheat
4. Press the menu toggle key (default: Insert)

--
Author of original is - sweeperxz
