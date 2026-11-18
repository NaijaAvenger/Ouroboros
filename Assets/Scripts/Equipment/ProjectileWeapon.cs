using UnityEngine;
using Fusion;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// Spawns a networked <see cref="Projectile"/> (grenade launcher, rocket, thrown charge). The
    /// projectile prefab comes from <see cref="Data.EquipmentData.projectilePrefab"/> and must carry
    /// NetworkObject + NetworkTransform + Projectile.
    /// </summary>
    public class ProjectileWeapon : BaseEquipment
    {
        private const float MuzzleOffset = 0.8f;

        protected override bool OnUse()
        {
            if (data == null || Runner == null) return false;
            if (data.projectilePrefab == null)
            {
                Debug.LogWarning($"[ProjectileWeapon] '{data.name}' has no projectilePrefab");
                return false;
            }
            if (!loadout.ConsumeAmmo(slotIndex)) return false;

            Vector3 aim = owner.AimDirection;
            Vector3 origin = owner.EyePosition + aim * MuzzleOffset;
            var shooter = owner;
            var stats = data;

            // (v0.9) OnUse also runs on the owning client as prediction: it spends the round and shows the
            //        launch at once, but only the state authority creates the projectile.
            if (HasAuthority)
            {
                Runner.Spawn(stats.projectilePrefab, origin, Quaternion.LookRotation(aim), owner.Object.InputAuthority, (runner, obj) =>
                {
                    var projectile = obj.GetComponent<Projectile>();
                    if (projectile != null) projectile.Configure(shooter, aim * stats.projectileSpeed, stats);
                });
            }

            // [v0.8] loadout.NotifyFired(slotIndex, origin, false);
            loadout.RegisterShot(slotIndex, origin, false);
            return true;
        }
    }
}
