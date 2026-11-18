using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// Saboteur trap. Arms after a short delay, then detonates on the first enemy (or AI) that comes
    /// within <see cref="triggerRadius"/>. Networked so every peer sees it; only the state authority
    /// evaluates triggering.
    /// </summary>
    public class ProximityTrap : NetworkBehaviour
    {
        [SerializeField] private float armDelay = 1.5f;
        [SerializeField] private float lifetime = 180f;
        [SerializeField] private float triggerRadius = 1.5f;
        [SerializeField] private float damage = 40f;
        [SerializeField] private float blastRadius = 2.5f;

        [Networked] public Core.TeamID OwnerTeam { get; set; }
        [Networked] public NetworkBool IsArmed { get; set; }
        [Networked] private TickTimer ArmTimer { get; set; }
        [Networked] private TickTimer LifetimeTimer { get; set; }

        private Classes.SaboteurClass ownerClass;

        /// <summary>Called from the spawn callback before <see cref="Spawned"/>.</summary>
        public void Configure(Core.TeamID team, float trapDamage, float radius, Classes.SaboteurClass owner)
        {
            OwnerTeam = team;
            damage = trapDamage;
            triggerRadius = radius;
            ownerClass = owner;
        }

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                ArmTimer = TickTimer.CreateFromSeconds(Runner, armDelay);
                LifetimeTimer = TickTimer.CreateFromSeconds(Runner, lifetime);
                IsArmed = false;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (LifetimeTimer.Expired(Runner))
            {
                Consume();
                return;
            }

            if (!IsArmed)
            {
                if (ArmTimer.Expired(Runner)) IsArmed = true;
                return;
            }

            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var victims = Network.NetworkPlayer.FindPlayersInRadius(transform.position, triggerRadius, OwnerTeam, aliveOnly: true);
            bool triggered = victims.Count > 0;

            if (!triggered)
            {
                for (int i = 0; i < AI.BaseAIAgent.All.Count; i++)
                {
                    var ai = AI.BaseAIAgent.All[i];
                    if (ai != null && ai.IsAlive && (ai.Position - transform.position).sqrMagnitude <= triggerRadius * triggerRadius)
                    {
                        triggered = true;
                        break;
                    }
                }
            }

            if (triggered) Detonate();
        }

        private void Detonate()
        {
            var owner = ownerClass != null ? Network.NetworkPlayer.Resolve(ownerClass.Context) : null;
            Combat.DamageUtil.ApplyRadialDamage(transform.position, blastRadius, damage, owner, OwnerTeam, falloff: true);
            RPC_Detonated();
            Consume();
        }

        private void Consume()
        {
            ownerClass?.OnTrapConsumed();
            Runner.Despawn(Object);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Detonated()
        {
            Debug.Log($"[ProximityTrap] Trap of {OwnerTeam} detonated");
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, triggerRadius);
        }
    }
}
