using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// The ink pass on one camera (v0.15): brushed outlines and the colour grade, drawn by
    /// Assets/Resources/OuroborosShaders/OuroborosInk.shader. It runs before see-through things are drawn, so
    /// particles, glass and shield bubbles are not outlined, and it never touches the HUD.
    /// <see cref="StyleDirector"/> adds it to every camera and switches it with the look.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class InkOutline : MonoBehaviour
    {
        public const string ShaderName = "Hidden/Ouroboros/Ink";

        [Range(0.5f, 4f)] public float thickness = 1.4f;
        [Range(0f, 2f)] public float wobble = 0.8f;
        [Range(0f, 1f)] public float strength = 0.9f;
        public Color ink = new Color(0.1f, 0.07f, 0.2f);
        // [v0.22] [Range(0f, 1f)] public float vignette = 0.28f;
        [Range(0f, 1f)] public float vignette = 0f; // (v0.23) off: it sat on the picture like a film

        private Material material;
        private Camera view;
        private bool unsupported;

        private static readonly int InkColorId = Shader.PropertyToID("_InkColor");
        private static readonly int ThicknessId = Shader.PropertyToID("_Thickness");
        private static readonly int WobbleId = Shader.PropertyToID("_Wobble");
        private static readonly int VignetteId = Shader.PropertyToID("_Vignette");

        private void OnEnable()
        {
            view = GetComponent<Camera>();
            if (view != null) view.depthTextureMode |= DepthTextureMode.DepthNormals; // the edges are found in depth and normals
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }

        private bool Ready()
        {
            if (unsupported) return false;
            if (material != null) return true;
            var shader = Shader.Find(ShaderName);
            if (shader == null || !shader.isSupported)
            {
                unsupported = true;
                Debug.LogWarning("[InkOutline] '" + ShaderName + "' is missing or did not compile, so there are no outlines. " +
                                 "Select Assets/Resources/OuroborosShaders/OuroborosInk.shader to see its errors.");
                return false;
            }
            material = new Material(shader) { hideFlags = HideFlags.DontSave };
            return true;
        }

        [ImageEffectOpaque]
        private void OnRenderImage(RenderTexture source, RenderTexture destination)
        {
            if (!Ready())
            {
                Graphics.Blit(source, destination);
                return;
            }
            // Thicker lines on bigger screens, so the drawing keeps its weight at 4K.
            float scale = Mathf.Clamp(source.height / 1080f, 0.75f, 2.5f);
            material.SetColor(InkColorId, new Color(ink.r, ink.g, ink.b, strength));
            material.SetFloat(ThicknessId, thickness * scale);
            material.SetFloat(WobbleId, wobble);
            material.SetFloat(VignetteId, vignette);
            Graphics.Blit(source, destination, material);
        }
    }
}
