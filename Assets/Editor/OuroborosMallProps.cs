using UnityEngine;

namespace Ouroboros.EditorTools
{
    /// <summary>
    /// Interior pieces for the Galleria (v0.14): shop counters, tables, kiosks, clothes rails, mannequins, sofas,
    /// desks, lockers, metal detectors, arcade cabinets, treadmills, rows of seats, an armoured truck and show
    /// plinths. Modelled in code from primitives, like the rest of the prop kit (OuroborosPropKit.cs).
    /// Each takes a floor position and a yaw; the floor height comes from <see cref="s_propY"/>.
    /// </summary>
    public static partial class OuroborosDevSceneBuilder
    {
        private static readonly Color Marble = new Color(0.8f, 0.78f, 0.74f);
        private static readonly Color Fabric = new Color(0.25f, 0.3f, 0.45f);
        private static readonly Color Screen = new Color(0.35f, 0.85f, 1f);

        private static void ShopCounter(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Counter", x, z, yaw);
            Hard(r, "Counter Body", new Vector3(0f, 0.5f, 0f), new Vector3(4f, 1f, 0.8f), WoodDark);
            Soft(r, "Counter Top", new Vector3(0f, 1.03f, 0f), new Vector3(4.1f, 0.06f, 0.9f), Marble);
            Soft(r, "Kick", new Vector3(0f, 0.08f, 0.41f), new Vector3(4f, 0.16f, 0.02f), MetalDark);
            Lit(r, "Strip", new Vector3(0f, 0.94f, 0.41f), new Vector3(3.9f, 0.04f, 0.02f), Gold);
        }

        private static void FoodTable(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Table", x, z, yaw);
            Drum(r, "Top", new Vector3(0f, 0.76f, 0f), 0.7f, 0.06f, Wood, true);
            Drum(r, "Leg", new Vector3(0f, 0.38f, 0f), 0.07f, 0.74f, MetalDark, false);
            Drum(r, "Foot", new Vector3(0f, 0.03f, 0f), 0.32f, 0.05f, MetalDark, false);
            for (int i = 0; i < 4; i++)
            {
                float a = (i * 90f + 45f) * Mathf.Deg2Rad;
                var at = new Vector3(Mathf.Cos(a) * 0.9f, 0f, Mathf.Sin(a) * 0.9f);
                Drum(r, "Stool", at + Vector3.up * 0.24f, 0.2f, 0.48f, i % 2 == 0 ? Fabric : new Color(0.55f, 0.2f, 0.15f), true);
            }
        }

        private static void Kiosk(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Kiosk", x, z, yaw);
            Hard(r, "Booth", new Vector3(0f, 0.55f, 0f), new Vector3(3f, 1.1f, 3f), new Color(0.2f, 0.36f, 0.42f));
            Soft(r, "Top", new Vector3(0f, 1.13f, 0f), new Vector3(3.1f, 0.06f, 3.1f), Marble);
            foreach (float sx in new[] { -1.4f, 1.4f })
            {
                foreach (float sz in new[] { -1.4f, 1.4f }) Soft(r, "Post", new Vector3(sx, 1.9f, sz), new Vector3(0.08f, 1.5f, 0.08f), MetalDark);
            }
            Soft(r, "Canopy", new Vector3(0f, 2.7f, 0f), new Vector3(3.4f, 0.1f, 3.4f), new Color(0.75f, 0.3f, 0.15f));
            Lit(r, "Sign", new Vector3(0f, 2.45f, 1.72f), new Vector3(2.4f, 0.3f, 0.04f), Gold);
            Lit(r, "Screen", new Vector3(0.8f, 1.4f, 0f), new Vector3(0.5f, 0.4f, 0.05f), Screen);
        }

        private static void ClothesRack(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Clothes Rail", x, z, yaw);
            Hard(r, "Base", new Vector3(0f, 0.05f, 0f), new Vector3(2.4f, 0.1f, 0.7f), MetalDark);
            foreach (float sx in new[] { -1.1f, 1.1f }) Soft(r, "Upright", new Vector3(sx, 0.85f, 0f), new Vector3(0.05f, 1.6f, 0.05f), Metal);
            Soft(r, "Rail", new Vector3(0f, 1.62f, 0f), new Vector3(2.3f, 0.05f, 0.05f), Metal);
            var cloth = new[] { new Color(0.8f, 0.3f, 0.2f), new Color(0.2f, 0.45f, 0.6f), new Color(0.85f, 0.7f, 0.2f), new Color(0.25f, 0.5f, 0.3f), new Color(0.5f, 0.25f, 0.5f) };
            for (int i = 0; i < 9; i++)
            {
                // Garments hang as solid blocks: thin cover, but cover.
                Hard(r, "Garment", new Vector3(-0.96f + i * 0.24f, 1.1f, 0f), new Vector3(0.18f, 0.95f, 0.5f), cloth[Mathf.Abs(i + Mathf.RoundToInt(x + z)) % cloth.Length]);
            }
        }

