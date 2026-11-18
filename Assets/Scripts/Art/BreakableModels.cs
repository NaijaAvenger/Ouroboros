using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Code-built models for breakable objects (v0.14) and what they look like when they break. Sizes here are
    /// the source of truth: the scene builder sizes each object's collider with <see cref="SizeOf"/>.
    /// Models stand on local y = 0 and have no colliders of their own.
    /// </summary>
    public static class BreakableModels
    {
        private static readonly Color Wood = new Color(0.47f, 0.34f, 0.2f);
        private static readonly Color WoodDark = new Color(0.3f, 0.21f, 0.13f);
        private static readonly Color Steel = new Color(0.36f, 0.38f, 0.42f);
        private static readonly Color Dark = new Color(0.13f, 0.14f, 0.16f);
        private static readonly Color Gold = new Color(0.92f, 0.72f, 0.22f);
        private static readonly Color GlassColour = new Color(0.6f, 0.85f, 0.95f, 0.3f);

        /// <summary>Footprint and height of each kind, in metres. Weak walls use their own size.</summary>
        public static Vector3 SizeOf(Interaction.BreakableKind kind, Vector3 wallSize)
        {
            switch (kind)
            {
                case Interaction.BreakableKind.DisplayCase:    return new Vector3(1.2f, 2.1f, 1.2f);
                case Interaction.BreakableKind.VendingMachine: return new Vector3(1.1f, 2f, 0.9f);
                case Interaction.BreakableKind.Atm:            return new Vector3(0.9f, 1.7f, 0.7f);
                case Interaction.BreakableKind.CashRegister:   return new Vector3(0.6f, 1.35f, 0.6f);
                case Interaction.BreakableKind.Locker:         return new Vector3(1.2f, 2f, 0.6f);
                case Interaction.BreakableKind.StrongBox:      return new Vector3(1.1f, 0.9f, 0.9f);
                case Interaction.BreakableKind.Safe:           return new Vector3(0.9f, 1.2f, 0.9f);
                case Interaction.BreakableKind.AirdropCrate:   return new Vector3(1.6f, 1.3f, 1.6f);
                case Interaction.BreakableKind.WeakWall:       return wallSize;
                default:                                       return new Vector3(1.1f, 1.1f, 1.1f);
            }
        }

        public static Transform Build(Transform parent, Interaction.BreakableKind kind, Vector3 wallSize, Data.LootTier tier)
        {
            var root = Prim.Group(parent, "Model");
            // The scene object may be scaled to fit its collider; the model is built in metres.
            Vector3 s = parent.lossyScale;
            root.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));
            Color glow = Data.LootCatalog.TierColor(tier);

            switch (kind)
            {
                case Interaction.BreakableKind.DisplayCase:
                    Prim.Box(root, "Base", new Vector3(0f, 0.5f, 0f), new Vector3(1.2f, 1f, 1.2f), ArtMaterials.Colour(Dark));
                    Prim.Box(root, "Trim", new Vector3(0f, 1.02f, 0f), new Vector3(1.26f, 0.05f, 1.26f), ArtMaterials.Colour(Gold));
                    Prim.Box(root, "Glass", new Vector3(0f, 1.57f, 0f), new Vector3(1.1f, 1.05f, 1.1f), ArtMaterials.Translucent(GlassColour));
                    Prim.Box(root, "Cap", new Vector3(0f, 2.1f, 0f), new Vector3(1.16f, 0.05f, 1.16f), ArtMaterials.Colour(Dark));
                    Prim.Box(root, "Cushion", new Vector3(0f, 1.1f, 0f), new Vector3(0.7f, 0.1f, 0.7f), ArtMaterials.Colour(new Color(0.35f, 0.08f, 0.12f)));
                    Prim.Ball(root, "Piece", new Vector3(0f, 1.32f, 0f), new Vector3(0.28f, 0.28f, 0.28f), ArtMaterials.Emissive(glow));
                    break;

                case Interaction.BreakableKind.VendingMachine:
                    Prim.Box(root, "Body", new Vector3(0f, 1f, 0f), new Vector3(1.1f, 2f, 0.9f), ArtMaterials.Colour(new Color(0.7f, 0.15f, 0.15f)));
                    Prim.Box(root, "Window", new Vector3(-0.15f, 1.25f, 0.455f), new Vector3(0.7f, 1.2f, 0.02f), ArtMaterials.Emissive(new Color(0.75f, 0.9f, 1f)));
                    Prim.Box(root, "Panel", new Vector3(0.4f, 1.2f, 0.455f), new Vector3(0.2f, 0.5f, 0.02f), ArtMaterials.Colour(Dark));
                    Prim.Box(root, "Slot", new Vector3(-0.15f, 0.3f, 0.455f), new Vector3(0.7f, 0.25f, 0.02f), ArtMaterials.Colour(Dark));
                    for (int i = 0; i < 4; i++)
                    {
                        Prim.Box(root, "Row", new Vector3(-0.15f, 0.82f + i * 0.28f, 0.44f), new Vector3(0.62f, 0.14f, 0.02f),
                            ArtMaterials.Colour(i % 2 == 0 ? new Color(0.95f, 0.8f, 0.2f) : new Color(0.2f, 0.7f, 0.4f)));
                    }
                    break;

                case Interaction.BreakableKind.Atm:
                    Prim.Box(root, "Body", new Vector3(0f, 0.85f, 0f), new Vector3(0.9f, 1.7f, 0.7f), ArtMaterials.Colour(new Color(0.2f, 0.3f, 0.45f)));
                    Prim.Box(root, "Screen", new Vector3(0f, 1.25f, 0.355f), new Vector3(0.5f, 0.36f, 0.02f), ArtMaterials.Emissive(new Color(0.4f, 0.9f, 1f)));
                    Prim.Box(root, "Keys", new Vector3(0f, 0.92f, 0.37f), new Vector3(0.4f, 0.05f, 0.2f), ArtMaterials.Colour(Dark), new Vector3(-25f, 0f, 0f));
                    Prim.Box(root, "Slot", new Vector3(0f, 0.7f, 0.355f), new Vector3(0.36f, 0.05f, 0.02f), ArtMaterials.Colour(Dark));
                    Prim.Box(root, "Sign", new Vector3(0f, 1.62f, 0.355f), new Vector3(0.7f, 0.12f, 0.02f), ArtMaterials.Emissive(Gold));
                    break;

                case Interaction.BreakableKind.CashRegister:
                    Prim.Box(root, "Stand", new Vector3(0f, 0.5f, 0f), new Vector3(0.6f, 1f, 0.6f), ArtMaterials.Colour(WoodDark));
                    Prim.Box(root, "Till", new Vector3(0f, 1.1f, 0f), new Vector3(0.46f, 0.2f, 0.42f), ArtMaterials.Colour(Dark));
                    Prim.Box(root, "Display", new Vector3(0f, 1.28f, -0.1f), new Vector3(0.3f, 0.16f, 0.04f), ArtMaterials.Emissive(new Color(0.3f, 1f, 0.5f)), new Vector3(-20f, 0f, 0f));
                    break;

                case Interaction.BreakableKind.Locker:
                    for (int i = 0; i < 3; i++)
                    {
                        float x = (i - 1) * 0.4f;
                        Prim.Box(root, "Door", new Vector3(x, 1f, 0f), new Vector3(0.38f, 2f, 0.6f), ArtMaterials.Colour(i == 1 ? new Color(0.25f, 0.4f, 0.5f) : new Color(0.3f, 0.45f, 0.55f)));
                        Prim.Box(root, "Vent", new Vector3(x, 1.7f, 0.305f), new Vector3(0.24f, 0.12f, 0.01f), ArtMaterials.Colour(Dark));
                        Prim.Box(root, "Handle", new Vector3(x + 0.12f, 1.05f, 0.31f), new Vector3(0.03f, 0.14f, 0.02f), ArtMaterials.Colour(Steel));
                    }
                    break;

                case Interaction.BreakableKind.StrongBox:
                    Prim.Box(root, "Box", new Vector3(0f, 0.45f, 0f), new Vector3(1.1f, 0.9f, 0.9f), ArtMaterials.Colour(Steel));
                    Prim.Box(root, "Band", new Vector3(-0.3f, 0.45f, 0f), new Vector3(0.1f, 0.94f, 0.94f), ArtMaterials.Colour(Dark));
                    Prim.Box(root, "Band", new Vector3(0.3f, 0.45f, 0f), new Vector3(0.1f, 0.94f, 0.94f), ArtMaterials.Colour(Dark));
                    Prim.Box(root, "Lock", new Vector3(0f, 0.55f, 0.46f), new Vector3(0.2f, 0.2f, 0.04f), ArtMaterials.Emissive(glow));
                    Prim.Box(root, "Hazard", new Vector3(0f, 0.2f, 0.455f), new Vector3(0.9f, 0.1f, 0.01f), ArtMaterials.Colour(new Color(0.95f, 0.78f, 0.1f)));
                    break;

                case Interaction.BreakableKind.Safe:
                    Prim.Box(root, "Body", new Vector3(0f, 0.6f, 0f), new Vector3(0.9f, 1.2f, 0.9f), ArtMaterials.Colour(new Color(0.2f, 0.22f, 0.25f)));
                    Prim.Box(root, "Door", new Vector3(0f, 0.6f, 0.455f), new Vector3(0.74f, 1.02f, 0.03f), ArtMaterials.Colour(Steel));
                    Prim.Rod(root, "Dial", new Vector3(0f, 0.7f, 0.49f), new Vector3(0.22f, 0.02f, 0.22f), ArtMaterials.Colour(Gold), new Vector3(90f, 0f, 0f));
                    Prim.Box(root, "Handle", new Vector3(0.22f, 0.5f, 0.49f), new Vector3(0.05f, 0.24f, 0.04f), ArtMaterials.Colour(Dark));
                    Prim.Box(root, "Light", new Vector3(-0.28f, 1.05f, 0.475f), new Vector3(0.06f, 0.06f, 0.02f), ArtMaterials.Emissive(glow));
                    break;

                case Interaction.BreakableKind.AirdropCrate:
                    Prim.Box(root, "Crate", new Vector3(0f, 0.6f, 0f), new Vector3(1.6f, 1.2f, 1.6f), ArtMaterials.Colour(new Color(0.2f, 0.28f, 0.2f)));
                    Prim.Box(root, "Lid", new Vector3(0f, 1.24f, 0f), new Vector3(1.7f, 0.1f, 1.7f), ArtMaterials.Colour(Dark));
                    Prim.Box(root, "Strap", new Vector3(0f, 0.6f, 0f), new Vector3(1.64f, 0.18f, 1.64f), ArtMaterials.Colour(new Color(0.95f, 0.78f, 0.1f)));
                    Prim.Rod(root, "Beacon Post", new Vector3(0.6f, 1.7f, 0.6f), new Vector3(0.05f, 0.42f, 0.05f), ArtMaterials.Colour(Steel));
                    Prim.Ball(root, "Beacon", new Vector3(0.6f, 2.2f, 0.6f), new Vector3(0.22f, 0.22f, 0.22f), ArtMaterials.Emissive(glow));
                    // A column of light so the delivery can be found from across the hall.
                    Prim.Rod(root, "Light Column", new Vector3(0f, 7f, 0f), new Vector3(0.5f, 6f, 0.5f), ArtMaterials.Translucent(new Color(glow.r, glow.g, glow.b, 0.16f)));
                    break;

                case Interaction.BreakableKind.WeakWall:
                {
                    // Patched brickwork with a crack and warning tape: it has to read as "this one comes down".
                    Vector3 w = wallSize;
                    Prim.Box(root, "Panel", new Vector3(0f, w.y * 0.5f, 0f), w, ArtMaterials.Colour(new Color(0.55f, 0.36f, 0.3f)));
                    for (int i = 0; i < 5; i++)
                    {
                        float y = w.y * (0.12f + i * 0.19f);
                        Prim.Box(root, "Course", new Vector3(0f, y, 0f), new Vector3(w.x * 1.005f, 0.04f, w.z * 1.03f), ArtMaterials.Colour(new Color(0.4f, 0.26f, 0.22f)));
                    }
                    Prim.Box(root, "Crack", new Vector3(w.x * 0.1f, w.y * 0.55f, 0f), new Vector3(0.07f, w.y * 0.7f, w.z * 1.05f), ArtMaterials.Colour(Dark), new Vector3(0f, 0f, 16f));
                    Prim.Box(root, "Crack", new Vector3(-w.x * 0.14f, w.y * 0.4f, 0f), new Vector3(0.05f, w.y * 0.4f, w.z * 1.05f), ArtMaterials.Colour(Dark), new Vector3(0f, 0f, -28f));
                    Prim.Box(root, "Tape", new Vector3(0f, 1.2f, 0f), new Vector3(w.x * 0.9f, 0.1f, w.z * 1.06f), ArtMaterials.Colour(new Color(0.95f, 0.78f, 0.1f)), new Vector3(0f, 0f, 6f));
                    break;
                }
                default: // Crate
                    Prim.Box(root, "Crate", new Vector3(0f, 0.55f, 0f), new Vector3(1.1f, 1.1f, 1.1f), ArtMaterials.Colour(Wood));
                    Prim.Box(root, "Batten", new Vector3(0f, 1f, 0f), new Vector3(1.14f, 0.1f, 1.14f), ArtMaterials.Colour(WoodDark));
                    Prim.Box(root, "Batten", new Vector3(0f, 0.1f, 0f), new Vector3(1.14f, 0.1f, 1.14f), ArtMaterials.Colour(WoodDark));
                    Prim.Box(root, "Mark", new Vector3(0f, 0.55f, 0.555f), new Vector3(0.4f, 0.2f, 0.01f), ArtMaterials.Emissive(glow));
                    break;
            }
            return root;
        }

        /// <summary>Shards, splinters or rubble, by what broke.</summary>
        public static void BreakEffect(Vector3 floor, Interaction.BreakableKind kind, Vector3 wallSize)
        {
            Vector3 size = SizeOf(kind, wallSize);
            Vector3 centre = floor + Vector3.up * (size.y * 0.5f);
            float reach = Mathf.Max(size.x, size.z);

            switch (kind)
            {
                case Interaction.BreakableKind.DisplayCase:
                    Fx.Burst(centre + Vector3.up * 0.5f, new Color(0.8f, 0.95f, 1f, 0.9f), 40, 5f, 0.09f, 0.7f, 1.4f);
                    break;
                case Interaction.BreakableKind.VendingMachine:
                case Interaction.BreakableKind.Atm:
                case Interaction.BreakableKind.CashRegister:
                    Fx.Burst(centre, new Color(0.4f, 0.8f, 1f, 1f), 26, 4f, 0.08f, 0.5f, 0.6f);
                    Fx.Burst(centre, new Color(0.3f, 0.32f, 0.36f, 1f), 18, 3.5f, 0.14f, 0.7f, 1.5f);
                    Fx.Flash(centre, new Color(0.5f, 0.8f, 1f), 6f, 3f, 0.2f);
                    break;
                case Interaction.BreakableKind.StrongBox:
                case Interaction.BreakableKind.Safe:
                case Interaction.BreakableKind.Locker:
                    Fx.Burst(centre, new Color(1f, 0.8f, 0.4f, 1f), 26, 5f, 0.07f, 0.45f, 1.2f);
                    Fx.Burst(centre, new Color(0.35f, 0.37f, 0.4f, 1f), 16, 3f, 0.16f, 0.8f, 1.6f);
                    break;
                case Interaction.BreakableKind.WeakWall:
                    Fx.Burst(centre, new Color(0.55f, 0.36f, 0.3f, 1f), 60, 6f, 0.3f, 1f, 1.6f);
                    Fx.Smoke(floor + Vector3.up * 0.5f, reach * 0.6f, 2.2f, new Color(0.6f, 0.52f, 0.46f));
                    Fx.Flash(centre, new Color(1f, 0.7f, 0.4f), reach * 3f, 4f, 0.25f);
                    break;
                default:
                    Fx.Burst(centre, new Color(0.5f, 0.36f, 0.22f, 1f), 34, 4.5f, 0.16f, 0.8f, 1.6f);
                    Fx.Smoke(floor + Vector3.up * 0.3f, reach * 0.5f, 1.2f, new Color(0.55f, 0.48f, 0.4f));
                    break;
            }
            AudioSource.PlayClipAtPoint(Core.ProceduralSfx.Explosion, centre, kind == Interaction.BreakableKind.WeakWall ? 0.9f : 0.35f);
        }

        /// <summary>A round bouncing off armour: grey sparks, so the shooter knows this needs something heavier.</summary>
        public static void DeflectEffect(Vector3 point)
        {
            Fx.Burst(point, new Color(0.85f, 0.87f, 0.9f, 1f), 6, 3f, 0.05f, 0.2f, 0.8f);
        }
    }
}
