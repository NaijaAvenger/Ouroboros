using System.Text;
using UnityEngine;

namespace Ouroboros.UI
{
    /// <summary>
    /// Immediate-mode debug overlay for Phase 0 validation. Shows match state, timers, the local
    /// player's vitals/cooldowns/status/loot, team scores and the nearest objective / extraction
    /// progress. Everything it reads is already replicated, so it also works on pure clients.
    /// Replace with a real HUD in Phase 1; toggle with F1.
    /// </summary>
    public class DevHUD : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        // [v0.2] [SerializeField] private KeyCode toggleKey = KeyCode.F1; // now F1 / gamepad Select via LocalInputSource

        private readonly StringBuilder sb = new StringBuilder(1024);
        private GUIStyle style;

        private void Update()
        {
            if (Core.LocalInputSource.DebugHudTogglePressed()) visible = !visible;
        }

        private static readonly Core.PlayerClassType[] PickerClasses =
        {
            Core.PlayerClassType.Hacker, Core.PlayerClassType.Saboteur, Core.PlayerClassType.Demolitions, Core.PlayerClassType.Agent
        };

        /// <summary>
        /// IMGUI lobby shown whenever the UI Toolkit picker is not showing but the match is waiting. Guarantees the
        /// class / start flow works even if the HUD document fails to render. Independent of the F1 toggle.
        /// </summary>
        private void DrawFallbackLobby()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null || !gm.AllowClassChange) return;
            if (ClassPickerUI.IsShown) return;

            Network.NetworkPlayer local = null;
            for (int i = 0; i < Network.NetworkPlayer.All.Count; i++)
            {
                if (Network.NetworkPlayer.All[i] != null && Network.NetworkPlayer.All[i].IsLocalPlayer) { local = Network.NetworkPlayer.All[i]; break; }
            }
            if (local == null) return;

            if (UnityEngine.Cursor.lockState == CursorLockMode.Locked) { UnityEngine.Cursor.lockState = CursorLockMode.None; UnityEngine.Cursor.visible = true; }

            float w = 460f, h = 170f;
            var rect = new Rect((Screen.width - w) * 0.5f, (Screen.height - h) * 0.5f, w, h);
            GUI.Box(rect, "LOBBY (fallback - UI Toolkit picker not visible)");
            GUILayout.BeginArea(new Rect(rect.x + 10, rect.y + 28, rect.width - 20, rect.height - 38));
            GUILayout.Label($"State: {gm.CurrentState}   You: {local.ClassType} on {local.Team}" +
                            (gm.CurrentState == GameMode.ExtractionHeistGameMode.GameState.PreMatch ? $"   starts in {Mathf.CeilToInt(gm.PhaseTimeRemaining ?? 0f)}s" : ""));
            GUILayout.BeginHorizontal();
            foreach (var cls in PickerClasses)
            {
                GUI.enabled = local.ClassType != cls;
                if (GUILayout.Button(cls.ToString(), GUILayout.Height(36))) local.RequestClass(cls);
            }
            GUI.enabled = true;
            GUILayout.EndHorizontal();
            if (gm.Object.HasStateAuthority && gm.CurrentState == GameMode.ExtractionHeistGameMode.GameState.WaitingForPlayers)
            {
                if (GUILayout.Button("Start match now", GUILayout.Height(32))) gm.StartMatch();
            }
            else
            {
                GUILayout.Label(gm.Object.HasStateAuthority ? "" : "Waiting for the host to start...");
            }
            GUILayout.EndArea();
        }

        private void OnGUI()
        {
            DrawFallbackLobby();
            if (!visible) return;

            if (style == null)
            {
                style = new GUIStyle(GUI.skin.box) { alignment = TextAnchor.UpperLeft, fontSize = 13, richText = true };
                style.normal.textColor = Color.white;
            }

            sb.Clear();
            AppendMatch();
            AppendLocalPlayer();
            AppendTeams();
            AppendZones();

            GUI.Box(new Rect(10, 10, 420, 440), sb.ToString(), style);
        }

        private void AppendMatch()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            var session = Network.GameSessionManager.Instance;
            sb.AppendLine($"<b>OUROBOROS DEV HUD</b>  (F1/Select toggles, Esc/Start frees cursor)  input: {(Core.LocalInputSource.UsingInputSystem ? "Input System" : "legacy")}{(Core.LocalInputSource.LastDeviceWasGamepad ? " [gamepad]" : "")}");
            if (session != null && session.Runner != null)
            {
                sb.AppendLine($"Session: {session.Runner.GameMode}  IsServer={session.Runner.IsServer}  Local={session.Runner.LocalPlayer}");
            }
            if (gm == null || gm.Object == null)
            {
                sb.AppendLine("Game mode: <i>not spawned</i>");
                return;
            }
            sb.AppendLine($"State: <b>{gm.CurrentState}</b>  Extraction open: {(gm.ExtractionOpen ? "YES" : "no")}");
            if (gm.MatchTimeRemaining.HasValue) sb.AppendLine($"Match time left: {gm.MatchTimeRemaining.Value:0}s");
            if (gm.PhaseTimeRemaining.HasValue) sb.AppendLine($"Phase time left: {gm.PhaseTimeRemaining.Value:0}s");
            if (gm.CurrentState == GameMode.ExtractionHeistGameMode.GameState.MatchEnded) sb.AppendLine($"<b>Winner: {gm.WinningTeam}</b>");
        }

        private void AppendLocalPlayer()
        {
            Network.NetworkPlayer local = null;
            for (int i = 0; i < Network.NetworkPlayer.All.Count; i++)
            {
                if (Network.NetworkPlayer.All[i] != null && Network.NetworkPlayer.All[i].IsLocalPlayer) { local = Network.NetworkPlayer.All[i]; break; }
            }
            sb.AppendLine();
            if (local == null)
            {
                sb.AppendLine("Local player: <i>none</i>");
                return;
            }

            sb.AppendLine($"<b>{local.DisplayName}</b>  {local.Team}  {local.ClassType}  {(local.IsAlive ? "alive" : "DEAD")}{(local.IsExtracted ? " EXTRACTED" : "")}");
            sb.AppendLine($"HP {local.Health:0}/{local.MaxHealth:0}   Stamina {local.Stamina:0}/{local.MaxStamina:0}   Loot {local.CarriedLoot}   K/D {local.Kills}/{local.Deaths}");
            sb.AppendLine($"Status: {local.Status}");
            if (!local.IsAlive && local.RespawnTimer.IsRunning)
            {
                var runner = local.Runner;
                float? t = local.RespawnTimer.RemainingTime(runner);
                if (t.HasValue) sb.AppendLine($"Respawn in {t.Value:0.0}s");
            }

            var cls = local.CurrentClass;
            for (int slot = 0; slot < Core.GameConstants.MAX_ABILITY_SLOTS; slot++)
            {
                var def = cls != null ? cls.GetAbilitySlot(slot) : default;
                float cd = local.AbilityCooldownRemaining(slot);
                string name = string.IsNullOrEmpty(def.Name) ? "-" : def.Name;
                sb.AppendLine($"  [{slot + 1}] {name,-18} {(cd > 0f ? $"{cd:0.0}s" : "ready")}  (cost {def.StaminaCost:0})");
            }
        }

        private void AppendTeams()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            var tm = Network.TeamManager.Instance;
            if (gm == null || gm.Object == null) return;
            sb.AppendLine();
            for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++)
            {
                var team = Core.TeamUtil.FromIndex(i);
                string status = "";
                if (tm != null && tm.Object != null)
                {
                    var s = tm.GetTeamStatus(team);
                    if (s.Total == 0) continue;
                    status = $"alive {s.Alive} extracted {s.Extracted} dead {s.Dead + s.AwaitingRespawn} carrying {s.CarriedLoot}";
                }
                sb.AppendLine($"{team,-12} {gm.TeamScores[i],5} pts  loot {gm.TeamLoot[i],4}  obj {gm.TeamObjectives[i]}  {status}");
            }
        }

        private void AppendZones()
        {
            sb.AppendLine();
            foreach (var obj in Core.SceneUtil.FindAll<GameMode.LootObjective>())
            {
                if (obj.Object == null) continue;
                string state = !obj.IsAvailable ? "depleted" : obj.CapturingTeam == Core.TeamID.None ? "idle" : $"{obj.CapturingTeam} {obj.ProgressNormalized:P0}{(obj.IsContested ? " CONTESTED" : "")}";
                sb.AppendLine($"{obj.ObjectiveName}: {state}");
            }
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null) return;
            int n = 0;
            foreach (var ep in gm.ExtractionPoints)
            {
                if (ep == null || ep.Object == null) continue;
                string state = !ep.IsActive ? "closed" : ep.CurrentExtractingTeam == Core.TeamID.None ? "open" : $"{ep.CurrentExtractingTeam} {ep.ProgressNormalized:P0}{(ep.IsContested ? " CONTESTED" : "")}";
                sb.AppendLine($"Extraction {++n}: {state}");
            }
        }
    }
}
