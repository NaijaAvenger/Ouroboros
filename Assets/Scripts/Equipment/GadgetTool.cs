using UnityEngine;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// A gadget in the Gadget slot (v0.14). Gadgets are how a crew gets past the building's obstacles, and each
    /// player carries one, so which gadgets a crew brings decides which doors are open to it:
    ///
    ///  - Breaching Kit: blows open the nearest weak wall, strongbox, safe or breachable door. Loud.
    ///  - EMP Emitter: switches off cameras, turrets and laser grids around you for a while. Quiet.
    ///  - Bypass Key: opens the nearest maglocked staff door for a short while. Silent.
    ///  - Loot Scanner: marks containers, loose loot and vaults through walls for a while.
    ///  - Grapple Line: gives any specialist the Smith's grapple (passive, used with the Grapple key).
    ///
    /// Gadgets work on a cooldown rather than a count. A use that finds no target costs nothing.
    /// Runs on the state authority and, as prediction, on the owner's client; only the authority changes the world.
    /// </summary>
    public class GadgetTool : BaseEquipment
    {
        protected override bool OnUse()
        {
            if (data == null || owner == null || owner.Object == null) return false;
            bool authority = HasAuthority;
            Vector3 feet = owner.Position;
            float range = Mathf.Max(1f, data.range);
            float seconds = Mathf.Max(1f, data.effectDuration);

            switch (data.gadget)
            {
                case Ouroboros.Data.GadgetType.BreachingKit:
                {
                    var target = FindBreachTarget(owner, range, out Vector3 point);
                    if (target == null) return false;
                    if (authority)
                    {
                        target.Breach(owner.Team);
                        GameMode.AlarmSystem.Raise(8f, "Breach");
                        AI.AIBlackboard.ReportNoise(point, 40f);
                        loadout.NotifyGadget(Ouroboros.Data.GadgetType.BreachingKit, point, 3f);
                    }
                    return true;
                }
                case Ouroboros.Data.GadgetType.EmpEmitter:
                {
                    if (authority)
                    {
                        foreach (var device in Core.SceneUtil.FindAllInRadius<Interaction.ISecurityDevice>(feet, range)) device.Disable(seconds);
                        loadout.NotifyGadget(Ouroboros.Data.GadgetType.EmpEmitter, feet + Vector3.up, range);
                    }
                    return true;
                }
                case Ouroboros.Data.GadgetType.BypassKey:
                {
                    var door = Interaction.MagLockDoor.FindShut(feet, range);
                    if (door == null) return false;
                    if (authority)
                    {
                        door.Bypass(seconds);
                        loadout.NotifyGadget(Ouroboros.Data.GadgetType.BypassKey, door.transform.position + Vector3.up * 1.5f, 1.5f);
                    }
                    return true;
                }
                case Ouroboros.Data.GadgetType.LootScanner:
                {
                    if (authority)
                    {
                        loadout.StartScan(seconds);
                        loadout.NotifyGadget(Ouroboros.Data.GadgetType.LootScanner, feet + Vector3.up * 0.1f, range);
                    }
                    return true;
                }
                default:
                    return false; // passive gadgets (the grapple line) have no button
            }
        }

        /// <summary>The nearest thing in front of the player that a charge can open.</summary>
        private static Interaction.IBreachable FindBreachTarget(Network.NetworkPlayer owner, float range, out Vector3 point)
        {
            Vector3 eye = owner.EyePosition, aim = owner.AimDirection;
            Interaction.IBreachable best = null;
            float bestSq = range * range;
            point = eye + aim * 1.5f;

            var breakables = Interaction.Breakable.All;
            for (int i = 0; i < breakables.Count; i++)
            {
                var b = breakables[i];
                if (b == null || !b.IsAlive) continue;
                Vector3 centre = b.Position + Vector3.up * 0.8f;
                Vector3 to = centre - eye;
                float sq = to.sqrMagnitude;
                if (sq >= bestSq || Vector3.Dot(to.normalized, aim) < 0.2f) continue; // roughly in front
                bestSq = sq; best = b; point = centre;
            }
            if (best != null) return best;

            foreach (var candidate in Core.SceneUtil.FindAllInRadius<Interaction.IBreachable>(owner.Position, range))
            {
                if (candidate == null || candidate.IsBreached || !(candidate is MonoBehaviour mb)) continue;
                float sq = (mb.transform.position - eye).sqrMagnitude;
                if (sq >= bestSq) continue;
                bestSq = sq; best = candidate; point = mb.transform.position + Vector3.up;
            }
            return best;
        }
    }
}