        private static void Mannequin(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Mannequin", x, z, yaw);
            Color skin = new Color(0.85f, 0.83f, 0.8f);
            Drum(r, "Stand", new Vector3(0f, 0.03f, 0f), 0.3f, 0.06f, MetalDark, false);
            Hard(r, "Legs", new Vector3(0f, 0.5f, 0f), new Vector3(0.3f, 0.9f, 0.2f), Fabric);
            Hard(r, "Torso", new Vector3(0f, 1.25f, 0f), new Vector3(0.42f, 0.6f, 0.24f), new Color(0.75f, 0.3f, 0.2f));
            Blob(r, "Head", new Vector3(0f, 1.72f, 0f), new Vector3(0.22f, 0.26f, 0.22f), skin);
            Soft(r, "Arm", new Vector3(0.27f, 1.2f, 0f), new Vector3(0.09f, 0.6f, 0.09f), skin, new Vector3(0f, 0f, 8f));
            Soft(r, "Arm", new Vector3(-0.27f, 1.2f, 0f), new Vector3(0.09f, 0.6f, 0.09f), skin, new Vector3(0f, 0f, -8f));
        }

        private static void Sofa(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Sofa", x, z, yaw);
            Hard(r, "Seat", new Vector3(0f, 0.25f, 0.05f), new Vector3(2.6f, 0.5f, 0.9f), Fabric);
            Hard(r, "Back", new Vector3(0f, 0.6f, -0.4f), new Vector3(2.6f, 0.7f, 0.2f), Fabric);
            Soft(r, "Arm", new Vector3(-1.2f, 0.45f, 0.05f), new Vector3(0.2f, 0.5f, 0.9f), Color.Lerp(Fabric, Color.black, 0.2f));
            Soft(r, "Arm", new Vector3(1.2f, 0.45f, 0.05f), new Vector3(0.2f, 0.5f, 0.9f), Color.Lerp(Fabric, Color.black, 0.2f));
            Soft(r, "Cushion", new Vector3(-0.6f, 0.54f, 0.05f), new Vector3(1.1f, 0.1f, 0.8f), Color.Lerp(Fabric, Color.white, 0.15f));
            Soft(r, "Cushion", new Vector3(0.6f, 0.54f, 0.05f), new Vector3(1.1f, 0.1f, 0.8f), Color.Lerp(Fabric, Color.white, 0.15f));
        }

        private static void Desk(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Desk", x, z, yaw);
            Hard(r, "Top", new Vector3(0f, 0.74f, 0f), new Vector3(2.4f, 0.06f, 1.2f), Wood);
            Hard(r, "Pedestal", new Vector3(-0.9f, 0.36f, 0f), new Vector3(0.5f, 0.72f, 1.1f), WoodDark);
            Hard(r, "Pedestal", new Vector3(0.9f, 0.36f, 0f), new Vector3(0.5f, 0.72f, 1.1f), WoodDark);
            Hard(r, "Modesty Panel", new Vector3(0f, 0.4f, -0.5f), new Vector3(1.3f, 0.6f, 0.05f), WoodDark);
            Lit(r, "Monitor", new Vector3(-0.35f, 1.1f, -0.2f), new Vector3(0.6f, 0.36f, 0.03f), Screen);
            Lit(r, "Monitor", new Vector3(0.35f, 1.1f, -0.2f), new Vector3(0.6f, 0.36f, 0.03f), new Color(0.3f, 1f, 0.6f));
            Soft(r, "Stand", new Vector3(0f, 0.86f, -0.2f), new Vector3(0.9f, 0.18f, 0.06f), MetalDark);
            Soft(r, "Keyboard", new Vector3(0f, 0.78f, 0.2f), new Vector3(0.5f, 0.02f, 0.16f), MetalDark);
            Hard(r, "Chair", new Vector3(0f, 0.45f, 1f), new Vector3(0.5f, 0.9f, 0.5f), MetalDark);
        }

