using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Everything the HUD says about cash-out stations (v0.16):
    ///
    ///  - a ticker under the match timer with every cash-out in progress: whose it is, how much, how long is
    ///    left and where, and how far a hijack has got. Money only drives decisions if everyone can see it;
    ///  - a marker on each running cash-out, through walls, pinned to the screen edge when out of view:
    ///    your crew's says DEFEND, a rival's says STEAL;
    ///  - a marker on the nearest free station while you are carrying loot;
    ///  - a prompt when you stand at a station, with what holding Interact will do.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CashOutHUD : MonoBehaviour
    {
        private const string RootName = "cash-out-hud";
        private const int MaxRows = 4, MaxMarkers = 6;
        private static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        private static readonly Color Danger = new Color(1f, 0.3f, 0.25f);
        private static readonly Color Free = new Color(0.4f, 1f, 0.55f);

        private class Row { public VisualElement Root, Swatch, Fill, HijackFill; public Label Text; }
        private class Marker { public VisualElement Root, Dot; public Label Text; }

        private VisualElement root, ticker, prompt, promptFill;
        private Label promptTitle, promptHint;
        private readonly Row[] rows = new Row[MaxRows];
        private readonly Marker[] markers = new Marker[MaxMarkers];
        private Network.NetworkPlayer local;
        private Camera localCamera;

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            var panelRoot = document != null ? document.rootVisualElement : null;
            if (panelRoot == null) { enabled = false; return; }

            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            panelRoot.Add(root);

            for (int i = 0; i < MaxMarkers; i++) markers[i] = BuildMarker();

            ticker = new VisualElement { pickingMode = PickingMode.Ignore };
            ticker.style.position = Position.Absolute;
            ticker.style.top = 132; ticker.style.left = 0; ticker.style.right = 0;
            ticker.style.alignItems = Align.Center;
            root.Add(ticker);
            for (int i = 0; i < MaxRows; i++) rows[i] = BuildRow();

            prompt = new VisualElement { pickingMode = PickingMode.Ignore };
            prompt.style.position = Position.Absolute;
            prompt.style.bottom = 196; prompt.style.left = 0; prompt.style.right = 0;
            prompt.style.alignItems = Align.Center;
            prompt.style.display = DisplayStyle.None;
            root.Add(prompt);
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.82f);
            card.style.paddingLeft = 14; card.style.paddingRight = 14; card.style.paddingTop = 7; card.style.paddingBottom = 8;
            card.style.borderLeftWidth = 3; card.style.borderLeftColor = Gold;
            card.style.minWidth = 300; card.style.alignItems = Align.Center;
            prompt.Add(card);
            promptTitle = Text("", 16, Color.white, true);
            promptHint = Text("", 12, new Color(1f, 1f, 1f, 0.75f), false);
            card.Add(promptTitle); card.Add(promptHint);
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = 5; track.style.marginTop = 5; track.style.alignSelf = Align.Stretch;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            promptFill = new VisualElement { pickingMode = PickingMode.Ignore };
            promptFill.style.height = Length.Percent(100);
            track.Add(promptFill);
            card.Add(track);
        }

        private void OnDisable()
        {
            root?.RemoveFromHierarchy();
        }

        private static Label Text(string text, int size, Color colour, bool bold)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size; label.style.color = colour;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            return label;
        }

        private Row BuildRow()
        {
            var row = new Row();
            row.Root = new VisualElement { pickingMode = PickingMode.Ignore };
            row.Root.style.flexDirection = FlexDirection.Row; row.Root.style.alignItems = Align.Center;
            row.Root.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.78f);
            row.Root.style.marginBottom = 3; row.Root.style.paddingRight = 10;
            row.Root.style.minWidth = 380;
            row.Root.style.display = DisplayStyle.None;
            ticker.Add(row.Root);

            row.Swatch = new VisualElement { pickingMode = PickingMode.Ignore };
            row.Swatch.style.width = 6; row.Swatch.style.alignSelf = Align.Stretch; row.Swatch.style.marginRight = 8;
            row.Root.Add(row.Swatch);

            var body = new VisualElement { pickingMode = PickingMode.Ignore };
            body.style.flexGrow = 1; body.style.paddingTop = 3; body.style.paddingBottom = 4;
            row.Text = Text("", 13, Color.white, true);
            body.Add(row.Text);
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = 4; track.style.marginTop = 2;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            row.Fill = new VisualElement { pickingMode = PickingMode.Ignore };
            row.Fill.style.position = Position.Absolute; row.Fill.style.left = 0; row.Fill.style.top = 0; row.Fill.style.bottom = 0;
            row.HijackFill = new VisualElement { pickingMode = PickingMode.Ignore };
            row.HijackFill.style.position = Position.Absolute; row.HijackFill.style.left = 0; row.HijackFill.style.top = 0; row.HijackFill.style.bottom = 0;
            row.HijackFill.style.backgroundColor = Danger;
            track.Add(row.Fill); track.Add(row.HijackFill);
            body.Add(track);
            row.Root.Add(body);
            return row;
        }

        private Marker BuildMarker()
        {
            var m = new Marker();
            m.Root = new VisualElement { pickingMode = PickingMode.Ignore };
            m.Root.style.position = Position.Absolute;
            m.Root.style.width = 0; m.Root.style.height = 0; m.Root.style.alignItems = Align.Center;
            m.Root.style.display = DisplayStyle.None;
            m.Dot = new VisualElement { pickingMode = PickingMode.Ignore };
            m.Dot.style.position = Position.Absolute; m.Dot.style.bottom = 0;
            m.Dot.style.width = 12; m.Dot.style.height = 12;
            m.Dot.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
            m.Text = Text("", 11, Color.white, true);
            m.Text.style.position = Position.Absolute; m.Text.style.top = 3;
            m.Text.style.unityTextAlign = TextAnchor.MiddleCenter;
            m.Root.Add(m.Dot); m.Root.Add(m.Text);
            root.Add(m.Root);
            return m;
        }

        private Network.NetworkPlayer Local()
        {
            if (local != null && local.Object != null && local.IsLocalPlayer) return local;
            local = null;
            localCamera = null;
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i] != null && all[i].Object != null && all[i].IsLocalPlayer) { local = all[i]; break; }
            }
            return local;
        }

        /// <summary>What the player has to deposit, in words: "$500", "$500 and the case", "the case", or nothing.</summary>
        private static string Carrying(Network.NetworkPlayer me)
        {
            bool hasCase = GameMode.CashOutStation.CarriesCase(me);
            if (me.CarriedLoot > 0) return hasCase ? $"${me.CarriedLoot} and the case" : $"${me.CarriedLoot}";
            return hasCase ? "the case" : "";
        }

        private static string Clock(float seconds)
        {
            int s = Mathf.Max(0, Mathf.CeilToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        private void LateUpdate()
        {
            if (root == null) return;
            var me = Local();
            bool show = !ClassPickerUI.IsShown && !GameMenu.IsOpen;
            var stations = GameMode.CashOutStation.All;

            // ---- ticker: every cash-out in progress
            int used = 0;
            for (int i = 0; i < stations.Count && used < MaxRows && show; i++)
            {
                var s = stations[i];
                if (s == null || s.Object == null || !s.IsActive) continue;
                var row = rows[used++];
                Color crew = CrewSkin.Accent(s.OwnerTeam);
                string where = GameMode.MapZone.NameAt(s.transform.position + Vector3.up);
                if (where.Length == 0) where = s.StationName;
                bool mine = me != null && me.Team == s.OwnerTeam;
                row.Text.text = $"{Core.TeamUtil.DisplayName(s.OwnerTeam).ToUpperInvariant()}   ${s.Pot}   {Clock(s.Remaining)}   {where}"
                              + (s.IsBeingHijacked ? $"   HIJACK {s.HijackProgress:P0}" + (s.Contested ? " (contested)" : "") : "");
                row.Text.style.color = mine ? CrewSkin.TextColour(s.OwnerTeam) : Color.white;
                row.Swatch.style.backgroundColor = crew;
                row.Fill.style.backgroundColor = crew;
                row.Fill.style.width = Length.Percent(s.Progress * 100f);
                row.HijackFill.style.width = Length.Percent(s.IsBeingHijacked ? s.HijackProgress * 100f : 0f);
                row.HijackFill.style.opacity = 0.75f;
                if (row.Root.resolvedStyle.display == DisplayStyle.None) { row.Root.style.display = DisplayStyle.Flex; Core.Juice.Pop(row.Root, 1.15f, 0.25f); }
            }
            for (int i = used; i < MaxRows; i++)
            {
                if (rows[i].Root.resolvedStyle.display != DisplayStyle.None) rows[i].Root.style.display = DisplayStyle.None;
            }

            // ---- markers and the prompt need a living local player
            int usedMarkers = 0;
            GameMode.CashOutStation here = null;
            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            if (show && me != null && me.IsActiveInMatch && !float.IsNaN(width) && width > 0f)
            {
                if (localCamera == null) localCamera = me.GetComponentInChildren<Camera>();
                GameMode.CashOutStation nearestFree = null;
                float nearestSq = float.MaxValue;
                bool carrying = me.CarriedLoot > 0 || GameMode.CashOutStation.CarriesCase(me);

                for (int i = 0; i < stations.Count; i++)
                {
                    var s = stations[i];
                    if (s == null || s.Object == null) continue;
                    if (s.InRange(me.Position)) here = s;
                    if (s.IsActive)
                    {
                        if (usedMarkers >= MaxMarkers || localCamera == null) continue;
                        bool mine = s.OwnerTeam == me.Team;
                        Place(markers[usedMarkers++], me, s.transform.position + Vector3.up * 3.3f,
                            mine ? $"DEFEND  {Clock(s.Remaining)}" : $"STEAL ${s.Pot}  {Clock(s.Remaining)}",
                            mine ? CrewSkin.Accent(me.Team) : Danger, width, height);
                    }
                    else if (carrying) // [v0.16 first draft] me.CarriedLoot > 0, which ignored a carried case
                    {
                        float sq = (s.transform.position - me.Position).sqrMagnitude;
                        if (sq < nearestSq) { nearestSq = sq; nearestFree = s; }
                    }
                }
                if (nearestFree != null && usedMarkers < MaxMarkers && localCamera != null)
                {
                    Place(markers[usedMarkers++], me, nearestFree.transform.position + Vector3.up * 3.3f, "CASH OUT", Free, width, height);
                }
            }
            for (int i = usedMarkers; i < MaxMarkers; i++)
            {
                if (markers[i].Root.resolvedStyle.display != DisplayStyle.None) markers[i].Root.style.display = DisplayStyle.None;
            }

            UpdatePrompt(me, here);
        }

        private void UpdatePrompt(Network.NetworkPlayer me, GameMode.CashOutStation s)
        {
            if (s == null || me == null)
            {
                if (prompt.resolvedStyle.display != DisplayStyle.None) prompt.style.display = DisplayStyle.None;
                return;
            }
            string key = Core.LocalInputSource.Hint(Core.InputButtons.Interact);
            string carrying = Carrying(me);
            float fill = 0f;
            Color colour = Gold;

            if (!s.IsActive)
            {
                promptTitle.text = "CASH-OUT STATION";
                promptHint.text = carrying.Length > 0
                    ? $"Hold [{key}] to cash out {carrying}. It banks in {s.CashOutSeconds:0}s, and everyone will know."
                    : "Bring loot here to bank it without leaving.";
            }
            else if (s.OwnerTeam == me.Team)
            {
                colour = CrewSkin.Accent(me.Team);
                promptTitle.text = $"CASHING OUT ${s.Pot}   {Clock(s.Remaining)}";
                promptHint.text = s.IsBeingHijacked
                    ? (s.Contested ? "They cannot finish the hijack while you stand here. Hold the station." : "It is being hijacked. Get to the station!")
                    : (carrying.Length > 0 ? $"Hold [{key}] to add {carrying}. Defend it until it banks." : "Defend it until it banks.");
                fill = s.Progress;
            }
            else
            {
                colour = Danger;
                promptTitle.text = $"{Core.TeamUtil.DisplayName(s.OwnerTeam).ToUpperInvariant()} IS CASHING OUT ${s.Pot}   {Clock(s.Remaining)}";
                bool otherCrewAtIt = s.IsBeingHijacked && s.HijackTeam != me.Team;
                promptHint.text = otherCrewAtIt ? "Another crew is already hijacking it."
                    : s.Contested ? "Contested: clear its owners away from the station."
                    : $"Hold [{key}] to hijack it. The pot and the timer become yours.";
                fill = s.HijackTeam == me.Team ? s.HijackProgress : 0f;
            }

            promptFill.style.width = Length.Percent(Mathf.Clamp01(fill) * 100f);
            promptFill.style.backgroundColor = colour;
            if (prompt.resolvedStyle.display == DisplayStyle.None)
            {
                prompt.style.display = DisplayStyle.Flex;
                Core.Juice.SlideIn(prompt, new Vector2(0f, 10f), 0.18f);
            }
        }

        private void Place(Marker m, Network.NetworkPlayer me, Vector3 world, string text, Color colour, float width, float height)
        {
            Vector3 vp = localCamera.WorldToViewportPoint(world);
            const float margin = 0.07f;
            bool behind = vp.z <= 0.01f;
            if (behind) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; }
            if (behind || vp.x < margin || vp.x > 1f - margin || vp.y < margin || vp.y > 1f - margin)
            {
                Vector2 fromCentre = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                if (fromCentre.sqrMagnitude < 0.0001f) fromCentre = Vector2.down;
                float scale = (0.5f - margin) / Mathf.Max(Mathf.Abs(fromCentre.x), Mathf.Abs(fromCentre.y), 0.0001f);
                if (behind || scale < 1f) fromCentre *= scale;
                vp.x = 0.5f + fromCentre.x; vp.y = 0.5f + fromCentre.y;
            }
            float distance = Vector3.Distance(me.Position, world);
            m.Root.style.left = vp.x * width;
            m.Root.style.top = (1f - vp.y) * height;
            m.Root.style.display = DisplayStyle.Flex;
            m.Dot.style.backgroundColor = colour;
            m.Text.text = $"{text}  {distance:0}m";
            m.Text.style.color = colour;
        }
    }
}
