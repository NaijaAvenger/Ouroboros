using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;
using Fusion;

namespace Ouroboros.UI
{
    /// <summary>
    /// In-game HUD built entirely in code with UI Toolkit (no UXML/USS assets to maintain).
    /// Shows: match state and timers, team scoreboard, local vitals, ability slots with cooldowns,
    /// status chips, loot / K-D, the nearest objective or extraction zone with progress, a kill feed,
    /// announcement banners, a death overlay with respawn countdown, and the end-of-match screen.
    ///
    /// Everything shown is replicated state, so the HUD is correct on host, client and late joiners.
    /// Requires a <see cref="UIDocument"/> with PanelSettings (the Phase 0 scene builder creates them).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class HeistHUD : MonoBehaviour
    {
        [SerializeField] private float refreshRate = 20f;
        [SerializeField] private float feedLifetime = 6f;
        [SerializeField] private int feedMaxEntries = 6;
        [SerializeField] private float bannerDuration = 3f;

        private UIDocument document;
        private VisualElement root;

        // Top
        private Label stateLabel, timerLabel, bannerLabel;
        // Scoreboard
        private readonly TeamRow[] teamRows = new TeamRow[Core.GameConstants.MAX_TEAMS];
        // Vitals
        private Label nameLabel, healthText, staminaText, statusLabel, lootLabel, kdLabel;
        private VisualElement healthFill, staminaFill;
        // Abilities
        private readonly AbilitySlotView[] slots = new AbilitySlotView[Core.GameConstants.MAX_ABILITY_SLOTS];
        // Zone
        private VisualElement zonePanel, zoneFill;
        private Label zoneTitle, zoneHint;
        // Feed / overlays
        private VisualElement feed, deathOverlay, endScreen;
        private Label deathTitle, deathSub, endTitle, endTable;
        private readonly List<FeedEntry> feedEntries = new List<FeedEntry>();

        private float nextRefresh;
        private float bannerUntil;
        private Network.NetworkPlayer local;

        private static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.55f);
        private static readonly Color HealthColor = new Color(0.85f, 0.2f, 0.2f);
        private static readonly Color StaminaColor = new Color(0.2f, 0.7f, 0.9f);
        private static readonly Color LootColor = new Color(1f, 0.82f, 0.2f);

        private struct FeedEntry { public VisualElement Element; public float Expires; }

        private class TeamRow { public VisualElement Root; public Label Name, Score, Loot, Status; }

        private class AbilitySlotView { public VisualElement Root, CooldownFill; public Label Key, Name, Cooldown, Cost; }

        // ------------------------------------------------------------------

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            root = document.rootVisualElement;
            if (root == null)
            {
                Debug.LogWarning("[HeistHUD] UIDocument has no PanelSettings; run Ouroboros > Setup > Create Dev Scene.");
                enabled = false;
                return;
            }

            root.Clear();
            Build();

