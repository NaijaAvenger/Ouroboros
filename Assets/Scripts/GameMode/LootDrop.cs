using UnityEngine;
using Fusion;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// Loot dropped by a player who died while carrying it. Any living player who walks over it picks it
    /// up (including the original owner's teammates), which makes contested fights over carriers
    /// meaningful. Despawns on pickup or after <see cref="lifetime"/> seconds.
    /// </summary>
    public class LootDrop : NetworkBehaviour
    {
        [SerializeField] private float pickupRadius = 1.5f;
        [SerializeField] private float lifetime = 120f;
        [Tooltip("Seconds before anyone can pick it up, so the killer doesn't instantly hoover it.")]
        [SerializeField] private float pickupDelay = 1f;

        [Networked] public int Value { get; set; }
        [Networked] private TickTimer LifetimeTimer { get; set; }
        [Networked] private TickTimer PickupDelayTimer { get; set; }

        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                LifetimeTimer = TickTimer.CreateFromSeconds(Runner, lifetime);
                PickupDelayTimer = TickTimer.CreateFromSeconds(Runner, pickupDelay);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (LifetimeTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
                return;
            }

            if (!PickupDelayTimer.ExpiredOrNotRunning(Runner)) return;
            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var candidates = Network.NetworkPlayer.FindPlayersInRadius(transform.position, pickupRadius, Core.TeamID.None, aliveOnly: true);
            foreach (var player in candidates)
            {
                int accepted = player.AddLoot(Value);
                if (accepted <= 0) continue;

                Value -= accepted;
                RPC_PickedUp(player.PlayerRef, accepted);
                if (Value <= 0)
                {
                    Runner.Despawn(Object);
                    return;
                }
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PickedUp(PlayerRef player, int amount)
        {
            Debug.Log($"[LootDrop] {player} picked up {amount} loot");
        }
    }
}
