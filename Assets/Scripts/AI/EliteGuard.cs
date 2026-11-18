using UnityEngine;
using Fusion;

namespace Ouroboros.AI
{
    /// <summary>
    /// Heavier guard spawned at higher alarm tiers. Armoured, fires 3-round bursts, keeps a preferred
    /// engagement distance and strafes around the target instead of charging. Engaging one raises the
    /// alarm more than a regular guard.
    /// </summary>
    public class EliteGuard : GuardAI
    {
        [Header("Elite")]
        [SerializeField] private float armor = 0.3f;           // fraction of incoming damage ignored
        [SerializeField] private int burstCount = 3;
        [SerializeField] private float burstShotInterval = 0.12f;
        [SerializeField] private float burstPause = 1.4f;
        [SerializeField] private float preferredDistance = 10f;
        [SerializeField] private float strafeInterval = 2.5f;

        [Networked] private int ShotsInBurst { get; set; }
        [Networked] private TickTimer StrafeTimer { get; set; }
        [Networked] private int StrafeDirection { get; set; }

        protected override void OnInitialize()
        {
            agentType = AIAgentType.Elite;
            maxHealth = 180f;
            damage = 18f;
            attackRange = 18f;
            detectionRange = 20f;
            movementSpeed = 4.2f;
            attackCooldown = burstShotInterval;
            base.OnInitialize();
        }

        public override void ApplyDamage(float amount, Network.NetworkPlayer attacker)
        {
            base.ApplyDamage(amount * (1f - Mathf.Clamp01(armor)), attacker);
        }

        protected override void UpdatePerception()
        {
            bool had = currentTargetPlayer != null;
            base.UpdatePerception();
            if (!had && currentTargetPlayer != null)
            {
                var gm = GameMode.ExtractionHeistGameMode.Instance;
                GameMode.AlarmSystem.Raise(gm != null ? gm.Config.alarmOnGuardEngaged : 15f, "elite engaged");
                AIBlackboard.ReportSighting(currentTargetPlayer.transform.position, currentTargetPlayer, priority: 1.5f);
            }
        }

        protected override BehaviorNode.NodeState Fight()
        {
            TransitionToState(AIBehaviorState.Combat);
            var target = CurrentTargetPlayer;
            Vector3 toTarget = target.transform.position - transform.position;
            float dist = toTarget.magnitude;

            AIBlackboard.ReportSighting(target.transform.position, target, priority: 1.5f);
            FaceTowards(target.transform.position, 540f);

            // Hold the preferred distance and strafe around the target.
            if (StrafeTimer.ExpiredOrNotRunning(Runner))
            {
                StrafeTimer = TickTimer.CreateFromSeconds(Runner, strafeInterval);
                StrafeDirection = Random.value < 0.5f ? -1 : 1;
            }
            Vector3 side = Vector3.Cross(Vector3.up, toTarget.normalized) * StrafeDirection;
            Vector3 desired = target.transform.position - toTarget.normalized * preferredDistance + side * 4f;
            if (Mathf.Abs(dist - preferredDistance) > 2f || StrafeDirection != 0)
            {
                MoveTo(desired, 0.5f);
            }

            if (dist <= attackRange) BurstFire();

            if (!chaseTimer.IsRunning) chaseTimer = TickTimer.CreateFromSeconds(Runner, GiveUpSeconds);
            if (chaseTimer.Expired(Runner) && dist > attackRange)
            {
                ClearTarget(forgetPosition: false);
                chaseTimer = TickTimer.None;
                return BehaviorNode.NodeState.Failure;
            }
            return BehaviorNode.NodeState.Running;
        }

        private void BurstFire()
        {
            if (!CanAttack()) return;
            if (TryAttackTarget())
            {
                ShotsInBurst++;
                if (ShotsInBurst >= burstCount)
                {
                    ShotsInBurst = 0;
                    AttackTimer = TickTimer.CreateFromSeconds(Runner, burstPause);
                }
            }
        }
    }
}
