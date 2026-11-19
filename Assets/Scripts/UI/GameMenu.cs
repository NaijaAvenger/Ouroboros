using System;
using UnityEngine;
using UnityEngine.UIElements;
using Cursor = UnityEngine.Cursor; // UIElements has a Cursor type too

namespace Ouroboros.UI
{
    /// <summary>
    /// The game shell (v0.13, milestone M5), in one UI Toolkit overlay:
    ///
    ///  - Main menu, shown before a session starts: quick play, solo, host a private match with a party code,
    ///    join with a code, and the player's name.
    ///  - Pause menu, on Esc / Start during a session: resume, leave the match, quit.
    ///  - Settings: look sensitivity, aim sensitivity, field of view, volume, and a few toggles.
    ///  - Controls: every action and what it is bound to, for keyboard and mouse and for a gamepad. It is a
    ///    reference list. The key caps look like buttons but cannot be clicked or focused, so nothing here
    ///    changes a binding.
    ///  - Profile: level, title and career totals.
    ///
    /// It also records each finished match in <see cref="Core.PlayerProfile"/>.
    /// Built in code and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class GameMenu : MonoBehaviour
    {
        private const string RootName = "game-menu";
        private const string QuickPlaySession = "freeport-quick";

        private enum Page { Profile, Settings, Controls }

        private static readonly Color Ink = new Color(0.05f, 0.05f, 0.07f, 0.94f);
        private static readonly Color Card = new Color(1f, 1f, 1f, 0.07f);
        private static readonly Color Gold = new Color(0.95f, 0.75f, 0.22f);
        private static readonly Color Soft = new Color(1f, 1f, 1f, 0.7f);

        /// <summary>True while the menu covers the screen. Gameplay input and cursor locking stand down.</summary>
        public static bool IsOpen { get; private set; }
        /// <summary>True when a menu exists in the scene, so it (not the session manager) owns the Esc key.</summary>
        public static bool Exists { get; private set; }

        private VisualElement root, nav, content;
        private Label statusLabel;
        private bool open, builtForSession, busy, matchRecorded;
        private Page page = Page.Profile;
        private string codeInput = "";

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            var panelRoot = document != null ? document.rootVisualElement : null;
            if (panelRoot == null) { enabled = false; return; }

            Core.GameSettings.Load();
            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName };
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            root.style.alignItems = Align.Center; root.style.justifyContent = Justify.Center;
            root.style.flexDirection = FlexDirection.Row;
            root.style.display = DisplayStyle.None;
            panelRoot.Add(root);

            // [v0.13] nav = Column(330);   // too narrow for the title: "OUROBOROS" ran past the edge
            nav = Column(NavWidth);
            content = Column(ContentWidth);
            content.style.marginLeft = 14;
            content.style.minHeight = 470;
            root.Add(nav); root.Add(content);
            root.RegisterCallback<GeometryChangedEvent>(_ => FitToScreen()); // (v0.14)

