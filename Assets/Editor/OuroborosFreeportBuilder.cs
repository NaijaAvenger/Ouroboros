using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;
using Fusion;
using Fusion.Editor;

namespace Ouroboros.EditorTools
{
    /// <summary>
    /// Builds the Freeport blockout (v0.10, milestone M2): the first map laid out for "The Run" rather than
    /// generated at random.
    ///
    ///   Centre   The Atrium: a raised plaza reached by four ramps. The central getaway is on top, in the open.
    ///   North    The Gallery: two locked vaults (keycard, passcode) under a mezzanine with a sniper post.
    ///   East     The Archive: a lockdown-override vault, an open server room, the security terminal, a catwalk.
    ///   West     The Docks: container stacks to climb, an ID-card vault, an open cage, the case on a high stack.
    ///   Corners  One dock per crew. Three side exits on the edges pay less than the centre.
    ///
    /// It is grey-box geometry made of cubes: sizes, sightlines and routes are what is being tested here, not
    /// looks. The scene is yours to edit afterwards; re-running the menu item keeps what is there.
    ///
    /// Menu: Ouroboros > Setup > Create Freeport Map (M2)
    /// </summary>
    public static partial class OuroborosDevSceneBuilder
    {
        private const string FreeportScenePath = SceneDir + "/Freeport.unity";
        private const string PendingFreeportKey = "Ouroboros.DevSceneBuilder.PendingFreeportBuild";

        private static readonly Color FloorColor = new Color(0.25f, 0.27f, 0.3f);
        private static readonly Color WallColor = new Color(0.36f, 0.36f, 0.4f);
        private static readonly Color PlatformColor = new Color(0.42f, 0.4f, 0.36f);
        private static readonly Color RampColor = new Color(0.5f, 0.46f, 0.38f);
        private static readonly Color CoverColor = new Color(0.45f, 0.45f, 0.5f);
        private static readonly Color VaultColor = new Color(0.95f, 0.75f, 0.2f);

        [MenuItem("Ouroboros/Setup/Create Freeport Map (M2)")]
        public static void CreateFreeportMap()
        {
            EnsureFolders();
            s_loaded.Clear();
            CreateAssetsStep();
            SessionState.SetBool(PendingFreeportKey, true);
            EditorApplication.delayCall += RunPendingFreeportBuild;
            Debug.Log("[Ouroboros] Assets ready; building the Freeport scene on the next editor update...");
        }

        /// <summary>Deletes the Freeport scene and builds it again from this script. Hand edits to the scene are lost.</summary>
        [MenuItem("Ouroboros/Setup/Recreate Freeport Map (overwrites the scene)")]
        public static void RecreateFreeportMap()
        {
            if (File.Exists(FreeportScenePath)
                && !EditorUtility.DisplayDialog("Recreate Freeport", "This deletes " + FreeportScenePath + " and rebuilds it from the builder script. Hand edits to that scene are lost.", "Rebuild", "Cancel"))
            {
                return;
            }
            // Leave the scene before deleting its file.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(FreeportScenePath);
            CreateFreeportMap();
        }

        [InitializeOnLoadMethod]
        private static void ResumeFreeportAfterReload()
        {
            if (SessionState.GetBool(PendingFreeportKey, false)) EditorApplication.delayCall += RunPendingFreeportBuild;
        }

        private static void RunPendingFreeportBuild()
        {
            if (!SessionState.GetBool(PendingFreeportKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunPendingFreeportBuild;
                return;
            }
            SessionState.SetBool(PendingFreeportKey, false);
            BuildFreeportSceneStep();
        }

        private static void BuildFreeportSceneStep()
        {
            string previousPath = ScenePath;
            var previousLevel = s_levelBuilder;
            ScenePath = FreeportScenePath;
            s_levelBuilder = BuildFreeportLevel;
            try
            {
                var config = LoadAsset<Data.GameModeConfig>(ConfigPath);
                var registry = LoadAsset<Data.ClassRegistry>(RegistryPath);
                var equipment = LoadAsset<Data.EquipmentRegistry>(EquipRegistryPath);
                var panel = LoadAsset<PanelSettings>(PanelSettingsPath);
                var feedback = LoadAsset<Data.FeedbackLibrary>(FeedbackPath);
                var playerPrefab = LoadNetworkObject(PlayerPrefabPath);
                var lootDropPrefab = LoadNetworkObject(LootDropPrefabPath);
                var guardPrefab = LoadNetworkObject(GuardPrefabPath);
                var elitePrefab = LoadNetworkObject(ElitePrefabPath);
                var sniperPrefab = LoadNetworkObject(SniperPrefabPath);
                var casePrefab = LoadNetworkObject(CasePrefabPath);
                var idCardPrefab = LoadNetworkObject(IDCardPrefabPath);

                // Managers, session, cameras and HUD are the same as the dev arena; only the level differs.
                BuildScene(config, playerPrefab, lootDropPrefab, registry, panel, feedback, equipment);
                BuildFreeportContent(guardPrefab, elitePrefab, sniperPrefab, casePrefab, idCardPrefab);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                NetworkProjectConfigUtilities.RebuildPrefabTable();
                Debug.Log("[Ouroboros] Freeport ready at " + FreeportScenePath + ". It is now the first scene in Build Settings. " +
                          "Press Play to host: you and bots fill four crews of three.");
            }
            finally
            {
                ScenePath = previousPath;
                s_levelBuilder = previousLevel;
            }
        }

        // ------------------------------------------------------------------
        // Static level: floor, walls, atrium, district structures, docks, exits

        private static void BuildFreeportLevel()
        {
            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 1.1f;
                lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            }

            // The NavMesh bake looks for an object called "Ground".
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground";
            ground.transform.localScale = new Vector3(14f, 1f, 14f); // 140 x 140 m
            ground.isStatic = true;
            Tint(ground, FloorColor);

            var level = new GameObject("Level").transform;

            // Perimeter
            var perimeter = Group(level, "Perimeter");
            Box(perimeter, "Wall N", new Vector3(0f, 3f, 70f), new Vector3(141f, 6f, 1f), WallColor);
            Box(perimeter, "Wall S", new Vector3(0f, 3f, -70f), new Vector3(141f, 6f, 1f), WallColor);
            Box(perimeter, "Wall E", new Vector3(70f, 3f, 0f), new Vector3(1f, 6f, 141f), WallColor);
            Box(perimeter, "Wall W", new Vector3(-70f, 3f, 0f), new Vector3(1f, 6f, 141f), WallColor);

            BuildAtrium(level);
            BuildGallery(level);
            BuildArchive(level);
            BuildDocks(level);
            BuildLanesCover(level);
            BuildCrewDocksAndExits();
        }

