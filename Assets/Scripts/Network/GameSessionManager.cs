using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using Fusion;
using Fusion.Sockets;

namespace Ouroboros.Network
{
    /// <summary>
    /// Owns the Fusion <see cref="NetworkRunner"/> lifecycle for a match: starts the session, spawns a
    /// player object per joining <see cref="PlayerRef"/>, assigns teams/classes, handles leaves,
    /// respawns, and collects local input every tick.
    ///
    /// Scene setup: one instance in the gameplay scene, with <see cref="playerPrefab"/> pointing at a
    /// prefab that carries NetworkObject + NetworkTransform + CharacterController + NetworkPlayer +
    /// PlayerController. <see cref="TeamManager"/> and <see cref="GameMode.ExtractionHeistGameMode"/>
    /// are expected to be scene NetworkObjects.
    ///
    /// Interface signatures match Photon Fusion 2.1.3 (the SDK checked into Assets/Photon). If a future SDK
    /// differs, let the IDE regenerate the <see cref="INetworkRunnerCallbacks"/> stubs; only OnPlayerJoined /
    /// OnPlayerLeft / OnInput / OnShutdown carry logic.
    /// </summary>
    public class GameSessionManager : MonoBehaviour, INetworkRunnerCallbacks
    {
        public static GameSessionManager Instance { get; private set; }

        [Header("Session")]
        [SerializeField] private Fusion.GameMode startMode = Fusion.GameMode.AutoHostOrClient;
        [SerializeField] private string sessionName = "ouroboros-dev";
        [SerializeField] private bool autoStartOnPlay = true;
        [SerializeField] private NetworkRunner runnerPrefab;

        [Header("Prefabs")]
        [SerializeField] private NetworkPrefabRef playerPrefab;
        [Tooltip("Fallback used when playerPrefab is unset: a direct prefab reference (the Phase 0 scene builder fills this).")]
        [SerializeField] private NetworkObject playerPrefabObject;

        [Header("Class Data (optional)")]
        [Tooltip("Designer stats / ability numbers / class prefabs. Published to every peer as ClassRegistry.Active.")]
        [SerializeField] private Data.ClassRegistry classRegistry;

        [Header("Defaults")]
        [SerializeField] private Core.PlayerClassType defaultClass = Core.PlayerClassType.Agent;
        [Tooltip("Cycle through classes for successive joins so a dev lobby exercises every class.")]
        [SerializeField] private bool rotateDefaultClass = true;

        [Header("Local Input")]
        [SerializeField] private float mouseSensitivity = 2f;
        [SerializeField] private float pitchLimit = 89f;

        private NetworkRunner runner;
        private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
        private TeamSpawnPointCache spawnPoints;
        private int classRotation;

        // Accumulated between ticks so no key press is lost (Update runs more often than OnInput).
        private float accumulatedYaw;
        private float accumulatedPitch;
        private int pressedSinceLastTick;

        public NetworkRunner Runner => runner;
        public bool IsSessionRunning => runner != null && runner.IsRunning;
        public IReadOnlyDictionary<PlayerRef, NetworkObject> SpawnedPlayers => spawnedPlayers;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            spawnPoints = new TeamSpawnPointCache();
            if (classRegistry != null) Data.ClassRegistry.Active = classRegistry;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private async void Start()
        {
            if (autoStartOnPlay)
            {
                await StartSession(startMode, sessionName);
            }
        }

        private void Update()
        {
            if (runner == null || !runner.IsRunning || runner.LocalPlayer == default) return;
            SampleLocalInput();
        }

        // ------------------------------------------------------------------
        // Session lifecycle
        // ------------------------------------------------------------------

