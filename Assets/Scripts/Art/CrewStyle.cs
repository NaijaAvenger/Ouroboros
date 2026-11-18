using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// A crew's look (v0.12, milestone M4): four colours and a pattern. Everything that shows crew identity
    /// reads it from here: character cloth, HUD accents, tethers, banners, spawn gizmos.
    /// </summary>
    public struct CrewStyle
    {
        public string Name;
        public Color Primary, Secondary, Accent, Dark;
        public int Pattern;

        public Color Colour(int index)
        {
            switch (index)
            {
                case 0: return Primary;
                case 1: return Secondary;
                case 2: return Accent;
                default: return Dark;
            }
        }

        private static Color Rgb(int r, int g, int b) => new Color(r / 255f, g / 255f, b / 255f);

        /// <summary>Primaries are far apart on the colour wheel (teal, red, gold, blue) so crews read at a glance.</summary>
        public static CrewStyle Get(Core.TeamID team)
        {
            switch (team)
            {
                case Core.TeamID.TeamAlpha: // Lagos: rings and dots
                    return new CrewStyle { Name = "Lagos", Primary = Rgb(0, 166, 153), Secondary = Rgb(237, 110, 31), Accent = Rgb(255, 209, 51), Dark = Rgb(20, 36, 46), Pattern = Core.PatternRules.Rings };
                case Core.TeamID.TeamBravo: // Nairobi: crossed check
                    return new CrewStyle { Name = "Nairobi", Primary = Rgb(204, 31, 38), Secondary = Rgb(31, 56, 153), Accent = Rgb(245, 245, 240), Dark = Rgb(20, 20, 26), Pattern = Core.PatternRules.Check };
                case Core.TeamID.TeamCharlie: // Accra: woven strips
                    return new CrewStyle { Name = "Accra", Primary = Rgb(242, 191, 31), Secondary = Rgb(26, 128, 56), Accent = Rgb(196, 36, 36), Dark = Rgb(26, 26, 20), Pattern = Core.PatternRules.Strips };
                case Core.TeamID.TeamDelta: // Joburg: stepped diamonds
                    return new CrewStyle { Name = "Joburg", Primary = Rgb(41, 97, 224), Secondary = Rgb(245, 245, 245), Accent = Rgb(232, 56, 150), Dark = Rgb(15, 15, 20), Pattern = Core.PatternRules.Stepped };
                default:
                    return new CrewStyle { Name = "-", Primary = Color.white, Secondary = Color.gray, Accent = Color.white, Dark = Color.black, Pattern = 0 };
            }
        }
    }

    /// <summary>Crew pattern textures, generated once from <see cref="Core.PatternRules"/> and cached.</summary>
    public static class PatternTextures
    {
        private static readonly Dictionary<Core.TeamID, Texture2D> Cache = new Dictionary<Core.TeamID, Texture2D>();

        public static Texture2D Get(Core.TeamID team)
        {
            if (Cache.TryGetValue(team, out var cached) && cached != null) return cached;
            var texture = Build(CrewStyle.Get(team));
            texture.name = "CrewPattern_" + team;
            texture.hideFlags = HideFlags.DontSave;
            Cache[team] = texture;
            return texture;
        }

        /// <summary>Builds a fresh texture for a style (used by the editor to save pattern assets).</summary>
        public static Texture2D Build(CrewStyle style)
        {
            int size = Core.PatternRules.Size;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                wrapMode = TextureWrapMode.Repeat,
                filterMode = FilterMode.Point // crisp edges: these are printed and woven patterns, not gradients
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    pixels[y * size + x] = style.Colour(Core.PatternRules.Index(style.Pattern, x, y));
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            return texture;
        }
    }

    /// <summary>
    /// Materials for code-built models, cached so a hundred parts share a handful of materials. Works on the
    /// built-in pipeline and on URP (whichever lit shader is present).
    /// </summary>
    public static class ArtMaterials
    {
        private static readonly Dictionary<int, Material> Solid = new Dictionary<int, Material>();
        private static readonly Dictionary<int, Material> Glow = new Dictionary<int, Material>();
        private static readonly Dictionary<Core.TeamID, Material> Cloth = new Dictionary<Core.TeamID, Material>();
        private static Shader lit;

        private static Shader Lit
        {
            get
            {
                if (lit == null) lit = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard") ?? Shader.Find("Diffuse");
                return lit;
            }
        }

        private static int Key(Color c) => ((Color32)c).GetHashCode();

        public static Material Colour(Color color)
        {
            int key = Key(color);
            if (Solid.TryGetValue(key, out var m) && m != null) return m;
            m = New(color);
            Solid[key] = m;
            return m;
        }

        /// <summary>A colour that also emits light: visors, eyes, status lights.</summary>
        public static Material Emissive(Color color)
        {
            int key = Key(color);
            if (Glow.TryGetValue(key, out var m) && m != null) return m;
            m = New(color);
            m.EnableKeyword("_EMISSION");
            if (m.HasProperty("_EmissionColor")) m.SetColor("_EmissionColor", color * 1.6f);
            Glow[key] = m;
            return m;
        }

        /// <summary>The crew's pattern, tiled about once per 40 cm of cloth.</summary>
        public static Material CrewCloth(Core.TeamID team)
        {
            if (Cloth.TryGetValue(team, out var m) && m != null) return m;
            m = New(Color.white);
            m.mainTexture = PatternTextures.Get(team);
            m.mainTextureScale = new Vector2(1.5f, 1.5f);
            Cloth[team] = m;
            return m;
        }

        // ---- v0.13: see-through and glowing materials for effects (shields, smoke, pulses, flames) ----
        private static readonly Dictionary<int, Material> Clear = new Dictionary<int, Material>();
        private static Shader unlitBlend;
        private static Texture2D softDot;
        private static Material particle;

        /// <summary>
        /// An unlit, alpha-blended shader that exists on the built-in pipeline and on URP and is always in a
        /// build: the sprite shader. It multiplies its colour by vertex colour, which is what particles need.
        /// </summary>
        private static Shader UnlitBlend
        {
            get
            {
                if (unlitBlend == null) unlitBlend = Shader.Find("Sprites/Default") ?? Shader.Find("UI/Default") ?? Shader.Find("Unlit/Transparent") ?? Lit;
                return unlitBlend;
            }
        }

        /// <summary>An unlit see-through colour (alpha is honoured). For bubbles, rings, beams and flashes.</summary>
        public static Material Translucent(Color color)
        {
            int key = Key(color);
            if (Clear.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(UnlitBlend) { color = color, hideFlags = HideFlags.DontSave, renderQueue = 3000 };
            Clear[key] = m;
            return m;
        }

        /// <summary>A fresh see-through material that one effect can fade without touching the shared ones.</summary>
        public static Material TranslucentInstance(Color color)
        {
            return new Material(UnlitBlend) { color = color, hideFlags = HideFlags.DontSave, renderQueue = 3000 };
        }

        /// <summary>A round, soft-edged white dot: the sprite every code-built particle uses.</summary>
        public static Texture2D SoftDot
        {
            get
            {
                if (softDot != null) return softDot;
                const int size = 64;
                softDot = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "SoftDot", hideFlags = HideFlags.DontSave, wrapMode = TextureWrapMode.Clamp };
                var pixels = new Color32[size * size];
                float half = (size - 1) * 0.5f;
                for (int y = 0; y < size; y++)
                {
                    for (int x = 0; x < size; x++)
                    {
                        float d = Mathf.Sqrt((x - half) * (x - half) + (y - half) * (y - half)) / half;
                        float a = Mathf.Clamp01(1f - d);
                        pixels[y * size + x] = new Color(1f, 1f, 1f, a * a);
                    }
                }
                softDot.SetPixels32(pixels);
                softDot.Apply(false, true);
                return softDot;
            }
        }

        /// <summary>Material for code-built particle systems: soft dots tinted by each particle's colour.</summary>
        public static Material Particle
        {
            get
            {
                if (particle != null) return particle;
                particle = new Material(UnlitBlend) { color = Color.white, hideFlags = HideFlags.DontSave, renderQueue = 3100 };
                particle.mainTexture = SoftDot;
                return particle;
            }
        }

        private static Material New(Color color)
        {
            var m = new Material(Lit) { color = color, hideFlags = HideFlags.DontSave };
            if (m.HasProperty("_BaseColor")) m.SetColor("_BaseColor", color);
            if (m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness", 0.15f);
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.15f);
            return m;
        }
    }
}