        /// <summary>Centre: a 22 m raised plaza, 3 m up, with a ramp on each side and four pillars for cover on top.</summary>
        private static void BuildAtrium(Transform level)
        {
            var atrium = Group(level, "Atrium");
            const float top = 3f, half = 11f, rampRun = 10f, rampWidth = 6f;

            Box(atrium, "Plaza", new Vector3(0f, top * 0.5f, 0f), new Vector3(half * 2f, top, half * 2f), PlatformColor);
            Ramp(atrium, "Ramp N", new Vector3(0f, 0f, half + rampRun), new Vector3(0f, top, half), rampWidth);
            Ramp(atrium, "Ramp S", new Vector3(0f, 0f, -half - rampRun), new Vector3(0f, top, -half), rampWidth);
            Ramp(atrium, "Ramp E", new Vector3(half + rampRun, 0f, 0f), new Vector3(half, top, 0f), rampWidth);
            Ramp(atrium, "Ramp W", new Vector3(-half - rampRun, 0f, 0f), new Vector3(-half, top, 0f), rampWidth);

            foreach (var corner in new[] { new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f) })
            {
                Box(atrium, "Pillar", new Vector3(corner.x * 7f, top + 1.4f, corner.y * 7f), new Vector3(1.6f, 2.8f, 1.6f), CoverColor);
            }
            // Low walls on the plaza edge between the ramps: cover for whoever holds the getaway, with gaps to shoot through.
            foreach (var side in new[] { new Vector2(1f, 1f), new Vector2(1f, -1f), new Vector2(-1f, 1f), new Vector2(-1f, -1f) })
            {
                Box(atrium, "Parapet", new Vector3(side.x * 7.5f, top + 0.6f, side.y * 10.6f), new Vector3(5f, 1.2f, 0.5f), CoverColor);
                Box(atrium, "Parapet", new Vector3(side.x * 10.6f, top + 0.6f, side.y * 7.5f), new Vector3(0.5f, 1.2f, 5f), CoverColor);
            }
        }

        /// <summary>North: a mezzanine over the vault fronts, reached by ramps at both ends and by climbing crates.</summary>
        private static void BuildGallery(Transform level)
        {
            var gallery = Group(level, "Gallery (North)");
            const float deck = 4f;

            Box(gallery, "Mezzanine", new Vector3(0f, deck - 0.2f, 60f), new Vector3(44f, 0.4f, 8f), PlatformColor);
            Ramp(gallery, "Mezzanine Ramp W", new Vector3(-34f, 0f, 60f), new Vector3(-22f, deck, 60f), 5f);
            Ramp(gallery, "Mezzanine Ramp E", new Vector3(34f, 0f, 60f), new Vector3(22f, deck, 60f), 5f);
            Box(gallery, "Mezzanine Rail", new Vector3(0f, deck + 0.5f, 56.2f), new Vector3(30f, 1f, 0.3f), CoverColor);
            // Crate steps: a mantle route up for anyone who does not want to use a ramp in view of the sniper.
            Box(gallery, "Crate Low", new Vector3(-8f, 1f, 54f), new Vector3(2.5f, 2f, 2.5f), CoverColor);
            Box(gallery, "Crate High", new Vector3(-8f, 2f, 51f), new Vector3(2.5f, 0.6f, 2.5f), CoverColor);
            // A wall between the two vault fronts so a fight at one is not a fight at both.
            Box(gallery, "Divider", new Vector3(0f, 1.75f, 44f), new Vector3(0.6f, 3.5f, 14f), WallColor);
        }

