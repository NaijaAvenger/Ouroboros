using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;
using Fusion;

namespace Ouroboros.AI
{
    /// <summary>
    /// Interface for AI agents. Designed for modularity and future AI expansion.
    /// </summary>
    public interface IAIAgent
    {
        AIAgentType AgentType { get; }
        AIBehaviorState CurrentState { get; }

        void Initialize();
        void UpdateBehavior(float deltaTime);
        void SetTarget(Transform target);
        void OnDamaged(float damage);
    }

    /// <summary>
    /// AI agent types for different NPC roles.
    /// </summary>
    public enum AIAgentType
    {
        Guard,
        Patrol,
        Elite,
        Boss,
        Civilian,
        Sniper
    }

    /// <summary>
    /// AI behavior states.
    /// </summary>
    public enum AIBehaviorState
    {
        Idle,
        Patrol,
        Alert,
        Combat,
        Fleeing,
        Investigating
    }

    /// <summary>
    /// Base AI agent class providing common AI functionality.
    ///
    /// v0.2: now a <see cref="NetworkBehaviour"/>. Behaviour runs in <c>FixedUpdateNetwork</c> on the
    /// state authority only; health and state are replicated so clients can drive animation/UI.
    /// Position replication requires a <c>NetworkTransform</c> on the prefab. Requires the AI Navigation
    /// package (com.unity.ai.navigation) on Unity 2022.2+.
    /// </summary>
    [RequireComponent(typeof(NavMeshAgent))]
    public abstract class BaseAIAgent : NetworkBehaviour, IAIAgent, Combat.IDamageable
    {
        /// <summary>Live registry of spawned agents on this peer.</summary>
        public static readonly List<BaseAIAgent> All = new List<BaseAIAgent>();

        [Header("AI Configuration")]
        [SerializeField] protected AIAgentType agentType;
        [SerializeField] protected float detectionRange = 15f;
        [SerializeField] protected float attackRange = 5f;
        [SerializeField] protected float fieldOfView = 120f;
        [SerializeField] protected float eyeHeight = 1.6f;
        [SerializeField] protected LayerMask visionOccluders = ~0;
        [Tooltip("Ticks between perception scans (cheap enough to be low).")]
        [SerializeField] protected int perceptionIntervalTicks = 3;

        [Header("AI Stats")]
        [SerializeField] protected float maxHealth = 100f;
        // [v0.1] [SerializeField] protected float health = 100f;  // now the networked Health property
        [SerializeField] protected float movementSpeed = 3.5f;
        [SerializeField] protected float damage = 15f;
        [SerializeField] protected float attackCooldown = 1.2f;
        [Tooltip("Faction team. None = hostile to every player (default for heist guards).")]
        [SerializeField] protected Core.TeamID faction = Core.TeamID.None;

        [Networked] public float Health { get; set; }
        [Networked] public AIBehaviorState NetworkedState { get; set; }
        [Networked] public NetworkBool IsAlive { get; set; }
        [Networked] protected TickTimer AttackTimer { get; set; }

        protected AIBehaviorState currentState;
        protected Transform currentTarget;
        protected Network.NetworkPlayer currentTargetPlayer;
        protected Vector3 lastKnownTargetPosition;
        protected bool hasLastKnownPosition;
        protected NavMeshAgent navAgent;
        protected bool isInitialized;

        public AIAgentType AgentType => agentType;
        public AIBehaviorState CurrentState => currentState;
        /// <summary>Public accessors so behavior-tree nodes (nested or external) can read agent state.</summary>
        public Transform CurrentTarget => currentTarget;
        public Network.NetworkPlayer CurrentTargetPlayer => currentTargetPlayer;
        public NavMeshAgent NavAgent => navAgent;
        public Vector3 EyePosition => transform.position + Vector3.up * eyeHeight;
        public Vector3 LastKnownTargetPosition => lastKnownTargetPosition;
        public bool HasLastKnownPosition => hasLastKnownPosition;
        public float AttackRange => attackRange;
        public float DetectionRange => detectionRange;

