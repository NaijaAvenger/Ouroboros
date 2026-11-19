using System;
using Fusion;
using UnityEngine;

namespace Ouroboros.Network
{
    /// <summary>
    /// (v0.17) What crewmates do for each other, and what a player's body does for itself:
    ///
    ///  - <b>Ping.</b> One button marks what you are looking at for your crew. The state authority works out
    ///    what it is (an enemy, loot, a device, a downed crewmate's card, or just a place) and tells every peer;
    ///    only the pinger's crew shows it.
    ///  - <b>Revive.</b> A crewmate who works a downed player's ID card stands them back up where they fell
    ///    (see <see cref="Interaction.IDCardDrop"/>). It is faster than waiting for a redeploy, it works during
    ///    the getaway when redeploys do not, and it does not use up a respawn.
    ///  - <b>Regeneration.</b> Health comes back after a few seconds without taking damage.
    ///  - <b>Marks</b> (v0.18). A rival only ever gets a marker over their head while they are marked, and a
    ///    mark belongs to the crew that earned it. See <see cref="MarkFor"/>.
    /// </summary>
    public partial class NetworkPlayer
    {
        private const float PingRange = 150f;
        private const float PingCooldownSeconds = 0.4f;
        /// <summary>How close to the line of sight a small thing with no solid collider (a pickup, a card) must be to be pinged.</summary>
        private const float PingSnapRadius = 1.1f;

        /// <summary>Raised on every peer: who pinged, what kind, where, and what it is called.</summary>
        public static event Action<NetworkPlayer, Core.PingKind, Vector3, string> Pinged;
        /// <summary>Raised on every peer: the player who stood up, and the crewmate who did it.</summary>
        public static event Action<NetworkPlayer, NetworkPlayer> Revived;

        /// <summary>The latest place each crew was told to go, for bots. State authority only.</summary>
        public struct CrewPing { public Vector3 Position; public Core.PingKind Kind; public int Tick; public NetworkPlayer By; }
        private static readonly CrewPing[] LatestPings = new CrewPing[Core.GameConstants.MAX_TEAMS];

        private int lastPingTick = -100000;

        /// <summary>The crew's most recent ping if it is younger than <paramref name="maxAgeSeconds"/>. State authority only.</summary>
        public static bool TryGetCrewPing(Core.TeamID team, NetworkRunner runner, float maxAgeSeconds, out CrewPing ping)
        {
            ping = default;
            int idx = Core.TeamUtil.ToIndex(team);
            if (runner == null || idx < 0 || idx >= LatestPings.Length) return false;
            ping = LatestPings[idx];
            return ping.By != null && ping.By.Object != null && ((int)runner.Tick - ping.Tick) * runner.DeltaTime <= maxAgeSeconds;
        }

        // ------------------------------------------------------------------
        // Marks (v0.18): the only way a rival gets a marker over their head
        //
        // Before v0.18 anything that set the Revealed status put a diamond over the player for every rival:
        // a camera seeing them, a laser grid, a sniper taking aim. Those still set Revealed, which is a
        // gameplay state (it breaks stealth), but they no longer show anything. A marker now needs a mark:
        //   - a crew's own reveal ability (Recon Drone, Net Sight, Data Mine) marks for that crew only;
        //   - carrying loot for the richest crew during the getaway marks for everyone;
        //   - (v0.22) standing in a security camera's view marks for everyone, for as long as it lasts.
        // A crewmate's ping on a rival is a separate, static marker at the place they were seen.

        /// <summary>Tick until which this player is marked: slot 0 for everyone, slots 1-4 for each crew.</summary>
        [Networked, Capacity(Core.GameConstants.MAX_TEAMS + 1)]
        private NetworkArray<int> MarkUntil => default;

        private static NetworkPlayer localCache;

        /// <summary>The player this peer controls, or null before they spawn.</summary>
        public static NetworkPlayer FindLocal()
        {
            if (localCache != null && localCache.Object != null && localCache.IsLocalPlayer) return localCache;
            localCache = null;
            for (int i = 0; i < All.Count; i++)
            {
                if (All[i] != null && All[i].Object != null && All[i].IsLocalPlayer) { localCache = All[i]; break; }
            }
            return localCache;
        }