        /// <summary>East: a catwalk along the outer wall and a raised server floor.</summary>
        private static void BuildArchive(Transform level)
        {
            var archive = Group(level, "Archive (East)");
            const float deck = 4f;

            Box(archive, "Catwalk", new Vector3(62f, deck - 0.2f, -4f), new Vector3(5f, 0.4f, 44f), PlatformColor);
            Ramp(archive, "Catwalk Ramp", new Vector3(62f, 0f, 30f), new Vector3(62f, deck, 18f), 4f);
            Box(archive, "Catwalk Rail", new Vector3(59.6f, deck + 0.5f, -4f), new Vector3(0.3f, 1f, 30f), CoverColor);

            // Server floor: one mantle (2 m) above the ground, open on the atrium side.
            Box(archive, "Server Floor", new Vector3(46f, 1f, -22f), new Vector3(14f, 2f, 12f), PlatformColor);
            Ramp(archive, "Server Ramp", new Vector3(46f, 0f, -8f), new Vector3(46f, 2f, -16f), 4f);
            foreach (float x in new[] { 41f, 46f, 51f })
            {
                Box(archive, "Server Rack", new Vector3(x, 3.1f, -26.4f), new Vector3(1.2f, 2.2f, 3f), CoverColor);
            }
            Box(archive, "Screen Wall", new Vector3(36f, 1.75f, 6f), new Vector3(0.6f, 3.5f, 12f), WallColor);
        }

        /// <summary>West: shipping containers, single (climbable) and stacked (reached from a single).</summary>
        private static void BuildDocks(Transform level)
        {
            var docks = Group(level, "Docks (West)");
            var container = new Vector3(6f, 2f, 2.5f);
            var colors = new[] { new Color(0.55f, 0.25f, 0.2f), new Color(0.2f, 0.4f, 0.55f), new Color(0.25f, 0.5f, 0.3f), new Color(0.6f, 0.5f, 0.2f) };
            int n = 0;

            void Container(Vector3 basePos, bool alongZ, int stack)
            {
                for (int level2 = 0; level2 < stack; level2++)
                {
                    var size = alongZ ? new Vector3(container.z, container.y, container.x) : container;
                    Box(docks, "Container", basePos + Vector3.up * (container.y * 0.5f + level2 * container.y), size, colors[n++ % colors.Length]);
                }
            }

            // Two rows with a lane between them; every double stack has a single beside it to climb from.
            Container(new Vector3(-38f, 0f, 8f), false, 1);
            Container(new Vector3(-38f, 0f, 11f), false, 2);
            Container(new Vector3(-50f, 0f, 2f), true, 1);
            Container(new Vector3(-53f, 0f, 2f), true, 2);
            Container(new Vector3(-56f, 0f, 2f), true, 2);
            Container(new Vector3(-40f, 0f, -4f), false, 1);
            Container(new Vector3(-58f, 0f, 14f), false, 1);
            Container(new Vector3(-58f, 0f, -18f), true, 2);
            Container(new Vector3(-55f, 0f, -18f), true, 1);
            Container(new Vector3(-36f, 0f, 26f), true, 1);
            Container(new Vector3(-36f, 0f, -24f), false, 1);
        }

        /// <summary>Cover on the approaches between the crew docks and the districts, so no lane is a clean sightline.</summary>
        private static void BuildLanesCover(Transform level)
        {
            var cover = Group(level, "Lane Cover");
            var spots = new[]
            {
                new Vector3(26f, 0f, 26f), new Vector3(-26f, 0f, 26f), new Vector3(26f, 0f, -26f), new Vector3(-26f, 0f, -26f),
                new Vector3(40f, 0f, 40f), new Vector3(-40f, 0f, 44f), new Vector3(42f, 0f, -44f), new Vector3(-34f, 0f, -46f),
                new Vector3(0f, 0f, -36f), new Vector3(14f, 0f, -48f), new Vector3(-14f, 0f, -48f),
                new Vector3(30f, 0f, 8f), new Vector3(-30f, 0f, -8f), new Vector3(8f, 0f, 30f), new Vector3(-8f, 0f, -30f),
                new Vector3(52f, 0f, 44f), new Vector3(-52f, 0f, 40f), new Vector3(54f, 0f, -52f), new Vector3(-50f, 0f, -52f),
            };
            for (int i = 0; i < spots.Length; i++)
            {
                // Alternate chest-high walls (shoot over, mantle) and full-height blocks (break line of sight).
                bool tall = i % 3 == 0;
                var size = tall ? new Vector3(3f, 3f, 3f) : new Vector3(i % 2 == 0 ? 5f : 1f, 1.5f, i % 2 == 0 ? 1f : 5f);
                Box(cover, tall ? "Block" : "Low Wall", spots[i] + Vector3.up * (size.y * 0.5f), size, CoverColor);
            }
        }

