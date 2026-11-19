using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>What kind of gun a piece of equipment looks like. Derived from its stats, so new items need no art entry.</summary>
    public enum WeaponShape { Pistol, Smg, Rifle, Shotgun, Marksman, Launcher }

    /// <summary>
    /// Code-built models for gear (v0.13): stand-in weapons for items without a prefab, the things specialists
    /// deploy (trap, charge, drone, grenade), and the passive gear worn on the body. The rule this file exists
    /// for: nothing a player carries or places is invisible.
    ///
    /// Weapons are built with the barrel along +Z and the grip at the origin, which is how a hand or a camera
    /// wants to hold them. Nothing here has colliders.
    /// </summary>
    public static class GearBuilder
    {
        private static readonly Color Gunmetal = new Color(0.16f, 0.17f, 0.2f);
        private static readonly Color Steel = new Color(0.34f, 0.36f, 0.4f);
        private static readonly Color Polymer = new Color(0.1f, 0.1f, 0.12f);
        private static readonly Color Brass = new Color(0.8f, 0.62f, 0.22f);
        private static readonly Color Warning = new Color(1f, 0.25f, 0.15f);

        // ------------------------------------------------------------------
        // Weapons

        public static WeaponShape ShapeOf(Data.EquipmentData data)
        {
            if (data == null) return WeaponShape.Rifle;
            if (data.kind == Data.EquipmentKind.ProjectileWeapon) return WeaponShape.Launcher;
            if (data.pelletCount > 1) return WeaponShape.Shotgun;
            if (data.adsZoom >= 2f || data.range >= 120f) return WeaponShape.Marksman;
            if (!data.automatic && data.magazineSize <= 15) return WeaponShape.Pistol;
            if (data.automatic && data.range < 60f) return WeaponShape.Smg;
            return WeaponShape.Rifle;
        }

        /// <summary>A stand-in gun for <paramref name="data"/>. <paramref name="accent"/> colours its sights and markings.</summary>
        public static Transform BuildWeapon(Transform parent, Data.EquipmentData data, Color accent)
        {
            var root = Prim.Group(parent, "Weapon (" + (data != null ? data.equipmentName : "none") + ")");
            var metal = ArtMaterials.Colour(Gunmetal);
            var steel = ArtMaterials.Colour(Steel);
            var grip = ArtMaterials.Colour(Polymer);
            var glow = ArtMaterials.Emissive(accent);

            switch (ShapeOf(data))
            {
                case WeaponShape.Pistol:
                    Prim.Box(root, "Slide", new Vector3(0f, 0.06f, 0.07f), new Vector3(0.035f, 0.045f, 0.2f), steel);
                    Prim.Box(root, "Frame", new Vector3(0f, 0.03f, 0.05f), new Vector3(0.03f, 0.03f, 0.15f), metal);
                    Prim.Box(root, "Grip", new Vector3(0f, -0.03f, -0.01f), new Vector3(0.032f, 0.11f, 0.045f), grip, new Vector3(-12f, 0f, 0f));
                    Prim.Box(root, "Sight", new Vector3(0f, 0.088f, 0.15f), new Vector3(0.008f, 0.012f, 0.012f), glow);
                    break;

                case WeaponShape.Smg:
                    Prim.Box(root, "Body", new Vector3(0f, 0.05f, 0.1f), new Vector3(0.05f, 0.08f, 0.34f), metal);
                    Prim.Rod(root, "Suppressor", new Vector3(0f, 0.055f, 0.36f), new Vector3(0.045f, 0.1f, 0.045f), grip, new Vector3(90f, 0f, 0f));
                    Prim.Box(root, "Magazine", new Vector3(0f, -0.06f, 0.1f), new Vector3(0.03f, 0.16f, 0.045f), steel, new Vector3(8f, 0f, 0f));
                    Prim.Box(root, "Grip", new Vector3(0f, -0.04f, -0.02f), new Vector3(0.035f, 0.1f, 0.045f), grip, new Vector3(-14f, 0f, 0f));
                    Prim.Box(root, "Stock", new Vector3(0f, 0.05f, -0.16f), new Vector3(0.03f, 0.05f, 0.2f), steel);
                    Prim.Box(root, "Sight", new Vector3(0f, 0.105f, 0.12f), new Vector3(0.02f, 0.025f, 0.05f), glow);
                    break;

                case WeaponShape.Shotgun:
                    Prim.Box(root, "Receiver", new Vector3(0f, 0.05f, 0.05f), new Vector3(0.055f, 0.085f, 0.3f), metal);
                    Prim.Rod(root, "Barrel", new Vector3(0f, 0.07f, 0.45f), new Vector3(0.04f, 0.26f, 0.04f), steel, new Vector3(90f, 0f, 0f));
                    Prim.Rod(root, "Tube", new Vector3(0f, 0.03f, 0.38f), new Vector3(0.035f, 0.2f, 0.035f), metal, new Vector3(90f, 0f, 0f));
                    Prim.Box(root, "Pump", new Vector3(0f, 0.025f, 0.34f), new Vector3(0.055f, 0.05f, 0.14f), ArtMaterials.Colour(new Color(0.42f, 0.27f, 0.15f)));
                    Prim.Box(root, "Stock", new Vector3(0f, 0.03f, -0.22f), new Vector3(0.045f, 0.09f, 0.26f), ArtMaterials.Colour(new Color(0.42f, 0.27f, 0.15f)), new Vector3(6f, 0f, 0f));
                    Prim.Box(root, "Grip", new Vector3(0f, -0.035f, -0.04f), new Vector3(0.035f, 0.09f, 0.045f), grip, new Vector3(-18f, 0f, 0f));
                    Prim.Box(root, "Bead", new Vector3(0f, 0.115f, 0.68f), new Vector3(0.012f, 0.012f, 0.012f), glow);
                    break;

                case WeaponShape.Marksman:
                    Prim.Box(root, "Receiver", new Vector3(0f, 0.05f, 0.08f), new Vector3(0.05f, 0.08f, 0.4f), metal);
                    Prim.Rod(root, "Barrel", new Vector3(0f, 0.06f, 0.6f), new Vector3(0.028f, 0.34f, 0.028f), steel, new Vector3(90f, 0f, 0f));
                    Prim.Rod(root, "Scope", new Vector3(0f, 0.125f, 0.1f), new Vector3(0.045f, 0.13f, 0.045f), grip, new Vector3(90f, 0f, 0f));
                    Prim.Rod(root, "Lens", new Vector3(0f, 0.125f, 0.235f), new Vector3(0.04f, 0.004f, 0.04f), glow, new Vector3(90f, 0f, 0f));
                    Prim.Box(root, "Magazine", new Vector3(0f, -0.03f, 0.12f), new Vector3(0.03f, 0.09f, 0.07f), steel);
                    Prim.Box(root, "Stock", new Vector3(0f, 0.035f, -0.26f), new Vector3(0.04f, 0.1f, 0.3f), grip);
                    Prim.Box(root, "Grip", new Vector3(0f, -0.04f, -0.05f), new Vector3(0.035f, 0.1f, 0.045f), grip, new Vector3(-16f, 0f, 0f));
                    break;

                case WeaponShape.Launcher:
                    Prim.Rod(root, "Tube", new Vector3(0f, 0.07f, 0.2f), new Vector3(0.1f, 0.28f, 0.1f), metal, new Vector3(90f, 0f, 0f));
                    Prim.Rod(root, "Muzzle", new Vector3(0f, 0.07f, 0.49f), new Vector3(0.115f, 0.02f, 0.115f), ArtMaterials.Colour(Brass), new Vector3(90f, 0f, 0f));
                    Prim.Rod(root, "Drum", new Vector3(0f, 0.02f, 0.08f), new Vector3(0.14f, 0.05f, 0.14f), steel, new Vector3(90f, 0f, 0f));
                    Prim.Box(root, "Grip", new Vector3(0f, -0.05f, -0.03f), new Vector3(0.035f, 0.1f, 0.045f), grip, new Vector3(-14f, 0f, 0f));
                    Prim.Box(root, "Stock", new Vector3(0f, 0.06f, -0.2f), new Vector3(0.04f, 0.07f, 0.24f), grip);
                    Prim.Box(root, "Band", new Vector3(0f, 0.07f, 0.34f), new Vector3(0.105f, 0.105f, 0.03f), glow);
                    break;

                default: // Rifle
                    Prim.Box(root, "Receiver", new Vector3(0f, 0.05f, 0.08f), new Vector3(0.05f, 0.085f, 0.36f), metal);
                    Prim.Rod(root, "Barrel", new Vector3(0f, 0.06f, 0.46f), new Vector3(0.026f, 0.2f, 0.026f), steel, new Vector3(90f, 0f, 0f));
                    Prim.Box(root, "Handguard", new Vector3(0f, 0.05f, 0.34f), new Vector3(0.055f, 0.065f, 0.2f), grip);
                    Prim.Box(root, "Magazine", new Vector3(0f, -0.06f, 0.13f), new Vector3(0.032f, 0.17f, 0.07f), steel, new Vector3(12f, 0f, 0f));
                    Prim.Box(root, "Stock", new Vector3(0f, 0.04f, -0.22f), new Vector3(0.04f, 0.09f, 0.26f), grip);
                    Prim.Box(root, "Grip", new Vector3(0f, -0.04f, -0.04f), new Vector3(0.035f, 0.1f, 0.045f), grip, new Vector3(-16f, 0f, 0f));
                    Prim.Box(root, "Sight", new Vector3(0f, 0.11f, 0.1f), new Vector3(0.022f, 0.03f, 0.06f), glow);
                    break;
            }
            return root;
        }

        /// <summary>Where the muzzle is on a stand-in gun, in its local space (for flashes).</summary>
        public static Vector3 MuzzleOf(WeaponShape shape)
        {
            switch (shape)
            {
                case WeaponShape.Pistol:   return new Vector3(0f, 0.06f, 0.18f);
                case WeaponShape.Smg:      return new Vector3(0f, 0.055f, 0.47f);
                case WeaponShape.Shotgun:  return new Vector3(0f, 0.07f, 0.72f);
                case WeaponShape.Marksman: return new Vector3(0f, 0.06f, 0.95f);
                case WeaponShape.Launcher: return new Vector3(0f, 0.07f, 0.52f);
                default:                   return new Vector3(0f, 0.06f, 0.67f);
            }
        }

        // ------------------------------------------------------------------
        // Things specialists throw and place

        /// <summary>A launched grenade or sticky charge: a banded canister with a light that shows it is live.</summary>
        public static Transform BuildProjectile(Transform parent, bool fused)
        {
            var root = Prim.Group(parent, "Projectile Model");
            Prim.Rod(root, "Body", Vector3.zero, new Vector3(0.11f, 0.09f, 0.11f), ArtMaterials.Colour(new Color(0.2f, 0.26f, 0.18f)), new Vector3(90f, 0f, 0f));
            Prim.Ball(root, "Nose", new Vector3(0f, 0f, 0.09f), new Vector3(0.11f, 0.11f, 0.11f), ArtMaterials.Colour(Brass));
            Prim.Rod(root, "Band", new Vector3(0f, 0f, -0.02f), new Vector3(0.118f, 0.012f, 0.118f), ArtMaterials.Emissive(fused ? Warning : new Color(1f, 0.7f, 0.1f)), new Vector3(90f, 0f, 0f));
            if (fused)
            {
                // Sticky charges get spikes so they read as "this stays where it lands".
                for (int i = 0; i < 4; i++)
                {
                    Prim.Box(root, "Spike", Vector3.zero, new Vector3(0.02f, 0.2f, 0.02f), ArtMaterials.Colour(Steel), new Vector3(0f, 0f, 45f + i * 45f));
                }
            }
            return root;
        }

        /// <summary>The Spider's proximity trap: a low disc with three feet and a sensor light in the owner's crew colour.</summary>
        public static Transform BuildTrap(Transform parent, Color crew, out Renderer light)
        {
            var root = Prim.Group(parent, "Trap Model");
            Prim.Rod(root, "Disc", new Vector3(0f, 0.035f, 0f), new Vector3(0.36f, 0.03f, 0.36f), ArtMaterials.Colour(Polymer));
            Prim.Rod(root, "Ring", new Vector3(0f, 0.05f, 0f), new Vector3(0.3f, 0.02f, 0.3f), ArtMaterials.Colour(crew));
            for (int i = 0; i < 3; i++)
            {
                float a = i * 120f * Mathf.Deg2Rad;
                Prim.Box(root, "Foot", new Vector3(Mathf.Sin(a) * 0.19f, 0.015f, Mathf.Cos(a) * 0.19f), new Vector3(0.06f, 0.03f, 0.1f), ArtMaterials.Colour(Steel), new Vector3(0f, i * 120f, 0f));
            }
            Prim.Rod(root, "Sensor Post", new Vector3(0f, 0.11f, 0f), new Vector3(0.03f, 0.05f, 0.03f), ArtMaterials.Colour(Steel));
            light = Prim.Ball(root, "Sensor", new Vector3(0f, 0.18f, 0f), new Vector3(0.07f, 0.07f, 0.07f), ArtMaterials.Emissive(Warning)).GetComponent<Renderer>();
            return root;
        }

        /// <summary>The Smith's placed charge: a taped block with a detonator and a light.</summary>
        public static Transform BuildCharge(Transform parent, Color crew, out Renderer light)
        {
            var root = Prim.Group(parent, "Charge Model");
            var block = ArtMaterials.Colour(new Color(0.75f, 0.7f, 0.55f));
            Prim.Box(root, "Block A", new Vector3(-0.07f, 0.06f, 0f), new Vector3(0.12f, 0.12f, 0.26f), block);
            Prim.Box(root, "Block B", new Vector3(0.07f, 0.06f, 0f), new Vector3(0.12f, 0.12f, 0.26f), block);
            Prim.Box(root, "Tape", new Vector3(0f, 0.06f, 0.06f), new Vector3(0.28f, 0.13f, 0.04f), ArtMaterials.Colour(crew));
            Prim.Box(root, "Tape", new Vector3(0f, 0.06f, -0.06f), new Vector3(0.28f, 0.13f, 0.04f), ArtMaterials.Colour(crew));
            Prim.Box(root, "Detonator", new Vector3(0f, 0.14f, 0f), new Vector3(0.1f, 0.04f, 0.08f), ArtMaterials.Colour(Polymer));
            Prim.Rod(root, "Aerial", new Vector3(0.03f, 0.22f, 0f), new Vector3(0.008f, 0.07f, 0.008f), ArtMaterials.Colour(Steel));
            light = Prim.Ball(root, "Light", new Vector3(-0.02f, 0.17f, 0f), new Vector3(0.035f, 0.035f, 0.035f), ArtMaterials.Emissive(Warning)).GetComponent<Renderer>();
            return root;
        }

        /// <summary>The Shield's recon drone: a small body, four arms with rotors and a scanning eye.</summary>
        public static Transform BuildDrone(Transform parent, Color crew, out Transform[] rotors)
        {
            var root = Prim.Group(parent, "Drone Model");
            Prim.Box(root, "Body", Vector3.zero, new Vector3(0.22f, 0.08f, 0.3f), ArtMaterials.Colour(Polymer));
            Prim.Box(root, "Stripe", new Vector3(0f, 0.045f, 0f), new Vector3(0.1f, 0.01f, 0.3f), ArtMaterials.Colour(crew));
            Prim.Ball(root, "Eye", new Vector3(0f, -0.05f, 0.1f), new Vector3(0.09f, 0.09f, 0.09f), ArtMaterials.Emissive(new Color(1f, 0.2f, 0.9f)));
            rotors = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                float x = (i % 2 == 0 ? -1f : 1f) * 0.2f, z = (i < 2 ? -1f : 1f) * 0.2f;
                Prim.Box(root, "Arm", new Vector3(x * 0.5f, 0f, z * 0.5f), new Vector3(0.03f, 0.02f, 0.3f), ArtMaterials.Colour(Steel), new Vector3(0f, Mathf.Atan2(x, z) * Mathf.Rad2Deg, 0f));
                var rotor = Prim.Group(root, "Rotor", new Vector3(x, 0.04f, z));
                Prim.Box(rotor, "Blade", Vector3.zero, new Vector3(0.26f, 0.006f, 0.03f), ArtMaterials.Colour(Steel));
                rotors[i] = rotor;
            }
            return root;
        }

        /// <summary>
        /// (v0.14) A security camera: a bracket, a housing and a lens that glows in <paramref name="lens"/>.
        /// Built looking along +Z, to match the object it is attached to.
        /// </summary>
        public static Transform BuildCamera(Transform parent, out Renderer lens)
        {
            var root = Prim.Group(parent, "Camera Model");
            Vector3 s = parent.lossyScale;
            root.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));
            var shell = ArtMaterials.Colour(new Color(0.86f, 0.87f, 0.9f));
            Prim.Box(root, "Bracket", new Vector3(0f, 0.22f, -0.2f), new Vector3(0.08f, 0.36f, 0.08f), ArtMaterials.Colour(Gunmetal));
            Prim.Box(root, "Mount", new Vector3(0f, 0.4f, -0.2f), new Vector3(0.3f, 0.06f, 0.3f), ArtMaterials.Colour(Gunmetal));
            Prim.Box(root, "Housing", new Vector3(0f, 0f, 0f), new Vector3(0.24f, 0.22f, 0.6f), shell);
            Prim.Box(root, "Hood", new Vector3(0f, 0.13f, 0.08f), new Vector3(0.28f, 0.03f, 0.7f), shell);
            Prim.Rod(root, "Lens Ring", new Vector3(0f, 0f, 0.31f), new Vector3(0.17f, 0.02f, 0.17f), ArtMaterials.Colour(Polymer), new Vector3(90f, 0f, 0f));
            lens = Prim.Rod(root, "Lens", new Vector3(0f, 0f, 0.335f), new Vector3(0.11f, 0.01f, 0.11f), ArtMaterials.Emissive(new Color(0.2f, 0.9f, 1f)), new Vector3(90f, 0f, 0f)).GetComponent<Renderer>();
            return root;
        }

        /// <summary>
        /// (v0.17) A sentry turret big enough to notice: a tripod foot with a hazard ring, a pedestal, and a
        /// twin-barrelled head with a sensor eye and a status beacon on a mast, about 2.6 m to the top.
        /// The foot is built under <paramref name="owner"/> at <paramref name="floorY"/>; the gun is built
        /// under <paramref name="head"/>, which the turret turns to face its target, with its barrels at
        /// <paramref name="muzzleY"/> so shots leave from where the turret really sees and fires from.
        /// </summary>
        // [v0.17 first draft] public static void BuildTurret(Transform owner, Transform head, float floorY, out Renderer beacon, out Renderer eye)
        public static void BuildTurret(Transform owner, Transform head, float floorY, float muzzleY, out Renderer beacon, out Renderer eye)
        {
            var metal = ArtMaterials.Colour(Gunmetal);
            var dark = ArtMaterials.Colour(Polymer);
            var hazard = ArtMaterials.Colour(new Color(0.95f, 0.75f, 0.1f));

            var root = Prim.Group(owner, "Turret Model");
            Vector3 s = owner.lossyScale;
            root.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));
            root.position = new Vector3(owner.position.x, floorY, owner.position.z);

            Prim.Rod(root, "Foot", new Vector3(0f, 0.06f, 0f), new Vector3(1.5f, 0.06f, 1.5f), metal);
            Prim.Rod(root, "Hazard Ring", new Vector3(0f, 0.14f, 0f), new Vector3(1.56f, 0.02f, 1.56f), hazard);
            for (int leg = 0; leg < 3; leg++)
            {
                var turn = Quaternion.Euler(0f, leg * 120f, 0f);
                Prim.Box(root, "Leg", turn * new Vector3(0f, 0.22f, 0.45f), new Vector3(0.24f, 0.16f, 0.95f), dark, new Vector3(0f, leg * 120f, 0f));
            }
            // [v0.17 first draft] a 1 m pedestal, which left the barrels 0.6 m below the point shots came from
            // Prim.Rod(root, "Pedestal", new Vector3(0f, 0.6f, 0f), new Vector3(0.5f, 0.42f, 0.5f), metal);
            // Prim.Rod(root, "Collar", new Vector3(0f, 1.04f, 0f), new Vector3(0.74f, 0.05f, 0.74f), hazard);
            float neck = Mathf.Max(0.5f, muzzleY - floorY - 0.3f); // the pedestal reaches up to just under the gun
            Prim.Rod(root, "Pedestal", new Vector3(0f, 0.18f + (neck - 0.18f) * 0.5f, 0f), new Vector3(0.5f, (neck - 0.18f) * 0.5f, 0.5f), metal);
            Prim.Rod(root, "Collar", new Vector3(0f, neck, 0f), new Vector3(0.74f, 0.05f, 0.74f), hazard);

            var gun = Prim.Group(head, "Turret Head");
            Vector3 hs = head.lossyScale;
            gun.localScale = new Vector3(1f / Mathf.Max(0.01f, hs.x), 1f / Mathf.Max(0.01f, hs.y), 1f / Mathf.Max(0.01f, hs.z));
            gun.position = new Vector3(head.position.x, muzzleY + 0.03f, head.position.z); // the barrels sit 3 cm below the gun's centre
            Prim.Box(gun, "Body", Vector3.zero, new Vector3(0.78f, 0.5f, 0.95f), metal);
            Prim.Rod(gun, "Drum", new Vector3(0f, -0.04f, -0.56f), new Vector3(0.5f, 0.22f, 0.5f), dark, new Vector3(0f, 0f, 90f));
            foreach (float side in new[] { -1f, 1f })
            {
                Prim.Box(gun, "Cheek", new Vector3(side * 0.42f, 0f, 0.05f), new Vector3(0.08f, 0.36f, 0.7f), hazard);
                Prim.Rod(gun, "Barrel", new Vector3(side * 0.19f, -0.03f, 0.86f), new Vector3(0.13f, 0.42f, 0.13f), dark, new Vector3(90f, 0f, 0f));
                Prim.Rod(gun, "Muzzle", new Vector3(side * 0.19f, -0.03f, 1.28f), new Vector3(0.18f, 0.05f, 0.18f), metal, new Vector3(90f, 0f, 0f));
            }
            var glow = ArtMaterials.Emissive(new Color(0.9f, 0.75f, 0.2f));
            eye = Prim.Ball(gun, "Eye", new Vector3(0f, 0.1f, 0.48f), new Vector3(0.24f, 0.24f, 0.12f), glow).GetComponent<Renderer>();
            Prim.Rod(gun, "Mast", new Vector3(0f, 0.42f, -0.25f), new Vector3(0.06f, 0.17f, 0.06f), dark);
            beacon = Prim.Ball(gun, "Beacon", new Vector3(0f, 0.7f, -0.25f), new Vector3(0.3f, 0.3f, 0.3f), glow).GetComponent<Renderer>();
        }

        // ------------------------------------------------------------------
        // Passive gear, worn on the character

        /// <summary>
        /// Adds a visible piece to <paramref name="model"/> for a passive item. Chosen from what the item does:
        /// defence is a plate, speed is an injector rig, damage is an ammunition belt. Returns the parts' parent.
        /// </summary>
        public static Transform BuildWornGear(CharacterModel model, Data.EquipmentData data, Color crew)
        {
            if (model == null || model.Torso == null || data == null) return null;
            var root = Prim.Group(model.Torso, "Gear (" + data.equipmentName + ")");

            if (data.kind == Data.EquipmentKind.Gadget)
            {
                // (v0.14) Gadgets hang on the left hip, each with its own shape and light, so a crew can read
                // at a glance who is carrying the breaching kit and who has the key.
                var body = ArtMaterials.Colour(Polymer);
                var hip = new Vector3(-0.24f, -0.26f, 0.02f);
                switch (data.gadget)
                {
                    case Data.GadgetType.BreachingKit:
                        Track(model, Prim.Box(root, "Charge", hip, new Vector3(0.12f, 0.2f, 0.26f), ArtMaterials.Colour(new Color(0.75f, 0.7f, 0.55f))));
                        Track(model, Prim.Box(root, "Tape", hip, new Vector3(0.125f, 0.05f, 0.27f), ArtMaterials.Colour(crew)));
                        Track(model, Prim.Ball(root, "Light", hip + new Vector3(-0.07f, 0.06f, 0f), new Vector3(0.04f, 0.04f, 0.04f), ArtMaterials.Emissive(Warning)));
                        break;
                    case Data.GadgetType.EmpEmitter:
                        Track(model, Prim.Rod(root, "Emitter", hip, new Vector3(0.14f, 0.09f, 0.14f), body));
                        Track(model, Prim.Rod(root, "Coil", hip + new Vector3(0f, 0.1f, 0f), new Vector3(0.1f, 0.02f, 0.1f), ArtMaterials.Emissive(new Color(0.35f, 0.75f, 1f))));
                        Track(model, Prim.Rod(root, "Aerial", hip + new Vector3(0f, 0.2f, 0f), new Vector3(0.012f, 0.1f, 0.012f), ArtMaterials.Colour(Steel)));
                        break;
                    case Data.GadgetType.BypassKey:
                        Track(model, Prim.Box(root, "Reader", hip, new Vector3(0.05f, 0.18f, 0.12f), body));
                        Track(model, Prim.Box(root, "Card", hip + new Vector3(-0.03f, 0.02f, 0f), new Vector3(0.01f, 0.1f, 0.07f), ArtMaterials.Emissive(new Color(0.3f, 1f, 0.5f))));
                        break;
                    case Data.GadgetType.LootScanner:
                        Track(model, Prim.Box(root, "Scanner", hip, new Vector3(0.06f, 0.2f, 0.13f), body));
                        Track(model, Prim.Box(root, "Screen", hip + new Vector3(-0.032f, 0.03f, 0f), new Vector3(0.005f, 0.1f, 0.1f), ArtMaterials.Emissive(new Color(0.3f, 0.95f, 1f))));
                        Track(model, Prim.Rod(root, "Dish", hip + new Vector3(0f, 0.14f, 0f), new Vector3(0.09f, 0.01f, 0.09f), ArtMaterials.Colour(Steel)));
                        break;
                    case Data.GadgetType.GrappleLine:
                        Track(model, Prim.Rod(root, "Coil", hip, new Vector3(0.2f, 0.035f, 0.2f), ArtMaterials.Colour(new Color(0.3f, 0.3f, 0.32f)), new Vector3(0f, 0f, 90f)));
                        Track(model, Prim.Box(root, "Hook", hip + new Vector3(-0.05f, 0.14f, 0f), new Vector3(0.03f, 0.12f, 0.1f), ArtMaterials.Colour(Steel)));
                        break;
                }
            }
            else if (data.defenseModifier > 0f)
            {
                var plate = ArtMaterials.Colour(new Color(0.3f, 0.32f, 0.36f));
                Track(model, Prim.Box(root, "Chest Plate", new Vector3(0f, 0.08f, 0.17f), new Vector3(0.36f, 0.34f, 0.05f), plate));
                Track(model, Prim.Box(root, "Back Plate", new Vector3(0f, 0.08f, -0.17f), new Vector3(0.34f, 0.32f, 0.05f), plate));
                Track(model, Prim.Box(root, "Plate Mark", new Vector3(0f, 0.16f, 0.2f), new Vector3(0.12f, 0.04f, 0.01f), ArtMaterials.Colour(crew)));
            }
            else if (data.speedModifier > 0f)
            {
                var rig = ArtMaterials.Colour(Polymer);
                Track(model, Prim.Box(root, "Rig Strap", new Vector3(0f, -0.2f, 0f), new Vector3(0.44f, 0.05f, 0.3f), rig));
                Track(model, Prim.Rod(root, "Injector", new Vector3(0.2f, -0.2f, 0.1f), new Vector3(0.035f, 0.07f, 0.035f), ArtMaterials.Emissive(new Color(0.3f, 1f, 0.5f))));
                Track(model, Prim.Rod(root, "Injector", new Vector3(0.2f, -0.2f, 0.02f), new Vector3(0.035f, 0.07f, 0.035f), ArtMaterials.Emissive(new Color(0.3f, 1f, 0.5f))));
                Track(model, Prim.Rod(root, "Line", new Vector3(0.14f, 0.05f, 0.14f), new Vector3(0.012f, 0.24f, 0.012f), ArtMaterials.Emissive(new Color(0.3f, 1f, 0.5f)), new Vector3(0f, 0f, 14f)));
            }
            else if (data.damageModifier > 0f)
            {
                var belt = ArtMaterials.Colour(new Color(0.3f, 0.2f, 0.12f));
                Track(model, Prim.Box(root, "Bandolier", new Vector3(0f, 0.04f, 0.155f), new Vector3(0.09f, 0.62f, 0.03f), belt, new Vector3(0f, 0f, -38f)));
                for (int i = 0; i < 6; i++)
                {
                    float t = (i - 2.5f) * 0.085f;
                    Track(model, Prim.Rod(root, "Round", new Vector3(-t * 0.62f, 0.04f + t * 0.79f, 0.18f), new Vector3(0.022f, 0.035f, 0.022f), ArtMaterials.Colour(Brass), new Vector3(0f, 0f, -38f)));
                }
            }
            else
            {
                Track(model, Prim.Box(root, "Pouch", new Vector3(-0.2f, -0.24f, 0.08f), new Vector3(0.1f, 0.14f, 0.12f), ArtMaterials.Colour(Polymer)));
            }
            return root;
        }

        /// <summary>Registers a part with the model so status tints and stealth treat it like the rest of the body.</summary>
        private static void Track(CharacterModel model, GameObject part)
        {
            var renderer = part.GetComponent<Renderer>();
            if (renderer == null) return;
            model.Renderers.Add(renderer);
            model.BaseColours.Add(renderer.sharedMaterial != null ? renderer.sharedMaterial.color : Color.white);
        }

        /// <summary>Removes worn gear and forgets its renderers.</summary>
        public static void RemoveWornGear(CharacterModel model, Transform gearRoot)
        {
            if (gearRoot == null) return;
            if (model != null)
            {
                foreach (var renderer in gearRoot.GetComponentsInChildren<Renderer>(true))
                {
                    int index = model.Renderers.IndexOf(renderer);
                    if (index < 0) continue;
                    model.Renderers.RemoveAt(index);
                    model.BaseColours.RemoveAt(index);
                }
            }
            Object.Destroy(gearRoot.gameObject);
        }
    }
}
