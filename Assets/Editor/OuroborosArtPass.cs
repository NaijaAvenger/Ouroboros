using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ouroboros.EditorTools
{
    /// <summary>
    /// The art pass (v0.12, milestone M4). It gives the grey box an identity without any hand modelling:
    ///
    ///   Assets   weapon models from the SciFi Gun Pack, hit and explosion effects from Cartoon FX Remaster,
    ///            and each crew's pattern saved as a texture and materials under Assets/Ouroboros/Art.
    ///   Scene    the Gallery district dressed as an exhibition hall (floor, entrance arch, colonnade,
    ///            plinths with show pieces, warm lights, signs), and a patterned banner at each crew dock.
    ///
    /// Everything is idempotent: slots that are already filled are left alone, and the scene dressing is
    /// skipped when an object called "Gallery Dressing" exists. It runs as part of "Create Freeport Map";
    /// the menu item below applies it to assets and to an already-built Freeport scene.
    ///
    /// Menu: Ouroboros > Setup > Apply Art Pass (M4)
    /// </summary>
    public static partial class OuroborosDevSceneBuilder
    {
        private const string ArtDir = Root + "/Art";
        private const string GunPackDir = "Assets/SciFi Gun Pack/Prefabs";
        private const string EffectsDir = "Assets/JMO Assets/Cartoon FX Remaster/CFXR Prefabs";
        private const string WorldTextMaterialPath = ArtDir + "/WorldText.mat";
        private const string GalleryDressingName = "Gallery Dressing";
        private const string DockDressingName = "Crew Dock Dressing";

        private static readonly Color Sandstone = new Color(0.66f, 0.54f, 0.38f);
        private static readonly Color SandstoneDark = new Color(0.5f, 0.4f, 0.28f);
        private static readonly Color DarkStone = new Color(0.16f, 0.14f, 0.13f);
        private static readonly Color Gold = new Color(0.92f, 0.72f, 0.22f);
        private static readonly Color WarmLight = new Color(1f, 0.82f, 0.6f);

        /// <summary>One dock per crew, in the corners. Shared by the spawn points and their banners.</summary>
        private static readonly Dictionary<Core.TeamID, Vector3> CrewDocks = new Dictionary<Core.TeamID, Vector3>
        {
            { Core.TeamID.TeamAlpha,   new Vector3(-60f, 0f, -60f) },
            { Core.TeamID.TeamBravo,   new Vector3( 60f, 0f, -60f) },
            { Core.TeamID.TeamCharlie, new Vector3(-60f, 0f,  62f) },
            { Core.TeamID.TeamDelta,   new Vector3( 60f, 0f,  62f) },
        };

        /// <summary>Which gun pack prefab stands in for which weapon. Only empty slots are filled.</summary>
        private static readonly Dictionary<string, string> WeaponModels = new Dictionary<string, string>
        {
            { "Assault Rifle", "P_AR02" },
            { "Suppressed SMG", "P_SMG01" },
            { "Breacher Shotgun", "P_SG02" },
            { "Marksman Rifle", "P_DMR01" },
            { "Service Pistol", "P_P01" },
            { "Grenade Launcher", "P_GLA01" },
            { "Sticky Charge Thrower", "P_LA01" },
        };

        [MenuItem("Ouroboros/Setup/Apply Art Pass (M4)")]
        public static void ApplyArtPassMenu()
        {
            EnsureFolders();
            ApplyArtAssets();

            // Dress the open scene too, when it is a Freeport built before this pass existed.
            var gallery = GameObject.Find("Gallery (North)");
            if (gallery != null && GameObject.Find(GalleryDressingName) == null)
            {
                DressGallery(gallery.transform.parent);
                DressCrewDocks(gallery.transform.parent);
                LinkGalleryLampsToPower();
                TryBakeNavMesh(); // the arch, columns and plinths are solid
                EditorSceneManager.MarkSceneDirty(gallery.scene);
                Debug.Log("[Ouroboros] Art pass: dressed the Gallery and the crew docks in the open scene. Save the scene to keep it.");
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[Ouroboros] Art pass applied. Weapon models, effects and crew pattern assets are under " + ArtDir + " and the equipment assets.");
        }

        // ------------------------------------------------------------------
        // Assets

        /// <summary>Fills empty art slots on the project's assets. Never overwrites a slot that is already set.</summary>
        private static void ApplyArtAssets()
        {
            AssignWeaponModels();
            FillFeedbackEffects();
            foreach (var team in CrewDocks.Keys)
            {
                CrewMaterialAsset(team, "Cloth", new Vector2(1.5f, 1.5f));
            }
        }

        private static void AssignWeaponModels()
        {
            if (!AssetDatabase.IsValidFolder(GunPackDir))
            {
                Debug.LogWarning("[Ouroboros] Art pass: '" + GunPackDir + "' not found, so weapons keep their code-built stand-in models.");
                return;
            }

            int assigned = 0;
            foreach (var kvp in WeaponModels)
            {
                var data = AssetDatabase.LoadAssetAtPath<Data.EquipmentData>($"{EquipDir}/{kvp.Key.Replace(' ', '_')}.asset");
                if (data == null || data.equipmentPrefab != null) continue;
                var model = AssetDatabase.LoadAssetAtPath<GameObject>($"{GunPackDir}/{kvp.Value}.prefab");
                if (model == null) continue;
                data.equipmentPrefab = model;
                EditorUtility.SetDirty(data);
                assigned++;
            }
            if (assigned > 0) Debug.Log($"[Ouroboros] Art pass: assigned {assigned} weapon models from the SciFi Gun Pack.");
        }

        private static void FillFeedbackEffects()
        {
            var lib = AssetDatabase.LoadAssetAtPath<Data.FeedbackLibrary>(FeedbackPath);
            if (lib == null || !AssetDatabase.IsValidFolder(EffectsDir)) return;

            bool changed = false;
            if (lib.impactVfx == null) { lib.impactVfx = FindEffect("CFXR Sparks Impact", "CFXR2 Hit"); changed |= lib.impactVfx != null; }
            if (lib.explosionVfx == null) { lib.explosionVfx = FindEffect("CFXR Explosion 1", "CFXR4 Explosion Quick"); changed |= lib.explosionVfx != null; }
            if (lib.extractedVfx == null) { lib.extractedVfx = FindEffect("CFXR Firework 1", "CFXR Firework 2"); changed |= lib.extractedVfx != null; }
            if (changed)
            {
                EditorUtility.SetDirty(lib);
                Debug.Log("[Ouroboros] Art pass: filled empty effect slots on the Feedback Library from Cartoon FX Remaster.");
            }
        }

        /// <summary>First effect prefab found whose file name matches one of <paramref name="names"/> exactly.</summary>
        private static GameObject FindEffect(params string[] names)
        {
            foreach (string name in names)
            {
                foreach (string guid in AssetDatabase.FindAssets("\"" + name + "\" t:Prefab", new[] { EffectsDir }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (Path.GetFileNameWithoutExtension(path) != name) continue;
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null) return prefab;
                }
            }
            return null;
        }

        /// <summary>The crew's pattern as a saved texture, created on first use.</summary>
        private static Texture2D CrewPatternAsset(Core.TeamID team)
        {
            string path = $"{ArtDir}/Pattern_{Core.TeamUtil.DisplayName(team)}.png";
            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture != null) return texture;

            var built = Art.PatternTextures.Build(Art.CrewStyle.Get(team));
            File.WriteAllBytes(path, built.EncodeToPNG());
            Object.DestroyImmediate(built);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);

            if (AssetImporter.GetAtPath(path) is TextureImporter importer)
            {
                importer.filterMode = FilterMode.Point;             // woven and printed edges stay crisp
                importer.wrapMode = TextureWrapMode.Repeat;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.mipmapEnabled = true;
                importer.SaveAndReimport();
            }
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        /// <summary>A lit material showing the crew's pattern at a given tiling, saved under the art folder.</summary>
        private static Material CrewMaterialAsset(Core.TeamID team, string kind, Vector2 tiling)
        {
            string path = $"{ArtDir}/{kind}_{Core.TeamUtil.DisplayName(team)}.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;

            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return null;
            material = new Material(shader) { color = Color.white };
            var texture = CrewPatternAsset(team);
            material.mainTexture = texture;
            material.mainTextureScale = tiling;
            if (material.HasProperty("_BaseMap")) { material.SetTexture("_BaseMap", texture); material.SetTextureScale("_BaseMap", tiling); }
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", 0.1f);
            if (material.HasProperty("_Glossiness")) material.SetFloat("_Glossiness", 0.1f);
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static bool s_warnedNoTextShader;

        /// <summary>The depth-tested sign text material, or null when its shader is missing.</summary>
        private static Material WorldTextMaterial(Font font)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>(WorldTextMaterialPath);
            if (material != null) return material;
            var shader = Shader.Find("Ouroboros/World Text");
            if (shader == null)
            {
                if (s_warnedNoTextShader) return null;
                s_warnedNoTextShader = true;
                Debug.LogWarning("[Ouroboros] Art pass: shader 'Ouroboros/World Text' not found, so signs use the default font material and will show through walls.");
                return null;
            }
            material = new Material(shader);
            if (font != null && font.material != null) material.mainTexture = font.material.mainTexture;
            AssetDatabase.CreateAsset(material, WorldTextMaterialPath);
            return material;
        }

        // ------------------------------------------------------------------
        // Scene: the Gallery

        /// <summary>
        /// Dresses the north district as an exhibition hall. Solid pieces (arch, columns, plinths, notice board)
        /// are cover and are carved out of the NavMesh; everything else has no collider.
        /// Clear of: both vault rooms, the divider, the mezzanine and its ramps, lane cover, guard spawns,
        /// patrol points, cameras, the power junction, the turret and the lift.
        /// </summary>
        private static void DressGallery(Transform level)
        {
            if (GameObject.Find(GalleryDressingName) != null) return;
            var dressing = Group(level, GalleryDressingName);
            var north = Quaternion.identity;                 // sign read by someone looking north
            var east = Quaternion.Euler(0f, 90f, 0f);        // ... looking east
            var west = Quaternion.Euler(0f, -90f, 0f);       // ... looking west

            // Floor: warm stone, with a dark threshold and a gold line where the district begins.
            Decor(dressing, "Gallery Floor", new Vector3(0f, 0.015f, 48f), new Vector3(90f, 0.03f, 42f), Sandstone);
            Decor(dressing, "Threshold", new Vector3(0f, 0.035f, 27.4f), new Vector3(90f, 0.03f, 0.8f), DarkStone);
            Decor(dressing, "Threshold Line", new Vector3(0f, 0.055f, 27.4f), new Vector3(90f, 0.03f, 0.14f), Gold);
            // A runner up the middle, from the arch to the divider.
            Decor(dressing, "Runner", new Vector3(0f, 0.035f, 32.5f), new Vector3(2.6f, 0.03f, 8.6f), new Color(0.45f, 0.1f, 0.1f));
            Decor(dressing, "Runner Edge W", new Vector3(-1.36f, 0.055f, 32.5f), new Vector3(0.12f, 0.03f, 8.6f), Gold);
            Decor(dressing, "Runner Edge E", new Vector3(1.36f, 0.055f, 32.5f), new Vector3(0.12f, 0.03f, 8.6f), Gold);

            // Entrance arch on the atrium side, 7 m clear between the pillars.
            Box(dressing, "Arch Pillar W", new Vector3(-4f, 2.5f, 28f), new Vector3(1f, 5f, 1f), DarkStone);
            Box(dressing, "Arch Pillar E", new Vector3(4f, 2.5f, 28f), new Vector3(1f, 5f, 1f), DarkStone);
            Box(dressing, "Arch Lintel", new Vector3(0f, 5.35f, 28f), new Vector3(10f, 0.7f, 1.2f), DarkStone);
            Decor(dressing, "Arch Trim", new Vector3(0f, 4.96f, 27.38f), new Vector3(10f, 0.08f, 0.06f), Gold);
            Sign(dressing, "THE GALLERY", new Vector3(0f, 5.35f, 27.37f), north, 0.4f, Gold);

            // Colonnades down both sides, tied together by a beam.
            foreach (float side in new[] { -1f, 1f })
            {
                foreach (float z in new[] { 32f, 40f, 48f })
                {
                    Box(dressing, "Column", new Vector3(side * 30f, 2.5f, z), new Vector3(0.9f, 5f, 0.9f), SandstoneDark);
                    Decor(dressing, "Column Cap", new Vector3(side * 30f, 5.08f, z), new Vector3(1.3f, 0.16f, 1.3f), Gold);
                    Decor(dressing, "Column Foot", new Vector3(side * 30f, 0.12f, z), new Vector3(1.2f, 0.24f, 1.2f), DarkStone);
                }
                Decor(dressing, "Colonnade Beam", new Vector3(side * 30f, 5.36f, 40f), new Vector3(1.1f, 0.4f, 17.3f), DarkStone);
            }

            // Show pieces. The real ones are in the vaults; these are what the freeport lets visitors see.
            Plinth(dressing, new Vector3(-30f, 0f, 36f), Data.LootShape.Leopard, Data.LootTier.Legendary, new Vector3(0.57f, 0f, 0f), west);
            Plinth(dressing, new Vector3(-30f, 0f, 44f), Data.LootShape.Harp, Data.LootTier.Rare, new Vector3(0.57f, 0f, 0f), west);
            Plinth(dressing, new Vector3(30f, 0f, 36f), Data.LootShape.Sword, Data.LootTier.Legendary, new Vector3(-0.57f, 0f, 0f), east);
            Plinth(dressing, new Vector3(30f, 0f, 44f), Data.LootShape.Astrolabe, Data.LootTier.Rare, new Vector3(-0.57f, 0f, 0f), east);
            Plinth(dressing, new Vector3(-7f, 0f, 36f), Data.LootShape.Plaque, Data.LootTier.Rare, new Vector3(0f, 0f, -0.57f), north);
            Plinth(dressing, new Vector3(7f, 0f, 36f), Data.LootShape.Manuscript, Data.LootTier.Rare, new Vector3(0f, 0f, -0.57f), north);

            // Notice board by the entrance. It is also a piece of cover.
            Box(dressing, "Notice Board", new Vector3(-11f, 1.1f, 29.5f), new Vector3(2.8f, 2.2f, 0.2f), DarkStone);
            Sign(dressing, "OUROBOROS FREEPORT", new Vector3(-11f, 1.85f, 29.38f), north, 0.17f, Gold);
            Sign(dressing, "Works held in trust for their owners.", new Vector3(-11f, 1.45f, 29.38f), north, 0.09f, Color.white);
            Sign(dressing, "Ownership enquiries are not accepted.", new Vector3(-11f, 1.2f, 29.38f), north, 0.09f, Color.white);
            Sign(dressing, "Lots 114 to 131 are not on display.", new Vector3(-11f, 0.95f, 29.38f), north, 0.09f, new Color(1f, 1f, 1f, 0.7f));

            // House banners on the north wall, above the mezzanine.
            foreach (float x in new[] { -32f, -16f, 0f, 16f, 32f })
            {
                Decor(dressing, "House Banner", new Vector3(x, 3.6f, 69.44f), new Vector3(2.6f, 4.2f, 0.06f), DarkStone);
                Decor(dressing, "Banner Bar Top", new Vector3(x, 5.6f, 69.4f), new Vector3(2.9f, 0.12f, 0.1f), Gold);
                Decor(dressing, "Banner Bar Bottom", new Vector3(x, 1.6f, 69.4f), new Vector3(2.9f, 0.12f, 0.1f), Gold);
                Sign(dressing, "FREEPORT", new Vector3(x, 3.6f, 69.38f), north, 0.3f, Gold);
            }

            // Warm fill so the district reads differently from the cold grey of the rest of the map.
            WarmLamp(dressing, "Gallery Fill W", new Vector3(-15f, 5.2f, 32f), 22f, 1.3f);
            WarmLamp(dressing, "Gallery Fill E", new Vector3(15f, 5.2f, 32f), 22f, 1.3f);
        }

        /// <summary>
        /// Puts the Gallery's lamps on the Gallery power junction, so a blackout there still darkens the district.
        /// Safe to call repeatedly: lamps already on the list are not added twice.
        /// </summary>
        private static void LinkGalleryLampsToPower()
        {
            var dressing = GameObject.Find(GalleryDressingName);
            var junction = GameObject.Find("Gallery Power");
            var node = junction != null ? junction.GetComponent<Interaction.PowerNode>() : null;
            if (dressing == null || node == null) return;

            var so = new SerializedObject(node);
            var lights = so.FindProperty("lights");
            if (lights == null || !lights.isArray) return;

            var known = new HashSet<Object>();
            for (int i = 0; i < lights.arraySize; i++) known.Add(lights.GetArrayElementAtIndex(i).objectReferenceValue);
            foreach (var lamp in dressing.GetComponentsInChildren<Light>(true))
            {
                if (!known.Add(lamp)) continue;
                lights.arraySize++;
                lights.GetArrayElementAtIndex(lights.arraySize - 1).objectReferenceValue = lamp;
            }
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(node);
        }

        /// <summary>A stone plinth with a show piece on top, a lamp over it and a name plate on one face.</summary>
        private static void Plinth(Transform parent, Vector3 floor, Data.LootShape shape, Data.LootTier tier, Vector3 plateOffset, Quaternion plateFacing)
        {
            var plinth = Box(parent, "Plinth (" + shape + ")", floor + Vector3.up * 0.5f, new Vector3(1.1f, 1f, 1.1f), DarkStone);
            Decor(parent, "Plinth Trim", floor + Vector3.up * 1.03f, new Vector3(1.2f, 0.06f, 1.2f), Gold);

            // The model is built at play time by the component, so the scene only stores two numbers.
            var piece = new GameObject("Show Piece (" + shape + ")");
            piece.transform.SetParent(plinth.transform.parent);
            piece.transform.position = floor + Vector3.up * 1.07f;
            var display = piece.AddComponent<Art.DisplayPiece>();
            display.shape = shape;
            display.tier = tier;

            Sign(parent, ShowName(shape), floor + plateOffset + Vector3.up * 0.72f, plateFacing, 0.075f, Gold);
            Sign(parent, "On loan. Lender not recorded.", floor + plateOffset + Vector3.up * 0.56f, plateFacing, 0.045f, Color.white);
            WarmLamp(parent, "Plinth Lamp", floor + Vector3.up * 3.3f, 6.5f, 2.2f);
        }

        /// <summary>The catalogue name of the first item with this shape, for the name plate.</summary>
        private static string ShowName(Data.LootShape shape)
        {
            var catalog = Data.LootCatalog.CreateDefault();
            string name = shape.ToString();
            foreach (var item in catalog.items)
            {
                if (item != null && item.shape == shape) { name = item.name; break; }
            }
            Object.DestroyImmediate(catalog);
            return name;
        }

        // ------------------------------------------------------------------
        // Scene: crew docks

        /// <summary>A banner in each crew's pattern behind its dock, with the crew's city over it.</summary>
        private static void DressCrewDocks(Transform level)
        {
            if (GameObject.Find(DockDressingName) != null) return;
            var dressing = Group(level, DockDressingName);

            foreach (var kvp in CrewDocks)
            {
                var style = Art.CrewStyle.Get(kvp.Key);
                Vector3 outward = new Vector3(kvp.Value.x, 0f, kvp.Value.z).normalized; // towards the corner
                Vector3 right = Vector3.Cross(Vector3.up, outward);
                Vector3 centre = new Vector3(kvp.Value.x, 0f, kvp.Value.z) + outward * 4.5f; // behind the spawn ring
                var facing = Quaternion.LookRotation(outward); // read by someone looking out at the corner

                var cloth = Decor(dressing, Core.TeamUtil.DisplayName(kvp.Key) + " Banner", centre + Vector3.up * 2.9f, new Vector3(4f, 2.4f, 0.06f), style.Primary);
                cloth.transform.rotation = facing;
                var material = CrewMaterialAsset(kvp.Key, "Banner", new Vector2(4f, 2.4f)); // one repeat per metre
                if (material != null) cloth.GetComponent<Renderer>().sharedMaterial = material;

                Box(dressing, "Banner Post", centre + right * 2.1f + Vector3.up * 2.2f, new Vector3(0.2f, 4.4f, 0.2f), DarkStone);
                Box(dressing, "Banner Post", centre - right * 2.1f + Vector3.up * 2.2f, new Vector3(0.2f, 4.4f, 0.2f), DarkStone);
                var bar = Decor(dressing, "Banner Bar", centre + Vector3.up * 4.2f, new Vector3(4.4f, 0.14f, 0.14f), style.Accent);
                bar.transform.rotation = facing;

                Sign(dressing, Core.TeamUtil.DisplayName(kvp.Key).ToUpperInvariant(), centre - outward * 0.12f + Vector3.up * 4.75f, facing, 0.5f,
                    Color.Lerp(style.Primary, Color.white, 0.35f));

                // A strip of the crew's colour on the floor marks the dock from across the map.
                var mat = Decor(dressing, "Dock Mat", new Vector3(kvp.Value.x, 0.02f, kvp.Value.z), new Vector3(7f, 0.04f, 7f), Color.Lerp(style.Dark, style.Primary, 0.35f));
                mat.transform.rotation = facing;
            }
        }

        // ------------------------------------------------------------------
        // Helpers

        /// <summary>A box that is only there to be looked at: no collider, not static.</summary>
        private static GameObject Decor(Transform parent, string name, Vector3 center, Vector3 size, Color color)
        {
            var box = Box(parent, name, center, size, color);
            box.isStatic = false;
            var collider = box.GetComponent<Collider>();
            if (collider != null) Object.DestroyImmediate(collider);
            return box;
        }

        private static void WarmLamp(Transform parent, string name, Vector3 position, float range, float intensity)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent);
            go.transform.position = position;
            var lamp = go.AddComponent<Light>();
            lamp.type = LightType.Point;
            lamp.color = WarmLight;
            lamp.range = range;
            lamp.intensity = intensity;
            lamp.shadows = LightShadows.None;
        }

        /// <summary>
        /// World-space text. <paramref name="facing"/> is the direction a reader looks to read it.
        /// <paramref name="letterHeight"/> is the approximate height of a capital letter in metres.
        /// </summary>
        private static void Sign(Transform parent, string text, Vector3 position, Quaternion facing, float letterHeight, Color color)
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null) return;

            const int glyphSize = 96;
            var go = new GameObject("Sign: " + text);
            go.transform.SetParent(parent);
            go.transform.SetPositionAndRotation(position, facing);
            var mesh = go.AddComponent<TextMesh>();
            mesh.font = font;
            mesh.text = text;
            mesh.fontSize = glyphSize;
            mesh.characterSize = letterHeight * 10f / (glyphSize * 0.72f); // a line is characterSize * fontSize / 10 tall; capitals are about 72% of it
            mesh.anchor = TextAnchor.MiddleCenter;
            mesh.alignment = TextAlignment.Center;
            mesh.color = color;

            var sign = go.AddComponent<Art.WorldSign>();
            var material = WorldTextMaterial(font);
            if (material != null) SetReference(sign, "material", material); // without it the sign falls back to the font's own material
        }
    }
}
