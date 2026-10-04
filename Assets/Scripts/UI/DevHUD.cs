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
        [SerializeField] private KeyCode toggleKey = KeyCode.F1;

        private readonly StringBuilder sb = new StringBuilder(1024);
        private GUIStyle style;

        private void Update()
        {
            if (Input.GetKeyDown(toggleKey)) visible = !visible;
        }

        private void OnGUI()
        {
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
            sb.AppendLine("<b>OUROBOROS DEV HUD</b>  (F1 toggles, Esc frees cursor)");
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
