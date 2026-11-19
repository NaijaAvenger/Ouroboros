using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// What the map tells the player (v0.14):
    ///
    ///  - the name of the zone they are standing in, bottom left ("BANK LOBBY"), gold for points of interest;
    ///  - a marker on every map event that is live (a delivery, a time lock, a blackout), with its distance;
    ///  - while a Loot Scanner sweep runs: markers on containers that hold loot, loose loot and open vaults nearby.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class MapHUD : MonoBehaviour
    {
        private const string RootName = "map-hud";
        private const int MarkerCount = 18;
        private const float ScanRange = 38f;

        private static readonly Color EventColour = new Color(1f, 0.78f, 0.2f);
        private static readonly Color ContainerColour = new Color(0.3f, 0.95f, 1f);
        private static readonly Color LootColour = new Color(0.4f, 1f, 0.5f);
        private static readonly Color VaultColour = new Color(1f, 0.55f, 0.2f);

        private class Marker { public VisualElement Root, Dot; public Label Text; }

        private VisualElement root;
        private Label zoneLabel;
        private readonly Marker[] markers = new Marker[MarkerCount];
        private Network.NetworkPlayer local;
        private Camera localCamera;
        private float nextZoneCheck, zonePulse;
        private string shownZone = "";

        private void OnEnable()
        {
            var document = GetComponent<UIDocument>();
            var panelRoot = document != null ? document.rootVisualElement : null;
            if (panelRoot == null) { enabled = false; return; }

            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName, pickingMode = PickingMode.Ignore };
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            panelRoot.Insert(0, root);

            zoneLabel = new Label { pickingMode = PickingMode.Ignore };
            zoneLabel.style.position = Position.Absolute;
            zoneLabel.style.left = 14; zoneLabel.style.bottom = 138;
            zoneLabel.style.fontSize = 15; zoneLabel.style.letterSpacing = 3;
            zoneLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            zoneLabel.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            root.Add(zoneLabel);

            for (int i = 0; i < MarkerCount; i++)
            {
                var m = new Marker();
                m.Root = new VisualElement { pickingMode = PickingMode.Ignore };
                m.Root.style.position = Position.Absolute;
                m.Root.style.width = 0; m.Root.style.height = 0; m.Root.style.alignItems = Align.Center;
                m.Root.style.display = DisplayStyle.None;
                m.Dot = new VisualElement { pickingMode = PickingMode.Ignore };
                m.Dot.style.position = Position.Absolute; m.Dot.style.bottom = 0;
                m.Dot.style.width = 9; m.Dot.style.height = 9;
                m.Text = new Label { pickingMode = PickingMode.Ignore };
                m.Text.style.position = Position.Absolute; m.Text.style.top = 2;
                m.Text.style.fontSize = 10; m.Text.style.whiteSpace = WhiteSpace.NoWrap;
                m.Text.style.unityTextAlign = TextAnchor.MiddleCenter;
                m.Text.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
                m.Root.Add(m.Dot); m.Root.Add(m.Text);
                root.Add(m.Root);
                markers[i] = m;
            }
        }

        private void OnDisable()
        {
            root?.RemoveFromHierarchy();
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

        private void LateUpdate()
        {
            if (root == null) return;
            var me = Local();
            bool playing = me != null && !ClassPickerUI.IsShown && !GameMenu.IsOpen;

            UpdateZone(me, playing);

            int used = 0;
            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            if (playing && !float.IsNaN(width) && width > 0f)
            {
                if (localCamera == null) localCamera = me.GetComponentInChildren<Camera>();
                if (localCamera == null || !localCamera.isActiveAndEnabled) localCamera = Camera.main;
                if (localCamera != null)
                {
                    used = DrawEvents(me, used, width, height);
                    if (me.Loadout != null && me.Loadout.IsScanning) used = DrawScan(me, used, width, height);
                }
            }
            for (int i = used; i < MarkerCount; i++)
            {
                if (markers[i].Root.resolvedStyle.display != DisplayStyle.None) markers[i].Root.style.display = DisplayStyle.None;
            }
        }

        // ------------------------------------------------------------------
        // Zone name

        private void UpdateZone(Network.NetworkPlayer me, bool playing)
        {
            if (!playing || !me.IsActiveInMatch)
            {
                zoneLabel.style.display = DisplayStyle.None;
                return;
            }
            if (Time.unscaledTime >= nextZoneCheck)
            {
                nextZoneCheck = Time.unscaledTime + 0.2f;
                var zone = GameMode.MapZone.At(me.Position + Vector3.up);
                string name = zone != null ? zone.zoneName.ToUpperInvariant() : "";
                if (name != shownZone)
                {
                    shownZone = name;
                    zoneLabel.text = name;
                    zoneLabel.style.color = zone != null && zone.pointOfInterest ? EventColour : Color.white;
                    zonePulse = 1f;
                }
            }
            zonePulse = Mathf.MoveTowards(zonePulse, 0f, Time.unscaledDeltaTime * 0.6f);
            zoneLabel.style.display = shownZone.Length > 0 ? DisplayStyle.Flex : DisplayStyle.None;
            zoneLabel.style.opacity = 0.6f + 0.4f * zonePulse;   // bright when you cross into it, then it settles
            zoneLabel.style.fontSize = 15 + Mathf.RoundToInt(5f * zonePulse);
        }

        // ------------------------------------------------------------------
        // Markers

        private int DrawEvents(Network.NetworkPlayer me, int used, float width, float height)
        {
            var director = GameMode.MapEventDirector.Instance;
            if (director == null || director.Object == null) return used;
            for (int i = 0; i < director.EventCount && used < MarkerCount; i++)
            {
                if (!director.IsMarked(i)) continue;
                var e = director.GetEvent(i);
                Vector3 point = GameMode.MapEventDirector.PointOf(e, director.transform) + Vector3.up * 2.2f;
                Place(markers[used++], me, point, e.title.ToUpperInvariant(), EventColour, width, height, true, 12);
            }
            return used;
        }

        private int DrawScan(Network.NetworkPlayer me, int used, float width, float height)
        {
            Vector3 origin = me.Position;
            float rangeSq = ScanRange * ScanRange;

            var breakables = Interaction.Breakable.All;
            for (int i = 0; i < breakables.Count && used < MarkerCount; i++)
            {
                var b = breakables[i];
                if (b == null || !b.IsAlive || !b.HoldsLoot || (b.Position - origin).sqrMagnitude > rangeSq) continue;
                Place(markers[used++], me, b.Position + Vector3.up * 1.2f, b.IsArmoured ? b.Label + " (armoured)" : b.Label, ContainerColour, width, height, false, 10);
            }

            var drops = GameMode.LootDrop.All;
            for (int i = 0; i < drops.Count && used < MarkerCount; i++)
            {
                var d = drops[i];
                if (d == null || d.Object == null || (d.transform.position - origin).sqrMagnitude > rangeSq) continue;
                // [v0.23] Place(markers[used++], me, d.transform.position + Vector3.up * 0.6f, "Loot", LootColour, width, height, false, 10);
                Place(markers[used++], me, d.transform.position + Vector3.up * 0.6f, d.IsAmmo ? "Ammo" : "Loot", d.IsAmmo ? Color.white : LootColour, width, height, false, 10); // (v0.24)
            }
            return used;
        }

        private void Place(Marker m, Network.NetworkPlayer me, Vector3 world, string text, Color colour, float width, float height, bool clampToEdge, int fontSize)
        {
            Vector3 vp = localCamera.WorldToViewportPoint(world);
            bool behind = vp.z <= 0.01f;
            const float margin = 0.06f;
            bool off = behind || vp.x < margin || vp.x > 1f - margin || vp.y < margin || vp.y > 1f - margin;
            if (off && !clampToEdge) { m.Root.style.display = DisplayStyle.None; return; }
            if (off)
            {
                if (behind) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; }
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
            m.Dot.style.rotate = new Rotate(new Angle(clampToEdge ? 45f : 0f, AngleUnit.Degree));
            m.Text.text = $"{text}  {distance:0}m";
            m.Text.style.color = colour;
            m.Text.style.fontSize = fontSize;
        }
    }
}
