# Implementation Guide - Adding New Content

## Example 1: Adding a New Player Class - "Medic"

### Step 1: Update GameConstants.cs
```csharp
public enum PlayerClassType
{
    None = 0,
    Hacker = 1,
    Saboteur = 2,
    Demolitions = 3,
    Agent = 4,
    Medic = 5  // Add new class
}
```

### Step 2: Create MedicClass.cs in Assets/Scripts/Classes/
```csharp
using UnityEngine;

namespace Ouroboros.Classes
{
    public class MedicClass : Core.BasePlayerClass
    {
        [Header("Medic Abilities")]
        [SerializeField] private float healAmount = 50f;
        [SerializeField] private float healRange = 10f;
        [SerializeField] private float reviveTime = 3f;
        
        protected override void OnInitialize()
        {
            className = "Medic";
            description = "Medical specialist capable of healing allies and reviving teammates.";
            
            maxHealth = 95f;
            movementSpeed = 5.3f;
        }
        
        public override void UseAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0:
                    HealTeammate();
                    break;
                case 1:
                    DeployMedkit();
                    break;
                case 2:
                    ReviveAlly();
                    break;
                case 3:
                    StimBoost();
                    break;
            }
        }
        
        private void HealTeammate()
        {
            Debug.Log($"[Medic] Healing nearby teammates for {healAmount} HP");
            // Implementation
        }
        
        private void DeployMedkit()
        {
            Debug.Log("[Medic] Deploying medkit station");
            // Implementation
        }
        
        private void ReviveAlly()
        {
            Debug.Log($"[Medic] Reviving ally - {reviveTime}s required");
            // Implementation
        }
        
        private void StimBoost()
        {
            Debug.Log("[Medic] Applying stim boost to team");
            // Implementation
        }
    }
}
```

### Step 3: Update NetworkPlayer.cs
Add to the switch statement in `RPC_AssignClass()`:
```csharp
case Core.PlayerClassType.Medic:
    currentClass = gameObject.AddComponent<Classes.MedicClass>();
    break;
```

### Step 4: Create ClassData ScriptableObject
1. Right-click in Project window
2. Create → Ouroboros → Class Data
3. Name it "MedicClassData"
4. Configure:
   - Class Name: "Medic"
   - Description: "Medical specialist..."
   - Class Type: Medic
   - Max Health: 95
   - Movement Speed: 5.3
   - Configure abilities array

### Step 5: Test
- Assign Medic class to a player
- Test ability keys 1-4
- Verify network synchronization

---

## Example 2: Adding New Equipment - "Grappling Hook"

### Step 1: Create GrapplingHook.cs in Assets/Scripts/Equipment/
```csharp
using UnityEngine;

namespace Ouroboros.Equipment
{
    public class GrapplingHook : BaseEquipment
    {
        [Header("Grappling Hook Settings")]
        [SerializeField] private float maxGrappleDistance = 30f;
        [SerializeField] private float grappleSpeed = 15f;
        [SerializeField] private LayerMask grappleableLayers;
        
        protected override void Awake()
        {
            base.Awake();
            
            equipmentName = "Grappling Hook";
            description = "Launch a hook to quickly reach high locations";
            slotType = EquipmentSlotType.Gadget;
            cooldown = 8f;
        }
        
        protected override void OnUse()
        {
            // Raycast to find grapple point
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            RaycastHit hit;
            
            if (Physics.Raycast(ray, out hit, maxGrappleDistance, grappleableLayers))
            {
                StartGrapple(hit.point);
            }
            else
            {
                Debug.Log("[GrapplingHook] No valid grapple point");
            }
        }
        
        private void StartGrapple(Vector3 targetPoint)
        {
            Debug.Log($"[GrapplingHook] Grappling to {targetPoint}");
            // Implementation: Move player toward point
        }
    }
}
```

### Step 2: Create EquipmentData ScriptableObject
1. Right-click → Create → Ouroboros → Equipment Data
2. Name: "GrapplingHookData"
3. Configure:
   - Equipment Name: "Grappling Hook"
   - Slot Type: Gadget
   - Cooldown: 8
   - Range: 30

### Step 3: Equip to Player
```csharp
// In player setup code
var grapplingHook = gameObject.AddComponent<GrapplingHook>();
equipmentLoadout.EquipItem(grapplingHook);
```

### Step 4: Test
- Press E key to use gadget
- Verify cooldown works
- Test in multiplayer

---

