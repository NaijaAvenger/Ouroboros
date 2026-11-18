using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Primitive parts for code-built models (v0.13). Every part is a visual only: its collider is removed, so
    /// a model can never stop a bullet, a player or a line of sight. Works at play time and in the editor.
    /// </summary>
    public static class Prim
    {
        public static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material material, Vector3 euler = default)
            => Part(PrimitiveType.Cube, parent, name, pos, size, material, euler);

        public static GameObject Ball(Transform parent, string name, Vector3 pos, Vector3 size, Material material)
            => Part(PrimitiveType.Sphere, parent, name, pos, size, material, default);

        /// <summary>Unity's cylinder is 2 units tall, so <paramref name="size"/>.y is the half-height. Its axis is local Y.</summary>
        public static GameObject Rod(Transform parent, string name, Vector3 pos, Vector3 size, Material material, Vector3 euler = default)
            => Part(PrimitiveType.Cylinder, parent, name, pos, size, material, euler);

        public static Transform Group(Transform parent, string name, Vector3 localPosition = default)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPosition;
            return t;
        }

        public static GameObject Part(PrimitiveType type, Transform parent, string name, Vector3 pos, Vector3 size, Material material, Vector3 euler)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            StripCollider(go);
            if (parent != null) go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localRotation = Quaternion.Euler(euler);
            go.transform.localScale = size;
            if (material != null) go.GetComponent<Renderer>().sharedMaterial = material;
            return go;
        }

        public static void StripCollider(GameObject go)
        {
            var collider = go.GetComponent<Collider>();
            if (collider == null) return;
            if (Application.isPlaying) Object.Destroy(collider);
            else Object.DestroyImmediate(collider);
        }
    }
}
