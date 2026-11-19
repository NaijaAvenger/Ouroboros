using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// A compass strip across the top of the screen (v0.20). It shows the half-circle in front of the view:
    /// the points of the compass on top, and under them a pip for everything worth walking to, at its bearing:
    ///
    ///   gold    the vault the objective points at, and cash-out stations that are free
    ///   crew    your crew's running cash-out, and your crewmates
    ///   red     a rival's running cash-out, and a crewmate's ENEMY ping
    ///   green   open lifts, and a downed crewmate
    ///   magenta a rival your crew has marked
    ///   other   a crewmate's ping, in the colour of its kind
    ///
    /// The things that matter most carry a word and a distance under the strip; "up" or "down" is added when
    /// the target is on the other floor. A pip for something behind you waits, dimmed, at the end of the strip
    /// on the side to turn toward. North is the top of the map: the Bank end of the Galleria.
    ///
    /// A strip and not a minimap: the maps are indoors and on two floors, where a flat map misleads more
    /// than it helps, and a strip costs nothing to draw.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CompassHUD : MonoBehaviour
    {
        private const string RootName = "compass-hud";
        private const float HalfView = 90f;     // degrees either side of the centre
        private const int TickCount = 24;       // one every 15 degrees
        private const int PipCount = 28;
        private static readonly string[] Points = { "N", "NE", "E", "SE", "S", "SW", "W", "NW" };
        private static readonly Color Gold = new Color(1f, 0.82f, 0.25f);
        private static readonly Color Red = new Color(1f, 0.3f, 0.22f);
        private static readonly Color Green = new Color(0.4f, 1f, 0.58f);
        private static readonly Color Magenta = new Color(1f, 0.2f, 0.9f);

        private class Tick { public VisualElement Line; public Label Name; }
        private class Pip { public VisualElement Root, Dot; public Label Text; }
        private struct Ping { public Network.NetworkPlayer By; public Core.PingKind Kind; public Vector3 Position; public float Until; }

        private VisualElement root, strip;
        private readonly Tick[] ticks = new Tick[TickCount];
        private readonly Pip[] pips = new Pip[PipCount];
        private readonly List<Ping> pings = new List<Ping>();
        private Network.NetworkPlayer local;
        private Camera localCamera;
        private int used;
        private float stripWidth, yaw;
        private Vector3 eye;

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

            strip = new VisualElement { pickingMode = PickingMode.Ignore };
            strip.style.position = Position.Absolute;
            strip.style.top = 8; strip.style.height = 30;
            strip.style.backgroundColor = new Color(0.03f, 0.03f, 0.05f, 0.62f);
            strip.style.borderBottomWidth = 2; strip.style.borderBottomColor = new Color(1f, 1f, 1f, 0.25f);
            strip.style.display = DisplayStyle.None;
            root.Add(strip);

            for (int i = 0; i < TickCount; i++)
            {
                bool named = i % 3 == 0;
                var t = new Tick { Line = new VisualElement { pickingMode = PickingMode.Ignore } };
                t.Line.style.position = Position.Absolute;
                t.Line.style.width = named ? 2 : 1; t.Line.style.height = named ? 7 : 4; t.Line.style.bottom = 0;
                t.Line.style.backgroundColor = new Color(1f, 1f, 1f, named ? 0.8f : 0.4f);
                strip.Add(t.Line);
                if (named)
                {
                    string point = Points[i / 3];
                    bool cardinal = point.Length == 1;
                    t.Name = Text(point, cardinal ? 15 : 11, point == "N" ? Gold : new Color(1f, 1f, 1f, cardinal ? 0.95f : 0.6f), true);
                    t.Name.style.position = Position.Absolute; t.Name.style.top = cardinal ? 1 : 4; t.Name.style.width = 30;
                    strip.Add(t.Name);
                }
                ticks[i] = t;
            }

            // the mark in the middle: where you are looking
            var needle = new VisualElement { pickingMode = PickingMode.Ignore };
            needle.style.position = Position.Absolute;
            needle.style.left = Length.Percent(50); needle.style.marginLeft = -1;
            needle.style.top = -3; needle.style.width = 2; needle.style.height = 36;
            needle.style.backgroundColor = new Color(1f, 0.82f, 0.25f, 0.9f);
            strip.Add(needle);

            for (int i = 0; i < PipCount; i++)
            {
                var p = new Pip { Root = new VisualElement { pickingMode = PickingMode.Ignore } };
                p.Root.style.position = Position.Absolute;
                p.Root.style.top = 27; p.Root.style.width = 0; p.Root.style.height = 0; p.Root.style.alignItems = Align.Center;
                p.Root.style.display = DisplayStyle.None;
                p.Dot = new VisualElement { pickingMode = PickingMode.Ignore };
                p.Dot.style.position = Position.Absolute; p.Dot.style.top = -6;
                p.Dot.style.width = 11; p.Dot.style.height = 11;
                var ink = new Color(0f, 0f, 0f, 0.8f);
                p.Dot.style.borderTopWidth = 1; p.Dot.style.borderBottomWidth = 1; p.Dot.style.borderLeftWidth = 1; p.Dot.style.borderRightWidth = 1;
                p.Dot.style.borderTopColor = ink; p.Dot.style.borderBottomColor = ink; p.Dot.style.borderLeftColor = ink; p.Dot.style.borderRightColor = ink;
                p.Dot.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
                p.Text = Text("", 10, Color.white, true);
                p.Text.style.position = Position.Absolute; p.Text.style.top = 8;
                p.Root.Add(p.Dot); p.Root.Add(p.Text);
                strip.Add(p.Root);
                pips[i] = p;
            }

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
            label.style.marginLeft = 0; label.style.marginRight = 0; label.style.paddingLeft = 0; label.style.paddingRight = 0;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            return label;
        }

        private void OnPinged(Network.NetworkPlayer by, Core.PingKind kind, Vector3 position, string label)
        {
            var me = Network.NetworkPlayer.FindLocal();
            if (me == null || by == null || by.Object == null || by.Team != me.Team) return;
            for (int i = pings.Count - 1; i >= 0; i--) if (pings[i].By == by) pings.RemoveAt(i);
            pings.Add(new Ping { By = by, Kind = kind, Position = position, Until = Time.time + Core.AssistRules.PingLifetime(kind) });
        }

        private void LateUpdate()
        {
            if (root == null) return;
            var me = Network.NetworkPlayer.FindLocal();
            if (me != local) { local = me; localCamera = null; }
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            float screen = root.resolvedStyle.width;
            bool show = me != null && gm != null && gm.Object != null && gm.IsMatchLive && !me.IsExtracted
                        && !ClassPickerUI.IsShown && !GameMenu.IsOpen && !float.IsNaN(screen) && screen > 0f;
            if (show && localCamera == null) localCamera = me.GetComponentInChildren<Camera>();
            if (!show || localCamera == null)
            {
                if (strip.resolvedStyle.display != DisplayStyle.None) strip.style.display = DisplayStyle.None;
                return;
            }
            if (strip.resolvedStyle.display == DisplayStyle.None) strip.style.display = DisplayStyle.Flex;

            // Wide enough to read, never so wide it runs under the cash board on a narrow screen.
            stripWidth = Mathf.Clamp(Mathf.Min(screen * 0.36f, screen - 820f), 240f, 560f);
            strip.style.width = stripWidth;
            strip.style.left = (screen - stripWidth) * 0.5f;

            // The camera, not the body: while spectating, the compass turns with the crewmate being watched.
            yaw = localCamera.transform.eulerAngles.y;
            eye = localCamera.transform.position;

            for (int i = 0; i < TickCount; i++)
            {
                float delta = Mathf.DeltaAngle(yaw, i * 15f);
                bool on = Mathf.Abs(delta) <= HalfView;
                var t = ticks[i];
                float x = stripWidth * 0.5f + delta / HalfView * stripWidth * 0.5f;
                t.Line.style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (on) t.Line.style.left = x;
                if (t.Name != null)
                {
                    bool nameOn = Mathf.Abs(delta) <= HalfView - 4f;
                    t.Name.style.display = nameOn ? DisplayStyle.Flex : DisplayStyle.None;
                    if (nameOn) t.Name.style.left = x - 15f;
                }
            }

            used = 0;
            Vector3 at = me.Position;

            // ---- (v0.21) Grand Vaults: every one still shut, the nearest named; keys on the floor; our drill; perks
            // [v0.20] one pip, on the nearest loot objective, and only while the instruction was to crack it:
            // [v0.20] if (ObjectiveHUD.Current == Core.Objective.CrackVault)
            // [v0.20] {
            // [v0.20] GameMode.LootObjective vault = null;
            // [v0.20] float bestSq = float.MaxValue;
            // [v0.20] var vaults = GameMode.LootObjective.All;
            // [v0.20] for (int i = 0; i < vaults.Count; i++)
            // [v0.20] {
            // [v0.20] var v = vaults[i];
            // [v0.20] if (v == null || v.Object == null || !v.IsAvailable) continue;
            // [v0.20] float sq = (v.transform.position - at).sqrMagnitude;
            // [v0.20] if (sq < bestSq) { bestSq = sq; vault = v; }
            // [v0.20] }
            // [v0.20] if (vault != null) Add(vault.transform.position, Gold, "VAULT", at, 1.2f);
            // [v0.20] }

            var nearestVault = Interaction.GrandVault.NearestShut(at, out _);
            int myPerks = GameMode.ExtractionHeistGameMode.PerksOf(me.Team);
            var grands = Interaction.GrandVault.All;
            for (int i = 0; i < grands.Count; i++)
            {
                var g = grands[i];
                if (g == null || g.Object == null) continue;
                if (!g.Opened)
                {
                    bool ours = g.IsDrilling && g.DrillTeam == me.Team;
                    bool theirs = g.IsDrilling && !ours;
                    Add(g.DoorPoint, ours ? CrewSkin.Accent(me.Team) : theirs ? Red : Gold, ours ? "DRILL" : (g == nearestVault ? "VAULT" : ""), at, ours || g == nearestVault ? 1.25f : 0.95f);
                    if (g.KeyState == Core.VaultKeyState.Dropped) Add(g.KeyPosition, Gold, "KEY", at, 1f);
                }
                else if (g.PerkAvailable && !Core.VaultRules.Has(myPerks, g.Perk))
                {
                    Add(g.PerkPoint, Interaction.GrandVault.PerkColour(g.Perk), "PERK", at, 1.1f);
                }
            }

            // ---- lifts, once the getaway is called
            if (gm.ExtractionOpen)
            {
                var nearest = ExitHUD.NearestOpen(at, out _);
                var exits = GameMode.ExtractionPoint.All;
                for (int i = 0; i < exits.Count; i++)
                {
                    var e = exits[i];
                    if (e == null || e.Object == null || !e.IsOpen) continue;
                    Add(e.transform.position, Green, e == nearest ? "LIFT" : "", at, e == nearest ? 1.25f : 0.9f);
                }
            }

            // ---- cash-out stations
            GameMode.CashOutStation nearestFree = null;
            float freeSq = float.MaxValue;
            var stations = GameMode.CashOutStation.All;
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                if (s == null || s.Object == null) continue;
                if (s.IsActive)
                {
                    bool mine = s.OwnerTeam == me.Team;
                    Add(s.transform.position, mine ? CrewSkin.Accent(me.Team) : Red, mine ? "DEFEND" : "STEAL", at, 1.2f);
                }
                else
                {
                    float sq = (s.transform.position - at).sqrMagnitude;
                    if (sq < freeSq) { freeSq = sq; nearestFree = s; }
                }
            }
            for (int i = 0; i < stations.Count; i++)
            {
                var s = stations[i];
                if (s == null || s.Object == null || s.IsActive) continue;
                bool wanted = s == nearestFree && me.CarriedLoot > 0;
                Add(s.transform.position, Gold, wanted ? "CASH OUT" : "", at, wanted ? 1.1f : 0.75f);
            }

            // ---- downed crewmates
            var cards = Interaction.IDCardDrop.All;
            for (int i = 0; i < cards.Count; i++)
            {
                var c = cards[i];
                if (c == null || c.Object == null || c.OwnerTeam != me.Team || !c.OwnerIsDown || c.Owner == me) continue;
                Add(c.transform.position, Green, "REVIVE", at, 1.1f);
            }

            // ---- crew pings
            for (int i = pings.Count - 1; i >= 0; i--) if (Time.time >= pings[i].Until || pings[i].By == null) pings.RemoveAt(i);
            for (int i = 0; i < pings.Count; i++)
            {
                if (pings[i].Kind == Core.PingKind.Revive) continue; // the card already has its pip
                Add(pings[i].Position, AssistHUD.ColourOf(pings[i].Kind), AssistHUD.WordFor(pings[i].Kind), at, 1f);
            }

            // ---- crewmates, and rivals this crew has marked
            var all = Network.NetworkPlayer.All;
            for (int i = 0; i < all.Count; i++)
            {
                var p = all[i];
                if (p == null || p == me || p.Object == null || !p.IsActiveInMatch) continue;
                if (p.Team == me.Team) Add(p.Position, CrewSkin.Accent(p.Team), "", at, 0.7f);
                else if (p.IsMarkedFor(me.Team)) Add(p.Position, Magenta, "", at, 0.95f);
            }

            for (int i = used; i < PipCount; i++)
            {
                if (pips[i].Root.resolvedStyle.display != DisplayStyle.None) pips[i].Root.style.display = DisplayStyle.None;
            }
        }

        /// <summary>Puts a pip at the bearing of <paramref name="world"/>. A word gives it a label with the distance.</summary>
        private void Add(Vector3 world, Color colour, string word, Vector3 from, float size)
        {
            if (used >= PipCount) return;
            Vector3 to = world - eye;
            if (to.x * to.x + to.z * to.z < 0.25f) return; // standing on it
            float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;
            float delta = Mathf.DeltaAngle(yaw, bearing);
            bool ahead = Mathf.Abs(delta) <= HalfView;
            float clamped = Mathf.Clamp(delta, -HalfView, HalfView);

            var p = pips[used++];
            p.Root.style.left = stripWidth * 0.5f + clamped / HalfView * stripWidth * 0.5f;
            p.Root.style.display = DisplayStyle.Flex;
            p.Root.style.opacity = ahead ? 1f : 0.45f;
            p.Dot.style.backgroundColor = colour;
            float px = 11f * size;
            p.Dot.style.width = px; p.Dot.style.height = px; p.Dot.style.top = -px * 0.5f - 1f;

            if (word.Length > 0 && ahead)
            {
                float distance = Vector3.Distance(from, world);
                float rise = world.y - from.y;
                string floor = rise > 3f ? "  up" : rise < -3f ? "  down" : "";
                p.Text.text = $"{word} {distance:0}m{floor}";
                p.Text.style.color = colour;
                p.Text.style.display = DisplayStyle.Flex;
            }
            else p.Text.style.display = DisplayStyle.None;
        }
    }
}