        private static void BuildCrewDocksAndExits()
        {
            // One dock per crew, in the corners, facing the centre.
            var spawns = new GameObject("SpawnPoints");
            var docks = new Dictionary<Core.TeamID, Vector3>
            {
                { Core.TeamID.TeamAlpha,   new Vector3(-60f, 0f, -60f) },
                { Core.TeamID.TeamBravo,   new Vector3( 60f, 0f, -60f) },
                { Core.TeamID.TeamCharlie, new Vector3(-60f, 0f,  62f) },
                { Core.TeamID.TeamDelta,   new Vector3( 60f, 0f,  62f) },
            };
            foreach (var kvp in docks)
            {
                var sp = new GameObject(Core.TeamUtil.DisplayName(kvp.Key) + " Dock");
                sp.transform.SetParent(spawns.transform);
                sp.transform.position = kvp.Value;
                sp.transform.rotation = Quaternion.LookRotation(-kvp.Value.normalized);
                var point = sp.AddComponent<Player.TeamSpawnPoint>();
                SetEnum(point, "team", (int)kvp.Key);
                SetFloat(point, "radius", 3f);
            }

            var exits = new GameObject("ExtractionPoints");
            Exit(exits.transform, "Central Getaway", new Vector3(0f, 3.05f, 0f), 6f, central: true, new Color(1f, 0.8f, 0.2f, 0.7f));
            Exit(exits.transform, "Side Exit South", new Vector3(0f, 0.05f, -64f), 4.5f, central: false, new Color(0.1f, 0.9f, 0.5f, 0.6f));
            Exit(exits.transform, "Side Exit West", new Vector3(-65f, 0.05f, -34f), 4.5f, central: false, new Color(0.1f, 0.9f, 0.5f, 0.6f));
            Exit(exits.transform, "Side Exit East", new Vector3(65f, 0.05f, 36f), 4.5f, central: false, new Color(0.1f, 0.9f, 0.5f, 0.6f));
        }

        private static void Exit(Transform parent, string name, Vector3 position, float radius, bool central, Color color)
        {
            var ep = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            ep.name = name;
            ep.transform.SetParent(parent);
            ep.transform.position = position;
            ep.transform.localScale = new Vector3(radius * 2f, 0.05f, radius * 2f);
            Object.DestroyImmediate(ep.GetComponent<Collider>());
            Tint(ep, color);
            ep.AddComponent<NetworkObject>();
            var point = ep.AddComponent<GameMode.ExtractionPoint>();
            SetFloat(point, "extractionRadius", radius);
            SetBool(point, "isCentral", central);
        }

        // ------------------------------------------------------------------
        // Gameplay content: vaults, locks, guards, cameras, terminal, case