            GameMode.ExtractionHeistGameMode.StateChanged += OnStateChanged;
            GameMode.ExtractionHeistGameMode.ExtractionOpened += OnExtractionOpened;
            GameMode.ExtractionHeistGameMode.TeamExtractedEvent += OnTeamExtracted;
            GameMode.LootObjective.Completed += OnObjectiveCompleted;
            Network.NetworkPlayer.PlayerKilled += OnPlayerKilled;
        }

        private void OnDisable()
        {
            GameMode.ExtractionHeistGameMode.StateChanged -= OnStateChanged;
            GameMode.ExtractionHeistGameMode.ExtractionOpened -= OnExtractionOpened;
            GameMode.ExtractionHeistGameMode.TeamExtractedEvent -= OnTeamExtracted;
            GameMode.LootObjective.Completed -= OnObjectiveCompleted;
            Network.NetworkPlayer.PlayerKilled -= OnPlayerKilled;
        }

        private void Update()
        {
            if (root == null) return;
            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, refreshRate);
                Refresh();
            }
            ExpireFeed();
            if (bannerLabel.resolvedStyle.display == DisplayStyle.Flex && Time.unscaledTime > bannerUntil)
            {
                bannerLabel.style.display = DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------
        // Build

        private void Build()
        {
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;

            // Top centre: state + timer
            var top = Panel(PanelBg);
            top.style.position = Position.Absolute;
            top.style.top = 12; top.style.left = Length.Percent(50);
            top.style.translate = new Translate(Length.Percent(-50), 0);
            top.style.alignItems = Align.Center;
            top.style.minWidth = 260;
            stateLabel = Text("", 16, Color.white, bold: true);
            timerLabel = Text("", 22, Color.white, bold: true);
            top.Add(stateLabel); top.Add(timerLabel);
            root.Add(top);

            // Banner under the timer
            bannerLabel = Text("", 26, LootColor, bold: true);
            bannerLabel.style.position = Position.Absolute;
            bannerLabel.style.top = 90; bannerLabel.style.left = Length.Percent(50);
            bannerLabel.style.translate = new Translate(Length.Percent(-50), 0);
            bannerLabel.style.display = DisplayStyle.None;
            bannerLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Add(bannerLabel);

            // Top right: scoreboard
            var board = Panel(PanelBg);
            board.style.position = Position.Absolute;
            board.style.top = 12; board.style.right = 12; board.style.minWidth = 300;
            board.Add(Text("TEAMS", 12, new Color(1f, 1f, 1f, 0.6f), bold: true));
            for (int i = 0; i < teamRows.Length; i++)
            {
                var row = new TeamRow { Root = new VisualElement() };
                row.Root.style.flexDirection = FlexDirection.Row;
                row.Root.style.justifyContent = Justify.SpaceBetween;
                row.Root.style.marginTop = 2;
                row.Name = Text("", 14, Player.TeamSpawnPoint.TeamColor(Core.TeamUtil.FromIndex(i)), bold: true);
                row.Name.style.minWidth = 100;
                row.Score = Text("", 14, Color.white, bold: true); row.Score.style.minWidth = 60; row.Score.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Loot = Text("", 13, LootColor); row.Loot.style.minWidth = 60; row.Loot.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Status = Text("", 12, new Color(1f, 1f, 1f, 0.7f)); row.Status.style.minWidth = 70; row.Status.style.unityTextAlign = TextAnchor.MiddleRight;
                row.Root.Add(row.Name); row.Root.Add(row.Score); row.Root.Add(row.Loot); row.Root.Add(row.Status);
                board.Add(row.Root);
                teamRows[i] = row;
            }
            root.Add(board);

            // Top left: kill feed
            feed = new VisualElement();
            feed.style.position = Position.Absolute;
            feed.style.top = 12; feed.style.left = 12; feed.style.minWidth = 240;
            feed.pickingMode = PickingMode.Ignore;
            root.Add(feed);

            // Bottom left: vitals
            var vitals = Panel(PanelBg);
            vitals.style.position = Position.Absolute;
            vitals.style.left = 12; vitals.style.bottom = 12; vitals.style.minWidth = 300;
            nameLabel = Text("", 15, Color.white, bold: true);
            vitals.Add(nameLabel);
            healthFill = Bar(vitals, HealthColor, out healthText);
            staminaFill = Bar(vitals, StaminaColor, out staminaText);
            statusLabel = Text("", 12, new Color(1f, 1f, 1f, 0.8f));
            statusLabel.style.marginTop = 4;
            vitals.Add(statusLabel);
            root.Add(vitals);

            // Bottom centre: abilities
            var abilities = new VisualElement();
            abilities.style.position = Position.Absolute;
            abilities.style.bottom = 12; abilities.style.left = Length.Percent(50);
            abilities.style.translate = new Translate(Length.Percent(-50), 0);
            abilities.style.flexDirection = FlexDirection.Row;
            for (int i = 0; i < slots.Length; i++)
            {
                var slot = new AbilitySlotView { Root = Panel(PanelBg) };
                slot.Root.style.width = 96; slot.Root.style.height = 72; slot.Root.style.marginLeft = 4; slot.Root.style.marginRight = 4;
                slot.Root.style.overflow = Overflow.Hidden;
                slot.Root.style.justifyContent = Justify.SpaceBetween;
                slot.CooldownFill = new VisualElement();
                slot.CooldownFill.style.position = Position.Absolute;
                slot.CooldownFill.style.left = 0; slot.CooldownFill.style.right = 0; slot.CooldownFill.style.bottom = 0;
                slot.CooldownFill.style.height = Length.Percent(0);
                slot.CooldownFill.style.backgroundColor = new Color(1f, 1f, 1f, 0.18f);
                slot.Root.Add(slot.CooldownFill);
                slot.Key = Text(Core.LocalInputSource.Hint((Core.InputButtons)((int)Core.InputButtons.Ability1 + i)), 12, new Color(1f, 1f, 1f, 0.6f), bold: true);
                slot.Name = Text("", 12, Color.white, bold: true);
                slot.Name.style.whiteSpace = WhiteSpace.Normal;
                slot.Cooldown = Text("", 14, LootColor, bold: true);
                slot.Cost = Text("", 10, StaminaColor);
                slot.Root.Add(slot.Key); slot.Root.Add(slot.Name); slot.Root.Add(slot.Cooldown); slot.Root.Add(slot.Cost);
                abilities.Add(slot.Root);
                slots[i] = slot;
            }
            root.Add(abilities);

            // Bottom right: loot + K/D
            var econ = Panel(PanelBg);
            econ.style.position = Position.Absolute;
            econ.style.right = 12; econ.style.bottom = 12; econ.style.minWidth = 160;
            econ.style.alignItems = Align.FlexEnd;
            lootLabel = Text("", 20, LootColor, bold: true);
            kdLabel = Text("", 13, Color.white);
            econ.Add(lootLabel); econ.Add(kdLabel);
            root.Add(econ);

            // Centre-bottom: zone prompt
            zonePanel = Panel(PanelBg);
            zonePanel.style.position = Position.Absolute;
            zonePanel.style.bottom = 110; zonePanel.style.left = Length.Percent(50);
            zonePanel.style.translate = new Translate(Length.Percent(-50), 0);
            zonePanel.style.minWidth = 320; zonePanel.style.alignItems = Align.Center;
            zonePanel.style.display = DisplayStyle.None;
            zoneTitle = Text("", 15, Color.white, bold: true);
            zoneHint = Text("", 12, new Color(1f, 1f, 1f, 0.8f));
            zonePanel.Add(zoneTitle);
            zoneFill = Bar(zonePanel, LootColor, out _);
            zonePanel.Add(zoneHint);
            root.Add(zonePanel);

            // Death overlay
            deathOverlay = Overlay(new Color(0.3f, 0f, 0f, 0.45f));
            deathTitle = Text("YOU DIED", 48, Color.white, bold: true);
            deathSub = Text("", 20, Color.white);
            deathOverlay.Add(deathTitle); deathOverlay.Add(deathSub);
            root.Add(deathOverlay);

            // End screen
            endScreen = Overlay(new Color(0f, 0f, 0f, 0.7f));
            endTitle = Text("", 44, LootColor, bold: true);
            endTable = Text("", 18, Color.white);
            endTable.style.unityTextAlign = TextAnchor.MiddleLeft;
            endTable.style.marginTop = 16;
            endScreen.Add(endTitle); endScreen.Add(endTable);
            root.Add(endScreen);
        }

        private static VisualElement Panel(Color bg)
        {
            var ve = new VisualElement();
            ve.style.backgroundColor = bg;
            ve.style.paddingLeft = 10; ve.style.paddingRight = 10; ve.style.paddingTop = 6; ve.style.paddingBottom = 6;
            ve.style.borderTopLeftRadius = 6; ve.style.borderTopRightRadius = 6; ve.style.borderBottomLeftRadius = 6; ve.style.borderBottomRightRadius = 6;
            ve.pickingMode = PickingMode.Ignore;
            return ve;
        }

        private static VisualElement Overlay(Color bg)
        {
            var ve = new VisualElement();
            ve.style.position = Position.Absolute;
            ve.style.left = 0; ve.style.top = 0; ve.style.right = 0; ve.style.bottom = 0;
            ve.style.backgroundColor = bg;
            ve.style.alignItems = Align.Center; ve.style.justifyContent = Justify.Center;
            ve.style.display = DisplayStyle.None;
            ve.pickingMode = PickingMode.Ignore;
            return ve;
        }

        private static Label Text(string text, int size, Color color, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            label.style.marginTop = 0; label.style.marginBottom = 0; label.style.marginLeft = 0; label.style.marginRight = 0;
            label.style.paddingTop = 0; label.style.paddingBottom = 0; label.style.paddingLeft = 0; label.style.paddingRight = 0;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.pickingMode = PickingMode.Ignore;
            return label;
        }

        /// <summary>Adds a horizontal bar to <paramref name="parent"/> and returns its fill element.</summary>
        private static VisualElement Bar(VisualElement parent, Color color, out Label overlayText)
        {
            var track = new VisualElement();
            track.style.height = 16; track.style.marginTop = 4;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            track.style.borderTopLeftRadius = 3; track.style.borderTopRightRadius = 3; track.style.borderBottomLeftRadius = 3; track.style.borderBottomRightRadius = 3;
            track.style.overflow = Overflow.Hidden;
            track.style.width = Length.Percent(100);
            track.pickingMode = PickingMode.Ignore;

            var fill = new VisualElement();
            fill.style.position = Position.Absolute;
            fill.style.left = 0; fill.style.top = 0; fill.style.bottom = 0;
            fill.style.width = Length.Percent(100);
            fill.style.backgroundColor = color;
            fill.pickingMode = PickingMode.Ignore;
            track.Add(fill);

            overlayText = Text("", 11, Color.white, bold: true);
            overlayText.style.position = Position.Absolute;
            overlayText.style.left = 0; overlayText.style.right = 0; overlayText.style.top = 0; overlayText.style.bottom = 0;
            overlayText.style.unityTextAlign = TextAnchor.MiddleCenter;
            track.Add(overlayText);

            parent.Add(track);
            return fill;
        }

        // ------------------------------------------------------------------
        // Refresh

        private void Refresh()
        {
            if (local == null || local.Object == null) local = FindLocal();

            RefreshMatch();
            RefreshTeams();
            RefreshLocal();
            RefreshZone();
            RefreshOverlays();
        }

        private void RefreshMatch()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm == null || gm.Object == null)
            {
                stateLabel.text = "Connecting...";
                timerLabel.text = "";
                return;
            }

            switch (gm.CurrentState)
            {
                case GameMode.ExtractionHeistGameMode.GameState.WaitingForPlayers:
                    stateLabel.text = "Waiting for players";
                    timerLabel.text = "";
                    break;
                case GameMode.ExtractionHeistGameMode.GameState.PreMatch:
                    stateLabel.text = "Match starting";
                    timerLabel.text = Fmt(gm.PhaseTimeRemaining);
                    break;
                case GameMode.ExtractionHeistGameMode.GameState.InProgress:
                    stateLabel.text = gm.ExtractionOpen ? "Extraction OPEN" : "Heist in progress";
                    timerLabel.text = Fmt(gm.MatchTimeRemaining);
                    break;
                case GameMode.ExtractionHeistGameMode.GameState.Extraction:
                    stateLabel.text = "EXTRACT NOW";
                    timerLabel.text = Fmt(gm.PhaseTimeRemaining);
                    break;
                case GameMode.ExtractionHeistGameMode.GameState.MatchEnded:
                    stateLabel.text = "Match over";
                    timerLabel.text = "";
                    break;
            }
        }

        private void RefreshTeams()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            var tm = Network.TeamManager.Instance;
            for (int i = 0; i < teamRows.Length; i++)
            {
                var row = teamRows[i];
                var team = Core.TeamUtil.FromIndex(i);
                bool present = tm != null && tm.Object != null && tm.GetTeamPlayerCount(team) > 0;
                row.Root.style.display = present ? DisplayStyle.Flex : DisplayStyle.None;
                if (!present) continue;

                var s = tm.GetTeamStatus(team);
                row.Name.text = TeamName(team) + (local != null && local.Team == team ? " ★" : "");
                row.Score.text = gm != null && gm.Object != null ? gm.TeamScores[i].ToString() : "0";
                row.Loot.text = gm != null && gm.Object != null ? $"${gm.TeamLoot[i]}" : "";
                row.Status.text = s.Extracted == s.Total && s.Total > 0 ? "out" : $"{s.Alive}/{s.Total} up";
            }
        }

        private void RefreshLocal()
        {
            if (local == null || local.Object == null)
            {
                nameLabel.text = "Spawning...";
                return;
            }

            nameLabel.text = $"{local.DisplayName}  ·  {local.ClassType}  ·  {TeamName(local.Team)}";
            SetBar(healthFill, local.Health, local.MaxHealth);
            healthText.text = $"{local.Health:0} / {local.MaxHealth:0}";
            SetBar(staminaFill, local.Stamina, local.MaxStamina);
            staminaText.text = $"{local.Stamina:0}";

            statusLabel.text = StatusText(local.Status);
            lootLabel.text = $"${local.CarriedLoot}";
            kdLabel.text = $"K {local.Kills}  D {local.Deaths}";

            var cls = local.CurrentClass;
            for (int i = 0; i < slots.Length; i++)
            {
                var view = slots[i];
                var def = cls != null ? cls.GetAbilitySlot(i) : default;
                bool has = !string.IsNullOrEmpty(def.Name);
                view.Root.style.display = has ? DisplayStyle.Flex : DisplayStyle.None;
                if (!has) continue;

                view.Name.text = def.Name;
                view.Key.text = Core.LocalInputSource.Hint((Core.InputButtons)((int)Core.InputButtons.Ability1 + i));
                float remaining = local.AbilityCooldownRemaining(i);
                float frac = def.Cooldown > 0f ? Mathf.Clamp01(remaining / def.Cooldown) : 0f;
                view.CooldownFill.style.height = Length.Percent(frac * 100f);
                view.Cooldown.text = remaining > 0f ? $"{remaining:0.0}s" : "";
                bool affordable = local.Stamina >= def.StaminaCost;
                view.Cost.text = def.StaminaCost > 0f ? $"{def.StaminaCost:0} stam" : "";
                view.Cost.style.color = affordable ? StaminaColor : HealthColor;
                view.Root.style.opacity = local.HasStatus(Core.StatusFlags.EMPDisabled) ? 0.35f : 1f;
            }
        }

        private void RefreshZone()
        {
            if (local == null || local.Object == null || !local.IsActiveInMatch)
            {
                zonePanel.style.display = DisplayStyle.None;
                return;
            }

            Vector3 pos = local.transform.position;

            // Nearest objective in (or nearly in) range
            GameMode.LootObjective bestObj = null; float bestObjSq = float.MaxValue;
            foreach (var obj in GameMode.LootObjective.All)
            {
                if (obj == null || obj.Object == null) continue;
                float r = obj.InteractRadius * 1.6f;
                float dSq = (obj.transform.position - pos).sqrMagnitude;
                if (dSq <= r * r && dSq < bestObjSq) { bestObj = obj; bestObjSq = dSq; }
            }

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            GameMode.ExtractionPoint bestEp = null; float bestEpSq = float.MaxValue;
            if (gm != null)
            {
                foreach (var ep in gm.ExtractionPoints)
                {
                    if (ep == null || ep.Object == null) continue;
                    float r = ep.ExtractionRadius * 1.6f;
                    float dSq = (ep.transform.position - pos).sqrMagnitude;
                    if (dSq <= r * r && dSq < bestEpSq) { bestEp = ep; bestEpSq = dSq; }
                }
            }

            if (bestObj == null && bestEp == null)
            {
                zonePanel.style.display = DisplayStyle.None;
                return;
            }

            zonePanel.style.display = DisplayStyle.Flex;

            if (bestObj != null && (bestEp == null || bestObjSq <= bestEpSq))
            {
                zoneTitle.text = bestObj.ObjectiveName + $"  (${bestObj.LootValue})";
                SetBar(zoneFill, bestObj.ProgressNormalized, 1f);
                if (!bestObj.IsAvailable) zoneHint.text = "Depleted";
                else if (!bestObj.IsUnlocked) zoneHint.text = "Locked - needs a Hacker";
                else if (bestObj.CapturingTeam == Core.TeamID.None) zoneHint.text = $"Hold {Core.LocalInputSource.Hint(Core.InputButtons.Interact)} to crack";
                else if (bestObj.IsContested) zoneHint.text = $"{TeamName(bestObj.CapturingTeam)} cracking - CONTESTED";
                else zoneHint.text = bestObj.CapturingTeam == local.Team ? $"Cracking... keep holding {Core.LocalInputSource.Hint(Core.InputButtons.Interact)}" : $"{TeamName(bestObj.CapturingTeam)} is cracking it!";
            }
            else
            {
                zoneTitle.text = "Extraction point";
                SetBar(zoneFill, bestEp.ProgressNormalized, 1f);
                if (gm != null && !gm.ExtractionOpen) zoneHint.text = "Not open yet";
                else if (!bestEp.IsActive) zoneHint.text = "Closed - reopening soon";
                else if (bestEp.CurrentExtractingTeam == Core.TeamID.None) zoneHint.text = gm != null && gm.CanExtract(local) ? "Stand here to extract" : "Not eligible to extract";
                else if (bestEp.IsContested) zoneHint.text = "CONTESTED - clear the zone";
                else zoneHint.text = bestEp.CurrentExtractingTeam == local.Team ? "Extracting... hold position" : $"{TeamName(bestEp.CurrentExtractingTeam)} is extracting!";
            }
        }

        private void RefreshOverlays()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            bool ended = gm != null && gm.Object != null && gm.CurrentState == GameMode.ExtractionHeistGameMode.GameState.MatchEnded;

            if (ended)
            {
                endScreen.style.display = DisplayStyle.Flex;
                deathOverlay.style.display = DisplayStyle.None;
                endTitle.text = gm.WinningTeam == Core.TeamID.None ? "Match over" : $"{TeamName(gm.WinningTeam)} wins the heist";
                endTitle.style.color = gm.WinningTeam == Core.TeamID.None ? Color.white : Player.TeamSpawnPoint.TeamColor(gm.WinningTeam);
                var sb = new System.Text.StringBuilder();
                var tm = Network.TeamManager.Instance;
                for (int i = 0; i < Core.GameConstants.MAX_TEAMS; i++)
                {
                    var team = Core.TeamUtil.FromIndex(i);
                    if (tm != null && tm.Object != null && tm.GetTeamPlayerCount(team) == 0) continue;
                    sb.AppendLine($"{TeamName(team),-14} {gm.TeamScores[i],6} pts   ${gm.TeamLoot[i],-6} banked   {gm.TeamExtractedCount[i]} extracted   {gm.TeamObjectives[i]} objectives");
                }
                endTable.text = sb.ToString().TrimEnd();
                return;
            }
            endScreen.style.display = DisplayStyle.None;

            if (local == null || local.Object == null)
            {
                deathOverlay.style.display = DisplayStyle.None;
                return;
            }

            if (local.IsExtracted)
            {
                deathOverlay.style.display = DisplayStyle.Flex;
                deathOverlay.style.backgroundColor = new Color(0f, 0.25f, 0.1f, 0.45f);
                deathTitle.text = "EXTRACTED";
                deathSub.text = "Your loot is banked. Watch your team finish the job.";
            }
            else if (!local.IsAlive)
            {
                deathOverlay.style.display = DisplayStyle.Flex;
                deathOverlay.style.backgroundColor = new Color(0.3f, 0f, 0f, 0.45f);
                deathTitle.text = "YOU DIED";
                float? t = local.RespawnTimer.IsRunning ? local.RespawnTimer.RemainingTime(local.Runner) : null;
                deathSub.text = t.HasValue ? $"Respawning in {Mathf.CeilToInt(t.Value)}s" : "No respawns left";
            }
            else
            {
                deathOverlay.style.display = DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------
        // Events

        private void OnStateChanged(GameMode.ExtractionHeistGameMode.GameState state)
        {
            switch (state)
            {
                case GameMode.ExtractionHeistGameMode.GameState.PreMatch:   ShowBanner("Get ready"); break;
                case GameMode.ExtractionHeistGameMode.GameState.InProgress: ShowBanner("GO - crack the vaults"); break;
                case GameMode.ExtractionHeistGameMode.GameState.Extraction: ShowBanner("EXTRACTION PHASE - get out!"); break;
            }
        }

        private void OnExtractionOpened() => ShowBanner("Extraction points are open");

        private void OnTeamExtracted(Core.TeamID team, int members, int loot)
        {
            AddFeed($"{TeamName(team)} extracted {members} with ${loot}", Player.TeamSpawnPoint.TeamColor(team));
        }

        private void OnObjectiveCompleted(GameMode.LootObjective objective, Core.TeamID team, int loot)
        {
            AddFeed($"{TeamName(team)} cracked {objective.ObjectiveName} (${loot})", Player.TeamSpawnPoint.TeamColor(team));
        }

        private void OnPlayerKilled(Network.NetworkPlayer victim, PlayerRef killerRef)
        {
            var killer = Network.NetworkPlayer.Resolve(killerRef);
            string killerName = killer != null ? killer.DisplayName.ToString() : "Environment";
            Color color = killer != null ? Player.TeamSpawnPoint.TeamColor(killer.Team) : Color.gray;
            AddFeed($"{killerName}  ✕  {victim.DisplayName}", color);
        }

        private void ShowBanner(string text)
        {
            bannerLabel.text = text;
            bannerLabel.style.display = DisplayStyle.Flex;
            bannerUntil = Time.unscaledTime + bannerDuration;
        }

        private void AddFeed(string text, Color color)
        {
            var entry = Panel(PanelBg);
            entry.style.marginBottom = 3;
            var label = Text(text, 13, color, bold: true);
            entry.Add(label);
            feed.Add(entry);
            feedEntries.Add(new FeedEntry { Element = entry, Expires = Time.unscaledTime + feedLifetime });

            while (feedEntries.Count > feedMaxEntries)
            {
                feed.Remove(feedEntries[0].Element);
                feedEntries.RemoveAt(0);
            }
        }

        private void ExpireFeed()
        {
            for (int i = feedEntries.Count - 1; i >= 0; i--)
            {
                if (Time.unscaledTime >= feedEntries[i].Expires)
                {
                    feed.Remove(feedEntries[i].Element);
                    feedEntries.RemoveAt(i);
                }
            }
        }

        // ------------------------------------------------------------------
        // Helpers

        private static void SetBar(VisualElement fill, float value, float max)
        {
            float frac = max > 0f ? Mathf.Clamp01(value / max) : 0f;
            fill.style.width = Length.Percent(frac * 100f);
        }

        private static string Fmt(float? seconds)
        {
            if (!seconds.HasValue) return "";
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds.Value));
            return $"{s / 60}:{s % 60:00}";
        }

        private static string TeamName(Core.TeamID team)
        {
            switch (team)
            {
                case Core.TeamID.TeamAlpha:   return "Alpha";
                case Core.TeamID.TeamBravo:   return "Bravo";
                case Core.TeamID.TeamCharlie: return "Charlie";
                case Core.TeamID.TeamDelta:   return "Delta";
                default: return "-";
            }
        }

        private static string StatusText(Core.StatusFlags status)
        {
            if (status == Core.StatusFlags.None) return "";
            var parts = new List<string>();
            if ((status & Core.StatusFlags.Shielded) != 0)    parts.Add("SHIELD");
            if ((status & Core.StatusFlags.DamageBoost) != 0) parts.Add("DMG+");
            if ((status & Core.StatusFlags.Stealthed) != 0)   parts.Add("STEALTH");
            if ((status & Core.StatusFlags.Flashed) != 0)     parts.Add("FLASHED");
            if ((status & Core.StatusFlags.EMPDisabled) != 0) parts.Add("EMP");
            if ((status & Core.StatusFlags.Revealed) != 0)    parts.Add("REVEALED");
            if ((status & Core.StatusFlags.Burning) != 0)     parts.Add("BURNING");
            if ((status & Core.StatusFlags.Sprinting) != 0)   parts.Add("sprint");
            if ((status & Core.StatusFlags.Interacting) != 0) parts.Add("interact");
            return string.Join("  ", parts);
        }

        private static Network.NetworkPlayer FindLocal()
        {
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].IsLocalPlayer) return all[i];
            }
            return null;
        }
    }
}
