# Ouroboros — Development Plan

This document records the v0.1 → v0.2 audit of the extraction-heist codebase, what was changed,
and the phased roadmap from here. Superseded code is kept in place as `// [v0.1]` comments so the
history of each change is readable inline.

---

## 1. Audit of v0.1 (what was not fine-tuned)

### 1.1 Outright bugs

| # | Where | Problem | Fix (v0.2) |
|---|-------|---------|------------|
| 1 | `ExtractionHeistGameMode.CheckMatchEnd` | `extracted >= 4 \|\| state == Extraction` ended the match the moment **any** team extracted during the Extraction phase. | Replaced by `CheckEarlyEnd` (every team with players is extracted or wiped) plus an extraction-window timer. |
| 2 | `ExtractionHeistGameMode` | Match timer expiry moved to `Extraction` but nothing ever ended that phase. | `PhaseTimer` drives PreMatch countdown and the Extraction window. |
| 3 | `NetworkPlayer.RPC_AssignClass` | RPC with `RpcTargets.All` wrote `Health`/`Stamina` (`[Networked]`) on clients — Fusion rejects writes from non-authority peers. Late joiners also never receive RPCs, so they got no class component. | Class type is replicated; each peer builds the component via `ChangeDetector`; only the authority writes state. |
| 4 | `NetworkPlayer.TakeDamage` | Damage applied twice (class-internal health and networked health) and the Agent shield only affected the unused copy. | Single health; classes expose `ModifyIncomingDamage` / `OutgoingDamageMultiplier`. |
| 5 | `SequenceNode.Evaluate` | After a full pass, the next call returned `Success` without evaluating any child. | Rewritten; added `Reset()` so selectors abort lower-priority running branches. |
| 6 | `EquipmentLoadout.ClearAllEquipment` | Mutated the dictionary while enumerating its keys (throws on Mono). | Iterates a copy. |
| 7 | `ExtractionPoint` | Unity trigger callbacks are not tick-aligned; players who died inside the zone were never removed; `FindObjectOfType` per completion. | Zone membership is scanned per tick from the player registry; progress is a replicated float. |
| 8 | Docs | `IMPLEMENTATION_GUIDE` examples didn't compile: `override AwardTeamScore` (not virtual), `AIAgentType.Sniper` (missing), nested BT nodes reading `protected` fields through a base reference. | `AwardTeamScore` virtual, `Sniper` added, public accessors on `BaseAIAgent`, guide rewritten. |

### 1.2 Not wired up / not tuned

- **No session bootstrap.** Nothing started a `NetworkRunner`, spawned players, assigned teams or collected input. → `GameSessionManager`.
- **Input polled inside `FixedUpdateNetwork`.** `Input.GetKeyDown` in a tick loop loses or duplicates presses, and movement only ran on the client (no server authority, no prediction). → `NetworkInputData` + `GetInput`.
- **Class stats never reached the controller.** `PlayerController.SetPlayerClass` had no caller, so every class moved at the default speed. → Controller subscribes to `NetworkPlayer.ClassChanged`.
- **Timing used `Invoke`/`Time.time`.** Not tick-aligned, breaks under resimulation, invisible to clients. → `TickTimer` everywhere; cooldowns and status effects are replicated.
- **Only one ability (Hacker slot 0) had a cooldown**; nothing cost stamina; stamina lived in two places. → Per-slot `AbilitySlot` table (cooldown, stamina cost, duration) enforced centrally.
- **`GameModeConfig` existed but nothing read it.** Every tunable was duplicated as a serialized field. → All systems read `ExtractionHeistGameMode.Config`.
- **Team data only on the server.** `TeamManager` used a plain dictionary; clients couldn't resolve teams. → `NetworkDictionary`.
- **No heist.** No objectives, no loot, no kill credit, no respawn, no winner. → `LootObjective`, `LootDrop`, carried/banked loot, kill scoring, respawns with limits, winner determination.
- **AI never ticked** and wasn't networked. → `BaseAIAgent : NetworkBehaviour` + `Perception` + `GuardAI`.
- **Overstated docs** ("production-ready", "battle-tested") for code that had never run.

---

## 2. What v0.2 delivers (this branch)

### Core / Network
- `Core/NetworkInputData.cs` — `INetworkInput` with move axes, yaw/pitch, `NetworkButtons`.
- `Core/IAbilityContext.cs` — what a class needs from its owner (runner, team, stamina, cooldowns, status flags, timers).
- `Core/GameConstants.cs` — `StatusFlags`, `InputButtons`, `TeamUtil`, stamina tuning constants.
- `Network/NetworkPlayer.cs` — single source of truth: health, stamina (sprint drain + delayed regen), status flags with per-flag expiry, burning DOT, ability cooldown array, carried loot, kills/deaths, respawn timer, look yaw/pitch, extraction state. Static registry `NetworkPlayer.All` for cheap radius queries.
- `Network/TeamManager.cs` — replicated assignments, `GetTeamStatus`, `IsTeamFinished`.
- `Network/GameSessionManager.cs` — runner start (Host/Server/Shared), spawn at `TeamSpawnPoint`, team + class assignment, leave handling, respawn placement, frame-accumulated input sampling.
- `Player/PlayerController.cs` — simulated from `GetInput` on both authorities; sprint; jump; server-side ability/interact/equipment dispatch; `Teleport`.
- `Player/TeamSpawnPoint.cs` — scene markers with gizmos.

