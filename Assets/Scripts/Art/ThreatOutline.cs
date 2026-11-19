using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// (v0.17) Marks something that can hurt or report you with a bright outline, so it reads at a glance:
    /// guards and turrets in <see cref="Hostile"/> red, cameras in <see cref="Watcher"/> amber, a hijacked
    /// turret in the colour of the crew that holds it. A thing that is switched off loses its outline.
    ///
    /// For every solid part of the owner's model this adds a child that draws the same mesh with the
    /// "Ouroboros/Threat Outline" shader: a flat shell just outside the part. Shells are hidden by walls like
    /// anything else. If the shader is missing the component does nothing and the game looks as it did.
    /// Settings > Threat outlines switches all of them.
    /// </summary>
    public class ThreatOutline : MonoBehaviour
    {
        public const string ShaderName = "Ouroboros/Threat Outline";
        private const string ShellName = "Threat Outline";
        /// <summary>Parts smaller than this are left out: their neighbours' shells cover them, and each shell is a draw call.</summary>
        private const float SmallestPart = 0.1f;

        /// <summary>It will shoot you.</summary>
        public static readonly Color Hostile = new Color(1f, 0.25f, 0.1f);
        /// <summary>It will see you and raise the alarm.</summary>
        public static readonly Color Watcher = new Color(1f, 0.78f, 0.1f);

        private static Shader shader;
        private static bool looked, warned;
        private static readonly Dictionary<string, Material> Materials = new Dictionary<string, Material>();
        private static readonly int HullCentre = Shader.PropertyToID("_HullCentre");
        private static readonly int HullExtents = Shader.PropertyToID("_HullExtents");

        private struct Shell { public MeshRenderer Source, Hull; public Vector3 Centre, Extents; }
        private readonly List<Shell> shells = new List<Shell>();
        private bool built, wanted = true, shown, dirty = true;
        private Color colour = Hostile, shownColour;
        private float nextSync;

        /// <summary>True if the outline shader is in the project and compiled.</summary>
        public static bool Available
        {
            get
            {
                if (!looked)
                {
                    looked = true;
                    var found = Shader.Find(ShaderName);
                    if (found != null && found.isSupported) shader = found;
                }
                return shader != null;
            }
        }

        /// <summary>Adds an outline to <paramref name="owner"/> (or returns the one it has) in <paramref name="colour"/>.</summary>
        public static ThreatOutline Attach(GameObject owner, Color colour)
        {
            if (owner == null) return null;
            var outline = owner.GetComponent<ThreatOutline>();
            if (outline == null) outline = owner.AddComponent<ThreatOutline>();
            outline.colour = colour;
            outline.dirty = true;
            return outline;
        }

        /// <summary>Shows or hides the outline and sets its colour. Cheap to call every frame.</summary>
        public void Set(bool visible, Color newColour)
        {
            if (visible == wanted && newColour == colour) return;
            wanted = visible;
            colour = newColour;
            dirty = true;
        }

        /// <summary>Call after the owner's model has been rebuilt.</summary>
        public void Rebuild()
        {
            for (int i = 0; i < shells.Count; i++) if (shells[i].Hull != null) Destroy(shells[i].Hull.gameObject);
            shells.Clear();
            built = false;
            dirty = true;
        }

        private static Material MaterialFor(Color colour, Vector3 centre, Vector3 extents)
        {
            // One material per colour and mesh shape: a handful in all, and nothing for other code's
            // property blocks to overwrite.
            string key = $"{(Color32)colour}|{centre.x:0.00},{centre.y:0.00},{centre.z:0.00}|{extents.x:0.00},{extents.y:0.00},{extents.z:0.00}";
            if (Materials.TryGetValue(key, out var m) && m != null) return m;
            m = new Material(shader) { color = colour, hideFlags = HideFlags.DontSave };
            m.SetVector(HullCentre, centre);
            m.SetVector(HullExtents, extents);
            Materials[key] = m;
            return m;
        }

        private void Build()
        {
            built = true;
            if (!Available)
            {
                if (!warned) { warned = true; Debug.LogWarning($"[Ouroboros] Shader '{ShaderName}' not found: threats will not be outlined."); }
                return;
            }
            var renderers = GetComponentsInChildren<MeshRenderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                var source = renderers[i];
                if (source == null || !source.enabled || source.gameObject.name == ShellName) continue;
                var filter = source.GetComponent<MeshFilter>();
                var mesh = filter != null ? filter.sharedMesh : null;
                var material = source.sharedMaterial;
                if (mesh == null || material == null || material.renderQueue >= 2450) continue; // glass, beams, vision cones
                Vector3 size = source.bounds.size;
                if (Mathf.Max(size.x, Mathf.Max(size.y, size.z)) < SmallestPart) continue;
                Vector3 extents = mesh.bounds.extents;
                if (extents.x <= 0f && extents.y <= 0f && extents.z <= 0f) continue;

                var go = new GameObject(ShellName);
                go.layer = source.gameObject.layer;
                go.transform.SetParent(source.transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                var hull = go.AddComponent<MeshRenderer>();
                hull.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hull.receiveShadows = false;
                hull.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
                hull.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
                hull.enabled = false;
                shells.Add(new Shell { Source = source, Hull = hull, Centre = mesh.bounds.center, Extents = extents });
            }
        }

        private void LateUpdate()
        {
            if (!built) Build(); // on the first frame, so a model built in the owner's Spawned is all there
            if (shells.Count == 0) return;

            bool show = wanted && Core.GameSettings.ThreatOutlines;
            if (!dirty && show == shown && Time.unscaledTime < nextSync) return;
            nextSync = Time.unscaledTime + 0.25f; // parts that other code hides are followed four times a second
            bool recolour = dirty || colour != shownColour;
            dirty = false;
            shown = show;
            shownColour = colour;

            for (int i = 0; i < shells.Count; i++)
            {
                var s = shells[i];
                if (s.Hull == null) continue;
                bool on = show && s.Source != null && s.Source.enabled;
                if (on && (recolour || s.Hull.sharedMaterial == null)) s.Hull.sharedMaterial = MaterialFor(colour, s.Centre, s.Extents);
                if (s.Hull.enabled != on) s.Hull.enabled = on;
            }
        }
    }
}
