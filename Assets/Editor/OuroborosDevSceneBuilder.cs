using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.EventSystems;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem.UI;
#endif
using Fusion;
using Fusion.Editor;

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
        private const string FeedbackPath = Root + "/FeedbackLibrary.asset";
        private const string EquipDir = Root + "/Equipment";
        private const string EquipRegistryPath = EquipDir + "/EquipmentRegistry.asset";
        private const string ProjectilePrefabPath = PrefabDir + "/Projectile.prefab";

        [MenuItem("Ouroboros/Setup/Create Dev Scene (Phase 0)")]
        public static void CreateDevScene()
        {
            EnsureFolders();

            var config = CreateOrLoadConfig();
            CreatePlayerPrefab();
            CreateLootDropPrefab();
            var trapPrefab = CreateTrapPrefab();
            var registry = CreateClassAssets(trapPrefab);
            CreateProjectilePrefab();
            var panelSettings = CreatePanelSettings();
            var feedback = CreateFeedbackLibrary();

            // Let Fusion's importer label + bake the prefabs, then take FRESH references: the objects returned by
            // SaveAsPrefabAsset can be replaced during that re-import, which previously left scene fields empty.
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            var playerPrefab = LoadNetworkObject(PlayerPrefabPath);
            var lootDropPrefab = LoadNetworkObject(LootDropPrefabPath);
            var projectilePrefab = LoadNetworkObject(ProjectilePrefabPath);
            var trapPrefabFresh = LoadNetworkObject(TrapPrefabPath);

            var equipment = CreateEquipmentAssets(projectilePrefab);
            AssignStartingEquipment(registry, equipment);
            foreach (var cls in registry.classes)
            {
                if (cls != null && cls.classType == Core.PlayerClassType.Saboteur && cls.trapPrefab == null) { cls.trapPrefab = trapPrefabFresh; EditorUtility.SetDirty(cls); }
            }

            BuildScene(config, playerPrefab, lootDropPrefab, registry, panelSettings, feedback, equipment);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            NetworkProjectConfigUtilities.RebuildPrefabTable();

            Debug.Log("[Ouroboros] Dev scene ready at " + ScenePath +
                      ". Press Play to host; run a second instance (ParrelSync / build) to join. Re-running this menu updates references without wiping the scene.");
        }

        private static NetworkObject LoadNetworkObject(string path)
        {
            var go = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            var no = go != null ? go.GetComponent<NetworkObject>() : null;
            if (no == null) Debug.LogError($"[Ouroboros] Could not load NetworkObject prefab at {path}");
            return no;
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
            foreach (var dir in new[] { Root, PrefabDir, SceneDir, ClassDir, UIDir, EquipDir })
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
            cfg.autoStart = false; // show the lobby / class picker until the host presses "Start match now"
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
            var playerCam = camPivot.AddComponent<Camera>();
            playerCam.depth = 10f; // renders above the spectator camera
            camPivot.AddComponent<AudioListener>();

            var netObj = go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();
            var networkPlayer = go.AddComponent<Network.NetworkPlayer>();
            go.AddComponent<Equipment.NetworkLoadout>();
            var controller = go.AddComponent<Player.PlayerController>();
            go.AddComponent<Equipment.EquipmentLoadout>();

            SetReference(controller, "characterController", cc);
            SetReference(controller, "cameraTransform", camPivot.transform);
            SetReference(networkPlayer, "playerModel", model.transform);

            var presentation = go.AddComponent<Player.PlayerPresentation>();
            SetReference(presentation, "modelRenderer", model.GetComponent<Renderer>());
            go.AddComponent<Player.PlayerFeedback>();

            PrefabUtility.SaveAsPrefabAsset(go, PlayerPrefabPath);
            Object.DestroyImmediate(go);
            return LoadNetworkObject(PlayerPrefabPath);
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
            if (root.GetComponent<Player.PlayerFeedback>() == null)
            {
                root.AddComponent<Player.PlayerFeedback>();
                changed = true;
            }
            if (root.GetComponent<Equipment.NetworkLoadout>() == null)
            {
                root.AddComponent<Equipment.NetworkLoadout>();
                changed = true;
            }
            if (root.GetComponent<Equipment.EquipmentLoadout>() == null)
            {
                root.AddComponent<Equipment.EquipmentLoadout>();
                changed = true;
            }

            if (changed) PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            PrefabUtility.UnloadPrefabContents(root);
            return LoadNetworkObject(PlayerPrefabPath);
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
        // Equipment

        private static NetworkObject CreateProjectilePrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(ProjectilePrefabPath);
            if (existing != null) return existing.GetComponent<NetworkObject>();

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Projectile";
            go.transform.localScale = Vector3.one * 0.25f;
            Object.DestroyImmediate(go.GetComponent<Collider>()); // sweeps its own path; must not hit itself
            Tint(go, new Color(0.3f, 0.3f, 0.3f));
            go.AddComponent<NetworkObject>();
            go.AddComponent<NetworkTransform>();
            go.AddComponent<Equipment.Projectile>();

            PrefabUtility.SaveAsPrefabAsset(go, ProjectilePrefabPath);
            Object.DestroyImmediate(go);
            return LoadNetworkObject(ProjectilePrefabPath);
        }

        private static Data.EquipmentRegistry CreateEquipmentAssets(NetworkObject projectilePrefab)
        {
            var registry = AssetDatabase.LoadAssetAtPath<Data.EquipmentRegistry>(EquipRegistryPath);
            if (registry == null)
            {
                registry = ScriptableObject.CreateInstance<Data.EquipmentRegistry>();
                AssetDatabase.CreateAsset(registry, EquipRegistryPath);
            }

            var items = new List<Data.EquipmentData>
            {
                Weapon("Assault Rifle", Equipment.EquipmentSlotType.Primary, dmg: 14f, cd: 0.1f, auto: true, mag: 30, reserve: 90, reload: 1.8f, spread: 1.5f, pellets: 1, range: 80f,
                    "Reliable automatic rifle. Good at everything, great at nothing."),
                Weapon("Suppressed SMG", Equipment.EquipmentSlotType.Primary, dmg: 10f, cd: 0.07f, auto: true, mag: 25, reserve: 100, reload: 1.5f, spread: 2.5f, pellets: 1, range: 45f,
                    "Fast and quiet. Made for Saboteurs working up close.", Core.PlayerClassType.Saboteur, Core.PlayerClassType.Agent),
                Weapon("Breacher Shotgun", Equipment.EquipmentSlotType.Primary, dmg: 9f, cd: 0.8f, auto: false, mag: 6, reserve: 24, reload: 2.4f, spread: 6f, pellets: 8, range: 25f,
                    "Eight pellets of persuasion.", Core.PlayerClassType.Demolitions, Core.PlayerClassType.Agent),
                Weapon("Marksman Rifle", Equipment.EquipmentSlotType.Primary, dmg: 45f, cd: 0.6f, auto: false, mag: 8, reserve: 32, reload: 2.2f, spread: 0.2f, pellets: 1, range: 150f,
                    "Precision over volume.", Core.PlayerClassType.Hacker, Core.PlayerClassType.Agent),
                Weapon("Service Pistol", Equipment.EquipmentSlotType.Secondary, dmg: 18f, cd: 0.2f, auto: false, mag: 12, reserve: 48, reload: 1.2f, spread: 1f, pellets: 1, range: 50f,
                    "Standard sidearm."),
                Launcher("Grenade Launcher", projectilePrefab, dmg: 60f, cd: 1.2f, mag: 3, reserve: 9, reload: 2.5f, speed: 22f, gravity: -9.81f, radius: 4f, fuse: 0f,
                    "Impact-fused grenades. Mind the splash.", Core.PlayerClassType.Demolitions),
                Launcher("Sticky Charge Thrower", projectilePrefab, dmg: 70f, cd: 1.5f, mag: 2, reserve: 6, reload: 2.5f, speed: 14f, gravity: -9.81f, radius: 3.5f, fuse: 2.5f,
                    "Lobbed charge with a short fuse.", Core.PlayerClassType.Demolitions, Core.PlayerClassType.Saboteur),
                Passive("Light Armor", Equipment.EquipmentSlotType.Armor, defense: 0.15f, speed: -0.05f, damage: 0f, "Take 15% less damage, move 5% slower."),
                Passive("Stim Rig", Equipment.EquipmentSlotType.Accessory, defense: 0f, speed: 0.08f, damage: 0f, "Move 8% faster."),
                Passive("Hollow Points", Equipment.EquipmentSlotType.Accessory, defense: 0f, speed: 0f, damage: 0.1f, "Deal 10% more damage."),
            };

            registry.items = items.ToArray();
            EditorUtility.SetDirty(registry);
            return registry;
        }

        private static Data.EquipmentData LoadOrCreateEquipment(string name, out bool created)
        {
            string path = $"{EquipDir}/{name.Replace(' ', '_')}.asset";
            var data = AssetDatabase.LoadAssetAtPath<Data.EquipmentData>(path);
            created = data == null;
            if (created)
            {
                data = ScriptableObject.CreateInstance<Data.EquipmentData>();
                AssetDatabase.CreateAsset(data, path);
            }
            return data;
        }

        private static Data.EquipmentData Weapon(string name, Equipment.EquipmentSlotType slot, float dmg, float cd, bool auto, int mag, int reserve,
            float reload, float spread, int pellets, float range, string description, params Core.PlayerClassType[] allowed)
        {
            var d = LoadOrCreateEquipment(name, out bool created);
            if (!created) return d; // keep designer edits
            d.equipmentName = name; d.description = description; d.slotType = slot; d.kind = Data.EquipmentKind.HitscanWeapon;
            d.damage = dmg; d.cooldown = cd; d.automatic = auto; d.magazineSize = mag; d.reserveAmmo = reserve; d.reloadTime = reload;
            d.spreadDegrees = spread; d.pelletCount = pellets; d.range = range; d.allowedClasses = allowed;
            EditorUtility.SetDirty(d);
            return d;
        }

        private static Data.EquipmentData Launcher(string name, NetworkObject projectile, float dmg, float cd, int mag, int reserve, float reload,
            float speed, float gravity, float radius, float fuse, string description, params Core.PlayerClassType[] allowed)
        {
            var d = LoadOrCreateEquipment(name, out bool created);
            if (!created) return d;
            d.equipmentName = name; d.description = description; d.slotType = Equipment.EquipmentSlotType.Secondary; d.kind = Data.EquipmentKind.ProjectileWeapon;
            d.damage = dmg; d.cooldown = cd; d.automatic = false; d.magazineSize = mag; d.reserveAmmo = reserve; d.reloadTime = reload;
            d.projectilePrefab = projectile; d.projectileSpeed = speed; d.projectileGravity = gravity; d.explosionRadius = radius; d.fuseTime = fuse;
            d.allowedClasses = allowed;
            EditorUtility.SetDirty(d);
            return d;
        }

        private static Data.EquipmentData Passive(string name, Equipment.EquipmentSlotType slot, float defense, float speed, float damage, string description)
        {
            var d = LoadOrCreateEquipment(name, out bool created);
            if (!created) return d;
            d.equipmentName = name; d.description = description; d.slotType = slot; d.kind = Data.EquipmentKind.PassiveGear;
            d.cooldown = 0f; d.magazineSize = 0; d.defenseModifier = defense; d.speedModifier = speed; d.damageModifier = damage;
            EditorUtility.SetDirty(d);
            return d;
        }

        /// <summary>Fills empty ClassData.startingEquipment so every class spawns armed.</summary>
        private static void AssignStartingEquipment(Data.ClassRegistry classes, Data.EquipmentRegistry equipment)
        {
            Data.EquipmentData Find(string n) { foreach (var i in equipment.items) if (i != null && i.equipmentName == n) return i; return null; }

            foreach (var cls in classes.classes)
            {
                if (cls == null || (cls.startingEquipment != null && cls.startingEquipment.Length > 0)) continue;
                switch (cls.classType)
                {
                    case Core.PlayerClassType.Hacker:      cls.startingEquipment = new[] { Find("Marksman Rifle"), Find("Service Pistol"), Find("Stim Rig") }; break;
                    case Core.PlayerClassType.Saboteur:    cls.startingEquipment = new[] { Find("Suppressed SMG"), Find("Service Pistol"), Find("Hollow Points") }; break;
                    case Core.PlayerClassType.Demolitions: cls.startingEquipment = new[] { Find("Breacher Shotgun"), Find("Grenade Launcher"), Find("Light Armor") }; break;
                    default:                               cls.startingEquipment = new[] { Find("Assault Rifle"), Find("Service Pistol"), Find("Light Armor") }; break;
                }
                EditorUtility.SetDirty(cls);
            }
        }

        private static Data.FeedbackLibrary CreateFeedbackLibrary()
        {
            var lib = AssetDatabase.LoadAssetAtPath<Data.FeedbackLibrary>(FeedbackPath);
            if (lib != null) return lib;
            lib = ScriptableObject.CreateInstance<Data.FeedbackLibrary>();
            AssetDatabase.CreateAsset(lib, FeedbackPath); // clips/prefabs left empty: assign your assets here
            return lib;
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

            PrefabUtility.SaveAsPrefabAsset(go, LootDropPrefabPath);
            Object.DestroyImmediate(go);
            return LoadNetworkObject(LootDropPrefabPath);
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

            PrefabUtility.SaveAsPrefabAsset(go, TrapPrefabPath);
            Object.DestroyImmediate(go);
            return LoadNetworkObject(TrapPrefabPath);
        }

        // ------------------------------------------------------------------

        /// <summary>
        /// Creates the DevArena scene on first run; on later runs opens it and only adds what is missing and
        /// re-applies asset references, so hand-made edits (and hand-assigned prefabs) survive.
        /// </summary>
        private static void BuildScene(Data.GameModeConfig config, NetworkObject playerPrefab, NetworkObject lootDropPrefab,
            Data.ClassRegistry registry, PanelSettings panelSettings, Data.FeedbackLibrary feedback, Data.EquipmentRegistry equipment)
        {
            bool exists = File.Exists(ScenePath);
            Scene scene = exists
                ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single)
                : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            if (!exists || Object.FindFirstObjectByType<Player.TeamSpawnPoint>() == null)
            {
                BuildLevel();
            }

            // Managers (scene NetworkObject)
            var teamManager = Object.FindFirstObjectByType<Network.TeamManager>();
            GameObject managers = teamManager != null ? teamManager.gameObject : new GameObject("GameManagers");
            if (managers.GetComponent<NetworkObject>() == null) managers.AddComponent<NetworkObject>();
            if (teamManager == null) managers.AddComponent<Network.TeamManager>();
            var gameMode = managers.GetComponent<GameMode.ExtractionHeistGameMode>() ?? managers.AddComponent<GameMode.ExtractionHeistGameMode>();
            SetReference(gameMode, "config", config);
            SetReference(gameMode, "lootDropPrefabObject", lootDropPrefab);

            // Session
            var session = Object.FindFirstObjectByType<Network.GameSessionManager>();
            if (session == null) session = new GameObject("GameSessionManager").AddComponent<Network.GameSessionManager>();
            if (playerPrefab == null) Debug.LogError("[Ouroboros] Player prefab reference is null; GameSessionManager.playerPrefabObject will be empty.");
            SetReference(session, "playerPrefabObject", playerPrefab);
            SetReference(session, "classRegistry", registry);
            SetReference(session, "feedbackLibrary", feedback);
            SetReference(session, "equipmentRegistry", equipment);
            var devHud = session.GetComponent<UI.DevHUD>();
            if (devHud == null) { devHud = session.gameObject.AddComponent<UI.DevHUD>(); SetBool(devHud, "visible", false); }

            // Spectator camera: renders the arena until the local player spawns (and again if they despawn)
            var spectator = Object.FindFirstObjectByType<Player.SpectatorCamera>();
            if (spectator == null)
            {
                var camGo = new GameObject("SpectatorCamera");
                camGo.tag = "MainCamera";
                camGo.transform.position = new Vector3(0f, 45f, -60f);
                camGo.transform.rotation = Quaternion.Euler(35f, 0f, 0f);
                camGo.AddComponent<Camera>();
                camGo.AddComponent<AudioListener>();
                spectator = camGo.AddComponent<Player.SpectatorCamera>();
            }
            SetReference(session, "spectatorCamera", spectator);

            // HUD (UI Toolkit)
            var hud = Object.FindFirstObjectByType<UI.HeistHUD>();
            GameObject hudGo = hud != null ? hud.gameObject : new GameObject("HUD");
            var doc = hudGo.GetComponent<UIDocument>() ?? hudGo.AddComponent<UIDocument>();
            doc.panelSettings = panelSettings;
            SetReference(doc, "m_PanelSettings", panelSettings); // the property alone did not persist into the saved scene
            if (panelSettings == null || panelSettings.themeStyleSheet == null)
            {
                Debug.LogError("[Ouroboros] Panel settings / theme missing: delete Assets/Ouroboros/UI and re-run the setup menu.");
            }
            if (hud == null) hudGo.AddComponent<UI.HeistHUD>();
            if (hudGo.GetComponent<UI.ClassPickerUI>() == null) hudGo.AddComponent<UI.ClassPickerUI>();
            if (hudGo.GetComponent<UI.MatchFeedback>() == null) hudGo.AddComponent<UI.MatchFeedback>();

            // Event system so UI Toolkit buttons receive pointer input (Input System module when available)
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var esGo = new GameObject("EventSystem");
                esGo.AddComponent<EventSystem>();
#if ENABLE_INPUT_SYSTEM
                esGo.AddComponent<InputSystemUIInputModule>();
#else
                esGo.AddComponent<StandaloneInputModule>();
#endif
            }

            Directory.CreateDirectory(Path.GetDirectoryName(ScenePath));
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
        }

        private static void BuildLevel()
        {
            // Lighting
            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

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
