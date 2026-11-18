using UnityEngine;

namespace Ouroboros.AI
{
    /// <summary>
    /// Standard heist guard. Patrols waypoints, investigates the last place it saw or was shot from (or
    /// anything on the <see cref="AIBlackboard"/>: teammates' sightings, camera spots, gunfire), chases
    /// and shoots players it can see, and gives up after a while to resume patrol.
    ///
    /// v0.6: extensible base for other guard types (leaves are protected virtual), blackboard-driven
    /// investigation, and alarm-tier aggression (faster, more persistent, longer memory as tiers rise).
    /// The decision-making is a behavior tree (priority selector); the <see cref="AIBehaviorState"/>
    /// is mirrored for animation / debugging and replicated through <see cref="BaseAIAgent.NetworkedState"/>.
    /// </summary>
    public class GuardAI : BaseAIAgent
    {
        [Header("Guard")]
        [SerializeField] protected Transform[] patrolPoints;
        [SerializeField] protected float patrolWaitSeconds = 2f;
        [SerializeField] protected float investigateSeconds = 6f;
        [SerializeField] protected float chaseGiveUpSeconds = 8f;
        [SerializeField] protected bool randomPatrolOrder = false;
        [Tooltip("How far away a blackboard sighting / noise can be and still draw this guard.")]
        [SerializeField] protected float hearingRange = 30f;
        [Tooltip("Share sightings so nearby guards converge (squad behaviour).")]
        [SerializeField] protected bool squadShare = true;

        // [v0.2] these were private; v0.6 makes them protected so EliteGuard / future types can extend
        protected BehaviorNode root;
        protected int patrolIndex;
        protected Fusion.TickTimer patrolWaitTimer;
        protected Fusion.TickTimer investigateTimer;
        protected Fusion.TickTimer chaseTimer;
        protected float baseSpeed;

        // ------------------------------------------------------------------
        // Alarm-tier aggression

        protected int AlarmTier
        {
            get { var a = GameMode.AlarmSystem.Instance; return a != null && a.Object != null ? a.Tier : 0; }
        }
        protected float SpeedMultiplier => 1f + 0.15f * AlarmTier;
        protected float GiveUpSeconds => chaseGiveUpSeconds * (1f + 0.5f * AlarmTier);
        protected float HearingRange => hearingRange * (1f + 0.5f * AlarmTier);
        protected float PatrolWait => AlarmTier >= 2 ? patrolWaitSeconds * 0.5f : patrolWaitSeconds;

        /// <summary>Assigns a patrol route at spawn time (used by AISpawner).</summary>
        public void SetPatrolPoints(Transform[] points, int startIndex = 0)
        {
            patrolPoints = points;
            patrolIndex = points != null && points.Length > 0 ? Mathf.Abs(startIndex) % points.Length : 0;
            if (isInitialized) TransitionToState(points != null && points.Length > 0 ? AIBehaviorState.Patrol : AIBehaviorState.Idle);
        }

        protected override void OnInitialize()
        {
            if (agentType == AIAgentType.Patrol) agentType = AIAgentType.Guard;
            baseSpeed = movementSpeed;
            BuildTree();
            TransitionToState(patrolPoints != null && patrolPoints.Length > 0 ? AIBehaviorState.Patrol : AIBehaviorState.Idle);
        }

        protected virtual void BuildTree()
        {
            root = new SelectorNode(
                // 1. Target in sight → fight
                new SequenceNode(
                    new ConditionFunc(() => CurrentTargetPlayer != null && CurrentTargetPlayer.IsActiveInMatch),
                    new ActionFunc(Fight, onReset: () => chaseTimer = Fusion.TickTimer.None)
                ),
                // 2. Lost the target but know where they were → go look
                new SequenceNode(
                    new ConditionFunc(() => HasLastKnownPosition),
                    new ActionFunc(DoInvestigate, onReset: () => investigateTimer = Fusion.TickTimer.None)
                ),
                // 3. Something on the blackboard worth checking (teammate sighting, camera, gunfire)
                new SequenceNode(
                    new ConditionFunc(PollBlackboard),
                    new ActionFunc(DoInvestigate, onReset: () => investigateTimer = Fusion.TickTimer.None)
                ),
                // 4. Otherwise patrol (or idle without waypoints)
                new ActionFunc(DoPatrol, onReset: () => patrolWaitTimer = Fusion.TickTimer.None)
            );
        }

        public override void UpdateBehavior(float deltaTime)
        {
            if (!isInitialized || root == null) return;
            if (navAgent != null && navAgent.enabled) navAgent.speed = baseSpeed * SpeedMultiplier;
            root.Evaluate();
        }

