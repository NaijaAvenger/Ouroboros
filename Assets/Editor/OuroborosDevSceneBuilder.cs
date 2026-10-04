using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Fusion;

namespace Ouroboros.EditorTools
{
    /// <summary>
    /// Phase 0 bring-up helper. One menu click produces everything the README's "Scene Setup" asks for:
    /// a dev GameModeConfig, the player / loot-drop / trap prefabs, and a playable DevArena scene that is
    /// added to Build Settings. Re-running is safe: existing assets are reused, the scene is rebuilt.
    ///
    /// Menu: Ouroboros > Setup > Create Dev Scene (Phase 0)
    /// </summary>
    public static class OuroborosDevSceneBuilder
    {
        private const string Root = "Assets/Ouroboros";
        private const string PrefabDir = Root + "/Prefabs";
        private const string SceneDir = Root + "/Scenes";
        private const string ConfigPath = Root + "/DevGameModeConfig.asset";
        private const string PlayerPrefabPath = PrefabDir + "/Player.prefab";
        private const string LootDropPrefabPath = PrefabDir + "/LootDrop.prefab";
        private const string TrapPrefabPath = PrefabDir + "/ProximityTrap.prefab";
        private const string ScenePath = SceneDir + "/DevArena.unity";
        private const string ClassDir = Root + "/Classes";
        private const string RegistryPath = ClassDir + "/ClassRegistry.asset";
        private const string UIDir = Root + "/UI";
        private const string ThemePath = UIDir + "/HeistRuntimeTheme.tss";
        private const string PanelSettingsPath = UIDir + "/HeistPanelSettings.asset";

        [MenuItem("Ouroboros/Setup/Create Dev Scene (Phase 0)")]
        public static void CreateDevScene()
        {
            EnsureFolders();

            var config = CreateOrLoadConfig();
            var playerPrefab = CreatePlayerPrefab();
            var lootDropPrefab = CreateLootDropPrefab();
            var trapPrefab = CreateTrapPrefab();
            var registry = CreateClassAssets(trapPrefab);
            var panelSettings = CreatePanelSettings();

            BuildScene(config, playerPrefab, lootDropPrefab, registry, panelSettings);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log("[Ouroboros] Dev scene created at " + ScenePath +
                      ". Press Play to host; run a second instance (ParrelSync / build) to join.");
        }

        [MenuItem("Ouroboros/Setup/Create Prefabs Only")]
        public static void CreatePrefabsOnly()
        {
            EnsureFolders();
            CreatePlayerPrefab();
            CreateLootDropPrefab();
            CreateTrapPrefab();
            AssetDatabase.SaveAssets();
        }

        // ------------------------------------------------------------------

        private static void EnsureFolders()
        {
            foreach (var dir in new[] { Root, PrefabDir, SceneDir, ClassDir, UIDir })
            {
                if (!AssetDatabase.IsValidFolder(dir))
                {
                    string parent = Path.GetDirectoryName(dir).Replace('\\', '/');
                    string leaf = Path.GetFileName(dir);
                    AssetDatabase.CreateFolder(parent, leaf);
                }
            }
        }

        private static Data.GameModeConfig CreateOrLoadConfig()
        {
            var cfg = AssetDatabase.LoadAssetAtPath<Data.GameModeConfig>(ConfigPath);
            if (cfg != null) return cfg;

            cfg = ScriptableObject.CreateInstance<Data.GameModeConfig>();
            cfg.modeName = "Dev Extraction Heist";
            cfg.modeDescription = "Short solo-friendly rules for Phase 0 testing.";
            cfg.matchDuration = 300f;
            cfg.preMatchCountdown = 5f;
            cfg.minPlayersToStart = 1;
            cfg.extractionPointActivationDelay = 30f;
            cfg.extractionWindowDuration = 60f;
            cfg.extractionTime = 6f;
            cfg.objectiveRespawnTime = 30f;
            cfg.respawnDelay = 5f;
            cfg.maxRespawns = 10;
            AssetDatabase.CreateAsset(cfg, ConfigPath);
            return cfg;
        }

        private static NetworkObject CreatePlayerPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath);
            if (existing != null) return UpgradePlayerPrefab(existing);

            var go = new GameObject("Player");