### Classes
- `BasePlayerClass` — `DefineAbility`, gated `UseAbility` → `ExecuteAbility`, timer helpers, damage hooks, death/respawn hooks.
- All four classes rebuilt on tick timers with real (if simple) effects: reveal, flash, EMP, stealth, shield, damage boost, traps that spawn, detonable charges with radial damage, breaching, incendiary DOT, security-device disabling, objective hacking.

### Game mode
- `ExtractionHeistGameMode` — config-driven state machine (Waiting → PreMatch → InProgress → Extraction → Ended), extraction opening delay, extraction window, kill/objective/extraction/loot scoring, respawn rules, early end, winner + tiebreak, loot drop on death.
- `ExtractionPoint` — per-tick scan, contested pause, reusable with reactivation delay, whole-team option, eligibility via game mode.
- `LootObjective` — hold-to-crack vault with contest, worker speed-up, hack unlock/boost, loot split, respawn.
- `LootDrop` — pickup with delay and lifetime.

### Interaction / Combat / AI
- `Interaction/` — `IHackable`, `ISecurityDevice`, `IBreachable`, `ProximityTrap`, `SecurityCamera` (reveals in cone), `BreachableDoor`.
- `Combat/` — `IDamageable`, `DamageUtil` (radial with falloff/LOS, hitscan).
- `AI/` — networked `BaseAIAgent` with perception, last-known-position memory, nav helpers, hitscan attack; fixed `BehaviorTree` with `Inverter`, `ConditionFunc`, `ActionFunc`; `GuardAI` (patrol / investigate / chase-and-shoot) built on the tree.

### Verification status
- Every script passes a C# **syntax** parse (tree-sitter). No Unity/Fusion SDK is available in this environment, so **type checking and play-mode testing are still owed** — see Phase 0 below. Fusion API usage targets Fusion 2.0.x; the exact `INetworkRunnerCallbacks` signatures and `NetworkButtons` generic overloads are the most likely places to need a one-line adjustment against your SDK version.

---

## 3. Roadmap

### Phase 0 — Bring-up in Unity (in progress; checklist in PHASE0_BRINGUP.md)
Done from the repo side:
- `Packages/manifest.json` (incl. AI Navigation) and `ProjectSettings/ProjectVersion.txt` (2022.3 LTS) so the folder opens as a project.
- `Assets/Editor/OuroborosDevSceneBuilder.cs`: menu **Ouroboros > Setup > Create Dev Scene (Phase 0)** builds the dev `GameModeConfig`, Player / LootDrop / ProximityTrap prefabs and the `DevArena` scene, and adds it to Build Settings.
- `Assets/Scripts/UI/DevHUD.cs`: F1 overlay with state, timers, vitals, cooldowns, loot, team scores, zone progress.
- Prefab references accept a direct `NetworkObject` fallback; the runner refuses to start with a clear error if the scene isn't in Build Settings; `NetworkButtons` calls use the `int` overloads.

Still requires a machine with Unity + Fusion:
1. Import Fusion 2, set the App ID, fix any SDK-signature drift (table in PHASE0_BRINGUP.md §3).
2. Run the menu item, press Play, complete the solo smoke test (§5) and Host + Client test (§6).
3. Record the fixes that were needed in PHASE0_BRINGUP.md §3.

### Phase 1 — Feel and clarity (implemented in v0.3, awaiting in-engine validation)
- **HeistHUD** (`UI/HeistHUD.cs`, UI Toolkit, code-built): match state + timers, team scoreboard, vitals bars, ability slots with cooldown fill / stamina cost / EMP dimming, status chips, loot and K/D, nearest vault or extraction zone with progress and contextual hint, kill feed, banners, death overlay with respawn countdown, extracted overlay, end-of-match table.
- **Events for presentation**: `NetworkPlayer.PlayerKilled` / `PlayerExtracted`, `LootObjective.Completed`, `ExtractionHeistGameMode.ExtractionOpened` / `TeamExtractedEvent` (all fire on every peer from the existing RPCs).
- **PlayerPresentation** (`Player/PlayerPresentation.cs`): team colour, status tints (shield, boost, revealed, burning, EMP), stealth hidden from enemy viewers unless revealed, own model hidden in first person, hidden on death / extraction. Replace with animation/VFX without touching gameplay.
- **ClassData wiring**: `BasePlayerClass.ApplyClassData` overrides stats and per-slot cooldown / stamina / duration; `ClassRegistry` asset published through `GameSessionManager` so every peer applies the same data; Saboteur's trap prefab now comes from `ClassData.trapPrefab`.
- **Scene builder** now also creates the four `ClassData` assets + `ClassRegistry`, the UI Toolkit theme + `PanelSettings`, the HUD object, and upgrades an existing Player prefab with `PlayerPresentation`. DevHUD stays available on F1.

- **Input System first** (`Core/LocalInputSource.cs`): keyboard, mouse and gamepad through `UnityEngine.InputSystem` when enabled, legacy Input Manager otherwise; HUD hints follow the last-used device.

