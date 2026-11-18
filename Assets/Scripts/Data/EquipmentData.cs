using UnityEngine;

namespace Ouroboros.Data
{
    /// <summary>
    /// What kind of behaviour an equipment item instantiates. Add a kind here and a case in
    /// <c>EquipmentLoadout.ComponentTypeFor</c> to introduce a new item family.
    /// </summary>
    public enum EquipmentKind
    {
        HitscanWeapon,
        ProjectileWeapon,
        PassiveGear,
        Deployable
    }

    /// <summary>
    /// ScriptableObject for equipment configuration.
    /// Allows designers to create and modify equipment data without code changes.
    ///
    /// v0.4: items are referenced across the network by their index in <see cref="EquipmentRegistry"/>,
    /// and the weapon / modifier fields below drive <c>HitscanWeapon</c>, <c>ProjectileWeapon</c> and
    /// <c>PassiveGear</c> directly, so most new items need no code.
    /// </summary>
    [CreateAssetMenu(fileName = "New Equipment", menuName = "Ouroboros/Equipment Data")]
    public class EquipmentData : ScriptableObject
    {
        [Header("Equipment Information")]
        public string equipmentName;
        [TextArea(2, 4)]
        public string description;
        public Sprite equipmentIcon;
        [Tooltip("Optional visual prefab attached to the player (cosmetic only).")]
        public GameObject equipmentPrefab;

        [Header("Equipment Type")]
        public Equipment.EquipmentSlotType slotType;
        public EquipmentRarity rarity;
        [Tooltip("(v0.4) Which behaviour class drives this item.")]
        public EquipmentKind kind = EquipmentKind.HitscanWeapon;

        [Header("Equipment Stats")]
        [Tooltip("Seconds between uses (for weapons: 1 / fire rate).")]
        public float cooldown = 5f;
        public int maxUses = -1; // -1 for unlimited
        public float range = 10f;
        public float effectDuration = 0f;

        [Header("Weapon (v0.4)")]
        public float damage = 20f;
        [Tooltip("Hold to keep firing (uses the held button state instead of the press edge).")]
        public bool automatic = false;
        [Tooltip("Rounds per magazine. 0 = no ammo system.")]
        public int magazineSize = 12;
        [Tooltip("Spare rounds carried at spawn.")]
        public int reserveAmmo = 48;
        public float reloadTime = 1.5f;
        [Tooltip("Cone half-angle in degrees applied per shot.")]
        public float spreadDegrees = 1f;
        [Tooltip("Rays per shot (shotguns).")]
        public int pelletCount = 1;
        [Tooltip("Layers a hitscan ray can hit. Leave as Everything to use the default raycast layers.")]
        public LayerMask hitMask = ~0;
        [Tooltip("(v0.6) How far guards hear this weapon. Suppressed weapons: small values.")]
        public float noiseRadius = 25f;

        [Header("Projectile (v0.4, kind = ProjectileWeapon)")]
        public Fusion.NetworkObject projectilePrefab;
        public float projectileSpeed = 25f;
        [Tooltip("Vertical acceleration (0 = straight, -9.81 = grenade arc).")]
        public float projectileGravity = 0f;
        [Tooltip("0 = direct damage on impact; > 0 = radial damage with this radius.")]
        public float explosionRadius = 0f;
        [Tooltip("0 = explode on impact; > 0 = explode after this many seconds (grenades).")]
        public float fuseTime = 0f;
        public float projectileLifetime = 6f;

        [Header("Stat Modifiers (fractions, e.g. 0.15 = +15%)")]
        public float damageModifier = 0f;
        [Tooltip("Fraction of incoming damage removed while equipped (0.2 = take 20% less).")]
        public float defenseModifier = 0f;
        public float speedModifier = 0f;
        public float stealthModifier = 0f;

        [Header("Requirements")]
        public int requiredLevel = 1;
        public Core.PlayerClassType[] allowedClasses; // Empty = all classes

        public bool UsesAmmo => magazineSize > 0;

        public bool IsAllowedFor(Core.PlayerClassType classType)
        {
            if (allowedClasses == null || allowedClasses.Length == 0) return true;
            for (int i = 0; i < allowedClasses.Length; i++)
            {
                if (allowedClasses[i] == classType) return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Equipment rarity levels.
    /// </summary>
    public enum EquipmentRarity
    {
        Common,
        Uncommon,
        Rare,
        Epic,
        Legendary
    }
}
