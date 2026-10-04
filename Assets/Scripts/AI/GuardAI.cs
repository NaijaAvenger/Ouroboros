using UnityEngine;

namespace Ouroboros.AI
{
    /// <summary>
    /// Standard heist guard. Patrols waypoints, investigates the last place it saw or was shot from,
    /// chases and shoots players it can see, and gives up after a while to resume patrol.
    ///
    /// The decision-making is a behavior tree (priority selector); the <see cref="AIBehaviorState"/>
    /// is mirrored for animation / debugging and replicated through <see cref="BaseAIAgent.NetworkedState"/>.
    /// </summary>
    public class GuardAI : BaseAIAgent
    {
        [Header("Guard")]
        [SerializeField] private Transform[] patrolPoints;
        [SerializeField] private float patrolWaitSeconds = 2f;
        [SerializeField] private float investigateSeconds = 6f;
        [SerializeField] private float chaseGiveUpSeconds = 8f;
        [SerializeField] private bool randomPatrolOrder = false;

        private BehaviorNode root;
        private int patrolIndex;
        private Fusion.TickTimer patrolWaitTimer;
        private Fusion.TickTimer investigateTimer;
        private Fusion.TickTimer chaseTimer;

        protected override void OnInitialize()
        {
            agentType = AIAgentType.Guard;
            BuildTree();
            TransitionToState(patrolPoints != null && patrolPoints.Length > 0 ? AIBehaviorState.Patrol : AIBehaviorState.Idle);
        }

        private void BuildTree()
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
                // 3. Otherwise patrol (or idle without waypoints)
                new ActionFunc(DoPatrol, onReset: () => patrolWaitTimer = Fusion.TickTimer.None)
            );
        }

        public override void UpdateBehavior(float deltaTime)
        {
            if (!isInitialized || root == null) return;
            root.Evaluate();
        }

        // ---- Leaves ----

        private BehaviorNode.NodeState Fight()
        {
            TransitionToState(AIBehaviorState.Combat);
            var target = CurrentTargetPlayer;

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
            if (!chaseTimer.IsRunning) chaseTimer = Fusion.TickTimer.CreateFromSeconds(Runner, chaseGiveUpSeconds);
            if (chaseTimer.Expired(Runner) && dist > AttackRange)
            {
                ClearTarget(forgetPosition: false);
                chaseTimer = Fusion.TickTimer.None;
                return BehaviorNode.NodeState.Failure;
            }

            return BehaviorNode.NodeState.Running;
        }

        private BehaviorNode.NodeState DoInvestigate()
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

        private BehaviorNode.NodeState DoPatrol()
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
                patrolWaitTimer = Fusion.TickTimer.CreateFromSeconds(Runner, patrolWaitSeconds);
            }
            return BehaviorNode.NodeState.Running;
        }

        protected override void OnTakeDamage()
        {
            base.OnTakeDamage();
            // Being hit resets the patience timers so the guard commits to the fight.
            chaseTimer = Fusion.TickTimer.None;
        }

        private void OnDrawGizmosSelected()
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