- **Rebindable actions** (`Core/HeistInputActions.cs`): the Input System actions are built in code (Keyboard and Gamepad binding groups), read by `LocalInputSource`, rebindable at runtime via `StartRebind` with overrides persisted in PlayerPrefs; HUD hints use the live binding display strings.
- **Class picker** (`UI/ClassPickerUI.cs`): pre-match overlay with one card per class (stats and abilities from `ClassData`), click or press the ability key / D-pad to request a class; the state authority gets a "Start match now" button. Abilities are locked until the match is live (`GameModeConfig.allowAbilitiesBeforeMatch`).
- **Feedback hooks**: `Data/FeedbackLibrary` (optional clips and VFX prefabs), `Player/PlayerFeedback` (ability use, hurt, death, respawn, extraction, shield/stealth, loot gain, spatialised for remote players) and `UI/MatchFeedback` (match start / extraction / objective / kill-confirm, 2D). New replicated events: `NetworkPlayer.AbilityUsed` and per-player `Damaged`; the HUD flashes red on damage.

Still open in Phase 1: actual audio/VFX assets in the FeedbackLibrary, a controls/rebind screen (API is ready), animations.

### Phase 2 — Weapons & equipment (implemented in v0.4, awaiting in-engine validation)
- **NetworkLoadout** (`Equipment/NetworkLoadout.cs`, on the player prefab): replicated item id per slot, magazine, reserve, use cooldowns and reload timers; equips class defaults from `ClassData.startingEquipment`; lobby swaps via `RequestEquip`; `WeaponFired` / `ReloadStarted` events for cosmetics; summed passive modifiers (defense, damage, speed).
- **EquipmentRegistry** asset gives every `EquipmentData` a network id; `EquipmentLoadout` builds the local `BaseEquipment` component per slot from the id (`EquipmentKind` → class).
- **BaseEquipment v2**: owner/loadout context, press-vs-held use (automatic weapons), cooldown and ammo through the loadout, state-authority-only execution.
- **HitscanWeapon**: lag-compensated raycast (`Runner.LagCompensation.Raycast`, PhysX colliders included), spread, pellets, damage to any `IDamageable`.
- **ProjectileWeapon + Projectile**: server-simulated networked projectile with gravity, sphere-cast collision, direct or radial damage, optional fuse (bounces until it blows), `Exploded` event.
- **PassiveGear**: armor / accessories as modifiers consulted by `NetworkPlayer.ApplyDamage` and `PlayerController` speed.
- **Reload** input (R / Y), HUD ammo readout, lobby loadout cyclers (Primary / Secondary), feedback hooks (fire, impact, reload, explosion).
- Scene builder creates ten starter items + registry + projectile prefab and arms each class.

Still open in Phase 2: `Hitbox`/`HitboxRoot` on the player prefab for full lag compensation (PhysX fallback works today), ammo pickups, deployables (`EquipmentKind.Deployable` is a placeholder), weapon viewmodels.

### Phase 3 — Heist depth (implemented in v0.5, awaiting in-engine validation)
- **AlarmSystem** (`GameMode/AlarmSystem.cs`): replicated level 0-100 with decay; tiers Quiet / Suspicious / Alert / Lockdown; raised by cameras spotting players, guards engaging, vault stages and completions, breaches and kills; lowered by hack terminals. Alert scales guard and camera detection range; Lockdown holds the level, lengthens extractions and triggers reinforcement waves. All amounts in `GameModeConfig`.
- **AISpawner** (`AI/AISpawner.cs`): spawns the initial guards when the match goes live, replaces losses while Alert, and sends waves during Lockdown (capped). Guards get patrol routes from the spawner.
- **HackTerminal** (`Interaction/HackTerminal.cs`): Hacker's System Hack activates instantly, anyone can hold Interact; unlocks / boosts linked vaults and lowers the alarm; re-arms on a cooldown.
- **LootCase** (`GameMode/LootCase.cs`): carry-the-case hot potato: hold Interact to pick up, carrier is `Encumbered` (slowed), drops on death, banked with a bonus multiplier when the carrier extracts.
- **Multi-stage vaults**: `LootObjective.stages`; intermediate stages reset progress and raise the alarm.
- **Loot weight**: `NetworkPlayer.CarryWeightSpeedMultiplier` slows players by carried loot (and more with a case), applied in prediction.
- **Shared mode**: `TeamManager.RequestTeamAssignment` RPC so a spawning client without state authority gets a team.
- HUD: alarm meter + tier, case / terminal prompts, stage counter; builder adds the heist layer (alarm, spawner + routes, cameras, terminal, case, two-stage vault).

Still open in Phase 3: guard NavMesh baking is manual (needs the AI Navigation package), alarm-driven music/VFX, objective variety beyond these three types.

