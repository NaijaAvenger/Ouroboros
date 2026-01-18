# Ouroboros
## 4v4v4v4 Team-Based Extraction Heist Game

A modular multiplayer extraction heist game built with **Photon Fusion 2**, featuring 4 teams of 4 players competing to complete objectives and successfully extract with their loot.

## Overview
Ouroboros is designed as a highly modular and expandable game architecture supporting:
- **4 Teams** of 4 players each (16 players total)
- **4 Initial Classes**: Hacker, Saboteur, Demolitions, Agent
- **Extraction-based gameplay** with dynamic objectives
- **Modular systems** for easy expansion of classes, equipment, tech, and AI

## Features

### Core Systems
- ✅ **Modular Class System** - Easy to add new player classes
- ✅ **Network Integration** - Built on Photon Fusion 2
- ✅ **Team Management** - Automatic 4-team balancing
- ✅ **Extraction Mechanics** - Time-based extraction zones
- ✅ **Equipment System** - 6 equipment slots per player
- ✅ **Tech/Ability System** - Special abilities and tech
- ✅ **AI Framework** - Behavior tree-based AI system
- ✅ **Data-Driven Design** - ScriptableObjects for configuration

### Player Classes

#### 🔹 Hacker
Electronic warfare specialist with system infiltration capabilities
- **Stats**: 85 HP, 5.5 Speed
- **Abilities**: System Hack, Camera Disable, EMP Blast, Data Mine

#### 🔹 Saboteur
Stealth operative skilled in traps and silent operations
- **Stats**: 90 HP, 6.0 Speed
- **Abilities**: Place Trap, Stealth Mode, Sabotage, Smoke Bomb

#### 🔹 Demolitions
Explosives expert for breaching and area denial
- **Stats**: 110 HP, 4.5 Speed
- **Abilities**: Place Explosive, Detonate, Breaching Charge, Incendiary

#### 🔹 Agent
Versatile balanced operative with tactical support
- **Stats**: 100 HP, 5.2 Speed
- **Abilities**: Tactical Shield, Damage Boost, Recon Drone, Flashbang

## Architecture
The game is built with modularity and expansion in mind. See [ARCHITECTURE.md](ARCHITECTURE.md) for detailed documentation.

### Directory Structure
```
Assets/Scripts/
├── Core/         # Base interfaces and constants
├── Classes/      # Player class implementations
├── Network/      # Photon Fusion integration
├── GameMode/     # Game modes and objectives
├── Equipment/    # Equipment and tech systems
├── AI/           # AI agents and behavior trees
├── Data/         # ScriptableObject definitions
└── Player/       # Player control and input
```

## Getting Started

### Prerequisites
- Unity 2021.3+ (LTS)
- Photon Fusion 2 SDK
- Photon Account & App ID

### Installation
1. Clone the repository
2. Open in Unity
3. Import Photon Fusion 2 from Package Manager or Asset Store
4. Configure your Photon App ID in Fusion settings
5. Open the main scene and test

### Quick Setup
1. **Create Class Data**: Right-click → Create → Ouroboros → Class Data
2. **Setup Network**: Add NetworkPlayer prefab to Fusion settings
3. **Configure Game Mode**: Add ExtractionHeistGameMode to scene
4. **Place Extraction Points**: Add ExtractionPoint components around map
5. **Test**: Build and run multiple instances

## Expansion Guide

### Adding New Classes
1. Add enum to `PlayerClassType` in `GameConstants.cs`
2. Create new class inheriting from `BasePlayerClass`
3. Implement abilities in `UseAbility()` method
4. Update `NetworkPlayer.RPC_AssignClass()`
5. Create `ClassData` ScriptableObject

### Adding New Equipment
1. Inherit from `BaseEquipment`
2. Override `OnUse()` method
3. Create `EquipmentData` ScriptableObject
4. Assign to equipment slots

### Adding New AI
1. Inherit from `BaseAIAgent`
2. Implement behavior state methods
3. Build behavior tree using node system
4. Add to AI spawn system

## Game Flow
1. **Lobby** - Players join and select classes
2. **Match Start** - Teams spawn at designated locations
3. **Objective Phase** - Complete objectives, combat other teams
4. **Extraction Phase** - Rush to extraction points
5. **Results** - Score calculation and winner determination

## Network Architecture
Built on Photon Fusion 2 using:
- **State Authority** - Server-authoritative game state
- **RPCs** - Remote procedure calls for actions
- **Network Properties** - Synchronized player state
- **Tick-based Timing** - Precise network timing

## Controls
- **WASD** - Movement
- **Mouse** - Look around
- **1-4** - Use class abilities
- **LMB** - Primary weapon/equipment
- **RMB** - Secondary weapon/equipment
- **Q** - Utility equipment
- **E** - Gadget equipment
- **Space** - Jump

## Technical Details
- **Max Players**: 16 (4 teams × 4 players)
- **Match Duration**: 15 minutes (configurable)
- **Ability Slots**: 4 per class
- **Equipment Slots**: 6 per player
- **Tech Slots**: 3 per player

## Roadmap
- [ ] Additional player classes
- [ ] Expanded equipment variety
- [ ] More AI types and behaviors
- [ ] Progression system
- [ ] Cosmetics and customization
- [ ] Multiple game modes
- [ ] Map variety
- [ ] Ranked matchmaking

## Contributing
When adding new features:
1. Follow the modular architecture patterns
2. Use interfaces for extensibility
3. Create ScriptableObjects for configuration
4. Integrate properly with Photon Fusion
5. Document expansion points

## License
[Add your license here]

## Credits
Built with Unity and Photon Fusion 2
