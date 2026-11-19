using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Keeps the whole game in the stylised look (v0.15), and takes it off again when the player switches it
    /// off in the settings. A few times a second it makes sure that:
    ///
    ///  - every camera has the ink pass, switched to match the setting;
    ///  - every mesh in the scene, including ones that arrived since the last look, wears the right materials;
    ///  - code-built materials (characters, gear, loot, effects) use the right shader;
    ///  - the sky is the banded dusk and the ambient light is warm above and indigo below.
    ///
    /// It only ever swaps in copies, so nothing it does is saved into the project. To bake the look into the
    /// project's own materials (so the editor shows it too), use Ouroboros > Setup > Apply Stylised Look.
    /// Added automatically by <c>UI.MatchFeedback</c>.
    /// </summary>
    public class StyleDirector : MonoBehaviour
    {
        public const string SkyShaderName = "Ouroboros/Sky";

        [Tooltip("Seconds between checks of the cameras and the setting.")]
        [SerializeField] private float interval = 0.5f;
        [Tooltip("Seconds between sweeps for meshes that arrived since the last one. A sweep walks every mesh in the scene, so not too often.")]
        [SerializeField] private float newObjectInterval = 2f;
        [Tooltip("Replace the scene's sky with the banded dusk while the look is on.")]
        [SerializeField] private bool ownTheSky = true;

        private float nextSweep, nextNewObjects;
        private bool applied, everApplied;
        private readonly HashSet<int> seen = new HashSet<int>();
        private Material sky, previousSky;
        private UnityEngine.Rendering.AmbientMode previousMode;
        private Color previousAmbient, previousSkyColour, previousEquator, previousGround;
        private static readonly int SunId = Shader.PropertyToID("_SunDirection");

        private void OnEnable()
        {
            nextSweep = 0f;
        }

        private void OnDisable()
        {
            if (applied) SetLook(false);
        }

        private void Update()
        {
            if (Time.unscaledTime < nextSweep) return;
            nextSweep = Time.unscaledTime + Mathf.Max(0.1f, interval);

            bool wanted = Core.GameSettings.StylisedLook && Stylizer.Available;
            if (wanted != applied || !everApplied) SetLook(wanted);
            else if (applied && Time.unscaledTime >= nextNewObjects)
            {
                nextNewObjects = Time.unscaledTime + Mathf.Max(0.5f, newObjectInterval);
                Sweep(true, onlyNew: true);
            }

            Cameras(wanted);
            if (applied && sky != null) AimSun();
        }

        private void SetLook(bool on)
        {
            everApplied = true;
            applied = on;
            seen.Clear();
            ArtMaterials.SetToon(on);
            Sweep(on, onlyNew: false);
            Sky(on);
        }

        /// <summary>Every mesh in the scene, or only the ones not seen before.</summary>
        private void Sweep(bool stylised, bool onlyNew)
        {
            if (!Stylizer.Available) return;
            var renderers = Core.SceneUtil.FindAll<MeshRenderer>();
            for (int i = 0; i < renderers.Length; i++)
            {
                var r = renderers[i];
                if (r == null) continue;
                if (onlyNew && !seen.Add(r.GetInstanceID())) continue;
                if (!onlyNew) seen.Add(r.GetInstanceID());
                Stylizer.Apply(r, stylised);
            }
        }

        private void Cameras(bool on)
        {
            var cameras = Camera.allCameras;
            for (int i = 0; i < cameras.Length; i++)
            {
                var cam = cameras[i];
                if (cam == null || cam.targetTexture != null) continue; // leave render-to-texture cameras alone
                var ink = cam.GetComponent<InkOutline>();
                if (ink == null)
                {
                    if (!on) continue;
                    ink = cam.gameObject.AddComponent<InkOutline>();
                }
                if (ink.enabled != on) ink.enabled = on;
            }
        }

        private void Sky(bool on)
        {
            if (!ownTheSky) return;
            if (on)
            {
                if (sky == null)
                {
                    var shader = Shader.Find(SkyShaderName);
                    if (shader == null || !shader.isSupported) return;
                    sky = new Material(shader) { hideFlags = HideFlags.DontSave };
                }
                if (RenderSettings.skybox != sky)
                {
                    previousSky = RenderSettings.skybox;
                    previousMode = RenderSettings.ambientMode;
                    previousAmbient = RenderSettings.ambientLight;
                    previousSkyColour = RenderSettings.ambientSkyColor;
                    previousEquator = RenderSettings.ambientEquatorColor;
                    previousGround = RenderSettings.ambientGroundColor;
                }
                RenderSettings.skybox = sky;
                // Keep the scene as bright as its author made it, and only add the tilt: warm from above, indigo from below.
                float level = Mathf.Clamp(Mathf.Max(previousAmbient.maxColorComponent, 0.35f), 0.35f, 0.75f);
                RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
                RenderSettings.ambientSkyColor = new Color(1.12f, 1.02f, 0.86f) * level;
                RenderSettings.ambientEquatorColor = new Color(0.98f, 0.94f, 0.96f) * level;
                RenderSettings.ambientGroundColor = new Color(0.72f, 0.68f, 0.98f) * level;
                AimSun();
            }
            else if (sky != null && RenderSettings.skybox == sky)
            {
                RenderSettings.skybox = previousSky;
                RenderSettings.ambientMode = previousMode;
                RenderSettings.ambientLight = previousAmbient;
                RenderSettings.ambientSkyColor = previousSkyColour;
                RenderSettings.ambientEquatorColor = previousEquator;
                RenderSettings.ambientGroundColor = previousGround;
            }
        }

        private void AimSun()
        {
            var sun = RenderSettings.sun;
            // A low sun for the sky, whatever angle the scene's light is at: dusk is the look.
            Vector3 direction = sun != null ? -sun.transform.forward : new Vector3(0.3f, 0.35f, 0.9f);
            direction.y = Mathf.Clamp(direction.y, 0.12f, 0.3f);
            sky.SetVector(SunId, direction.normalized);
        }
    }
}
