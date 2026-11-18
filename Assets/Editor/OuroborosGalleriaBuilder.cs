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
    /// Builds the Galleria (v0.14): an indoor, two-level mall around a bank, 220 by 180 metres.
    ///
    ///   Centre   The Atrium, open through both levels, with the central getaway on its floor.
    ///   Arms     Four concourses with shops, each with an escalator up to the galleries.
    ///   Anchors  North: the Bank (lobby, vault corridor, main vault, safe deposit, counting room).
    ///            East: the Luxe Wing (art gallery, jewellers, auction house and its strongroom).
    ///            West: the Market Hall (food court, supermarket and stockroom, dispensary and cold store).
    ///            South: Services (loading dock, truck bay, cash cage, power room).
    ///   Corners  A block of four shops, a hall, staff corridors and stairs; a yard; and a crew gate.
    ///   Upper    Walkways over the concourses, an upper mall over each block, and four secure rooms:
    ///            Security HQ, the Manager's Office, the Server Room and the VIP Lounge.
    ///
    /// The plan itself is data in OuroborosGalleriaLayout.cs, generated and checked by Tools/mapgen/galleria.py.
    /// This file turns that data into walls, floors, doors, props, vaults, security and events.
    ///
    /// Menu: Ouroboros > Setup > Create Galleria Map (M6)
    /// </summary>
    public static partial class OuroborosDevSceneBuilder
    {
        private struct GZone
        {
            public string Name; public int Level; public float X, Z, W, D; public bool Poi;
            public GZone(string name, int level, float x, float z, float w, float d, bool poi) { Name = name; Level = level; X = x; Z = z; W = w; D = d; Poi = poi; }
        }
        private struct GDoor
        {
            public string Kind, Name, Arg; public int Level; public float X, Z, Width; public bool AlongX;
            public GDoor(string kind, int level, float x, float z, bool alongX, float width, string name, string arg)
            { Kind = kind; Level = level; X = x; Z = z; AlongX = alongX; Width = width; Name = name; Arg = arg; }
        }
        private struct GRamp
        {
            public string Name; public float X0, Z0, X1, Z1, Width;
            public GRamp(string name, float x0, float z0, float x1, float z1, float width) { Name = name; X0 = x0; Z0 = z0; X1 = x1; Z1 = z1; Width = width; }
        }
        private struct GItem
        {
            public string Kind, Arg; public int Level; public float X, Z, Yaw;
            public GItem(string kind, int level, float x, float z, float yaw, string arg) { Kind = kind; Level = level; X = x; Z = z; Yaw = yaw; Arg = arg; }
        }

        private const string GalleriaScenePath = SceneDir + "/Galleria.unity";
        private const string PendingGalleriaKey = "Ouroboros.DevSceneBuilder.PendingGalleriaBuild";
        private const int GalleriaLootQuota = 6000;
        private const float GWallHeight = 5.5f, GUpperWall = 4.5f, GWallThick = 2f;

        private static readonly Color GWall = new Color(0.74f, 0.72f, 0.68f);
        private static readonly Color GUpper = new Color(0.63f, 0.62f, 0.6f);
        private static readonly Color GSlab = new Color(0.5f, 0.5f, 0.53f);
        private static readonly Color GFloor = new Color(0.44f, 0.44f, 0.46f);

        [MenuItem("Ouroboros/Setup/Create Galleria Map (M6)")]
        public static void CreateGalleriaMap()
        {
            EnsureFolders();
            s_loaded.Clear();
            CreateAssetsStep();
            SessionState.SetBool(PendingGalleriaKey, true);
            EditorApplication.delayCall += RunPendingGalleriaBuild;
            Debug.Log("[Ouroboros] Assets ready; building the Galleria scene on the next editor update...");
        }

        /// <summary>Deletes the Galleria scene and builds it again from the layout data. Hand edits to the scene are lost.</summary>
        [MenuItem("Ouroboros/Setup/Recreate Galleria Map (overwrites the scene)")]
        public static void RecreateGalleriaMap()
        {
            if (File.Exists(GalleriaScenePath)
                && !EditorUtility.DisplayDialog("Recreate Galleria", "This deletes " + GalleriaScenePath + " and rebuilds it from the layout data. Hand edits to that scene are lost.", "Rebuild", "Cancel"))
            {
                return;
            }
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            AssetDatabase.DeleteAsset(GalleriaScenePath);
            CreateGalleriaMap();
        }

        [InitializeOnLoadMethod]
        private static void ResumeGalleriaAfterReload()
        {
            if (SessionState.GetBool(PendingGalleriaKey, false)) EditorApplication.delayCall += RunPendingGalleriaBuild;
        }

        private static void RunPendingGalleriaBuild()
        {
            if (!SessionState.GetBool(PendingGalleriaKey, false)) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += RunPendingGalleriaBuild;
                return;
            }
            SessionState.SetBool(PendingGalleriaKey, false);
            BuildGalleriaSceneStep();
        }

        private static void BuildGalleriaSceneStep()
        {
            string previousPath = ScenePath;
            var previousLevel = s_levelBuilder;
            ScenePath = GalleriaScenePath;
            s_levelBuilder = BuildGalleriaLevel;
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
                var idCardPrefab = LoadNetworkObject(IDCardPrefabPath);

                BuildScene(config, playerPrefab, lootDropPrefab, registry, panel, feedback, equipment);
                BuildGalleriaContent(guardPrefab, elitePrefab, sniperPrefab, idCardPrefab);

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                NetworkProjectConfigUtilities.RebuildPrefabTable();
                Debug.Log("[Ouroboros] Galleria ready at " + GalleriaScenePath + ". It is in Build Settings. With that scene open, press Play and choose Solo.");
            }
            finally
            {
                ScenePath = previousPath;
                s_levelBuilder = previousLevel;
                s_propY = 0f;
            }
        }

        // ------------------------------------------------------------------
        // The building: floors, walls, the upper level, ramps, zones, spawns and exits

        private static float LevelY(int level) => level <= 0 ? 0f : GalleriaUpperY;

        private static void BuildGalleriaLevel()
        {
            // Indoors there is no sun to speak of: a bright flat ambient, a soft unshadowed key, and coloured
            // lights per zone carry the mood. Shadows from a ceiling would only make the place dark.
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.6f, 0.6f, 0.64f);
            if (Object.FindFirstObjectByType<Light>() == null)
            {
                var lightGo = new GameObject("Directional Light");
                var light = lightGo.AddComponent<Light>();
                light.type = LightType.Directional;
                light.intensity = 0.55f;
                light.shadows = LightShadows.None;
                lightGo.transform.rotation = Quaternion.Euler(62f, -30f, 0f);
            }

            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Ground"; // the NavMesh bake looks for this name
            ground.transform.localScale = new Vector3(GalleriaHalfX / 5f, 1f, GalleriaHalfZ / 5f);
            ground.isStatic = true;
            Tint(ground, GFloor);

            var level = new GameObject("Level").transform;

            var walls = Group(level, "Walls");
            for (int i = 0; i + 3 < GalleriaWalls.Length; i += 4)
            {
                BoxSpan(walls, "Wall", GalleriaWalls[i], GalleriaWalls[i + 1], GalleriaWalls[i + 2], GalleriaWalls[i + 3], 0f, GWallHeight, GWall);
            }

            var upper = Group(level, "Upper Level");
            for (int i = 0; i + 3 < GalleriaUpperMass.Length; i += 4)
            {
                BoxSpan(upper, "Upper Wall", GalleriaUpperMass[i], GalleriaUpperMass[i + 1], GalleriaUpperMass[i + 2], GalleriaUpperMass[i + 3], GWallHeight, GalleriaUpperY + GUpperWall, GUpper);
            }
            for (int i = 0; i + 3 < GalleriaUpperSlabs.Length; i += 4)
            {
                BoxSpan(upper, "Upper Floor", GalleriaUpperSlabs[i], GalleriaUpperSlabs[i + 1], GalleriaUpperSlabs[i + 2], GalleriaUpperSlabs[i + 3], GWallHeight, GalleriaUpperY, GSlab);
            }

            // Glass rails wherever an upper floor meets a drop. Waist high: cover to crouch behind, easy to vault.
            var rails = Group(level, "Rails");
            for (int i = 0; i + 3 < GalleriaRails.Length; i += 4)
            {
                float x0 = GalleriaRails[i], z0 = GalleriaRails[i + 1], x1 = GalleriaRails[i + 2], z1 = GalleriaRails[i + 3];
                bool alongX = Mathf.Abs(x1 - x0) > Mathf.Abs(z1 - z0);
                var centre = new Vector3((x0 + x1) * 0.5f, GalleriaUpperY + 0.55f, (z0 + z1) * 0.5f);
                var size = alongX ? new Vector3(Mathf.Abs(x1 - x0), 1.1f, 0.12f) : new Vector3(0.12f, 1.1f, Mathf.Abs(z1 - z0));
                Hard(rails, "Rail", centre, size, Glass);
                Soft(rails, "Rail Cap", centre + Vector3.up * 0.57f, alongX ? new Vector3(size.x, 0.06f, 0.16f) : new Vector3(0.16f, 0.06f, size.z), MetalDark);
            }

            var ramps = Group(level, "Ramps");
            foreach (var r in GalleriaRamps)
            {
                Ramp(ramps, r.Name, new Vector3(r.X0, 0f, r.Z0), new Vector3(r.X1, GalleriaUpperY, r.Z1), r.Width);
                // Balustrades down both sides, so the ramp reads as an escalator and you cannot step off its edge.
                Vector3 along = new Vector3(r.X1 - r.X0, GalleriaUpperY, r.Z1 - r.Z0);
                Vector3 side = Vector3.Cross(Vector3.up, new Vector3(along.x, 0f, along.z).normalized);
                foreach (float s in new[] { -1f, 1f })
                {
                    var rail = Box(ramps, "Balustrade", new Vector3((r.X0 + r.X1) * 0.5f, GalleriaUpperY * 0.5f + 0.5f, (r.Z0 + r.Z1) * 0.5f) + side * (s * r.Width * 0.5f),
                        new Vector3(0.14f, 1f, along.magnitude), MetalDark);
                    rail.transform.rotation = Quaternion.LookRotation(along.normalized, Vector3.up);
                }
            }

            // Zones: a name for the HUD, a floor in the district's colour, and a ceiling light.
            var zones = Group(level, "Zones");
            foreach (var z in GalleriaZones)
            {
                float y = LevelY(z.Level);
                float height = z.Level == 0 ? GWallHeight : GUpperWall;
                var go = new GameObject("Zone: " + z.Name);
                go.transform.SetParent(zones);
                go.transform.position = new Vector3(z.X, y + height * 0.5f, z.Z);
                var zone = go.AddComponent<GameMode.MapZone>();
                zone.zoneName = z.Name;
                zone.size = new Vector3(z.W, height, z.D);
                zone.pointOfInterest = z.Poi;

                ZoneLook(z.Name, out Color floor, out Color light);
                Soft(zones, "Floor: " + z.Name, new Vector3(z.X, y + 0.02f, z.Z), new Vector3(z.W, 0.03f, z.D), floor);
                if (z.W * z.D >= 150f)
                {
                    float range = Mathf.Clamp(Mathf.Max(z.W, z.D) * 0.75f, 10f, 30f);
                    CeilingLight(zones, new Vector3(z.X, y + (z.Level == 0 ? 4.9f : 4.2f), z.Z), light, range, z.Poi ? 1.5f : 1.15f);
                }
            }

            BuildGalleriaMarks(level);
            BuildGalleriaSkylight(level);
        }

        /// <summary>A box from a floor-plan rectangle (x0, z0)-(x1, z1), between two heights. Solid and static.</summary>
        private static GameObject BoxSpan(Transform parent, string name, float x0, float z0, float x1, float z1, float yBottom, float yTop, Color color)
        {
            return Box(parent, name, new Vector3((x0 + x1) * 0.5f, (yBottom + yTop) * 0.5f, (z0 + z1) * 0.5f),
                new Vector3(Mathf.Abs(x1 - x0), yTop - yBottom, Mathf.Abs(z1 - z0)), color);
        }

        /// <summary>Floor and light colours by what kind of place a zone is.</summary>
        private static void ZoneLook(string name, out Color floor, out Color light)
        {
            bool Has(string word) => name.IndexOf(word, System.StringComparison.OrdinalIgnoreCase) >= 0;
            if (Has("Vault") || Has("Deposit") || Has("Counting") || Has("Strongroom") || Has("Cage") || Has("Cold Store") || Has("Stockroom"))
            { floor = new Color(0.33f, 0.36f, 0.4f); light = new Color(0.75f, 0.85f, 1f); }
            else if (Has("Bank")) { floor = new Color(0.5f, 0.46f, 0.36f); light = new Color(1f, 0.9f, 0.7f); }
            else if (Has("Jewellers") || Has("Auction") || Has("Art Gallery") || Has("VIP")) { floor = new Color(0.42f, 0.16f, 0.18f); light = new Color(1f, 0.82f, 0.62f); }
            else if (Has("Food Court") || Has("Supermarket") || Has("Dispensary")) { floor = new Color(0.62f, 0.54f, 0.4f); light = new Color(1f, 0.95f, 0.8f); }
            else if (Has("Loading") || Has("Truck") || Has("Power") || Has("Yard") || Has("Gate")) { floor = new Color(0.34f, 0.34f, 0.36f); light = new Color(0.85f, 0.9f, 1f); }
            else if (Has("Staff Corridor")) { floor = new Color(0.28f, 0.3f, 0.32f); light = new Color(0.8f, 0.95f, 0.85f); }
            else if (Has("Security") || Has("Server")) { floor = new Color(0.2f, 0.26f, 0.32f); light = new Color(0.55f, 0.85f, 1f); }
            else if (Has("Concourse") || Has("Atrium") || Has("Gallery") || Has("Upper Mall")) { floor = new Color(0.7f, 0.68f, 0.64f); light = new Color(1f, 0.96f, 0.88f); }
            else { floor = new Color(0.52f, 0.42f, 0.32f); light = new Color(1f, 0.9f, 0.75f); }
        }

        /// <summary>Spawns and exits. They are in the level step because a scene without spawn points is rebuilt.</summary>
        private static void BuildGalleriaMarks(Transform level)
        {
            var spawns = new GameObject("SpawnPoints");
            var exits = new GameObject("ExtractionPoints");
            var dressing = Group(level, "Crew Gates");

            foreach (var item in GalleriaItems)
            {
                if (item.Kind == "Spawn")
                {
                    if (!System.Enum.TryParse(item.Arg, out Core.TeamID team)) continue;
                    var position = new Vector3(item.X, 0f, item.Z);
                    var sp = new GameObject(Core.TeamUtil.DisplayName(team) + " Gate");
                    sp.transform.SetParent(spawns.transform);
                    sp.transform.position = position;
                    sp.transform.rotation = Quaternion.LookRotation(-position.normalized);
                    var point = sp.AddComponent<Player.TeamSpawnPoint>();
                    SetEnum(point, "team", (int)team);
                    SetFloat(point, "radius", 3.5f);

                    // The gate is the crew's: its colour on the floor, its pattern on the wall, its city over it.
                    var style = Art.CrewStyle.Get(team);
                    Soft(dressing, "Gate Mat", position + Vector3.up * 0.04f, new Vector3(9f, 0.04f, 9f), Color.Lerp(style.Dark, style.Primary, 0.4f));
                    float wallX = Mathf.Sign(item.X) * (GalleriaHalfX - GWallThick - 0.06f);
                    var cloth = Soft(dressing, Core.TeamUtil.DisplayName(team) + " Banner", new Vector3(wallX, 2.9f, item.Z), new Vector3(0.06f, 2.6f, 5f), style.Primary);
                    var material = CrewMaterialAsset(team, "GateBanner", new Vector2(5f, 2.6f));
                    if (material != null) cloth.GetComponent<Renderer>().sharedMaterial = material;
                    Sign(dressing, Core.TeamUtil.DisplayName(team).ToUpperInvariant(), new Vector3(wallX - Mathf.Sign(item.X) * 0.08f, 4.7f, item.Z),
                        Quaternion.Euler(0f, item.X > 0f ? 90f : -90f, 0f), 0.55f, Color.Lerp(style.Primary, Color.white, 0.35f));
                }
                else if (item.Kind == "Exit")
                {
                    string[] a = item.Arg.Split('|');
                    bool central = a.Length > 1 && a[1] == "central";
                    Exit(exits.transform, a[0], new Vector3(item.X, 0.05f, item.Z), central ? 6f : 4.5f, central,
                        central ? new Color(1f, 0.8f, 0.2f, 0.7f) : new Color(0.1f, 0.9f, 0.5f, 0.6f));
                    if (!central) Sign(dressing, "EXIT", new Vector3(item.X, 3.6f, item.Z), Quaternion.LookRotation(new Vector3(item.X, 0f, item.Z).normalized), 0.6f, new Color(0.2f, 1f, 0.55f));
                }
            }
        }

        /// <summary>The glass roof over the atrium and the concourses: a grid of beams high up, which blocks nothing.</summary>
        private static void BuildGalleriaSkylight(Transform level)
        {
            var sky = Group(level, "Skylight");
            float y = GalleriaUpperY + GUpperWall + 0.6f;
            for (float t = -20f; t <= 20f; t += 8f)
            {
                Soft(sky, "Truss", new Vector3(t, y, 0f), new Vector3(0.4f, 0.5f, 44f), MetalDark);
                Soft(sky, "Truss", new Vector3(0f, y, t), new Vector3(44f, 0.5f, 0.4f), MetalDark);
            }
            foreach (float s in new[] { -1f, 1f })
            {
                for (float a = 26f; a <= 66f; a += 8f)
                {
                    if (a <= 52f) Soft(sky, "Truss", new Vector3(0f, y, s * a), new Vector3(16f, 0.4f, 0.3f), MetalDark);
                    Soft(sky, "Truss", new Vector3(s * a, y, 0f), new Vector3(0.3f, 0.4f, 16f), MetalDark);
                }
            }
            // The freeport's emblem in the atrium floor marks the central getaway.
            s_propY = 0f;
            RingOfBoxes(sky, new Vector3(0f, 0.05f, 0f), 6f, 44, 0.9f, Gold, upright: false);
            RingOfBoxes(sky, new Vector3(0f, 0.05f, 0f), 4.6f, 36, 0.35f, DarkStone, upright: false);

            // Wing names over the mouth of each concourse, readable from the atrium.
            Sign(sky, "THE BANK", new Vector3(0f, 4.6f, 21.9f), Quaternion.identity, 0.9f, Gold);
            Sign(sky, "SERVICES", new Vector3(0f, 4.6f, -21.9f), Quaternion.Euler(0f, 180f, 0f), 0.9f, Hazard);
            Sign(sky, "LUXE WING", new Vector3(21.9f, 4.6f, 0f), Quaternion.Euler(0f, 90f, 0f), 0.9f, WarmLight);
            Sign(sky, "MARKET HALL", new Vector3(-21.9f, 4.6f, 0f), Quaternion.Euler(0f, -90f, 0f), 0.9f, new Color(0.6f, 1f, 0.7f));
        }

        // ------------------------------------------------------------------
        // What is in the building: doors, vaults, breakables, devices, security, events

        private static void BuildGalleriaContent(NetworkObject guardPrefab, NetworkObject elitePrefab, NetworkObject sniperPrefab, NetworkObject idCardPrefab)
        {
            var scene = EditorSceneManager.GetActiveScene();

            var teamManager = Object.FindFirstObjectByType<Network.TeamManager>();
            if (teamManager != null && teamManager.GetComponent<GameMode.AlarmSystem>() == null) teamManager.gameObject.AddComponent<GameMode.AlarmSystem>();
            var gameMode = Object.FindFirstObjectByType<GameMode.ExtractionHeistGameMode>();
            if (gameMode != null) SetReference(gameMode, "idCardPrefabObject", idCardPrefab);
            // A bigger building with far more loot in it: the getaway is called at twice the usual haul.
            if (gameMode != null) SetInt(gameMode, "lootQuotaOverride", GalleriaLootQuota);
            var hud = Object.FindFirstObjectByType<UI.HeistHUD>();
            if (hud != null && hud.GetComponent<UI.KeypadUI>() == null) hud.gameObject.AddComponent<UI.KeypadUI>();

            // The lobby camera has a much bigger building to show.
            var spectator = Object.FindFirstObjectByType<Player.SpectatorCamera>();
            if (spectator != null)
            {
                spectator.transform.position = new Vector3(0f, 95f, -120f);
                spectator.transform.LookAt(new Vector3(0f, 0f, -10f));
            }

            if (GameObject.Find("Galleria Content") != null) { TryBakeNavMesh(); EditorSceneManager.SaveScene(scene, ScenePath); return; }
            var content = new GameObject("Galleria Content").transform;

            var vaultDoors = new Dictionary<string, Interaction.VaultDoor>();
            var weakWalls = new Dictionary<string, Interaction.Breakable>();
            var powerNodes = new Dictionary<string, Interaction.PowerNode>();
            var objectives = new List<Object>();
            var dormant = new Dictionary<string, List<Interaction.Breakable>>();

            // ---- Doors first: vaults and events refer to them by name
            var doors = Group(content, "Doors");
            foreach (var d in GalleriaDoors) BuildGalleriaDoor(doors, d, vaultDoors, weakWalls);

            // ---- Everything else
            var props = Group(content, "Props");
            var breakables = Group(content, "Breakables");
            var devices = Group(content, "Devices");
            var vaults = Group(content, "Vaults");
            var security = Group(content, "Security");
            var patrolA = new SortedDictionary<int, Object>(); var patrolB = new SortedDictionary<int, Object>();
            var spawnA = new List<Object>(); var spawnB = new List<Object>(); var snipers = new List<Object>();
            var events = new List<GItem>();
            Interaction.HackTerminal terminal = null;
            int posts = 0;

            Transform Marker(string name, Vector3 position, Vector3? lookAt = null)
            {
                var t = new GameObject(name).transform;
                t.SetParent(security);
                t.position = position;
                if (lookAt.HasValue) t.LookAt(lookAt.Value);
                return t;
            }

            foreach (var item in GalleriaItems)
            {
                float y = LevelY(item.Level);
                s_propY = y;
                var floor = new Vector3(item.X, y, item.Z);
                string[] a = (item.Arg ?? "").Split('|');

                if (item.Kind.StartsWith("B:"))
                {
                    var b = GalleriaBreakable(breakables, item, a);
                    if (b != null && a.Length > 3 && a[3] == "dormant")
                    {
                        string group = a.Length > 4 ? a[4] : "truck";
                        if (!dormant.TryGetValue(group, out var list)) dormant[group] = list = new List<Interaction.Breakable>();
                        list.Add(b);
                    }
                    continue;
                }

                switch (item.Kind)
                {
                    // ---- furniture
                    case "Shelf":      ShelfRack(props, item.X, item.Z, item.Yaw, 6f); break;
                    case "Counter":    ShopCounter(props, item.X, item.Z, item.Yaw); break;
                    case "Table":      FoodTable(props, item.X, item.Z, item.Yaw); break;
                    case "Bench":      Bench(props, item.X, item.Z, item.Yaw); break;
                    case "Planter":    // a tree needs the open atrium above it; under a ceiling it is a shrub
                        Planter(props, item.X, item.Z, item.Yaw, item.Arg != "shrub" && item.Level == 0 && Mathf.Abs(item.X) < 18f && Mathf.Abs(item.Z) < 18f);
                        break;
                    case "Kiosk":      Kiosk(props, item.X, item.Z, item.Yaw); break;
                    case "Rack":       ClothesRack(props, item.X, item.Z, item.Yaw); break;
                    case "Mannequin":  Mannequin(props, item.X, item.Z, item.Yaw); break;
                    case "Sofa":       Sofa(props, item.X, item.Z, item.Yaw); break;
                    case "Desk":       Desk(props, item.X, item.Z, item.Yaw); break;
                    case "Servers":    ServerCabinet(props, item.X, item.Z, item.Yaw); break;
                    case "Crate":      Crate(props, item.X, item.Z, item.Yaw); break;
                    case "CrateStack": CrateStack(props, item.X, item.Z, item.Yaw); break;
                    case "Pallet":     Pallet(props, item.X, item.Z, item.Yaw); break;
                    case "Barrels":    Barrels(props, item.X, item.Z, item.Yaw); break;
                    case "Forklift":   Forklift(props, item.X, item.Z, item.Yaw); break;
                    case "Truck":      ArmouredTruck(props, item.X, item.Z, item.Yaw); break;
                    case "Lockers":    LockerRow(props, item.X, item.Z, item.Yaw); break;
                    case "Detector":   MetalDetector(props, item.X, item.Z, item.Yaw); break;
                    case "Arcade":     ArcadeCabinet(props, item.X, item.Z, item.Yaw); break;
                    case "Treadmill":  Treadmill(props, item.X, item.Z, item.Yaw); break;
                    case "Seats":      SeatRow(props, item.X, item.Z, item.Yaw); break;
                    case "Plinth":     ShowPlinth(props, item.X, item.Z, item.Yaw, item.Arg); break;
                    case "Stall":
                        Stall(props, item.X, item.Z, item.Yaw, System.Enum.TryParse(item.Arg, out Core.TeamID cloth) ? cloth : Core.TeamID.TeamAlpha);
                        break;
                    case "Van":
                    {
                        Color paint = item.Arg == "white" ? new Color(0.85f, 0.85f, 0.8f) : item.Arg == "blue" ? new Color(0.2f, 0.3f, 0.45f)
                                    : item.Arg == "red" ? new Color(0.45f, 0.2f, 0.15f) : new Color(0.3f, 0.32f, 0.36f);
                        Van(props, item.X, item.Z, item.Yaw, paint);
                        break;
                    }

                    // ---- devices
                    case "Turret": Turret(devices, GalleriaZoneName(item.X, item.Z, item.Level) + " Turret", floor); break;
                    case "Power":
                        PowerJunction(devices, item.Arg, floor);
                        var node = GameObject.Find(item.Arg);
                        if (node != null && node.GetComponent<Interaction.PowerNode>() != null) powerNodes[item.Arg] = node.GetComponent<Interaction.PowerNode>();
                        break;
                    case "Lift": LiftPad(devices, "Atrium Lift", floor + Vector3.up * 0.05f, float.TryParse(item.Arg, out float rise) ? rise : 6f); break;
                    case "Gravity":
                        if (a.Length >= 7)
                        {
                            GravityControl(devices, a[0], floor + Vector3.up * 1.3f,
                                new Vector3(F(a[1]), F(a[2]), F(a[3])), new Vector3(F(a[4]), F(a[5]), F(a[6])));
                        }
                        break;
                    case "Gate": GalleriaShutter(devices, item.Arg + " " + (devices.childCount + 1), floor, item.Yaw < 45f, 16f, 4.4f, false, 2.4f); break;
                    case "Terminal":
                    {
                        var term = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        term.name = item.Arg;
                        term.transform.SetParent(devices);
                        term.transform.position = floor + Vector3.up * 0.6f;
                        term.transform.localScale = new Vector3(1f, 1.2f, 0.5f);
                        Tint(term, new Color(0.1f, 0.6f, 0.9f));
                        term.AddComponent<NetworkObject>();
                        terminal = term.AddComponent<Interaction.HackTerminal>();
                        break;
                    }
                    case "Camera":
                    {
                        var cam = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        cam.name = "SecurityCamera";
                        cam.transform.SetParent(devices);
                        cam.transform.position = floor + Vector3.up * (item.Level == 0 ? 4.4f : 3.8f);
                        cam.transform.localScale = new Vector3(0.4f, 0.4f, 0.8f);
                        if (a.Length >= 2) cam.transform.LookAt(new Vector3(F(a[0]), y + 1f, F(a[1])));
                        Object.DestroyImmediate(cam.GetComponent<Collider>());
                        Tint(cam, new Color(0.2f, 0.8f, 1f));
                        cam.AddComponent<NetworkObject>();
                        cam.AddComponent<Interaction.SecurityCamera>();
                        break;
                    }
                    case "Keycard":
                    {
                        if (!System.Enum.TryParse(item.Arg, out Interaction.KeycardColor colour)) break;
                        var key = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        key.name = "Keycard (" + item.Arg + ")";
                        key.transform.SetParent(devices);
                        key.transform.position = floor + Vector3.up * 1f;
                        key.transform.localScale = new Vector3(0.5f, 0.06f, 0.3f);
                        Object.DestroyImmediate(key.GetComponent<Collider>());
                        Tint(key, Interaction.KeycardPickup.ToColor(colour));
                        key.AddComponent<NetworkObject>();
                        SetEnum(key.AddComponent<Interaction.KeycardPickup>(), "color", (int)colour);
                        Soft(devices, "Keycard Stand", floor + Vector3.up * 0.45f, new Vector3(0.5f, 0.9f, 0.5f), DarkStone);
                        break;
                    }
                    case "Note":
                    {
                        if (!vaultDoors.TryGetValue(item.Arg, out var noteDoor)) break;
                        var note = GameObject.CreatePrimitive(PrimitiveType.Cube);
                        note.name = "Code Note (" + item.Arg + ")";
                        note.transform.SetParent(devices);
                        note.transform.position = floor + Vector3.up * 1.2f;
                        note.transform.localScale = new Vector3(0.6f, 0.8f, 0.1f);
                        Object.DestroyImmediate(note.GetComponent<Collider>());
                        Tint(note, new Color(0.95f, 0.95f, 0.8f));
                        note.AddComponent<NetworkObject>();
                        SetReference(note.AddComponent<Interaction.CodeNote>(), "door", noteDoor);
                        break;
                    }

                    // ---- vaults: name | tier | items | bonus | seconds | stages | door | weak wall
                    case "Vault":
                    {
                        if (a.Length < 6) break;
                        System.Enum.TryParse(a[1], out Data.LootTier tier);
                        var objective = VaultObjective(vaults, a[0], floor + Vector3.up * 0.75f, tier, int.Parse(a[2]), a[3] == "true", F(a[4]));
                        SetInt(objective, "stages", Mathf.Max(1, int.Parse(a[5])));
                        if (a.Length > 6 && vaultDoors.TryGetValue(a[6], out var gate)) SetReference(objective, "gatedBy", gate);
                        if (a.Length > 7 && weakWalls.TryGetValue(a[7], out var wall)) SetReference(objective, "alsoOpenedBy", wall);
                        objectives.Add(objective);
                        break;
                    }

                    // ---- security
                    case "Post":
                    {
                        bool elite = item.Arg == "elite";
                        var prefab = elite && elitePrefab != null ? elitePrefab : guardPrefab;
                        var go = new GameObject((elite ? "Elite Post " : "Guard Post ") + (++posts));
                        go.transform.SetParent(security);
                        go.transform.position = floor;
                        go.AddComponent<NetworkObject>();
                        var spawner = go.AddComponent<AI.AISpawner>();
                        var here = Marker("Post Point", floor);
                        SetReference(spawner, "guardPrefab", prefab);
                        SetReferenceArray(spawner, "spawnPoints", new Object[] { here });
                        SetReferenceArray(spawner, "patrolPoints", new Object[] { here });   // one point: the guard stands its ground
                        SetInt(spawner, "initialGuards", 1);
                        SetInt(spawner, "maxAlive", 1);
                        SetInt(spawner, "reinforcementsPerWave", 0);
                        SetBool(spawner, "replaceLossesWhenAlert", false);
                        break;
                    }
                    case "Patrol":
                        if (a.Length >= 2) (a[0] == "A" ? patrolA : patrolB)[int.Parse(a[1])] = Marker("PatrolPoint " + a[0] + a[1], floor);
                        break;
                    case "GuardSpawn": (item.Arg == "A" ? spawnA : spawnB).Add(Marker("GuardSpawn " + item.Arg, floor)); break;
                    case "Sniper": snipers.Add(Marker("SniperPostAnchor", floor + Vector3.up * 1f, new Vector3(0f, 1f, 0f))); break;
                    case "Event": events.Add(item); break;
                }
            }
            s_propY = 0f;

            // ---- Roaming security: one loop round the concourses, one round the upper galleries
            void Roamers(string name, List<Object> spawnPoints, SortedDictionary<int, Object> patrol, int initial, int max, Object[] posts2)
            {
                if (spawnPoints.Count == 0 || guardPrefab == null) return;
                var go = new GameObject(name);
                go.transform.SetParent(security);
                go.AddComponent<NetworkObject>();
                var spawner = go.AddComponent<AI.AISpawner>();
                SetReference(spawner, "guardPrefab", guardPrefab);
                if (elitePrefab != null) SetReference(spawner, "elitePrefab", elitePrefab);
                if (sniperPrefab != null && posts2.Length > 0) SetReference(spawner, "sniperPrefab", sniperPrefab);
                SetReferenceArray(spawner, "spawnPoints", spawnPoints.ToArray());
                SetReferenceArray(spawner, "patrolPoints", new List<Object>(patrol.Values).ToArray());
                if (posts2.Length > 0) SetReferenceArray(spawner, "sniperPosts", posts2);
                SetInt(spawner, "initialGuards", initial);
                SetInt(spawner, "maxAlive", max);
            }
            Roamers("Concourse Patrol", spawnA, patrolA, 6, 12, new Object[0]);
            Roamers("Gallery Patrol", spawnB, patrolB, 3, 6, snipers.ToArray());

            if (terminal != null && objectives.Count > 0) SetReferenceArray(terminal, "linkedObjectives", objectives.ToArray());

            // ---- Events: kind | title | place | seconds | what it acts on
            if (events.Count > 0)
            {
                var go = new GameObject("Map Events");
                go.transform.SetParent(content);
                go.AddComponent<NetworkObject>();
                var director = go.AddComponent<GameMode.MapEventDirector>();
                var list = new List<GameMode.MapEvent>();
                foreach (var item in events)
                {
                    string[] a = item.Arg.Split('|');
                    if (a.Length < 5 || !System.Enum.TryParse(a[0], out GameMode.MapEventKind kind)) continue;
                    var e = new GameMode.MapEvent { kind = kind, title = a[1], place = a[2], atSeconds = F(a[3]) };
                    var point = new GameObject("Event Point: " + a[1]).transform;
                    point.SetParent(go.transform);
                    point.position = new Vector3(item.X, LevelY(item.Level), item.Z);
                    e.point = point;
                    if (kind == GameMode.MapEventKind.Delivery && dormant.TryGetValue(a[4], out var crates)) e.containers = crates.ToArray();
                    if (kind == GameMode.MapEventKind.TimeLock && vaultDoors.TryGetValue(a[4], out var door)) e.door = door;
                    if (kind == GameMode.MapEventKind.Blackout && powerNodes.TryGetValue(a[4], out var power)) e.power = power;
                    list.Add(e);
                }
                director.EditorSetEvents(list.ToArray());
                EditorUtility.SetDirty(director);
            }

            TryBakeNavMesh();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Log($"[Ouroboros] Galleria content ready: {GalleriaZones.Length} zones, {GalleriaDoors.Length} doors, {objectives.Count} vaults, " +
                      $"{breakables.childCount} breakables, {posts} guard posts and two patrols, {events.Count} map events.");
        }

        /// <summary>The smallest zone of the layout that contains a point. Zones are not live objects at edit time.</summary>
        private static string GalleriaZoneName(float x, float z, int level)
        {
            string best = "Galleria";
            float bestArea = float.MaxValue;
            foreach (var zone in GalleriaZones)
            {
                if (zone.Level != level || Mathf.Abs(x - zone.X) > zone.W * 0.5f || Mathf.Abs(z - zone.Z) > zone.D * 0.5f) continue;
                if (zone.W * zone.D < bestArea) { bestArea = zone.W * zone.D; best = zone.Name; }
            }
            return best;
        }

        private static float F(string s) => float.Parse(s, System.Globalization.CultureInfo.InvariantCulture);

        /// <summary>A breakable from a layout item. Arg: tier | named items | loose cash [| dormant [| event group]].</summary>
        private static Interaction.Breakable GalleriaBreakable(Transform parent, GItem item, string[] a)
        {
            if (!System.Enum.TryParse(item.Kind.Substring(2), out Interaction.BreakableKind kind)) return null;
            System.Enum.TryParse(a.Length > 0 ? a[0] : "Common", out Data.LootTier tier);
            int items = a.Length > 1 && int.TryParse(a[1], out int n) ? n : 0;
            int cash = a.Length > 2 && int.TryParse(a[2], out int c) ? c : 0;
            bool dormant = a.Length > 3 && a[3] == "dormant";

            Vector3 size = Art.BreakableModels.SizeOf(kind, Vector3.one);
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube); // its box collider is what gets shot
            go.name = Interaction.Breakable.DefaultLabel(kind);
            go.transform.SetParent(parent);
            go.transform.position = new Vector3(item.X, LevelY(item.Level), item.Z);
            go.transform.rotation = Quaternion.Euler(0f, item.Yaw, 0f);
            var box = go.GetComponent<BoxCollider>();
            box.size = size;
            box.center = new Vector3(0f, size.y * 0.5f, 0f);
            go.GetComponent<Renderer>().enabled = false; // the model is built at run time
            var obstacle = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.size = size;
            obstacle.center = box.center;

            go.AddComponent<NetworkObject>();
            var b = go.AddComponent<Interaction.Breakable>();
            bool armoured = kind == Interaction.BreakableKind.StrongBox || kind == Interaction.BreakableKind.Safe;
            SetEnum(b, "kind", (int)kind);
            SetEnum(b, "lootTier", (int)tier);
            SetInt(b, "lootItems", items);
            SetInt(b, "looseCash", cash);
            SetFloat(b, "maxHealth", armoured ? 90f : kind == Interaction.BreakableKind.DisplayCase ? 25f : kind == Interaction.BreakableKind.AirdropCrate ? 120f : 55f);
            SetFloat(b, "armourThreshold", armoured ? 40f : 0f);
            SetFloat(b, "alarmOnBreak", kind == Interaction.BreakableKind.DisplayCase ? 8f : armoured ? 6f : 3f);
            SetBool(b, "startsDormant", dormant);
            return b;
        }

        // ------------------------------------------------------------------
        // Doors

        private static void BuildGalleriaDoor(Transform parent, GDoor d, Dictionary<string, Interaction.VaultDoor> vaultDoors, Dictionary<string, Interaction.Breakable> weakWalls)
        {
            float y = LevelY(d.Level);
            float wallHeight = d.Level == 0 ? GWallHeight : GUpperWall;
            float doorHeight = Mathf.Min(wallHeight - 0.6f, d.Width >= 8f ? 4.4f : 3.5f);
            var floor = new Vector3(d.X, y, d.Z);
            Vector3 SizeAlong(float length, float h, float t) => d.AlongX ? new Vector3(length, h, t) : new Vector3(t, h, length);
            var facing = d.AlongX ? Quaternion.identity : Quaternion.Euler(0f, 90f, 0f);
            string label = string.IsNullOrEmpty(d.Name) ? d.Kind + " Door" : d.Name;

            if (d.Kind != "WeakWall")
            {
                // The wall carries on above the doorway.
                Box(parent, "Lintel", floor + Vector3.up * ((doorHeight + wallHeight) * 0.5f), SizeAlong(d.Width, wallHeight - doorHeight, GWallThick), d.Level == 0 ? GWall : GUpper);
            }

            Color trim = MetalDark;
            switch (d.Kind)
            {
                case "Shutter":
                    GalleriaShutter(parent, label, floor, d.AlongX, d.Width, doorHeight, true, GWallThick + 0.4f);
                    trim = new Color(0.75f, 0.55f, 0.15f);
                    break;

                case "Vault":
                {
                    var lockType = Interaction.VaultLockType.Keycard;
                    var colour = Interaction.KeycardColor.None;
                    if (d.Arg.StartsWith("Keycard:")) { lockType = Interaction.VaultLockType.Keycard; System.Enum.TryParse(d.Arg.Substring(8), out colour); }
                    else System.Enum.TryParse(d.Arg, out lockType);

                    var doorGo = new GameObject(label + " Door");
                    doorGo.transform.SetParent(parent);
                    doorGo.transform.SetPositionAndRotation(floor + Vector3.up * (doorHeight * 0.5f), facing);
                    var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    visual.name = "DoorVisual";
                    visual.transform.SetParent(doorGo.transform, false);
                    visual.transform.localScale = new Vector3(d.Width, doorHeight, 0.6f);
                    trim = lockType == Interaction.VaultLockType.Keycard ? Interaction.KeycardPickup.ToColor(colour)
                         : lockType == Interaction.VaultLockType.PlayerCard ? new Color(0.8f, 0.8f, 0.9f)
                         : lockType == Interaction.VaultLockType.Passcode ? new Color(0.2f, 0.9f, 0.9f)
                         : new Color(0.9f, 0.2f, 0.2f);
                    Tint(visual, trim);
                    var obstacle = visual.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                    obstacle.carving = true;
                    obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                    obstacle.size = Vector3.one;
                    doorGo.AddComponent<NetworkObject>();
                    var door = doorGo.AddComponent<Interaction.VaultDoor>();
                    SetString(door, "vaultName", label);
                    SetEnum(door, "lockType", (int)lockType);
                    SetEnum(door, "keycardColor", (int)colour);
                    SetFloat(door, "interactRadius", 3.2f); // the wall is 2 m thick: reachable from either face
                    SetReference(door, "doorVisual", visual);
                    SetReference(door, "navObstacle", obstacle);
                    vaultDoors[label] = door;
                    break;
                }
                case "Maglock":
                {
                    var doorGo = new GameObject(label);
                    doorGo.transform.SetParent(parent);
                    doorGo.transform.SetPositionAndRotation(floor, facing);
                    var visual = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    visual.name = "DoorVisual";
                    visual.transform.SetParent(doorGo.transform, false);
                    visual.transform.localPosition = new Vector3(0f, doorHeight * 0.5f, 0f);
                    visual.transform.localScale = new Vector3(d.Width, doorHeight, 0.4f);
                    Tint(visual, Metal);
                    var obstacle = visual.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                    obstacle.carving = true;
                    obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                    obstacle.size = Vector3.one;
                    doorGo.AddComponent<NetworkObject>();
                    var door = doorGo.AddComponent<Interaction.MagLockDoor>();
                    SetString(door, "doorName", label);
                    SetFloat(door, "width", d.Width);
                    SetFloat(door, "height", doorHeight);
                    SetReference(door, "doorVisual", visual);
                    SetReference(door, "navObstacle", obstacle);
                    trim = Hazard;
                    break;
                }
                case "WeakWall":
                {
                    var size = new Vector3(d.Width, wallHeight, 1.2f);
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = label;
                    go.transform.SetParent(parent);
                    go.transform.SetPositionAndRotation(floor, facing);
                    var box = go.GetComponent<BoxCollider>();
                    box.size = size;
                    box.center = new Vector3(0f, size.y * 0.5f, 0f);
                    go.GetComponent<Renderer>().enabled = false;
                    var obstacle = go.AddComponent<UnityEngine.AI.NavMeshObstacle>();
                    obstacle.carving = true;
                    obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
                    obstacle.size = size;
                    obstacle.center = box.center;
                    go.AddComponent<NetworkObject>();
                    var wall = go.AddComponent<Interaction.Breakable>();
                    SetEnum(wall, "kind", (int)Interaction.BreakableKind.WeakWall);
                    SetString(wall, "label", label);
                    SetVector3(wall, "wallSize", size);
                    SetFloat(wall, "maxHealth", 110f);
                    SetFloat(wall, "armourThreshold", 40f);
                    SetFloat(wall, "alarmOnBreak", 10f);
                    SetFloat(wall, "noiseRadius", 40f);
                    weakWalls[label] = wall;
                    return; // no frame: it is a wall until it is not
                }
                case "Laser":
                {
                    var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
                    go.name = label;
                    go.transform.SetParent(parent);
                    go.transform.SetPositionAndRotation(floor, facing);
                    go.transform.localScale = Vector3.one;
                    Object.DestroyImmediate(go.GetComponent<Collider>());
                    go.GetComponent<Renderer>().enabled = false;
                    go.AddComponent<NetworkObject>();
                    var grid = go.AddComponent<Interaction.LaserGrid>();
                    SetString(grid, "gridName", label);
                    SetFloat(grid, "width", d.Width);
                    SetFloat(grid, "height", doorHeight - 0.2f);
                    SetVector3(grid, "controlOffset", new Vector3(d.Width * 0.5f - 0.22f, 1.4f, 0f));
                    trim = new Color(0.8f, 0.12f, 0.1f);
                    break;
                }
            }

            // A frame in the door's colour, so what kind of door it is reads from across a hall.
            foreach (float s in new[] { -1f, 1f })
            {
                Vector3 along = (d.AlongX ? Vector3.right : Vector3.forward) * (s * (d.Width * 0.5f - 0.06f));
                Soft(parent, "Jamb", floor + along + Vector3.up * (doorHeight * 0.5f), SizeAlong(0.12f, doorHeight, GWallThick + 0.08f), trim);
            }
            Soft(parent, "Head", floor + Vector3.up * (doorHeight - 0.06f), SizeAlong(d.Width, 0.12f, GWallThick + 0.08f), trim);
        }

        /// <summary>A roller shutter across an opening, with a control box that can be hacked from either side.</summary>
        private static void GalleriaShutter(Transform parent, string name, Vector3 floor, bool alongX, float width, float height, bool restsClosed, float controlDepth)
        {
            var gate = Group(parent, name);
            Vector3 along = alongX ? Vector3.right : Vector3.forward;
            Vector3 SizeAlong(float length, float h, float t) => alongX ? new Vector3(length, h, t) : new Vector3(t, h, length);

            var barrier = GameObject.CreatePrimitive(PrimitiveType.Cube);
            barrier.name = "Barrier";
            barrier.transform.SetParent(gate);
            barrier.transform.position = floor + Vector3.up * (height * 0.5f);
            barrier.transform.localScale = SizeAlong(width, height, 0.4f);
            Tint(barrier, new Color(0.75f, 0.55f, 0.15f));
            var obstacle = barrier.AddComponent<UnityEngine.AI.NavMeshObstacle>();
            obstacle.carving = true;
            obstacle.shape = UnityEngine.AI.NavMeshObstacleShape.Box;
            obstacle.size = Vector3.one;

            var box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name + " Control";
            box.transform.SetParent(gate);
            box.transform.position = floor + along * (width * 0.5f - 0.3f) + Vector3.up * 1.5f;
            box.transform.localScale = alongX ? new Vector3(0.5f, 0.6f, controlDepth) : new Vector3(controlDepth, 0.6f, 0.5f);
            Tint(box, new Color(0.2f, 0.8f, 1f));
            box.AddComponent<NetworkObject>();
            var shutter = box.AddComponent<Interaction.Shutter>();
            SetString(shutter, "shutterName", name);
            SetBool(shutter, "restsClosed", restsClosed);
            SetReference(shutter, "barrier", barrier);
            SetReference(shutter, "navObstacle", obstacle);
        }
    }
}
