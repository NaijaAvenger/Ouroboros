using UnityEngine;

namespace Ouroboros.UI
{
    /// <summary>
    /// Turns what happens in a match into what the local player feels (v0.15): camera shake and controller
    /// rumble, through <see cref="Core.Juice"/> (Feel and Nice Vibrations underneath).
    ///
    ///   Firing            a kick scaled to the weapon, a light tap on the pad
    ///   Being hit         a shake scaled to the damage, a medium tap
    ///   Explosions        a shake that fades with distance (see Art.Fx.Explosion), a heavy tap up close
    ///   A kill            a success pattern
    ///   Dying             a long shake, a failure pattern
    ///   The getaway, a map event, tripping lasers     a warning pattern
    ///
    /// Cosmetic and local. Added automatically by <see cref="MatchFeedback"/>.
    /// </summary>
    public class JuiceEvents : MonoBehaviour
    {
        private Network.NetworkPlayer local;
        private float lastHealth = -1f;
        private int shotsSinceTap;

        private void OnEnable()
        {
            Equipment.NetworkLoadout.WeaponFired += OnWeaponFired;
            Equipment.NetworkLoadout.WeaponSwapped += OnWeaponSwapped;
            Equipment.NetworkLoadout.GadgetUsed += OnGadgetUsed;
            Network.NetworkPlayer.HitConfirmed += OnHitConfirmed;
            Network.NetworkPlayer.PlayerKilled += OnPlayerKilled;
            GameMode.ExtractionHeistGameMode.GetawayCalled += OnGetawayCalled;
            GameMode.LootObjective.Completed += OnVaultOpened;
            GameMode.MapEventDirector.Fired += OnMapEvent;
            Interaction.Breakable.Broken += OnBroken;
            Interaction.LaserGrid.Tripped += OnLaserTripped;
            GameMode.CashOutStation.Moment += OnCashOut; // (v0.16)
        }

        private void OnDisable()
        {
            Equipment.NetworkLoadout.WeaponFired -= OnWeaponFired;
            Equipment.NetworkLoadout.WeaponSwapped -= OnWeaponSwapped;
            Equipment.NetworkLoadout.GadgetUsed -= OnGadgetUsed;
            Network.NetworkPlayer.HitConfirmed -= OnHitConfirmed;
            Network.NetworkPlayer.PlayerKilled -= OnPlayerKilled;
            GameMode.ExtractionHeistGameMode.GetawayCalled -= OnGetawayCalled;
            GameMode.LootObjective.Completed -= OnVaultOpened;
            GameMode.MapEventDirector.Fired -= OnMapEvent;
            Interaction.Breakable.Broken -= OnBroken;
            Interaction.LaserGrid.Tripped -= OnLaserTripped;
            GameMode.CashOutStation.Moment -= OnCashOut;
        }

