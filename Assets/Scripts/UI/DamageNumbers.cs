using UnityEngine;
using UnityEngine.UIElements;

namespace Ouroboros.UI
{
    /// <summary>
    /// Damage numbers (v0.14): every hit the local player lands shows what it took off, floating up from where it
    /// landed. White for a body hit, yellow for a headshot, red and larger for the hit that killed or broke the
    /// target. Hits that land together (shotgun pellets, a burst) add up into one number so they can be read.
    ///
    /// Driven by <c>NetworkPlayer.DamageDealt</c>, which the host sends only to the attacker. It covers players,
    /// guards, turrets and breakable objects. Built in code and added automatically by <see cref="HeistHUD"/>.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class DamageNumbers : MonoBehaviour
    {
        private const string RootName = "damage-numbers";
        private const int PoolSize = 20;
        private const float Life = 0.95f;
        private const float MergeSeconds = 0.14f;
        private const float MergeDistance = 1.4f;

        private static readonly Color BodyColour = Color.white;
        private static readonly Color HeadColour = new Color(1f, 0.85f, 0.2f);
        private static readonly Color KillColour = new Color(1f, 0.35f, 0.25f);

        private class Number
        {
            public Label Label;
            public Vector3 World;
            public float Amount, Age, Drift;
            public bool Active, Head, Kill;
        }

        private VisualElement root;
        private readonly Number[] pool = new Number[PoolSize];
        private Camera viewCamera;
        private Network.NetworkPlayer cameraOwner;

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

            for (int i = 0; i < PoolSize; i++)
            {
                var label = new Label { pickingMode = PickingMode.Ignore };
                label.style.position = Position.Absolute;
                label.style.unityFontStyleAndWeight = FontStyle.Bold;
                label.style.unityTextAlign = TextAnchor.MiddleCenter;
                label.style.width = 120; label.style.marginLeft = -60;
                label.style.textShadow = new TextShadow { offset = new Vector2(1f, 1f), blurRadius = 2f, color = new Color(0f, 0f, 0f, 0.95f) };
                label.style.display = DisplayStyle.None;
                root.Add(label);
                pool[i] = new Number { Label = label };
            }

            Network.NetworkPlayer.DamageDealt += OnDamageDealt;
        }

        private void OnDisable()
        {
            Network.NetworkPlayer.DamageDealt -= OnDamageDealt;
            root?.RemoveFromHierarchy();
        }

        private void OnDamageDealt(Network.NetworkPlayer attacker, Vector3 point, float damage, bool headshot, bool killed)
        {
            if (attacker == null || !attacker.IsLocalPlayer || root == null) return;
            if (attacker != cameraOwner) { cameraOwner = attacker; viewCamera = null; }

            // Fold into a number that just appeared at the same place.
            Number slot = null;
            for (int i = 0; i < PoolSize; i++)
            {
                var n = pool[i];
                if (n.Active && n.Age < MergeSeconds && (n.World - point).sqrMagnitude < MergeDistance * MergeDistance) { slot = n; break; }
            }
            if (slot != null)
            {
                slot.Amount += damage;
                slot.Head |= headshot;
                slot.Kill |= killed;
                slot.Age = 0f;
            }
            else
            {
                // A free slot, or the oldest one.
                for (int i = 0; i < PoolSize; i++)
                {
                    if (!pool[i].Active) { slot = pool[i]; break; }
                    if (slot == null || pool[i].Age > slot.Age) slot = pool[i];
                }
                slot.Active = true;
                slot.World = point;
                slot.Amount = damage;
                slot.Head = headshot;
                slot.Kill = killed;
                slot.Age = 0f;
                slot.Drift = Random.Range(-22f, 22f);
            }

            slot.Label.text = Mathf.Max(1, Mathf.RoundToInt(slot.Amount)).ToString();
            slot.Label.style.color = slot.Kill ? KillColour : (slot.Head ? HeadColour : BodyColour);
            slot.Label.style.fontSize = slot.Kill ? 26 : (slot.Head ? 22 : 18);
        }

        private void LateUpdate()
        {
            if (root == null) return;
            if (viewCamera == null && cameraOwner != null && cameraOwner.Object != null) viewCamera = cameraOwner.GetComponentInChildren<Camera>();
            float width = root.resolvedStyle.width, height = root.resolvedStyle.height;
            bool canDraw = viewCamera != null && viewCamera.isActiveAndEnabled && !float.IsNaN(width) && width > 0f;

            for (int i = 0; i < PoolSize; i++)
            {
                var n = pool[i];
                if (!n.Active) continue;
                n.Age += Time.unscaledDeltaTime;
                if (n.Age >= Life || !canDraw)
                {
                    n.Active = false;
                    n.Label.style.display = DisplayStyle.None;
                    continue;
                }

                Vector3 vp = viewCamera.WorldToViewportPoint(n.World);
                if (vp.z <= 0.05f) { n.Label.style.display = DisplayStyle.None; continue; }

                float t = n.Age / Life;
                float rise = (1f - (1f - t) * (1f - t)) * 46f;         // fast at first, then it hangs
                float pop = n.Age < 0.08f ? 1f + (0.08f - n.Age) * 5f : 1f;
                n.Label.style.left = vp.x * width + n.Drift * t;
                n.Label.style.top = (1f - vp.y) * height - 18f - rise;
                n.Label.style.opacity = t < 0.6f ? 1f : 1f - (t - 0.6f) / 0.4f;
                n.Label.style.scale = new Scale(new Vector3(pop, pop, 1f));
                n.Label.style.display = DisplayStyle.Flex;
            }
        }
    }
}
