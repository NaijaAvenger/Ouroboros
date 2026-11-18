using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// World-space sign text (v0.12, milestone M4). Unity's default font material draws over everything, so a
    /// plain TextMesh shows through walls. This swaps in the depth-tested "Ouroboros/World Text" material and
    /// keeps it pointed at the font's atlas, which Unity can rebuild at any time.
    /// Falls back to the default font material when the material is missing.
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(TextMesh))]
    [RequireComponent(typeof(MeshRenderer))]
    public class WorldSign : MonoBehaviour
    {
        [Tooltip("A material using the 'Ouroboros/World Text' shader. The Freeport builder assigns it.")]
        [SerializeField] private Material material;

        private void OnEnable() => Apply();

        private void Apply()
        {
            var mesh = GetComponent<TextMesh>();
            var meshRenderer = GetComponent<MeshRenderer>();
            if (mesh == null || meshRenderer == null || mesh.font == null) return;

            var fontMaterial = mesh.font.material;
            if (material == null || fontMaterial == null)
            {
                meshRenderer.sharedMaterial = fontMaterial;
                return;
            }
            if (material.mainTexture != fontMaterial.mainTexture) material.mainTexture = fontMaterial.mainTexture;
            meshRenderer.sharedMaterial = material;
        }
    }
}
