using DG.Tweening;
using Lofelt.NiceVibrations;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.Core
{
    /// <summary>
    /// Game feel, in one place (v0.15). This is the only file that talks to the two imported packs:
    ///
    ///  - <b>Feel</b> (More Mountains) for camera shake, through its <c>MMCameraShaker</c> and <c>MMWiggle</c>,
    ///    and for controller rumble and phone haptics, through Nice Vibrations.
    ///  - <b>DOTween</b> (Demigiant) for everything that moves or pops: HUD elements, menu panels, doors,
    ///    loot and breakables.
    ///
    /// Everything else calls <see cref="Juice"/>. If either pack is updated and an API changes, this file is
    /// where the errors will be, and nothing else needs touching.
    ///
    /// All of it is cosmetic and local. Nothing here changes time scale: slowing local time would pull this
    /// peer out of step with the network simulation, so there are no freeze frames.
    /// </summary>
    public static class Juice
    {
        // ------------------------------------------------------------------
        // Camera shake (Feel)

        private static Transform shakeProxy;

        /// <summary>
        /// Gives the local camera a shake source. Feel shakes a small proxy object under the camera; the player
        /// controller reads <see cref="ShakeOffset"/> each frame and adds it to the view. That keeps Feel's
        /// wiggle and the controller's own camera motion from fighting over one transform.
        /// </summary>
        public static void AttachShaker(Transform camera)
        {
            if (camera == null) return;
            var existing = camera.Find("Shake Proxy");
            if (existing != null) { shakeProxy = existing; return; }

            var go = new GameObject("Shake Proxy");
            go.transform.SetParent(camera, false);
            go.AddComponent<MMCameraShaker>(); // brings its MMWiggle with it
            var wiggle = go.GetComponent<MMWiggle>();
            if (wiggle != null)
            {
                wiggle.PositionActive = true;
                wiggle.PositionWiggleProperties = new WiggleProperties
                {
                    WiggleType = WiggleTypes.Noise,
                    StartWigglingAutomatically = false,
                    WigglePermitted = false,
                    UseUnscaledTime = true,
                    RelativeAmplitude = true,
                    LimitedTime = true,
                    LimitedTimeResetValue = true
                };
            }
            shakeProxy = go.transform;
        }

        /// <summary>What the shake adds to the view right now, in the camera's local space. Zero when nothing is shaking.</summary>
        public static Vector3 ShakeOffset => shakeProxy != null ? shakeProxy.localPosition : Vector3.zero;

        /// <summary>
        /// Shakes the local view. <paramref name="amplitude"/> is in metres of camera travel: 0.02 is a nudge,
        /// 0.1 is a nearby explosion. Scaled by the player's screen shake setting.
        /// </summary>
        public static void Shake(float amplitude, float seconds = 0.25f, float frequency = 26f)
        {
            float scale = GameSettings.ScreenShake;
            if (shakeProxy == null || amplitude <= 0f || scale <= 0f) return;
            MMCameraShakeEvent.Trigger(seconds, amplitude * scale, frequency, 0f, 0f, 0f, false, null, true);
        }

        /// <summary>A shake that fades with distance from the local player: full at the centre, nothing at <paramref name="radius"/>.</summary>
        public static void ShakeAt(Vector3 worldPoint, float amplitude, float radius, float seconds = 0.35f)
        {
            if (shakeProxy == null || radius <= 0f) return;
            float distance = Vector3.Distance(shakeProxy.position, worldPoint);
            float falloff = 1f - Mathf.Clamp01(distance / radius);
            if (falloff > 0.02f) Shake(amplitude * falloff * falloff, seconds);
        }

        // ------------------------------------------------------------------
        // Haptics (Nice Vibrations, part of Feel): controller rumble, and phone haptics on mobile

        public enum Tap { Select, Light, Medium, Heavy, Success, Warning, Failure }

        public static void Haptic(Tap tap)
        {
            if (!GameSettings.Vibration) return;
            try
            {
                switch (tap)
                {
                    case Tap.Select:  HapticPatterns.PlayPreset(HapticPatterns.PresetType.Selection); break;
                    case Tap.Light:   HapticPatterns.PlayPreset(HapticPatterns.PresetType.LightImpact); break;
                    case Tap.Medium:  HapticPatterns.PlayPreset(HapticPatterns.PresetType.MediumImpact); break;
                    case Tap.Heavy:   HapticPatterns.PlayPreset(HapticPatterns.PresetType.HeavyImpact); break;
                    case Tap.Success: HapticPatterns.PlayPreset(HapticPatterns.PresetType.Success); break;
                    case Tap.Warning: HapticPatterns.PlayPreset(HapticPatterns.PresetType.Warning); break;
                    case Tap.Failure: HapticPatterns.PlayPreset(HapticPatterns.PresetType.Failure); break;
                }
            }
            catch (System.Exception e)
            {
                // A platform without haptics must never take the game down with it.
                if (!hapticWarned) { hapticWarned = true; Debug.LogWarning("[Juice] Haptics unavailable: " + e.Message); }
            }
        }
        private static bool hapticWarned;

        // ------------------------------------------------------------------
        // HUD and menu motion (DOTween driving UI Toolkit styles)

        /// <summary>Scales an element from <paramref name="from"/> back to its normal size with a little overshoot.</summary>
        public static void Pop(VisualElement element, float from = 1.25f, float seconds = 0.28f)
        {
            if (element == null) return;
            DOTween.Kill(element);
            float s = from;
            element.style.scale = new Scale(new Vector3(s, s, 1f));
            DOTween.To(() => s, v => { s = v; element.style.scale = new Scale(new Vector3(v, v, 1f)); }, 1f, seconds)
                .SetEase(Ease.OutBack).SetUpdate(true).SetTarget(element);
        }

        /// <summary>Fades an element in from clear.</summary>
        public static void FadeIn(VisualElement element, float seconds = 0.2f, float delay = 0f)
        {
            if (element == null) return;
            float a = 0f;
            element.style.opacity = 0f;
            DOTween.To(() => a, v => { a = v; element.style.opacity = v; }, 1f, seconds)
                .SetDelay(delay).SetEase(Ease.OutQuad).SetUpdate(true).SetTarget(element);
        }

        /// <summary>Slides an element into place from an offset in pixels, fading in as it comes.</summary>
        public static void SlideIn(VisualElement element, Vector2 fromOffset, float seconds = 0.25f, float delay = 0f)
        {
            if (element == null) return;
            float t = 0f;
            element.style.opacity = 0f;
            element.style.translate = new Translate(fromOffset.x, fromOffset.y);
            DOTween.To(() => t, v =>
                {
                    t = v;
                    element.style.opacity = v;
                    element.style.translate = new Translate(fromOffset.x * (1f - v), fromOffset.y * (1f - v));
                }, 1f, seconds)
                .SetDelay(delay).SetEase(Ease.OutCubic).SetUpdate(true).SetTarget(element);
        }

        /// <summary>Stops any HUD tween running on this element and leaves it at rest.</summary>
        public static void Settle(VisualElement element)
        {
            if (element == null) return;
            DOTween.Kill(element);
            element.style.opacity = 1f;
            element.style.scale = new Scale(Vector3.one);
        }

        // ------------------------------------------------------------------
        // World motion (DOTween on transforms)

        /// <summary>Grows something from nothing to its size with a bounce: loot hitting the floor, a delivery landing.</summary>
        public static void PopIn(Transform target, float seconds = 0.35f)
        {
            if (target == null) return;
            Vector3 size = target.localScale;
            target.localScale = size * 0.05f;
            target.DOScale(size, seconds).SetEase(Ease.OutBack).SetLink(target.gameObject);
        }

        /// <summary>A quick squash outward and back: something took a hit.</summary>
        public static void Punch(Transform target, float amount = 0.12f, float seconds = 0.22f)
        {
            if (target == null) return;
            target.DOKill(true); // finish the last punch first, or sizes drift
            target.DOPunchScale(target.localScale * amount, seconds, 8, 0.6f).SetLink(target.gameObject);
        }

        /// <summary>Moves and scales a transform to a pose, then runs <paramref name="then"/>. Used by doors.</summary>
        public static void MoveTo(Transform target, Vector3 localPosition, Vector3 localScale, float seconds, Ease ease, System.Action then = null)
        {
            if (target == null) return;
            target.DOKill();
            target.DOLocalMove(localPosition, seconds).SetEase(ease).SetLink(target.gameObject);
            var scale = target.DOScale(localScale, seconds).SetEase(ease).SetLink(target.gameObject);
            if (then != null) scale.OnComplete(() => then());
        }
    }

    /// <summary>How a door's leaf gets out of the way.</summary>
    public enum DoorMotion { RollUp, Sink, SlideSide }

    /// <summary>
    /// A door that moves when it opens (v0.15) where it used to vanish. It sits on the door's visible leaf and
    /// remembers the shut pose. The collider goes the moment the door is opened, so play is never waiting on
    /// an animation; only the picture takes its time. Added on demand by the door scripts.
    /// </summary>
    public class JuiceDoor : MonoBehaviour
    {
        public DoorMotion motion = DoorMotion.RollUp;
        public float seconds = 0.45f;
        [Tooltip("How far the leaf travels, in metres. 0 = work it out from the leaf's own size.")]
        public float travel = 0f;

        private Vector3 shutPosition, shutScale;
        private Renderer[] renderers;
        private Collider[] colliders;
        private bool captured, isOpen;

        public static JuiceDoor For(GameObject leaf, DoorMotion motion, float travel = 0f)
        {
            if (leaf == null) return null;
            var door = leaf.GetComponent<JuiceDoor>();
            if (door == null)
            {
                door = leaf.AddComponent<JuiceDoor>();
                door.motion = motion;
                door.travel = travel;
            }
            return door;
        }

        private void Capture()
        {
            if (captured) return;
            captured = true;
            shutPosition = transform.localPosition;
            shutScale = transform.localScale;
            renderers = GetComponentsInChildren<Renderer>(true);
            colliders = GetComponentsInChildren<Collider>(true);
        }

        /// <summary>Opens or shuts the door. With <paramref name="animate"/> false it snaps: used for the state a door spawns in.</summary>
        public void Set(bool open, bool animate)
        {
            Capture();
            if (!gameObject.activeSelf) gameObject.SetActive(true);
            foreach (var c in colliders) if (c != null) c.enabled = !open;
            if (animate && open == isOpen) return;
            isOpen = open;

            Vector3 openPosition = shutPosition, openScale = shutScale;
            // World-space size of the leaf along each of its own axes, for knowing how far to travel.
            Vector3 size = Vector3.Scale(shutScale, transform.parent != null ? transform.parent.lossyScale : Vector3.one);
            switch (motion)
            {
                case DoorMotion.RollUp:
                    openScale = new Vector3(shutScale.x, shutScale.y * 0.04f, shutScale.z);
                    openPosition = shutPosition + Vector3.up * (LocalUp(size.y) * 0.48f);
                    break;
                case DoorMotion.Sink:
                    openPosition = shutPosition - Vector3.up * LocalUp(travel > 0f ? travel : size.y);
                    break;
                case DoorMotion.SlideSide:
                    openPosition = shutPosition + Vector3.right * LocalRight(travel > 0f ? travel : size.x);
                    break;
            }

            if (!animate)
            {
                transform.DOKill();
                transform.localPosition = open ? openPosition : shutPosition;
                transform.localScale = open ? openScale : shutScale;
                Show(!open || motion == DoorMotion.RollUp);
                return;
            }

            Show(true);
            if (open) Juice.MoveTo(transform, openPosition, openScale, seconds, Ease.InOutCubic, () => Show(motion == DoorMotion.RollUp));
            else Juice.MoveTo(transform, shutPosition, shutScale, seconds * 0.8f, Ease.OutBounce);
        }

        // Travel is measured in the parent's space, where localPosition lives.
        private float LocalUp(float worldHeight) => transform.parent != null ? worldHeight / Mathf.Max(0.0001f, transform.parent.lossyScale.y) : worldHeight;
        private float LocalRight(float worldWidth) => transform.parent != null ? worldWidth / Mathf.Max(0.0001f, transform.parent.lossyScale.x) : worldWidth;

        private void Show(bool visible)
        {
            foreach (var r in renderers) if (r != null) r.enabled = visible;
        }
    }
}