        /// <summary>
        /// Lets <paramref name="viewers"/> see a marker over this player for <paramref name="seconds"/>.
        /// <see cref="Core.TeamID.None"/> means every crew. State authority only.
        /// </summary>
        public void MarkFor(Core.TeamID viewers, float seconds)
        {
            if (!Object.HasStateAuthority || seconds <= 0f) return;
            int slot = viewers == Core.TeamID.None ? 0 : Core.TeamUtil.ToIndex(viewers) + 1;
            if (slot < 0 || slot >= MarkUntil.Length) return;
            int until = (int)Runner.Tick + Mathf.CeilToInt(seconds / Mathf.Max(0.0001f, Runner.DeltaTime));
            if (until > MarkUntil[slot]) MarkUntil.Set(slot, until);
        }

        /// <summary>Removes every mark: smoke, death.</summary>
        public void ClearMarks()
        {
            if (!Object.HasStateAuthority) return;
            for (int i = 0; i < MarkUntil.Length; i++) MarkUntil.Set(i, 0);
        }

        /// <summary>True if a player on <paramref name="viewer"/> may see a marker over this player right now.</summary>
        public bool IsMarkedFor(Core.TeamID viewer)
        {
            if (Object == null || Runner == null || !IsActiveInMatch || viewer == Team) return false;
            int now = (int)Runner.Tick;
            if (MarkUntil[0] > now) return true;
            int slot = Core.TeamUtil.ToIndex(viewer) + 1;
            return slot > 0 && slot < MarkUntil.Length && MarkUntil[slot] > now;
        }

        // ------------------------------------------------------------------
        // Ping