### Phase 4 — AI (implemented in v0.6, awaiting in-engine validation)
- **AIBlackboard** (`AI/AIBlackboard.cs`): server-side shared memory of sightings (guards, cameras, snipers, getting shot) and gunfire noise (per-weapon `noiseRadius`), with expiry and scoring. Idle guards investigate the best point of interest; at Suspicious+ nearby guards adopt a teammate's live sighting as their own target (squad hand-off).
- **Alarm-tier aggression** in `GuardAI`: movement speed, give-up time, hearing range and patrol tempo scale with the alarm tier.
- **EliteGuard**: armoured, 3-round bursts, holds a preferred distance and strafes, spawned in waves once the alarm is at least Alert.
- **SniperGuard**: stationary overwatch on raised posts; paints the target (`Revealed`) while aiming, then one heavy shot; scans toward blackboard points of interest.
- **NavMesh-carving doors**: `BreachableDoor` toggles a `NavMeshObstacle` so guards path through only once it is hacked or breached.
- `GuardAI` leaves are `protected virtual` so new guard types override just what they need (see `EliteGuard`).
- Builder adds elite / sniper prefabs, two sniper posts and a breachable door in a wall; still needs the NavMesh baked (AI Navigation package).

Still open in Phase 4: object pooling for guards, animation hooks, cover-seeking.

### Phase 4b — Vaults, movement tech, visibility (implemented in v0.7, awaiting in-engine validation)
- **Four-lock vaults** (`Interaction/VaultDoor.cs`): walled rooms whose interior objective is gated by an exterior door. Locks: **Keycard** (find the coloured card, consumed on use), **PlayerCard** (swipe an ID card looted from a dead rival; `IDCardDrop` spawns on every death), **Passcode** (4-digit code revealed to your team by a `CodeNote`; keypad UI; a wrong code slams the facility into lockdown and locks the keypad), **Lockdown** (forcing the override triggers a lockdown you must survive before the door opens). Hacker's System Hack reveals a passcode or shortens an active lockdown. Modelled on Hyenas-style vault runs: open the exterior, then crack the interior.
- **Movement tech** (`Player/PlayerController.cs`): sprint-slide (Ctrl / B) with camera dip, mantle onto ledges by jumping into them, and class perks via `Core.MovementProfile` (ClassData can override): Agent double jump, Hacker glide (hold Jump), Saboteur wall climb (hold Jump on a wall, drains stamina), Demolitions grappling hook (G / R3). All tick-simulated with networked state for prediction.
- **Vision cones** (`AI/VisionCone.cs`): translucent ground fans for cameras and guards, re-coloured by state (idle / searching / alert / disabled), scaled with the alarm tier.
- **Guard spawn diagnostics**: the spawner logs its references and every spawn, places guards on the floor, and the builder verifies the prefab links.

### v0.9 — M0 and the first slice of M1 (see GAME_AUDIT_AND_ROADMAP.md, which is the plan from here on)
- **M0**: feature branch merged; offline type-check and unit tests (`Tools/`), run in CI; `Core/MatchRules.cs` holds the pure match arithmetic; AI Navigation added to the manifest.
- **Movement sync**: the player prefab uses `NetworkCharacterController` instead of `NetworkTransform` (it disables the CharacterController while Fusion rewinds the transform, so reconciliation takes effect); `PlayerController` networks ground contact and velocity.
- **Weapons**: `NetworkLoadout` shots are networked state (`FireCount`, last shot point, bloom) predicted on the owning client instead of RPCs; `HitscanWeapon` uses a filtered lag-compensated `RaycastAll`, `Combat.HitZone` head/body zones, distance falloff, deterministic spread with bloom; recoil is a view punch in `GameSessionManager`; `Core/CombatRules.cs` holds the maths.
- **Feedback**: `UI/CombatHUD.cs` (crosshair, hit markers, damage direction, reload prompt), `Combat/ShotTracer.cs`, `Core/ProceduralSfx.cs` (synthesised placeholder sounds used when a FeedbackLibrary slot is empty), `NetworkPlayer.HitConfirmed` / `LastDamageOrigin`.
- **Guards**: hit chance from `CombatRules.GuardHitChance`, a settle delay before the first shot at a new target, `BaseAIAgent.AgentFired` for tracers and sound.
- Scene builder: swaps the sync component, adds hitboxes to player and guard prefabs, applies a per-weapon feel profile once (`EquipmentData.feelVersion`).

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7e.

### v0.10 — M2, "The Run"
- **Rules**: `GameModeConfig.lootQuota` and friends; `ExtractionHeistGameMode` calls the getaway on quota or timer, tracks the richest crew, resolves crew wipes (`ScheduleRespawn`), scores extracted value only; `ExtractionPoint.isCentral` and payout; `Core/MatchRules.cs` gains the quota, payout, wipe, rotation and item-pick arithmetic.
- **Loot**: `Data/LootCatalog.cs`; `LootDrop` carries a catalog id; `LootObjective` spills items; `NetworkPlayer.CarriedItemIds`.
- **Crews and bots**: `GameModeConfig.playersPerTeam` = 3, `TeamUtil.DisplayName`, `HumanAssignment`; `AI/BotBrain.cs`, `PlayerController.SetBot`, `GameSessionManager.FillWithBots`.
- **Map**: `Assets/Editor/OuroborosFreeportBuilder.cs` (menu **Ouroboros > Setup > Create Freeport Map (M2)**).

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7f.

