# Ouroboros - 4v4v4v4 Extraction Heist Game Architecture

## Overview
Ouroboros is a team-based extraction heist game built on **Photon Fusion 2** networking, designed for 4 teams of 4 players each (16 players total). The architecture is built with modularity in mind, allowing easy expansion of classes, equipment, tech, and AI.

## Architecture Principles
1. **Modularity** - Systems are decoupled and can be extended independently
2. **Data-Driven Design** - ScriptableObjects for easy designer configuration
3. **Network-First** - All core systems integrate with Photon Fusion 2
4. **Future-Proof** - Designed with expansion buffer for new content

## Core Systems

### 1. Player Class System (`Assets/Scripts/Classes/`)
The class system is built on interfaces and abstract base classes for maximum flexibility.

#### Base Components:
- **IPlayerClass** - Interface defining all class requirements
- **BasePlayerClass** - Abstract base providing common functionality
- **GameConstants.cs** - Core game constants and enums

#### Initial Classes:
1. **Hacker** - Electronic warfare specialist
   - Lower health (85), higher speed (5.5)
   - Abilities: System Hack, Camera Disable, EMP Blast, Data Mine
   
2. **Saboteur** - Stealth and trap specialist
   - Balanced stats (90 health, 6.0 speed)
   - Abilities: Place Trap, Stealth Mode, Sabotage, Smoke Bomb
   
3. **Demolitions** - Explosives expert
   - High health (110), lower speed (4.5)
   - Abilities: Place Explosive, Detonate, Breaching Charge, Incendiary
   
4. **Agent** - Versatile balanced operative
   - Balanced stats (100 health, 5.2 speed)
   - Abilities: Tactical Shield, Damage Boost, Recon Drone, Flashbang

#### Adding New Classes:
1. Add new enum to `PlayerClassType` in `GameConstants.cs`
2. Create new class inheriting from `BasePlayerClass`
3. Implement `UseAbility()` method with class-specific abilities
4. Update `NetworkPlayer.RPC_AssignClass()` to handle new class
5. Create corresponding `ClassData` ScriptableObject

### 2. Network System (`Assets/Scripts/Network/`)

#### Components:
- **NetworkPlayer** - Core networked player with Fusion integration
  - Syncs health, stamina, team, class type
  - Handles RPCs for abilities and damage
  - Manages player state across network

- **TeamManager** - Manages 4-team system
  - Auto-balances teams
  - Tracks team player counts
  - Network-synchronized team assignments

#### Key Features:
- State authority pattern for authoritative server
- RPC methods for client-server communication
- Network-synchronized properties using `[Networked]` attribute
- Tick-based timing with `TickTimer`

### 3. Game Mode System (`Assets/Scripts/GameMode/`)

#### Components:
- **ExtractionHeistGameMode** - Core game loop manager
  - Match timer (default: 15 minutes)
  - Objective tracking
  - Team scoring system
  - Extraction phase management
  
- **ExtractionPoint** - Physical extraction locations
  - Trigger-based detection
  - Team-specific extraction timing
  - Network-synchronized extraction state

#### Game Flow:
1. **WaitingForPlayers** - Lobby phase
2. **PreMatch** - Countdown before start
3. **InProgress** - Active gameplay
4. **Extraction** - Teams rush to extraction points
5. **MatchEnded** - Results and scoring

### 4. Equipment System (`Assets/Scripts/Equipment/`)

#### Components:
- **IEquipment** - Equipment interface
- **BaseEquipment** - Abstract equipment implementation
- **EquipmentLoadout** - Per-player equipment management
- **BaseTech** - Special tech/ability system

#### Equipment Slots:
1. Primary
2. Secondary
3. Utility
4. Gadget
5. Armor
6. Accessory

#### Adding New Equipment:
1. Create class inheriting from `BaseEquipment`
2. Override `OnUse()` method
3. Create `EquipmentData` ScriptableObject
4. Equipment automatically integrates with loadout system

### 5. AI System (`Assets/Scripts/AI/`)

#### Components:
- **IAIAgent** - AI agent interface
- **BaseAIAgent** - Abstract AI implementation with NavMesh
- **BehaviorTree.cs** - Behavior tree node system

#### AI States:
- Idle, Patrol, Alert, Combat, Fleeing, Investigating

#### AI Types (Expandable):
- Guard, Patrol, Elite, Boss, Civilian

#### Behavior Tree Nodes:
- **SequenceNode** - Execute children in order
- **SelectorNode** - Try children until success
- **ConditionNode** - Boolean condition checks
- **ActionNode** - Executable actions

#### Adding New AI:
1. Create class inheriting from `BaseAIAgent`
2. Implement state-specific behavior methods
3. Build behavior tree using node system
4. Add new `AIAgentType` if needed

### 6. Data System (`Assets/Scripts/Data/`)

#### ScriptableObjects:
- **ClassData** - Player class configuration
- **EquipmentData** - Equipment configuration
- **GameModeConfig** - Game mode settings

#### Benefits:
- Designer-friendly configuration
- No code changes for balance tweaks
- Runtime data loading
- Easy testing of variations

### 7. Player System (`Assets/Scripts/Player/`)

#### Components:
- **PlayerController** - Input handling and player control
  - WASD movement
  - Mouse look
  - Ability keys (1-4)
  - Equipment keys (LMB, RMB, Q, E)

## Network Integration

### Photon Fusion 2 Usage:
- **NetworkBehaviour** - Base for all networked components
- **[Networked]** - Property synchronization
- **[Rpc]** - Remote procedure calls
- **TickTimer** - Network-synchronized timing
- **PlayerRef** - Unique player identification
- **NetworkArray** - Fixed-size network arrays

