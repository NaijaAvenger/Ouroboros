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

        private const string RootName = "heist-hud";

        private UIDocument document;
        private VisualElement root;
        private VisualElement damageFlash;
        private float damageFlashAlpha;
        private Network.NetworkPlayer damageSubscribed;

        // Top
        private Label stateLabel, timerLabel, bannerLabel, alarmLabel;
        private VisualElement alarmFill;
        // Scoreboard
        private readonly TeamRow[] teamRows = new TeamRow[Core.GameConstants.MAX_TEAMS];
        // Vitals
        private Label nameLabel, healthText, staminaText, statusLabel, lootLabel, kdLabel, weaponLabel, ammoLabel;
        private VisualElement healthFill, staminaFill;
        private Label gadgetLabel; // (v0.14) the gadget in the Gadget slot and whether it is ready
        // (v0.12, M4) crew identity on the HUD
        private VisualElement crewStrip, vitalsPanel, topPanel;
        private Core.TeamID skinnedTeam = Core.TeamID.None;
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
            var panelRoot = document.rootVisualElement;
            if (panelRoot == null)
            {
                Debug.LogError("[HeistHUD] The HUD's UIDocument has no Panel Settings, so nothing can render. Assign " +
                               "Assets/Ouroboros/UI/HeistPanelSettings.asset to it, or re-run Ouroboros > Setup > Create Dev Scene (it repairs this).");
                enabled = false;
                return;
            }

            // [v0.3] root.Clear() replaced: the HUD owns a named child so other panels (ClassPickerUI) can share the document
            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName };
            panelRoot.Add(root);
            Build();

            // (v0.9) crosshair, hit markers and damage direction live in their own component on the same document
            if (GetComponent<CombatHUD>() == null) gameObject.AddComponent<CombatHUD>();
            // (v0.11) quick-hack prompt, deep-hack console and Net Sight markers
            if (GetComponent<HackHUD>() == null) gameObject.AddComponent<HackHUD>();
            // (v0.12, M4) crew radio lines
            if (GetComponent<CrewCallouts>() == null) gameObject.AddComponent<CrewCallouts>();
            // (v0.13) markers over crewmates, so friend and rival are never confused
            if (GetComponent<TeammateMarkers>() == null) gameObject.AddComponent<TeammateMarkers>();
            // (v0.13, M5) main menu, pause, settings, controls and profile
            if (GetComponent<GameMenu>() == null) gameObject.AddComponent<GameMenu>();
            // (v0.14) numbers on the hits you land, and a recap of what killed you
            if (GetComponent<DamageNumbers>() == null) gameObject.AddComponent<DamageNumbers>();
            if (GetComponent<DeathRecap>() == null) gameObject.AddComponent<DeathRecap>();
            if (GetComponent<MapHUD>() == null) gameObject.AddComponent<MapHUD>(); // zone name, event and scanner markers

            var ps = document.panelSettings;
            Debug.Log($"[HeistHUD] Enabled. PanelSettings='{(ps != null ? ps.name : "NONE")}', theme='{(ps != null && ps.themeStyleSheet != null ? ps.themeStyleSheet.name : "NONE")}', " +
                      $"elements={root.childCount}, screen={Screen.width}x{Screen.height}");

            GameMode.ExtractionHeistGameMode.StateChanged += OnStateChanged;
            GameMode.ExtractionHeistGameMode.ExtractionOpened += OnExtractionOpened;
            GameMode.ExtractionHeistGameMode.TeamExtractedEvent += OnTeamExtracted;
            GameMode.LootObjective.Completed += OnObjectiveCompleted;
            Network.NetworkPlayer.PlayerKilled += OnPlayerKilled;
            GameMode.ExtractionHeistGameMode.GetawayCalled += OnGetawayCalled; // (v0.10)
            GameMode.ExtractionHeistGameMode.CrewWiped += OnCrewWiped;
            GameMode.LootDrop.PickedUp += OnLootPickedUp;
            GameMode.LootObjective.TakenOver += OnVaultTakenOver; // (v0.11)
            Interaction.PowerNode.PowerCut += OnPowerCut;
            Interaction.QuickHack.Completed += OnQuickHack;
            Interaction.GravityZone.Switched += OnGravitySwitched; // (v0.12)
            // (v0.14) map events and security
            GameMode.MapEventDirector.Warned += OnMapEventWarned;
            GameMode.MapEventDirector.Fired += OnMapEventFired;
            Interaction.LaserGrid.Tripped += OnLaserTripped;
            Network.NetworkPlayer.Jammed += OnJammed;
        }

        private void OnDisable()
        {
            GameMode.LootObjective.TakenOver -= OnVaultTakenOver;
            Interaction.PowerNode.PowerCut -= OnPowerCut;
            Interaction.QuickHack.Completed -= OnQuickHack;
            Interaction.GravityZone.Switched -= OnGravitySwitched;
            GameMode.MapEventDirector.Warned -= OnMapEventWarned;
            GameMode.MapEventDirector.Fired -= OnMapEventFired;
            Interaction.LaserGrid.Tripped -= OnLaserTripped;
            Network.NetworkPlayer.Jammed -= OnJammed;
            GameMode.ExtractionHeistGameMode.GetawayCalled -= OnGetawayCalled;
            GameMode.ExtractionHeistGameMode.CrewWiped -= OnCrewWiped;
            GameMode.LootDrop.PickedUp -= OnLootPickedUp;
            GameMode.ExtractionHeistGameMode.StateChanged -= OnStateChanged;
            GameMode.ExtractionHeistGameMode.ExtractionOpened -= OnExtractionOpened;
            GameMode.ExtractionHeistGameMode.TeamExtractedEvent -= OnTeamExtracted;
            GameMode.LootObjective.Completed -= OnObjectiveCompleted;
            Network.NetworkPlayer.PlayerKilled -= OnPlayerKilled;
            if (damageSubscribed != null) { damageSubscribed.Damaged -= OnLocalDamaged; damageSubscribed = null; }
            root?.RemoveFromHierarchy();
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

            if (damageFlashAlpha > 0f)
            {
                damageFlashAlpha = Mathf.Max(0f, damageFlashAlpha - Time.unscaledDeltaTime * 1.5f);
                damageFlash.style.opacity = damageFlashAlpha;
                damageFlash.style.display = damageFlashAlpha > 0f ? DisplayStyle.Flex : DisplayStyle.None;
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
            alarmFill = Bar(top, new Color(0.9f, 0.3f, 0.1f), out alarmLabel);
            alarmFill.parent.style.width = 240;
            root.Add(top);
            topPanel = top;

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
            vitalsPanel = vitals;
            // (v0.12, M4) a band of the crew's pattern across the top of the vitals panel
            crewStrip = new VisualElement { pickingMode = PickingMode.Ignore };
            crewStrip.style.height = 20; crewStrip.style.marginBottom = 5;
            crewStrip.style.display = DisplayStyle.None;
            vitals.Add(crewStrip);
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
            gadgetLabel = Text("", 13, new Color(0.5f, 0.95f, 1f), bold: true); // (v0.14)
            gadgetLabel.style.marginTop = 6;
            weaponLabel = Text("", 13, new Color(1f, 1f, 1f, 0.8f), bold: true);
            weaponLabel.style.marginTop = 6;
            ammoLabel = Text("", 24, Color.white, bold: true);
            // [v0.13] econ.Add(lootLabel); econ.Add(kdLabel); econ.Add(weaponLabel); econ.Add(ammoLabel);
            econ.Add(lootLabel); econ.Add(kdLabel); econ.Add(gadgetLabel); econ.Add(weaponLabel); econ.Add(ammoLabel);
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

            // Damage flash (red vignette-ish full-screen tint)
            damageFlash = Overlay(new Color(0.8f, 0f, 0f, 0.35f));
            damageFlash.style.opacity = 0f;
            root.Add(damageFlash);

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

        /// <summary>(v0.12, M4) Dresses the HUD in the local crew's pattern and colour.</summary>
        private void ApplyCrewSkin(Core.TeamID team)
        {
            skinnedTeam = team;
            bool valid = Core.TeamUtil.IsValid(team);
            crewStrip.style.display = valid ? DisplayStyle.Flex : DisplayStyle.None;
            CrewSkin.FillPattern(crewStrip, team, 20, 15, vertical: false);
            CrewSkin.AccentLeft(vitalsPanel, team);
            CrewSkin.AccentBottom(topPanel, team);
            nameLabel.style.color = valid ? CrewSkin.TextColour(team) : Color.white;
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

        private bool loggedFirstRefresh;

        private void Refresh()
        {
            if (local == null || local.Object == null) local = FindLocal();
            if (!loggedFirstRefresh && local != null)
            {
                loggedFirstRefresh = true;
                var gmLog = GameMode.ExtractionHeistGameMode.Instance;
                Debug.Log($"[HeistHUD] Local player found: {local.DisplayName} ({local.Team}, {local.ClassType}); game mode state: " +
                          $"{(gmLog != null && gmLog.Object != null ? gmLog.CurrentState.ToString() : "no game mode")}; panel size={root.resolvedStyle.width}x{root.resolvedStyle.height}");
            }
            if (local != damageSubscribed)
            {
                if (damageSubscribed != null) damageSubscribed.Damaged -= OnLocalDamaged;
                damageSubscribed = local;
                if (damageSubscribed != null) damageSubscribed.Damaged += OnLocalDamaged;
            }

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

            var alarm = GameMode.AlarmSystem.Instance;
            if (alarm != null && alarm.Object != null)
            {
                alarmFill.parent.style.display = DisplayStyle.Flex;
                SetBar(alarmFill, alarm.Normalized, 1f);
                alarmLabel.text = $"ALARM  {GameMode.AlarmSystem.TierName(alarm.Tier)}";
                alarmFill.style.backgroundColor = alarm.Lockdown ? HealthColor : new Color(0.9f, 0.5f, 0.1f);
            }
            else
            {
                alarmFill.parent.style.display = DisplayStyle.None;
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
                    // [v0.9] stateLabel.text = gm.ExtractionOpen ? "Extraction OPEN" : "Heist in progress";
                    stateLabel.text = gm.Config.UseRunRules
                        ? $"Heist  -  first crew carrying ${gm.Config.lootQuota} calls the getaway"
                        : (gm.ExtractionOpen ? "Extraction OPEN" : "Heist in progress");
                    timerLabel.text = Fmt(gm.MatchTimeRemaining);
                    break;
                case GameMode.ExtractionHeistGameMode.GameState.Extraction:
                    // [v0.9] stateLabel.text = "EXTRACT NOW";
                    stateLabel.text = gm.Config.UseRunRules && gm.RichestTeam != Core.TeamID.None
                        ? $"GETAWAY  -  {TeamName(gm.RichestTeam)} crew holds the most"
                        : "EXTRACT NOW";
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
                // [v0.9] row.Loot.text = gm != null && gm.Object != null ? $"${gm.TeamLoot[i]}" : "";
                // [v0.9] row.Status.text = s.Extracted == s.Total && s.Total > 0 ? "out" : $"{s.Alive}/{s.Total} up";
                bool live = gm != null && gm.Object != null;
                if (live && gm.Config.UseRunRules)
                {
                    // (v0.10) Score is what the crew got out with; this column is what it is carrying toward the quota.
                    int carried = gm.GetTeamCarried(team);
                    row.Loot.text = gm.IsGetaway ? $"${carried}" : $"${carried} / {gm.Config.lootQuota}";
                    row.Loot.style.color = gm.IsGetaway && gm.RichestTeam == team ? HealthColor : LootColor;
                    row.Status.text = gm.CrewEliminated[i] ? "OUT"
                        : (s.Extracted == s.Total && s.Total > 0 ? "away" : $"{s.Alive}/{s.Total} up");
                }
                else
                {
                    row.Loot.text = live ? $"${gm.TeamLoot[i]}" : "";
                    row.Status.text = s.Extracted == s.Total && s.Total > 0 ? "out" : $"{s.Alive}/{s.Total} up";
                }
            }
        }

        private void RefreshLocal()
        {
            if (local == null || local.Object == null)
            {
                nameLabel.text = "Spawning...";
                return;
            }

            // [v0.11] nameLabel.text = $"{local.DisplayName}  ·  {local.ClassType}  ·  {TeamName(local.Team)}";
            nameLabel.text = $"{local.DisplayName}  ·  {Core.ClassUtil.DisplayName(local.ClassType)}  ·  {TeamName(local.Team)}";
            if (local.Team != skinnedTeam) ApplyCrewSkin(local.Team);
            SetBar(healthFill, local.Health, local.MaxHealth);
            healthText.text = $"{local.Health:0} / {local.MaxHealth:0}";
            SetBar(staminaFill, local.Stamina, local.MaxStamina);
            staminaText.text = $"{local.Stamina:0}";

            string keys = "";
            foreach (Interaction.KeycardColor c in System.Enum.GetValues(typeof(Interaction.KeycardColor)))
            {
                if (c != Interaction.KeycardColor.None && local.HasKeycard(c)) keys += $"  [{c} keycard]";
            }
            if (local.EnemyCards > 0) keys += $"  [ID cards: {local.EnemyCards}]";
            statusLabel.text = StatusText(local.Status) + keys;
            // [v0.9] lootLabel.text = $"${local.CarriedLoot}";
            lootLabel.text = local.CarriedItemCount > 0 || local.CarriedLoot > 0
                ? $"${local.CarriedLoot}   {local.CarriedItemCount}/{local.ItemCapacity} items"
                : "$0";
            kdLabel.text = $"K {local.Kills}  D {local.Deaths}";
            RefreshAmmo();

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

        private void RefreshAmmo()
        {
            var lo = local.Loadout;
            if (lo == null || lo.Object == null) { weaponLabel.text = ""; ammoLabel.text = ""; gadgetLabel.text = ""; return; }

            // (v0.14) the gadget, its key and its cooldown
            var gadget = lo.GadgetData;
            if (gadget == null) gadgetLabel.text = "";
            else if (gadget.gadget == Data.GadgetType.GrappleLine) gadgetLabel.text = $"[{Core.LocalInputSource.Hint(Core.InputButtons.Grapple)}] {gadget.equipmentName}";
            else
            {
                float wait = lo.CooldownRemaining((int)Equipment.EquipmentSlotType.Gadget);
                gadgetLabel.text = $"[{Core.LocalInputSource.Hint(Core.InputButtons.Gadget)}] {gadget.equipmentName}  " + (wait > 0.05f ? $"{wait:0}s" : "READY");
                gadgetLabel.style.opacity = wait > 0.05f ? 0.55f : 1f;
            }

            int p = (int)Equipment.EquipmentSlotType.Primary, s = (int)Equipment.EquipmentSlotType.Secondary;
            var primary = lo.GetData(p);
            var secondary = lo.GetData(s);

            string Line(Data.EquipmentData d, int slot, string key)
            {
                if (d == null) return "";
                if (!d.UsesAmmo) return $"{key} {d.equipmentName}";
                if (lo.IsReloading(slot)) return $"{key} {d.equipmentName}  reloading {lo.ReloadProgress(slot):P0}";
                return $"{key} {d.equipmentName}  {lo.Magazine[slot]} / {lo.Reserve[slot]}";
            }

            // [v0.12] weaponLabel.text = Line(secondary, s, Core.LocalInputSource.Hint(Core.InputButtons.Secondary));
            // [v0.12] ammoLabel.text = Line(primary, p, Core.LocalInputSource.Hint(Core.InputButtons.Primary));
            // [v0.12] ammoLabel.style.color = primary != null && primary.UsesAmmo && lo.Magazine[p] == 0 ? HealthColor : Color.white;
            // (v0.13) the weapon in hand is the big line; the stowed one is small, with the key that swaps to it
            int hand = lo.ActiveSlot == s ? s : p, stowed = hand == p ? s : p;
            var inHand = hand == p ? primary : secondary;
            var other = hand == p ? secondary : primary;
            weaponLabel.text = other != null ? Line(other, stowed, $"[{Core.LocalInputSource.Hint(Core.InputButtons.NextWeapon)}]") : "";
            ammoLabel.text = lo.IsSwapping && inHand != null ? $"{inHand.equipmentName}  ..." : Line(inHand, hand, "").Trim();
            ammoLabel.style.color = inHand != null && inHand.UsesAmmo && lo.Magazine[hand] == 0 ? HealthColor : Color.white;
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

            // Terminals and cases (v0.5)
            Interaction.HackTerminal bestTerm = null; float bestTermSq = float.MaxValue;
            foreach (var t in Interaction.HackTerminal.All)
            {
                if (t == null || t.Object == null) continue;
                float r = t.InteractRadius * 1.6f;
                float dSq = (t.transform.position - pos).sqrMagnitude;
                if (dSq <= r * r && dSq < bestTermSq) { bestTerm = t; bestTermSq = dSq; }
            }
            GameMode.LootCase bestCase = null; float bestCaseSq = float.MaxValue;
            foreach (var c in GameMode.LootCase.All)
            {
                if (c == null || c.Object == null || c.IsCarried) continue;
                float r = c.PickupRadius * 1.6f;
                float dSq = (c.transform.position - pos).sqrMagnitude;
                if (dSq <= r * r && dSq < bestCaseSq) { bestCase = c; bestCaseSq = dSq; }
            }

            // Vault doors, keycards, code notes (v0.7)
            Interaction.VaultDoor bestDoor = null; float bestDoorSq = float.MaxValue;
            foreach (var d in Interaction.VaultDoor.All)
            {
                if (d == null || d.Object == null || d.IsOpen) continue;
                float r = d.InteractRadius * 1.6f;
                float dSq = (d.transform.position - pos).sqrMagnitude;
                if (dSq <= r * r && dSq < bestDoorSq) { bestDoor = d; bestDoorSq = dSq; }
            }

            if (bestObj == null && bestEp == null && bestTerm == null && bestCase == null && bestDoor == null)
            {
                zonePanel.style.display = DisplayStyle.None;
                return;
            }

            zonePanel.style.display = DisplayStyle.Flex;
            string interact = Core.LocalInputSource.Hint(Core.InputButtons.Interact);

            if (bestDoor != null && bestDoorSq <= Mathf.Min(Mathf.Min(bestObjSq, bestEpSq), Mathf.Min(bestTermSq, bestCaseSq)))
            {
                zoneTitle.text = $"{bestDoor.VaultName} - {bestDoor.LockType} lock";
                switch (bestDoor.LockType)
                {
                    case Interaction.VaultLockType.Keycard:
                        SetBar(zoneFill, local.HasKeycard(bestDoor.RequiredKeycard) ? 1f : 0f, 1f);
                        zoneHint.text = local.HasKeycard(bestDoor.RequiredKeycard) ? $"Hold {interact} to swipe the {bestDoor.RequiredKeycard} keycard" : $"Needs the {bestDoor.RequiredKeycard} keycard - find it in the facility";
                        break;
                    case Interaction.VaultLockType.PlayerCard:
                        SetBar(zoneFill, Mathf.Clamp01((float)local.EnemyCards / bestDoor.EnemyCardsRequired), 1f);
                        zoneHint.text = local.EnemyCards >= bestDoor.EnemyCardsRequired ? $"Hold {interact} to swipe an enemy ID card" : $"Needs {bestDoor.EnemyCardsRequired} enemy ID card(s) - loot them from dead rivals (you have {local.EnemyCards})";
                        break;
                    case Interaction.VaultLockType.Passcode:
                        SetBar(zoneFill, bestDoor.TeamKnowsCode(local.Team) ? 1f : 0f, 1f);
                        zoneHint.text = bestDoor.IsLockedOut ? "Keypad locked out after a wrong code" : bestDoor.TeamKnowsCode(local.Team) ? $"Press {interact} - code {bestDoor.Passcode:0000}" : $"Press {interact} to use the keypad. Wrong code = LOCKDOWN. Find the code note.";
                        break;
                    case Interaction.VaultLockType.Lockdown:
                        float? left = bestDoor.LockdownRemaining;
                        SetBar(zoneFill, left.HasValue ? 1f - Mathf.Clamp01(left.Value / 45f) : 0f, 1f);
                        zoneHint.text = left.HasValue ? $"LOCKDOWN - survive {Mathf.CeilToInt(left.Value)}s and the vault opens" : $"Hold {interact} to force the override - this triggers a LOCKDOWN";
                        break;
                }
                return;
            }

            if (bestCase != null && bestCaseSq <= Mathf.Min(bestObjSq, Mathf.Min(bestEpSq, bestTermSq)))
            {
                zoneTitle.text = $"{bestCase.CaseName}  (${bestCase.Value})";
                SetBar(zoneFill, 0f, 1f);
                zoneHint.text = local.HasStatus(Core.StatusFlags.Encumbered) ? "You already carry a case" : $"Hold {interact} to pick up - carry it to extraction";
                return;
            }
            if (bestTerm != null && bestTermSq <= Mathf.Min(bestObjSq, bestEpSq))
            {
                zoneTitle.text = bestTerm.TerminalName;
                SetBar(zoneFill, bestTerm.ProgressNormalized, 1f);
                zoneHint.text = !bestTerm.IsReady ? "Rebooting..." : bestTerm.WorkingTeam == Core.TeamID.None
                    ? $"Hold {interact} to override (Hackers: System Hack)" : $"{TeamName(bestTerm.WorkingTeam)} overriding...";
                return;
            }

            if (bestObj != null && (bestEp == null || bestObjSq <= bestEpSq))
            {
                zoneTitle.text = bestObj.ObjectiveName + $"  (${bestObj.LootValue})" + (bestObj.Stages > 1 ? $"  stage {Mathf.Min(bestObj.StagesDone + 1, bestObj.Stages)}/{bestObj.Stages}" : "");
                SetBar(zoneFill, bestObj.ProgressNormalized, 1f);
                if (bestObj.IsGated) zoneHint.text = "Open the vault door first";
                else if (!bestObj.IsAvailable) zoneHint.text = "Depleted";
                else if (!bestObj.IsUnlocked) zoneHint.text = "Locked - needs a Hacker";
                // [v0.10] else if (bestObj.CapturingTeam == Core.TeamID.None) zoneHint.text = $"Hold {Core.LocalInputSource.Hint(Core.InputButtons.Interact)} to crack";
                // [v0.10] else if (bestObj.IsContested) zoneHint.text = $"{TeamName(bestObj.CapturingTeam)} cracking - CONTESTED";
                // [v0.10] else zoneHint.text = bestObj.CapturingTeam == local.Team ? $"Cracking... keep holding {Core.LocalInputSource.Hint(Core.InputButtons.Interact)}" : $"{TeamName(bestObj.CapturingTeam)} is cracking it!";
                // (v0.11) vaults are deep hacks: press once to jack in, answer signs, press again to jack out
                else if (local.IsJackedIn) zoneHint.text = "Jacked in - answer the signs";
                else if (bestObj.CapturingTeam == Core.TeamID.None) zoneHint.text = $"Press {Core.LocalInputSource.Hint(Core.InputButtons.Interact)} to jack in";
                else if (bestObj.CapturingTeam == local.Team) zoneHint.text = $"Your crew is cracking it - press {Core.LocalInputSource.Hint(Core.InputButtons.Interact)} to jack in and help";
                else zoneHint.text = $"{TeamName(bestObj.CapturingTeam)} crew holds it - press {Core.LocalInputSource.Hint(Core.InputButtons.Interact)} to counter-hack";
            }
            else
            {
                zoneTitle.text = "Extraction point";
                SetBar(zoneFill, bestEp.ProgressNormalized, 1f);
                if (gm != null && !gm.ExtractionOpen) zoneHint.text = "Not open yet";
                else if (!bestEp.IsActive) zoneHint.text = "Closed - reopening soon";
                // [v0.9] else if (bestEp.CurrentExtractingTeam == Core.TeamID.None) zoneHint.text = gm != null && gm.CanExtract(local) ? "Stand here to extract" : "Not eligible to extract";
                else if (bestEp.CurrentExtractingTeam == Core.TeamID.None)
                {
                    if (gm == null || !gm.CanExtract(local)) zoneHint.text = gm != null && !gm.ExtractionOpen ? "Locked until the getaway is called" : "Not eligible to extract";
                    else if (!gm.Config.UseRunRules) zoneHint.text = "Stand here to extract";
                    else zoneHint.text = bestEp.IsCentral
                        ? "CENTRAL GETAWAY - stand here to bank everything"
                        : $"Side exit - banks {Mathf.RoundToInt(bestEp.PayoutMultiplier * 100f)}% of what you carry";
                }
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
                // (v0.13) what the match added to the local profile
                var record = Core.PlayerProfile.LastMatch;
                if (record.Valid)
                {
                    sb.AppendLine();
                    sb.AppendLine($"+{record.Xp} XP    Level {record.LevelAfter}  {Core.ProfileRules.TitleForLevel(record.LevelAfter)}" + (record.LevelledUp ? "    LEVEL UP" : ""));
                    sb.AppendLine("[Esc] menu");
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
                case GameMode.ExtractionHeistGameMode.GameState.InProgress: ShowBanner("GO - crack the vaults, carry the loot"); break;
                // [v0.9] case GameMode.ExtractionHeistGameMode.GameState.Extraction: ShowBanner("EXTRACTION PHASE - get out!"); break;
                case GameMode.ExtractionHeistGameMode.GameState.Extraction:
                    var gmNow = GameMode.ExtractionHeistGameMode.Instance;
                    if (gmNow == null || !gmNow.Config.UseRunRules) ShowBanner("EXTRACTION PHASE - get out!"); // run rules: OnGetawayCalled speaks
                    break;
            }
        }

        private void OnExtractionOpened() => ShowBanner("Extraction points are open");

        // (v0.14) Map events: told to everyone, before and when they happen.
        private static string EventLine(GameMode.MapEvent e) => string.IsNullOrEmpty(e.place) ? e.title : $"{e.title}: {e.place}";

        private void OnMapEventWarned(GameMode.MapEvent e, float seconds)
        {
            AddFeed($"{EventLine(e)} in {seconds:0}s", LootColor);
        }

        private void OnMapEventFired(GameMode.MapEvent e)
        {
            ShowBanner(EventLine(e).ToUpperInvariant());
            AddFeed(EventLine(e), LootColor);
        }

        private void OnLaserTripped(Interaction.LaserGrid grid, Network.NetworkPlayer who)
        {
            if (grid == null) return;
            string zone = GameMode.MapZone.NameAt(grid.transform.position);
            AddFeed(who != null ? $"{who.DisplayName} tripped the lasers" + (zone.Length > 0 ? $" in {zone}" : "") : "Lasers tripped", HealthColor);
            if (who != null && who.IsLocalPlayer) ShowBanner("LASER GRID - every guard knows where you are");
        }

        // ---- (v0.11) Hacking ----

        private void OnVaultTakenOver(GameMode.LootObjective objective, Core.TeamID team)
        {
            AddFeed($"{TeamName(team)} counter-hacked {objective.ObjectiveName}", Player.TeamSpawnPoint.TeamColor(team));
            if (local != null && local.JackedObjective == objective && local.Team != team) ShowBanner("COUNTER-HACKED - take it back");
        }

        private void OnPowerCut(Interaction.PowerNode node, Core.TeamID team)
        {
            AddFeed($"{TeamName(team)} cut the power: {node.NodeName}", Player.TeamSpawnPoint.TeamColor(team));
        }

        private void OnQuickHack(Network.NetworkPlayer by, string what)
        {
            // Rivals' turret hijacks are worth knowing about; the rest would only be noise.
            if (by == null || string.IsNullOrEmpty(what) || !what.Contains("turret")) return;
            AddFeed($"{by.DisplayName}: {what.ToLowerInvariant()}", Player.TeamSpawnPoint.TeamColor(by.Team));
        }

        private void OnGravitySwitched(Interaction.GravityZone zone, Core.TeamID team, bool gravityOff)
        {
            AddFeed(gravityOff ? $"{TeamName(team)} killed the gravity: {zone.ZoneName}" : $"Gravity restored: {zone.ZoneName}", Player.TeamSpawnPoint.TeamColor(team));
            if (gravityOff && local != null && local.Object != null && zone.Bounds.Contains(local.Position + Vector3.up)) ShowBanner("ZERO-G  -  fly where you look, Jump up, Slide down");
        }

        private void OnJammed(Network.NetworkPlayer victim, Network.NetworkPlayer hacker)
        {
            if (victim == null) return;
            string by = hacker != null ? hacker.DisplayName.ToString() : "someone";
            AddFeed($"{by} jammed {victim.DisplayName}", hacker != null ? Player.TeamSpawnPoint.TeamColor(hacker.Team) : Color.gray);
        }

        // ---- (v0.10) The Run ----

        private void OnGetawayCalled(Core.TeamID byTeam)
        {
            if (byTeam == Core.TeamID.None) { ShowBanner("TIME IS UP - the getaway is open"); return; }
            bool ours = local != null && local.Team == byTeam;
            ShowBanner(ours ? "QUOTA HIT - get to the central getaway!" : $"{TeamName(byTeam)} crew hit the quota - stop them at the getaway");
            AddFeed($"{TeamName(byTeam)} crew called the getaway", Player.TeamSpawnPoint.TeamColor(byTeam));
        }

        private void OnCrewWiped(Core.TeamID team, bool eliminated)
        {
            AddFeed(eliminated ? $"{TeamName(team)} crew is OUT" : $"{TeamName(team)} crew wiped - redeploying", Player.TeamSpawnPoint.TeamColor(team));
            if (local != null && local.Team == team) ShowBanner(eliminated ? "CREW ELIMINATED" : "CREW WIPED - redeploying together");
        }

        private void OnLootPickedUp(Network.NetworkPlayer who, int itemId, int value)
        {
            if (who == null || itemId <= 0) return;
            var item = Data.LootCatalog.Active.Get(itemId);
            if (item == null) return;
            // Your own pickups show the story card; other crews' rare finds show up in the feed.
            if (who.IsLocalPlayer)
            {
                AddFeed($"+ {item.name}  (${value})", Data.LootCatalog.TierColor(item.tier));
                if (!string.IsNullOrEmpty(item.story)) AddFeed(item.story, new Color(0.85f, 0.85f, 0.85f));
            }
            else if (item.tier != Data.LootTier.Common)
            {
                AddFeed($"{who.DisplayName} took {item.name}", Player.TeamSpawnPoint.TeamColor(who.Team));
            }
        }

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

        private void OnLocalDamaged(Network.NetworkPlayer who, float amount)
        {
            damageFlashAlpha = Mathf.Clamp01(Mathf.Max(damageFlashAlpha, 0.35f + amount / 100f));
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
            return Core.TeamUtil.DisplayName(team); // (v0.10) city crews; the switch below is the [v0.9] naming
#pragma warning disable CS0162 // unreachable: kept for reference
            switch (team)
            {
                case Core.TeamID.TeamAlpha:   return "Alpha";
                case Core.TeamID.TeamBravo:   return "Bravo";
                case Core.TeamID.TeamCharlie: return "Charlie";
                case Core.TeamID.TeamDelta:   return "Delta";
                default: return "-";
    #pragma warning restore CS0162
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
            if ((status & Core.StatusFlags.Encumbered) != 0)  parts.Add("CARRYING CASE");
            if ((status & Core.StatusFlags.Sliding) != 0)     parts.Add("slide");
            if ((status & Core.StatusFlags.Gliding) != 0)     parts.Add("GLIDING");
            if ((status & Core.StatusFlags.Climbing) != 0)    parts.Add("CLIMBING");
            if ((status & Core.StatusFlags.Grappling) != 0)   parts.Add("GRAPPLE");
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