### v0.11 — M3, hacking
- **Rules**: `Core/HackRules.cs` (sign puzzles, deep-hack progress, counter-hack, quick-hack time, range and aim cone).
- **Quick hacks**: `Interaction/QuickHack.cs` (`IQuickHackable`, registry, targeting); `Network/NetworkPlayer.Hacking.cs` (tether state, jack-in state, answers); `Player/HackTether.cs`.
- **Devices**: cameras, breachable doors and proximity traps implement `IQuickHackable`; new `Interaction/Shutter.cs`, `PowerNode.cs`, `GravLift.cs`, `SentryTurret.cs`; `PlayerController.Launch`.
- **Deep hack**: `LootObjective.CanJackIn` / `ApplyHackAnswer` / `TakenOver`; passive progress slowed; abandon decay.
- **Cipher**: `Classes/HackerClass.cs` (Remote Hack, Net Sight), `StatusFlags.NetSight`, `Hacker.asset` names.
- **UI**: `UI/HackHUD.cs` (prompt, console, Net Sight markers), added automatically by `HeistHUD`.
- **Bots**: `AI/BotBrain.cs` answers signs on a timer and treats hostile turrets as targets.
- **Map**: `OuroborosFreeportBuilder.BuildFreeportDevices` (gates, power junctions, lifts, turrets).

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7g.

### v0.12 — M3 follow-up and M4, identity
M3 follow-up:
- **Zero gravity**: `Interaction/GravityZone.cs` (hackable volume), `PlayerController.SimulateZeroG` (networked `Floating`, `FloatVelocity`), bot steering in zero-g, two zones on Freeport.
- **Jamming players**: `NetworkPlayer` implements `IQuickHackable` (`Jam gear`: abilities off, weapon jammed, then immune for a while), `NetworkLoadout.Jam`, warning on the victim's HUD.
- **Pulse puzzle**: `HackRules.PuzzleKind`, pulse position and window from the shared seed and tick, `LootObjective.puzzleMode` (Signs, Pulse, Mixed), pulse panel in `HackHUD`.

M4, all generated in code:
- **Crew identity**: `Core/PatternRules.cs` (four patterns, unit-tested), `Art/CrewStyle.cs` (palettes, pattern textures, materials), `UI/CrewSkin.cs` (pattern band and accents for UI panels).
- **Models**: `Art/CharacterBuilder.cs` (four specialists, three guards, simple animation), `Art/ArtefactBuilder.cs` (nineteen artefact shapes), `Art/WeaponView.cs` (shows `EquipmentData.equipmentPrefab`), `Art/DisplayPiece.cs`, `Art/WorldSign.cs` with `Assets/Ouroboros/Shaders/WorldText.shader`.
- **Specialists**: Cipher, Spider, Smith, Shield (`Core.ClassUtil.DisplayName`, class scripts and assets). Enum names unchanged.
- **Loot**: catalog grown to twenty items, each with a shape.
- **Audio**: `Audio/MusicDirector.cs` (four synthesised layers following the alarm tier and the getaway).
- **Crew radio**: `UI/CrewCallouts.cs` (subtitled lines per crew; placeholder writing, needs native speakers).
- **Builder**: `Assets/Editor/OuroborosArtPass.cs` (weapon models, effect slots, pattern assets, Gallery dressing, dock banners; menu **Apply Art Pass (M4)**).

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7h.

### v0.13 — M5, the game shell, plus gun handling, gear visuals, teammate markers and the wider map
- **Weapons**: `InputButtons.NextWeapon` / `PrevWeapon`; `NetworkLoadout.ActiveSlot`, `SwapTimer`, `Aiming`, `CycleWeapon`, `SetAiming`, `ReloadActive`; `CombatRules.AimedSpread`, `ZoomedFov`, `ZoomSensitivity`; handling fields on `EquipmentData`; `PlayerController.AimBlend` and view zoom; `Art/WeaponView` rewritten (draw, aim, reload, kick, stowed weapon); bots swap and aim.
- **Gear and effects**: `Art/Prim.cs`, `Art/GearBuilder.cs` (stand-in weapons, trap, charge, grenade, drone, worn gear), `Art/Fx.cs` (particles, pulses, blasts, smoke, fire), `Art/AbilityEffects.cs` with `BasePlayerClass.GetAbilityVisual`, `Art/StatusEffects.cs`, status screen wash in `CombatHUD`, models on `ProximityTrap` and `Projectile`.
- **Teammate markers**: `UI/TeammateMarkers.cs`.
- **Map**: `Assets/Editor/OuroborosPropKit.cs` (prop kit, shared materials, `BuildFreeportExpansion`, menu **Build Out Freeport (M5)**).
- **Shell**: `UI/GameMenu.cs` (main menu, pause, settings, read-only controls list, profile), `Core/GameSettings.cs`, `Core/ProfileRules.cs` (unit-tested), `Core/PlayerProfile.cs`, `GameSessionManager.showMainMenu` / `LeaveToMenu` / `SessionCode`, player names sent to the host and cleaned there.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7i.

