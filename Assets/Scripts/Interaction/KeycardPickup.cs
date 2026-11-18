using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A coloured keycard lying in the facility. Hold Interact next to it to take it; it opens the
    /// matching <see cref="VaultDoor"/> (consumed on use).
    /// </summary>
    public class KeycardPickup : NetworkBehaviour
    {
        [SerializeField] private KeycardColor color = KeycardColor.Red;
        [SerializeField] private float pickupRadius = 2f;

        [Networked] public NetworkBool Taken { get; set; }

        public KeycardColor Color => color;
        public float PickupRadius => pickupRadius;

        public static event System.Action<KeycardPickup, Network.NetworkPlayer> PickedUp;

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || Taken) return;
            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var nearby = Network.NetworkPlayer.FindPlayersInRadius(transform.position, pickupRadius, Core.TeamID.None, aliveOnly: true);
            foreach (var p in nearby)
            {
                if (!p.HasStatus(Core.StatusFlags.Interacting)) continue;
                p.AddKeycard(color);
                Taken = true;
                RPC_PickedUp(p.PlayerRef);
                Runner.Despawn(Object);
                return;
            }
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PickedUp(PlayerRef who)
        {
            Debug.Log($"[Keycard] {color} keycard taken by {who}");
            PickedUp?.Invoke(this, Network.NetworkPlayer.Resolve(who));
        }

        public static UnityEngine.Color ToColor(KeycardColor c)
        {
            switch (c)
            {
                case KeycardColor.Red:    return UnityEngine.Color.red;
                case KeycardColor.Blue:   return UnityEngine.Color.blue;
                case KeycardColor.Green:  return UnityEngine.Color.green;
                case KeycardColor.Yellow: return UnityEngine.Color.yellow;
                default: return UnityEngine.Color.white;
            }
        }
    }
}
