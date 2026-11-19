using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Pre-match class picker (UI Toolkit, code-built). Visible while the match is waiting / counting
    /// down and the game mode allows class changes. Click a card, or press the ability keys 1-4 /
    /// D-pad, to request that class from the server (<c>NetworkPlayer.RequestClass</c>). The state
    /// authority also gets a "Start match" button. Unlocks the cursor while shown.
    ///
    /// (v0.23) It can also be opened during a match, with B, while the player stands at their own crew's gate
    /// or has just redeployed (<c>ExtractionHeistGameMode.CanChangeLoadout</c>). A line at the left of the
    /// screen says when that is possible. Opened that way it shows only the class and gear choices, the
    /// character stands still while it is up, and it closes itself when the player may no longer change.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ClassPickerUI : MonoBehaviour
    {
        private const string RootName = "class-picker";

        [SerializeField] private float refreshRate = 10f;

        private UIDocument document;
        private VisualElement root;
        private VisualElement panel;
        private Label title, subtitle;
        private Button startButton;
        // [v0.13] private readonly Label[] loadoutLabels = new Label[2];
        private readonly Label[] loadoutLabels = new Label[3]; // (v0.14) + gadget
        // [v0.13] private static readonly Equipment.EquipmentSlotType[] loadoutSlots = { Equipment.EquipmentSlotType.Primary, Equipment.EquipmentSlotType.Secondary };
        // (v0.14) the gadget is part of the plan: it decides which of the building's obstacles this player can open
        private static readonly Equipment.EquipmentSlotType[] loadoutSlots =
        {
            Equipment.EquipmentSlotType.Primary, Equipment.EquipmentSlotType.Secondary, Equipment.EquipmentSlotType.Gadget
        };
        private readonly Button[] cards = new Button[4];
        private readonly Core.PlayerClassType[] cardTypes =
        {
            Core.PlayerClassType.Hacker, Core.PlayerClassType.Saboteur, Core.PlayerClassType.Demolitions, Core.PlayerClassType.Agent
        };

        private float nextRefresh;
        // (v0.23) opened by the player during a match
        private bool midMatchOpen, midMatchAllowed;
        private VisualElement briefingBox;
        private Label gateHint;
        private bool shown;
        private float nextReasonLog;

        /// <summary>True while a picker owns the cursor (GameSessionManager won't re-lock it).</summary>
        public static bool IsShown { get; private set; }
        private Network.NetworkPlayer local;

        private static readonly Color PanelBg = new Color(0f, 0f, 0f, 0.75f);
        private static readonly Color CardBg = new Color(1f, 1f, 1f, 0.08f);
        private static readonly Color CardSelected = new Color(1f, 0.82f, 0.2f, 0.35f);

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            var panelRoot = document.rootVisualElement;
            if (panelRoot == null)
            {
                Debug.LogError("[ClassPickerUI] UIDocument has no Panel Settings (see HeistHUD error). Picker disabled.");
                enabled = false;
                return;
            }

            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName };
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            root.style.alignItems = Align.Center; root.style.justifyContent = Justify.Center;
            root.style.backgroundColor = new Color(0f, 0f, 0f, 0.35f);
            root.style.display = DisplayStyle.None;
            panelRoot.Add(root);

            Build();

            // (v0.23) the line that says the loadout can be changed here, shown while the screen itself is closed
            panelRoot.Q("class-picker-hint")?.RemoveFromHierarchy();
            gateHint = new Label("") { name = "class-picker-hint", pickingMode = PickingMode.Ignore };
            gateHint.style.position = Position.Absolute;
            gateHint.style.left = 12; gateHint.style.bottom = 150;
            gateHint.style.fontSize = 14; gateHint.style.color = new Color(1f, 0.82f, 0.25f);
            gateHint.style.unityFontStyleAndWeight = FontStyle.Bold;
            gateHint.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.78f);
            gateHint.style.paddingLeft = 10; gateHint.style.paddingRight = 10; gateHint.style.paddingTop = 4; gateHint.style.paddingBottom = 4;
            gateHint.style.borderLeftWidth = 3; gateHint.style.borderLeftColor = new Color(1f, 0.82f, 0.25f);
            gateHint.style.display = DisplayStyle.None;
            panelRoot.Add(gateHint);
        }

        private void OnDisable()
        {
            gateHint?.RemoveFromHierarchy();
            root?.RemoveFromHierarchy();
            if (shown) SetCursorLocked(true);
            shown = false;
            IsShown = false;
        }

        private void Build()
        {
            panel = new VisualElement();
            panel.style.backgroundColor = PanelBg;
            panel.style.paddingLeft = 24; panel.style.paddingRight = 24; panel.style.paddingTop = 18; panel.style.paddingBottom = 18;
            panel.style.borderTopLeftRadius = 10; panel.style.borderTopRightRadius = 10; panel.style.borderBottomLeftRadius = 10; panel.style.borderBottomRightRadius = 10;
            panel.style.alignItems = Align.Center;
            root.Add(panel);

            // (v0.12, M4) the premise, said once, where everyone reads it
            var setting = new Label("THE OUROBOROS FREEPORT");
            setting.style.fontSize = 15; setting.style.color = new Color(1f, 0.82f, 0.25f); setting.style.unityFontStyleAndWeight = FontStyle.Bold;
            setting.style.letterSpacing = 6;
            panel.Add(setting);
            var premise = new Label("Everything in these vaults was taken from somewhere. Your crew is here to take it back. So are three others.");
            premise.style.fontSize = 13; premise.style.color = new Color(1f, 1f, 1f, 0.8f); premise.style.marginBottom = 10;
            premise.style.whiteSpace = WhiteSpace.Normal; premise.style.maxWidth = 760; premise.style.unityTextAlign = TextAnchor.MiddleCenter;
            panel.Add(premise);

            // [v0.11] title = new Label("CHOOSE YOUR OPERATIVE");
            title = new Label("CHOOSE YOUR SPECIALIST");
            title.style.fontSize = 28; title.style.color = Color.white; title.style.unityFontStyleAndWeight = FontStyle.Bold;
            panel.Add(title);

            subtitle = new Label("");
            subtitle.style.fontSize = 14; subtitle.style.color = new Color(1f, 1f, 1f, 0.7f); subtitle.style.marginBottom = 12;
            panel.Add(subtitle);

            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            panel.Add(row);

            for (int i = 0; i < cards.Length; i++)
            {
                var type = cardTypes[i];
                var card = new Button(() => Select(type));
                card.style.width = 200; card.style.height = 150;
                card.style.marginLeft = 6; card.style.marginRight = 6;
                card.style.backgroundColor = CardBg;
                card.style.borderTopLeftRadius = 8; card.style.borderTopRightRadius = 8; card.style.borderBottomLeftRadius = 8; card.style.borderBottomRightRadius = 8;
                card.style.whiteSpace = WhiteSpace.Normal;
                card.style.unityTextAlign = TextAnchor.UpperLeft;
                card.style.color = Color.white;
                card.style.fontSize = 13;
                card.text = CardText(type, i);
                row.Add(card);
                cards[i] = card;
            }

            // Loadout rows: Primary / Secondary with < > cyclers
            var loadoutBox = new VisualElement();
            loadoutBox.style.marginTop = 12; loadoutBox.style.alignItems = Align.Center;
            panel.Add(loadoutBox);
            for (int i = 0; i < loadoutSlots.Length; i++)
            {
                var slot = loadoutSlots[i];
                var rowEl = new VisualElement();
                rowEl.style.flexDirection = FlexDirection.Row; rowEl.style.alignItems = Align.Center; rowEl.style.marginTop = 4;
                var prev = new Button(() => CycleLoadout(slot, -1)) { text = "<" };
                var label = new Label(""); label.style.whiteSpace = WhiteSpace.Normal; label.style.maxWidth = 460; label.style.minWidth = 260; label.style.unityTextAlign = TextAnchor.MiddleCenter; label.style.color = Color.white; label.style.fontSize = 14;
                var next = new Button(() => CycleLoadout(slot, +1)) { text = ">" };
                rowEl.Add(prev); rowEl.Add(label); rowEl.Add(next);
                loadoutBox.Add(rowEl);
                loadoutLabels[i] = label;
            }

            startButton = new Button(StartMatch) { text = "Start match now" };
            startButton.style.marginTop = 14;
            startButton.style.fontSize = 15;
            startButton.style.paddingLeft = 18; startButton.style.paddingRight = 18; startButton.style.paddingTop = 8; startButton.style.paddingBottom = 8;
            startButton.style.display = DisplayStyle.None;
            panel.Add(startButton);

            // (v0.22) The briefing lives here now: the ten seconds before the match are for choosing a class and
            //         reading this, with nobody able to move. It used to appear after the match had started.
            var briefing = new VisualElement();
            briefingBox = briefing;
            briefing.style.marginTop = 14; briefing.style.paddingTop = 10; briefing.style.alignSelf = Align.Stretch;
            briefing.style.borderTopWidth = 1; briefing.style.borderTopColor = new Color(1f, 1f, 1f, 0.2f);
            panel.Add(briefing);
            var heading = new Label("HOW A HEIST GOES");
            heading.style.fontSize = 16; heading.style.color = new Color(1f, 0.82f, 0.25f); heading.style.unityFontStyleAndWeight = FontStyle.Bold;
            heading.style.letterSpacing = 3; heading.style.unityTextAlign = TextAnchor.MiddleCenter;
            briefing.Add(heading);
            briefing.Add(BriefingStep("1", "TAKE IT", "Crack one of the four vaults. Its door opens to its guards' key, to a drill you set and defend, or to a lock you crack."));
            briefing.Add(BriefingStep("2", "BANK IT", "Carry the loot to a cash-out station and hold it for a minute. Every crew is told. A rival who reaches it steals the lot, and so can you."));
            briefingRule = new Label("");
            briefing.Add(BriefingStep("3", "GET OUT", briefingRule));
            var keys = new Label($"[{Core.LocalInputSource.Hint(Core.InputButtons.Interact)}] hack, drill, cash out, revive        [{Core.LocalInputSource.Hint(Core.InputButtons.Ping)}] mark what you are looking at for your crew");
            keys.style.fontSize = 12; keys.style.color = new Color(1f, 1f, 1f, 0.7f); keys.style.unityFontStyleAndWeight = FontStyle.Bold;
            keys.style.marginTop = 8; keys.style.unityTextAlign = TextAnchor.MiddleCenter;
            briefing.Add(keys);
        }

        private Label briefingRule;

        private static VisualElement BriefingStep(string number, string name, string body) => BriefingStep(number, name, new Label(body));

        private static VisualElement BriefingStep(string number, string name, Label body)
        {
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row; row.style.marginTop = 5; row.style.alignItems = Align.FlexStart;
            var n = new Label(number);
            n.style.fontSize = 20; n.style.color = new Color(1f, 0.82f, 0.25f); n.style.unityFontStyleAndWeight = FontStyle.Bold; n.style.width = 24;
            var title = new Label(name);
            title.style.fontSize = 14; title.style.color = new Color(1f, 0.82f, 0.25f); title.style.unityFontStyleAndWeight = FontStyle.Bold; title.style.width = 84; title.style.marginTop = 3;
            body.style.fontSize = 13; body.style.color = new Color(1f, 1f, 1f, 0.9f); body.style.whiteSpace = WhiteSpace.Normal;
            body.style.flexShrink = 1; body.style.flexGrow = 1; body.style.maxWidth = 740; body.style.marginTop = 3;
            row.Add(n); row.Add(title); row.Add(body);
            return row;
        }

        private string CardText(Core.PlayerClassType type, int index)
        {
            string key = Core.LocalInputSource.Hint((Core.InputButtons)((int)Core.InputButtons.Ability1 + index));
            var registry = Data.ClassRegistry.Active;
            var data = registry != null ? registry.Get(type) : null;
            string stats = data != null ? $"\n{data.maxHealth:0} HP · {data.movementSpeed:0.0} spd" : "";
            string abilities = "";
            if (data != null && data.abilities != null)
            {
                foreach (var a in data.abilities)
                {
                    if (a != null && !string.IsNullOrEmpty(a.abilityName)) abilities += "\n• " + a.abilityName;
                }
            }
            string perk = "";
            var compType = Network.NetworkPlayer.ClassComponentType(type);
            if (compType != null)
            {
                // Instantiate a throwaway to read its coded movement perks (ClassData overrides are applied at spawn)
                var temp = new GameObject("perk-probe") { hideFlags = HideFlags.HideAndDontSave };
                var cls = (Core.BasePlayerClass)temp.AddComponent(compType);
                cls.Initialize();
                if (data != null) cls.ApplyClassData(data);
                perk = "\nMove: " + cls.Movement.Describe();
                Destroy(temp);
            }
            return $"[{key}]  {type}{stats}{perk}{abilities}";
        }

        private void Update()
        {
            if (root == null) return;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, refreshRate);
                Refresh();
            }

            // (v0.23) B opens and closes it during a match, where that is allowed
            if (midMatchAllowed && !GameMenu.IsOpen && Core.LocalInputSource.LoadoutPressed())
            {
                midMatchOpen = !midMatchOpen;
                Refresh();
            }

            if (!shown) return;

            // Keyboard / gamepad selection mirrors the ability keys
            for (int i = 0; i < cards.Length; i++)
            {
                if (Core.LocalInputSource.Pressed((Core.InputButtons)((int)Core.InputButtons.Ability1 + i)))
                {
                    Select(cardTypes[i]);
                }
            }
        }

        private void Refresh()
        {
            if (local == null || local.Object == null) local = FindLocal();

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            // [v0.22] bool shouldShow = local != null && local.Object != null && gm != null && gm.Object != null && gm.AllowClassChange;
            // (v0.23) before the match it is always up; during it, only when the player opened it and may still change
            bool ready = local != null && local.Object != null && gm != null && gm.Object != null;
            bool before = ready && gm.AllowClassChange;
            midMatchAllowed = ready && !before && gm.CanChangeLoadout(local);
            if (!midMatchAllowed || GameMenu.IsOpen) midMatchOpen = false;
            bool shouldShow = before || (midMatchAllowed && midMatchOpen);

            if (gateHint != null)
            {
                bool hint = midMatchAllowed && !midMatchOpen && !GameMenu.IsOpen;
                if ((gateHint.resolvedStyle.display != DisplayStyle.None) != hint) gateHint.style.display = hint ? DisplayStyle.Flex : DisplayStyle.None;
                if (hint)
                {
                    float window = gm.Config.loadoutSecondsAfterRedeploy - local.SecondsSinceRedeploy;
                    bool atGate = Player.TeamSpawnPoint.InZone(local.Team, local.Position, gm.Config.gateRadius);
                    gateHint.text = atGate ? $"AT YOUR GATE   [{Core.LocalInputSource.LoadoutKeyLabel}] change class or gear"
                                           : $"REDEPLOYED   [{Core.LocalInputSource.LoadoutKeyLabel}] change class or gear   {Mathf.CeilToInt(Mathf.Max(0f, window))} s";
                }
            }

            if (!shouldShow && Time.unscaledTime >= nextReasonLog)
            {
                nextReasonLog = Time.unscaledTime + 5f;
                string reason = local == null || local.Object == null ? "no local player yet"
                              : gm == null || gm.Object == null ? "game mode not spawned"
                              : !gm.AllowClassChange ? $"state {gm.CurrentState} / classChangeBeforeMatch={gm.Config.allowClassChangeBeforeMatch}"
                              : "unknown";
                Debug.Log($"[ClassPickerUI] hidden: {reason}");
            }

            if (shouldShow != shown)
            {
                shown = shouldShow;
                IsShown = shown;
                Debug.Log(shown ? "[ClassPickerUI] shown (cursor released)" : "[ClassPickerUI] hidden (cursor locked)");
                root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                SetCursorLocked(!shown);
                if (shown)
                {
                    for (int i = 0; i < cards.Length; i++) cards[i].text = CardText(cardTypes[i], i);
                }
            }
            if (!shown) return;

            // [v0.21] subtitle.text = gm.CurrentState == GameMode.ExtractionHeistGameMode.GameState.PreMatch
            // [v0.21]     ? $"Match starts in {Mathf.CeilToInt(gm.PhaseTimeRemaining ?? 0f)}s"
            // [v0.21]     : "Waiting for players...";
            // (v0.22) the countdown is the thing to watch on this screen, so it is large and says what it means
            bool counting = gm.CurrentState == GameMode.ExtractionHeistGameMode.GameState.PreMatch;
            // (v0.23) opened during a match: just the choices, and how to close it
            bool midMatch = !gm.AllowClassChange;
            if (briefingBox != null && (briefingBox.resolvedStyle.display == DisplayStyle.None) != midMatch) briefingBox.style.display = midMatch ? DisplayStyle.None : DisplayStyle.Flex;
            title.text = midMatch ? "CHANGE CLASS OR GEAR" : "CHOOSE YOUR SPECIALIST";
            if (midMatch)
            {
                subtitle.text = $"[{Core.LocalInputSource.LoadoutKeyLabel}] to close. You stand still while this is open. Available at your gate, and for {gm.Config.loadoutSecondsAfterRedeploy:0} s after a redeploy.";
                subtitle.style.fontSize = 14;
                subtitle.style.color = new Color(1f, 1f, 1f, 0.8f);
                subtitle.style.unityFontStyleAndWeight = FontStyle.Normal;
            }
            else
            subtitle.text = counting
                ? $"MATCH STARTS IN {Mathf.CeilToInt(gm.PhaseTimeRemaining ?? 0f)}   -   you are held at your gate until then"
                : "Waiting for players. Nobody can move until the match starts.";
            subtitle.style.fontSize = counting ? 20 : 14;
            subtitle.style.color = counting ? new Color(1f, 0.82f, 0.25f) : new Color(1f, 1f, 1f, 0.7f);
            subtitle.style.unityFontStyleAndWeight = counting ? FontStyle.Bold : FontStyle.Normal;
            if (briefingRule != null)
            {
                briefingRule.text = gm.Config.UseRunRules
                    ? $"The first crew with ${gm.LootQuota:N0}, banked or carried, calls the getaway. Then the lifts open: ride one out with what you still carry."
                    : "When the lifts open, ride one out. What you carry only counts if you leave with it.";
            }

            for (int i = 0; i < cards.Length; i++)
            {
                cards[i].style.backgroundColor = local.ClassType == cardTypes[i] ? CardSelected : CardBg;
            }

            bool canStart = gm.Object.HasStateAuthority && gm.CurrentState == GameMode.ExtractionHeistGameMode.GameState.WaitingForPlayers;
            startButton.style.display = canStart ? DisplayStyle.Flex : DisplayStyle.None;

            var lo = local.Loadout;
            for (int i = 0; i < loadoutSlots.Length; i++)
            {
                var data = lo != null && lo.Object != null ? lo.GetData(loadoutSlots[i]) : null;
                // [v0.13] loadoutLabels[i].text = $"{loadoutSlots[i]}: {(data != null ? data.equipmentName : "-")}";
                bool gadget = loadoutSlots[i] == Equipment.EquipmentSlotType.Gadget;
                loadoutLabels[i].text = gadget && data != null
                    ? $"Gadget: {data.equipmentName}\n{data.description}"
                    : $"{loadoutSlots[i]}: {(data != null ? data.equipmentName : "-")}";
            }
        }

        private void CycleLoadout(Equipment.EquipmentSlotType slot, int direction)
        {
            if (local == null || local.Object == null) return;
            var lo = local.Loadout;
            var registry = Data.EquipmentRegistry.Active;
            if (lo == null || registry == null) return;

            var options = registry.Options(slot, local.ClassType);
            if (options.Count == 0) return;

            var current = lo.GetData(slot);
            int index = options.IndexOf(current);
            index = ((index + direction) % options.Count + options.Count) % options.Count;
            lo.RequestEquip(options[index]);
        }

        private void Select(Core.PlayerClassType type)
        {
            if (local == null || local.Object == null) return;
            local.RequestClass(type);
        }

        private void StartMatch()
        {
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm != null && gm.Object != null && gm.Object.HasStateAuthority) gm.StartMatch();
        }

        private static void SetCursorLocked(bool locked)
        {
            UnityEngine.Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            UnityEngine.Cursor.visible = !locked;
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
