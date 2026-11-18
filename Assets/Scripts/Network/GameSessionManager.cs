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
        [Tooltip("(v0.13) Show the main menu instead of starting a session on Play. Untick for the old press-Play-and-host flow.")]
        [SerializeField] private bool showMainMenu = true;
        [SerializeField] private NetworkRunner runnerPrefab;

        [Header("Player Prefab")]
        [Tooltip("Assign Assets/Ouroboros/Prefabs/Player.prefab here (direct reference; the scene builder fills it). Used when the NetworkPrefabRef below is empty.")]
        [SerializeField] private NetworkObject playerPrefabObject;
        [Tooltip("Optional Fusion prefab reference. If Fusion shows an error here, re-run Tools > Fusion > Rebuild Prefab Table or just use the field above.")]
        [SerializeField] private NetworkPrefabRef playerPrefab;

        [Header("Feedback (optional)")]
        [Tooltip("Audio/VFX library used by PlayerFeedback and MatchFeedback when they have no override.")]
        [SerializeField] private Data.FeedbackLibrary feedbackLibrary;

        [Header("Equipment (optional)")]
        [Tooltip("Ordered item list used for networked equipment ids. Published as EquipmentRegistry.Active.")]
        [SerializeField] private Data.EquipmentRegistry equipmentRegistry;

        [Header("Class Data (optional)")]
        [Tooltip("Designer stats / ability numbers / class prefabs. Published to every peer as ClassRegistry.Active.")]
        [SerializeField] private Data.ClassRegistry classRegistry;

        [Header("Loot (optional)")]
        [Tooltip("(v0.10) Named loot items. Leave empty to use the built-in catalog, which is identical on every peer.")]
        [SerializeField] private Data.LootCatalog lootCatalog;

        [Header("Defaults")]
        [SerializeField] private Core.PlayerClassType defaultClass = Core.PlayerClassType.Agent;
        [Tooltip("Cycle through classes for successive joins so a dev lobby exercises every class.")]
        [SerializeField] private bool rotateDefaultClass = true;

        [Header("Spectator")]
        [Tooltip("Scene camera shown while there is no local player (lobby before spawn, after despawn). Auto-found by SpectatorCamera if unset.")]
        [SerializeField] private Player.SpectatorCamera spectatorCamera;

        [Header("Local Input")]
        [Tooltip("Mouse look multiplier (Input System deltas are pre-scaled to match the legacy axes).")]
        [SerializeField] private float mouseSensitivity = 2f;
        [Tooltip("Gamepad right-stick look speed in degrees per second at full deflection.")]
        [SerializeField] private float gamepadLookSpeed = 180f;
        [SerializeField] private float pitchLimit = 89f;

        private NetworkRunner runner;
        private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
        private TeamSpawnPointCache spawnPoints;
        private int classRotation;

        // Accumulated between ticks so no key press is lost (Update runs more often than OnInput).
        private float accumulatedYaw;
        private float accumulatedPitch;
        private int pressedSinceLastTick;

        // (v0.9) Recoil is a view punch layered on top of the player's own aim: each shot kicks the offset, and
        // it settles back to zero on its own. The offset is part of the aim sent to the server, so recoil moves
        // where bullets go and the player has to pull against sustained fire.
        private float recoilPitch;   // degrees; negative = up, like accumulatedPitch
        private float recoilYaw;
        private float recoilRecovery = 9f;
        private int recoilBurstShots;
        private float lastRecoilTime = -10f;
        /// <summary>Seconds without firing after which the recoil ramp starts over.</summary>
        private const float RecoilBurstResetSeconds = 0.35f;

        private float AimYaw => accumulatedYaw + recoilYaw;
        private float AimPitch => Mathf.Clamp(accumulatedPitch + recoilPitch, -pitchLimit, pitchLimit);
        /// <summary>Set when a click was used to re-lock the cursor; that press must not also fire the weapon.</summary>
        private bool swallowPrimaryUntilRelease;

        public NetworkRunner Runner => runner;
        public static Data.FeedbackLibrary FeedbackLibrary { get; private set; }
        public bool IsSessionRunning => runner != null && runner.IsRunning;
        /// <summary>(v0.13) True when the main menu, not auto-start, decides when a session begins.</summary>
        public bool ShowMainMenu => showMainMenu;
        /// <summary>(v0.13) Name of the running session. For a private match this is the party code.</summary>
        public string SessionCode { get; private set; }
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

            // (v0.8) editor-only fallbacks for references the scene builder normally fills (see Core.DevAssetFallback).
            if (classRegistry == null) classRegistry = Core.DevAssetFallback.Load<Data.ClassRegistry>(Core.DevAssetFallback.ClassRegistryPath, nameof(GameSessionManager), nameof(classRegistry));
            if (feedbackLibrary == null) feedbackLibrary = Core.DevAssetFallback.Load<Data.FeedbackLibrary>(Core.DevAssetFallback.FeedbackPath, nameof(GameSessionManager), nameof(feedbackLibrary));
            if (equipmentRegistry == null) equipmentRegistry = Core.DevAssetFallback.Load<Data.EquipmentRegistry>(Core.DevAssetFallback.EquipmentRegistryPath, nameof(GameSessionManager), nameof(equipmentRegistry));
            if (playerPrefabObject == null && !playerPrefab.IsValid) playerPrefabObject = Core.DevAssetFallback.LoadPrefab(Core.DevAssetFallback.PlayerPrefabPath, nameof(GameSessionManager), nameof(playerPrefabObject));

            if (classRegistry != null) Data.ClassRegistry.Active = classRegistry;
            FeedbackLibrary = feedbackLibrary;
            if (equipmentRegistry != null) Data.EquipmentRegistry.Active = equipmentRegistry;

            if (lootCatalog != null) Data.LootCatalog.Active = lootCatalog; // (v0.10)

            Equipment.NetworkLoadout.WeaponFired += OnWeaponFired; // (v0.9) recoil
        }

        private void OnDestroy()
        {
            Equipment.NetworkLoadout.WeaponFired -= OnWeaponFired;
            if (Instance == this) Instance = null;
        }

        /// <summary>(v0.9) Kicks the local view when the local player's weapon fires (predicted, so it is immediate).</summary>
        private void OnWeaponFired(Equipment.NetworkLoadout loadout, int slot, Vector3 point, bool hit)
        {
            if (loadout == null || loadout.Owner == null || !loadout.Owner.IsLocalPlayer) return;
            var data = loadout.GetData(slot);
            if (data == null || (data.recoilPitch <= 0f && data.recoilYaw <= 0f)) return;

            if (Time.unscaledTime - lastRecoilTime > RecoilBurstResetSeconds) recoilBurstShots = 0;
            lastRecoilTime = Time.unscaledTime;

            Core.CombatRules.RecoilKick(recoilBurstShots, data.recoilPitch, data.recoilYaw, loadout.FireCount * 7919 + slot, out float up, out float yaw);
            recoilBurstShots++;
            // (v0.13) a shouldered weapon kicks less
            if (loadout.Aiming && slot == loadout.ActiveSlot)
            {
                float steady = Mathf.Clamp01(data.adsRecoilMultiplier);
                up *= steady;
                yaw *= steady;
            }
            recoilPitch -= up;
            recoilYaw += yaw;
            recoilRecovery = Mathf.Max(1f, data.recoilRecoveryPerSecond);
        }

        private async void Start()
        {
            // [v0.12] if (autoStartOnPlay)
            if (autoStartOnPlay && !showMainMenu) // (v0.13) with the menu on, the player picks how to start
            {
                await StartSession(startMode, sessionName);
            }
        }

        private void Update()
        {
            if (runner == null || !runner.IsRunning || runner.LocalPlayer == default) return;
            SampleLocalInput();
            UpdateCursorLock();
        }

        /// <summary>
        /// Re-locks the cursor when the player clicks into the game while playing (the editor releases the lock on
        /// Esc / focus loss). Never locks while the class picker owns the cursor or there is no local player.
        /// </summary>
        private void UpdateCursorLock()
        {
            if (Cursor.lockState == CursorLockMode.Locked) return;
            if (UI.ClassPickerUI.IsShown || UI.KeypadUI.IsShown) return;
            if (UI.GameMenu.IsOpen) return; // (v0.13) the menu owns the cursor while it is up
            if (!runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject local) || local == null) return;
            if (Core.LocalInputSource.Pressed(Core.InputButtons.Primary))
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
                swallowPrimaryUntilRelease = true;
                pressedSinceLastTick &= ~(1 << (int)Core.InputButtons.Primary);
            }
        }

        /// <summary>Called by PlayerController when the local player's object spawns / despawns.</summary>
        public static void NotifyLocalPlayer(bool present)
        {
            var cam = Instance != null && Instance.spectatorCamera != null ? Instance.spectatorCamera : Player.SpectatorCamera.Instance;
            if (cam != null) cam.SetActiveView(!present);
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
            SessionCode = session; // (v0.13)
            return true;
        }

        /// <summary>
        /// (v0.13) Leaves the session and reloads the scene, which brings the main menu back. Reloading is the
        /// simple way to get every scene object back to how it was before the session touched it.
        /// </summary>
        public async void LeaveToMenu()
        {
            int buildIndex = SceneManager.GetActiveScene().buildIndex;
            var leaving = runner;
            try
            {
                if (leaving != null) await leaving.Shutdown();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }

            // If Fusion moved this object out of the scene to keep the runner alive, the reload would not replace it.
            if (this != null && gameObject.scene.name == "DontDestroyOnLoad")
            {
                if (Instance == this) Instance = null;
                Destroy(gameObject);
            }
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (buildIndex >= 0) SceneManager.LoadScene(buildIndex);
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
                Debug.LogError("[GameSessionManager] No player prefab: assign Assets/Ouroboros/Prefabs/Player.prefab to 'Player Prefab Object' " +
                               "on the GameSessionManager (or re-run Ouroboros > Setup > Create Dev Scene). Nothing will spawn.");
                return;
            }

            Core.TeamID team = Core.TeamID.None;
            var teamManager = TeamManager.Instance;
            if (teamManager != null && teamManager.Object != null && teamManager.Object.HasStateAuthority)
            {
                team = teamManager.AssignPlayerToTeam(player);
                // (v0.10) every seat taken, some by bots: a person joining late takes a bot's place
                if (team == Core.TeamID.None)
                {
                    Core.TeamID freed = RemoveOneBot(r);
                    if (freed != Core.TeamID.None) team = teamManager.AssignPlayerToTeam(player, freed);
                }
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

            // Shared mode: the spawner is not the TeamManager's authority, so ask for a team (v0.5)
            if (team == Core.TeamID.None && teamManager != null && teamManager.Object != null && !teamManager.Object.HasStateAuthority)
            {
                teamManager.RequestTeamAssignment();
            }

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

        // ------------------------------------------------------------------
        // Bots (v0.10)
        // ------------------------------------------------------------------

        /// <summary>
        /// Bots are identified by PlayerRefs far above anything Fusion hands to a real connection, so everything
        /// keyed by <c>NetworkPlayer.PlayerRef</c> (teams, kill credit, the carried case) works for them unchanged.
        /// They have no input authority.
        /// </summary>
        private const int BotRefBase = 200;
        private static readonly string[] BotNames =
        {
            "Ada", "Kofi", "Zuri", "Tunde", "Amara", "Sipho", "Wanjiru", "Chidi", "Efua", "Thabo", "Nia", "Jabari", "Folake", "Kwame", "Imani", "Sizwe"
        };
        private readonly List<PlayerRef> botRefs = new List<PlayerRef>();
        private int botSerial;

        public int BotCount => botRefs.Count;

        /// <summary>
        /// Fills every empty crew seat with a bot. Called by the game mode when the match starts. Host / Server
        /// mode only: in Shared mode there is no single peer that could simulate them.
        /// </summary>
        public void FillWithBots()
        {
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            var tm = TeamManager.Instance;
            if (gm == null || tm == null || tm.Object == null || !tm.Object.HasStateAuthority) return;

            var cfg = gm.Config;
            int added = 0;
            for (int t = 0; t < cfg.CrewCount; t++)
            {
                Core.TeamID team = Core.TeamUtil.FromIndex(t);
                for (int guard = 0; guard < Core.GameConstants.PLAYERS_PER_TEAM && tm.GetTeamPlayerCount(team) < cfg.CrewSize; guard++)
                {
                    if (!SpawnBot(runner, team, cfg.botSkill)) break;
                    added++;
                }
            }
            if (added > 0) Debug.Log($"[GameSessionManager] Filled empty seats with {added} bot(s): {cfg.CrewCount} crews of {cfg.CrewSize}");
        }

        private bool SpawnBot(NetworkRunner r, Core.TeamID team, float skill)
        {
            if (!playerPrefab.IsValid && playerPrefabObject == null) return false;
            var tm = TeamManager.Instance;

            PlayerRef botRef = PlayerRef.FromIndex(BotRefBase + botSerial);
            if (tm.AssignPlayerToTeam(botRef, team) != team) { tm.RemovePlayerFromTeam(botRef); return false; }

            int classCount = Enum.GetValues(typeof(Core.PlayerClassType)).Length - 1; // skip None
            Core.PlayerClassType classType = (Core.PlayerClassType)(1 + (classRotation++ % classCount));
            string botName = BotNames[botSerial % BotNames.Length];
            botSerial++;

            var point = spawnPoints.Pick(team);
            Vector3 pos = point != null ? point.GetSpawnPosition() : Vector3.zero;
            Quaternion rot = point != null ? point.GetSpawnRotation() : Quaternion.identity;

            void InitBot(NetworkRunner runner, NetworkObject o)
            {
                var np = o.GetComponent<NetworkPlayer>();
                if (np == null) return;
                np.PlayerRef = botRef;
                np.AssignedTeam = team;
                np.ClassType = classType;
                np.DisplayName = botName;
                np.IsBot = true;
            }

            // No input authority: the state authority simulates the bot from AI.BotBrain's input.
            NetworkObject obj = playerPrefab.IsValid
                ? r.Spawn(playerPrefab, pos, rot, null, InitBot)
                : r.Spawn(playerPrefabObject, pos, rot, null, InitBot);
            if (obj == null) { tm.RemovePlayerFromTeam(botRef); return false; }

            var brain = obj.gameObject.AddComponent<AI.BotBrain>();
            brain.skill = skill;
            var controller = obj.GetComponent<Player.PlayerController>();
            if (controller != null) controller.SetBot(brain);

            spawnedPlayers[botRef] = obj;
            botRefs.Add(botRef);
            Debug.Log($"[GameSessionManager] Spawned bot '{botName}' on {Core.TeamUtil.DisplayName(team)} as {classType}");
            return true;
        }

        /// <summary>Removes one bot to make room for a person. Returns the crew whose seat was freed, or None.</summary>
        private Core.TeamID RemoveOneBot(NetworkRunner r)
        {
            for (int i = 0; i < botRefs.Count; i++)
            {
                PlayerRef botRef = botRefs[i];
                if (!spawnedPlayers.TryGetValue(botRef, out NetworkObject obj) || obj == null) continue;
                var np = obj.GetComponent<NetworkPlayer>();
                Core.TeamID team = np != null ? np.Team : Core.TeamID.None;

                r.Despawn(obj);
                spawnedPlayers.Remove(botRef);
                botRefs.RemoveAt(i);
                var tm = TeamManager.Instance;
                if (tm != null && tm.Object != null && tm.Object.HasStateAuthority) tm.RemovePlayerFromTeam(botRef);
                return team;
            }
            return Core.TeamID.None;
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

        // [v0.2] This method polled UnityEngine.Input directly (legacy Input Manager only).
        // [v0.3] All device reads go through Core.LocalInputSource: Input System first (keyboard, mouse,
        //        gamepad), legacy Input Manager only as a fallback.
        private void SampleLocalInput()
        {
            if (Cursor.lockState == CursorLockMode.Locked || Core.LocalInputSource.LastDeviceWasGamepad)
            {
                // [v0.12] Vector2 look = Core.LocalInputSource.LookDelta(mouseSensitivity, gamepadLookSpeed, Time.unscaledDeltaTime);
                // [v0.12] accumulatedYaw += look.x;
                // [v0.12] accumulatedPitch -= look.y;
                // (v0.13) the player's sensitivity setting, scaled down with the zoom while aiming
                var localController = Player.PlayerController.Local;
                float lookScale = Core.GameSettings.Sensitivity * (localController != null ? localController.LookSensitivityScale : 1f);
                Vector2 look = Core.LocalInputSource.LookDelta(mouseSensitivity * lookScale, gamepadLookSpeed * lookScale, Time.unscaledDeltaTime);
                accumulatedYaw += look.x;
                accumulatedPitch -= Core.GameSettings.InvertY ? -look.y : look.y;
                accumulatedPitch = Mathf.Clamp(accumulatedPitch, -pitchLimit, pitchLimit);
            }

            // (v0.9) the recoil punch settles back toward the player's own aim
            recoilPitch = Core.CombatRules.RecoverRecoil(recoilPitch, recoilRecovery, Time.unscaledDeltaTime);
            recoilYaw = Core.CombatRules.RecoverRecoil(recoilYaw, recoilRecovery, Time.unscaledDeltaTime);

            // Edge-triggered buttons are latched here (Update runs more often than OnInput) so no press is lost.
            for (int b = 0; b < Core.GameConstants.INPUT_BUTTON_COUNT; b++)
            {
                if (Core.LocalInputSource.Pressed((Core.InputButtons)b)) pressedSinceLastTick |= 1 << b;
            }

            // [v0.12] if (!UI.KeypadUI.IsShown && Core.LocalInputSource.CursorTogglePressed())
            // (v0.13) Esc opens the pause menu when there is one; this is the fallback for scenes without it
            if (!UI.GameMenu.Exists && !UI.KeypadUI.IsShown && Core.LocalInputSource.CursorTogglePressed())
            {
                Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;
                Cursor.visible = Cursor.lockState != CursorLockMode.Locked;
            }

            // Drive the local camera immediately for a responsive feel.
            if (runner.TryGetPlayerObject(runner.LocalPlayer, out NetworkObject local) && local != null)
            {
                var controller = local.GetComponent<Player.PlayerController>();
                // [v0.8] controller?.SetLocalPitch(accumulatedPitch);
                controller?.SetLocalPitch(AimPitch); // (v0.9) includes recoil
            }
        }

        private bool WasPressed(Core.InputButtons button) => (pressedSinceLastTick & (1 << (int)button)) != 0;

        public void OnInput(NetworkRunner r, NetworkInput input)
        {
            var data = new Core.NetworkInputData
            {
                Move = Core.LocalInputSource.Move(),
                // [v0.8] Yaw = accumulatedYaw, Pitch = accumulatedPitch
                Yaw = AimYaw,       // (v0.9) the player's aim plus the current recoil punch
                Pitch = AimPitch
            };

            // [v0.2] data.Buttons.Set(..., Input.GetKey(KeyCode.X) || WasPressed(...)) per button (legacy only)
            for (int b = 0; b < Core.GameConstants.INPUT_BUTTON_COUNT; b++)
            {
                var button = (Core.InputButtons)b;
                data.Buttons.Set(b, Core.LocalInputSource.Held(button) || WasPressed(button));
            }

            if (UI.KeypadUI.IsShown)
            {
                // Typing a code must not fire abilities (1-4) or weapons
                for (int b = (int)Core.InputButtons.Ability1; b <= (int)Core.InputButtons.Ability4; b++) data.Buttons.Set(b, false);
                data.Buttons.Set((int)Core.InputButtons.Primary, false);
                data.Buttons.Set((int)Core.InputButtons.Secondary, false);
                data.Buttons.Set((int)Core.InputButtons.NextWeapon, false); // (v0.13)
                data.Buttons.Set((int)Core.InputButtons.PrevWeapon, false);
                data.Buttons.Set((int)Core.InputButtons.Interact, false);
                data.Move = Vector2.zero;
            }

            // (v0.13) with the menu up the character stands still and does nothing
            if (UI.GameMenu.IsOpen)
            {
                data.Buttons = default;
                data.Move = Vector2.zero;
            }

            if (swallowPrimaryUntilRelease)
            {
                data.Buttons.Set((int)Core.InputButtons.Primary, false);
                if (!Core.LocalInputSource.Held(Core.InputButtons.Primary)) swallowPrimaryUntilRelease = false;
            }

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
            botRefs.Clear(); // (v0.10)
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