        private static void LockerRow(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Lockers", x, z, yaw);
            for (int i = 0; i < 6; i++)
            {
                float px = -1.5f + i * 0.6f;
                Hard(r, "Locker", new Vector3(px, 1f, 0f), new Vector3(0.58f, 2f, 0.6f), i % 2 == 0 ? new Color(0.35f, 0.4f, 0.45f) : new Color(0.3f, 0.35f, 0.4f));
                Soft(r, "Vent", new Vector3(px, 1.7f, 0.305f), new Vector3(0.36f, 0.12f, 0.01f), MetalDark);
                Soft(r, "Handle", new Vector3(px + 0.18f, 1.05f, 0.31f), new Vector3(0.03f, 0.14f, 0.02f), Metal);
            }
        }

        /// <summary>A walk-through metal detector. The posts are cover for a head; the arch is not.</summary>
        private static void MetalDetector(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Metal Detector", x, z, yaw);
            Hard(r, "Post", new Vector3(-0.7f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 0.4f), Marble);
            Hard(r, "Post", new Vector3(0.7f, 1.1f, 0f), new Vector3(0.2f, 2.2f, 0.4f), Marble);
            Soft(r, "Arch", new Vector3(0f, 2.25f, 0f), new Vector3(1.6f, 0.12f, 0.4f), Marble);
            Lit(r, "Light", new Vector3(0f, 2.25f, 0.21f), new Vector3(0.3f, 0.06f, 0.02f), new Color(0.3f, 1f, 0.5f));
        }

        private static void ArcadeCabinet(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Arcade Cabinet", x, z, yaw);
            Color shell = Vary(x, z) > 0.5f ? new Color(0.5f, 0.15f, 0.55f) : new Color(0.15f, 0.3f, 0.6f);
            Hard(r, "Cabinet", new Vector3(0f, 0.95f, 0f), new Vector3(0.8f, 1.9f, 0.9f), shell);
            Lit(r, "Screen", new Vector3(0f, 1.35f, -0.455f), new Vector3(0.6f, 0.45f, 0.02f), Vary(z, x) > 0.5f ? Screen : new Color(1f, 0.5f, 0.9f));
            Soft(r, "Deck", new Vector3(0f, 0.95f, -0.55f), new Vector3(0.74f, 0.08f, 0.3f), MetalDark, new Vector3(18f, 0f, 0f));
            Lit(r, "Marquee", new Vector3(0f, 1.78f, -0.455f), new Vector3(0.7f, 0.18f, 0.02f), Gold);
        }

        private static void Treadmill(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Treadmill", x, z, yaw);
            Hard(r, "Deck", new Vector3(0f, 0.15f, 0f), new Vector3(0.9f, 0.3f, 2f), MetalDark);
            Soft(r, "Belt", new Vector3(0f, 0.31f, 0.1f), new Vector3(0.7f, 0.02f, 1.6f), new Color(0.08f, 0.08f, 0.09f));
            Hard(r, "Console Post", new Vector3(0f, 0.8f, -0.85f), new Vector3(0.8f, 1.2f, 0.12f), Metal);
            Lit(r, "Console", new Vector3(0f, 1.35f, -0.78f), new Vector3(0.5f, 0.3f, 0.03f), Screen);
            Soft(r, "Rail", new Vector3(-0.4f, 1f, -0.3f), new Vector3(0.05f, 0.05f, 1.1f), Metal);
            Soft(r, "Rail", new Vector3(0.4f, 1f, -0.3f), new Vector3(0.05f, 0.05f, 1.1f), Metal);
        }

        private static void SeatRow(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Seats", x, z, yaw);
            for (int i = 0; i < 4; i++)
            {
                float px = -1.5f + i * 1f;
                Hard(r, "Seat", new Vector3(px, 0.25f, 0.1f), new Vector3(0.9f, 0.5f, 0.7f), new Color(0.5f, 0.12f, 0.15f));
                Hard(r, "Back", new Vector3(px, 0.75f, -0.32f), new Vector3(0.9f, 0.9f, 0.14f), new Color(0.45f, 0.1f, 0.13f));
            }
            Soft(r, "Rail", new Vector3(0f, 0.04f, 0f), new Vector3(4f, 0.08f, 0.9f), MetalDark);
        }

