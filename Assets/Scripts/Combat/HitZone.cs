using UnityEngine;

namespace Ouroboros.Combat
{
    public enum HitZoneType
    {
        Body,
        Head
    }

    /// <summary>
    /// Marks a Fusion <c>Hitbox</c> (or a plain collider) as a body part (v0.9). Weapons read it to apply
    /// <see cref="Data.EquipmentData.headshotMultiplier"/>. A hit on something without a HitZone is a body hit,
    /// unless the weapon's height fallback says otherwise.
    /// </summary>
    public class HitZone : MonoBehaviour
    {
        public HitZoneType zone = HitZoneType.Body;
    }
}
