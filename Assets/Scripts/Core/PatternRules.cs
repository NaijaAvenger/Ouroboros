using System;

namespace Ouroboros.Core
{
    /// <summary>
    /// Crew pattern generators (v0.12, milestone M4). Each crew wears a repeating geometric pattern; this class
    /// decides, for a pixel, which of the crew's four colours goes there. Pure integer maths so it can be
    /// unit-tested and gives the same texture on every machine. <c>Art.PatternTextures</c> turns it into textures.
    ///
    /// The four patterns are original geometry in the spirit of textile and wall-painting traditions (rings and
    /// dots, a crossed check, woven strips, stepped diamonds). They do not reproduce any specific cloth or motif
    /// that carries meaning; the art direction should be reviewed with designers from those traditions.
    /// </summary>
    public static class PatternRules
    {
        public const int PatternCount = 4;
        public const int ColourCount = 4;
        /// <summary>Textures are generated at this size and tile seamlessly.</summary>
        public const int Size = 128;

        public const int Rings = 0, Check = 1, Strips = 2, Stepped = 3;

        /// <summary>Colour index (0 primary, 1 secondary, 2 accent, 3 dark) for pixel (x, y) of a pattern.</summary>
        public static int Index(int pattern, int x, int y)
        {
            x = Wrap(x); y = Wrap(y);
            switch (((pattern % PatternCount) + PatternCount) % PatternCount)
            {
                case Rings:   return RingsAt(x, y);
                case Check:   return CheckAt(x, y);
                case Strips:  return StripsAt(x, y);
                default:      return SteppedAt(x, y);
            }
        }

        private static int Wrap(int v) => ((v % Size) + Size) % Size;

        /// <summary>Concentric rings on a grid, with a dot where four cells meet.</summary>
        private static int RingsAt(int x, int y)
        {
            const int cell = 32;
            int cx = x % cell - cell / 2, cy = y % cell - cell / 2;
            int rSq = cx * cx + cy * cy;
            // Corner dots (distance to the nearest cell corner).
            int kx = Math.Min(x % cell, cell - x % cell), ky = Math.Min(y % cell, cell - y % cell);
            if (kx * kx + ky * ky <= 9) return 2;
            if (rSq > 14 * 14) return 0;
            int ring = (int)Math.Sqrt(rSq) / 4;       // 0..3
            return ring == 0 ? 2 : (ring % 2 == 1 ? 1 : 3);
        }

        /// <summary>Wide bands crossing at right angles with thin lines between them; where two bands cross, the colours weave.</summary>
        private static int CheckAt(int x, int y)
        {
            int a = Band(x % 64), b = Band(y % 64);
            if (a == 2 || b == 2) return 2;
            if (a == 3 || b == 3) return 3;
            if (a == b) return a;
            return ((x + y) & 1) == 0 ? 0 : 1;        // crossing of the two wide bands: a one-pixel weave
        }

        private static int Band(int u)
        {
            if (u < 28) return 0;
            if (u < 32) return 2;
            if (u < 60) return 1;
            return 3;
        }

        /// <summary>Vertical strips of alternating blocks: solid, fine-striped, and zigzag.</summary>
        private static int StripsAt(int x, int y)
        {
            int strip = x / 32, block = y / 16;
            int lx = x % 32, ly = y % 16;
            if (lx == 0 || lx == 31) return 3;                         // seam between strips
            int kind = (strip + block) % 4;
            if (kind == 0) return block % 2 == 0 ? 0 : 1;              // solid
            if (kind == 1) return ly % 4 < 2 ? 2 : 3;                  // fine stripes
            if (kind == 2) return ((lx + (ly < 8 ? ly : 15 - ly)) % 8) < 4 ? 1 : 0;   // zigzag
            return ly % 8 < 4 ? 0 : 2;                                 // broad stripes
        }

        /// <summary>Stepped diamonds with a dark outline between every step.</summary>
        private static int SteppedAt(int x, int y)
        {
            int lx = Math.Abs(x % 64 - 32), ly = Math.Abs(y % 64 - 32);
            // Quantise to 4-pixel steps so the diamond has stairs instead of smooth diagonals.
            int d = (lx / 4) * 4 + (ly / 4) * 4;
            int step = d / 8;
            if (d % 8 == 0 && (lx % 4 == 0 || ly % 4 == 0)) return 3;
            switch (step % 4)
            {
                case 0: return 2;
                case 1: return 0;
                case 2: return 1;
                default: return 0;
            }
        }
    }
}
