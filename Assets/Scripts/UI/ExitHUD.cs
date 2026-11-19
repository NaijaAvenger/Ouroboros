using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Points the player at the way out (v0.18). From the moment the getaway is called:
    ///
    ///  - a line near the bottom of the screen names the nearest open exit, how far it is and what it pays;
    ///  - a green marker sits on that exit in the world, through walls, pinned to the screen edge when it is
    ///    out of view, with the distance.
    ///
    /// "Nearest" is the nearest exit that is open right now: one that is resetting after a crew left is
    /// skipped. Both go away once the player is standing in the exit, where the extraction bar takes over.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class ExitHUD : MonoBehaviour
    {
        private const string RootName = "exit-hud";
        /// <summary>
        /// (v0.19) The line is off: the objective tracker now says "GET TO A LIFT" with the same name, distance
        /// and payout, and the line sat where the crew radio captions are drawn. The world marker stays.
        /// </summary>
        private static readonly bool ShowLine = false;
        private static readonly Color ExitColour = new Color(0.35f, 1f, 0.55f);

        private VisualElement root, chip, marker, dot;
        private Label chipText, markerText;
        private Network.NetworkPlayer local;
        private Camera localCamera;
        private GameMode.ExtractionPoint shown;

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

            marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.style.position = Position.Absolute;
            marker.style.width = 0; marker.style.height = 0; marker.style.alignItems = Align.Center;
            marker.style.display = DisplayStyle.None;
            dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.style.position = Position.Absolute; dot.style.bottom = 0;
            dot.style.width = 16; dot.style.height = 16;
            dot.style.backgroundColor = ExitColour;
            var ink = new Color(0f, 0f, 0f, 0.75f);
            dot.style.borderTopWidth = 2; dot.style.borderBottomWidth = 2; dot.style.borderLeftWidth = 2; dot.style.borderRightWidth = 2;
            dot.style.borderTopColor = ink; dot.style.borderBottomColor = ink; dot.style.borderLeftColor = ink; dot.style.borderRightColor = ink;
            dot.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
            markerText = Text("", 12, ExitColour, true);
            markerText.style.position = Position.Absolute; markerText.style.top = 5;
            marker.Add(dot); marker.Add(markerText);
            root.Add(marker);

            chip = new VisualElement { pickingMode = PickingMode.Ignore };
            chip.style.position = Position.Absolute;
            chip.style.bottom = 150; chip.style.left = 0; chip.style.right = 0;
            chip.style.alignItems = Align.Center;
            chip.style.display = DisplayStyle.None;
            root.Add(chip);
            var card = new VisualElement { pickingMode = PickingMode.Ignore };
            card.style.backgroundColor = new Color(0.04f, 0.04f, 0.06f, 0.8f);
            card.style.paddingLeft = 12; card.style.paddingRight = 12; card.style.paddingTop = 4; card.style.paddingBottom = 5;
            card.style.borderLeftWidth = 3; card.style.borderLeftColor = ExitColour;
            chip.Add(card);
            chipText = Text("", 14, Color.white, true);
            card.Add(chipText);
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
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            return label;
        }

        /// <summary>The nearest exit a crew could leave from right now, or null.</summary>
        public static GameMode.ExtractionPoint NearestOpen(Vector3 from, out float distance)
        {
            GameMode.ExtractionPoint best = null;
            float bestSq = float.MaxValue;
            var exits = GameMode.ExtractionPoint.All;
            for (int i = 0; i < exits.Count; i++)
            {
                var e = exits[i];
                if (e == null || e.Object == null || !e.IsOpen) continue;
                float sq = (e.transform.position - from).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = e; }
            }
            distance = best != null ? Mathf.Sqrt(bestSq) : 0f;
            return best;
        }

        private void Hide()
        {
            if (chip.resolvedStyle.display != DisplayStyle.None) chip.style.display = DisplayStyle.None;
            if (marker.resolvedStyle.display != DisplayStyle.None) marker.style.display = DisplayStyle.None;
            shown = null;
        }

        private void LateUpdate()
        {
            if (root == null) return;
            var me = Network.NetworkPlayer.FindLocal();
            if (me != local) { local = me; localCamera = null; }
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (me == null || gm == null || gm.Object == null || !gm.ExtractionOpen || !me.IsActiveInMatch || ClassPickerUI.IsShown || GameMenu.IsOpen)
            {
                Hide();
                return;
            }

            var exit = NearestOpen(me.Position, out float distance);
            if (exit == null)
            {
                // every exit is resetting after a crew left
                chipText.text = "EXITS RESETTING";
                chipText.style.color = new Color(1f, 0.8f, 0.3f);
                // [v0.18] if (chip.resolvedStyle.display == DisplayStyle.None) chip.style.display = DisplayStyle.Flex;
                if (ShowLine && chip.resolvedStyle.display == DisplayStyle.None) chip.style.display = DisplayStyle.Flex;
                if (marker.resolvedStyle.display != DisplayStyle.None) marker.style.display = DisplayStyle.None;
                shown = null;
                return;
            }

            float flat = new Vector2(exit.transform.position.x - me.Position.x, exit.transform.position.z - me.Position.z).magnitude;
            if (flat <= exit.ExtractionRadius) { Hide(); return; } // standing in it: the extraction bar says the rest

            string pays = exit.PayoutMultiplier >= 0.999f ? "pays in full" : $"pays {exit.PayoutMultiplier:P0}";
            chipText.text = $"NEAREST EXIT   {exit.ExitName}   {distance:0} m   {pays}";
            chipText.style.color = Color.white;
            // [v0.18] if (chip.resolvedStyle.display == DisplayStyle.None || exit != shown)
            if (ShowLine && (chip.resolvedStyle.display == DisplayStyle.None || exit != shown))
            {
                chip.style.display = DisplayStyle.Flex;
                Core.Juice.Pop(chip, 1.2f, 0.25f); // the nearest exit changed, or the getaway was just called
            }
            shown = exit;

            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            if (localCamera == null) localCamera = me.GetComponentInChildren<Camera>();
            if (localCamera == null || float.IsNaN(width) || width <= 0f)
            {
                if (marker.resolvedStyle.display != DisplayStyle.None) marker.style.display = DisplayStyle.None;
                return;
            }

            Vector3 vp = localCamera.WorldToViewportPoint(exit.transform.position + Vector3.up * 4.3f);
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
            marker.style.left = vp.x * width;
            marker.style.top = (1f - vp.y) * height;
            marker.style.display = DisplayStyle.Flex;
            markerText.text = $"EXIT  {distance:0}m";
        }
    }
}
