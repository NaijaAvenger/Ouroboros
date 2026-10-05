# Quick Reference Guide - Ouroboros

## File Organization

### Core (`Assets/Scripts/Core/`)
- `GameConstants.cs` - Limits, `TeamID`, `PlayerClassType`, `StatusFlags`, `InputButtons`, `TeamUtil`
- `IPlayerClass.cs` / `BasePlayerClass.cs` - Class contract, ability gate, `AbilitySlot`
- `IAbilityContext.cs` - Owner services a class may use
- `NetworkInputData.cs` - `INetworkInput` sent each tick
- `SceneUtil.cs` - Version-safe scene lookups

### Classes (`Assets/Scripts/Classes/`)
- `HackerClass.cs` (85 HP, 5.5) · `SaboteurClass.cs` (90 HP, 6.0) · `DemolitionsClass.cs` (110 HP, 4.5) · `AgentClass.cs` (100 HP, 5.2)

### Network (`Assets/Scripts/Network/`)
- `NetworkPlayer.cs` - Authoritative player state, abilities, damage, loot, extraction
- `TeamManager.cs` - Replicated team assignments and team status
- `GameSessionManager.cs` - Runner start, spawning, respawns, input sampling

### Game Mode (`Assets/Scripts/GameMode/`)
- `ExtractionHeistGameMode.cs` - Match state machine, scoring, winner
- `ExtractionPoint.cs` - Extraction zone (contested, reusable)
- `LootObjective.cs` - Hold-to-crack vault
- `LootDrop.cs` - Dropped loot pickup

### Interaction / Combat
- `Interaction/Interfaces.cs` - `IHackable`, `ISecurityDevice`, `IBreachable`
- `Interaction/ProximityTrap.cs`, `SecurityCamera.cs`, `BreachableDoor.cs`
- `Combat/IDamageable.cs`, `Combat/DamageUtil.cs`

### AI (`Assets/Scripts/AI/`)
- `BaseAIAgent.cs` - Networked AI base with perception and nav helpers
- `Perception.cs` - FOV / LOS / reveal-aware visibility
- `BehaviorTree.cs` - Sequence, Selector, Inverter, Condition/Action (+ delegate variants)
- `GuardAI.cs` - Patrol / investigate / fight (extensible: `EliteGuard.cs`, `SniperGuard.cs`), `AIBlackboard.cs` shared sightings + noise, `AISpawner.cs`

### Heist layer (v0.5)
- `GameMode/AlarmSystem.cs`, `GameMode/LootCase.cs`, `Interaction/HackTerminal.cs`, `AI/AISpawner.cs`

### Equipment (v0.4)
- `Equipment/NetworkLoadout.cs` (replicated slots/ammo/cooldowns), `EquipmentLoadout.cs` (local components), `BaseEquipment.cs`
- `Equipment/HitscanWeapon.cs`, `ProjectileWeapon.cs`, `Projectile.cs`, `PassiveGear.cs`
- `Data/EquipmentData.cs` (kind + weapon stats), `Data/EquipmentRegistry.cs` (network ids)

### Data / Player / Equipment / UI
- `Data/GameModeConfig.cs` (all match rules), `ClassData.cs`, `ClassRegistry.cs`, `EquipmentData.cs`
- `Player/PlayerController.cs`, `Player/TeamSpawnPoint.cs`, `Player/PlayerPresentation.cs`
- `UI/HeistHUD.cs` (UI Toolkit HUD), `UI/ClassPickerUI.cs` (pre-match picker), `UI/MatchFeedback.cs`, `UI/DevHUD.cs` (F1 debug overlay)
- `Core/LocalInputSource.cs` (only device reader), `Core/HeistInputActions.cs` (rebindable Input System actions)
- `Data/FeedbackLibrary.cs`, `Player/PlayerFeedback.cs` (audio/VFX hooks)
- `Assets/Editor/OuroborosDevSceneBuilder.cs` — menu *Ouroboros > Setup > Create Dev Scene*
- `Equipment/BaseEquipment.cs`, `EquipmentLoadout.cs`, `BaseTech.cs`

## Key Constants
```csharp
MAX_TEAMS = 4            PLAYERS_PER_TEAM = 4        MAX_PLAYERS = 16
MAX_ABILITY_SLOTS = 4    MAX_EQUIPMENT_SLOTS = 6     MAX_TECH_SLOTS = 3
STAMINA_REGEN_PER_SECOND = 10   SPRINT_STAMINA_DRAIN_PER_SECOND = 15   STAMINA_REGEN_DELAY = 1s
```