### v0.14 — the Galleria, breakables, gadgets, damage numbers and the death recap
- **Menu**: title and columns sized from the screen (`GameMenu.FitToScreen`).
- **Damage numbers and death recap**: `Core/DamageLog.cs` (unit-tested), `Combat/DamageContext.cs`, `NetworkPlayer.DamageDealt` / `DeathReported`, `UI/DamageNumbers.cs`, `UI/DeathRecap.cs`. Guards, turrets, traps, explosions, fire and lasers are named sources.
- **Breakables**: `Interaction/Breakable.cs` (ten kinds, armour, loot, dormant until an event), `Art/BreakableModels.cs`. Explosions damage them.
- **Gear puzzles**: `Equipment/GadgetTool.cs` and `Data.GadgetType` (Breaching Kit, EMP Emitter, Bypass Key, Loot Scanner, Grapple Line), a gadget row in the lobby picker, `Interaction/LaserGrid.cs`, `Interaction/MagLockDoor.cs`, `LootObjective.alsoOpenedBy`.
- **Map systems**: `GameMode/MapZone.cs`, `GameMode/MapEventDirector.cs`, `UI/MapHUD.cs` (zone name, event markers, scanner markers), camera model.
- **The Galleria**: `Tools/mapgen/galleria.py` (generator and checks), `Assets/Editor/OuroborosGalleriaLayout.cs` (generated), `OuroborosGalleriaBuilder.cs`, `OuroborosMallProps.cs` (fifteen interior props). Menu **Create Galleria Map (M6)**.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7j.

### v0.15 — Feel, DOTween and the stylised look
- **Adapter**: `Core/Juice.cs` is the only file that calls Feel (`MMCameraShaker`, `MMWiggle`, Nice Vibrations) and DOTween. `JuiceDoor` animates door leaves. `UI/JuiceEvents.cs` maps match events to shake and rumble.
- **Camera**: `PlayerController` tracks eye height on its own and adds `Juice.ShakeOffset`, so shake never fights the slide.
- **Motion**: shutters, vault doors and maglocks move; loot pops in; breakables flinch; banners, feed, menu and recap animate.
- **Look**: `Assets/Resources/OuroborosShaders/OuroborosToon.shader`, `OuroborosInk.shader`, `OuroborosSky.shader`; `Art/Stylizer.cs` (toon twins of lit materials), `Art/InkOutline.cs` (camera pass), `Art/StyleDirector.cs` (cameras, new meshes, sky, ambient); `ArtMaterials.SetToon`.
- **Editor**: menus **Apply Stylised Look (materials)** and **Remove Stylised Look (materials)**; the builders make new materials in the toon shader.
- **Settings**: screen shake, controller vibration, stylised look.
- **Tools**: `Tools/shadercheck` compiles the shaders' code with glslang; `Tools/typecheck/VendorStubs.cs` stands in for Feel; DOTween is referenced as its DLL. Both checks run in CI.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7k.

### v0.16 — cash-out stations
- **Rules** (`Core/MatchRules.cs`, unit-tested): `CrewHaul`, `CashOutTimeAfterDeposit`, `CashOutTimeAfterHijack`, `HijackStep`, `CashOutCountdown`.
- **Station**: `GameMode/CashOutStation.cs`. Hold Interact to deposit; the pot banks after `cashOutSeconds`; owners can add; rivals hold Interact to hijack, blocked while an owner stands in range, countdown frozen during a hijack. Static event `Moment` (started, added, hijacked, banked). Builds its own kiosk model.
- **Game mode**: `BankCashOut`, `GetTeamBanked`, `GetTeamHaul`, `CashBanked`; the quota is checked against carried plus banked (`GameModeConfig.quotaCountsBanked`, `cashOutPayout`).
- **HUD**: `UI/CashOutHUD.cs` (ticker, world markers, station prompt), announcer lines and banners in `HeistHUD`, scoreboard shows banked and carried, haptics in `JuiceEvents`.
- **Bots**: `BotBrain.TryCashOutGoal` (defend ours, hijack theirs, bank what is carried).
- **Maps**: four stations in the Galleria layout (`'CashOut'` in `Tools/mapgen/galleria.py`), two added to the Freeport by its builder at the nearest clear floor.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7l.

