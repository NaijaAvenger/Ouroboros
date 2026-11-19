using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// The crew's side of the HUD (v0.17):
    ///
    ///  - <b>Pings.</b> A crewmate's ping shows as a marker in the world with what it is, who marked it and how
    ///    far it is, through walls and pinned to the screen edge when out of view. Red for a threat, gold for
    ///    loot, blue for a device, green for a downed crewmate, white for a place. One ping per player: a new
    ///    one replaces their last. Rival crews never see them.
    ///  - <b>Revive.</b> A downed crewmate's ID card carries a REVIVE marker with their name. Standing at it
    ///    shows a prompt and the progress. The downed player is told a revive is possible and how far it is.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class AssistHUD : MonoBehaviour
    {
        private const string RootName = "assist-hud";
        private static readonly bool ShowDownedCard = false;
        private const int MaxPings = 8, MaxRevives = 3;
        private static readonly Color GoColour = new Color(0.92f, 0.96f, 1f);
        private static readonly Color EnemyColour = new Color(1f, 0.3f, 0.22f);
        private static readonly Color LootColour = new Color(1f, 0.82f, 0.25f);
        private static readonly Color DeviceColour = new Color(0.4f, 0.75f, 1f);
        private static readonly Color ReviveColour = new Color(0.45f, 1f, 0.6f);

        private class Marker { public VisualElement Root, Dot; public Label Title, Sub; }
        private class Ping { public Network.NetworkPlayer By; public Core.PingKind Kind; public Vector3 Position; public string Label; public float Until; public bool Fresh; }

        private VisualElement root, prompt, promptFill, downed;
        private Label promptTitle, promptHint, downedText;
        private readonly Marker[] pingMarkers = new Marker[MaxPings];
        private readonly Marker[] reviveMarkers = new Marker[MaxRevives];
        private readonly List<Ping> pings = new List<Ping>();
        private Network.NetworkPlayer local;
        private Camera localCamera;

        public static Color ColourOf(Core.PingKind kind)
        {
            switch (kind)
            {
                case Core.PingKind.Enemy:  return EnemyColour;
                case Core.PingKind.Loot:   return LootColour;
                case Core.PingKind.Device: return DeviceColour;
                case Core.PingKind.Revive: return ReviveColour;
                default:                   return GoColour;
            }
        }

        public static string WordFor(Core.PingKind kind)
        {
            switch (kind)
            {
                case Core.PingKind.Enemy:  return "ENEMY";
                case Core.PingKind.Loot:   return "LOOT";
                case Core.PingKind.Device: return "USE";
                case Core.PingKind.Revive: return "REVIVE";
                default:                   return "HERE";
            }
        }

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

            for (int i = 0; i < MaxPings; i++) pingMarkers[i] = BuildMarker();
            for (int i = 0; i < MaxRevives; i++) reviveMarkers[i] = BuildMarker();

            prompt = Card(out promptTitle, out promptHint, out promptFill, ReviveColour);
            prompt.style.bottom = 240;
            downed = Card(out downedText, out _, out _, ReviveColour);
            downed.style.top = 96;

            Network.NetworkPlayer.Pinged += OnPinged;
        }

        private void OnDisable()
        {
            Network.NetworkPlayer.Pinged -= OnPinged;
            root?.RemoveFromHierarchy();
            pings.Clear();
        }

        private static Label Text(string text, int size, Color colour, bool bold)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size; label.style.color = colour;
            label.style.whiteSpace = WhiteSpace.NoWrap;
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            return label;
        }

        private VisualElement Card(out Label title, out Label hint, out VisualElement fill, Color edge)
        {
            var holder = new VisualElement { pickingMode = PickingMode.Ignore };
            holder.style.position = Position.Absolute;
            holder.style.left = 0; holder.style.right = 0;
            holder.style.alignItems = Align.Center;
            holder.style.display = DisplayStyle.None;
            root.Add(holder);
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.82f);
            card.style.paddingLeft = 14; card.style.paddingRight = 14; card.style.paddingTop = 7; card.style.paddingBottom = 8;
            card.style.borderLeftWidth = 3; card.style.borderLeftColor = edge;
            card.style.minWidth = 280; card.style.alignItems = Align.Center;
            holder.Add(card);
            title = Text("", 16, Color.white, true);
            hint = Text("", 12, new Color(1f, 1f, 1f, 0.75f), false);
            card.Add(title); card.Add(hint);
            var track = new VisualElement { pickingMode = PickingMode.Ignore };
            track.style.height = 5; track.style.marginTop = 5; track.style.alignSelf = Align.Stretch;
            track.style.backgroundColor = new Color(1f, 1f, 1f, 0.12f);
            fill = new VisualElement { pickingMode = PickingMode.Ignore };
            fill.style.height = Length.Percent(100);
            fill.style.backgroundColor = edge;
            track.Add(fill);
            card.Add(track);
            return holder;
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
            m.Dot.style.width = 14; m.Dot.style.height = 14;
            m.Dot.style.borderTopWidth = 2; m.Dot.style.borderBottomWidth = 2; m.Dot.style.borderLeftWidth = 2; m.Dot.style.borderRightWidth = 2;
            var ink = new Color(0f, 0f, 0f, 0.75f);
            m.Dot.style.borderTopColor = ink; m.Dot.style.borderBottomColor = ink; m.Dot.style.borderLeftColor = ink; m.Dot.style.borderRightColor = ink;
            m.Dot.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
            m.Title = Text("", 12, Color.white, true);
            m.Title.style.position = Position.Absolute; m.Title.style.top = 4;
            m.Sub = Text("", 10, new Color(1f, 1f, 1f, 0.8f), false);
            m.Sub.style.position = Position.Absolute; m.Sub.style.top = 19;
            m.Root.Add(m.Dot); m.Root.Add(m.Title); m.Root.Add(m.Sub);
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

        private void OnPinged(Network.NetworkPlayer by, Core.PingKind kind, Vector3 position, string label)
        {
            var me = Local();
            if (me == null || by == null || by.Object == null || by.Team != me.Team) return; // a crew's pings are its own

            for (int i = pings.Count - 1; i >= 0; i--) if (pings[i].By == by) pings.RemoveAt(i); // one ping per player
            if (pings.Count >= MaxPings) pings.RemoveAt(0);
            pings.Add(new Ping { By = by, Kind = kind, Position = position, Label = label, Until = Time.time + Core.AssistRules.PingLifetime(kind), Fresh = true });

            Color colour = ColourOf(kind);
            Art.Fx.Ring(position, kind == Core.PingKind.Go ? 1.4f : 1f, colour, 0.6f, 0.1f);
            Art.Fx.Flash(position, colour, 6f, 2.5f, 0.35f);
        }

        private void LateUpdate()
        {
            if (root == null) return;
            var me = Local();
            bool show = me != null && !ClassPickerUI.IsShown && !GameMenu.IsOpen;
            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            bool canPlace = show && !float.IsNaN(width) && width > 0f;
            if (canPlace && localCamera == null) localCamera = me.GetComponentInChildren<Camera>();
            if (localCamera == null) canPlace = false;

            // ---- pings
            int used = 0;
            for (int i = pings.Count - 1; i >= 0; i--) if (Time.time >= pings[i].Until || pings[i].By == null) pings.RemoveAt(i);
            for (int i = 0; i < pings.Count && used < MaxPings && canPlace; i++)
            {
                var p = pings[i];
                var m = pingMarkers[used++];
                string who = p.By == me ? "you" : p.By.DisplayName.ToString();
                string title = string.IsNullOrEmpty(p.Label) ? WordFor(p.Kind) : $"{WordFor(p.Kind)}  {p.Label}";
                Place(m, me, p.Position, title, who, ColourOf(p.Kind), width, height);
                float left = p.Until - Time.time;
                m.Root.style.opacity = Mathf.Clamp01(left / 1.2f); // fades out over its last second
                if (p.Fresh) { p.Fresh = false; Core.Juice.Pop(m.Root, 1.6f, 0.3f); }
            }
            for (int i = used; i < MaxPings; i++) Hide(pingMarkers[i]);

            // ---- downed crewmates, and the card the local player is standing at
            int usedRevives = 0;
            Interaction.IDCardDrop here = null, mine = null;
            var cards = Interaction.IDCardDrop.All;
            for (int i = 0; i < cards.Count && me != null; i++)
            {
                var c = cards[i];
                if (c == null || c.Object == null || c.OwnerTeam != me.Team || !c.OwnerIsDown) continue;
                var owner = c.Owner;
                if (owner == me) { mine = c; continue; }
                if (!me.IsActiveInMatch) continue;
                if (c.InReviveRange(me.Position)) here = c;
                if (canPlace && usedRevives < MaxRevives)
                {
                    string name = owner != null ? owner.DisplayName.ToString() : "crewmate";
                    Place(reviveMarkers[usedRevives++], me, c.transform.position + Vector3.up * 1.2f, $"REVIVE  {name}",
                        c.ReviveProgress > 0f ? $"{c.ReviveProgress:P0}" : "", ReviveColour, width, height);
                }
            }
            for (int i = usedRevives; i < MaxRevives; i++) Hide(reviveMarkers[i]);

            // ---- prompt for the reviver
            if (show && here != null)
            {
                var owner = here.Owner;
                string key = Core.LocalInputSource.Hint(Core.InputButtons.Interact);
                promptTitle.text = $"REVIVE {(owner != null ? owner.DisplayName.ToString().ToUpperInvariant() : "CREWMATE")}";
                promptHint.text = here.ReviveProgress > 0f ? "Keep holding. Letting go loses it." : $"Hold [{key}] to stand them back up here.";
                promptFill.style.width = Length.Percent(Mathf.Clamp01(here.ReviveProgress) * 100f);
                if (prompt.resolvedStyle.display == DisplayStyle.None) { prompt.style.display = DisplayStyle.Flex; Core.Juice.SlideIn(prompt, new Vector2(0f, 10f), 0.18f); }
            }
            else if (prompt.resolvedStyle.display != DisplayStyle.None) prompt.style.display = DisplayStyle.None;

            // ---- word for the player who is down
            // [v0.19] if (show && mine != null && !me.IsAlive)
            // (v0.20) off: the objective tracker says the same thing ("YOU ARE DOWN ..."), and this card sat
            //         where the top panel now is since the compass moved it down.
            if (ShowDownedCard && show && mine != null && !me.IsAlive)
            {
                downedText.text = mine.ReviveProgress > 0f
                    ? $"A crewmate is reviving you   {mine.ReviveProgress:P0}"
                    : "A crewmate can revive you at your ID card";
                if (downed.resolvedStyle.display == DisplayStyle.None) downed.style.display = DisplayStyle.Flex;
            }
            else if (downed.resolvedStyle.display != DisplayStyle.None) downed.style.display = DisplayStyle.None;
        }

        private static void Hide(Marker m)
        {
            if (m.Root.resolvedStyle.display != DisplayStyle.None) m.Root.style.display = DisplayStyle.None;
        }

        private void Place(Marker m, Network.NetworkPlayer me, Vector3 world, string title, string sub, Color colour, float width, float height)
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
            m.Root.style.opacity = 1f;
            m.Dot.style.backgroundColor = colour;
            m.Title.text = $"{title}  {distance:0}m";
            m.Title.style.color = colour;
            m.Sub.text = sub;
        }
    }
}