        private static void BuildFreeportContent(NetworkObject guardPrefab, NetworkObject elitePrefab, NetworkObject sniperPrefab,
            NetworkObject casePrefab, NetworkObject idCardPrefab)
        {
            var scene = EditorSceneManager.GetActiveScene();

            var teamManager = Object.FindFirstObjectByType<Network.TeamManager>();
            if (teamManager != null && teamManager.GetComponent<GameMode.AlarmSystem>() == null) teamManager.gameObject.AddComponent<GameMode.AlarmSystem>();
            var gameMode = Object.FindFirstObjectByType<GameMode.ExtractionHeistGameMode>();
            if (gameMode != null) SetReference(gameMode, "idCardPrefabObject", idCardPrefab);
            var hud = Object.FindFirstObjectByType<UI.HeistHUD>();
            if (hud != null && hud.GetComponent<UI.KeypadUI>() == null) hud.gameObject.AddComponent<UI.KeypadUI>();

            if (Object.FindFirstObjectByType<GameMode.LootObjective>() == null)
            {
                var objectives = new GameObject("Vaults").transform;
                var terminalLinks = new List<Object>();

                // North: the Gallery. Rare pieces behind a keycard, the best thing on the map behind a passcode.
                FreeportVault(objectives, "Gallery West", new Vector3(-18f, 0f, 44f), Interaction.VaultLockType.Keycard, Interaction.KeycardColor.Red,
                    Data.LootTier.Rare, items: 2, bonus: false, captureTime: 7f, stages: 1, out _);
                FreeportVault(objectives, "Gallery East", new Vector3(18f, 0f, 44f), Interaction.VaultLockType.Passcode, Interaction.KeycardColor.None,
                    Data.LootTier.Rare, items: 2, bonus: true, captureTime: 8f, stages: 2, out var passcodeDoor);

                // East: the Archive. The override vault is loud by design; the server room is open but exposed.
                var archive = FreeportVault(objectives, "Archive Strongroom", new Vector3(48f, 0f, 12f), Interaction.VaultLockType.Lockdown, Interaction.KeycardColor.None,
                    Data.LootTier.Rare, items: 3, bonus: false, captureTime: 7f, stages: 1, out _);
                var servers = OpenVault(objectives, "Server Room", new Vector3(46f, 2f, -22f), Data.LootTier.Common, items: 3, bonus: true, captureTime: 6f);
                terminalLinks.Add(archive); terminalLinks.Add(servers);

                // West: the Docks. Customs wants a rival's ID card, so somebody has to die first.
                FreeportVault(objectives, "Customs Vault", new Vector3(-46f, 0f, -34f), Interaction.VaultLockType.PlayerCard, Interaction.KeycardColor.None,
                    Data.LootTier.Rare, items: 3, bonus: false, captureTime: 7f, stages: 1, out _);
                OpenVault(objectives, "Dockside Cage", new Vector3(-46f, 0f, 20f), Data.LootTier.Common, items: 4, bonus: false, captureTime: 5f);

                // South: a quick, low-value crack near two crew docks, to get a match moving.
                OpenVault(objectives, "Loading Bay", new Vector3(0f, 0f, -50f), Data.LootTier.Common, items: 3, bonus: false, captureTime: 5f);

                // Red keycard: on the far side of the map from the vault it opens, up on the server floor.
                var key = GameObject.CreatePrimitive(PrimitiveType.Cube);
                key.name = "Keycard (Red)";
                key.transform.position = new Vector3(50f, 2.4f, -19f);
                key.transform.localScale = new Vector3(0.5f, 0.06f, 0.3f);
                Object.DestroyImmediate(key.GetComponent<Collider>());
                Tint(key, Color.red);
                key.AddComponent<NetworkObject>();
                SetEnum(key.AddComponent<Interaction.KeycardPickup>(), "color", (int)Interaction.KeycardColor.Red);

                // Passcode note: up on the docks' double stack, a climb away.
                if (passcodeDoor != null)
                {
                    var note = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    note.name = "Code Note";
                    note.transform.position = new Vector3(-56f, 4.5f, 2f);
                    note.transform.localScale = new Vector3(0.6f, 0.8f, 0.1f);
                    Object.DestroyImmediate(note.GetComponent<Collider>());
                    Tint(note, new Color(0.95f, 0.95f, 0.8f));
                    note.AddComponent<NetworkObject>();
                    SetReference(note.AddComponent<Interaction.CodeNote>(), "door", passcodeDoor);
                }

                // Security terminal on the atrium side of the Archive: calms the alarm and boosts the Archive vaults.
                var term = GameObject.CreatePrimitive(PrimitiveType.Cube);
                term.name = "SecurityTerminal";
                term.transform.position = new Vector3(34f, 0.6f, 2f);
                term.transform.localScale = new Vector3(1f, 1.2f, 0.5f);
                Tint(term, new Color(0.1f, 0.6f, 0.9f));
                term.AddComponent<NetworkObject>();
                SetReferenceArray(term.AddComponent<Interaction.HackTerminal>(), "linkedObjectives", terminalLinks.ToArray());
            }

            if (Object.FindFirstObjectByType<AI.AISpawner>() == null)
            {
                var spawnerGo = new GameObject("GuardSpawner");
                spawnerGo.AddComponent<NetworkObject>();
                var spawner = spawnerGo.AddComponent<AI.AISpawner>();

                Transform Marker(string name, Vector3 pos, Vector3? lookAt = null)
                {
                    var p = new GameObject(name).transform;
                    p.SetParent(spawnerGo.transform);
                    p.position = pos;
                    if (lookAt.HasValue) p.LookAt(lookAt.Value);
                    return p;
                }

                var spawns = new List<Object>
                {
                    Marker("GuardSpawn", new Vector3(0f, 0f, 34f)), Marker("GuardSpawn", new Vector3(34f, 0f, -6f)),
                    Marker("GuardSpawn", new Vector3(-32f, 0f, 4f)), Marker("GuardSpawn", new Vector3(6f, 0f, -26f)),
                    Marker("GuardSpawn", new Vector3(40f, 0f, 30f)), Marker("GuardSpawn", new Vector3(-44f, 0f, -18f)),
                };
                // One loop round the atrium through every district front.
                var patrol = new List<Object>
                {
                    Marker("PatrolPoint", new Vector3(0f, 0f, 30f)), Marker("PatrolPoint", new Vector3(24f, 0f, 24f)),
                    Marker("PatrolPoint", new Vector3(32f, 0f, 0f)), Marker("PatrolPoint", new Vector3(24f, 0f, -24f)),
                    Marker("PatrolPoint", new Vector3(0f, 0f, -30f)), Marker("PatrolPoint", new Vector3(-24f, 0f, -24f)),
                    Marker("PatrolPoint", new Vector3(-32f, 0f, 0f)), Marker("PatrolPoint", new Vector3(-24f, 0f, 24f)),
                };
                // Overwatch: the gallery mezzanine and the archive catwalk both look down on the atrium.
                var posts = new List<Object>
                {
                    Marker("SniperPostAnchor", new Vector3(0f, 5f, 58f), new Vector3(0f, 3f, 0f)),
                    Marker("SniperPostAnchor", new Vector3(62f, 5f, -4f), new Vector3(0f, 3f, 0f)),
                };

                SetReference(spawner, "guardPrefab", guardPrefab);
                SetReference(spawner, "elitePrefab", elitePrefab);
                SetReference(spawner, "sniperPrefab", sniperPrefab);
                SetReferenceArray(spawner, "spawnPoints", spawns.ToArray());
                SetReferenceArray(spawner, "patrolPoints", patrol.ToArray());
                SetReferenceArray(spawner, "sniperPosts", posts.ToArray());
                SetInt(spawner, "initialGuards", 6);
                SetInt(spawner, "maxAlive", 12);
            }

            if (Object.FindFirstObjectByType<Interaction.SecurityCamera>() == null)
            {
                // One camera watching each locked vault's front, plus one on the south ramp of the atrium.
                var cams = new[]
                {
                    (pos: new Vector3(-18f, 3.6f, 34f), look: new Vector3(-18f, 1f, 26f)),
                    (pos: new Vector3(18f, 3.6f, 34f), look: new Vector3(18f, 1f, 26f)),
                    (pos: new Vector3(40f, 3.6f, 12f), look: new Vector3(30f, 1f, 8f)),
                    (pos: new Vector3(-37f, 3.6f, -27f), look: new Vector3(-30f, 1f, -21f)),
                    (pos: new Vector3(7f, 6.2f, -7f), look: new Vector3(0f, 0f, -24f)),
                };
                foreach (var c in cams)
                {
                    var cam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    cam.name = "SecurityCamera";
                    cam.transform.position = c.pos;
                    cam.transform.localScale = new Vector3(0.4f, 0.4f, 0.8f);
                    cam.transform.LookAt(c.look);
                    Object.DestroyImmediate(cam.GetComponent<Collider>());
                    Tint(cam, new Color(0.2f, 0.8f, 1f));
                    cam.AddComponent<NetworkObject>();
                    cam.AddComponent<Interaction.SecurityCamera>();
                }
            }

            // The case: on top of the docks' double stack. Worth the climb, and the climb is in the open.
            if (Object.FindFirstObjectByType<GameMode.LootCase>() == null && casePrefab != null)
            {
                var inst = (GameObject)PrefabUtility.InstantiatePrefab(casePrefab.gameObject, scene);
                inst.transform.position = new Vector3(-58f, 4.3f, -18f);
            }

            BuildFreeportDevices(); // (v0.11, M3)
            BuildFreeportGravityZones(); // (v0.12)

            TryBakeNavMesh();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log("[Ouroboros] Freeport content ready: 7 vaults (keycard, passcode, lockdown, ID card, 3 open), 6 guards + 2 snipers, " +
                      "5 cameras, terminal, case, 1 central getaway and 3 side exits.");
        }