            var cc = go.AddComponent<CharacterController>();
            cc.height = 2f;
            cc.radius = 0.4f;
            cc.center = new Vector3(0f, 1f, 0f);

            var model = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            model.name = "Model";
            model.transform.SetParent(go.transform, false);
            model.transform.localPosition = new Vector3(0f, 1f, 0f);
            Object.DestroyImmediate(model.GetComponent<Collider>());

            var camPivot = new GameObject("CameraPivot");
            camPivot.transform.SetParent(go.transform, false);
            camPivot.transform.localPosition = new Vector3(0f, 1.6f, 0f);
            camPivot.AddComponent<Camera>();
            camPivot.AddComponent<AudioListener>();

            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();
            var networkPlayer = go.AddComponent<Network.NetworkPlayer>();
            var controller = go.AddComponent<Player.PlayerController>();
            go.AddComponent<Equipment.EquipmentLoadout>();

            SetReference(controller, "characterController", cc);
            SetReference(controller, "cameraTransform", camPivot.transform);
            SetReference(networkPlayer, "playerModel", model.transform);

            var presentation = go.AddComponent<Player.PlayerPresentation>();
            SetReference(presentation, "modelRenderer", model.GetComponent<Renderer>());

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<NetworkObject>();
        }

        /// <summary>Adds components introduced after the prefab was first generated (idempotent).</summary>
        private static NetworkObject UpgradePlayerPrefab(GameObject prefabAsset)
        {
            var root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
            bool changed = false;

            if (root.GetComponent<Player.PlayerPresentation>() == null)
            {
                var presentation = root.AddComponent<Player.PlayerPresentation>();
                var model = root.transform.Find("Model");
                if (model != null) SetReference(presentation, "modelRenderer", model.GetComponent<Renderer>());
                changed = true;
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            return AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPrefabPath).GetComponent<NetworkObject>();
        }

        // ------------------------------------------------------------------
        // Class data

        private static Data.ClassRegistry CreateClassAssets(NetworkObject trapPrefab)
        {
            var registry = AssetDatabase.LoadAssetAtPath<Data.ClassRegistry>(RegistryPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<Data.ClassRegistry>();
                AssetDatabase.CreateAsset(registry, RegistryPath);
            }

            var list = new List<Data.ClassData>
            {
                ClassAsset(Core.PlayerClassType.Hacker, "Hacker", 85f, 5.5f, 7.8f,
                    "Electronic warfare specialist capable of disabling security systems and hacking enemy equipment.",
                    ("System Hack", 15f, 15f, 5f), ("Disable Camera", 20f, 10f, 5f), ("EMP Blast", 25f, 25f, 4f), ("Data Mine", 30f, 20f, 5f)),
                ClassAsset(Core.PlayerClassType.Saboteur, "Saboteur", 90f, 6f, 8.5f,
                    "Stealth operative skilled in setting traps, sabotaging equipment, and silent elimination.",
                    ("Place Trap", 6f, 10f, 0f), ("Stealth Mode", 25f, 25f, 10f), ("Sabotage", 18f, 15f, 8f), ("Smoke Bomb", 15f, 10f, 8f)),
                ClassAsset(Core.PlayerClassType.Demolitions, "Demolitions", 110f, 4.5f, 6.5f,
                    "Explosives expert capable of breaching reinforced structures and creating area denial zones.",
                    ("Place Explosive", 2f, 5f, 0f), ("Detonate", 4f, 0f, 0f), ("Breaching Charge", 20f, 20f, 0f), ("Incendiary", 18f, 15f, 5f)),
                ClassAsset(Core.PlayerClassType.Agent, "Agent", 100f, 5.2f, 7.5f,
                    "Versatile field operative with balanced combat capabilities and tactical support options.",
                    ("Tactical Shield", 20f, 20f, 8f), ("Damage Boost", 18f, 15f, 6f), ("Recon Drone", 25f, 10f, 6f), ("Flashbang", 12f, 10f, 3f)),
            };

            foreach (var data in list)
            {
                if (data.classType == Core.PlayerClassType.Saboteur && data.trapPrefab == null)
                {
                    data.trapPrefab = trapPrefab;
                    EditorUtility.SetDirty(data);
                }
            }

            registry.classes = list.ToArray();
            EditorUtility.SetDirty(registry);
            return registry;
        }

        private static Data.ClassData ClassAsset(Core.PlayerClassType type, string name, float hp, float speed, float sprint,
            string description, params (string name, float cooldown, float cost, float duration)[] abilities)
        {
            string path = $"{ClassDir}/{name}.asset";
            var data = AssetDatabase.LoadAssetAtPath<Data.ClassData>(path);
            if (data != null) return data; // keep designer edits

            data = ScriptableObject.CreateInstance<Data.ClassData>();
            data.className = name;
            data.description = description;
            data.classType = type;
            data.maxHealth = hp;
            data.maxStamina = 100f;
            data.movementSpeed = speed;
            data.sprintSpeed = sprint;
            data.abilities = new Data.AbilityData[abilities.Length];
            for (int i = 0; i < abilities.Length; i++)
            {
                data.abilities[i] = new Data.AbilityData
                {
                    abilityName = abilities[i].name,
                    cooldown = abilities[i].cooldown,
                    energyCost = abilities[i].cost,
                    duration = abilities[i].duration
                };
            }
            AssetDatabase.CreateAsset(data, path);
            return data;
        }

        // ------------------------------------------------------------------
        // UI Toolkit assets

        private static PanelSettings CreatePanelSettings()
        {
            var settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings != null) return settings;

            if (!File.Exists(ThemePath))
            {
                File.WriteAllText(ThemePath, "@import url(\"unity-theme://default\");\n");
                AssetDatabase.ImportAsset(ThemePath);
            }
            var theme = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);

            settings = ScriptableObject.CreateInstance<PanelSettings>();
            settings.themeStyleSheet = theme;
            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.match = 0.5f;
            AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            return settings;
        }

        private static NetworkObject CreateLootDropPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(LootDropPrefabPath);
            if (existing != null) return existing.GetComponent<NetworkObject>();

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "LootDrop";
            go.transform.localScale = Vector3.one * 0.5f;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Tint(go, new Color(1f, 0.85f, 0.1f));

            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();
            go.AddComponent<GameMode.LootDrop>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, LootDropPrefabPath);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<NetworkObject>();
        }

        private static NetworkObject CreateTrapPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(TrapPrefabPath);
            if (existing != null) return existing.GetComponent<NetworkObject>();

            var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            go.name = "ProximityTrap";
            go.transform.localScale = new Vector3(0.6f, 0.05f, 0.6f);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            Tint(go, new Color(0.9f, 0.2f, 0.2f));

            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();
            go.AddComponent<Interaction.ProximityTrap>();

            var prefab = PrefabUtility.SaveAsPrefabAsset(go, TrapPrefabPath);
            Object.DestroyImmediate(go);
            return prefab.GetComponent<NetworkObject>();
        }

        // ------------------------------------------------------------------

        private static void BuildScene(Data.GameModeConfig config, NetworkObject playerPrefab, NetworkObject lootDropPrefab,
            Data.ClassRegistry registry, PanelSettings panelSettings)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lighting
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // Ground + cover
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(10f, 1f, 10f); // 100 x 100 m
            ground.isStatic = true;
            Tint(ground, new Color(0.25f, 0.27f, 0.3f));

            var cover = new GameObject("Cover");
            var rng = new System.Random(42);
            for (int i = 0; i < 24; i++)
            {
                var block = GameObject.CreatePrimitive(PrimitiveType.Cube);
                block.name = "Block" + i;
                block.transform.SetParent(cover.transform);
                float x = (float)(rng.NextDouble() * 70 - 35), z = (float)(rng.NextDouble() * 70 - 35);
                if (Mathf.Abs(x) < 8f && Mathf.Abs(z) < 20f) { x += 12f; } // keep the objective lane open
                float h = 1.5f + (float)rng.NextDouble() * 2.5f;
                block.transform.position = new Vector3(x, h * 0.5f, z);
                block.transform.localScale = new Vector3(2f + (float)rng.NextDouble() * 3f, h, 2f + (float)rng.NextDouble() * 3f);
                block.isStatic = true;
                Tint(block, new Color(0.45f, 0.45f, 0.5f));
            }

            // Managers (scene NetworkObject)
            var managers = new GameObject("GameManagers");
            managers.AddComponent<NetworkObject>();
            managers.AddComponent<Network.TeamManager>();
            var gameMode = managers.AddComponent<GameMode.ExtractionHeistGameMode>();
            SetReference(gameMode, "config", config);
            SetReference(gameMode, "lootDropPrefabObject", lootDropPrefab);

            // Session
            var sessionGo = new GameObject("GameSessionManager");
            var session = sessionGo.AddComponent<Network.GameSessionManager>();
            SetReference(session, "playerPrefabObject", playerPrefab);
            SetReference(session, "classRegistry", registry);
            var devHud = sessionGo.AddComponent<UI.DevHUD>();
            SetBool(devHud, "visible", false); // F1 brings the debug overlay back

            // HUD (UI Toolkit)
            var hudGo = new GameObject("HUD");
            var doc = hudGo.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            hudGo.AddComponent<UI.HeistHUD>();

            // Spawn points: one per team in each corner, facing the centre
            var spawns = new GameObject("SpawnPoints");
            var corners = new Dictionary<Core.TeamID, Vector3>
            {
                { Core.TeamID.TeamAlpha,   new Vector3(-38f, 0f, -38f) },
                { Core.TeamID.TeamBravo,   new Vector3( 38f, 0f, -38f) },
                { Core.TeamID.TeamCharlie, new Vector3(-38f, 0f,  38f) },
                { Core.TeamID.TeamDelta,   new Vector3( 38f, 0f,  38f) },
            };
            foreach (var kvp in corners)
            {
                var sp = new GameObject(kvp.Key + " Spawn");
                sp.transform.SetParent(spawns.transform);
                sp.transform.position = kvp.Value;
                sp.transform.rotation = Quaternion.LookRotation(-kvp.Value.normalized);
                var point = sp.AddComponent<Player.TeamSpawnPoint>();
                SetEnum(point, "team", (int)kvp.Key);
                SetFloat(point, "radius", 2.5f);
            }

            // Extraction points: east and west
            var extractions = new GameObject("ExtractionPoints");
            foreach (var pos in new[] { new Vector3(42f, 0f, 0f), new Vector3(-42f, 0f, 0f) })
            {
                var ep = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                ep.name = "ExtractionPoint";
                ep.transform.SetParent(extractions.transform);
                ep.transform.position = pos + Vector3.up * 0.05f;
                ep.transform.localScale = new Vector3(10f, 0.05f, 10f);
                Object.DestroyImmediate(ep.GetComponent<Collider>());
                Tint(ep, new Color(0.1f, 0.9f, 0.5f, 0.6f));
                ep.AddComponent<NetworkObject>();
                var point = ep.AddComponent<GameMode.ExtractionPoint>();
                SetFloat(point, "extractionRadius", 5f);
            }

            // Loot objectives down the middle
            var objectives = new GameObject("Objectives");
            int n = 0;
            foreach (var pos in new[] { Vector3.zero, new Vector3(0f, 0f, 18f), new Vector3(0f, 0f, -18f) })
            {
                var vault = GameObject.CreatePrimitive(PrimitiveType.Cube);
                vault.name = "Vault " + (++n);
                vault.transform.SetParent(objectives.transform);
                vault.transform.position = pos + Vector3.up * 0.75f;
                vault.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
                Tint(vault, new Color(0.95f, 0.75f, 0.2f));
                vault.AddComponent<NetworkObject>();
                var obj = vault.AddComponent<GameMode.LootObjective>();
                SetString(obj, "objectiveName", vault.name);
                SetFloat(obj, "interactRadius", 3.5f);
                SetFloat(obj, "captureTime", 6f);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
        }

        private static void AddToBuildSettings(string scenePath)
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var s in scenes)
            {
                if (s.path == scenePath) { s.enabled = true; EditorBuildSettings.scenes = scenes.ToArray(); return; }
            }
            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        // ------------------------------------------------------------------
        // Serialized-field helpers (fields are private [SerializeField] by design)

        private static void SetReference(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogWarning($"[Ouroboros] Field '{field}' not found on {target.GetType().Name}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetFloat(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetEnum(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Object target, string field, string value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.stringValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void Tint(GameObject go, Color color)
        {
            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) return;
            var shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null) return;
            var mat = new Material(shader) { color = color };
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            renderer.sharedMaterial = mat;
        }
    }
}
