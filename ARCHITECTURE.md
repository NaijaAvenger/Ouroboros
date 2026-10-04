# Ouroboros - 4v4v4v4 Extraction Heist Game Architecture

## Overview
Ouroboros is a team-based extraction heist game built on **Photon Fusion 2** networking, designed for 4 teams of 4 players each (16 players total). The architecture is built with modularity in mind, allowing easy expansion of classes, equipment, interactables, and AI.

## Architecture Principles
1. **Modularity** - Systems are decoupled and can be extended independently
2. **Data-Driven Design** - ScriptableObjects for designer configuration
3. **Network-First** - One authoritative copy of every piece of state; peers react to replicated changes
4. **Tick-Deterministic** - All gameplay timing uses Fusion `TickTimer`s and runs in `FixedUpdateNetwork`

## Runtime Topology

```
GameSessionManager (MonoBehaviour, INetworkRunnerCallbacks)
 ├─ starts NetworkRunner, samples local input → NetworkInputData
 ├─ OnPlayerJoined → TeamManager.AssignPlayerToTeam → Runner.Spawn(playerPrefab)
 └─ RespawnPlayer()

Scene NetworkObjects
 ├─ TeamManager            NetworkDictionary<PlayerRef, TeamID>
 ├─ ExtractionHeistGameMode state machine, scores, loot banking, winner
 ├─ ExtractionPoint ×N     per-tick zone scan, contested progress
 ├─ LootObjective ×N       hold-to-crack, loot split
 ├─ SecurityCamera / BreachableDoor / GuardAI
 └─ (spawned) LootDrop, ProximityTrap

Player prefab
 ├─ NetworkObject + NetworkTransform + CharacterController
 ├─ NetworkPlayer   ← source of truth (IAbilityContext, IDamageable)
 │    └─ BasePlayerClass component (added locally from replicated ClassType)
 ├─ PlayerController  GetInput() → movement/abilities/interact
 └─ EquipmentLoadout
```

## Core Systems

### 1. Player Class System (`Core/`, `Classes/`)
- **IPlayerClass / BasePlayerClass** — stats, ability slot table, `UseAbility` gate (slot valid → alive → cooldown → stamina) → `ExecuteAbility`. Returns `false` to refund the cooldown when nothing happened.
- **IAbilityContext** — the owner's services: `Runner`, team, stamina spend, cooldown start, status flags, tick timers, aim data. `NetworkPlayer` implements it; tests can fake it.
- **AbilitySlot** — `(Name, Cooldown, StaminaCost, Duration)` registered in `OnInitialize` via `DefineAbility`.
- Timed effects (shield, stealth, boost) use `TickTimer`s checked in `OnUpdate`; their visibility is a `StatusFlags` bit on `NetworkPlayer`.
- Damage hooks: `ModifyIncomingDamage(float)` and `OutgoingDamageMultiplier`.

#### Adding New Classes
1. Add the enum value to `PlayerClassType`
2. Create a class inheriting `BasePlayerClass`; call `DefineAbility` for each slot in `OnInitialize`; implement `ExecuteAbility`
3. Add one line to `NetworkPlayer.ClassComponentType`
4. (Optional) create a `ClassData` asset for designer tuning

### 2. Network System (`Network/`)
- **NetworkPlayer** — replicated: `ClassType`, `Health`, `Stamina`, `IsAlive`, `IsExtracted`, `Status` (flags), `CarriedLoot`, `Kills/Deaths/RespawnsUsed`, `RespawnTimer`, `LookYaw/Pitch`, `AbilityCooldowns[4]`, per-flag status timers, burning DOT. Class component is rebuilt on every peer through `ChangeDetector`. C# events (`ClassChanged`, `Died`, `Respawned`, `StatusChanged`) feed presentation.
- **TeamManager** — replicated assignments; `GetTeamStatus` / `IsTeamFinished` derive match state from the live player registry.
- **GameSessionManager** — runner lifecycle, spawning, respawn placement, input sampling (accumulated per frame, emitted per tick).

#### Authority rules
- `[Networked]` writes only under `Object.HasStateAuthority`.
- Input-authority clients predict movement (`GetInput` runs on both sides); abilities, interaction and equipment dispatch only on the state authority.
- RPCs are used for one-shot notifications (logs, VFX hooks), never to carry state late joiners would need.

