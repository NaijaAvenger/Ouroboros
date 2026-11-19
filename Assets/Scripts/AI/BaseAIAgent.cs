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

        [Header("Accuracy (v0.9)")]
        [Tooltip("Chance to hit a stationary target at point-blank range. Distance, target speed and the alarm tier adjust it (Core.CombatRules.GuardHitChance).")]
        [Range(0f, 1f)] [SerializeField] protected float baseAccuracy = 0.55f;
        [Tooltip("Seconds the agent needs to line up its first shot after it acquires (or re-acquires) a target. Breaking line of sight resets it.")]
        [SerializeField] protected float aimSettleSeconds = 0.45f;

        /// <summary>
        /// (v0.9) Fired on every peer when an agent shoots (agent, muzzle, where the shot went, whether it hit).
        /// Misses are reported too, so tracers and audio can show near misses.
        /// </summary>
        public static event System.Action<BaseAIAgent, Vector3, Vector3, bool> AgentFired;
        /// <summary>(v0.21) Raised on the state authority when a guard dies, before it despawns: a vault's key may fall here.</summary>
        public static event System.Action<BaseAIAgent> AgentDied;

        [Networked] public float Health { get; set; }
        [Networked] public AIBehaviorState NetworkedState { get; set; }
        /// <summary>(v0.9) Runs while the agent is still lining up its first shot at a newly acquired target.</summary>
        [Networked] protected TickTimer AimSettleTimer { get; set; }
        [Networked] public NetworkBool IsAlive { get; set; }
        [Networked] protected TickTimer AttackTimer { get; set; }

        protected AIBehaviorState currentState;
        protected VisionCone visionCone;
        [Tooltip("Draw the detection cone on the ground (every peer).")]
        [SerializeField] protected bool showVisionCone = true;
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

            if (showVisionCone)
            {
                visionCone = VisionCone.Attach(transform, detectionRange, fieldOfView, groundOffset: 1f, VisionCone.IdleColor);
            }
            // [v0.11] else if (navAgent != null)
            // [v0.11] {
            // [v0.11]     // Proxies are driven by NetworkTransform; the NavMeshAgent must not fight it.
            // [v0.11]     navAgent.enabled = false;
            // [v0.11] }
            // (v0.12) That "else" had ended up attached to the vision-cone check above instead of the authority
            //         check, so with cones on, clients kept their NavMeshAgents running against the replicated
            //         transform, and with cones off the server's own agents were switched off. Fixed:
            if (!Object.HasStateAuthority && navAgent != null)
            {
                // Proxies are driven by NetworkTransform; the NavMeshAgent must not fight it.
                navAgent.enabled = false;
            }
            DressModel(); // (v0.12)
        }

        /// <summary>
        /// (v0.12, M4) Replaces the prefab's capsule with a code-built guard: grey armour, orange trim, a rifle.
        /// The capsule's collider stays (it is what gets shot); only its renderer is switched off.
        /// </summary>
        private void DressModel()
        {
            var capsule = GetComponent<MeshRenderer>();
            if (capsule != null) capsule.enabled = false;
            var kind = this is EliteGuard ? AIAgentType.Elite : this is SniperGuard ? AIAgentType.Sniper : agentType;
            var built = Art.CharacterBuilder.BuildGuard(transform, kind, feetY: -1f);
            var walk = gameObject.AddComponent<Art.CharacterAnimator>();
            walk.Model = built;
            Art.ThreatOutline.Attach(gameObject, Art.ThreatOutline.Hostile); // (v0.17) a guard is easy to lose against a busy room
            Audio.Footsteps.Attach(gameObject, Audio.Footsteps.Kind.Guard);  // (v0.20) and now a patrol can be heard coming
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            if (visionCone != null) Destroy(visionCone.gameObject);
        }

        public override void Render()
        {
            if (visionCone == null) return;
            var alarm = GameMode.AlarmSystem.Instance;
            float scale = alarm != null && alarm.Object != null ? alarm.DetectionMultiplier : 1f;
            visionCone.Configure(detectionRange * scale, fieldOfView);
            visionCone.SetVisible(IsAlive);
            switch (NetworkedState)
            {
                case AIBehaviorState.Combat:        visionCone.SetColor(VisionCone.AlertColor); break;
                case AIBehaviorState.Alert:
                case AIBehaviorState.Investigating: visionCone.SetColor(VisionCone.SearchColor); break;
                default:                            visionCone.SetColor(VisionCone.IdleColor); break;
            }
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
            var alarm = GameMode.AlarmSystem.Instance;
            float range = detectionRange * (alarm != null && alarm.Object != null ? alarm.DetectionMultiplier : 1f);
            if (Interaction.PowerNode.IsBlackedOut(transform.position)) range *= 0.5f; // (v0.11) lights out
            var seen = Perception.FindVisiblePlayer(EyePosition, transform.forward, range, fieldOfView, visionOccluders, faction);

            if (seen != null)
            {
                bool newEngagement = currentTargetPlayer == null;
                SetTargetPlayer(seen);
                if (newEngagement)
                {
                    var gm = GameMode.ExtractionHeistGameMode.Instance;
                    GameMode.AlarmSystem.Raise(gm != null ? gm.Config.alarmOnGuardEngaged : 15f, $"{agentType} engaged");
                    AIBlackboard.ReportSighting(seen.transform.position, seen, priority: 1f); // v0.6: squad knowledge
                }
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
                    AIBlackboard.ReportSighting(lastKnownTargetPosition, currentTargetPlayer, priority: 0.8f);
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
            // (v0.9) a new target (or one seen again after losing it) has to be lined up before the first shot
            var newPlayer = target != null ? target.GetComponentInParent<Network.NetworkPlayer>() : null;
            if (target != null && newPlayer != currentTargetPlayer && aimSettleSeconds > 0f && Object != null && Object.HasStateAuthority)
            {
                AimSettleTimer = TickTimer.CreateFromSeconds(Runner, aimSettleSeconds);
            }

            currentTarget = target;
            // [v0.8] currentTargetPlayer = target != null ? target.GetComponentInParent<Network.NetworkPlayer>() : null;
            currentTargetPlayer = newPlayer;
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

        /// <summary>(v0.9) Origin-aware entry point; agents only need the attacker, so it forwards.</summary>
        public void ApplyDamage(float amount, Network.NetworkPlayer attacker, Vector3 origin)
        {
            ApplyDamage(amount, attacker);
        }

        /// <summary>(v0.14) What a player's death recap calls this agent.</summary>
        public virtual string SourceLabel
        {
            get
            {
                string type = GetType().Name;
                if (type.Contains("Elite")) return "Elite Guard";
                if (type.Contains("Sniper")) return "Sniper";
                return "Guard";
            }
        }

        public virtual void ApplyDamage(float amount, Network.NetworkPlayer attacker)
        {
            if (!Object.HasStateAuthority || !IsAlive || amount <= 0f) return;

            float healthBefore = Health; // (v0.14)
            Health = Mathf.Max(0f, Health - amount);
            if (attacker != null && healthBefore > Health)
            {
                // (v0.14) damage numbers for the shooter
                var info = Combat.DamageContext.Current;
                attacker.NotifyDamageDealt(healthBefore - Health, info.HasPoint ? info.Point : transform.position + Vector3.up * 1.3f, info.Headshot, Health <= 0f);
            }

            if (attacker != null && attacker.IsActiveInMatch)
            {
                // Getting shot tells you (and the squad) where the shooter is.
                lastKnownTargetPosition = attacker.transform.position;
                hasLastKnownPosition = true;
                AIBlackboard.ReportSighting(attacker.transform.position, attacker, priority: 1.1f);
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

            // (v0.9) still lining up the first shot: the player gets a beat to react or break line of sight
            if (!AimSettleTimer.ExpiredOrNotRunning(Runner)) return false;

            // [v0.8] currentTargetPlayer.ApplyDamage(damage, null);   // every shot hit, at any speed and distance
            // (v0.9) shots can miss. A miss still fires (cooldown, noise, tracer) and returns true.
            bool hit = Random.value < HitChanceAgainst(currentTargetPlayer, dist);
            Vector3 end = currentTargetPlayer.EyePosition;
            if (hit)
            {
                // [v0.13] currentTargetPlayer.ApplyDamage(damage, null, origin);
                using (Combat.DamageContext.Begin(source: SourceLabel, means: "Rifle")) // (v0.14) named in the death recap
                {
                    currentTargetPlayer.ApplyDamage(damage, null, origin);
                }
            }
            else
            {
                // Put the tracer visibly past the target, 0.5 to 1.2 m off to one side.
                Vector3 side = Vector3.Cross(dir, Vector3.up);
                if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
                side = side.normalized * (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.5f, 1.2f);
                end += side + Vector3.up * Random.Range(-0.3f, 0.4f) + dir * 4f;
            }

            AttackTimer = TickTimer.CreateFromSeconds(Runner, attackCooldown);
            RPC_Fired(origin, end, hit);
            OnAttacked(currentTargetPlayer);
            return true;
        }

        /// <summary>
        /// (v0.9) Chance for the next shot to hit. Override for agents with their own rules (the sniper's
        /// telegraphed shot always lands).
        /// </summary>
        protected virtual float HitChanceAgainst(Network.NetworkPlayer target, float distance)
        {
            var alarm = GameMode.AlarmSystem.Instance;
            int tier = alarm != null && alarm.Object != null ? alarm.Tier : 0;
            return Core.CombatRules.GuardHitChance(baseAccuracy, distance, attackRange, target.Velocity.magnitude, tier);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        protected void RPC_Fired(Vector3 from, Vector3 to, NetworkBool hit)
        {
            AgentFired?.Invoke(this, from, to, hit);
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
            AgentDied?.Invoke(this); // (v0.21)
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
