using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Builds a small model for each kind of loot item out of primitives (v0.12, milestone M4), so a pickup on
    /// the floor looks like the thing its story card describes instead of a gold ball. Stand-ins for real
    /// prop art: recognisable shapes at a glance, nothing more. Visual only; no colliders.
    /// </summary>
    public static class ArtefactBuilder
    {
        private static readonly Color Brass = new Color(0.78f, 0.6f, 0.22f);
        private static readonly Color Gold = new Color(1f, 0.8f, 0.25f);
        private static readonly Color Bronze = new Color(0.55f, 0.36f, 0.18f);
        private static readonly Color Wood = new Color(0.42f, 0.27f, 0.15f);
        private static readonly Color Clay = new Color(0.72f, 0.42f, 0.28f);
        private static readonly Color Iron = new Color(0.3f, 0.31f, 0.34f);
        private static readonly Color Paper = new Color(0.9f, 0.86f, 0.72f);
        private static readonly Color Indigo = new Color(0.12f, 0.16f, 0.45f);
        private static readonly Color Tech = new Color(0.16f, 0.18f, 0.22f);

        /// <summary>Builds the model as a child of <paramref name="parent"/>. Sizes are in metres; the model sits on local y = 0.</summary>
        public static Transform Build(Transform parent, Data.LootShape shape, Data.LootTier tier)
        {
            var root = new GameObject("Artefact").transform;
            root.SetParent(parent, false);
            Color glow = Data.LootCatalog.TierColor(tier);

            switch (shape)
            {
                case Data.LootShape.Weights:
                    for (int i = 0; i < 4; i++) Box(root, new Vector3(-0.12f + i * 0.08f, 0.03f + i * 0.012f, 0f), Vector3.one * (0.05f + i * 0.02f), Brass, new Vector3(0f, i * 17f, 0f));
                    break;
                case Data.LootShape.Cloth:
                    Cylinder(root, new Vector3(0f, 0.08f, 0f), new Vector3(0.16f, 0.2f, 0.16f), Indigo, new Vector3(0f, 0f, 90f));
                    Cylinder(root, new Vector3(0f, 0.08f, 0f), new Vector3(0.165f, 0.03f, 0.165f), Paper, new Vector3(0f, 0f, 90f));
                    break;
                case Data.LootShape.Headrest:
                    Box(root, new Vector3(0f, 0.02f, 0f), new Vector3(0.26f, 0.04f, 0.12f), Wood);
                    Box(root, new Vector3(0f, 0.09f, 0f), new Vector3(0.06f, 0.1f, 0.06f), Wood);
                    Box(root, new Vector3(0f, 0.16f, 0f), new Vector3(0.3f, 0.04f, 0.1f), Wood, new Vector3(0f, 0f, 0f));
                    break;
                case Data.LootShape.Beads:
                    for (int i = 0; i < 10; i++)
                    {
                        float a = i / 10f * Mathf.PI * 2f;
                        Sphere(root, new Vector3(Mathf.Cos(a) * 0.12f, 0.03f, Mathf.Sin(a) * 0.12f), Vector3.one * 0.05f, i % 3 == 0 ? new Color(0.8f, 0.15f, 0.15f) : i % 3 == 1 ? new Color(0.15f, 0.3f, 0.8f) : Paper);
                    }
                    break;
                case Data.LootShape.Fragment:
                    Sphere(root, new Vector3(0f, 0.13f, 0f), new Vector3(0.2f, 0.26f, 0.2f), Clay);
                    Box(root, new Vector3(0.06f, 0.2f, 0.05f), new Vector3(0.14f, 0.16f, 0.14f), Clay, new Vector3(20f, 35f, 12f));
                    break;
                case Data.LootShape.Blade:
                    Box(root, new Vector3(0f, 0.02f, 0f), new Vector3(0.06f, 0.012f, 0.5f), Iron, new Vector3(0f, 25f, 0f));
                    Box(root, new Vector3(-0.08f, 0.02f, -0.18f), new Vector3(0.16f, 0.014f, 0.05f), Iron, new Vector3(0f, 25f, 0f));
                    break;
                case Data.LootShape.Reels:
                    Cylinder(root, new Vector3(-0.1f, 0.02f, 0f), new Vector3(0.18f, 0.015f, 0.18f), Tech);
                    Cylinder(root, new Vector3(0.1f, 0.02f, 0f), new Vector3(0.18f, 0.015f, 0.18f), Tech);
                    Cylinder(root, new Vector3(-0.1f, 0.036f, 0f), new Vector3(0.1f, 0.004f, 0.1f), Wood);
                    Cylinder(root, new Vector3(0.1f, 0.036f, 0f), new Vector3(0.06f, 0.004f, 0.06f), Wood);
                    break;
                case Data.LootShape.Ledger:
                    Box(root, new Vector3(0f, 0.03f, 0f), new Vector3(0.24f, 0.06f, 0.32f), new Color(0.3f, 0.12f, 0.1f));
                    Box(root, new Vector3(0.01f, 0.03f, 0f), new Vector3(0.23f, 0.045f, 0.3f), Paper);
                    break;
                case Data.LootShape.Plaque:
                    Box(root, new Vector3(0f, 0.2f, 0f), new Vector3(0.3f, 0.4f, 0.04f), Bronze, new Vector3(-12f, 0f, 0f));
                    Box(root, new Vector3(0f, 0.22f, 0.03f), new Vector3(0.1f, 0.22f, 0.03f), Brass, new Vector3(-12f, 0f, 0f));
                    Sphere(root, new Vector3(0f, 0.36f, 0.06f), Vector3.one * 0.08f, Brass);
                    break;
                case Data.LootShape.Pendant:
                    Cylinder(root, new Vector3(0f, 0.02f, 0f), new Vector3(0.14f, 0.01f, 0.14f), Gold);
                    Cylinder(root, new Vector3(0f, 0.035f, 0f), new Vector3(0.08f, 0.01f, 0.08f), Brass);
                    Sphere(root, new Vector3(0f, 0.05f, 0f), Vector3.one * 0.04f, Gold);
                    break;
                case Data.LootShape.Collar:
                    for (int i = 0; i < 14; i++)
                    {
                        float a = i / 14f * Mathf.PI * 2f;
                        Box(root, new Vector3(Mathf.Cos(a) * 0.15f, 0.02f, Mathf.Sin(a) * 0.15f), new Vector3(0.06f, 0.02f, 0.1f),
                            i % 4 == 0 ? new Color(0.85f, 0.15f, 0.15f) : i % 4 == 1 ? Paper : i % 4 == 2 ? new Color(0.1f, 0.35f, 0.75f) : Gold, new Vector3(0f, -a * Mathf.Rad2Deg, 0f));
                    }
                    break;
                case Data.LootShape.Manuscript:
                    Box(root, new Vector3(0f, 0.02f, 0f), new Vector3(0.3f, 0.03f, 0.22f), Paper, new Vector3(0f, 12f, 0f));
                    Box(root, new Vector3(0.02f, 0.045f, 0.01f), new Vector3(0.3f, 0.02f, 0.22f), Paper, new Vector3(0f, -6f, 0f));
                    Box(root, new Vector3(0f, 0.058f, 0f), new Vector3(0.2f, 0.004f, 0.12f), Indigo, new Vector3(0f, -6f, 0f));
                    break;
                case Data.LootShape.Harp:
                    Sphere(root, new Vector3(0f, 0.14f, 0f), new Vector3(0.28f, 0.26f, 0.2f), Wood);
                    Cylinder(root, new Vector3(0f, 0.5f, 0f), new Vector3(0.035f, 0.36f, 0.035f), Wood);
                    for (int i = 0; i < 5; i++) Box(root, new Vector3(0f, 0.42f - i * 0.03f, 0.03f + i * 0.02f), new Vector3(0.004f, 0.5f - i * 0.06f, 0.004f), Paper, new Vector3(18f, 0f, 0f));
                    break;
                case Data.LootShape.DataCore:
                    Box(root, new Vector3(0f, 0.12f, 0f), new Vector3(0.2f, 0.24f, 0.2f), Tech);
                    Box(root, new Vector3(0f, 0.12f, 0f), new Vector3(0.21f, 0.05f, 0.21f), glow, default, emissive: true);
                    Box(root, new Vector3(0f, 0.25f, 0f), new Vector3(0.1f, 0.03f, 0.1f), Iron);
                    break;
                case Data.LootShape.Leopard:
                    // Blocky and stylised on purpose: a crouching cat, not a copy of any real casting.
                    Box(root, new Vector3(0f, 0.2f, 0f), new Vector3(0.18f, 0.18f, 0.46f), Bronze);
                    Box(root, new Vector3(0f, 0.3f, 0.27f), new Vector3(0.17f, 0.16f, 0.17f), Bronze);
                    Box(root, new Vector3(-0.06f, 0.4f, 0.28f), new Vector3(0.04f, 0.05f, 0.04f), Bronze);
                    Box(root, new Vector3(0.06f, 0.4f, 0.28f), new Vector3(0.04f, 0.05f, 0.04f), Bronze);
                    foreach (float z in new[] { -0.17f, 0.17f })
                    {
                        Box(root, new Vector3(-0.07f, 0.06f, z), new Vector3(0.06f, 0.14f, 0.07f), Bronze);
                        Box(root, new Vector3(0.07f, 0.06f, z), new Vector3(0.06f, 0.14f, 0.07f), Bronze);
                    }
                    Box(root, new Vector3(0f, 0.24f, -0.3f), new Vector3(0.04f, 0.04f, 0.2f), Bronze, new Vector3(-35f, 0f, 0f));
                    Sphere(root, new Vector3(-0.045f, 0.32f, 0.355f), Vector3.one * 0.03f, Gold);
                    Sphere(root, new Vector3(0.045f, 0.32f, 0.355f), Vector3.one * 0.03f, Gold);
                    break;
                case Data.LootShape.Chest:
                    Box(root, new Vector3(0f, 0.12f, 0f), new Vector3(0.44f, 0.24f, 0.28f), Wood);
                    Cylinder(root, new Vector3(0f, 0.24f, 0f), new Vector3(0.28f, 0.22f, 0.28f), Wood, new Vector3(0f, 0f, 90f));
                    foreach (float x in new[] { -0.15f, 0.15f }) Box(root, new Vector3(x, 0.14f, 0f), new Vector3(0.04f, 0.3f, 0.3f), Iron);
                    Box(root, new Vector3(0f, 0.2f, 0.145f), new Vector3(0.06f, 0.07f, 0.02f), Brass);
                    break;
                case Data.LootShape.Sword:
                    Box(root, new Vector3(0f, 0.03f, 0.1f), new Vector3(0.07f, 0.014f, 0.62f), new Color(0.75f, 0.76f, 0.8f), new Vector3(0f, -20f, 0f));
                    Box(root, new Vector3(0.07f, 0.03f, -0.22f), new Vector3(0.22f, 0.03f, 0.04f), Gold, new Vector3(0f, -20f, 0f));
                    Cylinder(root, new Vector3(0.11f, 0.03f, -0.31f), new Vector3(0.045f, 0.09f, 0.045f), Gold, new Vector3(90f, -20f, 0f));
                    Sphere(root, new Vector3(0.14f, 0.03f, -0.4f), Vector3.one * 0.08f, Gold);
                    break;
                case Data.LootShape.Astrolabe:
                    Cylinder(root, new Vector3(0f, 0.16f, 0f), new Vector3(0.28f, 0.012f, 0.28f), Brass, new Vector3(78f, 0f, 0f));
                    Cylinder(root, new Vector3(0f, 0.16f, 0.012f), new Vector3(0.2f, 0.006f, 0.2f), Gold, new Vector3(78f, 0f, 0f));
                    Box(root, new Vector3(0f, 0.16f, 0.022f), new Vector3(0.02f, 0.24f, 0.008f), Iron, new Vector3(-12f, 0f, 35f));
                    Cylinder(root, new Vector3(0f, 0.31f, -0.03f), new Vector3(0.05f, 0.008f, 0.05f), Brass, new Vector3(78f, 0f, 0f));
                    break;
                default: // Purse and anything new
                    Sphere(root, new Vector3(0f, 0.09f, 0f), new Vector3(0.2f, 0.18f, 0.18f), new Color(0.5f, 0.32f, 0.2f));
                    Cylinder(root, new Vector3(0f, 0.19f, 0f), new Vector3(0.07f, 0.02f, 0.07f), Wood);
                    for (int i = 0; i < 5; i++) Sphere(root, new Vector3(-0.08f + i * 0.04f, 0.1f, 0.09f), new Vector3(0.035f, 0.045f, 0.02f), Paper);
                    break;
            }

            // A lit ring underneath in the tier colour: what makes it readable from across a room.
            Cylinder(root, new Vector3(0f, 0.005f, 0f), new Vector3(0.62f, 0.004f, 0.62f), glow, default, emissive: true);
            return root;
        }

        private static void Box(Transform parent, Vector3 pos, Vector3 size, Color color, Vector3 euler = default, bool emissive = false)
            => Part(PrimitiveType.Cube, parent, pos, size, color, euler, emissive);

        private static void Sphere(Transform parent, Vector3 pos, Vector3 size, Color color)
            => Part(PrimitiveType.Sphere, parent, pos, size, color, default, false);

        private static void Cylinder(Transform parent, Vector3 pos, Vector3 size, Color color, Vector3 euler = default, bool emissive = false)
            => Part(PrimitiveType.Cylinder, parent, pos, size, color, euler, emissive);

        private static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 size, Color color, Vector3 euler, bool emissive)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = type.ToString();
            var collider = go.GetComponent<Collider>();
            if (collider != null) Object.Destroy(collider);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            go.GetComponent<Renderer>().sharedMaterial = emissive ? ArtMaterials.Emissive(color) : ArtMaterials.Colour(color);
        }
    }

    /// <summary>Slow turn and bob for things lying on the floor waiting to be picked up.</summary>
    public class PickupSpin : MonoBehaviour
    {
        public float degreesPerSecond = 50f;
        public float bobHeight = 0.06f;
        private Vector3 rest;
        private float phase;

        private void Start()
        {
            rest = transform.localPosition;
            phase = (transform.position.x + transform.position.z) * 1.7f; // neighbours do not bob in step
        }

        private void Update()
        {
            transform.Rotate(0f, degreesPerSecond * Time.deltaTime, 0f, Space.Self);
            transform.localPosition = rest + Vector3.up * (0.12f + Mathf.Sin(Time.time * 2f + phase) * bobHeight);
        }
    }
}