### 3. Game Mode System (`GameMode/`)
- **ExtractionHeistGameMode** — `WaitingForPlayers → PreMatch → InProgress → Extraction → MatchEnded`. Reads every tunable from `GameModeConfig`. Scores kills, objectives, extractions, banked loot. Decides respawn and extraction eligibility. Early-ends when every team is finished. Picks the winner with tiebreaks. Spawns `LootDrop` on death.
- **ExtractionPoint** — scans `NetworkPlayer.All` each tick; the lowest-id eligible team starts; enemies pause progress (contested); completion extracts the members present and closes the point for `extractionReactivationDelay`.
- **LootObjective** — players holding **Interact** inside the radius crack it; contested by enemies; more workers crack faster; `IHackable` for the Hacker; loot split among workers; respawns.
- **LootDrop** — picked up by any living player after a short delay.

### 4. Interaction & Combat (`Interaction/`, `Combat/`)
- `IHackable`, `ISecurityDevice`, `IBreachable` are the seams abilities talk to. Implementations: `BreachableDoor`, `SecurityCamera`, `LootObjective`, `ProximityTrap`.
- `IDamageable` unifies players and AI; `DamageUtil` provides radial (falloff, optional LOS) and hitscan damage.

### 5. Equipment System (`Equipment/`)
Unchanged structurally (6 slots, `BaseEquipment`, `BaseTech`). Equipment use is dispatched from `PlayerController` on the state authority. Networking the equipment itself (tick-timer cooldowns, projectiles) is Phase 2.

### 6. AI System (`AI/`)
- **BaseAIAgent** — `NetworkBehaviour`; ticks on the state authority; replicated `Health`, `IsAlive`, `NetworkedState`; perception (`Perception.FindVisiblePlayer`: range, FOV, occlusion, stealth/reveal aware); last-known-position memory; `MoveTo`, `FaceTowards`, hitscan `TryAttackTarget` with cooldown. Proxies disable their `NavMeshAgent` and follow `NetworkTransform`.
- **BehaviorTree** — `Sequence`, `Selector` (reactive, resets displaced branches), `Inverter`, `ConditionNode`/`ActionNode`, and delegate-based `ConditionFunc`/`ActionFunc`.
- **GuardAI** — priority selector: fight visible target → investigate last-known position → patrol.

#### Adding New AI
1. Inherit `BaseAIAgent`; build a tree in `OnInitialize`; evaluate it in `UpdateBehavior`
2. Override `TryAttackTarget` / `UpdatePerception` for different weapons or senses
3. Add an `AIAgentType` if needed

### 7. Data System (`Data/`)
- **GameModeConfig** — the only place match rules live (durations, extraction rules, scoring, respawns, friendly fire).
- **ClassData / EquipmentData** — designer-facing definitions (wiring `ClassData` into `BasePlayerClass` is on the roadmap).

### 8. Player System (`Player/`)
- **PlayerController** — consumes `NetworkInputData`; yaw from input, pitch replicated for aim; sprint with stamina drain; jump; `Teleport` for spawns.
- **TeamSpawnPoint** — scene marker with team and radius.

## Network Integration (Photon Fusion 2)
- `NetworkBehaviour`, `[Networked]`, `NetworkArray`, `NetworkDictionary`, `TickTimer`, `ChangeDetector`, `NetworkButtons`, `INetworkInput`, `INetworkRunnerCallbacks`, `NetworkPrefabRef`, `Runner.Spawn/Despawn`, `SetPlayerObject`.
- Supported topologies: **Host/Server** (fully); **Shared** (spawns per-client; team assignment on the master client — see roadmap).

## File Structure
```
Assets/Scripts/
├── Core/         GameConstants, IPlayerClass, BasePlayerClass, IAbilityContext, NetworkInputData, SceneUtil
├── Classes/      HackerClass, SaboteurClass, DemolitionsClass, AgentClass
├── Network/      NetworkPlayer, TeamManager, GameSessionManager
├── GameMode/     ExtractionHeistGameMode, ExtractionPoint, LootObjective, LootDrop
├── Interaction/  Interfaces, ProximityTrap, SecurityCamera, BreachableDoor
├── Combat/       IDamageable, DamageUtil
├── Equipment/    BaseEquipment, EquipmentLoadout, BaseTech
├── AI/           BaseAIAgent, Perception, BehaviorTree, GuardAI
├── Data/         ClassData, EquipmentData, GameModeConfig
└── Player/       PlayerController, TeamSpawnPoint
```

## Testing Strategy
1. Host + client: spawn, team assignment, class component on both peers, late join
2. Movement prediction / sprint drain / jump
3. Abilities: cooldown gating, stamina cost, status flags visible on proxies
4. Objective crack → loot → death drop → pickup → extraction → score → winner
5. Extraction contest pause and reactivation
6. GuardAI: patrol, detection, chase, attack, investigate, give-up
7. Respawn limits and extraction-phase lockout

## Future Expansion Points
See [DEVELOPMENT_PLAN.md](DEVELOPMENT_PLAN.md) for the phased roadmap.
