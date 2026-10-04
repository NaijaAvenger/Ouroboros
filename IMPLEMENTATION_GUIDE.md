# Implementation Guide - Adding New Content

All examples target the v0.2 APIs. Patterns to keep: write `[Networked]` state only on the state
authority, time things with `TickTimer`, and leave superseded code as `// [v0.1]` comments.

---

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
using Fusion;
using UnityEngine;

namespace Ouroboros.Classes
{
    public class MedicClass : Core.BasePlayerClass
    {
        [Header("Medic Abilities")]
        [SerializeField] private float healAmount = 50f;
        [SerializeField] private float healRange = 10f;
        [SerializeField] private float stimDuration = 6f;

        private TickTimer stimTimer;
        private bool stimActive;

        protected override void OnInitialize()
        {
            className = "Medic";
            description = "Medical specialist capable of healing allies and reviving teammates.";
            maxHealth = 95f;
            movementSpeed = 5.3f;
            sprintSpeed = 7.6f;

            DefineAbility(0, "Heal Teammates", cooldown: 12f, staminaCost: 20f);
            DefineAbility(1, "Deploy Medkit",  cooldown: 30f, staminaCost: 15f);
            DefineAbility(2, "Revive Ally",    cooldown: 45f, staminaCost: 30f);
            DefineAbility(3, "Stim Boost",     cooldown: 25f, staminaCost: 10f, duration: stimDuration);
        }

        // Return false when nothing happened so the cooldown is refunded.
        protected override bool ExecuteAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0: return HealTeammates();
                case 1: Log("Deploying medkit station"); return true;
                case 2: Log("Reviving ally"); return true;
                case 3: return StimBoost();
            }
            return false;
        }

        private bool HealTeammates()
        {
            if (context == null) return true;
            int healed = 0;
            foreach (var p in Network.NetworkPlayer.FindPlayersInRadius(context.Transform.position, healRange, Core.TeamID.None, aliveOnly: true))
            {
                if (p.Team != context.Team) continue;
                p.Heal(healAmount);
                healed++;
            }
            return healed > 0;
        }

        private bool StimBoost()
        {
            if (stimActive) return false;
            stimActive = true;
            stimTimer = StartTimer(stimDuration);
            SetStatus(Core.StatusFlags.DamageBoost, true);
            return true;
        }

        protected override void OnUpdate(float deltaTime)
        {
            if (stimActive && TimerExpired(stimTimer))
            {
                stimActive = false;
                SetStatus(Core.StatusFlags.DamageBoost, false);
            }
        }

        public override float OutgoingDamageMultiplier => stimActive ? 1.2f : 1f;
    }
}
```

### Step 3: Register in NetworkPlayer.ClassComponentType
```csharp
case Core.PlayerClassType.Medic: return typeof(Classes.MedicClass);
```

### Step 4: (Optional) ClassData asset
Create → Ouroboros → Class Data for designer-facing numbers.

### Step 5: Test
- Assign via `GameSessionManager` default class or `networkPlayer.RequestClass(PlayerClassType.Medic)`
- Press 1–4; watch `AbilityCooldownRemaining` and the `DamageBoost` status on a second client

---

## Example 2: Adding New Equipment - "Grappling Hook"

Equipment is still a local `MonoBehaviour` (networking it is Phase 2). The pattern is unchanged:

```csharp
using UnityEngine;

namespace Ouroboros.Equipment
{
    public class GrapplingHook : BaseEquipment
    {
        [SerializeField] private float maxGrappleDistance = 30f;
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
            var player = GetComponent<Network.NetworkPlayer>();
            if (player == null) return;

            if (Physics.Raycast(player.EyePosition, player.AimDirection, out RaycastHit hit, maxGrappleDistance, grappleableLayers))
            {
                Debug.Log($"[GrapplingHook] Grappling to {hit.point}");
                // Move the player toward hit.point (e.g. via PlayerController.Teleport over several ticks)
            }
        }
    }
}
```
Equip with `equipmentLoadout.EquipItem(gameObject.AddComponent<GrapplingHook>())`; **E** uses the Gadget slot.
Use `player.AimDirection` rather than `Camera.main` so the server resolves the same ray as the client.

---

## Example 3: Adding New AI - "Sniper Guard"

`AIAgentType.Sniper` already exists. Snipers hold position, aim for a moment, then fire a high-damage hitscan.

```csharp
using Fusion;
using UnityEngine;

namespace Ouroboros.AI
{
    public class SniperGuard : BaseAIAgent
    {
        [Header("Sniper")]
        [SerializeField] private float aimTime = 1.5f;

        [Networked] private TickTimer AimTimer { get; set; }

        protected override void OnInitialize()
        {
            agentType = AIAgentType.Sniper;
            detectionRange = 40f;
            attackRange = 35f;
            damage = 50f;
            attackCooldown = 3f;
            TransitionToState(AIBehaviorState.Idle);
        }

