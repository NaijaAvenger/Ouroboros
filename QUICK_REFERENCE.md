# Quick Reference Guide - Ouroboros

## File Organization

### Core System (`Assets/Scripts/Core/`)
- `IPlayerClass.cs` - Interface for all player classes
- `BasePlayerClass.cs` - Abstract base class with common functionality
- `GameConstants.cs` - Game-wide constants and enums

### Player Classes (`Assets/Scripts/Classes/`)
- `HackerClass.cs` - Electronic warfare specialist (85 HP, 5.5 Speed)
- `SaboteurClass.cs` - Stealth and traps (90 HP, 6.0 Speed)
- `DemolitionsClass.cs` - Explosives expert (110 HP, 4.5 Speed)
- `AgentClass.cs` - Balanced operative (100 HP, 5.2 Speed)

### Network System (`Assets/Scripts/Network/`)
- `NetworkPlayer.cs` - Main networked player component (Photon Fusion)
- `TeamManager.cs` - 4-team management and balancing

### Game Mode (`Assets/Scripts/GameMode/`)
- `ExtractionHeistGameMode.cs` - Main game loop and match management
- `ExtractionPoint.cs` - Extraction zone implementation

### Equipment (`Assets/Scripts/Equipment/`)
- `BaseEquipment.cs` - Base class for all equipment
- `EquipmentLoadout.cs` - Player equipment management (6 slots)
- `BaseTech.cs` - Special tech/abilities system

### AI System (`Assets/Scripts/AI/`)
- `BaseAIAgent.cs` - Base AI with NavMesh and state machine
- `BehaviorTree.cs` - Behavior tree node system

### Data (`Assets/Scripts/Data/`)
- `ClassData.cs` - ScriptableObject for class configuration
- `EquipmentData.cs` - ScriptableObject for equipment
- `GameModeConfig.cs` - ScriptableObject for game mode settings

### Player (`Assets/Scripts/Player/`)
- `PlayerController.cs` - Player input and movement

## Key Constants

```csharp
MAX_TEAMS = 4
PLAYERS_PER_TEAM = 4
MAX_PLAYERS = 16
MAX_ABILITY_SLOTS = 4
MAX_EQUIPMENT_SLOTS = 6
MAX_TECH_SLOTS = 3
```

## Team IDs
```csharp
TeamAlpha = 1
TeamBravo = 2
TeamCharlie = 3
TeamDelta = 4
```

## Player Class Types
```csharp
Hacker = 1
Saboteur = 2
Demolitions = 3
Agent = 4
```

## Equipment Slot Types
```csharp
Primary
Secondary
Utility
Gadget
Armor
Accessory
```

## AI Agent Types
```csharp
Guard
Patrol
Elite
Boss
Civilian
```

## AI Behavior States
```csharp
Idle
Patrol
Alert
Combat
Fleeing
Investigating
```

## Game States
```csharp
WaitingForPlayers
PreMatch
InProgress
Extraction
MatchEnded
```

## Controls

### Movement
- `W/A/S/D` - Move
- `Space` - Jump
- `Mouse` - Look

### Abilities
- `1` - Ability 1
- `2` - Ability 2
- `3` - Ability 3
- `4` - Ability 4

### Equipment
- `LMB` - Primary
- `RMB` - Secondary
- `Q` - Utility
- `E` - Gadget

## Quick Setup Steps

1. **Import Photon Fusion 2**
   - Add via Package Manager or Asset Store
   - Configure App ID

2. **Create Network Scene**
   - Add NetworkRunner
   - Configure Fusion settings
   - Add NetworkPlayer prefab

3. **Setup Game Mode**
   - Add ExtractionHeistGameMode to scene
   - Add TeamManager
   - Place ExtractionPoints

4. **Configure Classes**
   - Create ClassData assets
   - Assign to player spawner

5. **Test**
   - Build multiple instances
   - Test network synchronization

