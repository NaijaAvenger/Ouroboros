# Ouroboros
## 4v4v4v4 Team-Based Extraction Heist Game

A modular multiplayer extraction heist game built with **Photon Fusion 2**, featuring 4 teams of 4 players competing to crack objectives, hold onto the loot, and extract before the window closes.

> **Status: v0.2 — architecture + gameplay loop implemented, awaiting first in-engine bring-up.**
> See [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for the audit, what changed, and the roadmap, and [PHASE0_BRINGUP.md](PHASE0_BRINGUP.md) for the first-run checklist.

## Overview
- **4 Teams** of 4 players each (16 players total)
- **4 Initial Classes**: Hacker, Saboteur, Demolitions, Agent
- **Heist loop**: crack vaults → carry loot → extract to bank it. Dying drops your loot for anyone to grab.
- **Modular systems** for easy expansion of classes, equipment, interactables, and AI

## Features

### Core Systems
- ✅ **Fusion input pipeline** — `INetworkInput` + `GetInput`, client prediction, server authority
- ✅ **Session bootstrap** — runner start, spawning at team spawn points, team/class assignment, respawns
- ✅ **Networked player state** — health, stamina (sprint), replicated status effects, cooldowns, loot
- ✅ **Class system** — per-slot cooldown/stamina table, tick-timer effects, damage hooks
- ✅ **Team management** — replicated assignments, alive/extracted/dead tracking
- ✅ **Match flow** — countdown → match → extraction window → winner, all from `GameModeConfig`
- ✅ **Objectives & loot** — hold-to-crack vaults, contested capture, loot drops
- ✅ **Extraction zones** — contested pause, reusable points, whole-team option
- ✅ **Interactables** — hackable/breachable doors, security cameras, proximity traps
- ✅ **AI** — server-authoritative agents, perception, behavior trees, a working guard
- ✅ **HUD** — UI Toolkit HUD: vitals, cooldowns, status, loot, timers, scoreboard, zone prompts, kill feed, death/end screens
- ✅ **Presentation hooks** — team colours, status tints, stealth visibility, data-driven class stats (`ClassData` + `ClassRegistry`)
- ⬜ **VFX / audio / animation** — hook into `PlayerPresentation` and the static events (Phase 1, remaining)
- ⬜ **Weapons** — equipment slots exist; networked weapons are Phase 2

### Player Classes

| Class | HP | Speed / Sprint | Abilities (1–4) |
|-------|----|----------------|-----------------|
| **Hacker** | 85 | 5.5 / 7.8 | System Hack, Disable Camera, EMP Blast, Data Mine |
| **Saboteur** | 90 | 6.0 / 8.5 | Place Trap, Stealth Mode, Sabotage, Smoke Bomb |
| **Demolitions** | 110 | 4.5 / 6.5 | Place Explosive, Detonate, Breaching Charge, Incendiary |
| **Agent** | 100 | 5.2 / 7.5 | Tactical Shield, Damage Boost, Recon Drone, Flashbang |

## Architecture
See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed documentation.

### Directory Structure
```
Assets/Scripts/
├── Core/         # Constants, interfaces, input struct, ability context, scene utils
├── Classes/      # Player class implementations
├── Network/      # Session manager, networked player, team manager
├── GameMode/     # Match flow, extraction points, loot objectives, loot drops
├── Interaction/  # Hackable / breachable / security interfaces and objects
├── Combat/       # IDamageable and damage helpers
├── Equipment/    # Equipment and tech systems
├── AI/           # Networked AI base, perception, behavior trees, GuardAI
├── Data/         # ScriptableObject definitions
└── Player/       # Player controller and spawn points
```

## Getting Started

### Prerequisites
- **Unity 6000.4** (project settings are committed; built-in render pipeline, legacy Input Manager)
- **Photon Fusion 2.1.3** (committed under `Assets/Photon`) + your Photon App ID
- **AI Navigation** package (`com.unity.ai.navigation`) for NavMesh baking (GuardAI)

### Scene Setup
**Fast path:** menu **Ouroboros > Setup > Create Dev Scene (Phase 0)** generates the config asset, prefabs and a playable `DevArena` scene (see [PHASE0_BRINGUP.md](PHASE0_BRINGUP.md)). Manual equivalent:

1. **Player prefab**: `NetworkObject` + `NetworkTransform` + `CharacterController` + `NetworkPlayer` + `PlayerController` (+ child camera assigned to `cameraTransform`). Register it in the Fusion `NetworkProjectConfig` prefab table.
2. **Scene objects**: `GameSessionManager` (assign the player prefab), `TeamManager` and `ExtractionHeistGameMode` as scene `NetworkObject`s.
3. **Config**: Create → Ouroboros → Game Mode Config, assign to `ExtractionHeistGameMode`. For solo testing set `minPlayersToStart = 1`.
4. **Level**: place `TeamSpawnPoint`s (one per team at minimum), `ExtractionPoint`s, `LootObjective`s, optional `SecurityCamera`s, `BreachableDoor`s and `GuardAI`s (with a baked NavMesh).
5. Press Play — `GameSessionManager` auto-starts an `AutoHostOrClient` session. Run a second instance (ParrelSync or a build) to join.

## Game Flow
1. **Waiting** — players join, get a team and class
2. **Pre-match countdown** (10s default)
3. **In progress** — crack objectives, fight; extraction points open after 5 minutes (default)
4. **Extraction window** — match timer expired, 2 minutes (default) to get out; no respawns
5. **Results** — highest score wins (ties: banked loot, then extracted members)

Scoring: objectives, kills, first extraction per team, each member extracted, and banked loot (`GameModeConfig`).

## Controls
- **WASD** — Move · **Shift** — Sprint (drains stamina) · **Space** — Jump · **Mouse** — Look
- **1–4** — Class abilities · **F (hold)** — Interact (crack objectives)
- **LMB / RMB** — Primary / Secondary · **Q** — Utility · **E** — Gadget
- **Esc** — Toggle cursor lock

## Technical Details
- **Max Players**: 16 (4 teams × 4 players)
- **Match Duration**: 15 min + 2 min extraction window (configurable)
- **Ability Slots**: 4 per class · **Equipment Slots**: 6 · **Tech Slots**: 3

## Roadmap
See [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) — Phase 0 (engine bring-up) through Phase 5 (meta).

## Contributing
1. Write `[Networked]` state only on the state authority
2. Use `TickTimer`, never `Invoke` / `Time.time`, for gameplay timing
3. Put tunables in `GameModeConfig` / `AbilitySlot` definitions
4. Keep superseded code as `// [v0.1]` comments next to its replacement
5. Document expansion points

## License
[Add your license here]

## Credits
Built with Unity and Photon Fusion 2
