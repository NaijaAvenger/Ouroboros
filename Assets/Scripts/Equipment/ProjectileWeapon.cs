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

            Runner.Spawn(stats.projectilePrefab, origin, Quaternion.LookRotation(aim), owner.Object.InputAuthority, (runner, obj) =>
            {
                var projectile = obj.GetComponent<Projectile>();
                if (projectile != null) projectile.Configure(shooter, aim * stats.projectileSpeed, stats);
            });

            loadout.NotifyFired(slotIndex, origin, false);
            return true;
        }
    }
}