### v0.17 — threat outlines, open stairwells, ping, revive, regeneration
- **Threat outlines**: `Assets/Resources/OuroborosShaders/OuroborosThreat.shader` (an inverted hull pushed from the mesh's bounds, so boxes close at the corners), `Art/ThreatOutline.cs` (adds and colours the shells; one material per colour and mesh shape). Hooked from `BaseAIAgent.DressModel`, `SentryTurret`, `SecurityCamera`. Setting `GameSettings.ThreatOutlines`.
- **Turret**: `GearBuilder.BuildTurret`, built in `SentryTurret.BuildModel` with a larger hit box. Cameras scaled 1.5.
- **Galleria floors**: `Tools/mapgen/galleria.py` emits `GalleriaZoneFloors` cut from real floor cells and fails if a tint covers a void; the builder draws those in `BuildGalleriaZoneFloors`. Menu **Update Galleria Map (keeps the scene)**.
- **Rules** (`Core/AssistRules.cs`, unit-tested): `PingKind`, `CanPing`, `PingLifetime`, `DistanceFromRay`, `ReviveStep`, `ReviveHealth`, `RegenStep`, `IsRegenerating`.
- **Player** (`Network/NetworkPlayer.Assist.cs`): `TryPing` (classifies what is in the crosshair on the state authority), `Pinged`, `TryGetCrewPing`, `Revive`, `Revived`, `TickRegen`, `IsRegenerating`.
- **Input**: `InputButtons.Ping` (middle mouse, T).
- **Revive**: `IDCardDrop` knows its owner (`OwnerId`) and runs the revive (`ReviveProgress`, `CanBeRevivedBy`).
- **HUD**: `UI/AssistHUD.cs` (ping markers, revive markers, prompt, word for the downed player); feed and banners in `HeistHUD`; a "+" on the health readout.
- **Bots**: revive first (`TryReviveGoal`); a led bot walks to its crew's latest ping.
- **Config**: `allowRevive`, `reviveSeconds`, `reviveHealthFraction`, `healthRegenDelay`, `healthRegenPerSecond`, `healthRegenCap`.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7m.

### v0.18 — who gets a marker, cash-out markers, lift exits
- **Marks**: `NetworkPlayer.MarkFor(team, seconds)`, `IsMarkedFor(viewerTeam)`, `ClearMarks`, `FindLocal` (`Network/NetworkPlayer.Assist.cs`). A mark is per viewing crew (slot 0 is every crew). `TeammateMarkers` and the diamond in `Art/StatusEffects` show a rival only while marked for the viewer's crew. The Revealed status is unchanged as gameplay state; cameras, laser grids and snipers set it without marking.
- **Cash-out stations**: a bobbing coin marker and an always-on outline (`ThreatOutline.Objective`, `ThreatOutline.Always`).
- **Exits**: `ExtractionPoint` builds a lift car with four door leaves that open on `IsOpen`; `DoorYaw` is chosen by the state authority from the longer free axis. No colliders, no NavMesh change.
- **HUD**: `UI/ExitHUD.cs` (nearest open exit: a line and a world marker).

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7n.

### v0.19 — feel and clarity pass
- **Cash board**: `HeistHUD` builds the top-right board as the scoreboard: one large haul per crew, a banked/carried bar toward the quota, status, richest first, the local crew lit, figures that pop.
- **Objective**: `Core/ObjectiveRules.cs` (`Objective`, `ObjectiveFacts`, `Choose`, `WorthBanking`, `IsUrgent`; unit-tested) and `UI/ObjectiveHUD.cs` (tracker, briefing card, vault marker). The nearest-exit line from v0.18 is folded into the tracker; `ExitHUD` keeps its marker.
- **Feel**: `PlayerController` widens the view at a sprint and in a slide and dips on landing (owner's view only, scaled by the screen-shake setting); `HeistHUD` pulses red at low health and pops the carried readout.
- **Audit**: GAME_AUDIT_AND_ROADMAP.md, "Feel and clarity next to The Finals and Hyenas".

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7o.

### v0.20 — sound, compass, spectating
- **Sound** (`Core/ProceduralSfx.cs`, synthesised): footsteps (four takes), guard boots (three), landing, slide, pickup, coin drop, cash-out start, banked, alarm, countdown tick, lift bell, two pings, revive chime. `Audio/Footsteps.cs` plays steps by distance covered for players (`PlayerFeedback`) and guards (`BaseAIAgent.DressModel`). `MatchFeedback` plays the money and crew cues; `CashOutStation.Render` ticks the last ten seconds; `ExtractionPoint` rings on opening.
- **Compass**: `UI/CompassHUD.cs`. The top panel, banner, cash-out ticker and hack warning moved down 50 px.
- **Spectating**: `PlayerController.UpdateSpectate` moves the local camera behind a living crewmate while the player is down or away (`PlayerController.Spectating`). `HeistHUD` drops the death overlay while watching; `ObjectiveHUD` names the crewmate and the keys.
- No announcer voice, by decision.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7p.

### v0.21 — Grand Vaults
- **Rules** (`Core/VaultRules.cs`, unit-tested): `VaultMethod` (Key, Drill, Crack), `VaultKeyState`, `VaultOpening`, `VaultPerk` and its multipliers, `HoldStep`, `DrillStep`, `DrillAbandoned`, `DetailCleared`, `HaulValue`.
- **Vault** (`Interaction/GrandVault.cs`): sits beside a `VaultDoor` of the new lock type `Grand`. Holds the key (on the vault's guards, dropped, carried, used), the drill (set, defended, stopped), the loot laid out on opening, and the perk pedestal. Static event `Moment`. Builds its own door face, drill rig, key and pedestal.
- **Lock**: a `LootObjective` with `opensVault` set is a vault's lock console: cracking it calls `GrandVault.OpenByCrack` and spills nothing.
- **Perks**: `ExtractionHeistGameMode.TeamPerks`, `PerksOf`, `GrantTeamPerk`. Applied in `NetworkPlayer.ApplyDamage`, `TickRegen`, `IDCardDrop.TickRevive`, `CashOutStation`.
- **Guards**: `BaseAIAgent.AgentDied`.
- **Objective**: `UseVaultKey`, `DefendDrill`, `ClaimPerk`, `TakeVaultKey`, `CrackSafe`; `VaultAvailable` now means a Grand Vault is shut.
- **HUD**: `UI/VaultHUD.cs` (door card, perk card, key marker); drills in the cash-out ticker; announcements and perk tags in `HeistHUD`; vault pips on the compass.
- **Bots**: `BotBrain.TryGrandVaultGoal`.
- **Galleria**: Main Vault (key), Cash Cage (drill), Auction Strongroom (lock), Cold Store (any). `VaultPerk` and `VaultLock` items; door argument `Grand:<ways>:<perk>:<weak wall>`. Passcode note and blue keycard removed. Five guard posts added or promoted.
- The Freeport keeps the old vaults.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7q.

### v0.22 — class select and briefing, loot models, cameras
- **Before the match**: `PlayerController.IsHeldBeforeMatch` zeroes movement and buttons while the game mode is waiting or counting down (host and predicting client alike; bots too). `preMatchCountdown` is 10 s (asset, builder, config migration to version 3).
- **Briefing**: on the class-select screen (`ClassPickerUI`), with a large countdown. `ObjectiveHUD` keeps its card only as the fallback for a match with no class selection, and no longer shows it after the start.
- **Loot models**: `ArtefactBuilder.BuildCash`, `BuildCase`, `BuildIdCard`, and `LootShape.SeedArchive`. `LootDrop`, `IDCardDrop` and `LootCase` hide their prefab shapes and build these. `PlayerPresentation` no longer falls back to the prefab capsule.
- **Cameras**: `SecurityCamera.OnPlayerSeen` marks the player for every crew while in view, announces them (`Spotted` event, once per player per 10 s) and sends the five nearest guards (`AIBlackboard.DispatchNearest`, `BaseAIAgent.Dispatch`), repeating every 3 s while they stay in view.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7r.

### v0.23 — clean picture, vault worth, sound reach, prep phase and gear changes
- **Picture**: the ink pass no longer grades the colour or vignettes (shader defaults and `InkOutline.vignette`); the outlines are unchanged.
- **Vault worth**: `VaultRules.HaulTarget`, `FitsHaul`, `CashPiles` (unit-tested). `GrandVault.haulShareOfQuota` (0.8): items are laid until the next would pass the target and cash makes up the rest.
- **Sound**: `Audio/Sfx.cs` plays a clip at a place with a linear falloff to a stated reach (`Whisper`, `Close`, `Room`, `Shot`, `Blast`). Every `AudioSource.PlayClipAtPoint` is replaced. `PlayerFeedback` plays other players' sounds through it, each with its own reach. Footsteps carry 20 m (guards 16 m).
- **Prep**: `preMatchCountdown` 30 s (asset, builder, config migration to version 4).
- **Loadout during a match**: `ExtractionHeistGameMode.CanChangeLoadout`, `TeamSpawnPoint.InZone`, `NetworkPlayer.LastRedeployTick`, config `allowLoadoutChangeAtGate`, `gateRadius`, `loadoutSecondsAfterRedeploy`. `ClassPickerUI` opens with B where allowed; input is held while it is up.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7s.

### v0.24 — ammo, and a kit of two abilities and one tactical
- **Ammo**: `CombatRules.ReserveAfterPickup`, `WantsAmmo`, `Chance` (unit-tested). `NetworkLoadout.RefillAll` (called on redeploy), `WantsAmmo`, `TakeAmmoBox`. An ammo box is a `LootDrop` with `ItemId == LootDrop.AmmoItemId`; `ExtractionHeistGameMode.SpawnAmmo` leaves one for dead guards, dead players and broken crates and lockers. `ArtefactBuilder.BuildAmmo`. Config under Ammunition.
- **Kit size**: `GameConstants.ABILITIES_PER_CLASS = 2`. `BasePlayerClass.UseAbility` refuses a third; `ApplyClassData` ignores asset entries for abilities the class does not have.
- **Classes**: Cipher keeps Remote Hack and Net Sight; Spider keeps Place Trap and Stealth Mode; Smith has Remote Charge (place, then detonate: `AbilityVisualKind.ToggleCharge`) and Breaching Charge and loses the built-in grapple; Shield keeps Tactical Shield and moves Flashbang to key 2.
- **Tactical**: the Gadget slot. `Data.Kit.DefaultTactical` gives each specialist theirs on class assignment. The tactical key fires the grapple when the Grapple Line is carried.
- **HUD**: two ability cards and a tactical card; the class cards and controls list describe the kit.
- **Input**: gamepad D-pad down pings, D-pad left opens the gear screen.

Awaiting in-engine validation: PHASE0_BRINGUP.md section 7t.

### Phase 5 — Meta
- Lobby scene with class picker (`NetworkPlayer.RequestClass` is in place), session browser, late-join handling, host migration.
- Progression/cosmetics via `ClassData`/`EquipmentData`.

---

## 4. Conventions established in v0.2
- **Authority:** writes to `[Networked]` state only inside `if (Object.HasStateAuthority)`. Visuals react to changes through `ChangeDetector`/events, never by assuming an RPC arrived.
- **Timing:** `TickTimer` only. No `Invoke`, `Time.time`, or coroutines for gameplay.
- **Zones:** scan the static registries (`NetworkPlayer.All`, `BaseAIAgent.All`) in `FixedUpdateNetwork`; don't rely on Unity trigger callbacks.
- **Tunables:** `GameModeConfig` for match rules; `AbilitySlot` definitions for class abilities; serialized overrides on scene objects only when a specific instance must differ.
- **Superseded code** stays as `// [v0.1]` comments adjacent to its replacement.
