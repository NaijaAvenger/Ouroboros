using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// Carry-the-case objective: a single high-value item that must be physically carried to an
    /// extraction point. Hold Interact next to it to pick it up. The carrier is slowed
    /// (<see cref="Core.StatusFlags.Encumbered"/>), drops it on death or when they leave the match, and
    /// banks its value when they extract. Any player can pick it up, so it's the match's hot potato.
    /// </summary>
    public class LootCase : NetworkBehaviour
    {
        [SerializeField] private string caseName = "The Case";
        [SerializeField] private int value = 1500;
        [SerializeField] private float pickupRadius = 2f;
        [SerializeField] private Vector3 carryOffset = new Vector3(0f, 1.0f, -0.6f);
        [Tooltip("Seconds after a drop before it can be picked up again.")]
        [SerializeField] private float pickupDelay = 1f;

        [Networked] public PlayerRef Carrier { get; set; }
        [Networked] public NetworkBool Banked { get; set; }
        [Networked] private TickTimer PickupTimer { get; set; }

        public static readonly List<LootCase> All = new List<LootCase>();
        public static event System.Action<LootCase, Network.NetworkPlayer> PickedUp;
        public static event System.Action<LootCase> Dropped;

        public string CaseName => caseName;
        public int Value => value;
        public float PickupRadius => pickupRadius;
        public bool IsCarried => Carrier != default;

        private Network.NetworkPlayer carrier;

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || Banked) return;

            if (IsCarried)
            {
                if (carrier == null || carrier.PlayerRef != Carrier) carrier = Network.NetworkPlayer.Resolve(Carrier);
                if (carrier == null || carrier.Object == null || !carrier.IsActiveInMatch)
                {
                    Drop();
                    return;
                }
                carrier.SetStatus(Core.StatusFlags.Encumbered, true);
                transform.position = carrier.transform.position + carrier.transform.TransformDirection(carryOffset);
                transform.rotation = carrier.transform.rotation;
                return;
            }

            if (!PickupTimer.ExpiredOrNotRunning(Runner)) return;
            var gm = ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null || !gm.IsMatchLive) return;
            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var nearby = Network.NetworkPlayer.FindPlayersInRadius(transform.position, pickupRadius, Core.TeamID.None, aliveOnly: true);
            foreach (var p in nearby)
            {
                if (!p.HasStatus(Core.StatusFlags.Interacting) || p.HasStatus(Core.StatusFlags.Encumbered)) continue;
                PickUp(p);
                break;
            }
        }

        private void PickUp(Network.NetworkPlayer player)
        {
            carrier = player;
            Carrier = player.PlayerRef;
            player.SetStatus(Core.StatusFlags.Encumbered, true);
            RPC_PickedUp(player.PlayerRef);
        }

        /// <summary>Releases the case at the carrier's feet. State authority only.</summary>
        public void Drop()
        {
            if (!Object.HasStateAuthority || !IsCarried) return;
            var prev = carrier != null ? carrier : Network.NetworkPlayer.Resolve(Carrier);
            if (prev != null && prev.Object != null)
            {
                prev.SetStatus(Core.StatusFlags.Encumbered, false);
                transform.position = prev.transform.position + Vector3.up * 0.3f;
            }
            carrier = null;
            Carrier = default;
            PickupTimer = TickTimer.CreateFromSeconds(Runner, pickupDelay);
            RPC_Dropped();
        }

        /// <summary>Banks the case for an extracting carrier: returns its value and removes it. State authority only.</summary>
        public int BankFor(Network.NetworkPlayer player)
        {
            if (!Object.HasStateAuthority || Banked || player == null || player.PlayerRef != Carrier) return 0;
            Banked = true;
            player.SetStatus(Core.StatusFlags.Encumbered, false);
            Carrier = default;
            carrier = null;
            Runner.Despawn(Object);
            return value;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PickedUp(PlayerRef who)
        {
            var p = Network.NetworkPlayer.Resolve(who);
            Debug.Log($"[LootCase] {caseName} picked up by {who}");
            PickedUp?.Invoke(this, p);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Dropped()
        {
            Debug.Log($"[LootCase] {caseName} dropped");
            Dropped?.Invoke(this);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.6f, 0.1f);
            Gizmos.DrawWireSphere(transform.position, pickupRadius);
        }
    }
}