## Common Tasks

### Spawn a Player with Class
```csharp
var player = Runner.Spawn(playerPrefab, position, rotation, inputAuthority);
var networkPlayer = player.GetComponent<NetworkPlayer>();
networkPlayer.AssignClass(PlayerClassType.Hacker);
```

### Use an Ability
```csharp
networkPlayer.UseAbility(0); // First ability
```

### Equip Item
```csharp
var equipment = gameObject.AddComponent<YourEquipment>();
equipmentLoadout.EquipItem(equipment);
```

### Award Team Points
```csharp
gameModeManager.AwardTeamScore(TeamID.TeamAlpha, 100);
```

### Trigger Extraction
```csharp
gameModeManager.TeamExtractedSuccessfully(TeamID.TeamAlpha);
```

## Network Patterns

### Networked Property
```csharp
[Networked] public float Health { get; set; }
```

### RPC Call
```csharp
[Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
private void RPC_UseAbility(int index) { }
```

### Check Authority
```csharp
if (Object.HasStateAuthority) { }
if (Object.HasInputAuthority) { }
```

### Network Timer
```csharp
[Networked] public TickTimer Timer { get; set; }
Timer = TickTimer.CreateFromSeconds(Runner, 10f);
if (Timer.Expired(Runner)) { }
```

## Class Stats Reference

| Class       | Health | Speed | Role              |
|-------------|--------|-------|-------------------|
| Hacker      | 85     | 5.5   | Electronic warfare|
| Saboteur    | 90     | 6.0   | Stealth/Traps    |
| Demolitions | 110    | 4.5   | Explosives       |
| Agent       | 100    | 5.2   | Balanced         |

## Ability Indices

### Hacker
0. System Hack
1. Disable Camera
2. EMP Blast
3. Data Mine

### Saboteur
0. Place Trap
1. Stealth Mode
2. Sabotage Equipment
3. Smoke Bomb

### Demolitions
0. Place Explosive
1. Detonate All
2. Breaching Charge
3. Incendiary Grenade

### Agent
0. Tactical Shield
1. Damage Boost
2. Recon Drone
3. Flashbang

## Extension Points

### Add New Class
1. Add to `PlayerClassType` enum
2. Create class file inheriting `BasePlayerClass`
3. Update `NetworkPlayer.RPC_AssignClass()`
4. Create `ClassData` asset

### Add New Equipment
1. Create class inheriting `BaseEquipment`
2. Override `OnUse()`
3. Create `EquipmentData` asset

### Add New AI
1. Create class inheriting `BaseAIAgent`
2. Implement behavior methods
3. Build behavior tree

### Add New Game Mode
1. Create `GameModeConfig` asset
2. (Optional) Extend `ExtractionHeistGameMode`
3. Configure objectives

## Debugging Tips

### Check Network Sync
- Use Fusion Stats Monitor
- Check `[Networked]` properties in inspector
- Verify RPCs are being called

### Test Locally
- Build standalone builds
- Run multiple instances
- Test with network simulation

### Common Issues
- **Players not syncing**: Check authority
- **Abilities not working**: Verify RPC setup
- **Teams unbalanced**: Check TeamManager
- **AI not moving**: Check NavMesh

## Performance Tips

1. Use object pooling for frequently spawned objects
2. Minimize RPC calls - batch when possible
3. Use NetworkArray for fixed-size collections
4. Cache component references
5. Avoid expensive operations in FixedUpdateNetwork

## Documentation Files

- `README.md` - Project overview
- `ARCHITECTURE.md` - Detailed architecture documentation
- `IMPLEMENTATION_GUIDE.md` - Examples for adding content
- `QUICK_REFERENCE.md` - This file

## Support

For detailed information, see:
- Full architecture: `ARCHITECTURE.md`
- Implementation examples: `IMPLEMENTATION_GUIDE.md`
- Code comments in source files
