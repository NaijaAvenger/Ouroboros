using UnityEngine;

namespace Ouroboros.AI
{
    /// <summary>
    /// Translucent fan drawn on the ground that shows what a camera or guard can see, so players know
    /// what to avoid. Pure presentation: built from a range and angle, re-coloured by state. Follows its
    /// owner's position and yaw every frame.
    /// </summary>
    public class VisionCone : MonoBehaviour
    {
        private const int Segments = 28;

        private Transform owner;
        private float range;
        private float angle;
        private float groundOffset;
        private MeshFilter filter;
        private MeshRenderer meshRenderer;
        private Material material;
        private Color currentColor;

        public static readonly Color IdleColor = new Color(1f, 0.9f, 0.2f, 0.18f);
        public static readonly Color SearchColor = new Color(1f, 0.55f, 0.1f, 0.25f);
        public static readonly Color AlertColor = new Color(1f, 0.1f, 0.1f, 0.32f);
        public static readonly Color DisabledColor = new Color(0.4f, 0.4f, 0.4f, 0.08f);

        /// <summary>Creates a cone object for <paramref name="owner"/>. <paramref name="groundOffset"/> is the owner's local height above the floor.</summary>
        public static VisionCone Attach(Transform owner, float range, float angle, float groundOffset, Color color)
        {
            var go = new GameObject(owner.name + " VisionCone");
            var cone = go.AddComponent<VisionCone>();
            cone.owner = owner;
            cone.range = range;
            cone.angle = angle;
            cone.groundOffset = groundOffset;
            cone.Build();
            cone.SetColor(color);
            cone.LateUpdate();
            return cone;
        }

        public void Configure(float newRange, float newAngle)
        {
            if (Mathf.Approximately(newRange, range) && Mathf.Approximately(newAngle, angle)) return;
            range = newRange;
            angle = newAngle;
            Build();
        }

        public void SetColor(Color color)
        {
            if (material == null || currentColor == color) return;
            currentColor = color;
            material.color = color;
            if (material.HasProperty("_TintColor")) material.SetColor("_TintColor", color);
        }

        public void SetVisible(bool visible)
        {
            if (meshRenderer != null) meshRenderer.enabled = visible;
        }

        private void Build()
        {
            if (filter == null)
            {
                filter = gameObject.AddComponent<MeshFilter>();
                meshRenderer = gameObject.AddComponent<MeshRenderer>();
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
                material = new Material(shader);
                meshRenderer.sharedMaterial = material;
                meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                meshRenderer.receiveShadows = false;
            }

            var mesh = filter.sharedMesh != null ? filter.sharedMesh : new Mesh { name = "VisionCone" };
            var verts = new Vector3[Segments + 2];
            var tris = new int[Segments * 3];
            verts[0] = Vector3.zero;
            float half = angle * 0.5f;
            for (int i = 0; i <= Segments; i++)
            {
                float a = Mathf.Lerp(-half, half, (float)i / Segments) * Mathf.Deg2Rad;
                verts[i + 1] = new Vector3(Mathf.Sin(a) * range, 0f, Mathf.Cos(a) * range);
            }
            for (int i = 0; i < Segments; i++)
            {
                tris[i * 3] = 0;
                tris[i * 3 + 1] = i + 1;
                tris[i * 3 + 2] = i + 2;
            }
            mesh.Clear();
            mesh.vertices = verts;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            filter.sharedMesh = mesh;
        }

        private void LateUpdate()
        {
            if (owner == null)
            {
                Destroy(gameObject);
                return;
            }
            Vector3 flatForward = owner.forward;
            flatForward.y = 0f;
            if (flatForward.sqrMagnitude < 0.0001f) flatForward = Vector3.forward;
            transform.position = new Vector3(owner.position.x, owner.position.y - groundOffset + 0.06f, owner.position.z);
            transform.rotation = Quaternion.LookRotation(flatForward.normalized, Vector3.up);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
