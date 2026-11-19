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

        // [v0.18] private class TeamRow { public VisualElement Root; public Label Name, Score, Loot, Status; }
        // (v0.19) the board is the game's scoreboard: one big cash total per crew, a bar toward the quota, and a line of detail
        private class TeamRow
        {
            public VisualElement Root, BankFill, CarryFill;
            public Label Name, Score, Loot, Status;   // Score is the big total; Loot is the detail line
            public int ShownTotal = -1;
        }
        private int shownCarried = -1;
        private Label boardRule;
        private VisualElement boardPanel;
        private readonly int[] boardOrder = new int[Core.GameConstants.MAX_TEAMS];
        private readonly int[] boardTotals = new int[Core.GameConstants.MAX_TEAMS];

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
            if (GetComponent<CashOutHUD>() == null) gameObject.AddComponent<CashOutHUD>(); // (v0.16) cash-out ticker, markers and prompt
            if (GetComponent<AssistHUD>() == null) gameObject.AddComponent<AssistHUD>(); // (v0.17) crew pings and revives
            if (GetComponent<ExitHUD>() == null) gameObject.AddComponent<ExitHUD>(); // (v0.18) nearest exit once the getaway is called
            if (GetComponent<ObjectiveHUD>() == null) gameObject.AddComponent<ObjectiveHUD>(); // (v0.19) what to do next, and the briefing
            if (GetComponent<CompassHUD>() == null) gameObject.AddComponent<CompassHUD>(); // (v0.20) bearings to everything worth walking to
            if (GetComponent<VaultHUD>() == null) gameObject.AddComponent<VaultHUD>(); // (v0.21) Grand Vault doors, keys and perks

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
            GameMode.CashOutStation.Moment += OnCashOutMoment; // (v0.16)
            Network.NetworkPlayer.Revived += OnRevived; // (v0.17)
            Interaction.GrandVault.Moment += OnVaultMoment; // (v0.21)
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
            GameMode.CashOutStation.Moment -= OnCashOutMoment;
            Network.NetworkPlayer.Revived -= OnRevived;
            Interaction.GrandVault.Moment -= OnVaultMoment;
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

            // [v0.18] if (damageFlashAlpha > 0f)
            // [v0.18] {
            // [v0.18]     damageFlashAlpha = Mathf.Max(0f, damageFlashAlpha - Time.unscaledDeltaTime * 1.5f);
            // [v0.18]     damageFlash.style.opacity = damageFlashAlpha;
            // [v0.18]     damageFlash.style.display = damageFlashAlpha > 0f ? DisplayStyle.Flex : DisplayStyle.None;
            // [v0.18] }
            // (v0.19) Low health is felt, not read: under a third of full the screen keeps a red pulse that
            //         deepens as health drops and quickens near death. It fades as regeneration brings health back.
            damageFlashAlpha = Mathf.Max(0f, damageFlashAlpha - Time.unscaledDeltaTime * 1.5f);
            float hurt = 0f;
            if (local != null && local.Object != null && local.IsActiveInMatch && local.MaxHealth > 0f)
            {
                float danger = Mathf.Clamp01(1f - (local.Health / local.MaxHealth) / 0.33f);
                if (danger > 0f) hurt = danger * (0.3f + 0.22f * Mathf.Sin(Time.unscaledTime * Mathf.Lerp(4f, 9f, danger)));
            }
            float redness = Mathf.Max(damageFlashAlpha, hurt);
            bool red = redness > 0.001f;
            if (red) damageFlash.style.opacity = redness;
            if ((damageFlash.resolvedStyle.display != DisplayStyle.None) != red) damageFlash.style.display = red ? DisplayStyle.Flex : DisplayStyle.None;
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
            // [v0.19] top.style.top = 12; top.style.left = Length.Percent(50);
            top.style.top = 62; top.style.left = Length.Percent(50); // (v0.20) the compass strip and its labels sit above
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
            // [v0.19] bannerLabel.style.top = 90; bannerLabel.style.left = Length.Percent(50);
            bannerLabel.style.top = 140; bannerLabel.style.left = Length.Percent(50); // (v0.20) moved down with the top panel
            bannerLabel.style.translate = new Translate(Length.Percent(-50), 0);
            bannerLabel.style.display = DisplayStyle.None;
            bannerLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            root.Add(bannerLabel);

            // Top right: the cash board (v0.19). This is the game's scoreboard, so it is the biggest thing on the
            // HUD after the crosshair: each crew's money in large figures, a bar showing how close it is to the
            // quota (solid = banked, pale = still being carried and still losable), and who is up.
            // [v0.18] The board was a 300 px table of 12-14 px text headed "TEAMS":
            // [v0.18] var board = Panel(PanelBg);
            // [v0.18] board.style.position = Position.Absolute;
            // [v0.18] board.style.top = 12; board.style.right = 12; board.style.minWidth = 300;
            // [v0.18] board.Add(Text("TEAMS", 12, new Color(1f, 1f, 1f, 0.6f), bold: true));
            // [v0.18] for (int i = 0; i < teamRows.Length; i++)
            // [v0.18] {
            // [v0.18] var row = new TeamRow { Root = new VisualElement() };
            // [v0.18] row.Root.style.flexDirection = FlexDirection.Row;
            // [v0.18] row.Root.style.justifyContent = Justify.SpaceBetween;
            // [v0.18] row.Root.style.marginTop = 2;
            // [v0.18] row.Name = Text("", 14, Player.TeamSpawnPoint.TeamColor(Core.TeamUtil.FromIndex(i)), bold: true);
            // [v0.18] row.Name.style.minWidth = 100;
            // [v0.18] row.Score = Text("", 14, Color.white, bold: true); row.Score.style.minWidth = 60; row.Score.style.unityTextAlign = TextAnchor.MiddleRight;
            // [v0.18] row.Loot = Text("", 13, LootColor); row.Loot.style.minWidth = 60; row.Loot.style.unityTextAlign = TextAnchor.MiddleRight;
            // [v0.18] row.Status = Text("", 12, new Color(1f, 1f, 1f, 0.7f)); row.Status.style.minWidth = 70; row.Status.style.unityTextAlign = TextAnchor.MiddleRight;
            // [v0.18] row.Root.Add(row.Name); row.Root.Add(row.Score); row.Root.Add(row.Loot); row.Root.Add(row.Status);
            // [v0.18] board.Add(row.Root);
            // [v0.18] teamRows[i] = row;
            // [v0.18] }
            // [v0.18] root.Add(board);
            var board = Panel(PanelBg);
            board.style.position = Position.Absolute;
            board.style.top = 12; board.style.right = 12; board.style.width = 380;
            boardPanel = board;
            var head = new VisualElement();
            head.style.flexDirection = FlexDirection.Row; head.style.justifyContent = Justify.SpaceBetween; head.style.alignItems = Align.FlexEnd;
            head.Add(Text("CASH", 18, LootColor, bold: true));
            boardRule = Text("", 12, new Color(1f, 1f, 1f, 0.7f), bold: true);
            head.Add(boardRule);
            board.Add(head);
            for (int i = 0; i < teamRows.Length; i++)
            {
                Color crew = Player.TeamSpawnPoint.TeamColor(Core.TeamUtil.FromIndex(i));
                var row = new TeamRow { Root = new VisualElement() };
                row.Root.style.marginTop = 6; row.Root.style.paddingLeft = 8; row.Root.style.paddingRight = 6; row.Root.style.paddingTop = 2; row.Root.style.paddingBottom = 4;
                row.Root.style.borderLeftWidth = 4; row.Root.style.borderLeftColor = crew;

                var line = new VisualElement();
                line.style.flexDirection = FlexDirection.Row; line.style.justifyContent = Justify.SpaceBetween; line.style.alignItems = Align.FlexEnd;
                row.Name = Text("", 17, crew, bold: true);
                row.Score = Text("", 30, LootColor, bold: true);
                row.Score.style.unityTextAlign = TextAnchor.LowerRight;
                line.Add(row.Name); line.Add(row.Score);
                row.Root.Add(line);

                var track = new VisualElement();
                track.style.height = 7; track.style.marginTop = 1; track.style.flexDirection = FlexDirection.Row;
                track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
                row.BankFill = new VisualElement(); row.BankFill.style.backgroundColor = crew;
                row.CarryFill = new VisualElement(); row.CarryFill.style.backgroundColor = new Color(crew.r, crew.g, crew.b, 0.45f);
                track.Add(row.BankFill); track.Add(row.CarryFill);
                row.Root.Add(track);

                var detail = new VisualElement();
                detail.style.flexDirection = FlexDirection.Row; detail.style.justifyContent = Justify.SpaceBetween; detail.style.marginTop = 2;
                row.Loot = Text("", 12, new Color(1f, 1f, 1f, 0.8f));
                row.Status = Text("", 12, new Color(1f, 1f, 1f, 0.8f), bold: true);
                detail.Add(row.Loot); detail.Add(row.Status);
                row.Root.Add(detail);

                board.Add(row.Root);
                teamRows[i] = row;
                boardOrder[i] = i;
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
                    // [v0.18] stateLabel.text = gm.Config.UseRunRules
                    // [v0.18]     ? $"Heist  -  first crew to bank or carry ${gm.LootQuota} calls the getaway" // [v0.13] gm.Config.lootQuota
                    // [v0.18]     : (gm.ExtractionOpen ? "Extraction OPEN" : "Heist in progress");
                    // (v0.19) the rule now lives on the cash board and in the objective tracker; this is just the phase
                    stateLabel.text = gm.ExtractionOpen ? "LIFTS OPEN" : "HEIST IN PROGRESS";
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

        // [v0.18] private void RefreshTeams()
        // [v0.18] {
        // [v0.18]     var gm = GameMode.ExtractionHeistGameMode.Instance;
        // [v0.18]     var tm = Network.TeamManager.Instance;
        // [v0.18]     for (int i = 0; i < teamRows.Length; i++)
        // [v0.18]     {
        // [v0.18]         var row = teamRows[i];
        // [v0.18]         var team = Core.TeamUtil.FromIndex(i);
        // [v0.18]         bool present = tm != null && tm.Object != null && tm.GetTeamPlayerCount(team) > 0;
        // [v0.18]         row.Root.style.display = present ? DisplayStyle.Flex : DisplayStyle.None;
        // [v0.18]         if (!present) continue;

        // [v0.18]         var s = tm.GetTeamStatus(team);
        // [v0.18]         row.Name.text = TeamName(team) + (local != null && local.Team == team ? " ★" : "");
        // [v0.18]         row.Score.text = gm != null && gm.Object != null ? gm.TeamScores[i].ToString() : "0";
        // [v0.18]         // [v0.9] row.Loot.text = gm != null && gm.Object != null ? $"${gm.TeamLoot[i]}" : "";
        // [v0.18]         // [v0.9] row.Status.text = s.Extracted == s.Total && s.Total > 0 ? "out" : $"{s.Alive}/{s.Total} up";
        // [v0.18]         bool live = gm != null && gm.Object != null;
        // [v0.18]         if (live && gm.Config.UseRunRules)
        // [v0.18]         {
        // [v0.18]             // (v0.10) Score is what the crew got out with; this column is what it is carrying toward the quota.
        // [v0.18]             int carried = gm.GetTeamCarried(team);
        // [v0.18]             // [v0.15] row.Loot.text = gm.IsGetaway ? $"${carried}" : $"${carried} / {gm.LootQuota}"; // [v0.13] gm.Config.lootQuota
        // [v0.18]             // (v0.16) banked money is shown on its own and counts toward the quota with what is carried
        // [v0.18]             int banked = gm.GetTeamBanked(team);
        // [v0.18]             row.Loot.text = gm.IsGetaway ? $"${banked} + ${carried}" : $"${banked} + ${carried} / {gm.LootQuota}";
        // [v0.18]             row.Loot.style.color = gm.IsGetaway && gm.RichestTeam == team ? HealthColor : LootColor;
        // [v0.18]             row.Status.text = gm.CrewEliminated[i] ? "OUT"
        // [v0.18]                 : (s.Extracted == s.Total && s.Total > 0 ? "away" : $"{s.Alive}/{s.Total} up");
        // [v0.18]         }
        // [v0.18]         else
        // [v0.18]         {
        // [v0.18]             row.Loot.text = live ? $"${gm.TeamLoot[i]}" : "";
        // [v0.18]             row.Status.text = s.Extracted == s.Total && s.Total > 0 ? "out" : $"{s.Alive}/{s.Total} up";
        // [v0.18]         }
        // [v0.18]     }
        // [v0.18] }

        /// <summary>
        /// (v0.19) The cash board. Under the run rules a crew's figure is everything it has toward the quota:
        /// banked plus carried. Rows are ordered richest first, the local crew's row is lit, and a figure pops
        /// when it goes up so money arriving is something you see happen.
        /// </summary>
        private void RefreshTeams()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            var tm = Network.TeamManager.Instance;
            bool live = gm != null && gm.Object != null;
            bool run = live && gm.Config.UseRunRules;
            int quota = run ? Mathf.Max(1, gm.LootQuota) : 1;

            boardRule.text = !run ? "" : gm.IsGetaway ? "getaway called: get out with it" : $"first to ${quota:N0} calls the getaway";

            int richest = 1;
            for (int i = 0; i < teamRows.Length; i++)
            {
                var team = Core.TeamUtil.FromIndex(i);
                boardTotals[i] = !live ? 0 : run ? gm.GetTeamHaul(team) : gm.TeamScores[i];
                if (boardTotals[i] > richest) richest = boardTotals[i];
            }
            // Before the getaway the bar runs to the quota; after it, to the richest crew.
            float full = run && !gm.IsGetaway ? quota : richest;

            for (int i = 0; i < teamRows.Length; i++)
            {
                var row = teamRows[i];
                var team = Core.TeamUtil.FromIndex(i);
                bool present = tm != null && tm.Object != null && tm.GetTeamPlayerCount(team) > 0;
                row.Root.style.display = present ? DisplayStyle.Flex : DisplayStyle.None;
                if (!present) continue;

                bool mine = local != null && local.Object != null && local.Team == team;
                var s = tm.GetTeamStatus(team);
                int total = boardTotals[i];
                row.Name.text = mine ? $"{TeamName(team)}  (you)" : TeamName(team);
                row.Root.style.backgroundColor = mine ? new Color(1f, 1f, 1f, 0.1f) : new Color(0f, 0f, 0f, 0f);
                row.Score.text = run ? $"${total:N0}" : total.ToString("N0");

                bool cashing = false, robbed = false;
                var stations = GameMode.CashOutStation.All;
                for (int k = 0; k < stations.Count; k++)
                {
                    var st = stations[k];
                    if (st == null || st.Object == null || !st.IsActive || st.OwnerTeam != team) continue;
                    cashing = true;
                    if (st.IsBeingHijacked) robbed = true;
                }

                if (run)
                {
                    int banked = gm.GetTeamBanked(team), carried = gm.GetTeamCarried(team);
                    float bankedShare = Mathf.Clamp01(banked / full);
                    row.BankFill.style.width = Length.Percent(bankedShare * 100f);
                    row.CarryFill.style.width = Length.Percent(Mathf.Min(Mathf.Clamp01(carried / full), 1f - bankedShare) * 100f);
                    row.Loot.text = $"banked ${banked:N0}   carrying ${carried:N0}";
                    row.Score.style.color = gm.IsGetaway && gm.RichestTeam == team ? HealthColor : LootColor;
                    row.Status.text = gm.CrewEliminated[i] ? "OUT"
                        : s.Extracted == s.Total && s.Total > 0 ? "AWAY"
                        : robbed ? "BEING ROBBED"
                        : cashing ? "CASHING OUT"
                        : $"{s.Alive}/{s.Total} up";
                    row.Status.style.color = robbed ? HealthColor : cashing ? LootColor : new Color(1f, 1f, 1f, 0.8f);
                }
                else
                {
                    row.BankFill.style.width = Length.Percent(Mathf.Clamp01(total / full) * 100f);
                    row.CarryFill.style.width = Length.Percent(0f);
                    row.Loot.text = live ? $"banked ${gm.TeamLoot[i]:N0}" : "";
                    row.Status.text = s.Extracted == s.Total && s.Total > 0 ? "out" : $"{s.Alive}/{s.Total} up";
                }

                // Money arriving is an event: the figure jumps. Only upward, and not on the first frame.
                if (row.ShownTotal >= 0 && total > row.ShownTotal) Core.Juice.Pop(row.Score, mine ? 1.35f : 1.18f, 0.3f);
                row.ShownTotal = total;
            }

            // Richest first. Rows are only re-stacked when the order changes.
            bool changed = false;
            for (int pass = 0; pass < boardOrder.Length - 1; pass++)
            {
                for (int k = 0; k < boardOrder.Length - 1 - pass; k++)
                {
                    if (boardTotals[boardOrder[k + 1]] <= boardTotals[boardOrder[k]]) continue;
                    int swap = boardOrder[k]; boardOrder[k] = boardOrder[k + 1]; boardOrder[k + 1] = swap;
                    changed = true;
                }
            }
            if (changed) for (int k = 0; k < boardOrder.Length; k++) teamRows[boardOrder[k]].Root.BringToFront();
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
            // [v0.16] healthText.text = $"{local.Health:0} / {local.MaxHealth:0}";
            healthText.text = local.IsRegenerating ? $"{local.Health:0} / {local.MaxHealth:0}  +" : $"{local.Health:0} / {local.MaxHealth:0}"; // (v0.17) "+" while it comes back
            SetBar(staminaFill, local.Stamina, local.MaxStamina);
            staminaText.text = $"{local.Stamina:0}";

            string keys = "";
            foreach (Interaction.KeycardColor c in System.Enum.GetValues(typeof(Interaction.KeycardColor)))
            {
                if (c != Interaction.KeycardColor.None && local.HasKeycard(c)) keys += $"  [{c} keycard]";
            }
            if (local.EnemyCards > 0) keys += $"  [ID cards: {local.EnemyCards}]";
            // (v0.21) the vault key in the pocket, and the perks the crew holds
            var vaultKey = Interaction.GrandVault.KeyCarriedBy(local);
            if (vaultKey != null) keys += $"  [{vaultKey.VaultName} key]";
            int perkMask = GameMode.ExtractionHeistGameMode.PerksOf(local.Team);
            if (perkMask != 0)
            {
                foreach (Core.VaultPerk perk in System.Enum.GetValues(typeof(Core.VaultPerk)))
                    if (Core.VaultRules.Has(perkMask, perk)) keys += $"  [{Core.VaultRules.PerkName(perk)}]";
            }
            statusLabel.text = StatusText(local.Status) + keys;
            // [v0.9] lootLabel.text = $"${local.CarriedLoot}";
            // [v0.18] lootLabel.text = local.CarriedItemCount > 0 || local.CarriedLoot > 0
            // [v0.18]     ? $"${local.CarriedLoot}   {local.CarriedItemCount}/{local.ItemCapacity} items"
            // [v0.18]     : "$0";
            // (v0.19) says what the number is, and jumps when it grows
            lootLabel.text = local.CarriedItemCount > 0 || local.CarriedLoot > 0
                ? $"CARRYING ${local.CarriedLoot:N0}   {local.CarriedItemCount}/{local.ItemCapacity} items"
                : "CARRYING $0";
            if (shownCarried >= 0 && local.CarriedLoot > shownCarried) Core.Juice.Pop(lootLabel, 1.3f, 0.28f);
            shownCarried = local.CarriedLoot;
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
                // [v0.20] if (d == null || d.Object == null || d.IsOpen) continue;
                if (d == null || d.Object == null || d.IsOpen || d.LockType == Interaction.VaultLockType.Grand) continue; // (v0.21) Grand Vault doors have their own prompt (VaultHUD)
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
                // [v0.20] zoneTitle.text = bestObj.ObjectiveName + $"  (${bestObj.LootValue})" + (bestObj.Stages > 1 ? ... : "");
                // (v0.21) a Grand Vault's lock console holds no money of its own: it opens the door beside it
                zoneTitle.text = bestObj.ObjectiveName + (bestObj.IsVaultLock ? "  -  cracking it opens the vault" : $"  (${bestObj.LootValue})")
                    + (bestObj.Stages > 1 ? $"  stage {Mathf.Min(bestObj.StagesDone + 1, bestObj.Stages)}/{bestObj.Stages}" : "");
                SetBar(zoneFill, bestObj.ProgressNormalized, 1f);
                if (bestObj.IsGated) zoneHint.text = "Open the vault door first";
                else if (!bestObj.IsAvailable) zoneHint.text = bestObj.IsVaultLock ? "The vault is open" : "Depleted";
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

            // (v0.20) While the camera is with a crewmate the full-screen overlay goes away, so they can be
            //         watched; the objective tracker carries the state and the keys. The overlay still shows
            //         for the first moment after dying, and whenever there is nobody to watch.
            if (Player.PlayerController.Spectating != null && (local.IsExtracted || !local.IsAlive))
            {
                deathOverlay.style.display = DisplayStyle.None;
            }
            // [v0.19] if (local.IsExtracted)
            else if (local.IsExtracted)
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

        /// <summary>
        /// (v0.21) Grand Vaults are the match, so what happens at one is news for every crew: a drill starting
        /// or being stopped, a key hitting the floor or being taken, a door opening, a perk going.
        /// </summary>
        private void OnVaultMoment(Interaction.GrandVault vault, Interaction.VaultMoment moment, Core.TeamID team, Core.TeamID other, Network.NetworkPlayer who)
        {
            if (vault == null) return;
            string name = vault.VaultName;
            string upper = name.ToUpperInvariant();
            bool mine = local != null && local.Object != null && local.Team == team && Core.TeamUtil.IsValid(team);
            bool wasMine = local != null && local.Object != null && local.Team == other && Core.TeamUtil.IsValid(other);
            Color colour = Core.TeamUtil.IsValid(team) ? CrewSkin.TextColour(team) : Color.white;
            switch (moment)
            {
                case Interaction.VaultMoment.KeyDropped:
                    AddFeed($"The {name} key is on the floor", LootColor);
                    break;
                case Interaction.VaultMoment.KeyTaken:
                    AddFeed($"{(who != null ? who.DisplayName.ToString() : TeamName(team))} has the {name} key", colour);
                    if (who != null && who == local) ShowBanner($"{upper} KEY - TAKE IT TO THE DOOR");
                    break;
                case Interaction.VaultMoment.DrillStarted:
                    AddFeed($"{TeamName(team)} is drilling the {name}", colour);
                    ShowBanner(mine ? $"DRILL SET - HOLD THE {upper} DOORWAY" : $"{TeamName(team).ToUpperInvariant()} IS DRILLING THE {upper}");
                    break;
                case Interaction.VaultMoment.DrillStopped:
                    if (Core.TeamUtil.IsValid(team)) AddFeed($"{TeamName(team)} pulled the plug on {TeamName(other)}'s drill at the {name}", colour);
                    else AddFeed($"{TeamName(other)} abandoned the drill at the {name}", Color.white);
                    if (wasMine) ShowBanner(Core.TeamUtil.IsValid(team) ? "YOUR DRILL WAS STOPPED" : "YOUR DRILL RAN ALL THE WAY BACK");
                    else if (mine) ShowBanner("DRILL STOPPED");
                    break;
                case Interaction.VaultMoment.Opened:
                {
                    string how = vault.OpenedHow == Core.VaultOpening.Key ? "with its key"
                               : vault.OpenedHow == Core.VaultOpening.Drill ? "with a drill"
                               : vault.OpenedHow == Core.VaultOpening.Crack ? "by cracking the lock"
                               : vault.OpenedHow == Core.VaultOpening.TimeLock ? "on its time lock" : "through the wall";
                    AddFeed(Core.TeamUtil.IsValid(team) ? $"{TeamName(team)} opened the {name} {how}" : $"The {name} is open ({how})", LootColor);
                    ShowBanner(mine ? $"{upper} OPEN - PERK FIRST, THEN THE LOOT" : $"THE {upper} IS OPEN");
                    break;
                }
                case Interaction.VaultMoment.PerkClaimed:
                    AddFeed($"{TeamName(team)} took {Core.VaultRules.PerkName(vault.Perk)} from the {name}", colour);
                    if (mine) ShowBanner($"{Core.VaultRules.PerkName(vault.Perk).ToUpperInvariant()}: {Core.VaultRules.PerkEffect(vault.Perk)}");
                    break;
            }
        }

        /// <summary>(v0.17) A revive is news for the crew it happened to, and a banner for the two players involved.</summary>
        private void OnRevived(Network.NetworkPlayer revived, Network.NetworkPlayer by)
        {
            if (revived == null || local == null || local.Object == null || revived.Team != local.Team) return;
            string who = by != null ? by.DisplayName.ToString() : "A crewmate";
            AddFeed($"{who} revived {revived.DisplayName}", CrewSkin.TextColour(revived.Team));
            if (revived == local) ShowBanner($"REVIVED BY {who.ToUpperInvariant()}");
            else if (by == local) ShowBanner($"{revived.DisplayName.ToString().ToUpperInvariant()} IS BACK UP");
        }

        /// <summary>
        /// (v0.16) The house announcer for cash-outs. Everyone hears every one: who, how much, and where.
        /// The local crew gets the banner worded for them.
        /// </summary>
        private void OnCashOutMoment(GameMode.CashOutStation station, GameMode.CashOutMoment moment, Core.TeamID team, Core.TeamID from, int value)
        {
            if (station == null) return;
            string where = GameMode.MapZone.NameAt(station.transform.position + Vector3.up);
            string place = where.Length > 0 ? $" in the {where}" : "";
            string crew = TeamName(team);
            bool mine = local != null && local.Object != null && local.Team == team;
            bool wasMine = local != null && local.Object != null && local.Team == from && Core.TeamUtil.IsValid(from);
            Color colour = Core.TeamUtil.IsValid(team) ? CrewSkin.TextColour(team) : Color.white;

            switch (moment)
            {
                case GameMode.CashOutMoment.Started:
                    AddFeed($"{crew} is cashing out ${value}{place}", colour);
                    ShowBanner(mine ? $"CASHING OUT ${value} - HOLD THE STATION" : $"{crew.ToUpperInvariant()} IS CASHING OUT ${value}{place.ToUpperInvariant()}");
                    break;
                case GameMode.CashOutMoment.Added:
                    AddFeed($"{crew} added ${value} to the cash-out{place}", colour);
                    break;
                case GameMode.CashOutMoment.Hijacked:
                    AddFeed($"{crew} hijacked {TeamName(from)}'s cash-out{place}: ${value}", colour);
                    ShowBanner(mine ? $"HIJACKED - ${value} IS YOURS IF YOU HOLD IT" : wasMine ? "YOUR CASH-OUT WAS HIJACKED - TAKE IT BACK" : $"{crew.ToUpperInvariant()} HIJACKED THE CASH-OUT{place.ToUpperInvariant()}");
                    break;
                case GameMode.CashOutMoment.Banked:
                    AddFeed($"{crew} banked ${value}{place}", LootColor);
                    if (mine) ShowBanner($"BANKED ${value}");
                    break;
            }
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
            Core.Juice.Pop(bannerLabel, 1.35f, 0.32f); // (v0.15) banners land, they do not just appear
        }

        private void AddFeed(string text, Color color)
        {
            var entry = Panel(PanelBg);
            entry.style.marginBottom = 3;
            var label = Text(text, 13, color, bold: true);
            entry.Add(label);
            feed.Add(entry);
            Core.Juice.SlideIn(entry, new Vector2(-28f, 0f), 0.22f); // (v0.15)
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
