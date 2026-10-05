using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A dead player's ID card. Players of a different team can take it (hold Interact) and swipe it at a
    /// <see cref="VaultLockType.PlayerCard"/> vault door. Expires after a while.
    /// </summary>
    public class IDCardDrop : NetworkBehaviour
    {
        [SerializeField] private float pickupRadius = 1.8f;
        [SerializeField] private float lifetime = 120f;

        [Networked] public Core.TeamID OwnerTeam { get; set; }
        [Networked] private TickTimer LifeTimer { get; set; }

        public static event System.Action<IDCardDrop, Network.NetworkPlayer> PickedUp;

        public float PickupRadius => pickupRadius;

        public override void Spawned()
        {
            if (Object.HasStateAuthority) LifeTimer = TickTimer.CreateFromSeconds(Runner, lifetime);
            var r = GetComponentInChildren<Renderer>();
            if (r != null)
            {
                var block = new MaterialPropertyBlock();
                var c = Player.TeamSpawnPoint.TeamColor(OwnerTeam);
                block.SetColor("_Color", c); block.SetColor("_BaseColor", c);
                r.SetPropertyBlock(block);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;
            if (LifeTimer.Expired(Runner)) { Runner.Despawn(Object); return; }
            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var nearby = Network.NetworkPlayer.FindPlayersInRadius(transform.position, pickupRadius, OwnerTeam, aliveOnly: true);
            foreach (var p in nearby)
            {
                if (!p.HasStatus(Core.StatusFlags.Interacting)) continue;
                p.AddEnemyCard();
                RPC_PickedUp(p.PlayerRef);
                Runner.Despawn(Object);
                return;
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PickedUp(PlayerRef who)
        {
            Debug.Log($"[IDCard] {OwnerTeam} ID card taken by {who}");
            PickedUp?.Invoke(this, Network.NetworkPlayer.Resolve(who));
        }
    }
}