        public async System.Threading.Tasks.Task<bool> StartSession(Fusion.GameMode mode, string session)
        {
            if (runner != null)
            {
                Debug.LogWarning("[GameSessionManager] Session already running");
                return false;
            }

            runner = runnerPrefab != null ? Instantiate(runnerPrefab) : gameObject.AddComponent<NetworkRunner>();
            runner.name = "NetworkRunner";
            runner.ProvideInput = true;
            runner.AddCallbacks(this);

            var sceneManager = runner.GetComponent<NetworkSceneManagerDefault>();
            if (sceneManager == null) sceneManager = runner.gameObject.AddComponent<NetworkSceneManagerDefault>();

            int buildIndex = SceneManager.GetActiveScene().buildIndex;
            if (buildIndex < 0)
            {
                Debug.LogError("[GameSessionManager] The active scene is not in Build Settings, so Fusion cannot reference it. " +
                               "Add it via File > Build Settings (Ouroboros > Setup > Create Dev Scene does this for DevArena).");
                if (runnerPrefab != null) Destroy(runner.gameObject); else Destroy(runner);
                runner = null;
                return false;
            }
            var scene = SceneRef.FromIndex(buildIndex);

            var result = await runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = session,
                Scene = scene,
                SceneManager = sceneManager,
                PlayerCount = Core.GameConstants.MAX_PLAYERS
            });

            if (!result.Ok)
            {
                Debug.LogError($"[GameSessionManager] Failed to start session: {result.ShutdownReason}");
                return false;
            }

