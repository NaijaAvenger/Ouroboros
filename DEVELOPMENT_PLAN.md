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

### Phase 0 — Bring-up in Unity (next, blocking everything else)
1. Open in Unity 2021.3+ (2022.3 LTS recommended), import Fusion 2, add **AI Navigation** package (2022.2+).
2. Build the player prefab: `NetworkObject`, `NetworkTransform`, `CharacterController`, `NetworkPlayer`, `PlayerController`, child camera. Register it in the Fusion prefab table.
3. Scene: `GameSessionManager`, `TeamManager` + `ExtractionHeistGameMode` as scene NetworkObjects, 4+ `TeamSpawnPoint`s, 2–4 `ExtractionPoint`s, 3+ `LootObjective`s, a `GameModeConfig` asset (set `minPlayersToStart = 1` for solo testing).
4. Fix any compile errors from SDK signature drift; run Host + 1 client (ParrelSync or two builds).
5. Validate: spawn/teams, movement prediction, sprint drain, abilities on cooldown, objective crack → loot → extraction → scoring → winner.

### Phase 1 — Feel and clarity
- Minimal HUD: health/stamina, cooldown pips, status icons, carried loot, match/phase timers, objective & extraction progress bars, team scores. All data already replicated.
- Kill feed + death/respawn flow (`Died`/`Respawned` events are wired).
- Audio/VFX hooks on the existing RPC stubs.
- Data-driven class stats: load `ClassData` into `BasePlayerClass` (today's values are code defaults; `ClassData` fields exist). Move `trapPrefab`/VFX prefabs into `ClassData` so runtime-added class components can reference them.

### Phase 2 — Weapons & equipment
- Networked `BaseEquipment` with tick-timer cooldowns and server-side execution (currently local `Time.time`).
- Hitscan primary + projectile secondary using `DamageUtil.Hitscan` / lag compensation.
- Loadout selection in lobby and `EquipmentData` → runtime instantiation.

### Phase 3 — Heist depth
- Alarm system: cameras/guards raise an alarm level that spawns reinforcements and shortens extraction windows.
- Objective variety (hack terminal, carry-the-case, multi-stage vault) behind the `IHackable`/`IBreachable` seams.
- Loot weight: carried loot slows movement; encourages mid-match extraction decisions.
- Shared-mode polish: team assignment via TeamManager RPC so a joining client can request a team.

### Phase 4 — AI
- AI spawner + pooling; `SniperGuard`, `Elite`; squad coordination via shared blackboard; alarm-driven aggression tiers.
- NavMesh links for doors; `BreachableDoor` carves the NavMesh.

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
