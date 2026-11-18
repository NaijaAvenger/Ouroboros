using UnityEngine;
using Fusion;

namespace Ouroboros.AI
{
    /// <summary>
    /// Stationary overwatch. Scans slowly, and when it sees a player it paints them (<c>Revealed</c>, so
    /// the HUD warns the victim) for <see cref="aimSeconds"/>, then fires one high-damage shot. Never
    /// moves; turns toward blackboard sightings while idle. Place on elevated sniper posts.
    /// </summary>
    public class SniperGuard : BaseAIAgent
    {
        [Header("Sniper")]
        [SerializeField] private float aimSeconds = 1.6f;
        [SerializeField] private float scanDegreesPerSecond = 20f;
        [SerializeField] private float scanArc = 120f;

        [Networked] private TickTimer AimTimer { get; set; }
        [Networked] private NetworkBool Aiming { get; set; }

        private float homeYaw;
        private float scanPhase;

        protected override void OnInitialize()
        {
            agentType = AIAgentType.Sniper;
            maxHealth = 90f;
            damage = 55f;
            attackRange = 70f;
            detectionRange = 70f;
            fieldOfView = 100f;
            attackCooldown = 3f;
            homeYaw = transform.eulerAngles.y;
            if (navAgent != null) navAgent.enabled = false; // snipers never move
            TransitionToState(AIBehaviorState.Idle);
        }

        public override void UpdateBehavior(float deltaTime)
        {
            var target = CurrentTargetPlayer;
            if (target == null || !target.IsActiveInMatch)
            {
                if (Aiming) { Aiming = false; AimTimer = TickTimer.None; }
                Scan(deltaTime);
                return;
            }

            TransitionToState(AIBehaviorState.Combat);
            FaceTowards(target.transform.position, 120f);
            AIBlackboard.ReportSighting(target.transform.position, target, priority: 1.2f);

            float dist = Vector3.Distance(EyePosition, target.EyePosition);
            if (dist > attackRange) return;

            if (!Aiming)
            {
                Aiming = true;
                AimTimer = TickTimer.CreateFromSeconds(Runner, aimSeconds);
            }

            // Laser warning: the victim is revealed while being aimed at.
            target.ApplyTimedStatus(Core.StatusFlags.Revealed, 0.5f);

            if (AimTimer.Expired(Runner) && CanAttack())
            {
                if (TryAttackTarget())
                {
                    Aiming = false;
                    AimTimer = TickTimer.None;
                }
            }
        }

        private void Scan(float deltaTime)
        {
            TransitionToState(AIBehaviorState.Idle);

            // Turn toward anything interesting on the board; otherwise sweep the arc.
            if (AIBlackboard.TryGetPointOfInterest(transform.position, detectionRange, out Vector3 point, out float score) && score > 0.3f)
            {
                FaceTowards(point, 60f);
                return;
            }

            scanPhase += deltaTime * scanDegreesPerSecond;
            float yaw = homeYaw + Mathf.Sin(scanPhase * Mathf.Deg2Rad * 2f) * scanArc * 0.5f;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
        }

        protected override void OnTakeDamage()
        {
            base.OnTakeDamage();
            if (currentTargetPlayer != null) AIBlackboard.ReportSighting(currentTargetPlayer.transform.position, currentTargetPlayer, 1.2f);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0f, 0.5f);
            Gizmos.DrawWireSphere(transform.position, attackRange);
        }
    }
}