        protected override void UpdatePerception()
        {
            base.UpdatePerception();

            // Squad hand-off: adopt a teammate's very recent sighting as a live target if close enough.
            if (currentTargetPlayer == null && squadShare && AlarmTier >= 1)
            {
                var handoff = AIBlackboard.RecentPlayerNear(transform.position, HearingRange * 0.5f, maxAge: 3f);
                if (handoff != null) SetTargetPlayer(handoff);
            }
        }

        // ------------------------------------------------------------------
        // Leaves

        /// <summary>Picks up a point of interest from the blackboard and stores it as the last-known position.</summary>
        protected virtual bool PollBlackboard()
        {
            if (AIBlackboard.TryGetPointOfInterest(transform.position, HearingRange, out Vector3 point, out float score))
            {
                if (score < 0.2f) return false;
                lastKnownTargetPosition = point;
                hasLastKnownPosition = true;
                return true;
            }
            return false;
        }

        protected virtual BehaviorNode.NodeState Fight()
        {
            TransitionToState(AIBehaviorState.Combat);
            var target = CurrentTargetPlayer;

            if (squadShare) AIBlackboard.ReportSighting(target.transform.position, target, priority: 1f);

            float dist = Vector3.Distance(transform.position, target.transform.position);
            if (dist <= AttackRange * 0.9f)
            {
                StopMoving();
                FaceTowards(target.transform.position);
                TryAttackTarget();
            }
            else
            {
                MoveTo(target.transform.position, AttackRange * 0.75f);
                FaceTowards(target.transform.position, 360f);
            }

            // Give up on a target we can't reach for a while
            if (!chaseTimer.IsRunning) chaseTimer = Fusion.TickTimer.CreateFromSeconds(Runner, GiveUpSeconds);
            if (chaseTimer.Expired(Runner) && dist > AttackRange)
            {
                ClearTarget(forgetPosition: false);
                chaseTimer = Fusion.TickTimer.None;
                return BehaviorNode.NodeState.Failure;
            }

            return BehaviorNode.NodeState.Running;
        }

        protected virtual BehaviorNode.NodeState DoInvestigate()
        {
            TransitionToState(AIBehaviorState.Investigating);

            bool arrived = MoveTo(LastKnownTargetPosition, 1f);
            if (!arrived) return BehaviorNode.NodeState.Running;

            if (!investigateTimer.IsRunning)
            {
                investigateTimer = Fusion.TickTimer.CreateFromSeconds(Runner, investigateSeconds);
            }

            // Look around while waiting
            transform.Rotate(Vector3.up, 90f * Runner.DeltaTime);

            if (investigateTimer.Expired(Runner))
            {
                ClearTarget(forgetPosition: true);
                investigateTimer = Fusion.TickTimer.None;
                return BehaviorNode.NodeState.Success;
            }
            return BehaviorNode.NodeState.Running;
        }

        protected virtual BehaviorNode.NodeState DoPatrol()
        {
            if (patrolPoints == null || patrolPoints.Length == 0)
            {
                TransitionToState(AIBehaviorState.Idle);
                StopMoving();
                return BehaviorNode.NodeState.Running;
            }

            TransitionToState(AIBehaviorState.Patrol);

            Transform point = patrolPoints[patrolIndex % patrolPoints.Length];
            if (point == null)
            {
                patrolIndex++;
                return BehaviorNode.NodeState.Running;
            }

            if (patrolWaitTimer.IsRunning)
            {
                if (!patrolWaitTimer.Expired(Runner)) return BehaviorNode.NodeState.Running;
                patrolWaitTimer = Fusion.TickTimer.None;
                patrolIndex = randomPatrolOrder ? Random.Range(0, patrolPoints.Length) : patrolIndex + 1;
                return BehaviorNode.NodeState.Running;
            }

            if (MoveTo(point.position, 0.75f))
            {
                patrolWaitTimer = Fusion.TickTimer.CreateFromSeconds(Runner, PatrolWait);
            }
            return BehaviorNode.NodeState.Running;
        }

        protected override void OnTakeDamage()
        {
            base.OnTakeDamage();
            // Being hit resets the patience timers so the guard commits to the fight.
            chaseTimer = Fusion.TickTimer.None;
        }

        protected virtual void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(transform.position, detectionRange);
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, attackRange);
            if (patrolPoints == null) return;
            Gizmos.color = Color.white;
            for (int i = 0; i < patrolPoints.Length; i++)
            {
                if (patrolPoints[i] == null) continue;
                var next = patrolPoints[(i + 1) % patrolPoints.Length];
                if (next != null) Gizmos.DrawLine(patrolPoints[i].position, next.position);
            }
        }
    }
}
