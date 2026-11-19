using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ouroboros.EditorTools
{
    /// <summary>
    /// Environment pieces and the wider Freeport (v0.13). Everything is modelled in code from boxes, cylinders
    /// and spheres: crates, pallets, barrels, shelving, server cabinets, planters with trees, lamp posts, vans,
    /// a forklift, market stalls, a fountain, a gantry crane, a warehouse and a vehicle canopy.
    ///
    /// The placements below were generated from a layout script that tests every solid piece against the
    /// vaults, ramps, cover, devices, spawns, patrol points and exits already on the map, so nothing overlaps.
    /// No new vaults are added: the new areas are routes and cover, so no crew gains loot by where it starts.
    ///
    /// Solid parts are static and are carved out of the NavMesh. Decorative parts have no collider and sit on
    /// the Ignore Raycast layer, which the NavMesh bake skips.
    ///
    /// Runs as part of "Create Freeport Map". On an existing scene: Ouroboros > Setup > Build Out Freeport (M5).
    /// Skipped when an object called "Freeport Props" exists.
    /// </summary>
    public static partial class OuroborosDevSceneBuilder
    {
        private const string PropsName = "Freeport Props";
        private const string MaterialDir = ArtDir + "/Materials";
        private const int DecorLayer = 2; // Ignore Raycast

        private static readonly Color Wood = new Color(0.47f, 0.34f, 0.2f);
        private static readonly Color WoodDark = new Color(0.3f, 0.21f, 0.13f);
        private static readonly Color Metal = new Color(0.32f, 0.34f, 0.38f);
        private static readonly Color MetalDark = new Color(0.14f, 0.15f, 0.17f);
        private static readonly Color Hazard = new Color(0.95f, 0.78f, 0.1f);
        private static readonly Color Leaf = new Color(0.2f, 0.42f, 0.2f);
        private static readonly Color Glass = new Color(0.55f, 0.8f, 0.9f, 0.28f);

        [MenuItem("Ouroboros/Setup/Build Out Freeport (M5)")]
        public static void BuildOutFreeportMenu()
        {
            var level = GameObject.Find("Level");
            if (level == null || GameObject.Find("Gallery (North)") == null)
            {
                EditorUtility.DisplayDialog("Build Out Freeport", "Open the Freeport scene first (Ouroboros > Setup > Create Freeport Map).", "OK");
                return;
            }
            if (GameObject.Find(PropsName) != null)
            {
                EditorUtility.DisplayDialog("Build Out Freeport", "This scene already has '" + PropsName + "'. Delete that object to build it again.", "OK");
                return;
            }
            EnsureFolders();
            BuildFreeportExpansion(level.transform);
            TryBakeNavMesh();
            EditorSceneManager.MarkSceneDirty(level.scene);
            AssetDatabase.SaveAssets();
            Debug.Log("[Ouroboros] Freeport built out: props, warehouse, courtyard, motor pool and harbour office. Save the scene to keep it.");
        }

        // ------------------------------------------------------------------
        // Shared materials: one asset per colour, so a thousand boxes batch into a handful of draw calls

        private static readonly Dictionary<string, Material> s_materials = new Dictionary<string, Material>();

        private static Material SharedMaterial(Color color, bool emissive = false)
        {
            string key = (emissive ? "Glow_" : (color.a < 0.99f ? "Clear_" : "Mat_")) + ColorUtility.ToHtmlStringRGBA(color);
            if (s_materials.TryGetValue(key, out var cached) && cached != null) return cached;

            if (!AssetDatabase.IsValidFolder(ArtDir)) AssetDatabase.CreateFolder(Root, "Art");
            if (!AssetDatabase.IsValidFolder(MaterialDir)) AssetDatabase.CreateFolder(ArtDir, "Materials");
            string path = MaterialDir + "/" + key + ".mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                bool clear = color.a < 0.99f;
                var shader = clear
                    ? (Shader.Find("Sprites/Default") ?? Shader.Find("Standard"))
                    : (Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                if (shader == null) return null;
                material = new Material(shader) { color = color };
                if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
                if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.2f);
                if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.2f);
                if (clear) material.renderQueue = 3000;
                if (emissive)
                {
                    material.EnableKeyword("_EMISSION");
                    if (material.HasProperty("_EmissionColor")) material.SetColor("_EmissionColor", color * 1.8f);
                    material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                }
                AssetDatabase.CreateAsset(material, path);
            }
            s_materials[key] = material;
            return material;
        }

        // ------------------------------------------------------------------
        // Part helpers. A prop is a root at a floor position with a yaw; parts are placed in its local space.

        /// <summary>(v0.14) Height of the floor props are being placed on. 0 for a ground floor.</summary>
        private static float s_propY = 0f;

        private static Transform Prop(Transform parent, string name, float x, float z, float yaw)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent);
            // [v0.13] t.position = new Vector3(x, 0f, z);
            t.position = new Vector3(x, s_propY, z); // (v0.14) props can stand on an upper floor
            t.rotation = Quaternion.Euler(0f, yaw, 0f);
            return t;
        }

        private static GameObject Shape(PrimitiveType type, Transform root, string name, Vector3 local, Vector3 size, Color color, bool solid, Vector3 euler, bool glow)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            var material = SharedMaterial(color, glow);
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            if (solid) go.isStatic = true;
            else
            {
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.layer = DecorLayer;
                // (v0.14) Decoration never moves, so it can be batched. The NavMesh bake skips it by layer, not by this flag.
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic);
                var renderer = go.GetComponent<Renderer>();
                if (color.a < 0.99f || glow) renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
            return go;
        }

        /// <summary>A solid box: cover, and carved out of the NavMesh.</summary>
        private static GameObject Hard(Transform root, string name, Vector3 local, Vector3 size, Color color, Vector3 euler = default)
            => Shape(PrimitiveType.Cube, root, name, local, size, color, true, euler, false);

        /// <summary>A box that is only looked at.</summary>
        private static GameObject Soft(Transform root, string name, Vector3 local, Vector3 size, Color color, Vector3 euler = default)
            => Shape(PrimitiveType.Cube, root, name, local, size, color, false, euler, false);

        private static GameObject Lit(Transform root, string name, Vector3 local, Vector3 size, Color color, Vector3 euler = default)
            => Shape(PrimitiveType.Cube, root, name, local, size, color, false, euler, true);

        /// <summary>An upright cylinder of the given radius and full height, centred at <paramref name="local"/>.</summary>
        private static GameObject Drum(Transform root, string name, Vector3 local, float radius, float height, Color color, bool solid, Vector3 euler = default)
            => Shape(PrimitiveType.Cylinder, root, name, local, new Vector3(radius * 2f, height * 0.5f, radius * 2f), color, solid, euler, false);

        private static GameObject Blob(Transform root, string name, Vector3 local, Vector3 size, Color color)
            => Shape(PrimitiveType.Sphere, root, name, local, size, color, false, default, false);

        private static void PropLight(Transform root, Vector3 local, Color color, float range, float intensity)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(root, false);
            go.transform.localPosition = local;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
        }

        /// <summary>A repeatable 0..1 value from two numbers, so props vary without changing between builds.</summary>
        private static float Vary(float a, float b)
        {
            float v = Mathf.Sin(a * 12.9898f + b * 78.233f) * 43758.5453f;
            return v - Mathf.Floor(v);
        }

        // ------------------------------------------------------------------
        // Props

        private static void Crate(Transform p, float x, float z, float yaw, float size = 1.2f)
        {
            var r = Prop(p, "Crate", x, z, yaw);
            CrateAt(r, Vector3.zero, size, 0f);
        }

        private static void CrateAt(Transform r, Vector3 floor, float s, float turn)
        {
            var box = Hard(r, "Crate Body", floor + Vector3.up * (s * 0.5f), new Vector3(s, s, s), Wood, new Vector3(0f, turn, 0f));
            // Battens and a stencil band, as children so they turn with the crate. Child sizes are fractions of the body.
            Soft(box.transform, "Batten", new Vector3(0f, 0.42f, 0f), new Vector3(1.04f, 0.1f, 1.04f), WoodDark);
            Soft(box.transform, "Batten", new Vector3(0f, -0.42f, 0f), new Vector3(1.04f, 0.1f, 1.04f), WoodDark);
            Soft(box.transform, "Stencil", new Vector3(0f, 0f, 0.505f), new Vector3(0.5f, 0.22f, 0.01f), MetalDark);
            Soft(box.transform, "Stencil", new Vector3(0.505f, 0f, 0f), new Vector3(0.01f, 0.22f, 0.5f), MetalDark);
        }

        private static void CrateStack(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Crate Stack", x, z, yaw);
            CrateAt(r, new Vector3(-0.6f, 0f, 0f), 1.2f, 0f);
            CrateAt(r, new Vector3(0.62f, 0f, 0.05f), 1.05f, 0f);
            CrateAt(r, new Vector3(-0.45f, 1.2f, 0f), 0.9f, 14f);
        }

        private static void Barrels(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Barrels", x, z, yaw);
            var colours = new[] { new Color(0.15f, 0.3f, 0.5f), new Color(0.55f, 0.25f, 0.12f), new Color(0.2f, 0.4f, 0.25f) };
            var spots = new[] { new Vector3(-0.38f, 0f, -0.3f), new Vector3(0.38f, 0f, -0.3f), new Vector3(0f, 0f, 0.4f) };
            for (int i = 0; i < 3; i++)
            {
                Drum(r, "Barrel", spots[i] + Vector3.up * 0.47f, 0.31f, 0.94f, colours[i], true);
                Drum(r, "Hoop", spots[i] + Vector3.up * 0.7f, 0.325f, 0.05f, MetalDark, false);
                Drum(r, "Hoop", spots[i] + Vector3.up * 0.25f, 0.325f, 0.05f, MetalDark, false);
            }
            Drum(r, "Warning Band", spots[1] + Vector3.up * 0.47f, 0.318f, 0.16f, Hazard, false);
        }

        private static void Pallet(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Pallet", x, z, yaw);
            Hard(r, "Pallet Base", new Vector3(0f, 0.07f, 0f), new Vector3(1.2f, 0.14f, 1f), Wood);
            Hard(r, "Load", new Vector3(0f, 0.62f, 0f), new Vector3(1.08f, 0.96f, 0.9f), new Color(0.72f, 0.72f, 0.68f));
            Soft(r, "Strap", new Vector3(0.25f, 0.62f, 0f), new Vector3(0.06f, 0.98f, 0.92f), new Color(0.1f, 0.3f, 0.6f));
            Soft(r, "Strap", new Vector3(-0.25f, 0.62f, 0f), new Vector3(0.06f, 0.98f, 0.92f), new Color(0.1f, 0.3f, 0.6f));
            Soft(r, "Label", new Vector3(0f, 0.75f, 0.455f), new Vector3(0.3f, 0.2f, 0.01f), Color.white);
        }

        private static void Bench(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Bench", x, z, yaw);
            Hard(r, "Seat", new Vector3(0f, 0.45f, 0f), new Vector3(2f, 0.08f, 0.5f), Wood);
            Hard(r, "Leg", new Vector3(-0.85f, 0.21f, 0f), new Vector3(0.1f, 0.42f, 0.5f), MetalDark);
            Hard(r, "Leg", new Vector3(0.85f, 0.21f, 0f), new Vector3(0.1f, 0.42f, 0.5f), MetalDark);
            Soft(r, "Back", new Vector3(0f, 0.8f, -0.24f), new Vector3(2f, 0.3f, 0.06f), Wood, new Vector3(-8f, 0f, 0f));
        }

        /// <summary>A stone planter. With a tree it is a broad-trunked, flat-crowned tree: shade, and a trunk to stand behind.</summary>
        private static void Planter(Transform p, float x, float z, float yaw, bool tree = true)
        {
            var r = Prop(p, "Planter", x, z, yaw);
            Hard(r, "Planter Box", new Vector3(0f, 0.3f, 0f), new Vector3(2.2f, 0.6f, 2.2f), SandstoneDark);
            Soft(r, "Soil", new Vector3(0f, 0.61f, 0f), new Vector3(2f, 0.04f, 2f), new Color(0.22f, 0.15f, 0.1f));
            if (!tree)
            {
                for (int i = 0; i < 5; i++) Blob(r, "Shrub", new Vector3(Vary(x, i) * 1.4f - 0.7f, 0.85f, Vary(z, i) * 1.4f - 0.7f), new Vector3(0.7f, 0.6f, 0.7f), Leaf);
                return;
            }
            float lean = Vary(x, z) * 6f - 3f;
            Drum(r, "Trunk", new Vector3(0f, 2.1f, 0f), 0.42f, 3f, new Color(0.45f, 0.38f, 0.32f), true, new Vector3(lean, 0f, 0f));
            Drum(r, "Trunk Foot", new Vector3(0f, 0.85f, 0f), 0.55f, 0.6f, new Color(0.45f, 0.38f, 0.32f), false);
            for (int i = 0; i < 4; i++)
            {
                float a = i * 90f + Vary(x + i, z) * 40f;
                Drum(r, "Branch", new Vector3(Mathf.Sin(a * Mathf.Deg2Rad) * 0.8f, 3.9f, Mathf.Cos(a * Mathf.Deg2Rad) * 0.8f), 0.12f, 1.7f, new Color(0.45f, 0.38f, 0.32f), false, new Vector3(55f, a, 0f));
            }
            Blob(r, "Crown", new Vector3(0f, 4.7f, 0f), new Vector3(5f, 1.3f, 5f), Leaf);
            Blob(r, "Crown", new Vector3(1.2f, 5.1f, 0.6f), new Vector3(3f, 1f, 3f), new Color(0.25f, 0.48f, 0.22f));
            Blob(r, "Crown", new Vector3(-1.1f, 5f, -0.8f), new Vector3(3.2f, 1f, 3.2f), new Color(0.17f, 0.38f, 0.19f));
        }

        private static void LampPost(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Lamp Post", x, z, yaw);
            Drum(r, "Post", new Vector3(0f, 2.3f, 0f), 0.08f, 4.6f, MetalDark, true);
            Drum(r, "Foot", new Vector3(0f, 0.15f, 0f), 0.17f, 0.3f, MetalDark, false);
            Soft(r, "Arm", new Vector3(0f, 4.55f, 0.4f), new Vector3(0.07f, 0.07f, 0.9f), MetalDark);
            Lit(r, "Lamp", new Vector3(0f, 4.48f, 0.8f), new Vector3(0.28f, 0.1f, 0.4f), new Color(1f, 0.93f, 0.75f));
            PropLight(r, new Vector3(0f, 4.2f, 0.8f), new Color(1f, 0.93f, 0.78f), 13f, 1.5f);
        }

        /// <summary>A boxy panel van, nose along local +Z. Parked at the side exits it reads as the getaway.</summary>
        private static void Van(Transform p, float x, float z, float yaw, Color paint)
        {
            var r = Prop(p, "Van", x, z, yaw);
            Hard(r, "Body", new Vector3(0f, 1.3f, -0.5f), new Vector3(2f, 1.7f, 3.6f), paint);
            Hard(r, "Cab", new Vector3(0f, 1.05f, 1.75f), new Vector3(2f, 1.2f, 1.1f), paint);
            Soft(r, "Windscreen", new Vector3(0f, 1.75f, 1.55f), new Vector3(1.8f, 0.6f, 0.5f), MetalDark, new Vector3(-28f, 0f, 0f));
            Soft(r, "Bumper", new Vector3(0f, 0.5f, 2.33f), new Vector3(2.05f, 0.25f, 0.12f), MetalDark);
            Soft(r, "Stripe", new Vector3(0f, 1.15f, -0.5f), new Vector3(2.03f, 0.18f, 3.62f), Color.Lerp(paint, Color.white, 0.6f));
            Soft(r, "Rear Doors", new Vector3(0f, 1.3f, -2.31f), new Vector3(1.7f, 1.4f, 0.02f), Color.Lerp(paint, Color.black, 0.25f));
            Soft(r, "Roof Rack", new Vector3(0f, 2.2f, -0.5f), new Vector3(1.7f, 0.06f, 3f), MetalDark);
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1.5f, 1.5f })
                {
                    Drum(r, "Wheel", new Vector3(sx * 0.98f, 0.38f, sz), 0.38f, 0.24f, MetalDark, false, new Vector3(0f, 0f, 90f));
                }
                Lit(r, "Headlight", new Vector3(sx * 0.7f, 0.85f, 2.31f), new Vector3(0.3f, 0.16f, 0.03f), new Color(1f, 0.95f, 0.8f));
                Lit(r, "Tail Light", new Vector3(sx * 0.85f, 0.9f, -2.31f), new Vector3(0.16f, 0.3f, 0.03f), new Color(1f, 0.15f, 0.1f));
            }
            Lit(r, "Beacon", new Vector3(0f, 2.3f, 1.2f), new Vector3(0.3f, 0.12f, 0.2f), new Color(0.1f, 0.95f, 0.5f));
        }

        private static void Forklift(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Forklift", x, z, yaw);
            Hard(r, "Body", new Vector3(0f, 0.75f, -0.35f), new Vector3(1.2f, 0.9f, 1.6f), Hazard);
            Hard(r, "Mast", new Vector3(0f, 1.25f, 0.62f), new Vector3(1f, 2.3f, 0.16f), MetalDark);
            Soft(r, "Counterweight", new Vector3(0f, 0.7f, -1.2f), new Vector3(1.1f, 0.7f, 0.2f), MetalDark);
            Soft(r, "Seat", new Vector3(0f, 1.3f, -0.45f), new Vector3(0.5f, 0.2f, 0.5f), MetalDark);
            foreach (float sx in new[] { -1f, 1f })
            {
                Soft(r, "Fork", new Vector3(sx * 0.3f, 0.14f, 1.2f), new Vector3(0.12f, 0.06f, 1.1f), Metal);
                Soft(r, "Guard Post", new Vector3(sx * 0.52f, 1.75f, -0.9f), new Vector3(0.07f, 1.1f, 0.07f), MetalDark);
                Soft(r, "Guard Post", new Vector3(sx * 0.52f, 1.75f, 0.35f), new Vector3(0.07f, 1.1f, 0.07f), MetalDark);
                Drum(r, "Wheel", new Vector3(sx * 0.62f, 0.3f, 0.25f), 0.3f, 0.2f, MetalDark, false, new Vector3(0f, 0f, 90f));
                Drum(r, "Wheel", new Vector3(sx * 0.62f, 0.24f, -0.95f), 0.24f, 0.18f, MetalDark, false, new Vector3(0f, 0f, 90f));
            }
            Soft(r, "Guard Roof", new Vector3(0f, 2.32f, -0.28f), new Vector3(1.15f, 0.06f, 1.4f), MetalDark);
            Lit(r, "Beacon", new Vector3(0f, 2.42f, -0.8f), new Vector3(0.14f, 0.12f, 0.14f), new Color(1f, 0.6f, 0.1f));
        }

        /// <summary>A market stall under an awning in a crew's pattern. The counter is cover; the awning is not.</summary>
        private static void Stall(Transform p, float x, float z, float yaw, Core.TeamID cloth)
        {
            var r = Prop(p, "Market Stall", x, z, yaw);
            Hard(r, "Counter", new Vector3(0f, 0.5f, -0.2f), new Vector3(3f, 1f, 0.9f), Wood);
            Soft(r, "Counter Top", new Vector3(0f, 1.02f, -0.2f), new Vector3(3.1f, 0.05f, 1f), WoodDark);
            foreach (float sx in new[] { -1.45f, 1.45f })
            {
                foreach (float sz in new[] { -0.95f, 0.95f }) Soft(r, "Pole", new Vector3(sx, 1.35f, sz), new Vector3(0.08f, 2.7f, 0.08f), WoodDark);
            }
            var awning = Soft(r, "Awning", new Vector3(0f, 2.72f, 0f), new Vector3(3.5f, 0.05f, 2.5f), Art.CrewStyle.Get(cloth).Primary, new Vector3(-7f, 0f, 0f));
            var material = CrewMaterialAsset(cloth, "Awning", new Vector2(3.5f, 2.5f));
            if (material != null) awning.GetComponent<Renderer>().sharedMaterial = material;
            // Goods: baskets, bolts of cloth and a row of jars. Nothing here is a real, named object.
            for (int i = 0; i < 3; i++) Drum(r, "Basket", new Vector3(-1.05f + i * 0.5f, 1.17f, -0.2f), 0.2f, 0.26f, new Color(0.72f, 0.56f, 0.3f), false);
            for (int i = 0; i < 3; i++) Soft(r, "Cloth Bolt", new Vector3(0.75f + i * 0.02f, 1.1f + i * 0.09f, -0.2f), new Vector3(0.9f, 0.09f, 0.4f), Art.CrewStyle.Get(cloth).Colour(i));
            Hard(r, "Sack", new Vector3(-1.1f, 0.3f, 0.6f), new Vector3(0.6f, 0.6f, 0.5f), new Color(0.7f, 0.62f, 0.45f));
        }

        private static void Booth(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Harbour Office", x, z, yaw);
            Hard(r, "Walls", new Vector3(0f, 1.3f, 0f), new Vector3(3f, 2.6f, 3f), new Color(0.3f, 0.36f, 0.42f));
            Lit(r, "Windows", new Vector3(0f, 1.6f, 0f), new Vector3(3.02f, 0.7f, 2.2f), new Color(0.55f, 0.8f, 0.95f));
            Lit(r, "Windows", new Vector3(0f, 1.6f, 0f), new Vector3(2.2f, 0.7f, 3.02f), new Color(0.55f, 0.8f, 0.95f));
            Hard(r, "Roof", new Vector3(0f, 2.7f, 0f), new Vector3(3.5f, 0.2f, 3.5f), MetalDark);
            Soft(r, "Door", new Vector3(0f, 1f, 1.51f), new Vector3(0.9f, 2f, 0.02f), MetalDark);
            Drum(r, "Aerial", new Vector3(1.2f, 3.6f, -1.2f), 0.03f, 1.6f, MetalDark, false);
            Lit(r, "Aerial Light", new Vector3(1.2f, 4.42f, -1.2f), new Vector3(0.1f, 0.1f, 0.1f), new Color(1f, 0.2f, 0.15f));
        }

        /// <summary>A round basin with a ring sculpture of a snake eating its tail: the freeport's emblem, and hard cover.</summary>
        private static void Fountain(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Fountain", x, z, yaw);
            Drum(r, "Basin", new Vector3(0f, 0.25f, 0f), 2.6f, 0.5f, SandstoneDark, true);
            Drum(r, "Water", new Vector3(0f, 0.3f, 0f), 2.35f, 0.5f, new Color(0.25f, 0.55f, 0.7f, 0.6f), false);
            Drum(r, "Plinth", new Vector3(0f, 0.8f, 0f), 0.5f, 1.6f, DarkStone, true);
            RingOfBoxes(r, new Vector3(0f, 2.55f, 0f), 0.95f, 22, 0.24f, Gold, upright: true);
            PropLight(r, new Vector3(0f, 2.2f, 1.6f), new Color(0.6f, 0.85f, 1f), 9f, 1.6f);
        }

        /// <summary>A ring made of short boxes, with a wider "head" closing on a thin "tail". Flat on the floor, or standing.</summary>
        private static void RingOfBoxes(Transform root, Vector3 centre, float radius, int segments, float thickness, Color color, bool upright)
        {
            float segment = 2f * Mathf.PI * radius / segments * 1.08f;
            for (int i = 0; i < segments; i++)
            {
                float t = i / (float)segments;
                float a = t * Mathf.PI * 2f;
                float girth = thickness * Mathf.Lerp(0.45f, 1f, t); // the tail thickens toward the head
                Vector3 offset = upright
                    ? new Vector3(Mathf.Cos(a) * radius, Mathf.Sin(a) * radius, 0f)
                    : new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
                Vector3 euler = upright ? new Vector3(0f, 0f, a * Mathf.Rad2Deg) : new Vector3(0f, -a * Mathf.Rad2Deg, 0f);
                Vector3 size = upright ? new Vector3(girth, segment, girth) : new Vector3(girth, 0.03f, segment); // flat rings are an inlay, not a kerb
                Soft(root, "Ring", centre + offset, size, color, euler);
            }
            // The head, just behind the tail tip.
            float h = -0.06f * Mathf.PI * 2f;
            Vector3 head = upright ? new Vector3(Mathf.Cos(h) * radius, Mathf.Sin(h) * radius, 0f) : new Vector3(Mathf.Cos(h) * radius, 0f, Mathf.Sin(h) * radius);
            Vector3 headEuler = upright ? new Vector3(0f, 0f, h * Mathf.Rad2Deg) : new Vector3(0f, -h * Mathf.Rad2Deg, 0f);
            Vector3 headSize = upright ? new Vector3(thickness * 1.6f, segment * 1.5f, thickness * 1.6f) : new Vector3(thickness * 1.6f, 0.04f, segment * 1.5f);
            Soft(root, "Ring Head", centre + head, headSize, color, headEuler);
        }

        private static void ServerCabinet(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Server Cabinet", x, z, yaw);
            Hard(r, "Cabinet", new Vector3(0f, 1.1f, 0f), new Vector3(0.8f, 2.2f, 1f), MetalDark);
            for (int i = 0; i < 6; i++)
            {
                Color led = Vary(x + i, z) > 0.3f ? new Color(0.2f, 1f, 0.5f) : new Color(1f, 0.6f, 0.1f);
                Lit(r, "Led", new Vector3(-0.2f + Vary(i, x) * 0.4f, 0.4f + i * 0.3f, 0.505f), new Vector3(0.22f, 0.03f, 0.01f), led);
                Soft(r, "Blade", new Vector3(0f, 0.4f + i * 0.3f, 0.502f), new Vector3(0.7f, 0.2f, 0.01f), Metal);
            }
        }

        private static void DisplayCase(Transform p, float x, float z, float yaw, Data.LootShape shape, Data.LootTier tier)
        {
            var r = Prop(p, "Display Case (" + shape + ")", x, z, yaw);
            Hard(r, "Base", new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1f, 1.2f), DarkStone);
            Soft(r, "Trim", new Vector3(0f, 1.02f, 0f), new Vector3(1.26f, 0.05f, 1.26f), Gold);
            Soft(r, "Glass", new Vector3(0f, 1.6f, 0f), new Vector3(1.1f, 1.1f, 1.1f), Glass);
            Soft(r, "Cap", new Vector3(0f, 2.17f, 0f), new Vector3(1.16f, 0.05f, 1.16f), DarkStone);
            var piece = new GameObject("Show Piece (" + shape + ")");
            piece.transform.SetParent(r, false);
            piece.transform.localPosition = new Vector3(0f, 1.06f, 0f);
            var display = piece.AddComponent<Art.DisplayPiece>();
            display.shape = shape;
            display.tier = tier;
            display.scale = 1.25f;
            PropLight(r, new Vector3(0f, 2.05f, 0f), WarmLight, 3.5f, 1.8f);
        }

        /// <summary>Open shelving, <paramref name="length"/> metres long (along local X), loaded with boxes.</summary>
        private static void ShelfRack(Transform p, float x, float z, float yaw, float length)
        {
            var r = Prop(p, "Shelf Rack", x, z, yaw);
            float[] heights = { 0.1f, 1.05f, 2f, 2.95f };
            foreach (float y in heights) Hard(r, "Shelf", new Vector3(0f, y, 0f), new Vector3(length, 0.06f, 0.9f), Metal);
            int bays = Mathf.Max(1, Mathf.RoundToInt(length / 2f));
            for (int i = 0; i <= bays; i++)
            {
                float px = -length * 0.5f + i * (length / bays);
                Hard(r, "Upright", new Vector3(px, 1.55f, 0f), new Vector3(0.08f, 3.1f, 0.9f), new Color(0.2f, 0.35f, 0.6f));
            }
            // Boxes: enough to break lines of sight, not enough to make the rack a wall.
            var cardboard = new[] { new Color(0.62f, 0.48f, 0.3f), new Color(0.55f, 0.42f, 0.27f), new Color(0.3f, 0.32f, 0.36f), new Color(0.68f, 0.66f, 0.6f) };
            for (int bay = 0; bay < bays; bay++)
            {
                for (int level = 0; level < 3; level++)
                {
                    for (int n = 0; n < 2; n++)
                    {
                        float seed = Vary(x * 3f + bay * 7f + n, z * 5f + level * 11f);
                        if (seed < 0.22f) continue; // an empty spot
                        float w = 0.5f + Vary(seed, 1f) * 0.35f, hgt = 0.35f + Vary(seed, 2f) * 0.45f;
                        float bx = -length * 0.5f + (bay + 0.28f + n * 0.44f) * (length / bays);
                        Hard(r, "Box", new Vector3(bx, heights[level] + 0.03f + hgt * 0.5f, 0f), new Vector3(w, hgt, 0.7f), cardboard[(int)(seed * 3.99f)]);
                    }
                }
            }
        }

        /// <summary>A gantry crane: four solid legs, with rails, a trolley and a hook overhead that block nothing.</summary>
        private static void GantryCrane(Transform p, float x0, float x1, float z0, float z1, float height)
        {
            var r = Prop(p, "Gantry Crane", 0f, 0f, 0f);
            foreach (float x in new[] { x0, x1 })
            {
                foreach (float z in new[] { z0, z1 })
                {
                    Hard(r, "Leg", new Vector3(x, height * 0.5f, z), new Vector3(0.7f, height, 0.7f), Hazard);
                    Soft(r, "Leg Band", new Vector3(x, 1f, z), new Vector3(0.74f, 0.5f, 0.74f), MetalDark);
                    Soft(r, "Leg Band", new Vector3(x, 2f, z), new Vector3(0.74f, 0.5f, 0.74f), MetalDark);
                }
                Soft(r, "Rail", new Vector3(x, height + 0.3f, (z0 + z1) * 0.5f), new Vector3(0.8f, 0.6f, Mathf.Abs(z1 - z0) + 1.5f), Hazard);
            }
            float mid = (z0 + z1) * 0.5f + 3f;
            Soft(r, "Bridge", new Vector3((x0 + x1) * 0.5f, height + 0.75f, mid), new Vector3(Mathf.Abs(x1 - x0) + 1f, 0.7f, 1.2f), Hazard);
            float hookX = Mathf.Lerp(x0, x1, 0.4f);
            Soft(r, "Trolley", new Vector3(hookX, height + 0.25f, mid), new Vector3(1.4f, 0.5f, 1.4f), MetalDark);
            Soft(r, "Cable", new Vector3(hookX, height - 1.6f, mid), new Vector3(0.06f, 3.4f, 0.06f), MetalDark);
            Soft(r, "Hook Block", new Vector3(hookX, height - 3.4f, mid), new Vector3(0.5f, 0.4f, 0.3f), Hazard);
            Lit(r, "Crane Light", new Vector3(hookX, height + 0.05f, mid), new Vector3(0.3f, 0.1f, 0.3f), new Color(1f, 0.95f, 0.8f));
        }

        /// <summary>
        /// A walled store with three doors and a roof over its back half: a place to break a chase. No loot inside.
        /// </summary>
        private static void Warehouse(Transform p, float x0, float x1, float z0, float z1, float height)
        {
            var r = Prop(p, "Bonded Store", 0f, 0f, 0f);
            Color wall = new Color(0.52f, 0.44f, 0.36f);
            const float t = 0.5f, door = 4f;
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;

            void WallX(string name, float fromX, float toX, float z) =>
                Hard(r, name, new Vector3((fromX + toX) * 0.5f, height * 0.5f, z), new Vector3(Mathf.Abs(toX - fromX), height, t), wall);
            void WallZ(string name, float x, float fromZ, float toZ) =>
                Hard(r, name, new Vector3(x, height * 0.5f, (fromZ + toZ) * 0.5f), new Vector3(t, height, Mathf.Abs(toZ - fromZ)), wall);

            WallX("Wall N", x0, cx - door * 0.5f, z1); WallX("Wall N", cx + door * 0.5f, x1, z1);
            WallX("Wall S", x0, x1, z0);
            WallZ("Wall W", x0, z0, cz - door * 0.5f); WallZ("Wall W", x0, cz + door * 0.5f, z1);
            WallZ("Wall E", x1, z0, cz - door * 0.5f); WallZ("Wall E", x1, cz + door * 0.5f, z1);

            // Door frames in hazard yellow, so the ways in read from across the yard.
            Soft(r, "Lintel N", new Vector3(cx, height - 0.25f, z1), new Vector3(door + 0.6f, 0.5f, t + 0.1f), Hazard);
            Soft(r, "Lintel W", new Vector3(x0, height - 0.25f, cz), new Vector3(t + 0.1f, 0.5f, door + 0.6f), Hazard);
            Soft(r, "Lintel E", new Vector3(x1, height - 0.25f, cz), new Vector3(t + 0.1f, 0.5f, door + 0.6f), Hazard);
            Soft(r, "Top Band", new Vector3(cx, height + 0.08f, z1), new Vector3(x1 - x0 + t, 0.16f, t + 0.06f), MetalDark);
            Soft(r, "Top Band", new Vector3(cx, height + 0.08f, z0), new Vector3(x1 - x0 + t, 0.16f, t + 0.06f), MetalDark);

            // Roof over the back half; open rafters over the front, so the room is lit and still open to the sky.
            Hard(r, "Roof", new Vector3(cx, height + 0.3f, (z0 + cz) * 0.5f), new Vector3(x1 - x0 + 1f, 0.25f, cz - z0 + 0.5f), MetalDark);
            for (float x = x0 + 2f; x < x1 - 1f; x += 4f) Soft(r, "Rafter", new Vector3(x, height + 0.3f, (cz + z1) * 0.5f), new Vector3(0.25f, 0.25f, z1 - cz), MetalDark);
            Soft(r, "Floor", new Vector3(cx, 0.02f, cz), new Vector3(x1 - x0, 0.04f, z1 - z0), new Color(0.36f, 0.36f, 0.38f));
            PropLight(r, new Vector3(cx - 5f, height - 0.6f, cz - 3f), new Color(0.9f, 0.95f, 1f), 14f, 1.6f);
            PropLight(r, new Vector3(cx + 5f, height - 0.6f, cz - 3f), new Color(0.9f, 0.95f, 1f), 14f, 1.6f);
            Sign(r, "BONDED STORE", new Vector3(cx, height + 0.75f, z1 + 0.3f), Quaternion.Euler(0f, 180f, 0f), 0.55f, Hazard);
        }

        /// <summary>Four posts and a roof: overhead cover for whatever is parked under it.</summary>
        private static void Canopy(Transform p, float x0, float x1, float z0, float z1, float height, string label)
        {
            var r = Prop(p, "Canopy", 0f, 0f, 0f);
            foreach (float x in new[] { x0, x1 })
            {
                foreach (float z in new[] { z0, z1 }) Hard(r, "Post", new Vector3(x, height * 0.5f, z), new Vector3(0.4f, height, 0.4f), MetalDark);
            }
            float cx = (x0 + x1) * 0.5f, cz = (z0 + z1) * 0.5f;
            Hard(r, "Roof", new Vector3(cx, height + 0.13f, cz), new Vector3(x1 - x0 + 1.2f, 0.26f, z1 - z0 + 1.2f), Metal);
            Soft(r, "Fascia", new Vector3(cx, height - 0.1f, z1 + 0.62f), new Vector3(x1 - x0 + 1.2f, 0.5f, 0.06f), Hazard);
            Soft(r, "Floor", new Vector3(cx, 0.02f, cz), new Vector3(x1 - x0, 0.04f, z1 - z0), new Color(0.2f, 0.2f, 0.22f));
            PropLight(r, new Vector3(cx, height - 0.5f, cz), new Color(1f, 0.92f, 0.75f), 12f, 1.5f);
            Sign(r, label, new Vector3(cx, height - 0.1f, z1 + 0.67f), Quaternion.Euler(0f, 180f, 0f), 0.32f, MetalDark);
        }

        // ------------------------------------------------------------------
        // Flat dressing

        private static void FloorSlab(Transform p, string name, float x0, float x1, float z0, float z1, Color color, float y = 0.015f)
        {
            var slab = Soft(p, name, new Vector3((x0 + x1) * 0.5f, y, (z0 + z1) * 0.5f), new Vector3(Mathf.Abs(x1 - x0), 0.03f, Mathf.Abs(z1 - z0)), color);
            slab.GetComponent<Renderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        }

        /// <summary>A yellow and black striped line on the floor between two points on the same axis.</summary>
        private static void HazardLine(Transform p, float x0, float z0, float x1, float z1, float width = 0.4f)
        {
            bool alongX = Mathf.Abs(x1 - x0) >= Mathf.Abs(z1 - z0);
            float length = alongX ? Mathf.Abs(x1 - x0) : Mathf.Abs(z1 - z0);
            int pieces = Mathf.Max(1, Mathf.RoundToInt(length));
            for (int i = 0; i < pieces; i++)
            {
                float t = (i + 0.5f) / pieces;
                var at = new Vector3(Mathf.Lerp(x0, x1, t), 0.045f, Mathf.Lerp(z0, z1, t));
                var size = alongX ? new Vector3(length / pieces, 0.03f, width) : new Vector3(width, 0.03f, length / pieces);
                Soft(p, "Hazard", at, size, i % 2 == 0 ? Hazard : MetalDark);
            }
        }

        /// <summary>A board on a perimeter wall with a district name on it.</summary>
        private static void WallSign(Transform p, string text, Vector3 position, float yaw, Color color, float letterHeight = 1.1f)
        {
            var facing = Quaternion.Euler(0f, yaw, 0f);
            float width = Mathf.Max(3f, text.Length * letterHeight * 0.72f + 1.2f);
            var board = Soft(p, "Sign Board", position, new Vector3(width, letterHeight + 0.9f, 0.12f), DarkStone);
            board.transform.rotation = facing;
            Sign(p, text, position - (facing * Vector3.forward) * 0.09f, facing, letterHeight, color);
        }

        // ------------------------------------------------------------------
        // The build-out

        /// <summary>
        /// Adds the wider Freeport to a level: props in every district, three new areas (a bonded store in the
        /// south-east, a courtyard in the north-east, a motor pool in the south-west), a harbour office, a crane
        /// over the docks, getaway vans at the side exits, district floors, signs, wall trim and lighting.
        /// </summary>
        private static void BuildFreeportExpansion(Transform level)
        {
            if (GameObject.Find(PropsName) != null) return;
            var p = Group(level, PropsName);

            // ---- District floors, so you know where you are by what is under your feet
            var floors = Group(p, "Floors");
            FloorSlab(floors, "Docks Floor", -69f, -29f, -31f, 31f, new Color(0.2f, 0.21f, 0.24f));
            FloorSlab(floors, "Archive Floor", 29f, 69f, -31f, 26f, new Color(0.29f, 0.34f, 0.4f));
            FloorSlab(floors, "Loading Floor", -27f, 27f, -69f, -31f, new Color(0.35f, 0.34f, 0.31f));
            FloorSlab(floors, "Courtyard Paving", 35.5f, 55.5f, 45.5f, 67f, new Color(0.56f, 0.46f, 0.34f));
            FloorSlab(floors, "Courtyard Lawn", 41f, 50f, 46.5f, 49f, new Color(0.24f, 0.42f, 0.22f), 0.035f);
            FloorSlab(floors, "Courtyard Lawn", 41f, 50f, 63f, 65.5f, new Color(0.24f, 0.42f, 0.22f), 0.035f);
            FloorSlab(floors, "Atrium Ring Road", -24f, 24f, -24f, 24f, new Color(0.3f, 0.31f, 0.34f));

            // Lanes painted through the container yard and the loading bay.
            HazardLine(floors, -68f, -12.5f, -31f, -12.5f, 0.25f);
            HazardLine(floors, -68f, 19f, -49f, 19f, 0.25f);
            HazardLine(floors, -12f, -45f, 12f, -45f);
            HazardLine(floors, -12f, -60f, 12f, -60f);
            HazardLine(floors, -12f, -45f, -12f, -60f);
            HazardLine(floors, 12f, -45f, 12f, -60f);
            // A warning strip either side of every shutter gate.
            HazardLine(floors, -6f, -38.6f, 6f, -38.6f); HazardLine(floors, -6f, -41.4f, 6f, -41.4f);
            HazardLine(floors, 26.6f, -20f, 26.6f, -8f); HazardLine(floors, 29.4f, -20f, 29.4f, -8f);
            HazardLine(floors, -26.6f, 8f, -26.6f, 20f); HazardLine(floors, -29.4f, 8f, -29.4f, 20f);

            // ---- The atrium: the freeport's emblem laid into the plaza, marking the central getaway
            var atrium = Group(p, "Atrium Dressing");
            RingOfBoxes(atrium, new Vector3(0f, 3.03f, 0f), 6f, 44, 0.9f, Gold, upright: false);
            RingOfBoxes(atrium, new Vector3(0f, 3.03f, 0f), 4.6f, 36, 0.35f, DarkStone, upright: false);
            foreach (var corner in new[] { new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f) })
            {
                // Masts on the plaza pillars carry the house banner high enough to be seen from every dock.
                var at = new Vector3(corner.x * 7f, 0f, corner.y * 7f);
                Drum(atrium, "Mast", at + Vector3.up * 8.4f, 0.07f, 5.2f, MetalDark, false);
                Soft(atrium, "House Banner", at + new Vector3(0f, 8.6f, 0f) + new Vector3(corner.x, 0f, 0f) * 0.75f, new Vector3(1.4f, 3.6f, 0.04f), DarkStone);
                Soft(atrium, "Banner Bar", at + new Vector3(0f, 10.45f, 0f) + new Vector3(corner.x, 0f, 0f) * 0.75f, new Vector3(1.6f, 0.1f, 0.08f), Gold);
                Soft(atrium, "Banner Stripe", at + new Vector3(0f, 8.6f, 0f) + new Vector3(corner.x, 0f, 0f) * 0.75f, new Vector3(0.3f, 3.6f, 0.05f), Gold);
                Lit(atrium, "Mast Light", at + Vector3.up * 11.05f, new Vector3(0.16f, 0.16f, 0.16f), new Color(1f, 0.25f, 0.15f));
            }

            // ---- Big structures
            GantryCrane(p, -63f, -47f, -9f, 16.5f, 9f);
            Warehouse(p, 28f, 50f, -64f, -48f, 4f);
            Canopy(p, -46f, -34f, -64f, -52f, 4.2f, "MOTOR POOL");

            // ---- Generated placements (see the summary at the top of this file)
            Planter(p, -17.5f, -17.5f, 0f, true);
            Planter(p, -17.5f, 17.5f, 0f, true);
            Planter(p, 17.5f, -17.5f, 0f, true);
            Planter(p, 17.5f, 17.5f, 0f, true);
            DisplayCase(p, 12f, 52f, 0f, Data.LootShape.Pendant, Data.LootTier.Rare);
            DisplayCase(p, -20f, 53f, 0f, Data.LootShape.Collar, Data.LootTier.Rare);
            Bench(p, 16f, 30.5f, 0f);
            Bench(p, -16f, 30.5f, 0f);
            ShelfRack(p, 52f, -4f, 90f, 8f);
            ShelfRack(p, 56f, -4f, 90f, 8f);
            ServerCabinet(p, 37.3f, 4f, 90f);
            ServerCabinet(p, 37.3f, 6.5f, 90f);
            ServerCabinet(p, 37.3f, 9f, 90f);
            LampPost(p, 33f, -18f, 0f);
            Crate(p, 55.5f, 6f, 0f);
            CrateStack(p, 56f, 22f, 90f);
            Barrels(p, -44f, 14f, 0f);
            Pallet(p, -52f, -10f, 0f);
            Pallet(p, -54f, -10f, 0f);
            Forklift(p, -46f, 8f, 0f);
            LampPost(p, -34f, -12f, 0f);
            LampPost(p, -33f, 21.5f, 0f);
            CrateStack(p, -62f, 24f, 0f);
            Barrels(p, -64f, -24f, 0f);
            Pallet(p, -5f, -53f, 0f);
            Pallet(p, 5f, -53f, 0f);
            CrateStack(p, 8f, -57f, 0f);
            Barrels(p, -8f, -56.5f, 0f);
            Van(p, 9f, -64.5f, 90f, new Color(0.85f, 0.85f, 0.8f));
            LampPost(p, -10f, -44f, 0f);
            LampPost(p, 10f, -44f, 0f);
            Van(p, -65f, -42.5f, 0f, new Color(0.2f, 0.3f, 0.45f));
            Van(p, 65f, 44.5f, 0f, new Color(0.45f, 0.2f, 0.15f));
            ShelfRack(p, 34f, -60f, 0f, 6f);
            ShelfRack(p, 44f, -60f, 0f, 6f);
            ShelfRack(p, 34f, -52f, 0f, 6f);
            ShelfRack(p, 44f, -52f, 0f, 6f);
            CrateStack(p, 39f, -56.5f, 0f);
            Pallet(p, 30f, -62.5f, 0f);
            Pallet(p, 48f, -62.5f, 0f);
            Barrels(p, 47.5f, -50.5f, 0f);
            Fountain(p, 45.5f, 56f, 0f);
            Planter(p, 39f, 49f, 0f, true);
            Planter(p, 52f, 49f, 0f, true);
            Planter(p, 39f, 64f, 0f, true);
            Planter(p, 52f, 64f, 0f, true);
            Bench(p, 45.5f, 50.5f, 0f);
            Bench(p, 45.5f, 61.5f, 0f);
            Stall(p, 38.5f, 56f, 90f, Core.TeamID.TeamCharlie);
            Stall(p, 52.5f, 56f, 90f, Core.TeamID.TeamDelta);
            LampPost(p, 36f, 47f, 0f);
            LampPost(p, 55f, 47.5f, 0f);
            Van(p, -43f, -58f, 0f, new Color(0.75f, 0.6f, 0.15f));
            Van(p, -37f, -58f, 0f, new Color(0.3f, 0.32f, 0.36f));
            Barrels(p, -31f, -55f, 0f);
            Forklift(p, -50f, -60f, 0f);
            CrateStack(p, -30f, -62f, 0f);
            LampPost(p, -46.5f, -49.8f, 0f);
            Booth(p, -48f, 54f, 0f);
            CrateStack(p, -42f, 58f, 0f);
            CrateStack(p, -44f, 49f, 90f);
            Barrels(p, -52f, 48f, 0f);
            LampPost(p, -38f, 50.5f, 0f);
            Stall(p, -22f, -40f, 0f, Core.TeamID.TeamAlpha);
            Stall(p, 22f, -40f, 0f, Core.TeamID.TeamBravo);
            Bench(p, -20f, 34f, 90f);
            Bench(p, 20f, -34f, 90f);

            // ---- Server floor: LED strips on the existing racks, so the room reads as live hardware
            var tech = Group(p, "Archive Dressing");
            foreach (float x in new[] { 41f, 46f, 51f })
            {
                for (int i = 0; i < 5; i++)
                {
                    Lit(tech, "Rack Led", new Vector3(x - 0.35f + Vary(x, i) * 0.7f, 2.3f + i * 0.38f, -24.88f), new Vector3(0.3f, 0.04f, 0.02f),
                        Vary(i, x) > 0.4f ? new Color(0.2f, 1f, 0.5f) : new Color(0.3f, 0.7f, 1f));
                }
            }
            Soft(tech, "Cable Tray", new Vector3(46f, 4.5f, -26.4f), new Vector3(13f, 0.1f, 0.5f), MetalDark);

            // ---- Perimeter: pilasters and bands break up 140 metres of flat wall
            var trim = Group(p, "Perimeter Trim");
            // The two grids are offset so no pilaster lands behind a sign or a banner.
            for (float t = -63f; t <= 63f; t += 14f)
            {
                Soft(trim, "Pilaster", new Vector3(t, 3f, 69.3f), new Vector3(0.8f, 6f, 0.4f), SandstoneDark);
                Soft(trim, "Pilaster", new Vector3(t, 3f, -69.3f), new Vector3(0.8f, 6f, 0.4f), SandstoneDark);
            }
            for (float t = -56f; t <= 56f; t += 14f)
            {
                Soft(trim, "Pilaster", new Vector3(69.3f, 3f, t), new Vector3(0.4f, 6f, 0.8f), SandstoneDark);
                Soft(trim, "Pilaster", new Vector3(-69.3f, 3f, t), new Vector3(0.4f, 6f, 0.8f), SandstoneDark);
            }
            Soft(trim, "Skirting", new Vector3(0f, 0.3f, -69.42f), new Vector3(139f, 0.6f, 0.16f), DarkStone);
            Soft(trim, "Skirting", new Vector3(69.42f, 0.3f, 0f), new Vector3(0.16f, 0.6f, 139f), DarkStone);
            Soft(trim, "Skirting", new Vector3(-69.42f, 0.3f, 0f), new Vector3(0.16f, 0.6f, 139f), DarkStone);
            Soft(trim, "Cap", new Vector3(0f, 6.1f, 70f), new Vector3(141.4f, 0.2f, 1.3f), Gold);
            Soft(trim, "Cap", new Vector3(0f, 6.1f, -70f), new Vector3(141.4f, 0.2f, 1.3f), Gold);
            Soft(trim, "Cap", new Vector3(70f, 6.1f, 0f), new Vector3(1.3f, 0.2f, 141.4f), Gold);
            Soft(trim, "Cap", new Vector3(-70f, 6.1f, 0f), new Vector3(1.3f, 0.2f, 141.4f), Gold);

            // ---- Signs: a district name on each wall, and green boards over the side exits
            var signs = Group(p, "Signs");
            Color exitGreen = new Color(0.2f, 1f, 0.55f);
            WallSign(signs, "DOCKS", new Vector3(-69.38f, 4.4f, 7f), -90f, new Color(0.7f, 0.85f, 1f));
            WallSign(signs, "ARCHIVE", new Vector3(69.38f, 5f, -35f), 90f, new Color(0.5f, 0.9f, 1f));
            WallSign(signs, "LOADING", new Vector3(-14f, 4.4f, -69.38f), 180f, Hazard);
            WallSign(signs, "COURTYARD", new Vector3(42f, 4.4f, 69.38f), 0f, WarmLight);
            WallSign(signs, "EXIT", new Vector3(0f, 4.4f, -69.38f), 180f, exitGreen, 0.9f);
            WallSign(signs, "EXIT", new Vector3(-69.38f, 4.4f, -34f), -90f, exitGreen, 0.9f);
            WallSign(signs, "EXIT", new Vector3(69.38f, 4.4f, 36f), 90f, exitGreen, 0.9f);

            // ---- Light: each district has its own colour, so the map reads in zones even from the far side
            var lights = Group(p, "District Lights");
            PropLight(lights, new Vector3(-48f, 9.5f, 3f), new Color(0.7f, 0.82f, 1f), 42f, 1.5f);
            PropLight(lights, new Vector3(49f, 8f, -10f), new Color(0.5f, 0.85f, 1f), 36f, 1.4f);
            PropLight(lights, new Vector3(0f, 7.5f, -53f), new Color(1f, 0.82f, 0.5f), 34f, 1.4f);
            PropLight(lights, new Vector3(45.5f, 7f, 56f), new Color(1f, 0.85f, 0.65f), 26f, 1.3f);
            PropLight(lights, new Vector3(-46f, 7f, 53f), new Color(0.75f, 0.85f, 1f), 24f, 1.2f);
            PropLight(lights, new Vector3(0f, 12f, 0f), new Color(1f, 0.9f, 0.7f), 34f, 1.2f);

            Debug.Log("[Ouroboros] Freeport build-out ready: props in every district, a bonded store (south-east), a courtyard (north-east), " +
                      "a motor pool (south-west), a harbour office (north-west), a crane over the docks and getaway vans at the side exits.");
        }
    }
}