            Exists = true;
            Core.PlayerProfile.Changed += OnDataChanged;
        }

        private void OnDisable()
        {
            Core.PlayerProfile.Changed -= OnDataChanged;
            root?.RemoveFromHierarchy();
            Exists = false;
            IsOpen = false;
            open = false;
        }

        private const float NavWidth = 390f, ContentWidth = 560f, Gutter = 14f, Margin = 24f;

        /// <summary>
        /// (v0.14) Keeps both columns on screen. On a wide screen they sit side by side at full size; as the
        /// window narrows the right column shrinks, and below that it gives way so the buttons never clip.
        /// </summary>
        private void FitToScreen()
        {
            if (root == null || nav == null || content == null) return;
            float width = root.resolvedStyle.width;
            if (float.IsNaN(width) || width <= 0f) return;

            float navWidth = Mathf.Min(NavWidth, width - Margin * 2f);
            float room = width - navWidth - Gutter - Margin * 2f;
            bool showContent = room >= 320f;
            nav.style.width = navWidth;
            content.style.display = showContent ? DisplayStyle.Flex : DisplayStyle.None;
            if (showContent) content.style.width = Mathf.Min(ContentWidth, room);
        }

        private void OnDataChanged()
        {
            if (open && page == Page.Profile) BuildContent();
        }

        // ------------------------------------------------------------------
        // Open, close, and the keys that do it

        private static Network.GameSessionManager Session => Network.GameSessionManager.Instance;

        private void Update()
        {
            if (root == null) return;
            var session = Session;
            bool running = session != null && session.IsSessionRunning;

            if (!running)
            {
                // Before a session there is nothing behind the menu to return to: keep it up.
                bool wanted = session != null && session.ShowMainMenu;
                if (wanted && (!open || builtForSession)) Open(false);
                else if (!wanted && open) Close();
            }
            else
            {
                if (open && !builtForSession) Close(); // the session we were starting is up
                if (!KeypadUI.IsShown && !HackHUD.IsConsoleShown && Core.LocalInputSource.CursorTogglePressed())
                {
                    if (open) Close(); else Open(true);
                }
            }

            if (open)
            {
                root.BringToFront();
                if (Cursor.lockState != CursorLockMode.None) Cursor.lockState = CursorLockMode.None;
                if (!Cursor.visible) Cursor.visible = true;
            }

            RecordFinishedMatch();
        }

        private void Open(bool inSession)
        {
            open = true;
            IsOpen = true;
            builtForSession = inSession;
            root.style.display = DisplayStyle.Flex;
            root.style.backgroundColor = new Color(0f, 0f, 0f, inSession ? 0.6f : 0.78f);
            BuildNav();
            BuildContent();
            FitToScreen();
            // (v0.15) the panels arrive: the list from the left, the page a beat later
            Core.Juice.SlideIn(nav, new Vector2(-36f, 0f), 0.24f);
            Core.Juice.SlideIn(content, new Vector2(0f, 14f), 0.24f, 0.06f);
        }

        private void Close()
        {
            open = false;
            IsOpen = false;
            root.style.display = DisplayStyle.None;
            var session = Session;
            if (session != null && session.IsSessionRunning && !ClassPickerUI.IsShown)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        // ------------------------------------------------------------------
        // Left column

        private void BuildNav()
        {
            nav.Clear();
            var session = Session;

            if (!builtForSession)
            {
                // [v0.13] var title = Text("OUROBOROS", 40, Gold, true);
                // [v0.13] title.style.letterSpacing = 8;   // wider than the column it sat in
                var title = Text("OUROBOROS", 36, Gold, true);
                title.style.letterSpacing = 4;
                title.style.whiteSpace = WhiteSpace.NoWrap;
                title.style.overflow = Overflow.Hidden;
                nav.Add(title);
                var tag = Text("Everything in the freeport was taken from somewhere. Take it back.", 12, Soft);
                tag.style.whiteSpace = WhiteSpace.Normal; tag.style.marginBottom = 14;
                nav.Add(tag);

                nav.Add(Caption("YOUR NAME"));
                var nameField = new TextField { value = Core.PlayerProfile.Name, maxLength = Core.ProfileRules.MaxNameLength };
                nameField.style.marginBottom = 12;
                nameField.RegisterCallback<FocusOutEvent>(_ =>
                {
                    Core.PlayerProfile.Name = nameField.value;
                    nameField.SetValueWithoutNotify(Core.PlayerProfile.Name); // show what was kept
                });
                nav.Add(nameField);

                nav.Add(MenuButton("QUICK PLAY", "Join a public match, or open one", () => StartGame(Fusion.GameMode.AutoHostOrClient, QuickPlaySession), primary: true));
                nav.Add(MenuButton("SOLO", "Offline, with bots in every other seat", () => StartGame(Fusion.GameMode.Single, "solo")));
                nav.Add(MenuButton("HOST PRIVATE MATCH", "Get a party code to share", HostPrivate));

                var joinRow = new VisualElement();
                joinRow.style.flexDirection = FlexDirection.Row; joinRow.style.marginTop = 4; joinRow.style.marginBottom = 10;
                var codeField = new TextField { value = codeInput, maxLength = Core.ProfileRules.PartyCodeLength };
                codeField.style.flexGrow = 1; codeField.style.marginRight = 6;
                codeField.RegisterValueChangedCallback(evt =>
                {
                    codeInput = Core.ProfileRules.NormaliseCode(evt.newValue);
                    codeField.SetValueWithoutNotify(codeInput);
                });
                var join = new Button(JoinWithCode) { text = "JOIN CODE" };
                StyleButton(join, false);
                join.style.width = 124; join.style.marginBottom = 0; join.style.flexShrink = 0;
                codeField.style.minWidth = 0; codeField.style.flexShrink = 1;
                joinRow.Add(codeField); joinRow.Add(join);
                nav.Add(joinRow);
            }
            else
            {
                nav.Add(Text("PAUSED", 34, Gold, true));
                string code = session != null ? session.SessionCode : null;
                if (Core.ProfileRules.IsValidCode(code))
                {
                    var party = Text($"Party code   {code}", 16, Color.white, true);
                    party.style.marginBottom = 4;
                    nav.Add(party);
                    nav.Add(Text("Friends join from the main menu with this code.", 11, Soft));
                }
                var note = Text("The match keeps running while this is open.", 11, Soft);
                note.style.marginBottom = 12; note.style.marginTop = 4;
                nav.Add(note);
                nav.Add(MenuButton("RESUME", null, Close, primary: true));
            }

            nav.Add(MenuButton("PROFILE", null, () => Show(Page.Profile)));
            nav.Add(MenuButton("SETTINGS", null, () => Show(Page.Settings)));
            nav.Add(MenuButton("CONTROLS", null, () => Show(Page.Controls)));
            if (builtForSession) nav.Add(MenuButton("LEAVE MATCH", "Back to the main menu", LeaveMatch));
            nav.Add(MenuButton("QUIT TO DESKTOP", null, Quit));

            statusLabel = Text("", 12, new Color(1f, 0.6f, 0.4f));
            statusLabel.style.whiteSpace = WhiteSpace.Normal; statusLabel.style.marginTop = 8;
            nav.Add(statusLabel);
        }

        private void Show(Page next)
        {
            page = next;
            BuildContent();
            Core.Juice.SlideIn(content, new Vector2(0f, 12f), 0.2f);
            Core.Juice.Haptic(Core.Juice.Tap.Select);
        }

        private void HostPrivate()
        {
            string code = Core.ProfileRules.PartyCode(Environment.TickCount ^ (int)DateTime.UtcNow.Ticks);
            StartGame(Fusion.GameMode.Host, code);
        }

        private void JoinWithCode()
        {
            if (!Core.ProfileRules.IsValidCode(codeInput))
            {
                SetStatus($"A party code is {Core.ProfileRules.PartyCodeLength} letters or digits.");
                return;
            }
            StartGame(Fusion.GameMode.Client, codeInput);
        }

        private async void StartGame(Fusion.GameMode mode, string sessionName)
        {
            var session = Session;
            if (busy || session == null) return;
            busy = true;
            SetStatus(mode == Fusion.GameMode.Client ? "Looking for that match..." : "Starting...");
            bool ok;
            try { ok = await session.StartSession(mode, sessionName); }
            catch (Exception e) { Debug.LogException(e); ok = false; }
            busy = false;
            if (this == null || root == null) return; // the scene went away while we waited
            if (ok) Close();
            else SetStatus(mode == Fusion.GameMode.Client
                ? "No match with that code could be joined. Check the code and that the host is still in the match."
                : "The match could not be started. The console has the reason; a missing Photon App Id is the usual one. Solo needs no connection.");
        }

        private void LeaveMatch()
        {
            var session = Session;
            if (session == null) return;
            Close();
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            session.LeaveToMenu();
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        private void SetStatus(string text)
        {
            if (statusLabel != null) statusLabel.text = text ?? "";
        }

        // ------------------------------------------------------------------
        // Right column

        private void BuildContent()
        {
            content.Clear();
            switch (page)
            {
                case Page.Settings: BuildSettings(); break;
                case Page.Controls: BuildControls(); break;
                default: BuildProfile(); break;
            }
        }

        private void BuildProfile()
        {
            content.Add(Text("PROFILE", 22, Gold, true));
            int level = Core.PlayerProfile.Level;
            content.Add(Text(Core.PlayerProfile.Name, 28, Color.white, true));
            content.Add(Text($"Level {level}   {Core.PlayerProfile.Title}", 15, Soft));

            var track = new VisualElement();
            track.style.height = 10; track.style.marginTop = 10; track.style.marginBottom = 4;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            var fill = new VisualElement();
            fill.style.height = Length.Percent(100);
            fill.style.width = Length.Percent(Core.PlayerProfile.LevelProgress * 100f);
            fill.style.backgroundColor = Gold;
            track.Add(fill);
            content.Add(track);

            int next = level < Core.ProfileRules.MaxLevel ? Core.ProfileRules.XpToReach(level + 1) : Core.PlayerProfile.Xp;
            int nextTitle = Core.ProfileRules.NextTitleLevel(level);
            content.Add(Text(level < Core.ProfileRules.MaxLevel ? $"{Core.PlayerProfile.Xp} / {next} XP" : $"{Core.PlayerProfile.Xp} XP   (level cap)", 12, Soft));
            if (nextTitle > 0) content.Add(Text($"Next title at level {nextTitle}: {Core.ProfileRules.TitleForLevel(nextTitle)}", 12, Soft));

            var grid = new VisualElement();
            grid.style.flexDirection = FlexDirection.Row; grid.style.flexWrap = Wrap.Wrap; grid.style.marginTop = 16;
            int matches = Core.PlayerProfile.Matches;
            grid.Add(Stat("MATCHES", matches.ToString()));
            grid.Add(Stat("WINS", Core.PlayerProfile.Wins.ToString()));
            grid.Add(Stat("GOT OUT", matches > 0 ? $"{Core.PlayerProfile.Extractions * 100 / matches}%" : "-"));
            grid.Add(Stat("CREW LOOT BANKED", "$" + Core.PlayerProfile.LootBanked));
            grid.Add(Stat("BEST HAUL", "$" + Core.PlayerProfile.BestHaul));
            grid.Add(Stat("TAKEDOWNS", Core.PlayerProfile.Kills.ToString()));
            content.Add(grid);

            var last = Core.PlayerProfile.LastMatch;
            if (last.Valid)
            {
                var recent = Text($"Last match: +{last.Xp} XP" + (last.Won ? "   won" : "") + (last.Extracted ? "   got out" : "") + (last.LevelledUp ? $"   reached level {last.LevelAfter}" : ""), 13, Color.white);
                recent.style.marginTop = 12;
                content.Add(recent);
            }

            var note = Text("Levels and titles are for show. They never change what you bring into a match. " +
                            "The profile is stored on this computer only.", 11, Soft);
            note.style.whiteSpace = WhiteSpace.Normal; note.style.marginTop = 16;
            content.Add(note);
        }

        private void BuildSettings()
        {
            content.Add(Text("SETTINGS", 22, Gold, true));

            content.Add(Caption("LOOK"));
            content.Add(SliderRow("Sensitivity", Core.GameSettings.MinSensitivity, Core.GameSettings.MaxSensitivity, Core.GameSettings.Sensitivity, "0.00", v => Core.GameSettings.Sensitivity = v));
            content.Add(SliderRow("Sensitivity while aiming", Core.GameSettings.MinSensitivity, Core.GameSettings.MaxSensitivity, Core.GameSettings.AimSensitivity, "0.00", v => Core.GameSettings.AimSensitivity = v));
            content.Add(SliderRow("Field of view", Core.GameSettings.MinFov, Core.GameSettings.MaxFov, Core.GameSettings.FieldOfView, "0", v => Core.GameSettings.FieldOfView = v));
            content.Add(ToggleRow("Invert vertical look", Core.GameSettings.InvertY, v => Core.GameSettings.InvertY = v));

            content.Add(Caption("SOUND"));
            content.Add(SliderRow("Master volume", 0f, 1f, Core.GameSettings.MasterVolume, "P0", v => Core.GameSettings.MasterVolume = v));
            content.Add(SliderRow("Music volume", 0f, 1f, Core.GameSettings.MusicVolume, "P0", v => Core.GameSettings.MusicVolume = v));

            content.Add(Caption("DISPLAY"));
            content.Add(ToggleRow("Markers over crewmates", Core.GameSettings.TeammateMarkers, v => Core.GameSettings.TeammateMarkers = v));
            content.Add(ToggleRow("Crew radio lines", Core.GameSettings.CrewRadio, v => Core.GameSettings.CrewRadio = v));
            content.Add(ToggleRow("Full screen", Screen.fullScreen, v => Screen.fullScreen = v));

            // (v0.15)
            content.Add(Caption("FEEL AND LOOK"));
            content.Add(SliderRow("Screen shake", 0f, 1.5f, Core.GameSettings.ScreenShake, "P0", v => Core.GameSettings.ScreenShake = v));
            content.Add(ToggleRow("Controller vibration", Core.GameSettings.Vibration, v => Core.GameSettings.Vibration = v));
            content.Add(ToggleRow("Stylised look (cel shading and outlines)", Core.GameSettings.StylisedLook, v => Core.GameSettings.StylisedLook = v));

            var reset = new Button(() => { Core.GameSettings.ResetToDefaults(); BuildContent(); }) { text = "RESET TO DEFAULTS" };
            StyleButton(reset, false);
            reset.style.marginTop = 14; reset.style.width = 220;
            content.Add(reset);
        }

        private void BuildControls()
        {
            content.Add(Text("CONTROLS", 22, Gold, true));
            var hint = Text("What each action is bound to. This is a reference: the keys shown here cannot be changed from this screen.", 11, Soft);
            hint.style.whiteSpace = WhiteSpace.Normal; hint.style.marginBottom = 8;
            content.Add(hint);

            var header = Row();
            header.Add(Cell("ACTION", 230, Soft, 11));
            header.Add(Cell("KEYBOARD AND MOUSE", 150, Soft, 11));
            header.Add(Cell("GAMEPAD", 130, Soft, 11));
            content.Add(header);

            var list = new ScrollView(ScrollViewMode.Vertical);
            list.style.flexGrow = 1; list.style.maxHeight = 400;
            content.Add(list);

            void Fixed(string action, string keys, string pad) => list.Add(ControlRow(action, keys, pad));
            void Bound(string action, Core.InputButtons button) =>
                list.Add(ControlRow(action, Core.LocalInputSource.BindingLabel(button, false), Core.LocalInputSource.BindingLabel(button, true)));

            Fixed("Move", "W A S D", "Left stick");
            Fixed("Look", "Mouse", "Right stick");
            Bound("Jump, mantle, thrust up in zero gravity", Core.InputButtons.Jump);
            Bound("Sprint", Core.InputButtons.Sprint);
            Bound("Slide, thrust down in zero gravity", Core.InputButtons.Slide);
            Bound("Fire", Core.InputButtons.Primary);
            Bound("Aim down sights", Core.InputButtons.Secondary);
            Bound("Swap weapon", Core.InputButtons.NextWeapon);
            Bound("Reload", Core.InputButtons.Reload);
            Bound("Hack (hold on a device), jack in (press at a vault)", Core.InputButtons.Interact);
            Bound("Ability 1", Core.InputButtons.Ability1);
            Bound("Ability 2", Core.InputButtons.Ability2);
            Bound("Ability 3", Core.InputButtons.Ability3);
            Bound("Ability 4", Core.InputButtons.Ability4);
            Bound("Utility", Core.InputButtons.Utility);
            Bound("Gadget", Core.InputButtons.Gadget);
            Bound("Grapple (Smith)", Core.InputButtons.Grapple);
            Fixed("Answer a sign puzzle", "1 2 3 4", "-");
            Fixed("Menu", "Esc", "Start");
            Fixed("Debug overlay", "F1", "-");
        }

        // ------------------------------------------------------------------
        // Match record

        /// <summary>Adds each finished match to the profile, once.</summary>
        private void RecordFinishedMatch()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            bool ended = gm != null && gm.Object != null && gm.CurrentState == GameMode.ExtractionHeistGameMode.GameState.MatchEnded;
            if (!ended) { matchRecorded = false; return; }
            if (matchRecorded) return;

            Network.NetworkPlayer local = null;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Object != null && all[i].IsLocalPlayer) { local = all[i]; break; }
            }
            if (local == null) return; // spectating: nothing to record
            matchRecorded = true;

            int teamIndex = Core.TeamUtil.ToIndex(local.Team);
            int banked = teamIndex >= 0 && teamIndex < Core.GameConstants.MAX_TEAMS ? gm.TeamLoot[teamIndex] : 0;
            Core.PlayerProfile.RecordMatch(banked, local.IsExtracted, Core.TeamUtil.IsValid(local.Team) && gm.WinningTeam == local.Team, local.Kills);
        }

        // ------------------------------------------------------------------
        // Building blocks

        private static VisualElement Column(float width)
        {
            var column = new VisualElement();
            column.style.width = width;
            column.style.backgroundColor = Ink;
            column.style.paddingLeft = 22; column.style.paddingRight = 22; column.style.paddingTop = 20; column.style.paddingBottom = 20;
            column.style.borderLeftWidth = 3; column.style.borderLeftColor = Gold;
            column.style.flexShrink = 0;          // (v0.14) sized by FitToScreen, never squeezed by the row
            column.style.overflow = Overflow.Hidden;
            return column;
        }

        private static VisualElement Row()
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row; row.style.alignItems = Align.Center;
            row.style.marginBottom = 3;
            return row;
        }

        private static Label Text(string text, int size, Color color, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size; label.style.color = color;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Label Caption(string text)
        {
            var label = Text(text, 11, Soft, true);
            label.style.letterSpacing = 3; label.style.marginTop = 12; label.style.marginBottom = 4;
            return label;
        }

        private static Label Cell(string text, float width, Color color, int size)
        {
            var label = Text(text, size, color);
            label.style.width = width; label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static void StyleButton(Button button, bool primary)
        {
            button.style.height = 38; button.style.marginBottom = 6; button.style.marginLeft = 0; button.style.marginRight = 0;
            button.style.fontSize = 14; button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.unityTextAlign = TextAnchor.MiddleLeft; button.style.paddingLeft = 12;
            button.style.whiteSpace = WhiteSpace.NoWrap; button.style.overflow = Overflow.Hidden; // (v0.14)
            button.style.color = primary ? new Color(0.08f, 0.07f, 0.05f) : Color.white;
            button.style.backgroundColor = primary ? Gold : new Color(1f, 1f, 1f, 0.1f);
            button.style.borderTopWidth = 0; button.style.borderBottomWidth = 0; button.style.borderLeftWidth = 0; button.style.borderRightWidth = 0;
        }

        private static VisualElement MenuButton(string text, string hint, Action onClick, bool primary = false)
        {
            var button = new Button(onClick) { text = text };
            StyleButton(button, primary);
            if (string.IsNullOrEmpty(hint)) return button;

            var wrap = new VisualElement();
            button.style.marginBottom = 1;
            wrap.Add(button);
            var sub = Text(hint, 10, Soft);
            sub.style.marginBottom = 6; sub.style.marginLeft = 2;
            sub.style.whiteSpace = WhiteSpace.Normal; // (v0.14) wrap, never clip
            wrap.Add(sub);
            return wrap;
        }

        private static VisualElement Stat(string caption, string value)
        {
            var card = new VisualElement();
            card.style.width = 160; card.style.marginRight = 8; card.style.marginBottom = 8;
            card.style.backgroundColor = Card;
            card.style.paddingLeft = 10; card.style.paddingTop = 8; card.style.paddingBottom = 8;
            card.Add(Text(value, 22, Color.white, true));
            card.Add(Text(caption, 10, Soft));
            return card;
        }

        private static VisualElement SliderRow(string label, float min, float max, float value, string format, Action<float> set)
        {
            var row = Row();
            row.Add(Cell(label, 210, Color.white, 13));
            var slider = new Slider(min, max) { value = value };
            slider.style.flexGrow = 1;
            var readout = Text(value.ToString(format), 13, Soft);
            readout.style.width = 54; readout.style.unityTextAlign = TextAnchor.MiddleRight;
            slider.RegisterValueChangedCallback(evt =>
            {
                set(evt.newValue);
                readout.text = evt.newValue.ToString(format);
            });
            row.Add(slider); row.Add(readout);
            return row;
        }

        private static VisualElement ToggleRow(string label, bool value, Action<bool> set)
        {
            var row = Row();
            row.Add(Cell(label, 210, Color.white, 13));
            var toggle = new Toggle { value = value };
            toggle.RegisterValueChangedCallback(evt => set(evt.newValue));
            row.Add(toggle);
            return row;
        }

        /// <summary>One line of the controls list: the action, then a key cap for each device.</summary>
        private static VisualElement ControlRow(string action, string keys, string pad)
        {
            var row = Row();
            row.pickingMode = PickingMode.Ignore;
            row.Add(Cell(action, 230, Color.white, 13));
            row.Add(KeyCap(keys, 140));
            row.Add(KeyCap(pad, 120));
            return row;
        }

        /// <summary>
        /// A box drawn like a button that is not one: it ignores the pointer and cannot take focus, so the
        /// binding it shows is display only.
        /// </summary>
        private static VisualElement KeyCap(string text, float width)
        {
            var cap = new Label(string.IsNullOrEmpty(text) ? "-" : text) { pickingMode = PickingMode.Ignore, focusable = false };
            cap.style.width = width; cap.style.height = 26; cap.style.marginRight = 10;
            cap.style.fontSize = 12; cap.style.unityFontStyleAndWeight = FontStyle.Bold;
            cap.style.unityTextAlign = TextAnchor.MiddleCenter;
            cap.style.color = Color.white;
            cap.style.backgroundColor = new Color(1f, 1f, 1f, 0.1f);
            Color edge = new Color(1f, 1f, 1f, 0.25f);
            cap.style.borderTopWidth = 1; cap.style.borderBottomWidth = 2; cap.style.borderLeftWidth = 1; cap.style.borderRightWidth = 1;
            cap.style.borderTopColor = edge; cap.style.borderBottomColor = edge; cap.style.borderLeftColor = edge; cap.style.borderRightColor = edge;
            cap.style.borderTopLeftRadius = 4; cap.style.borderTopRightRadius = 4; cap.style.borderBottomLeftRadius = 4; cap.style.borderBottomRightRadius = 4;
            return cap;
        }
    }
}
