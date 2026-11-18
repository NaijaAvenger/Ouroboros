using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A note / terminal that holds the 4-digit code of a passcode <see cref="VaultDoor"/>. Reading it
    /// (hold Interact) reveals the code to the reader's whole team; the note stays for other teams.
    /// </summary>
    public class CodeNote : NetworkBehaviour
    {
        [SerializeField] private VaultDoor door;
        [SerializeField] private float readRadius = 2f;

        public VaultDoor Door => door;
        public float ReadRadius => readRadius;

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || door == null || door.Object == null) return;
            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var nearby = Network.NetworkPlayer.FindPlayersInRadius(transform.position, readRadius, Core.TeamID.None, aliveOnly: true);
            foreach (var p in nearby)
            {
                if (!p.HasStatus(Core.StatusFlags.Interacting)) continue;
                if (door.TeamKnowsCode(p.Team)) continue;
                door.RevealCodeToTeam(p.Team);
            }
        }
    }
}