        public override void UpdateBehavior(float deltaTime)
        {
            if (CurrentTargetPlayer == null || !CurrentTargetPlayer.IsActiveInMatch)
            {
                AimTimer = TickTimer.None;
                TransitionToState(AIBehaviorState.Idle);
                return;
            }

            TransitionToState(AIBehaviorState.Combat);
            FaceTowards(CurrentTargetPlayer.transform.position, 180f);

            if (!AimTimer.IsRunning) AimTimer = TickTimer.CreateFromSeconds(Runner, aimTime);
            if (AimTimer.Expired(Runner) && TryAttackTarget())
            {
                AimTimer = TickTimer.None;   // re-aim before the next shot
            }
        }
    }
}
```
Prefab: `NetworkObject` + `NetworkTransform` + `NavMeshAgent` + `SniperGuard`. Spawn with
`Runner.Spawn(sniperPrefab, pos, rot)` on the state authority; perception and `IDamageable` come from the base.

---

## Example 4: Creating a Custom Game Mode - "Bank Heist"

### Step 1: GameModeConfig asset
Create → Ouroboros → Game Mode Config; e.g. `matchDuration = 1200`, `objectivesToComplete = 5`,
`requireAllObjectivesForExtraction = true`.

### Step 2: BankHeistGameMode.cs (only if different logic is needed)
```csharp
using UnityEngine;
using Fusion;

namespace Ouroboros.GameMode
{
    public class BankHeistGameMode : ExtractionHeistGameMode
    {
        [SerializeField] private float alarmTriggeredPenalty = 0.5f;

        [Networked] public NetworkBool AlarmTriggered { get; set; }

        public void TriggerAlarm()
        {
            if (!Object.HasStateAuthority || AlarmTriggered) return;
            AlarmTriggered = true;
            RPC_AlarmTriggered();
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_AlarmTriggered()
        {
            Debug.Log("[BankHeist] ALARM TRIGGERED! Security reinforcements incoming!");
        }

        public override void AwardTeamScore(Core.TeamID team, int points)
        {
            if (AlarmTriggered) points = Mathf.RoundToInt(points * alarmTriggeredPenalty);
            base.AwardTeamScore(team, points);
        }
    }
}
```
`AwardTeamScore` and `DetermineWinner` are virtual for exactly this purpose.

---

## Example 5: Building a Behavior Tree

`GuardAI` is the reference. Compact trees use the delegate nodes:

```csharp
root = new SelectorNode(
    new SequenceNode(
        new ConditionFunc(() => CurrentTargetPlayer != null && CurrentTargetPlayer.IsActiveInMatch),
        new ActionFunc(Fight)
    ),
    new SequenceNode(
        new ConditionFunc(() => HasLastKnownPosition),
        new ActionFunc(DoInvestigate)
    ),
    new ActionFunc(DoPatrol)
);
```
Leaves return `Running` while busy, `Success` when done, `Failure` to fall through. Class-based nodes
inherit `ConditionNode`/`ActionNode`; read agent state through the public accessors
(`CurrentTarget`, `NavAgent`, `LastKnownTargetPosition`), not the protected fields.

---

## Example 6: A New Interactable - "Alarm Panel"

```csharp
using Fusion;
using UnityEngine;

namespace Ouroboros.Interaction
{
    public class AlarmPanel : NetworkBehaviour, IHackable, ISecurityDevice
    {
        [Networked] private TickTimer DisabledTimer { get; set; }
        public bool IsDisabled => DisabledTimer.IsRunning && !DisabledTimer.Expired(Runner);

        public void Disable(float duration)
        {
            if (!Object.HasStateAuthority) return;
            DisabledTimer = TickTimer.CreateFromSeconds(Runner, duration);
        }

        public void OnHacked(Core.TeamID byTeam, float duration) => Disable(duration);
    }
}
```
It is now automatically a valid target for System Hack, Disable Camera, EMP Blast and Sabotage.

---

## Best Practices

1. **Authority** — guard every `[Networked]` write with `Object.HasStateAuthority`
2. **Timing** — `TickTimer` only; no `Invoke`, coroutines, or `Time.time` in gameplay code
3. **Zones** — scan `NetworkPlayer.All` / `BaseAIAgent.All` in `FixedUpdateNetwork`, not trigger callbacks
4. **Late joiners** — anything they must see is replicated state, not an RPC
5. **Tunables** — `GameModeConfig` for rules, `DefineAbility` for class numbers
6. **Superseded code** — comment it out with a `// [v0.1]` marker beside the replacement

## Common Pitfalls to Avoid

1. ❌ Reading `UnityEngine.Input` inside `FixedUpdateNetwork`
2. ❌ Writing networked properties in an `RpcTargets.All` RPC
3. ❌ Keeping a second copy of health/stamina on a class component
4. ❌ `FindObjectOfType` in per-tick code (use the static `Instance`s and registries)
5. ❌ Forgetting `NetworkTransform` on anything that moves

## Testing Checklist
- ✅ Works as host and as a joining client
- ✅ Late joiner sees classes, statuses and door states
- ✅ No writes from non-authority peers (Fusion logs them)
- ✅ Cooldowns and stamina gate correctly
- ✅ Documentation updated
