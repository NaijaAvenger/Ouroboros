using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A staff door held shut by a magnet (v0.14). It cannot be shot, hacked or forced. It opens two ways:
    ///
    ///  - cut the district's power at its junction, and every maglock in range lets go for the blackout;
    ///  - use a Bypass Key gadget on it, which opens that one door quietly for a short while.
    ///
    /// So a crew either plans the blackout (and loses the lights and cameras with it, for everybody) or brings
    /// the key. The door is <see cref="width"/> wide along local X.
    /// </summary>
    public class MagLockDoor : NetworkBehaviour
    {
        public static readonly List<MagLockDoor> All = new List<MagLockDoor>();

        [SerializeField] private string doorName = "Staff Door";
        [SerializeField] private float width = 4f;
        [SerializeField] private float height = 4f;
        [SerializeField] private GameObject doorVisual;
        [SerializeField] private UnityEngine.AI.NavMeshObstacle navObstacle;

        [Networked] private TickTimer BypassTimer { get; set; }

        /// <summary>Raised on every peer when a door opens or shuts (door, now open). Cosmetic.</summary>
        public static event System.Action<MagLockDoor, bool> Switched;

        public string DoorName => doorName;
        public bool IsBypassed => Object != null && BypassTimer.IsRunning && !BypassTimer.Expired(Runner);
        public bool IsOpen => Object != null && (IsBypassed || PowerNode.IsBlackedOut(transform.position));

        private Collider[] colliders;
        private Renderer statusLight;
        private Transform modelRoot;
        private bool shownOpen, shownOnce;

        /// <summary>A bypass key: this door only, for <paramref name="seconds"/>. State authority.</summary>
        public bool Bypass(float seconds)
        {
            if (!Object.HasStateAuthority || seconds <= 0f || IsOpen) return false;
            BypassTimer = TickTimer.CreateFromSeconds(Runner, seconds);
            return true;
        }

        /// <summary>The closest shut maglock within <paramref name="range"/> of a point, or null.</summary>
        public static MagLockDoor FindShut(Vector3 position, float range)
        {
            MagLockDoor best = null;
            float bestSq = range * range;
            for (int i = 0; i < All.Count; i++)
            {
                var d = All[i];
                if (d == null || d.Object == null || d.IsOpen) continue;
                float sq = (d.transform.position - position).sqrMagnitude;
                if (sq < bestSq) { bestSq = sq; best = d; }
            }
            return best;
        }

        public override void Spawned()
        {
            All.Add(this);
            colliders = doorVisual != null ? doorVisual.GetComponentsInChildren<Collider>() : GetComponentsInChildren<Collider>();
            BuildVisuals();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
        }

        public override void Render()
        {
            bool open = IsOpen;
            if (shownOnce && open == shownOpen) return;
            bool changed = shownOnce;
            shownOnce = true;
            shownOpen = open;

            // [v0.14] if (modelRoot != null) modelRoot.gameObject.SetActive(!open);
            if (modelRoot != null) Core.JuiceDoor.For(modelRoot.gameObject, Core.DoorMotion.SlideSide, width).Set(open, animate: changed); // (v0.15) slides aside
            if (colliders != null) foreach (var c in colliders) if (c != null) c.enabled = !open;
            if (navObstacle != null) navObstacle.enabled = !open;
            if (statusLight != null) statusLight.sharedMaterial = Art.ArtMaterials.Emissive(open ? new Color(0.3f, 1f, 0.5f) : new Color(1f, 0.6f, 0.1f));
            if (changed) Switched?.Invoke(this, open);
        }

        private void BuildVisuals()
        {
            if (doorVisual != null)
            {
                var placeholder = doorVisual.GetComponent<Renderer>();
                if (placeholder != null) placeholder.enabled = false;
            }
            var frame = Art.Prim.Group(transform, "Maglock Model");
            Vector3 s = transform.lossyScale;
            frame.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));

            // The leaf hides when the door is open; the magnet housing and its light stay.
            modelRoot = Art.Prim.Group(frame, "Leaf");
            var steel = Art.ArtMaterials.Colour(new Color(0.3f, 0.33f, 0.38f));
            var dark = Art.ArtMaterials.Colour(new Color(0.13f, 0.14f, 0.16f));
            Art.Prim.Box(modelRoot, "Door", new Vector3(0f, height * 0.5f, 0f), new Vector3(width, height, 0.18f), steel);
            Art.Prim.Box(modelRoot, "Kick Plate", new Vector3(0f, 0.25f, 0f), new Vector3(width * 0.96f, 0.5f, 0.2f), dark);
            Art.Prim.Box(modelRoot, "Stripe", new Vector3(0f, height * 0.62f, 0f), new Vector3(width * 0.96f, 0.16f, 0.2f), Art.ArtMaterials.Colour(new Color(0.95f, 0.78f, 0.1f)));
            Art.Prim.Box(modelRoot, "Push Bar", new Vector3(0f, 1.05f, 0.13f), new Vector3(width * 0.6f, 0.06f, 0.06f), dark);

            Art.Prim.Box(frame, "Magnet", new Vector3(0f, height + 0.12f, 0f), new Vector3(width * 0.4f, 0.24f, 0.3f), dark);
            statusLight = Art.Prim.Box(frame, "Status", new Vector3(0f, height + 0.12f, 0.16f), new Vector3(0.3f, 0.1f, 0.02f), Art.ArtMaterials.Emissive(new Color(1f, 0.6f, 0.1f))).GetComponent<Renderer>();
            shownOnce = false;
        }
    }
}
