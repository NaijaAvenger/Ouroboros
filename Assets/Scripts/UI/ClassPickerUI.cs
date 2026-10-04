using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Pre-match class picker (UI Toolkit, code-built). Visible while the match is waiting / counting
    /// down and the game mode allows class changes. Click a card, or press the ability keys 1-4 /
    /// D-pad, to request that class from the server (<c>NetworkPlayer.RequestClass</c>). The state
    /// authority also gets a "Start match" button. Unlocks the cursor while shown.
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
        private readonly Label[] loadoutLabels = new Label[2];
        private static readonly Equipment.EquipmentSlotType[] loadoutSlots = { Equipment.EquipmentSlotType.Primary, Equipment.EquipmentSlotType.Secondary };
        private readonly Button[] cards = new Button[4];
        private readonly Core.PlayerClassType[] cardTypes =
        {
            Core.PlayerClassType.Hacker, Core.PlayerClassType.Saboteur, Core.PlayerClassType.Demolitions, Core.PlayerClassType.Agent
        };

        private float nextRefresh;
        private bool shown;

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
        }

        private void OnDisable()
        {
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

            title = new Label("CHOOSE YOUR OPERATIVE");
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
                var label = new Label(""); label.style.minWidth = 260; label.style.unityTextAlign = TextAnchor.MiddleCenter; label.style.color = Color.white; label.style.fontSize = 14;
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
            return $"[{key}]  {type}{stats}{abilities}";
        }

        private void Update()
        {
            if (root == null) return;

            if (Time.unscaledTime >= nextRefresh)
            {
                nextRefresh = Time.unscaledTime + 1f / Mathf.Max(1f, refreshRate);
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
            bool shouldShow = local != null && local.Object != null && gm != null && gm.Object != null && gm.AllowClassChange;

            if (shouldShow != shown)
            {
                shown = shouldShow;
                IsShown = shown;
                root.style.display = shown ? DisplayStyle.Flex : DisplayStyle.None;
                SetCursorLocked(!shown);
                if (shown)
                {
                    for (int i = 0; i < cards.Length; i++) cards[i].text = CardText(cardTypes[i], i);
                }
            }
            if (!shown) return;

            subtitle.text = gm.CurrentState == GameMode.ExtractionHeistGameMode.GameState.PreMatch
                ? $"Match starts in {Mathf.CeilToInt(gm.PhaseTimeRemaining ?? 0f)}s"
                : "Waiting for players...";

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
                loadoutLabels[i].text = $"{loadoutSlots[i]}: {(data != null ? data.equipmentName : "-")}";
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