        /// <summary>Marks what this player is looking at for their crew. State authority only; rate limited.</summary>
        public void TryPing()
        {
            if (!Object.HasStateAuthority || !IsActiveInMatch) return;
            int now = (int)Runner.Tick;
            int cooldown = Mathf.CeilToInt(PingCooldownSeconds / Mathf.Max(0.0001f, Runner.DeltaTime));
            if (!Core.AssistRules.CanPing(now, lastPingTick, cooldown)) return;

            Vector3 origin = EyePosition, direction = AimDirection;
            Core.PingKind kind = Core.PingKind.Go;
            Vector3 point;
            string label = "";
            float reach = PingRange;

            bool solid = Physics.Raycast(origin, direction, out RaycastHit hit, PingRange, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            if (solid)
            {
                point = hit.point;
                reach = hit.distance;
                Classify(hit.collider, ref kind, ref label, ref point);
            }
            else
            {
                point = origin + direction * PingRange;
            }

            // Pickups and cards are small and have no solid collider: take the one nearest the line of sight,
            // if it is in front of whatever the ray hit.
            bool snapped = SnapToSmallThings(origin, direction, reach + 0.5f, ref kind, ref label, ref point);
            if (!solid && !snapped) return; // looking at the sky

            lastPingTick = now;
            int idx = Core.TeamUtil.ToIndex(Team);
            if (idx >= 0 && idx < LatestPings.Length) LatestPings[idx] = new CrewPing { Position = point, Kind = kind, Tick = now, By = this };
            RPC_Ping((byte)kind, point, label);
        }

        private void Classify(Collider collider, ref Core.PingKind kind, ref string label, ref Vector3 point)
        {
            var player = collider.GetComponentInParent<NetworkPlayer>();
            if (player != null)
            {
                if (player == this) return;
                if (player.Team != Team && player.IsActiveInMatch) { kind = Core.PingKind.Enemy; label = player.DisplayName.ToString(); point = player.Position + Vector3.up; }
                return;
            }
            var agent = collider.GetComponentInParent<AI.BaseAIAgent>();
            if (agent != null) { kind = Core.PingKind.Enemy; label = agent.AgentType == AI.AIAgentType.Sniper ? "Sniper" : agent.AgentType == AI.AIAgentType.Elite ? "Elite guard" : "Guard"; point = agent.transform.position + Vector3.up * 0.6f; return; }

            var turret = collider.GetComponentInParent<Interaction.SentryTurret>();
            if (turret != null)
            {
                bool ours = turret.IsHijacked && turret.OwnerTeam == Team;
                kind = ours ? Core.PingKind.Device : Core.PingKind.Enemy;
                label = ours ? "Our turret" : "Turret";
                point = turret.transform.position + Vector3.up * 0.8f;
                return;
            }
            var camera = collider.GetComponentInParent<Interaction.SecurityCamera>();
            if (camera != null) { kind = Core.PingKind.Enemy; label = "Camera"; point = camera.transform.position; return; }

            var lootCase = collider.GetComponentInParent<GameMode.LootCase>();
            if (lootCase != null) { kind = Core.PingKind.Loot; label = lootCase.CaseName; return; }
            var breakable = collider.GetComponentInParent<Interaction.Breakable>();
            if (breakable != null) { kind = Core.PingKind.Loot; label = breakable.Label; return; }
            var objective = collider.GetComponentInParent<GameMode.LootObjective>();
            if (objective != null) { kind = Core.PingKind.Loot; label = objective.ObjectiveName; return; }
            var vaultDoor = collider.GetComponentInParent<Interaction.VaultDoor>();
            if (vaultDoor != null) { kind = Core.PingKind.Loot; label = vaultDoor.VaultName; return; }

            var station = collider.GetComponentInParent<GameMode.CashOutStation>();
            if (station != null)
            {
                bool theirs = station.IsActive && station.OwnerTeam != Team;
                kind = theirs ? Core.PingKind.Enemy : Core.PingKind.Device;
                label = theirs ? "Steal this cash-out" : station.IsActive ? "Defend the cash-out" : "Cash out here";
                point = station.transform.position + Vector3.up * 1.5f;
                return;
            }
            var hackable = collider.GetComponentInParent<Interaction.IQuickHackable>() as Component;
            if (hackable != null) { kind = Core.PingKind.Device; label = hackable.gameObject.name; return; }
            var terminal = collider.GetComponentInParent<Interaction.HackTerminal>();
            if (terminal != null) { kind = Core.PingKind.Device; label = terminal.TerminalName; }
        }

        private bool SnapToSmallThings(Vector3 origin, Vector3 direction, float reach, ref Core.PingKind kind, ref string label, ref Vector3 point)
        {
            float best = PingSnapRadius;
            bool found = false;
            Core.PingKind foundKind = kind; string foundLabel = label; Vector3 foundPoint = point;

            var drops = GameMode.LootDrop.All;
            for (int i = 0; i < drops.Count; i++)
            {
                var d = drops[i];
                if (d == null || d.Object == null) continue;
                Vector3 p = d.transform.position;
                float off = Core.AssistRules.DistanceFromRay(origin.x, origin.y, origin.z, direction.x, direction.y, direction.z, p.x, p.y, p.z, out float along);
                if (along <= 0f || along > reach || off >= best) continue;
                best = off; found = true; foundKind = Core.PingKind.Loot; foundPoint = p;
                var def = d.ItemId > 0 ? Data.LootCatalog.Active.Get(d.ItemId) : null;
                // [v0.23] foundLabel = def != null && !string.IsNullOrEmpty(def.name) ? def.name : $"${d.Value}";
                foundLabel = d.IsAmmo ? "Ammo" : def != null && !string.IsNullOrEmpty(def.name) ? def.name : $"${d.Value}"; // (v0.24)
            }

            var cards = Interaction.IDCardDrop.All;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c == null || c.Object == null) continue;
                Vector3 p = c.transform.position;
                float off = Core.AssistRules.DistanceFromRay(origin.x, origin.y, origin.z, direction.x, direction.y, direction.z, p.x, p.y, p.z, out float along);
                if (along <= 0f || along > reach || off >= best) continue;
                best = off; found = true; foundPoint = p;
                var owner = c.Owner;
                if (c.OwnerTeam == Team && c.CanBeRevivedBy(this)) { foundKind = Core.PingKind.Revive; foundLabel = owner != null ? owner.DisplayName.ToString() : "Crewmate"; }
                else { foundKind = Core.PingKind.Loot; foundLabel = c.OwnerTeam == Team ? "Our ID card" : "ID card"; }
            }

            if (!found) return false;
            kind = foundKind; label = foundLabel; point = foundPoint;
            return true;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Ping(byte kind, Vector3 point, string label)
        {
            Pinged?.Invoke(this, (Core.PingKind)kind, point, label ?? "");
        }

        // ------------------------------------------------------------------
        // Revive