            Debug.Log($"[GameSessionManager] Session '{session}' started as {mode}");
            return true;
        }

        public async void ShutdownSession()
        {
            if (runner == null) return;
            await runner.Shutdown();
        }

        /// <summary>True when this peer is responsible for spawning the given player.</summary>
        private bool ShouldSpawn(NetworkRunner r, PlayerRef player)
        {
            if (r.GameMode == Fusion.GameMode.Shared) return player == r.LocalPlayer;
            return r.IsServer;
        }

        // ------------------------------------------------------------------
        // Spawning
        // ------------------------------------------------------------------

        private void SpawnPlayer(NetworkRunner r, PlayerRef player)
        {
            if (!playerPrefab.IsValid && playerPrefabObject == null)
            {
                Debug.LogError("[GameSessionManager] Player prefab not assigned (set playerPrefab or playerPrefabObject)");
                return;
            }

            Core.TeamID team = Core.TeamID.None;
            var teamManager = TeamManager.Instance;
            if (teamManager != null && teamManager.Object != null && teamManager.Object.HasStateAuthority)
            {
                team = teamManager.AssignPlayerToTeam(player);
            }

            Core.PlayerClassType classType = defaultClass;
            if (rotateDefaultClass)
            {
                int count = Enum.GetValues(typeof(Core.PlayerClassType)).Length - 1; // skip None
                classType = (Core.PlayerClassType)(1 + (classRotation++ % count));
            }

            var point = spawnPoints.Pick(team);
            Vector3 pos = point != null ? point.GetSpawnPosition() : Vector3.zero;
            Quaternion rot = point != null ? point.GetSpawnRotation() : Quaternion.identity;

            void InitPlayer(NetworkRunner runner, NetworkObject o)
            {
                var np = o.GetComponent<NetworkPlayer>();
                if (np != null)
                {
                    np.PlayerRef = player;
                    np.AssignedTeam = team;
                    np.ClassType = classType;
                    np.DisplayName = $"Player {player.PlayerId}";
                }
            }

            // [v0.2] NetworkObject obj = r.Spawn(playerPrefab, pos, rot, player, (runner, o) => { ... });
            NetworkObject obj = playerPrefab.IsValid
                ? r.Spawn(playerPrefab, pos, rot, player, InitPlayer)
                : r.Spawn(playerPrefabObject, pos, rot, player, InitPlayer);

            if (obj == null) return;

            r.SetPlayerObject(player, obj);
            spawnedPlayers[player] = obj;

            Debug.Log($"[GameSessionManager] Spawned {player} on {team} as {classType}");
        }

        private void DespawnPlayer(NetworkRunner r, PlayerRef player)
        {
            if (spawnedPlayers.TryGetValue(player, out NetworkObject obj))
            {
                if (obj != null) r.Despawn(obj);
                spawnedPlayers.Remove(player);
            }

            var teamManager = TeamManager.Instance;
            if (teamManager != null && teamManager.Object != null && teamManager.Object.HasStateAuthority)
            {
                teamManager.RemovePlayerFromTeam(player);
            }
        }

        /// <summary>Respawns a dead player at one of their team's spawn points. State authority only.</summary>
        public void RespawnPlayer(NetworkPlayer player)
        {
            if (player == null || player.Object == null || !player.Object.HasStateAuthority) return;

            var point = spawnPoints.Pick(player.Team);
            Vector3 pos = point != null ? point.GetSpawnPosition() : Vector3.zero;
            Quaternion rot = point != null ? point.GetSpawnRotation() : Quaternion.identity;

            var controller = player.GetComponent<Player.PlayerController>();
            if (controller != null) controller.Teleport(pos, rot);
            else player.transform.SetPositionAndRotation(pos, rot);

            player.ResetForRespawn();
            Debug.Log($"[GameSessionManager] Respawned {player.PlayerRef} ({player.RespawnsUsed} respawns used)");
        }

        // ------------------------------------------------------------------
        // Local input
        // ------------------------------------------------------------------

        private void SampleLocalInput()
        {
            if (Cursor.lockState == CursorLockMode.Locked)
            {
                accumulatedYaw += Input.GetAxis("Mouse X") * mouseSensitivity;
                accumulatedPitch -= Input.GetAxis("Mouse Y") * mouseSensitivity;
                accumulatedPitch = Mathf.Clamp(accumulatedPitch, -pitchLimit, pitchLimit);
            }

            if (Input.GetKeyDown(KeyCode.Space))    pressedSinceLastTick |= 1 << (int)Core.InputButtons.Jump;
            if (Input.GetKeyDown(KeyCode.Alpha1))   pressedSinceLastTick |= 1 << (int)Core.InputButtons.Ability1;
            if (Input.GetKeyDown(KeyCode.Alpha2))   pressedSinceLastTick |= 1 << (int)Core.InputButtons.Ability2;
            if (Input.GetKeyDown(KeyCode.Alpha3))   pressedSinceLastTick |= 1 << (int)Core.InputButtons.Ability3;
            if (Input.GetKeyDown(KeyCode.Alpha4))   pressedSinceLastTick |= 1 << (int)Core.InputButtons.Ability4;
            if (Input.GetMouseButtonDown(0))        pressedSinceLastTick |= 1 << (int)Core.InputButtons.Primary;
            if (Input.GetMouseButtonDown(1))        pressedSinceLastTick |= 1 << (int)Core.InputButtons.Secondary;
            if (Input.GetKeyDown(KeyCode.Q))        pressedSinceLastTick |= 1 << (int)Core.InputButtons.Utility;
            if (Input.GetKeyDown(KeyCode.E))        pressedSinceLastTick |= 1 << (int)Core.InputButtons.Gadget;

            if (Input.GetKeyDown(KeyCode.Escape))
            {
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            }

            // Drive the local camera immediately for a responsive feel.
            if (runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject local) && local != null)
            {
                var controller = local.GetComponent<Player.PlayerController>();
                controller?.SetLocalPitch(accumulatedPitch);
            }
        }

        private bool WasPressed(Core.InputButtons button) => (pressedSinceLastTick & (1 << (int)button)) != 0;

        public void OnInput(NetworkRunner r, NetworkInput input)
        {
            var data = new Core.NetworkInputData
            {
                Move = new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")),
                Yaw = accumulatedYaw,
                Pitch = accumulatedPitch
            };

            data.Buttons.Set((int)Core.InputButtons.Jump,      Input.GetKey(KeyCode.Space) || WasPressed(Core.InputButtons.Jump));
            data.Buttons.Set((int)Core.InputButtons.Sprint,    Input.GetKey(KeyCode.LeftShift));
            data.Buttons.Set((int)Core.InputButtons.Interact,  Input.GetKey(KeyCode.F));
            data.Buttons.Set((int)Core.InputButtons.Ability1,  Input.GetKey(KeyCode.Alpha1) || WasPressed(Core.InputButtons.Ability1));
            data.Buttons.Set((int)Core.InputButtons.Ability2,  Input.GetKey(KeyCode.Alpha2) || WasPressed(Core.InputButtons.Ability2));
            data.Buttons.Set((int)Core.InputButtons.Ability3,  Input.GetKey(KeyCode.Alpha3) || WasPressed(Core.InputButtons.Ability3));
            data.Buttons.Set((int)Core.InputButtons.Ability4,  Input.GetKey(KeyCode.Alpha4) || WasPressed(Core.InputButtons.Ability4));
            data.Buttons.Set((int)Core.InputButtons.Primary,   Input.GetMouseButton(0) || WasPressed(Core.InputButtons.Primary));
            data.Buttons.Set((int)Core.InputButtons.Secondary, Input.GetMouseButton(1) || WasPressed(Core.InputButtons.Secondary));
            data.Buttons.Set((int)Core.InputButtons.Utility,   Input.GetKey(KeyCode.Q) || WasPressed(Core.InputButtons.Utility));
            data.Buttons.Set((int)Core.InputButtons.Gadget,    Input.GetKey(KeyCode.E) || WasPressed(Core.InputButtons.Gadget));

            pressedSinceLastTick = 0;
            input.Set(data);
        }

        // ------------------------------------------------------------------
        // INetworkRunnerCallbacks
        // ------------------------------------------------------------------

        public void OnPlayerJoined(NetworkRunner r, PlayerRef player)
        {
            Debug.Log($"[GameSessionManager] Player joined: {player}");

            if (ShouldSpawn(r, player))
            {
                SpawnPlayer(r, player);
            }

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm != null && gm.Object != null && gm.Object.HasStateAuthority)
            {
                gm.NotifyPlayerCountChanged(CountPlayers(r));
            }
        }

        public void OnPlayerLeft(NetworkRunner r, PlayerRef player)
        {
            Debug.Log($"[GameSessionManager] Player left: {player}");

            if (r.IsServer || r.GameMode == Fusion.GameMode.Shared)
            {
                DespawnPlayer(r, player);
            }

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm != null && gm.Object != null && gm.Object.HasStateAuthority)
            {
                gm.NotifyPlayerCountChanged(CountPlayers(r));
            }
        }

        private static int CountPlayers(NetworkRunner r)
        {
            int count = 0;
            foreach (var p in r.ActivePlayers) count++;
            return count;
        }

        public void OnShutdown(NetworkRunner r, ShutdownReason shutdownReason)
        {
            Debug.Log($"[GameSessionManager] Runner shutdown: {shutdownReason}");
            spawnedPlayers.Clear();
            runner = null;
        }

        public void OnInputMissing(NetworkRunner r, PlayerRef player, NetworkInput input) { }
        public void OnConnectedToServer(NetworkRunner r) { }
        public void OnDisconnectedFromServer(NetworkRunner r, NetDisconnectReason reason) { }
        public void OnConnectRequest(NetworkRunner r, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
        public void OnConnectFailed(NetworkRunner r, NetAddress remoteAddress, NetConnectFailedReason reason) { }
        public void OnUserSimulationMessage(NetworkRunner r, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner r, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner r, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner r, HostMigrationToken hostMigrationToken) { }
        // [v0.2] public void OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { } // Fusion 2.0 signature
        public void OnReliableDataReceived(NetworkRunner r, PlayerRef player, ReliableKey key, ReadOnlySpan<byte> data) { }           // Fusion 2.1.x signature
        public void OnReliableDataProgress(NetworkRunner r, PlayerRef player, ReliableKey key, float progress) { }
        public void OnSceneLoadDone(NetworkRunner r) { }
        public void OnSceneLoadStart(NetworkRunner r) { }
        public void OnObjectExitAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner r, NetworkObject obj, PlayerRef player) { }

        // ------------------------------------------------------------------
        // Spawn point cache
        // ------------------------------------------------------------------

        private class TeamSpawnPointCache
        {
            private Player.TeamSpawnPoint[] points;

            public Player.TeamSpawnPoint Pick(Core.TeamID team)
            {
                if (points == null || points.Length == 0)
                {
                    points = Core.SceneUtil.FindAll<Player.TeamSpawnPoint>();
                }
                if (points.Length == 0) return null;

                Player.TeamSpawnPoint fallback = null;
                int matches = 0;
                for (int i = 0; i < points.Length; i++)
                {
                    if (points[i] == null) continue;
                    if (points[i].Team == team) matches++;
                    else if (points[i].Team == Core.TeamID.None && fallback == null) fallback = points[i];
                }

                if (matches == 0) return fallback ?? points[0];

                int pick = UnityEngine.Random.Range(0, matches);
                for (int i = 0; i < points.Length; i++)
                {
                    if (points[i] != null && points[i].Team == team && pick-- == 0) return points[i];
                }
                return fallback;
            }
        }
    }
}
