# Ouroboros
## 4v4v4v4 Team-Based Extraction Heist Game

A modular multiplayer extraction heist game built with **Photon Fusion 2**, featuring 4 teams of 4 players competing to crack objectives, hold onto the loot, and extract before the window closes.

> **Status: v0.15 — networked prototype with a cel-shaded look of its own, a game shell and an indoor mall map.** The systems below exist in code and pass an
> offline C# type-check and unit tests (`Tools/`). They have not been validated in play mode. Characters, loot,
> patterns, music and sound are generated in code as stand-ins; there is no authored art, audio or voice yet.
> A ✅ in the feature list means "implemented in code", not "tested and fun".
>
> - Where the game stands and where it is going: [GAME_AUDIT_AND_ROADMAP.md](GAME_AUDIT_AND_ROADMAP.md) (current plan, milestones M0 to M6)
> - History of what was built in each version: [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md)
> - First-run and two-client checklist: [PHASE0_BRINGUP.md](PHASE0_BRINGUP.md)
> - Checks that run without Unity: [Tools/README.md](Tools/README.md)

<!-- [v0.2 status, superseded]
> **Status: v0.2 — architecture + gameplay loop implemented, awaiting first in-engine bring-up.**
> See [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for the audit, what changed, and the roadmap, and [PHASE0_BRINGUP.md](PHASE0_BRINGUP.md) for the first-run checklist.
-->

## Overview
- **4 crews of 3** (Lagos, Nairobi, Accra, Joburg); bots fill every empty seat, so one person can play a full match
- **The Run**: crack vaults, carry the named loot they spill, and when a crew has the quota (banked plus carried) the getaway is called for everyone. The central getaway banks everything; side exits bank less. Score is what you got out with.
- **Crew play**: ping what you are looking at for your crew (middle mouse or T); revive a downed crewmate by holding Interact at their ID card; health comes back out of combat.
- **Hear and find your way**: footsteps for players and guards, a sound for every money event, and a compass strip with a pip for everything worth walking to. Down or away, you watch a crewmate (Fire and Aim change who).
- **Always know what to do**: an objective tracker names the next thing and marks it; a briefing card explains the heist before it starts; the cash board top right is the scoreboard.
- **Readable threats**: guards, turrets and cameras carry a bright outline. Rival players carry no marker unless your crew has tagged them.
- **Exits are lifts**: doors shut until the getaway is called, then they open and the HUD points at the nearest one.
- **Cash-out stations**: bank in the middle of the match by holding Interact at a station. It takes a minute, every crew is told where, and a rival who holds Interact at it steals the whole pot.
- **4 specialists**: Cipher, Spider, Smith, Shield (`Hacker`, `Saboteur`, `Demolitions`, `Agent` in code)
- **Premise**: the Ouroboros Freeport holds what was taken from a continent. Four crews are here to take it back.
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
- ✅ **AI** — server-authoritative agents, perception, behavior trees, shared blackboard, alarm-tier aggression; Guard, Elite and Sniper types; doors that carve the NavMesh
- ✅ **HUD** — UI Toolkit HUD: vitals, cooldowns, status, loot, timers, scoreboard, zone prompts, kill feed, death/end screens
- ✅ **Presentation hooks** — team colours, status tints, stealth visibility, data-driven class stats (`ClassData` + `ClassRegistry`)
- ✅ **Input System first** — rebindable keyboard/mouse/gamepad actions, legacy fallback
- ✅ **Lobby class picker** and pre-match ability lock
- ✅ **Feedback hooks** — `FeedbackLibrary` audio/VFX slots wired to every gameplay event (assets not included)
- ⬜ **Animation / art** — presentation is placeholder capsules
- ✅ **Vaults** — four walled vaults with keycard, enemy-ID-card, passcode (keypad, wrong code = lockdown) and lockdown-override locks gating the interior crack
- ✅ **Movement tech** — slide, mantle, and per-class double jump / glide / wall climb / grapple
- ✅ **Heist layer** — alarm tiers with lockdown, guard spawner and reinforcements, hack terminals, carry-the-case, multi-stage vaults, loot weight
- ✅ **Weapons** — networked loadout (ammo, reload, cooldowns), lag-compensated hitscan, server-simulated projectiles, passive gear, lobby loadout picker

### Player Classes

| Class | HP | Speed / Sprint | Abilities (1–4) |
|-------|----|----------------|-----------------|
| **Cipher** (hack specialist; `Hacker` in code) | 85 | 5.5 / 7.8 | Remote Hack, Net Sight, EMP Blast, Data Mine |
| **Spider** (ambush; `Saboteur` in code) | 90 | 6.0 / 8.5 | Place Trap, Stealth Mode, Sabotage, Smoke Bomb |
| **Smith** (breacher; `Demolitions` in code) | 110 | 4.5 / 6.5 | Place Explosive, Detonate, Breaching Charge, Incendiary |
| **Shield** (protector; `Agent` in code) | 100 | 5.2 / 7.5 | Tactical Shield, Damage Boost, Recon Drone, Flashbang |

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
- **Unity 6000.4** (project settings are committed; built-in render pipeline, Input System + legacy "Both")
- **Input System** (`com.unity.inputsystem`) and **uGUI** (`com.unity.ugui`, provides TextMeshPro for the Fusion Menu) — both in `Packages/manifest.json`
- **Photon Fusion 2.1.3** (committed under `Assets/Photon`) + your Photon App ID
- **AI Navigation** package (`com.unity.ai.navigation`, in `Packages/manifest.json` since v0.9) for NavMesh baking (GuardAI)

### Scene Setup
**Current map:** menu **Ouroboros > Setup > Create Galleria Map (M6)** builds the prefabs, assets and the `Galleria`: an indoor, two-level mall around a bank, 220 by 180 metres, with four anchor wings, sixteen shops, staff corridors, eleven vaults, about 120 breakable containers, guard posts and patrols, and timed events. Its plan is generated by `Tools/mapgen/galleria.py`.

**Earlier map:** menu **Ouroboros > Setup > Create Freeport Map (M2)** builds the prefabs, assets and the `Freeport` map (a central getaway, the Gallery, Archive and Docks districts, a bonded store, a courtyard and a motor pool, dressed with code-built props) and makes it the first scene. On a Freeport scene built before v0.13, **Ouroboros > Setup > Apply Art Pass (M4)** and **Build Out Freeport (M5)** add the dressing and the props. The older generated test arena is still available:

**Dev arena:** menu **Ouroboros > Setup > Create Dev Scene (Phase 0)** generates the config asset, prefabs and a playable `DevArena` scene (see [PHASE0_BRINGUP.md](PHASE0_BRINGUP.md)); **Ouroboros > Setup > Rebuild Scene References** re-applies the asset references if any field in the scene comes up empty. Manual equivalent:

1. **Player prefab**: `NetworkObject` + `NetworkTransform` + `CharacterController` + `NetworkPlayer` + `PlayerController` (+ child camera assigned to `cameraTransform`). Register it in the Fusion `NetworkProjectConfig` prefab table.
2. **Scene objects**: `GameSessionManager` (assign the player prefab), `TeamManager` and `ExtractionHeistGameMode` as scene `NetworkObject`s.
3. **Config**: Create → Ouroboros → Game Mode Config, assign to `ExtractionHeistGameMode`. For solo testing set `minPlayersToStart = 1`.
4. **Level**: place `TeamSpawnPoint`s (one per team at minimum), `ExtractionPoint`s, `LootObjective`s, optional `SecurityCamera`s, `BreachableDoor`s and `GuardAI`s (with a baked NavMesh).
5. Press Play. The main menu comes up (v0.13): **Solo** plays offline with bots, **Quick Play** hosts or joins a public match, **Host Private Match** gives a party code and **Join Code** takes one. Run a second instance (ParrelSync or a build) to join. To get the old press-Play-and-host flow, untick `showMainMenu` on `GameSessionManager`.

## Game Flow
1. **Waiting** — players join, get a team and class
2. **Pre-match countdown** (10s default)
3. **In progress** — crack objectives, fight; extraction points open after 5 minutes (default)
4. **Extraction window** — match timer expired, 2 minutes (default) to get out; no respawns
5. **Results** — highest score wins (ties: banked loot, then extracted members)

Scoring: objectives, kills, first extraction per team, each member extracted, and banked loot (`GameModeConfig`). Loot is banked at an exit during the getaway or at a cash-out station at any time.

## Look and feel
- **Look**: a toon shader whose light ends in a woven band of marks, indigo shade and warm light, ring highlights, brushed ink outlines and a banded dusk sky (`Assets/Resources/OuroborosShaders`, `Art/Stylizer.cs`, `Art/StyleDirector.cs`). It is applied to everything at run time and can be switched off in the settings.
- **Feel**: camera shake and controller rumble through [Feel](https://feel.moremountains.com/), motion through [DOTween](https://dotween.demigiant.com/). Both are imported asset packs you must own; game code reaches them only through `Core/Juice.cs`.

## Controls
Input goes through the **Input System** package (keyboard, mouse, gamepad) when it is enabled; the legacy Input Manager is only a fallback. The pause menu (Esc) has a **Controls** page listing every binding; it is a read-only reference. Rebinding exists in code (`HeistInputActions.StartRebind`, saved in PlayerPrefs) but has no screen yet.

You hold one weapon at a time (v0.13). Left click fires it, right click aims down its sights, and the mouse wheel swaps to the other one.

| Action | Keyboard / Mouse | Gamepad |
|--------|------------------|---------|
| Move / Look | WASD / Mouse | Left stick / Right stick |
| Sprint (drains stamina) | Shift | L3 (left stick click) |
| Jump | Space | A |
| Interact: **hold** while aiming at a device to quick-hack it; **press** at a vault to jack in (then 1-4 answer the signs, press again to jack out); hold for cards, notes and doors | F | X |
| Abilities 1–4 | 1 2 3 4 | D-pad ↑ → ↓ ← |
| Ping what you are looking at for your crew | MMB or T | none by default |
| Fire the weapon in hand | LMB | RT |
| Aim down sights (tighter spread, less kick, slower walk, no sprint) | RMB (hold) | LT (hold) |
| Swap weapon | Mouse wheel, or X | Select |
| Utility | Q | LB |
| Gadget: Breaching Kit, EMP Emitter, Bypass Key or Loot Scanner (chosen in the lobby) | E | RB |
| Reload | R | Y |
| Slide (while sprinting) | Ctrl | B |
| Grapple (Smith, or anyone carrying the Grapple Line gadget) | G | R3 |
| Glide / wall climb | hold Space | hold A |
| Menu (pause, settings, controls, profile, leave match) | Esc | Start |
| Debug overlay | F1 | - |

## Technical Details
- **Max Players**: 16 (4 teams × 4 players)
- **Match Duration**: 15 min + 2 min extraction window (configurable)
- **Ability Slots**: 4 per class · **Equipment Slots**: 6 · **Tech Slots**: 3

## Roadmap
See [GAME_AUDIT_AND_ROADMAP.md](GAME_AUDIT_AND_ROADMAP.md) for the current plan (milestones M0 to M6).
[DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) keeps the earlier Phase 0 to Phase 5 history.

## Checks without Unity
```
dotnet test Tools/tests        # unit tests for rules arithmetic, constants, behaviour tree
Tools/typecheck/run.sh         # compiles every game script against Unity / Fusion / Input System references
```
Both run in CI on every push. See [Tools/README.md](Tools/README.md) for what they do and do not prove.

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