        /// <summary>
        /// Stands this player back up where they fell, on part of their health. Does not count as a respawn.
        /// State authority only. Returns false if the player is not down.
        /// </summary>
        public bool Revive(NetworkPlayer by, float healthFraction)
        {
            if (!Object.HasStateAuthority || IsAlive || IsExtracted) return false;
            Health = Core.AssistRules.ReviveHealth(MaxHealth, healthFraction);
            Stamina = MaxStamina;
            Status = Core.StatusFlags.None;
            IsAlive = true;
            RespawnTimer = TickTimer.None;
            LastDamageTick = (int)Runner.Tick; // regeneration waits its full delay after a revive
            currentClass?.OnOwnerRespawned();
            RPC_Revived(by != null && by.Object != null ? by.Object.Id : default);
            return true;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Revived(NetworkId by)
        {
            NetworkPlayer reviver = null;
            if (by != default && Runner.TryFindObject(by, out NetworkObject obj) && obj != null) reviver = obj.GetComponent<NetworkPlayer>();
            Debug.Log($"[NetworkPlayer] {DisplayName} was revived" + (reviver != null ? $" by {reviver.DisplayName}" : ""));
            Art.Fx.Ring(transform.position - Vector3.up * 0.9f, 2.2f, new Color(0.5f, 1f, 0.65f, 0.9f), 0.7f, 0.16f);
            Art.Fx.Burst(transform.position, new Color(0.6f, 1f, 0.7f, 1f), 22, 3.5f, 0.09f, 0.7f, -0.4f);
            Revived?.Invoke(this, reviver);
        }

        // ------------------------------------------------------------------
        // Changing class and gear during a match (v0.23)

        /// <summary>Tick of this player's last redeploy, or 0 if they have not redeployed this match.</summary>
        [Networked] public int LastRedeployTick { get; set; }

        /// <summary>Seconds since this player last redeployed. Very large if they never have.</summary>
        public float SecondsSinceRedeploy => Object != null && Runner != null && LastRedeployTick > 0
            ? ((int)Runner.Tick - LastRedeployTick) * Runner.DeltaTime : float.MaxValue;

        // ------------------------------------------------------------------
        // Regeneration

        /// <summary>Seconds since this player last took damage.</summary>
        public float SecondsSinceDamage => Object != null && Runner != null ? ((int)Runner.Tick - LastDamageTick) * Runner.DeltaTime : 0f;

        /// <summary>True while health is coming back on its own. Works on every peer, for the HUD.</summary>
        public bool IsRegenerating
        {
            get
            {
                var gm = GameMode.ExtractionHeistGameMode.Instance;
                if (gm == null || gm.Config == null || !IsActiveInMatch) return false;
                // [v0.20] return Core.AssistRules.IsRegenerating(Health, MaxHealth, SecondsSinceDamage, gm.Config.healthRegenDelay, gm.Config.healthRegenPerSecond, gm.Config.healthRegenCap);
                int perks = GameMode.ExtractionHeistGameMode.PerksOf(Team); // (v0.21) Second Wind
                return Core.AssistRules.IsRegenerating(Health, MaxHealth, SecondsSinceDamage, gm.Config.healthRegenDelay * Core.VaultRules.RegenDelay(perks),
                    gm.Config.healthRegenPerSecond * Core.VaultRules.RegenRate(perks), gm.Config.healthRegenCap);
            }
        }

        private void TickRegen()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Config == null || gm.Config.healthRegenPerSecond <= 0f) return;
            if (BurnTimer.IsRunning && !BurnTimer.Expired(Runner)) return; // nothing heals while it burns
            // [v0.20] Health = Core.AssistRules.RegenStep(Health, MaxHealth, SecondsSinceDamage, gm.Config.healthRegenDelay,
            // [v0.20]     gm.Config.healthRegenPerSecond, gm.Config.healthRegenCap, Runner.DeltaTime);
            int perks = GameMode.ExtractionHeistGameMode.PerksOf(Team); // (v0.21) Second Wind: sooner and faster
            Health = Core.AssistRules.RegenStep(Health, MaxHealth, SecondsSinceDamage, gm.Config.healthRegenDelay * Core.VaultRules.RegenDelay(perks),
                gm.Config.healthRegenPerSecond * Core.VaultRules.RegenRate(perks), gm.Config.healthRegenCap, Runner.DeltaTime);
        }
    }
}