## Example 3: Adding New AI - "Sniper Guard"

### Step 1: Update AIAgentType enum (if needed)
```csharp
public enum AIAgentType
{
    Guard,
    Patrol,
    Elite,
    Boss,
    Civilian,
    Sniper  // Add new type
}
```

### Step 2: Create SniperGuard.cs in Assets/Scripts/AI/
```csharp
using UnityEngine;

namespace Ouroboros.AI
{
    public class SniperGuard : BaseAIAgent
    {
        [Header("Sniper Settings")]
        [SerializeField] private float optimalRange = 25f;
        [SerializeField] private float aimTime = 1.5f;
        [SerializeField] private Transform sniperPosition;
        
        private float currentAimTime = 0f;
        private bool isAiming = false;
        
        protected override void OnInitialize()
        {
            agentType = AIAgentType.Sniper;
            detectionRange = 40f;  // Long range detection
            attackRange = 30f;
            damage = 50f;  // High damage per shot
            
            // Snipers stay in position
            currentState = AIBehaviorState.Idle;
        }
        
        protected override void UpdateIdleBehavior(float deltaTime)
        {
            // Scan for targets
            ScanForTargets();
        }
        
        protected override void UpdateCombatBehavior(float deltaTime)
        {
            if (currentTarget == null)
            {
                TransitionToState(AIBehaviorState.Idle);
                return;
            }
            
            float distance = Vector3.Distance(transform.position, currentTarget.position);
            
            if (distance <= attackRange)
            {
                // Aim at target
                if (!isAiming)
                {
                    StartAiming();
                }
                
                currentAimTime += deltaTime;
                
                if (currentAimTime >= aimTime)
                {
                    FireShot();
                    currentAimTime = 0f;
                    isAiming = false;
                }
            }
        }
        
        private void ScanForTargets()
        {
            // Implementation: Look for players in range
            Collider[] hits = Physics.OverlapSphere(transform.position, detectionRange);
            
            foreach (var hit in hits)
            {
                var player = hit.GetComponent<Network.NetworkPlayer>();
                if (player != null && player.IsAlive)
                {
                    SetTarget(player.transform);
                    break;
                }
            }
        }
        
        private void StartAiming()
        {
            isAiming = true;
            Debug.Log("[SniperGuard] Aiming at target");
        }
        
        private void FireShot()
        {
            Debug.Log($"[SniperGuard] Firing shot - {damage} damage");
            // Implementation: Raycast and apply damage
        }
    }
}
```

### Step 3: Create Prefab
1. Create GameObject with SniperGuard component
2. Add NavMeshAgent
3. Configure visuals and animations
4. Save as prefab

### Step 4: Spawn in Game
```csharp
// In level setup or game mode
var sniperPrefab = Resources.Load<GameObject>("AI/SniperGuard");
var sniper = Instantiate(sniperPrefab, spawnPosition, Quaternion.identity);
sniper.GetComponent<SniperGuard>().Initialize();
```

---

## Example 4: Creating a Custom Game Mode - "Bank Heist"

### Step 1: Create BankHeistConfig ScriptableObject
1. Right-click → Create → Ouroboros → Game Mode Config
2. Name: "BankHeistMode"
3. Configure:
   - Mode Name: "Bank Heist"
   - Match Duration: 1200 (20 minutes)
   - Objectives To Complete: 5
   - Custom rules

### Step 2: Create BankHeistGameMode.cs (optional - if different logic needed)
```csharp
using UnityEngine;
using Fusion;

namespace Ouroboros.GameMode
{
    public class BankHeistGameMode : ExtractionHeistGameMode
    {
        [Header("Bank Heist Specific")]
        [SerializeField] private int vaultObjectivePoints = 500;
        [SerializeField] private float alarmTriggeredPenalty = 0.5f;
        
        private bool alarmTriggered = false;
        
        public void TriggerAlarm()
        {
            if (!Object.HasStateAuthority) return;
            
            if (!alarmTriggered)
            {
                alarmTriggered = true;
                RPC_AlarmTriggered();
                
                // Reduce extraction time
                // Spawn more AI
            }
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_AlarmTriggered()
        {
            Debug.Log("[BankHeist] ALARM TRIGGERED! Security reinforcements incoming!");
        }
        
        public override void AwardTeamScore(Core.TeamID team, int points)
        {
            // Reduce points if alarm was triggered
            if (alarmTriggered)
            {
                points = Mathf.RoundToInt(points * alarmTriggeredPenalty);
            }
            
            base.AwardTeamScore(team, points);
        }
    }
}
```