### Authority Model:
- **StateAuthority** - Server-side authority for game state
- **InputAuthority** - Client-side input ownership
- **Proxies** - Other clients observing

## Expansion Guidelines

### Adding New Classes:
1. Define in `PlayerClassType` enum
2. Create class file in `Assets/Scripts/Classes/`
3. Implement unique abilities
4. Update network assignment logic
5. Create ScriptableObject data

### Adding New Equipment:
1. Inherit from `BaseEquipment`
2. Implement `OnUse()` logic
3. Create `EquipmentData` asset
4. Equipment slots auto-handle integration

### Adding New Tech:
1. Inherit from `BaseTech`
2. Implement `OnActivate()` and `OnDeactivate()`
3. Add to tech slots (max 3)

### Adding New AI Types:
1. Add to `AIAgentType` enum
2. Create class inheriting from `BaseAIAgent`
3. Build behavior tree
4. Configure spawning in game mode

### Adding New Game Modes:
1. Create `GameModeConfig` ScriptableObject
2. Inherit from or modify `ExtractionHeistGameMode`
3. Customize objectives and win conditions

## File Structure
```
Assets/
├── Scripts/
│   ├── Core/              # Base interfaces and constants
│   │   ├── IPlayerClass.cs
│   │   ├── BasePlayerClass.cs
│   │   └── GameConstants.cs
│   ├── Classes/           # Player class implementations
│   │   ├── HackerClass.cs
│   │   ├── SaboteurClass.cs
│   │   ├── DemolitionsClass.cs
│   │   └── AgentClass.cs
│   ├── Network/           # Photon Fusion integration
│   │   ├── NetworkPlayer.cs
│   │   └── TeamManager.cs
│   ├── GameMode/          # Game mode and objectives
│   │   ├── ExtractionHeistGameMode.cs
│   │   └── ExtractionPoint.cs
│   ├── Equipment/         # Equipment and tech systems
│   │   ├── BaseEquipment.cs
│   │   ├── EquipmentLoadout.cs
│   │   └── BaseTech.cs
│   ├── AI/                # AI and behavior trees
│   │   ├── BaseAIAgent.cs
│   │   └── BehaviorTree.cs
│   ├── Data/              # ScriptableObject definitions
│   │   ├── ClassData.cs
│   │   ├── EquipmentData.cs
│   │   └── GameModeConfig.cs
│   └── Player/            # Player control and input
│       └── PlayerController.cs
```

## Key Design Patterns

1. **Interface Segregation** - Small, focused interfaces (IPlayerClass, IEquipment, IAIAgent)
2. **Template Method** - Abstract base classes with customizable hooks
3. **Strategy Pattern** - Swappable class behaviors
4. **Observer Pattern** - Event-driven ability triggers
5. **Command Pattern** - Network RPC calls
6. **Composite Pattern** - Behavior tree nodes
7. **Data-Driven Design** - ScriptableObjects for configuration

## Constants and Configuration

### Team System:
- Max Teams: 4
- Players Per Team: 4
- Total Max Players: 16

### Ability System:
- Max Ability Slots: 4
- Max Equipment Slots: 6
- Max Tech Slots: 3

## Network Synchronization

All player state is synchronized via:
- Health/Stamina via `[Networked]`
- Abilities via RPC calls
- Team assignments via TeamManager
- Match state via GameMode

## Performance Considerations

1. **Network Optimization**
   - Use NetworkArray for fixed-size collections
   - Minimize RPC calls
   - Batch state updates

2. **Memory Management**
   - Object pooling for projectiles/effects
   - ScriptableObjects for shared data
   - Efficient collision detection

3. **Scalability**
   - Modular systems support easy additions
   - Data-driven approach reduces code changes
   - Clear separation of concerns

## Testing Strategy

1. Test individual classes in isolation
2. Test network synchronization with multiple clients
3. Test team balancing with various player counts
4. Test extraction mechanics under load
5. Test AI behavior in different scenarios

## Future Expansion Points

### Ready for Expansion:
- ✅ New player classes (extend PlayerClassType enum)
- ✅ New equipment types (inherit BaseEquipment)
- ✅ New tech abilities (inherit BaseTech)
- ✅ New AI types (inherit BaseAIAgent)
- ✅ New game modes (inherit or configure GameMode)
- ✅ Progression systems (add to ClassData)
- ✅ Skill trees (extend ClassData)
- ✅ Crafting systems (use EquipmentData)

### Architecture Supports:
- Dynamic loadouts
- Cosmetic systems
- Achievement tracking
- Matchmaking integration
- Replay systems
- Spectator mode
- Tournament features

## Getting Started

1. **Setup Photon Fusion 2**
   - Import Photon Fusion 2 SDK
   - Configure App ID
   - Set up network scene

2. **Create Class Data Assets**
   - Right-click → Create → Ouroboros → Class Data
   - Configure stats and abilities

3. **Setup Network Prefabs**
   - Add NetworkPlayer component
   - Add PlayerController component
   - Configure in Fusion settings

4. **Create Game Mode**
   - Add ExtractionHeistGameMode to scene
   - Configure TeamManager
   - Place ExtractionPoints

5. **Test Locally**
   - Build and test with multiple instances
   - Verify network synchronization
   - Test class abilities

## Support and Contribution

For adding new features:
1. Follow the modular architecture patterns
2. Use interfaces for extensibility
3. Create ScriptableObjects for data
4. Integrate with Photon Fusion properly
5. Document expansion points

This architecture provides a solid foundation for a 4v4v4v4 extraction heist game with room to grow into a full-featured multiplayer experience.
