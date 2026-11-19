using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A dead player's ID card. Players of a different team can take it (hold Interact) and swipe it at a
    /// <see cref="VaultLockType.PlayerCard"/> vault door. Expires after a while.
    ///
    /// (v0.17) A player of the same team can hold Interact at it to revive its owner where they fell, as long as
    /// the owner is still down. A revive uses the card up. So the card is a race: the crew wants it to get a
    /// player back, and rivals want it for the vault it opens and to stop the revive.
    /// </summary>
    public class IDCardDrop : NetworkBehaviour
    {
        [SerializeField] private float pickupRadius = 1.8f;
        [SerializeField] private float lifetime = 120f;

        [Tooltip("(v0.17) How fast an abandoned revive runs back down, as a share per second.")]
        [SerializeField] private float reviveDrainPerSecond = 0.6f;
        [Tooltip("(v0.17) How close a crewmate must stand to revive.")]
        [SerializeField] private float reviveRadius = 2.4f;

        [Networked] public Core.TeamID OwnerTeam { get; set; }
        [Networked] private TickTimer LifeTimer { get; set; }
        /// <summary>(v0.17) The player this card fell from.</summary>
        [Networked] public NetworkId OwnerId { get; set; }
        /// <summary>(v0.17) 0 to 1: how far a crewmate's revive has got.</summary>
        [Networked] public float ReviveProgress { get; set; }

        /// <summary>(v0.17) The player this card fell from, if they are still in the session.</summary>
        public Network.NetworkPlayer Owner
        {
            get
            {
                if (owner != null && owner.Object != null) return owner;
                owner = null;
                if (Object == null || OwnerId == default) return null;
                if (Runner.TryFindObject(OwnerId, out NetworkObject obj) && obj != null) owner = obj.GetComponent<Network.NetworkPlayer>();
                return owner;
            }
        }
        private Network.NetworkPlayer owner;

        public float ReviveRadius => reviveRadius;
        /// <summary>(v0.17) True while the owner is down and could be stood back up with this card.</summary>
        public bool OwnerIsDown
        {
            get
            {
                var gm = GameMode.ExtractionHeistGameMode.Instance;
                if (gm == null || gm.Config == null || !gm.Config.allowRevive || !gm.IsMatchLive) return false;
                var o = Owner;
                return o != null && !o.IsAlive && !o.IsExtracted;
            }
        }

        /// <summary>(v0.17) True if <paramref name="player"/> could revive this card's owner right now.</summary>
        public bool CanBeRevivedBy(Network.NetworkPlayer player)
        {
            return player != null && player.Object != null && player.IsActiveInMatch && player.Team == OwnerTeam && player != Owner && OwnerIsDown;
        }

        public bool InReviveRange(Vector3 position) => (position - transform.position).sqrMagnitude <= reviveRadius * reviveRadius;

        public static event System.Action<IDCardDrop, Network.NetworkPlayer> PickedUp;

        public float PickupRadius => pickupRadius;
        /// <summary>(v0.10) Live registry on this peer.</summary>
        public static readonly System.Collections.Generic.List<IDCardDrop> All = new System.Collections.Generic.List<IDCardDrop>();

        public override void Despawned(NetworkRunner runner, bool hasState) { All.Remove(this); }

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
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
            if (TickRevive()) return; // (v0.17)
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

        /// <summary>
        /// (v0.17) A crewmate holding Interact at the card fills the revive; letting go drains it. Returns true
        /// when the owner stood up and the card was used.
        /// </summary>
        private bool TickRevive()
        {
            if (!OwnerIsDown)
            {
                if (ReviveProgress > 0f) ReviveProgress = 0f;
                return false;
            }
            Network.NetworkPlayer reviver = null;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (!CanBeRevivedBy(p) || !InReviveRange(p.Position)) continue;
                if (!p.HasStatus(Core.StatusFlags.Interacting) || p.HasStatus(Core.StatusFlags.EMPDisabled)) continue;
                reviver = p;
                break;
            }

            var config = GameMode.ExtractionHeistGameMode.Instance.Config;
            // [v0.20] ReviveProgress = Core.AssistRules.ReviveStep(ReviveProgress, reviver != null, Runner.DeltaTime, config.reviveSeconds, reviveDrainPerSecond);
            float reviveSeconds = config.reviveSeconds * Core.VaultRules.ActionTime(GameMode.ExtractionHeistGameMode.PerksOf(OwnerTeam)); // (v0.21) Quick Hands
            ReviveProgress = Core.AssistRules.ReviveStep(ReviveProgress, reviver != null, Runner.DeltaTime, reviveSeconds, reviveDrainPerSecond);
            if (reviver == null || ReviveProgress < 1f) return false;

            var revived = Owner;
            if (revived == null || !revived.Revive(reviver, config.reviveHealthFraction)) { ReviveProgress = 0f; return false; }
            Runner.Despawn(Object);
            return true;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_PickedUp(PlayerRef who)
        {
            Debug.Log($"[IDCard] {OwnerTeam} ID card taken by {who}");
            PickedUp?.Invoke(this, Network.NetworkPlayer.Resolve(who));
        }
    }
}