### Step 3: Setup Scene
1. Add BankHeistGameMode to scene
2. Assign BankHeistConfig ScriptableObject
3. Place objectives (vault, safe deposit boxes, etc.)
4. Configure extraction points
5. Setup AI spawners

---

## Example 5: Adding a Behavior Tree for AI

### Creating a Patrol and Attack Behavior
```csharp
using UnityEngine;
using Ouroboros.AI;

public class PatrolAttackAI : BaseAIAgent
{
    [SerializeField] private Transform[] patrolPoints;
    private int currentPatrolIndex = 0;
    
    private BehaviorNode rootNode;
    
    protected override void OnInitialize()
    {
        BuildBehaviorTree();
    }
    
    private void BuildBehaviorTree()
    {
        // Build tree: Check for enemy → Attack OR Patrol
        rootNode = new SelectorNode(
            new SequenceNode(
                new HasTargetCondition(this),
                new AttackTargetAction(this)
            ),
            new PatrolAction(this, patrolPoints)
        );
    }
    
    protected override void UpdateIdleBehavior(float deltaTime)
    {
        rootNode?.Evaluate();
    }
    
    // Condition: Check if target exists
    private class HasTargetCondition : ConditionNode
    {
        private BaseAIAgent agent;
        
        public HasTargetCondition(BaseAIAgent agent)
        {
            this.agent = agent;
        }
        
        protected override bool CheckCondition()
        {
            return agent.currentTarget != null;
        }
    }
    
    // Action: Attack target
    private class AttackTargetAction : ActionNode
    {
        private BaseAIAgent agent;
        
        public AttackTargetAction(BaseAIAgent agent)
        {
            this.agent = agent;
        }
        
        protected override NodeState ExecuteAction()
        {
            if (agent.currentTarget == null)
                return NodeState.Failure;
            
            // Move toward and attack
            agent.navAgent.SetDestination(agent.currentTarget.position);
            return NodeState.Running;
        }
    }
    
    // Action: Patrol waypoints
    private class PatrolAction : ActionNode
    {
        private BaseAIAgent agent;
        private Transform[] waypoints;
        private int currentIndex = 0;
        
        public PatrolAction(BaseAIAgent agent, Transform[] waypoints)
        {
            this.agent = agent;
            this.waypoints = waypoints;
        }
        
        protected override NodeState ExecuteAction()
        {
            if (waypoints == null || waypoints.Length == 0)
                return NodeState.Failure;
            
            Transform target = waypoints[currentIndex];
            agent.navAgent.SetDestination(target.position);
            
            if (Vector3.Distance(agent.transform.position, target.position) < 1f)
            {
                currentIndex = (currentIndex + 1) % waypoints.Length;
            }
            
            return NodeState.Running;
        }
    }
}
```

---

## Best Practices

### When Adding New Content:

1. **Follow Naming Conventions**
   - Classes: PascalCase
   - Files: Match class names
   - Namespaces: Ouroboros.{System}

2. **Use ScriptableObjects**
   - Create data assets for balance
   - Easy testing and iteration
   - Designer-friendly

3. **Network Considerations**
   - Use [Networked] for synced properties
   - RPCs for actions
   - Check HasStateAuthority for authority-only code

4. **Maintain Modularity**
   - Keep systems decoupled
   - Use interfaces
   - Avoid hard dependencies

5. **Test Thoroughly**
   - Test locally first
   - Test with network simulation
   - Test edge cases

6. **Document Your Work**
   - Add XML comments
   - Update ARCHITECTURE.md
   - Create example usage

## Common Pitfalls to Avoid

1. ❌ Don't modify core base classes for specific features
2. ❌ Don't create tight coupling between systems
3. ❌ Don't bypass the equipment loadout system
4. ❌ Don't forget network synchronization
5. ❌ Don't hardcode values - use ScriptableObjects
6. ❌ Don't skip testing with multiple clients

## Testing Checklist

When adding new content, verify:
- ✅ Works in single player
- ✅ Synchronizes across network
- ✅ Handles disconnection gracefully
- ✅ No console errors or warnings
- ✅ Performance is acceptable
- ✅ Works with all class types
- ✅ Balances with existing content
- ✅ Documentation is updated

---

This guide provides practical examples for extending the Ouroboros game architecture. Follow these patterns for consistent, maintainable code.