## Enums
- `TeamID`: None, TeamAlpha=1, TeamBravo, TeamCharlie, TeamDelta
- `PlayerClassType`: None, Hacker=1, Saboteur, Demolitions, Agent
- `StatusFlags` (bit flags): Shielded, DamageBoost, Stealthed, Flashed, EMPDisabled, Revealed, Burning, Sprinting, Interacting, Extracted
- `InputButtons`: Jump, Sprint, Interact, Ability1-4, Primary, Secondary, Utility, Gadget
- `ExtractionHeistGameMode.GameState`: WaitingForPlayers, PreMatch, InProgress, Extraction, MatchEnded
- `AIAgentType`: Guard, Patrol, Elite, Boss, Civilian, Sniper · `AIBehaviorState`: Idle, Patrol, Alert, Combat, Fleeing, Investigating

## Controls
| Key | Action |
|-----|--------|
| W/A/S/D | Move |
| Shift | Sprint |
| Space | Jump |
| Mouse | Look |
| 1 – 4 | Abilities |
| F (hold) | Interact / crack objective |
| LMB / RMB | Primary / Secondary |
| Q / E | Utility / Gadget |
| Esc | Toggle cursor |

## Quick Setup Steps
1. Import Fusion 2, set App ID; add AI Navigation package (2022.2+)
2. Player prefab: NetworkObject, NetworkTransform, CharacterController, NetworkPlayer, PlayerController, camera child → register in prefab table
3. Scene: GameSessionManager (assign prefab), TeamManager, ExtractionHeistGameMode (+ GameModeConfig asset), TeamSpawnPoints, ExtractionPoints, LootObjectives
4. Press Play (AutoHostOrClient); second instance joins

## Common Tasks

### Spawn is automatic — but to assign a class later
```csharp
networkPlayer.AssignClass(PlayerClassType.Hacker);   // state authority
networkPlayer.RequestClass(PlayerClassType.Hacker);  // from the owning client (pre-match only)
```

### Use an ability (normally driven by input)
```csharp
bool used = networkPlayer.UseAbility(0);
float cd = networkPlayer.AbilityCooldownRemaining(0);
```

### Deal damage
```csharp
target.ApplyDamage(25f, attackerNetworkPlayer);                      // any IDamageable
DamageUtil.ApplyRadialDamage(pos, 6f, 60f, attacker, attacker.Team);
```

### Status effects
```csharp
player.ApplyTimedStatus(StatusFlags.Revealed, 5f);
if (player.HasStatus(StatusFlags.Stealthed)) { ... }
```

### Loot and scoring
```csharp
player.AddLoot(250);
gameMode.AwardTeamScore(TeamID.TeamAlpha, 100);
gameMode.OnTeamExtractionCompleted(team, membersInZone);
```

### Match control
```csharp
gameMode.StartMatch();            // or automatic via GameModeConfig.autoStart
gameMode.MatchTimeRemaining;      // float?
ExtractionHeistGameMode.StateChanged += state => ...;
```

## Network Patterns
```csharp
[Networked] public float Health { get; set; }
[Networked, Capacity(4)] public NetworkArray<TickTimer> Cooldowns => default;
[Networked, Capacity(16)] public NetworkDictionary<PlayerRef, TeamID> Teams => default;

if (!Object.HasStateAuthority) return;              // before any write

TickTimer t = TickTimer.CreateFromSeconds(Runner, 10f);
if (t.Expired(Runner)) { ... }                       // never Time.time / Invoke

if (GetInput(out NetworkInputData input)) { ... }    // runs on input + state authority
var pressed = input.Buttons.GetPressed(PreviousButtons);

changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);  // in Spawned
foreach (var c in changeDetector.DetectChanges(this)) { ... }                // in Render
```

## Ability Indices
| Slot | Hacker | Saboteur | Demolitions | Agent |
|------|--------|----------|-------------|-------|
| 0 | System Hack | Place Trap | Place Explosive | Tactical Shield |
| 1 | Disable Camera | Stealth Mode | Detonate | Damage Boost |
| 2 | EMP Blast | Sabotage | Breaching Charge | Recon Drone |
| 3 | Data Mine | Smoke Bomb | Incendiary | Flashbang |

## Debugging Tips
- **Players not syncing**: prefab missing `NetworkTransform`, or a write outside state authority
- **Abilities do nothing**: check `AbilityCooldownRemaining`, stamina, `EMPDisabled`, and that the press reached the server (`GetInput` true?)
- **No spawn**: `GameSessionManager.playerPrefab` unassigned or not in the prefab table
- **Match never starts**: `GameModeConfig.minPlayersToStart` too high for your test
- **AI static**: NavMesh not baked / AI Navigation package missing
- **Objective won't crack**: hold **F** inside the radius, match must be InProgress, enemy inside pauses it
