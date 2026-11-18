using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>A code-built character: its parts, and the joints <see cref="CharacterAnimator"/> swings.</summary>
    public sealed class CharacterModel
    {
        public Transform Root;
        public Transform Torso, Head, ArmL, ArmR, LegL, LegR, HandR;
        public readonly List<Renderer> Renderers = new List<Renderer>();
        public readonly List<Color> BaseColours = new List<Color>();
    }

    /// <summary>
    /// Builds characters out of primitives (v0.12, milestone M4): the four specialists and three kinds of guard.
    /// They are stand-ins for real character art, but they do the two jobs that matter in a blockout. Each
    /// specialist has its own silhouette, so you can tell a Cipher from a Smith at fifty metres, and each wears
    /// its crew's pattern, so you can tell whose side they are on.
    ///
    /// Nothing here has colliders: these are visuals only. Feet are at local y = 0 of the returned root.
    /// </summary>
    public static class CharacterBuilder
    {
        private static readonly Color Suit = new Color(0.13f, 0.14f, 0.17f);
        private static readonly Color SuitLight = new Color(0.24f, 0.25f, 0.29f);
        private static readonly Color GuardArmour = new Color(0.22f, 0.23f, 0.27f);
        private static readonly Color GuardTrim = new Color(0.95f, 0.55f, 0.1f);

        // ------------------------------------------------------------------
        // Specialists

        public static CharacterModel BuildSpecialist(Transform parent, Core.PlayerClassType classType, Core.TeamID team)
        {
            var style = CrewStyle.Get(team);
            var cloth = ArtMaterials.CrewCloth(team);
            var suit = ArtMaterials.Colour(Suit);
            var plate = ArtMaterials.Colour(style.Primary);
            var trim = ArtMaterials.Colour(style.Accent);

            // Proportions are the silhouette. Height is fixed (it matches the hitboxes and the camera).
            float shoulder = 0.50f, torsoDepth = 0.26f, armLength = 0.62f, armThick = 0.13f, legThick = 0.19f, headSize = 0.27f;
            switch (classType)
            {
                case Core.PlayerClassType.Hacker:      shoulder = 0.42f; armThick = 0.11f; legThick = 0.16f; break;                    // slight
                case Core.PlayerClassType.Saboteur:    shoulder = 0.46f; armLength = 0.76f; armThick = 0.11f; headSize = 0.25f; break; // long-limbed
                case Core.PlayerClassType.Demolitions: shoulder = 0.66f; torsoDepth = 0.34f; armThick = 0.18f; legThick = 0.24f; break; // heavy
                case Core.PlayerClassType.Agent:       shoulder = 0.54f; armThick = 0.15f; legThick = 0.20f; break;                    // upright
            }

            var model = Skeleton(parent, "Specialist", shoulder, torsoDepth, armLength, armThick, legThick, headSize, suit, ArtMaterials.Colour(SuitLight), 0f);

            // Every crew member wears the pattern as a waist wrap; the rest of the cloth depends on the class.
            Box(model, model.Torso, "Wrap", new Vector3(0f, -0.26f, 0f), new Vector3(shoulder + 0.04f, 0.2f, torsoDepth + 0.05f), cloth);

            switch (classType)
            {
                case Core.PlayerClassType.Hacker:
                    // The Cipher: a lit visor, a deck on the back, an antenna. Reads as "the one with the gear".
                    Box(model, model.Head, "Visor", new Vector3(0f, 0.02f, headSize * 0.42f), new Vector3(headSize * 0.95f, 0.07f, 0.08f), ArtMaterials.Emissive(new Color(0.3f, 0.9f, 1f)));
                    Box(model, model.Torso, "Deck", new Vector3(0f, 0.05f, -torsoDepth * 0.5f - 0.07f), new Vector3(0.3f, 0.36f, 0.12f), plate);
                    Cylinder(model, model.Torso, "Antenna", new Vector3(0.12f, 0.5f, -torsoDepth * 0.5f - 0.07f), new Vector3(0.02f, 0.28f, 0.02f), trim);
                    Box(model, model.Torso, "Sash", new Vector3(0f, 0.05f, torsoDepth * 0.5f + 0.01f), new Vector3(0.14f, 0.5f, 0.02f), cloth, new Vector3(0f, 0f, 24f));
                    break;

                case Core.PlayerClassType.Saboteur:
                    // The Spider: hooded, hunched, two bright eyes. Reads as "the one you did not see".
                    model.Torso.localRotation = Quaternion.Euler(14f, 0f, 0f);
                    Sphere(model, model.Head, "Hood", new Vector3(0f, 0.03f, -0.03f), Vector3.one * (headSize * 1.28f), ArtMaterials.Colour(style.Dark));
                    Sphere(model, model.Head, "Eye", new Vector3(-0.06f, 0.02f, headSize * 0.5f), Vector3.one * 0.05f, ArtMaterials.Emissive(style.Accent));
                    Sphere(model, model.Head, "Eye", new Vector3(0.06f, 0.02f, headSize * 0.5f), Vector3.one * 0.05f, ArtMaterials.Emissive(style.Accent));
                    Box(model, model.Torso, "Scarf", new Vector3(0f, 0.3f, 0.02f), new Vector3(shoulder * 0.9f, 0.12f, torsoDepth + 0.1f), cloth);
                    Box(model, model.Torso, "Pouch", new Vector3(shoulder * 0.5f, -0.3f, 0.05f), new Vector3(0.1f, 0.16f, 0.14f), plate);
                    Box(model, model.Torso, "Pouch", new Vector3(-shoulder * 0.5f, -0.3f, 0.05f), new Vector3(0.1f, 0.16f, 0.14f), plate);
                    break;

                case Core.PlayerClassType.Demolitions:
                    // The Smith: wide shoulders, a pack, a launcher tube. Reads as "the big one".
                    Box(model, model.Torso, "Pauldron", new Vector3(shoulder * 0.5f + 0.04f, 0.3f, 0f), new Vector3(0.26f, 0.16f, torsoDepth + 0.08f), plate);
                    Box(model, model.Torso, "Pauldron", new Vector3(-shoulder * 0.5f - 0.04f, 0.3f, 0f), new Vector3(0.26f, 0.16f, torsoDepth + 0.08f), plate);
                    Box(model, model.Torso, "Pack", new Vector3(0f, 0.05f, -torsoDepth * 0.5f - 0.14f), new Vector3(0.46f, 0.5f, 0.26f), ArtMaterials.Colour(SuitLight));
                    Cylinder(model, model.Torso, "Tube", new Vector3(0.2f, 0.25f, -torsoDepth * 0.5f - 0.16f), new Vector3(0.11f, 0.4f, 0.11f), trim, new Vector3(0f, 0f, 20f));
                    Box(model, model.Torso, "Apron", new Vector3(0f, -0.45f, torsoDepth * 0.5f + 0.02f), new Vector3(0.36f, 0.4f, 0.02f), cloth);
                    break;

                case Core.PlayerClassType.Agent:
                    // The Shield: a plate on the left arm, a crest, a short cape. Reads as "the one in front".
                    Box(model, model.ArmL, "Shield", new Vector3(-0.1f, -armLength * 0.45f, 0.05f), new Vector3(0.06f, 0.62f, 0.44f), plate);
                    Box(model, model.ArmL, "ShieldBoss", new Vector3(-0.14f, -armLength * 0.45f, 0.05f), new Vector3(0.04f, 0.18f, 0.18f), trim);
                    Box(model, model.Head, "Crest", new Vector3(0f, headSize * 0.55f, -0.02f), new Vector3(0.05f, 0.14f, headSize * 0.9f), trim);
                    Box(model, model.Torso, "Cape", new Vector3(0f, -0.1f, -torsoDepth * 0.5f - 0.03f), new Vector3(shoulder * 0.95f, 0.75f, 0.03f), cloth, new Vector3(-8f, 0f, 0f));
                    break;
            }
            return model;
        }

        // ------------------------------------------------------------------
        // Guards (facility security: grey armour, orange trim; no crew pattern)

        /// <summary><paramref name="feetY"/> is the local height of the feet (-1 for the capsule-primitive guard prefabs).</summary>
        public static CharacterModel BuildGuard(Transform parent, AI.AIAgentType type, float feetY)
        {
            bool elite = type == AI.AIAgentType.Elite, sniper = type == AI.AIAgentType.Sniper;
            var armour = ArtMaterials.Colour(elite ? new Color(0.3f, 0.12f, 0.12f) : sniper ? new Color(0.14f, 0.17f, 0.3f) : GuardArmour);
            var under = ArtMaterials.Colour(new Color(0.08f, 0.08f, 0.1f));
            var trim = ArtMaterials.Colour(GuardTrim);

            float shoulder = elite ? 0.62f : sniper ? 0.44f : 0.54f;
            var model = Skeleton(parent, "Guard", shoulder, elite ? 0.32f : 0.28f, 0.62f, elite ? 0.17f : 0.14f, elite ? 0.22f : 0.19f, 0.26f, armour, under, feetY);

            // Helmet with a visor slit: guards have no faces.
            Box(model, model.Head, "Helmet", new Vector3(0f, 0.04f, 0f), new Vector3(0.3f, 0.26f, 0.32f), armour);
            Box(model, model.Head, "VisorSlit", new Vector3(0f, 0.02f, 0.16f), new Vector3(0.22f, 0.05f, 0.03f), ArtMaterials.Emissive(elite ? new Color(1f, 0.2f, 0.15f) : new Color(1f, 0.6f, 0.15f)));
            Box(model, model.Torso, "ChestPlate", new Vector3(0f, 0.08f, 0.15f), new Vector3(shoulder * 0.8f, 0.36f, 0.06f), armour);
            Box(model, model.Torso, "HazardStripe", new Vector3(0f, -0.14f, 0.16f), new Vector3(shoulder * 0.8f, 0.05f, 0.05f), trim);
            Box(model, model.Torso, "Belt", new Vector3(0f, -0.28f, 0f), new Vector3(shoulder * 0.92f, 0.08f, 0.32f), under);

            // A rifle in the right hand; the sniper's is long, with a lit scope.
            float rifle = sniper ? 1.25f : elite ? 0.85f : 0.7f;
            Box(model, model.HandR, "Rifle", new Vector3(0f, 0f, rifle * 0.35f), new Vector3(0.06f, 0.1f, rifle), under);
            if (sniper) Cylinder(model, model.HandR, "Scope", new Vector3(0f, 0.09f, rifle * 0.3f), new Vector3(0.05f, 0.12f, 0.05f), ArtMaterials.Emissive(new Color(0.4f, 0.7f, 1f)), new Vector3(90f, 0f, 0f));
            if (elite)
            {
                Box(model, model.Torso, "Pauldron", new Vector3(shoulder * 0.5f + 0.03f, 0.3f, 0f), new Vector3(0.22f, 0.14f, 0.36f), armour);
                Box(model, model.Torso, "Pauldron", new Vector3(-shoulder * 0.5f - 0.03f, 0.3f, 0f), new Vector3(0.22f, 0.14f, 0.36f), armour);
            }
            return model;
        }

        // ------------------------------------------------------------------
        // Shared body

        /// <summary>Legs, torso, head and arms on joints. About 1.85 m tall, feet at <paramref name="feetY"/>.</summary>
        private static CharacterModel Skeleton(Transform parent, string name, float shoulder, float torsoDepth, float armLength, float armThick,
            float legThick, float headSize, Material body, Material limbs, float feetY)
        {
            var model = new CharacterModel();
            model.Root = new GameObject(name + " Model").transform;
            model.Root.SetParent(parent, false);
            model.Root.localPosition = new Vector3(0f, feetY, 0f);

            const float hip = 0.9f, torsoHeight = 0.62f;
            float torsoCentre = hip + torsoHeight * 0.5f;

            model.Torso = Joint(model.Root, "Torso", new Vector3(0f, torsoCentre, 0f));
            Box(model, model.Torso, "Chest", Vector3.zero, new Vector3(shoulder, torsoHeight, torsoDepth), body);

            model.Head = Joint(model.Torso, "Head", new Vector3(0f, torsoHeight * 0.5f + headSize * 0.55f, 0f));
            Sphere(model, model.Head, "Skull", Vector3.zero, Vector3.one * headSize, limbs);

            float hipSpread = Mathf.Max(0.11f, shoulder * 0.24f);
            model.LegL = Joint(model.Root, "LegL", new Vector3(-hipSpread, hip, 0f));
            model.LegR = Joint(model.Root, "LegR", new Vector3(hipSpread, hip, 0f));
            Box(model, model.LegL, "Leg", new Vector3(0f, -hip * 0.5f, 0f), new Vector3(legThick, hip, legThick * 1.1f), limbs);
            Box(model, model.LegR, "Leg", new Vector3(0f, -hip * 0.5f, 0f), new Vector3(legThick, hip, legThick * 1.1f), limbs);
            Box(model, model.LegL, "Boot", new Vector3(0f, -hip + 0.06f, 0.05f), new Vector3(legThick * 1.15f, 0.12f, legThick * 1.7f), body);
            Box(model, model.LegR, "Boot", new Vector3(0f, -hip + 0.06f, 0.05f), new Vector3(legThick * 1.15f, 0.12f, legThick * 1.7f), body);

            float shoulderY = torsoHeight * 0.5f - 0.08f;
            model.ArmL = Joint(model.Torso, "ArmL", new Vector3(-shoulder * 0.5f - armThick * 0.5f, shoulderY, 0f));
            model.ArmR = Joint(model.Torso, "ArmR", new Vector3(shoulder * 0.5f + armThick * 0.5f, shoulderY, 0f));
            Box(model, model.ArmL, "Arm", new Vector3(0f, -armLength * 0.5f, 0f), new Vector3(armThick, armLength, armThick), limbs);
            Box(model, model.ArmR, "Arm", new Vector3(0f, -armLength * 0.5f, 0f), new Vector3(armThick, armLength, armThick), limbs);
            model.HandR = Joint(model.ArmR, "HandR", new Vector3(0f, -armLength, 0.02f));
            // The hand joint points along the forearm when the arm is raised to aim, so a held weapon faces forward.
            model.HandR.localRotation = Quaternion.Euler(90f, 0f, 0f);
            return model;
        }

        private static Transform Joint(Transform parent, string name, Vector3 localPosition)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        public static GameObject Box(CharacterModel model, Transform parent, string name, Vector3 pos, Vector3 size, Material material, Vector3 euler = default)
            => Part(model, PrimitiveType.Cube, parent, name, pos, size, material, euler);

        public static GameObject Sphere(CharacterModel model, Transform parent, string name, Vector3 pos, Vector3 size, Material material)
            => Part(model, PrimitiveType.Sphere, parent, name, pos, size, material, default);

        /// <summary>Unity's cylinder is 2 units tall: <paramref name="size"/>.y is the half-height.</summary>
        public static GameObject Cylinder(CharacterModel model, Transform parent, string name, Vector3 pos, Vector3 size, Material material, Vector3 euler = default)
            => Part(model, PrimitiveType.Cylinder, parent, name, pos, size, material, euler);

        private static GameObject Part(CharacterModel model, PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 size, Material material, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider); // visuals must never block movement, shots or line of sight
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            var renderer = go.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            if (model != null)
            {
                model.Renderers.Add(renderer);
                model.BaseColours.Add(material != null ? material.color : Color.white);
            }
            return go;
        }
    }

    /// <summary>
    /// Walk cycle for a <see cref="CharacterModel"/> without an animation rig (v0.12): legs and the free arm
    /// swing with ground speed, the body bobs and leans, and the right arm stays raised when a weapon is held.
    /// Speed comes from how far the object actually moved, so it works the same for the local player, remote
    /// players and guards.
    /// </summary>
    public class CharacterAnimator : MonoBehaviour
    {
        public CharacterModel Model;
        public bool AimRightArm = true;

        private Vector3 lastPosition;
        private float phase, smoothedSpeed;
        private Vector3 torsoRest;
        private Quaternion torsoRestRotation;
        private bool ready;

        private void LateUpdate()
        {
            if (Model == null || Model.Root == null) return;
            if (!ready)
            {
                ready = true;
                lastPosition = transform.position;
                torsoRest = Model.Torso.localPosition;
                torsoRestRotation = Model.Torso.localRotation;
                return;
            }

            float dt = Mathf.Max(0.0001f, Time.deltaTime);
            Vector3 delta = transform.position - lastPosition;
            lastPosition = transform.position;
            delta.y = 0f;
            float speed = delta.magnitude / dt;
            if (speed > 30f) speed = 0f; // teleport / respawn, not a step
            smoothedSpeed = Mathf.Lerp(smoothedSpeed, speed, 1f - Mathf.Exp(-10f * dt));

            float stride = Mathf.Clamp01(smoothedSpeed / 6f);
            phase += smoothedSpeed * dt * 2.4f;
            float swing = Mathf.Sin(phase) * 38f * stride;

            Model.LegL.localRotation = Quaternion.Euler(swing, 0f, 0f);
            Model.LegR.localRotation = Quaternion.Euler(-swing, 0f, 0f);
            Model.ArmL.localRotation = Quaternion.Euler(-swing * 0.8f, 0f, 0f);
            Model.ArmR.localRotation = AimRightArm
                ? Quaternion.Euler(-82f + Mathf.Sin(phase * 2f) * 3f * stride, 0f, 0f)
                : Quaternion.Euler(swing * 0.8f, 0f, 0f);

            // Bob twice per stride, lean into the run, breathe when standing.
            float bob = Mathf.Abs(Mathf.Sin(phase)) * 0.05f * stride + Mathf.Sin(Time.time * 1.6f) * 0.006f;
            Model.Torso.localPosition = torsoRest + Vector3.up * bob;
            Model.Torso.localRotation = torsoRestRotation * Quaternion.Euler(8f * stride, 0f, 0f);
        }
    }
}
