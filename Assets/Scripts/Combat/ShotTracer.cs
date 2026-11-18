using System.Collections.Generic;
using UnityEngine;

namespace Ouroboros.Combat
{
    /// <summary>
    /// Pooled bullet tracer (v0.9): a thin line from muzzle to impact that fades in a few frames. Built in code
    /// (no prefab or material asset), purely cosmetic, and safe to call on every peer. Without tracers a
    /// gunfight between capsules is unreadable: you cannot tell who is shooting at whom.
    /// </summary>
    public class ShotTracer : MonoBehaviour
    {
        private static readonly Stack<ShotTracer> Pool = new Stack<ShotTracer>();
        private static Material sharedMaterial;
        private const int MaxPooled = 64;

        private LineRenderer line;
        private Color color;
        private float life, age;

        /// <summary>Draws a tracer from <paramref name="from"/> to <paramref name="to"/>.</summary>
        public static void Spawn(Vector3 from, Vector3 to, Color color, float width = 0.03f, float seconds = 0.08f)
        {
            if ((to - from).sqrMagnitude < 0.25f) return;

            ShotTracer tracer = null;
            while (Pool.Count > 0 && tracer == null) tracer = Pool.Pop(); // skip entries destroyed by a scene change
            if (tracer == null) tracer = Create();

            tracer.color = color;
            tracer.life = Mathf.Max(0.02f, seconds);
            tracer.age = 0f;
            tracer.line.startWidth = width;
            tracer.line.endWidth = width * 0.4f;
            tracer.line.SetPosition(0, from);
            tracer.line.SetPosition(1, to);
            tracer.ApplyColor(1f);
            tracer.gameObject.SetActive(true);
        }

        private static ShotTracer Create()
        {
            var go = new GameObject("ShotTracer");
            go.hideFlags = HideFlags.DontSave;
            var tracer = go.AddComponent<ShotTracer>();
            var lr = go.AddComponent<LineRenderer>();
            lr.positionCount = 2;
            lr.useWorldSpace = true;
            lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows = false;
            lr.numCapVertices = 2;
            if (sharedMaterial == null)
            {
                var shader = Shader.Find("Sprites/Default") ?? Shader.Find("Unlit/Transparent") ?? Shader.Find("Standard");
                if (shader != null) sharedMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            }
            if (sharedMaterial != null) lr.sharedMaterial = sharedMaterial;
            tracer.line = lr;
            return tracer;
        }

        private void Update()
        {
            age += Time.deltaTime;
            if (age >= life)
            {
                gameObject.SetActive(false);
                if (Pool.Count < MaxPooled) Pool.Push(this); else Destroy(gameObject);
                return;
            }
            ApplyColor(1f - age / life);
        }

        private void ApplyColor(float alpha)
        {
            var start = color; start.a = color.a * alpha;
            var end = color; end.a = color.a * alpha * 0.2f;
            line.startColor = start;
            line.endColor = end;
        }
    }
}
