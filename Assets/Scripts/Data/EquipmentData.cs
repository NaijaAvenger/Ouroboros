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
        // [v0.13] Deployable
        Deployable,
        /// <summary>(v0.14) A tool in the Gadget slot. See <see cref="GadgetType"/> and <c>Equipment.GadgetTool</c>.</summary>
        Gadget
    }

    /// <summary>(v0.14) What a gadget does. Values are stored in assets: add new ones at the end.</summary>
    public enum GadgetType
    {
        None = 0,
        BreachingKit = 1,
        EmpEmitter = 2,
        BypassKey = 3,
        LootScanner = 4,
        GrappleLine = 5
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
        [Tooltip("(v0.14) For kind = Gadget: what it does. Its reach is 'range' and how long its effect lasts is 'effectDuration'.")]
        public GadgetType gadget = GadgetType.None;

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

        [Header("Weapon feel (v0.9)")]
        [Tooltip("Damage multiplier for hits on a Head hit zone.")]
        public float headshotMultiplier = 1.5f;
        [Tooltip("Full damage up to this distance (metres). Leave start and end at 0 for no falloff.")]
        public float falloffStart = 0f;
        [Tooltip("Damage has dropped to the minimum fraction at this distance.")]
        public float falloffEnd = 0f;
        [Range(0f, 1f)] public float falloffMinFraction = 0.5f;
        [Tooltip("Degrees of spread added by each shot.")]
        public float bloomPerShot = 0.35f;
        [Tooltip("Most spread sustained fire can add, in degrees.")]
        public float bloomMax = 3f;
        [Tooltip("Degrees of bloom lost per second while not firing.")]
        public float bloomRecoveryPerSecond = 6f;
        [Tooltip("Degrees the view kicks upward per shot.")]
        public float recoilPitch = 0.6f;
        [Tooltip("Largest sideways kick per shot, in degrees (random left or right).")]
        public float recoilYaw = 0.25f;
        [Tooltip("How quickly the view settles back after firing (higher = snappier).")]
        public float recoilRecoveryPerSecond = 9f;
        [Tooltip("Set by the scene builder once it has applied a per-weapon feel profile. 0 = defaults above.")]
        public int feelVersion = 0;

        [Header("Handling (v0.13): aiming down sights and swapping")]
        [Tooltip("Magnification while aiming. 1 = no zoom, 1.25 = rifle sights, 2.5 or more = scope.")]
        public float adsZoom = 1.25f;
        [Tooltip("Spread while aiming, as a fraction of hip spread.")]
        [Range(0f, 1f)] public float adsSpreadMultiplier = 0.4f;
        [Tooltip("Movement speed while aiming, as a fraction of walking speed.")]
        [Range(0.2f, 1f)] public float adsMoveMultiplier = 0.65f;
        [Tooltip("View kick while aiming, as a fraction of the hip kick.")]
        [Range(0f, 1f)] public float adsRecoilMultiplier = 0.75f;
        [Tooltip("Seconds to bring the sights up or down (view only).")]
        public float adsSeconds = 0.18f;
        [Tooltip("Seconds after swapping to this weapon before it can fire.")]
        public float swapSeconds = 0.4f;
        [Tooltip("Position in front of the camera while aiming: centred, with the sights near the middle of the screen.")]
        public Vector3 adsPosition = new Vector3(0f, -0.135f, 0.36f);

        [Header("Model placement (v0.12): how equipmentPrefab sits when shown by Art.WeaponView")]
        [Tooltip("Position in front of the camera for the local player's first-person view.")]
        public Vector3 viewPosition = new Vector3(0.2f, -0.2f, 0.45f);
        [Tooltip("Position relative to the hand joint on other players' characters.")]
        public Vector3 handPosition = new Vector3(0f, 0.02f, 0.12f);
        [Tooltip("Rotation applied to the model. The imported gun pack's barrels point along +X, so the default turns them to face forward.")]
        public Vector3 viewEuler = new Vector3(0f, -90f, 0f);
        public float viewScale = 1f;

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