        // ------------------------------------------------------------------
        // Hackable devices (v0.11, milestone M3). Added to an existing Freeport scene on the next run of the menu.

        private static void BuildFreeportDevices()
        {
            if (Object.FindFirstObjectByType<Interaction.PowerNode>() != null) return;
            var devices = new GameObject("Devices").transform;

            // Shutter gates on three approaches to the atrium: close one behind you, or open the one a rival closed.
            Gatehouse(devices, "South Gate", new Vector3(0f, 0f, -40f), alongX: true);
            Gatehouse(devices, "East Gate", new Vector3(28f, 0f, -14f), alongX: false);
            Gatehouse(devices, "West Gate", new Vector3(-28f, 0f, 14f), alongX: false);

            // One power junction per district: cameras and turrets nearby go dark, guards see half as far.
            PowerJunction(devices, "Gallery Power", new Vector3(2f, 0f, 35f));
            PowerJunction(devices, "Archive Power", new Vector3(38f, 0f, -10f));
            PowerJunction(devices, "Docks Power", new Vector3(-33f, 0f, 16f));

            // Lifts: a second way up to each high level, for whoever starts them.
            LiftPad(devices, "Mezzanine Lift", new Vector3(-14f, 0.05f, 53.5f), 4f);
            LiftPad(devices, "Catwalk Lift", new Vector3(57.5f, 0.05f, -23f), 4f);
            LiftPad(devices, "Plaza Lift", new Vector3(13.5f, 0.05f, 13.5f), 3f);

            // Sentry turrets on three vault fronts. Hijack one and it holds the door for you.
            Turret(devices, "Gallery Turret", new Vector3(12f, 0f, 33f));
            Turret(devices, "Archive Turret", new Vector3(41f, 0f, 2f));
            Turret(devices, "Customs Turret", new Vector3(-41f, 0f, -24f));

            Debug.Log("[Ouroboros] Freeport devices ready: 3 shutter gates, 3 power junctions, 3 lifts, 3 sentry turrets.");
        }

        /// <summary>
        /// (v0.12) Two volumes whose gravity can be hacked off: the air over the central getaway, and the
        /// container yard. Added to an existing Freeport scene on the next run of the menu.
        /// </summary>
        private static void BuildFreeportGravityZones()
        {
            if (Object.FindFirstObjectByType<Interaction.GravityZone>() != null) return;
            var parent = new GameObject("Gravity Zones").transform;

            // Over the plaza: whoever holds the getaway can be lifted off it, and so can whoever comes for them.
            GravityControl(parent, "Atrium Gravity", new Vector3(7f, 4.2f, 5.9f), new Vector3(0f, 9f, 0f), new Vector3(24f, 12f, 24f));
            // Over the containers: the quick way to the case and the code note on the high stacks.
            GravityControl(parent, "Docks Gravity", new Vector3(-40f, 1.2f, -5.6f), new Vector3(-48f, 6f, 0f), new Vector3(26f, 12f, 34f));

            Debug.Log("[Ouroboros] Freeport gravity zones ready: Atrium (over the central getaway) and Docks (over the containers).");
        }

