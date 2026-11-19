using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Puts the game's look on things (v0.15). The look is three shaders in Assets/Resources/OuroborosShaders:
    /// a toon surface with a woven shadow edge, an ink outline pass on the camera, and a banded dusk sky.
    ///
    /// This class deals with surfaces. It swaps an ordinary lit material (Standard, Diffuse, URP Lit) for a toon
    /// twin with the same colour, texture and glow, and can swap it back. Twins are made once and shared, and
    /// they are copies: the original material assets are not edited at run time. Particles, trails, lines,
    /// sprites, text and see-through materials are left alone.
    ///
    /// If the toon shader is missing or will not run on this machine, nothing is swapped and the game looks as
    /// it did before. <see cref="StyleDirector"/> decides when to call this.
    /// </summary>
    public static class Stylizer
    {
        public const string ToonShaderName = "Ouroboros/Toon";

        private static Shader toon, plain;
        private static bool looked;
        private static readonly Dictionary<Material, Material> toonTwin = new Dictionary<Material, Material>();
        private static readonly Dictionary<Material, Material> plainTwin = new Dictionary<Material, Material>();
        private static readonly List<Material> scratch = new List<Material>(8);

        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private static readonly int WeaveId = Shader.PropertyToID("_Weave");

        /// <summary>The toon shader, or null when it is missing or unsupported here.</summary>
        public static Shader Toon
        {
            get
            {
                if (!looked)
                {
                    looked = true;
                    var found = Shader.Find(ToonShaderName);
                    toon = found != null && found.isSupported ? found : null;
                    plain = Shader.Find("Standard") ?? Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Diffuse");
                    if (toon == null) Debug.LogWarning("[Stylizer] '" + ToonShaderName + "' is missing or did not compile, so the stylised look is off. " +
                                                       "Select Assets/Resources/OuroborosShaders/OuroborosToon.shader to see its errors.");
                }
                return toon;
            }
        }

        public static bool Available => Toon != null;

        private static bool IsPlainLit(Shader shader)
        {
            if (shader == null) return false;
            string n = shader.name;
            return n == "Standard" || n == "Standard (Specular setup)" || n == "Diffuse" || n == "Legacy Shaders/Diffuse"
                || n == "Legacy Shaders/Bumped Diffuse" || n == "Legacy Shaders/Specular" || n == "Legacy Shaders/Bumped Specular"
                || n == "Mobile/Diffuse" || n == "Universal Render Pipeline/Lit" || n == "Universal Render Pipeline/Simple Lit";
        }

        private static Color ColourOf(Material m)
        {
            if (m.HasProperty(ColorId)) return m.GetColor(ColorId);
            if (m.HasProperty(BaseColorId)) return m.GetColor(BaseColorId);
            return Color.white;
        }

        private static Texture TextureOf(Material m, out Vector2 scale, out Vector2 offset)
        {
            scale = Vector2.one; offset = Vector2.zero;
            if (m.HasProperty("_MainTex")) { scale = m.mainTextureScale; offset = m.mainTextureOffset; return m.mainTexture; }
            if (m.HasProperty(BaseMapId)) { scale = m.GetTextureScale(BaseMapId); offset = m.GetTextureOffset(BaseMapId); return m.GetTexture(BaseMapId); }
            return null;
        }

        /// <summary>The material to draw with for the look being on or off. Returns the one given when no swap applies.</summary>
        public static Material For(Material source, bool stylised)
        {
            if (source == null || !Available) return source;
            bool isToon = source.shader == toon;

            if (stylised)
            {
                if (isToon || !IsPlainLit(source.shader) || source.renderQueue >= 2450) return source; // already toon, or not a plain opaque surface
                if (toonTwin.TryGetValue(source, out var twin) && twin != null) return twin;
                twin = new Material(toon) { name = source.name + " (toon)", hideFlags = HideFlags.DontSave };
                Copy(source, twin);
                toonTwin[source] = twin;
                plainTwin[twin] = source;
                return twin;
            }

            if (!isToon) return source;
            if (plainTwin.TryGetValue(source, out var back) && back != null) return back;
            if (plain == null) return source;
            // A material that was authored as toon (saved that way by the editor menu): make it a plain twin once.
            back = new Material(plain) { name = source.name + " (plain)", hideFlags = HideFlags.DontSave };
            Copy(source, back);
            plainTwin[source] = back;
            toonTwin[back] = source;
            return back;
        }

        private static void Copy(Material from, Material to)
        {
            Color colour = ColourOf(from);
            colour.a = 1f;
            if (to.HasProperty(ColorId)) to.SetColor(ColorId, colour);
            if (to.HasProperty(BaseColorId)) to.SetColor(BaseColorId, colour);

            var texture = TextureOf(from, out Vector2 scale, out Vector2 offset);
            if (to.HasProperty("_MainTex")) { to.mainTexture = texture; to.mainTextureScale = scale; to.mainTextureOffset = offset; }

            if (from.HasProperty(EmissionId) && to.HasProperty(EmissionId))
            {
                bool glows = from.shader == toon || from.IsKeywordEnabled("_EMISSION");
                Color emission = glows ? from.GetColor(EmissionId) : Color.black;
                to.SetColor(EmissionId, emission);
                if (emission.maxColorComponent > 0.01f) to.EnableKeyword("_EMISSION");
            }
            // Patterned cloth takes the diamond weave; everything else the strokes.
            if (to.HasProperty(WeaveId)) to.SetFloat(WeaveId, texture != null ? 1f : 0f);
        }

        /// <summary>Swaps the materials on one renderer. Returns true if anything changed.</summary>
        public static bool Apply(Renderer renderer, bool stylised)
        {
            if (renderer == null || !Available) return false;
            if (!(renderer is MeshRenderer) && !(renderer is SkinnedMeshRenderer)) return false;

            renderer.GetSharedMaterials(scratch);
            bool changed = false;
            for (int i = 0; i < scratch.Count; i++)
            {
                var swapped = For(scratch[i], stylised);
                if (swapped != scratch[i]) { scratch[i] = swapped; changed = true; }
            }
            if (changed) renderer.sharedMaterials = scratch.ToArray();
            return changed;
        }

        /// <summary>Swaps the materials on everything under <paramref name="root"/>: for things that arrive mid-match.</summary>
        public static void Apply(GameObject root, bool stylised)
        {
            if (root == null || !Available) return;
            foreach (var renderer in root.GetComponentsInChildren<Renderer>(true)) Apply(renderer, stylised);
        }
    }
}