        /// <summary>The armoured truck the delivery event arrives in: tall, plated and solid cover from end to end.</summary>
        private static void ArmouredTruck(Transform p, float x, float z, float yaw)
        {
            var r = Prop(p, "Armoured Truck", x, z, yaw);
            Color plate = new Color(0.22f, 0.26f, 0.3f);
            Hard(r, "Box", new Vector3(0f, 1.75f, -0.8f), new Vector3(2.6f, 2.5f, 4.8f), plate);
            Hard(r, "Cab", new Vector3(0f, 1.35f, 2.5f), new Vector3(2.6f, 1.7f, 1.8f), plate);
            Soft(r, "Windscreen", new Vector3(0f, 1.85f, 3.36f), new Vector3(2.2f, 0.55f, 0.06f), MetalDark);
            Soft(r, "Bumper", new Vector3(0f, 0.6f, 3.46f), new Vector3(2.7f, 0.35f, 0.14f), MetalDark);
            Soft(r, "Stripe", new Vector3(0f, 1.2f, -0.8f), new Vector3(2.63f, 0.3f, 4.82f), Hazard);
            Soft(r, "Rear Doors", new Vector3(0f, 1.7f, -3.21f), new Vector3(2.2f, 2.2f, 0.03f), Color.Lerp(plate, Color.black, 0.3f));
            Soft(r, "Door Bar", new Vector3(0f, 1.7f, -3.24f), new Vector3(0.08f, 2f, 0.04f), Metal);
            Lit(r, "Door Lock", new Vector3(0.5f, 1.5f, -3.24f), new Vector3(0.14f, 0.14f, 0.03f), new Color(1f, 0.6f, 0.1f));
            foreach (float sx in new[] { -1f, 1f })
            {
                foreach (float sz in new[] { -2.2f, -0.9f, 2.3f }) Drum(r, "Wheel", new Vector3(sx * 1.28f, 0.5f, sz), 0.5f, 0.3f, MetalDark, false, new Vector3(0f, 0f, 90f));
                Lit(r, "Headlight", new Vector3(sx * 0.95f, 1f, 3.46f), new Vector3(0.36f, 0.2f, 0.03f), new Color(1f, 0.95f, 0.8f));
                Soft(r, "Gun Port", new Vector3(sx * 1.31f, 2f, -0.8f), new Vector3(0.02f, 0.2f, 0.5f), MetalDark);
            }
            Lit(r, "Beacon", new Vector3(0f, 3.08f, 2.4f), new Vector3(0.4f, 0.14f, 0.24f), new Color(1f, 0.6f, 0.1f));
        }

        /// <summary>A plinth with a slowly turning show piece on it. Solid, waist high.</summary>
        private static void ShowPlinth(Transform p, float x, float z, float yaw, string shapeName)
        {
            var r = Prop(p, "Plinth (" + shapeName + ")", x, z, yaw);
            Hard(r, "Plinth", new Vector3(0f, 0.5f, 0f), new Vector3(1.1f, 1f, 1.1f), DarkStone);
            Soft(r, "Trim", new Vector3(0f, 1.03f, 0f), new Vector3(1.2f, 0.06f, 1.2f), Gold);
            var piece = new GameObject("Show Piece (" + shapeName + ")");
            piece.transform.SetParent(r, false);
            piece.transform.localPosition = new Vector3(0f, 1.07f, 0f);
            var display = piece.AddComponent<Art.DisplayPiece>();
            if (System.Enum.TryParse(shapeName, out Data.LootShape shape)) display.shape = shape;
            display.tier = Data.LootTier.Legendary;
            PropLight(r, new Vector3(0f, 3.2f, 0f), WarmLight, 6f, 2f);
        }

        /// <summary>A lit ceiling panel: gives a room its light and tells you which district you are in by its colour.</summary>
        private static void CeilingLight(Transform p, Vector3 position, Color color, float range, float intensity)
        {
            var go = new GameObject("Ceiling Light");
            go.transform.SetParent(p);
            go.transform.position = position;
            Lit(go.transform, "Panel", new Vector3(0f, 0.18f, 0f), new Vector3(1.6f, 0.06f, 1.6f), Color.Lerp(color, Color.white, 0.5f));
            PropLight(go.transform, Vector3.zero, color, range, intensity);
        }
    }
}