        private static void GravityControl(Transform parent, string name, Vector3 controlPosition, Vector3 zoneCenter, Vector3 zoneSize)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent);
            box.transform.position = controlPosition;
            box.transform.localScale = new Vector3(0.6f, 0.6f, 0.4f);
            Tint(box, new Color(0.35f, 0.35f, 0.45f));

            // The volume is shown as its twelve edges while gravity is off, so players can see where it ends.
            var frame = new GameObject(name + " Volume");
            frame.transform.SetParent(parent);
            frame.transform.position = zoneCenter;
            var purple = new Color(0.75f, 0.4f, 1f);
            Vector3 h = zoneSize * 0.5f;
            const float t = 0.12f;
            foreach (float sy in new[] { -1f, 1f })
            {
                foreach (float s in new[] { -1f, 1f })
                {
                    Edge(frame.transform, zoneCenter + new Vector3(0f, sy * h.y, s * h.z), new Vector3(zoneSize.x, t, t), purple);
                    Edge(frame.transform, zoneCenter + new Vector3(s * h.x, sy * h.y, 0f), new Vector3(t, t, zoneSize.z), purple);
                }
            }
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -1f, 1f })
                {
                    Edge(frame.transform, zoneCenter + new Vector3(sx * h.x, 0f, sz * h.z), new Vector3(t, zoneSize.y, t), purple);
                }
            }
            frame.SetActive(false);

            box.AddComponent<NetworkObject>();
            var zone = box.AddComponent<Interaction.GravityZone>();
            SetString(zone, "zoneName", name);
            SetVector3(zone, "zoneCenter", zoneCenter);
            SetVector3(zone, "zoneSize", zoneSize);
            SetReference(zone, "zoneVisual", frame);
            SetReference(zone, "indicator", box.GetComponent<Renderer>());
        }

        private static void Edge(Transform parent, Vector3 center, Vector3 size, Color color)
        {
            var edge = GameObject.CreatePrimitive(PrimitiveType.Cube);
            edge.name = "Edge";
            edge.transform.SetParent(parent);
            edge.transform.position = center;
            edge.transform.localScale = size;
            Object.DestroyImmediate(edge.GetComponent<Collider>()); // visual only: must not block movement, shots or the NavMesh
            Tint(edge, color);
        }

        private static void SetVector3(Object target, string field, Vector3 value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) return;
            prop.vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }

        /// <summary>A 12 m wall with a 4 m doorway that a shutter can close. The control box beside the doorway is what you aim at.</summary>
        private static void Gatehouse(Transform parent, string name, Vector3 center, bool alongX)
        {
            const float height = 3.5f, thick = 0.5f, gap = 4f, segment = 4f;
            var gate = Group(parent, name);
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            Vector3 SizeAlong(float length, float h, float t) => alongX ? new Vector3(length, h, t) : new Vector3(t, h, length);

            foreach (float sign in new[] { -1f, 1f })
            {
                Box(gate, "Gate Wall", center + along * sign * (gap * 0.5f + segment * 0.5f) + Vector3.up * (height * 0.5f), SizeAlong(segment, height, thick), WallColor);
            }
            Box(gate, "Lintel", center + Vector3.up * (height + 0.2f), SizeAlong(gap + segment * 2f, 0.4f, thick), WallColor);

            // The barrier: off while open, carves the NavMesh while closed.
            var barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrier.name = "Barrier";
            barrier.transform.SetParent(gate);
            barrier.transform.position = center + Vector3.up * (height * 0.5f);
            barrier.transform.localScale = SizeAlong(gap, height, thick * 0.8f);
            Tint(barrier, new Color(0.75f, 0.55f, 0.15f));
            var obstacle = barrier.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;

            // The control box sticks out of both faces of the wall so it can be hacked from either side.
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name + " Control";
            box.transform.SetParent(gate);
            box.transform.position = center + along * (gap * 0.5f + 0.5f) + Vector3.up * 1.5f;
            box.transform.localScale = alongX ? new Vector3(0.5f, 0.6f, 1.1f) : new Vector3(1.1f, 0.6f, 0.5f);
            Tint(box, new Color(0.2f, 0.8f, 1f));
            box.AddComponent<NetworkObject>();
            var shutter = box.AddComponent<Interaction.Shutter>();
            SetString(shutter, "shutterName", name);
            SetReference(shutter, "barrier", barrier);
            SetReference(shutter, "navObstacle", obstacle);
        }

        private static void PowerJunction(Transform parent, string name, Vector3 floor)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.position = floor + Vector3.up * 0.8f;
            go.transform.localScale = new Vector3(0.8f, 1.6f, 0.8f);
            Tint(go, new Color(0.3f, 1f, 0.5f));

            // A district light the blackout switches off, so everyone can see what happened.
            var lightGo = new GameObject(name + " Light");
            lightGo.transform.SetParent(parent);
            lightGo.transform.position = floor + Vector3.up * 7f;
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = 32f;
            light.intensity = 2.2f;
            light.color = new Color(1f, 0.92f, 0.75f);

            go.AddComponent<NetworkObject>();
            var node = go.AddComponent<Interaction.PowerNode>();
            SetString(node, "nodeName", name);
            SetReference(node, "indicator", go.GetComponent<Renderer>());
            SetReferenceArray(node, "lights", new Object[] { light });
        }

        private static void LiftPad(Transform parent, string name, Vector3 position, float liftHeight)
        {
            var pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            pad.name = name;
            pad.transform.SetParent(parent);
            pad.transform.position = position;
            pad.transform.localScale = new Vector3(3.2f, 0.05f, 3.2f);
            Object.DestroyImmediate(pad.GetComponent<Collider>());
            Tint(pad, new Color(0.2f, 0.25f, 0.3f));
            pad.AddComponent<NetworkObject>();
            var lift = pad.AddComponent<Interaction.GravLift>();
            SetString(lift, "liftName", name);
            SetFloat(lift, "liftHeight", liftHeight);
            SetReference(lift, "indicator", pad.GetComponent<Renderer>());
        }

        private static void Turret(Transform parent, string name, Vector3 floor)
        {
            // Base keeps its collider: that is what weapons hit.
            var turretBase = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            turretBase.name = name;
            turretBase.transform.SetParent(parent);
            turretBase.transform.position = floor + Vector3.up * 0.55f;
            turretBase.transform.localScale = new Vector3(0.9f, 0.55f, 0.9f);
            Tint(turretBase, new Color(0.22f, 0.22f, 0.26f));

            var head = GameObject.CreatePrimitive(PrimitiveType.Cube);
            head.name = "Head";
            head.transform.SetParent(turretBase.transform);
            head.transform.position = floor + Vector3.up * 1.3f;
            head.transform.localScale = new Vector3(0.5f / 0.9f, 0.35f / 0.55f, 1f / 0.9f); // world size 0.5 x 0.35 x 1.0
            Object.DestroyImmediate(head.GetComponent<Collider>());
            Tint(head, new Color(0.9f, 0.75f, 0.2f));

            turretBase.AddComponent<NetworkObject>();
            var turret = turretBase.AddComponent<Interaction.SentryTurret>();
            SetReference(turret, "head", head.transform);
            SetReference(turret, "indicator", head.GetComponent<Renderer>());
        }

        /// <summary>A walled vault room whose door faces the atrium, with the loot objective inside.</summary>
        private static GameMode.LootObjective FreeportVault(Transform parent, string name, Vector3 center, Interaction.VaultLockType lockType,
            Interaction.KeycardColor color, Data.LootTier tier, int items, bool bonus, float captureTime, int stages, out Interaction.VaultDoor door)
        {
            door = CreateVaultRoom(name, center, lockType, color);
            var interior = VaultObjective(parent, name + " Interior", center + Vector3.up * 0.75f, tier, items, bonus, captureTime);
            SetInt(interior, "stages", Mathf.Max(1, stages));
            SetReference(interior, "gatedBy", door);
            return interior;
        }

        /// <summary>A vault with no door: quick to reach, and you crack it in the open.</summary>
        private static GameMode.LootObjective OpenVault(Transform parent, string name, Vector3 floor, Data.LootTier tier, int items, bool bonus, float captureTime)
        {
            return VaultObjective(parent, name, floor + Vector3.up * 0.75f, tier, items, bonus, captureTime);
        }

        private static GameMode.LootObjective VaultObjective(Transform parent, string name, Vector3 position, Data.LootTier tier, int items, bool bonus, float captureTime)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.position = position;
            cube.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            Tint(cube, tier == Data.LootTier.Common ? VaultColor : Data.LootCatalog.TierColor(tier));
            cube.AddComponent<NetworkObject>();
            var objective = cube.AddComponent<GameMode.LootObjective>();
            SetString(objective, "objectiveName", name);
            SetFloat(objective, "interactRadius", 3.5f);
            SetFloat(objective, "captureTime", captureTime);
            SetInt(objective, "lootItemCount", items);
            SetEnum(objective, "lootTier", (int)tier);
            SetBool(objective, "bonusItemFromNextTier", bonus);
            SetBool(objective, "respawns", false); // a vault is cracked once: the loot on the map is finite
            return objective;
        }

        // ------------------------------------------------------------------
        // Geometry helpers

        private static Transform Group(Transform parent, string name)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent);
            return t;
        }

        private static GameObject Box(Transform parent, string name, Vector3 center, Vector3 size, Color color)
        {
            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent);
            box.transform.position = center;
            box.transform.localScale = size;
            box.isStatic = true;
            Tint(box, color);
            return box;
        }

        /// <summary>
        /// A walkable slab from <paramref name="bottom"/> (ground end, centre of its edge) up to <paramref name="top"/>.
        /// Keep the slope under about 30 degrees so the NavMesh treats it as walkable.
        /// </summary>
        private static GameObject Ramp(Transform parent, string name, Vector3 bottom, Vector3 top, float width)
        {
            Vector3 along = top - bottom;
            const float thickness = 0.4f;
            var ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = name;
            ramp.transform.SetParent(parent);
            ramp.transform.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
            // Sink the slab by half its thickness along its own up axis so the walking surface meets both ends.
            ramp.transform.position = (bottom + top) * 0.5f - ramp.transform.up * (thickness * 0.5f);
            ramp.transform.localScale = new Vector3(width, thickness, along.magnitude + 0.6f);
            ramp.isStatic = true;
            Tint(ramp, RampColor);
            return ramp;
        }
    }
}