        private Network.NetworkPlayer Local()
        {
            if (local != null && local.Object != null && local.IsLocalPlayer) return local;
            local = null;
            lastHealth = -1f;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Object != null && all[i].IsLocalPlayer) { local = all[i]; break; }
            }
            return local;
        }

        private void Update()
        {
            var me = Local();
            if (me == null) return;

            // Damage taken: read from the networked health, so it covers every source without a hook in each.
            float health = me.Health;
            if (lastHealth >= 0f && health < lastHealth - 0.5f && me.IsAlive)
            {
                float lost = lastHealth - health;
                Core.Juice.Shake(Mathf.Clamp(0.012f + lost * 0.0011f, 0.012f, 0.07f), 0.22f);
                Core.Juice.Haptic(lost >= 30f ? Core.Juice.Tap.Heavy : Core.Juice.Tap.Medium);
            }
            lastHealth = health;
        }

        private void OnWeaponFired(Equipment.NetworkLoadout loadout, int slot, Vector3 point, bool hit)
        {
            if (loadout == null || loadout.Owner == null || !loadout.Owner.IsLocalPlayer) return;
            var data = loadout.GetData(slot);
            float kick = data != null ? Mathf.Clamp(data.recoilPitch * 0.011f, 0.004f, 0.04f) : 0.008f;
            if (loadout.Aiming) kick *= 0.6f;
            Core.Juice.Shake(kick, 0.09f, 34f);

            // An automatic weapon would buzz the pad into one long hum: tap every third round.
            bool automatic = data != null && data.automatic;
            if (!automatic || ++shotsSinceTap >= 3)
            {
                shotsSinceTap = 0;
                Core.Juice.Haptic(data != null && data.recoilPitch >= 2f ? Core.Juice.Tap.Heavy : Core.Juice.Tap.Light);
            }
        }

        private void OnWeaponSwapped(Equipment.NetworkLoadout loadout, int slot)
        {
            if (loadout != null && loadout.Owner != null && loadout.Owner.IsLocalPlayer) Core.Juice.Haptic(Core.Juice.Tap.Select);
        }

        private void OnGadgetUsed(Equipment.NetworkLoadout loadout, Data.GadgetType type, Vector3 point, float reach)
        {
            if (loadout != null && loadout.Owner != null && loadout.Owner.IsLocalPlayer) Core.Juice.Haptic(Core.Juice.Tap.Medium);
            if (type == Data.GadgetType.EmpEmitter) Core.Juice.ShakeAt(point, 0.03f, reach + 6f, 0.3f);
        }

        private void OnHitConfirmed(Network.NetworkPlayer shooter, float damage, bool headshot, bool killed)
        {
            if (shooter == null || !shooter.IsLocalPlayer) return;
            if (killed) Core.Juice.Haptic(Core.Juice.Tap.Success);
        }

        private void OnPlayerKilled(Network.NetworkPlayer victim, Fusion.PlayerRef killer)
        {
            if (victim == null || !victim.IsLocalPlayer) return;
            Core.Juice.Shake(0.08f, 0.55f, 18f);
            Core.Juice.Haptic(Core.Juice.Tap.Failure);
        }

        private void OnGetawayCalled(Core.TeamID team) => Core.Juice.Haptic(Core.Juice.Tap.Warning);

        private void OnVaultOpened(GameMode.LootObjective objective, Core.TeamID team, int loot)
        {
            var me = Local();
            if (objective == null) return;
            Core.Juice.ShakeAt(objective.transform.position, 0.035f, 14f, 0.35f);
            if (me != null && me.Team == team) Core.Juice.Haptic(Core.Juice.Tap.Success);
        }

        private void OnMapEvent(GameMode.MapEvent e)
        {
            Core.Juice.Shake(0.015f, 0.5f, 12f);
            Core.Juice.Haptic(Core.Juice.Tap.Warning);
        }

        private void OnBroken(Interaction.Breakable b)
        {
            if (b == null) return;
            bool big = b.Kind == Interaction.BreakableKind.WeakWall;
            Core.Juice.ShakeAt(b.Position, big ? 0.09f : 0.03f, big ? 26f : 10f, big ? 0.4f : 0.2f);
        }

        private void OnCashOut(GameMode.CashOutStation station, GameMode.CashOutMoment moment, Core.TeamID team, Core.TeamID from, int value)
        {
            var me = Local();
            if (me == null) return;
            bool mine = me.Team == team, wasMine = me.Team == from;
            switch (moment)
            {
                case GameMode.CashOutMoment.Started:  Core.Juice.Haptic(mine ? Core.Juice.Tap.Success : Core.Juice.Tap.Warning); break;
                case GameMode.CashOutMoment.Hijacked: if (mine || wasMine) Core.Juice.Haptic(mine ? Core.Juice.Tap.Success : Core.Juice.Tap.Failure); break;
                case GameMode.CashOutMoment.Banked:   if (mine) Core.Juice.Haptic(Core.Juice.Tap.Success); break;
            }
        }

        private void OnLaserTripped(Interaction.LaserGrid grid, Network.NetworkPlayer who)
        {
            if (who == null || !who.IsLocalPlayer) return;
            Core.Juice.Shake(0.04f, 0.3f);
            Core.Juice.Haptic(Core.Juice.Tap.Warning);
        }
    }
}
