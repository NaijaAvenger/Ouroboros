using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Markers over crewmates (v0.13): a diamond in the crew's colour, the name, a health bar and the distance,
    /// drawn over each teammate's head and visible through walls. When a teammate is off screen the marker
    /// slides to the screen edge in their direction. A teammate who is down shows a red marker where they fell.
    ///
    /// Rule of thumb for the player: a marker means friend. Rivals never get one, except while a reveal
    /// (Data Mine, Recon Drone, Net Sight) has tagged them, when they show a bare magenta diamond.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>. Can be switched off in
    /// the settings (<c>Core.GameSettings.TeammateMarkers</c>).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class TeammateMarkers : MonoBehaviour
    {
        private const string RootName = "teammate-markers";
        private const int MarkerCount = 12;
        private const float HeadHeight = 2.3f;
        private const float EdgeMargin = 0.05f;

        private static readonly Color DownColour = new Color(1f, 0.25f, 0.2f);
        private static readonly Color RevealedColour = new Color(1f, 0.2f, 0.9f);

        private class Marker
        {
            public VisualElement Root, Diamond, HealthTrack, HealthFill;
            public Label Name, Distance;
        }

        private VisualElement root;
        private readonly Marker[] markers = new Marker[MarkerCount];
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
            panelRoot.Insert(0, root); // under the rest of the HUD

            for (int i = 0; i < MarkerCount; i++) markers[i] = Build();
        }

        private void OnDisable()
        {
            root?.RemoveFromHierarchy();
        }

        private Marker Build()
        {
            var m = new Marker();
            // A zero-size anchor: children are centred on it, so placing a marker is two numbers.
            m.Root = new VisualElement { pickingMode = PickingMode.Ignore };
            m.Root.style.position = Position.Absolute;
            m.Root.style.width = 0; m.Root.style.height = 0;
            m.Root.style.alignItems = Align.Center;
            m.Root.style.display = DisplayStyle.None;
            root.Add(m.Root);

            m.Name = new Label { pickingMode = PickingMode.Ignore };
            m.Name.style.position = Position.Absolute;
            m.Name.style.bottom = 22; m.Name.style.fontSize = 12;
            m.Name.style.unityFontStyleAndWeight = FontStyle.Bold;
            m.Name.style.unityTextAlign = TextAnchor.MiddleCenter;
            m.Name.style.whiteSpace = WhiteSpace.NoWrap;
            m.Name.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            m.Root.Add(m.Name);

            m.HealthTrack = new VisualElement { pickingMode = PickingMode.Ignore };
            m.HealthTrack.style.position = Position.Absolute;
            m.HealthTrack.style.bottom = 16; m.HealthTrack.style.width = 44; m.HealthTrack.style.height = 4;
            m.HealthTrack.style.backgroundColor = new Color(0f, 0f, 0f, 0.6f);
            m.HealthFill = new VisualElement { pickingMode = PickingMode.Ignore };
            m.HealthFill.style.height = Length.Percent(100);
            m.HealthTrack.Add(m.HealthFill);
            m.Root.Add(m.HealthTrack);

            m.Diamond = new VisualElement { pickingMode = PickingMode.Ignore };
            m.Diamond.style.position = Position.Absolute;
            m.Diamond.style.bottom = 0; m.Diamond.style.width = 11; m.Diamond.style.height = 11;
            m.Diamond.style.rotate = new Rotate(new Angle(45f, AngleUnit.Degree));
            m.Diamond.style.borderTopWidth = 1; m.Diamond.style.borderBottomWidth = 1; m.Diamond.style.borderLeftWidth = 1; m.Diamond.style.borderRightWidth = 1;
            Color edge = new Color(0f, 0f, 0f, 0.85f);
            m.Diamond.style.borderTopColor = edge; m.Diamond.style.borderBottomColor = edge; m.Diamond.style.borderLeftColor = edge; m.Diamond.style.borderRightColor = edge;
            m.Root.Add(m.Diamond);

            m.Distance = new Label { pickingMode = PickingMode.Ignore };
            m.Distance.style.position = Position.Absolute;
            m.Distance.style.top = 2; m.Distance.style.fontSize = 10;
            m.Distance.style.color = new Color(1f, 1f, 1f, 0.8f);
            m.Distance.style.unityTextAlign = TextAnchor.MiddleCenter;
            m.Distance.style.whiteSpace = WhiteSpace.NoWrap;
            m.Distance.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.9f) };
            m.Root.Add(m.Distance);
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

        private void LateUpdate()
        {
            if (root == null) return;
            int used = 0;

            var me = Local();
            bool show = me != null && Core.GameSettings.TeammateMarkers && !ClassPickerUI.IsShown;
            if (show)
            {
                if (localCamera == null) localCamera = me.GetComponentInChildren<Camera>();
                if (localCamera == null || !localCamera.isActiveAndEnabled) localCamera = Camera.main; // spectating after death
            }

            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            if (show && localCamera != null && !float.IsNaN(width) && width > 0f && height > 0f)
            {
                var all = Network.NetworkPlayer.All;
                for (int i = 0; i < all.Count && used < MarkerCount; i++)
                {
                    var p = all[i];
                    if (p == null || p == me || p.Object == null || p.IsExtracted) continue;

                    bool mate = p.Team == me.Team && Core.TeamUtil.IsValid(p.Team);
                    bool tagged = !mate && p.IsAlive && p.HasStatus(Core.StatusFlags.Revealed);
                    if (!mate && !tagged) continue;

                    Place(markers[used++], me, p, mate, width, height);
                }
            }

            for (int i = used; i < MarkerCount; i++)
            {
                if (markers[i].Root.resolvedStyle.display != DisplayStyle.None) markers[i].Root.style.display = DisplayStyle.None;
            }
        }

        private void Place(Marker m, Network.NetworkPlayer me, Network.NetworkPlayer p, bool mate, float width, float height)
        {
            bool down = !p.IsAlive;
            Vector3 world = p.transform.position + Vector3.up * (down ? 0.6f : HeadHeight);
            Vector3 vp = localCamera.WorldToViewportPoint(world);

            // Behind the camera the projection is mirrored: flip it and push it to the edge.
            bool behind = vp.z <= 0.01f;
            if (behind) { vp.x = 1f - vp.x; vp.y = 1f - vp.y; }
            bool offScreen = behind || vp.x < EdgeMargin || vp.x > 1f - EdgeMargin || vp.y < EdgeMargin || vp.y > 1f - EdgeMargin;
            if (offScreen)
            {
                // Slide along the line from the screen centre until it meets the margin.
                Vector2 fromCentre = new Vector2(vp.x - 0.5f, vp.y - 0.5f);
                if (behind && fromCentre.sqrMagnitude < 0.0001f) fromCentre = Vector2.down;
                float reach = 0.5f - EdgeMargin;
                float scale = reach / Mathf.Max(Mathf.Abs(fromCentre.x), Mathf.Abs(fromCentre.y), 0.0001f);
                if (behind || scale < 1f) fromCentre *= scale;
                vp.x = 0.5f + fromCentre.x; vp.y = 0.5f + fromCentre.y;
            }

            float distance = Vector3.Distance(me.Position, p.Position);
            Color colour = !mate ? RevealedColour : (down ? DownColour : CrewSkin.Accent(p.Team));

            m.Root.style.left = vp.x * width;
            m.Root.style.top = (1f - vp.y) * height;
            m.Root.style.display = DisplayStyle.Flex;
            // Someone right in front of you needs no label over your crosshair.
            m.Root.style.opacity = !offScreen && distance < 3.5f ? 0.35f : 1f;

            m.Diamond.style.backgroundColor = colour;

            bool details = mate && !offScreen;
            m.Name.style.display = details ? DisplayStyle.Flex : DisplayStyle.None;
            m.HealthTrack.style.display = details && !down ? DisplayStyle.Flex : DisplayStyle.None;
            if (details)
            {
                m.Name.text = down ? $"{p.DisplayName}  DOWN" : p.DisplayName.ToString();
                m.Name.style.color = down ? DownColour : CrewSkin.TextColour(p.Team);
                m.Name.style.bottom = down ? 16 : 22;
                if (!down)
                {
                    float fraction = p.MaxHealth > 0f ? Mathf.Clamp01(p.Health / p.MaxHealth) : 0f;
                    m.HealthFill.style.width = Length.Percent(fraction * 100f);
                    m.HealthFill.style.backgroundColor = fraction > 0.5f ? new Color(0.35f, 0.9f, 0.45f) : (fraction > 0.25f ? new Color(1f, 0.8f, 0.2f) : DownColour);
                }
            }
            m.Distance.text = $"{distance:0}m";
        }
    }
}
