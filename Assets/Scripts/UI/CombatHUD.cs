using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Combat readouts drawn over the centre of the screen (v0.9, milestone M1):
    ///  - a crosshair whose gap follows the weapon's live spread (base + bloom + movement),
    ///  - a hit marker when one of your shots lands (white body, gold headshot, red kill) with a tick sound,
    ///  - arcs around the crosshair pointing at where recent damage came from,
    ///  - a reload prompt when the magazine is empty.
    ///
    /// Built in code with UI Toolkit and added automatically by <see cref="HeistHUD"/>, so no scene change is
    /// needed. Everything is driven by replicated or predicted state and local events.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class CombatHUD : MonoBehaviour
    {
        private const string RootName = "combat-hud";
        private const int ArrowCount = 4;

        [SerializeField] private float baseGapPixels = 5f;
        [SerializeField] private float barLength = 9f;
        [SerializeField] private float barThickness = 2f;
        [SerializeField] private float hitMarkerSeconds = 0.28f;
        [SerializeField] private float damageArrowSeconds = 1.4f;
        [SerializeField] private float damageArrowRadius = 90f;
        [Range(0f, 1f)] [SerializeField] private float tickVolume = 0.6f;

        private static readonly Color CrosshairColor = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color HitColor = Color.white;
        private static readonly Color HeadshotColor = new Color(1f, 0.82f, 0.25f);
        private static readonly Color KillColor = new Color(1f, 0.25f, 0.2f);
        private static readonly Color DamageColor = new Color(1f, 0.15f, 0.1f, 0.85f);

        private UIDocument document;
        private VisualElement root, centre, crosshair, hitMarker;
        private readonly VisualElement[] crossBars = new VisualElement[4];
        private readonly VisualElement[] hitBars = new VisualElement[4];
        private Label promptLabel, confirmLabel;

        private struct DamageArrow { public VisualElement Pivot; public Vector3 Origin; public float Until; }
        private readonly DamageArrow[] arrows = new DamageArrow[ArrowCount];
        private int nextArrow;

        private float hitUntil, confirmUntil;
        private float smoothedGap;
        private Network.NetworkPlayer local, damageSubscribed;
        private Camera localCamera;
        private AudioSource audioSource;

        private void OnEnable()
        {
            document = GetComponent<UIDocument>();
            var panelRoot = document != null ? document.rootVisualElement : null;
            if (panelRoot == null) { enabled = false; return; }

            panelRoot.Q(RootName)?.RemoveFromHierarchy();
            root = new VisualElement { name = RootName };
            panelRoot.Add(root);
            Build();

            if (audioSource == null)
            {
                audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
                audioSource.spatialBlend = 0f;
            }

            Network.NetworkPlayer.HitConfirmed += OnHitConfirmed;
        }

        private void OnDisable()
        {
            Network.NetworkPlayer.HitConfirmed -= OnHitConfirmed;
            if (damageSubscribed != null) { damageSubscribed.Damaged -= OnLocalDamaged; damageSubscribed = null; }
            root?.RemoveFromHierarchy();
        }

        // ------------------------------------------------------------------
        // Build

        private void Build()
        {
            root.style.position = Position.Absolute;
            root.style.left = 0; root.style.top = 0; root.style.right = 0; root.style.bottom = 0;
            root.pickingMode = PickingMode.Ignore;

            // A zero-size anchor at the screen centre; everything below is positioned relative to it.
            centre = new VisualElement { pickingMode = PickingMode.Ignore };
            centre.style.position = Position.Absolute;
            centre.style.left = Length.Percent(50);
            centre.style.top = Length.Percent(50);
            centre.style.width = 0; centre.style.height = 0;
            root.Add(centre);

            crosshair = Anchor();
            for (int i = 0; i < 4; i++) crossBars[i] = Bar(crosshair, CrosshairColor);
            var dot = Bar(crosshair, CrosshairColor);
            Place(dot, 0f, 0f, barThickness, barThickness);

            hitMarker = Anchor();
            for (int i = 0; i < 4; i++)
            {
                hitBars[i] = Bar(hitMarker, HitColor);
                hitBars[i].style.rotate = new Rotate(new Angle(i % 2 == 0 ? 45f : -45f, AngleUnit.Degree));
            }
            hitMarker.style.display = DisplayStyle.None;

            for (int i = 0; i < ArrowCount; i++)
            {
                var pivot = Anchor();
                var arc = Bar(pivot, DamageColor);
                Place(arc, 0f, -damageArrowRadius, 46f, 7f);
                arc.style.borderTopLeftRadius = 4; arc.style.borderTopRightRadius = 4;
                arc.style.borderBottomLeftRadius = 4; arc.style.borderBottomRightRadius = 4;
                pivot.style.display = DisplayStyle.None;
                arrows[i].Pivot = pivot;
            }

            confirmLabel = CentredLabel(34f, 13, KillColor);
            promptLabel = CentredLabel(58f, 13, Color.white);
        }

        private VisualElement Anchor()
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = 0; e.style.top = 0; e.style.width = 0; e.style.height = 0;
            centre.Add(e);
            return e;
        }

        private static VisualElement Bar(VisualElement parent, Color color)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.backgroundColor = color;
            parent.Add(e);
            return e;
        }

        /// <summary>Positions an element by its centre, in pixels from the screen centre.</summary>
        private static void Place(VisualElement e, float centreX, float centreY, float width, float height)
        {
            e.style.width = width;
            e.style.height = height;
            e.style.left = centreX - width * 0.5f;
            e.style.top = centreY - height * 0.5f;
        }

        private Label CentredLabel(float offsetY, int size, Color color)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.width = 300; label.style.left = -150; label.style.top = offsetY;
            label.style.unityTextAlign = TextAnchor.UpperCenter;
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.display = DisplayStyle.None;
            centre.Add(label);
            return label;
        }

        // ------------------------------------------------------------------
        // Update

        private void Update()
        {
            if (root == null) return;

            ResolveLocal();
            bool playing = local != null && local.Object != null && local.IsActiveInMatch
                           && !ClassPickerUI.IsShown && !KeypadUI.IsShown;

            crosshair.style.display = playing ? DisplayStyle.Flex : DisplayStyle.None;
            if (playing) UpdateCrosshair();
            UpdatePrompt(playing);
            UpdateHitMarker();
            UpdateDamageArrows(playing);
        }

        private void ResolveLocal()
        {
            if (local == null || local.Object == null || !local.IsLocalPlayer)
            {
                local = null;
                localCamera = null;
                var all = Network.NetworkPlayer.All;
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] != null && all[i].Object != null && all[i].IsLocalPlayer) { local = all[i]; break; }
                }
            }

            if (local != damageSubscribed)
            {
                if (damageSubscribed != null) damageSubscribed.Damaged -= OnLocalDamaged;
                damageSubscribed = local;
                if (damageSubscribed != null) damageSubscribed.Damaged += OnLocalDamaged;
            }
        }

        private void UpdateCrosshair()
        {
            float spreadDegrees = 0f;
            var loadout = local.Loadout;
            if (loadout != null && loadout.Object != null)
            {
                spreadDegrees = loadout.CurrentSpread((int)Equipment.EquipmentSlotType.Primary);
            }

            // Degrees to pixels: the panel's height covers the camera's vertical field of view.
            if (localCamera == null) localCamera = local.GetComponentInChildren<Camera>();
            float fov = localCamera != null ? localCamera.fieldOfView : 60f;
            float panelHeight = root.resolvedStyle.height;
            if (float.IsNaN(panelHeight) || panelHeight <= 0f) panelHeight = Screen.height;
            float targetGap = baseGapPixels + spreadDegrees * (panelHeight / Mathf.Max(1f, fov));

            // Ease so the gap breathes instead of snapping every shot.
            smoothedGap = Mathf.Lerp(smoothedGap, targetGap, 1f - Mathf.Exp(-22f * Time.unscaledDeltaTime));
            float d = smoothedGap + barLength * 0.5f;

            Place(crossBars[0], 0f, -d, barThickness, barLength); // top
            Place(crossBars[1], 0f, d, barThickness, barLength);  // bottom
            Place(crossBars[2], -d, 0f, barLength, barThickness); // left
            Place(crossBars[3], d, 0f, barLength, barThickness);  // right
        }

        private void UpdatePrompt(bool playing)
        {
            string text = null;
            if (playing)
            {
                var loadout = local.Loadout;
                int slot = (int)Equipment.EquipmentSlotType.Primary;
                var data = loadout != null && loadout.Object != null ? loadout.GetData(slot) : null;
                if (data != null && data.UsesAmmo)
                {
                    if (loadout.IsReloading(slot)) text = "RELOADING";
                    else if (loadout.Magazine[slot] == 0)
                    {
                        text = loadout.Reserve[slot] > 0 ? $"RELOAD  [{Core.LocalInputSource.Hint(Core.InputButtons.Reload)}]" : "NO AMMO";
                    }
                }
            }
            promptLabel.text = text ?? "";
            promptLabel.style.display = text != null ? DisplayStyle.Flex : DisplayStyle.None;

            bool showConfirm = Time.unscaledTime < confirmUntil;
            confirmLabel.style.display = showConfirm ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void UpdateHitMarker()
        {
            float remaining = hitUntil - Time.unscaledTime;
            if (remaining <= 0f)
            {
                if (hitMarker.resolvedStyle.display != DisplayStyle.None) hitMarker.style.display = DisplayStyle.None;
                return;
            }
            hitMarker.style.opacity = Mathf.Clamp01(remaining / Mathf.Max(0.01f, hitMarkerSeconds));
        }

        private void UpdateDamageArrows(bool playing)
        {
            for (int i = 0; i < ArrowCount; i++)
            {
                float remaining = arrows[i].Until - Time.unscaledTime;
                if (remaining <= 0f || !playing)
                {
                    arrows[i].Pivot.style.display = DisplayStyle.None;
                    continue;
                }

                // Angle between where the player is looking and where the damage came from, on the ground plane.
                Vector3 to = arrows[i].Origin - local.Position;
                float bearing = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg - local.LookYaw;
                arrows[i].Pivot.style.rotate = new Rotate(new Angle(bearing, AngleUnit.Degree));
                arrows[i].Pivot.style.opacity = Mathf.Clamp01(remaining / damageArrowSeconds * 1.5f);
                arrows[i].Pivot.style.display = DisplayStyle.Flex;
            }
        }

        // ------------------------------------------------------------------
        // Events

        private void OnHitConfirmed(Network.NetworkPlayer shooter, float damage, bool headshot, bool killed)
        {
            if (shooter == null || !shooter.IsLocalPlayer || hitMarker == null) return;

            Color color = killed ? KillColor : (headshot ? HeadshotColor : HitColor);
            float reach = headshot || killed ? 11f : 8f;
            float length = headshot || killed ? 10f : 8f;
            Place(hitBars[0], -reach, -reach, length, barThickness); // top-left, "\"
            Place(hitBars[1], reach, -reach, length, barThickness);  // top-right, "/"
            Place(hitBars[2], reach, reach, length, barThickness);   // bottom-right, "\"
            Place(hitBars[3], -reach, reach, length, barThickness);  // bottom-left, "/"
            for (int i = 0; i < 4; i++) hitBars[i].style.backgroundColor = color;

            hitMarker.style.opacity = 1f;
            hitMarker.style.display = DisplayStyle.Flex;
            hitUntil = Time.unscaledTime + hitMarkerSeconds * (killed ? 1.8f : 1f);

            if (killed || headshot)
            {
                confirmLabel.text = killed ? (headshot ? "HEADSHOT KILL" : "ELIMINATED") : "HEADSHOT";
                confirmLabel.style.color = killed ? KillColor : HeadshotColor;
                confirmUntil = Time.unscaledTime + (killed ? 1.2f : 0.5f);
            }

            if (audioSource != null)
            {
                var clip = killed ? Core.ProceduralSfx.KillTick : (headshot ? Core.ProceduralSfx.HeadshotTick : Core.ProceduralSfx.HitTick);
                audioSource.PlayOneShot(clip, tickVolume);
            }
        }

        private void OnLocalDamaged(Network.NetworkPlayer who, float amount)
        {
            if (who == null || who != local) return;
            Vector3 origin = who.LastDamageOrigin;
            if ((origin - who.Position).sqrMagnitude < 0.25f) return; // self-inflicted or unknown source

            // Refresh an arrow already pointing at (almost) the same source instead of stacking a new one.
            int slot = -1;
            for (int i = 0; i < ArrowCount; i++)
            {
                if (arrows[i].Until > Time.unscaledTime && (arrows[i].Origin - origin).sqrMagnitude < 4f) { slot = i; break; }
            }
            if (slot < 0) { slot = nextArrow; nextArrow = (nextArrow + 1) % ArrowCount; }

            arrows[slot].Origin = origin;
            arrows[slot].Until = Time.unscaledTime + damageArrowSeconds;
        }
    }
}