        // ---- IDamageable ----
        bool Combat.IDamageable.IsAlive => IsAlive;
        public Core.TeamID Team => faction;
        public Vector3 Position => transform.position;

        protected virtual void Awake()
        {
            navAgent = GetComponent<NavMeshAgent>();
            if (navAgent != null)
            {
                navAgent.speed = movementSpeed;
            }
        }

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);

            if (Object.HasStateAuthority)
            {
                Health = maxHealth;
                IsAlive = true;
                Initialize();
            }
            else if (navAgent != null)
            {
                // Proxies are driven by NetworkTransform; the NavMeshAgent must not fight it.
                navAgent.enabled = false;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || !IsAlive) return;

            if (((int)Runner.Tick) % Mathf.Max(1, perceptionIntervalTicks) == 0)
            {
                UpdatePerception();
            }

            UpdateBehavior(Runner.DeltaTime);
            NetworkedState = currentState;
        }

        public virtual void Initialize()
        {
            if (isInitialized) return;

            currentState = AIBehaviorState.Idle;
            isInitialized = true;

            OnInitialize();
        }

        /// <summary>Default perception: look for the nearest visible player; remember where they were last seen.</summary>
        protected virtual void UpdatePerception()
        {
            var seen = Perception.FindVisiblePlayer(EyePosition, transform.forward, detectionRange, fieldOfView, visionOccluders, faction);

            if (seen != null)
            {
                SetTargetPlayer(seen);
            }
            else if (currentTargetPlayer != null)
            {
                // Lost sight (or target died / extracted)
                if (!currentTargetPlayer.IsActiveInMatch)
                {
                    ClearTarget(forgetPosition: true);
                }
                else
                {
                    lastKnownTargetPosition = currentTargetPlayer.transform.position;
                    hasLastKnownPosition = true;
                    ClearTarget(forgetPosition: false);
                }
            }
        }

        public virtual void UpdateBehavior(float deltaTime)
        {
            if (!isInitialized) return;

            switch (currentState)
            {
                case AIBehaviorState.Idle:
                    UpdateIdleBehavior(deltaTime);
                    break;
                case AIBehaviorState.Patrol:
                    UpdatePatrolBehavior(deltaTime);
                    break;
                case AIBehaviorState.Alert:
                    UpdateAlertBehavior(deltaTime);
                    break;
                case AIBehaviorState.Combat:
                    UpdateCombatBehavior(deltaTime);
                    break;
                case AIBehaviorState.Investigating:
                    UpdateInvestigatingBehavior(deltaTime);
                    break;
                case AIBehaviorState.Fleeing:
                    UpdateFleeingBehavior(deltaTime);
                    break;
            }
        }

        public virtual void SetTarget(Transform target)
        {
            currentTarget = target;
            currentTargetPlayer = target != null ? target.GetComponentInParent<Network.NetworkPlayer>() : null;
            if (target != null)
            {
                lastKnownTargetPosition = target.position;
                hasLastKnownPosition = true;
                TransitionToState(AIBehaviorState.Combat);
            }
        }

        public virtual void SetTargetPlayer(Network.NetworkPlayer player)
        {
            SetTarget(player != null ? player.transform : null);
        }

        public virtual void ClearTarget(bool forgetPosition)
        {
            currentTarget = null;
            currentTargetPlayer = null;
            if (forgetPosition) hasLastKnownPosition = false;
        }

        /// <summary>Legacy damage entry (no attacker).</summary>
        public virtual void OnDamaged(float damage)
        {
            ApplyDamage(damage, null);
        }

        public virtual void ApplyDamage(float amount, Network.NetworkPlayer attacker)
        {
            if (!Object.HasStateAuthority || !IsAlive || amount <= 0f) return;

            Health = Mathf.Max(0f, Health - amount);

            if (attacker != null && attacker.IsActiveInMatch)
            {
                // Getting shot tells you where the shooter is.
                lastKnownTargetPosition = attacker.transform.position;
                hasLastKnownPosition = true;
                if (currentTargetPlayer == null) SetTargetPlayer(attacker);
            }

            if (Health <= 0f)
            {
                OnDeath();
            }
            else
            {
                OnTakeDamage();
            }
        }

        /// <summary>True when the attack cooldown has elapsed.</summary>
        protected bool CanAttack() => AttackTimer.ExpiredOrNotRunning(Runner);

        /// <summary>Simple hitscan attack toward the current target. Override for projectiles / melee.</summary>
        protected virtual bool TryAttackTarget()
        {
            if (currentTargetPlayer == null || !CanAttack()) return false;

            Vector3 origin = EyePosition;
            Vector3 dir = (currentTargetPlayer.EyePosition - origin).normalized;
            float dist = Vector3.Distance(origin, currentTargetPlayer.EyePosition);
            if (dist > attackRange) return false;

            // Blocked by level geometry?
            if (Physics.Raycast(origin, dir, Mathf.Max(0f, dist - 0.3f), visionOccluders, QueryTriggerInteraction.Ignore)) return false;

            currentTargetPlayer.ApplyDamage(damage, null);
            AttackTimer = TickTimer.CreateFromSeconds(Runner, attackCooldown);
            OnAttacked(currentTargetPlayer);
            return true;
        }

        /// <summary>Moves toward a position using the NavMeshAgent. Returns true if the agent has (nearly) arrived.</summary>
        protected bool MoveTo(Vector3 position, float stoppingDistance = 0.5f)
        {
            if (navAgent == null || !navAgent.enabled || !navAgent.isOnNavMesh) return true;

            navAgent.stoppingDistance = stoppingDistance;
            if ((navAgent.destination - position).sqrMagnitude > 0.25f)
            {
                navAgent.SetDestination(position);
            }
            return !navAgent.pathPending && navAgent.remainingDistance <= stoppingDistance + 0.1f;
        }

        protected void StopMoving()
        {
            if (navAgent != null && navAgent.enabled && navAgent.isOnNavMesh)
            {
                navAgent.ResetPath();
            }
        }

        protected void FaceTowards(Vector3 position, float turnSpeed = 720f)
        {
            Vector3 flat = position - transform.position;
            flat.y = 0f;
            if (flat.sqrMagnitude < 0.0001f) return;
            Quaternion wanted = Quaternion.LookRotation(flat);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, wanted, turnSpeed * Runner.DeltaTime);
        }

        protected virtual void TransitionToState(AIBehaviorState newState)
        {
            if (currentState == newState) return;

            OnExitState(currentState);
            currentState = newState;
            OnEnterState(newState);
        }

        protected virtual void OnInitialize() { }
        protected virtual void OnEnterState(AIBehaviorState state) { }
        protected virtual void OnExitState(AIBehaviorState state) { }
        protected virtual void OnAttacked(Network.NetworkPlayer target) { }

        protected virtual void UpdateIdleBehavior(float deltaTime) { }
        protected virtual void UpdatePatrolBehavior(float deltaTime) { }
        protected virtual void UpdateAlertBehavior(float deltaTime) { }
        protected virtual void UpdateCombatBehavior(float deltaTime) { }
        protected virtual void UpdateInvestigatingBehavior(float deltaTime) { }
        protected virtual void UpdateFleeingBehavior(float deltaTime) { }

        protected virtual void OnTakeDamage()
        {
            if (currentState != AIBehaviorState.Combat) TransitionToState(AIBehaviorState.Alert);
        }

        protected virtual void OnDeath()
        {
            IsAlive = false;
            StopMoving();
            RPC_OnDeath();
            Debug.Log($"[AI] {agentType} agent died");
            // Despawn next tick so the death RPC is delivered; spawners can pool instead by overriding.
            Runner.Despawn(Object);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        protected void RPC_OnDeath()
        {
            // Hook for death VFX / ragdoll on every peer.
        }
    }
}
