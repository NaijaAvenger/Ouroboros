using UnityEngine;

namespace Ouroboros.Art
{
    /// <summary>
    /// Code-built visual effects (v0.13): pulses, blasts, smoke, fire, sparks, flashes. They exist so every
    /// ability and every piece of gear shows something in the world without any effect assets. All of them are
    /// cosmetic, run on every peer, and clean themselves up.
    ///
    /// Particles use one soft-dot sprite on the sprite shader, which exists on the built-in pipeline and on URP.
    /// </summary>
    public static class Fx
    {
        public static readonly Color FireColour = new Color(1f, 0.55f, 0.12f);
        public static readonly Color SmokeColour = new Color(0.62f, 0.64f, 0.68f);
        public static readonly Color ShockColour = new Color(0.35f, 0.75f, 1f);

        // ------------------------------------------------------------------
        // Particles

        /// <summary>A particle system built in code. Returned inactive-safe: it is already playing.</summary>
        public static ParticleSystem Particles(string name, Vector3 position, Transform parent, Color from, Color to,
            float rate, int burst, float duration, bool loop, float lifeMin, float lifeMax, float speedMin, float speedMax,
            float sizeMin, float sizeMax, float shapeRadius, float gravity, float grow, bool destroyWhenDone)
        {
            var go = new GameObject(name);
            go.SetActive(false); // configure before it starts: a playing system refuses a new duration
            if (parent != null) { go.transform.SetParent(parent, false); go.transform.localPosition = position; }
            else go.transform.position = position;

            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.duration = Mathf.Max(0.05f, duration);
            main.loop = loop;
            main.startLifetime = new ParticleSystem.MinMaxCurve(lifeMin, lifeMax);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speedMin, speedMax);
            main.startSize = new ParticleSystem.MinMaxCurve(sizeMin, sizeMax);
            main.startColor = Color.white;
            main.gravityModifier = gravity;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            main.playOnAwake = true;
            main.stopAction = destroyWhenDone ? ParticleSystemStopAction.Destroy : ParticleSystemStopAction.None;

            var emission = ps.emission;
            emission.rateOverTime = rate;
            if (burst > 0) emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Min(burst, 400)) });

            var shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = Mathf.Max(0.01f, shapeRadius);

            var colour = ps.colorOverLifetime;
            colour.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(from, 0f), new GradientColorKey(to, 1f) },
                new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(from.a, 0.12f), new GradientAlphaKey(to.a * 0.8f, 0.6f), new GradientAlphaKey(0f, 1f) });
            colour.color = new ParticleSystem.MinMaxGradient(gradient);

            if (!Mathf.Approximately(grow, 1f))
            {
                var size = ps.sizeOverLifetime;
                size.enabled = true;
                size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, grow));
            }

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = ArtMaterials.Particle;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            go.SetActive(true);
            return ps;
        }

        /// <summary>A one-shot spray of dots: sparks, debris, embers.</summary>
        public static void Burst(Vector3 position, Color colour, int count, float speed, float size, float life, float gravity = 0.6f)
        {
            Color end = colour; end.a = 0f;
            Particles("Fx Burst", position, null, colour, end, 0f, count, life, false, life * 0.5f, life, speed * 0.35f, speed, size * 0.5f, size, 0.08f, gravity, 0.4f, true);
        }

        /// <summary>A cloud that hangs in a volume for <paramref name="seconds"/>, then thins out.</summary>
        public static void Smoke(Vector3 position, float radius, float seconds, Color colour)
        {
            Color start = colour; start.a = 0.55f;
            Color end = colour; end.a = 0.35f;
            float puff = Mathf.Max(1.2f, radius * 0.75f);
            var ps = Particles("Fx Smoke", position + Vector3.up * (radius * 0.3f), null, start, end, 14f, 26, seconds, false,
                3f, 4.5f, 0.05f, 0.35f, puff, puff * 1.6f, radius * 0.7f, -0.01f, 1.3f, true);
            var main = ps.main;
            main.maxParticles = 120;
        }

        /// <summary>Flames licking up from a patch of floor for <paramref name="seconds"/>.</summary>
        public static void Fire(Vector3 position, float radius, float seconds)
        {
            Color hot = new Color(1f, 0.85f, 0.3f, 0.95f), cool = new Color(1f, 0.3f, 0.05f, 0.7f);
            Particles("Fx Fire", position + Vector3.up * 0.15f, null, hot, cool, 55f, 30, seconds, false,
                0.45f, 0.9f, 1.2f, 2.6f, 0.35f, 0.9f, radius * 0.75f, -0.5f, 0.3f, true);
            Flash(position + Vector3.up * 0.6f, FireColour, radius * 2.5f, 3.5f, Mathf.Min(seconds, 1.2f));
        }

        /// <summary>Looping flames stuck to something. Toggle the returned object to show or hide them.</summary>
        public static GameObject AttachFlames(Transform parent, Vector3 localPosition)
        {
            Color hot = new Color(1f, 0.8f, 0.25f, 0.95f), cool = new Color(1f, 0.25f, 0.05f, 0.6f);
            return Particles("Flames", localPosition, parent, hot, cool, 34f, 0, 1f, true, 0.35f, 0.7f, 0.8f, 1.8f, 0.2f, 0.5f, 0.3f, -0.6f, 0.3f, false).gameObject;
        }

        /// <summary>Looping electric sparks stuck to something.</summary>
        public static GameObject AttachSparks(Transform parent, Vector3 localPosition, Color colour)
        {
            Color end = Color.white; end.a = 0.2f;
            return Particles("Sparks", localPosition, parent, colour, end, 22f, 0, 1f, true, 0.12f, 0.3f, 1.5f, 4f, 0.05f, 0.12f, 0.45f, 0.3f, 0.5f, false).gameObject;
        }

        /// <summary>Looping motes drifting upward: an aura.</summary>
        public static GameObject AttachMotes(Transform parent, Vector3 localPosition, Color colour)
        {
            Color end = colour; end.a = 0.1f;
            return Particles("Motes", localPosition, parent, colour, end, 16f, 0, 1f, true, 0.6f, 1.1f, 0.4f, 1.1f, 0.06f, 0.14f, 0.5f, -0.25f, 0.6f, false).gameObject;
        }

        // ------------------------------------------------------------------
        // Shapes that grow and fade

        /// <summary>A sphere that swells to <paramref name="radius"/> and fades: shockwaves, EMP, flashbangs.</summary>
        public static void Sphere(Vector3 position, float radius, Color colour, float seconds)
        {
            var go = Prim.Ball(null, "Fx Sphere", Vector3.zero, Vector3.one * 0.1f, null);
            go.transform.position = position;
            FxFade.Begin(go, colour, Vector3.one * 0.2f, Vector3.one * (radius * 2f), seconds);
        }

        /// <summary>A ring on the ground that runs out to <paramref name="radius"/>: scans, pulses, area warnings.</summary>
        public static void Ring(Vector3 position, float radius, Color colour, float seconds, float width = 0.12f)
        {
            var go = new GameObject("Fx Ring");
            go.transform.position = position;
            var line = go.AddComponent<LineRenderer>();
            line.useWorldSpace = false;
            line.loop = true;
            line.positionCount = FxRing.Segments;
            line.widthMultiplier = width;
            line.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            line.receiveShadows = false;
            line.alignment = LineAlignment.View;
            var ring = go.AddComponent<FxRing>();
            ring.Begin(line, colour, radius, seconds);
        }

        /// <summary>A short-lived light, with nothing drawn: sells an explosion or a muzzle flash on the walls around it.</summary>
        public static void Flash(Vector3 position, Color colour, float range, float intensity, float seconds)
        {
            var go = new GameObject("Fx Flash");
            go.transform.position = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = colour;
            light.range = range;
            light.intensity = intensity;
            light.shadows = LightShadows.None;
            go.AddComponent<FxFade>().BeginLight(light, seconds);
        }

        // ------------------------------------------------------------------
        // Recipes

        public static void Explosion(Vector3 position, float radius)
        {
            float r = Mathf.Max(1.5f, radius);
            Sphere(position, r * 0.55f, new Color(1f, 0.8f, 0.4f, 0.7f), 0.22f);
            Sphere(position, r, new Color(1f, 0.6f, 0.2f, 0.22f), 0.4f);
            Ring(position + Vector3.up * 0.05f, r, new Color(1f, 0.75f, 0.35f, 0.8f), 0.4f, 0.2f);
            Color hot = new Color(1f, 0.85f, 0.35f, 1f), ash = new Color(0.25f, 0.2f, 0.18f, 0.6f);
            Particles("Fx Blast", position, null, hot, ash, 0f, 46, 0.9f, false, 0.4f, 0.95f, r * 1.2f, r * 3.2f, 0.25f, 0.8f, 0.3f, 0.5f, 1.6f, true);
            Smoke(position, r * 0.5f, 1.6f, new Color(0.3f, 0.28f, 0.27f));
            Flash(position + Vector3.up * 0.5f, FireColour, r * 4f, 6f, 0.3f);
            Core.Juice.ShakeAt(position, 0.11f, r * 4f + 12f, 0.4f); // (v0.15) felt, not just seen
        }

        public static void MuzzleFlash(Vector3 position, Vector3 direction, float scale = 1f)
        {
            var go = Prim.Ball(null, "Fx Muzzle", Vector3.zero, Vector3.one * 0.05f, null);
            go.transform.position = position;
            go.transform.rotation = Quaternion.LookRotation(direction.sqrMagnitude > 0.001f ? direction : Vector3.forward);
            FxFade.Begin(go, new Color(1f, 0.85f, 0.45f, 0.95f), new Vector3(0.1f, 0.1f, 0.22f) * scale, new Vector3(0.2f, 0.2f, 0.5f) * scale, 0.06f);
            Flash(position, new Color(1f, 0.8f, 0.5f), 5f * scale, 2.2f, 0.06f);
        }

        public static void Impact(Vector3 position)
        {
            Burst(position, new Color(1f, 0.85f, 0.5f, 1f), 7, 3.5f, 0.07f, 0.25f, 1.2f);
        }

        public static void Shock(Vector3 position, float radius, Color colour, float seconds = 0.5f)
        {
            Color shell = colour; shell.a = 0.22f;
            Color rim = colour; rim.a = 0.9f;
            Sphere(position, radius, shell, seconds);
            Ring(position + Vector3.up * 0.1f, radius, rim, seconds, 0.16f);
            Burst(position + Vector3.up * 0.8f, rim, 26, radius * 1.4f, 0.1f, 0.4f, 0f);
            Flash(position + Vector3.up, colour, radius * 2f, 4f, 0.25f);
        }
    }

    /// <summary>Grows a shape, fades its colour to nothing and destroys it. Also fades a light.</summary>
    public class FxFade : MonoBehaviour
    {
        private Material material;
        private Color colour;
        private Vector3 fromScale, toScale;
        private Light fadingLight;
        private float lightIntensity, life, age;

        public static void Begin(GameObject go, Color colour, Vector3 fromScale, Vector3 toScale, float seconds)
        {
            var fade = go.AddComponent<FxFade>();
            fade.material = ArtMaterials.TranslucentInstance(colour);
            fade.colour = colour;
            fade.fromScale = fromScale;
            fade.toScale = toScale;
            fade.life = Mathf.Max(0.02f, seconds);
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.sharedMaterial = fade.material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            go.transform.localScale = fromScale;
        }

        public void BeginLight(Light light, float seconds)
        {
            fadingLight = light;
            lightIntensity = light.intensity;
            life = Mathf.Max(0.02f, seconds);
        }

        private void Update()
        {
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / life);
            if (fadingLight != null) fadingLight.intensity = lightIntensity * (1f - t);
            if (material != null)
            {
                float eased = 1f - (1f - t) * (1f - t); // fast out, slow settle
                transform.localScale = Vector3.Lerp(fromScale, toScale, eased);
                Color c = colour; c.a = colour.a * (1f - t);
                material.color = c;
            }
            if (t >= 1f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }

    /// <summary>A circle drawn with a line that runs outward and fades.</summary>
    public class FxRing : MonoBehaviour
    {
        public const int Segments = 48;
        private LineRenderer line;
        private Material material;
        private Color colour;
        private float radius, life, age;
        private bool hold; // when true the ring stays at full radius until Release()

        public void Begin(LineRenderer renderer, Color ringColour, float targetRadius, float seconds)
        {
            line = renderer;
            colour = ringColour;
            radius = targetRadius;
            life = Mathf.Max(0.02f, seconds);
            material = ArtMaterials.TranslucentInstance(ringColour);
            line.sharedMaterial = material;
            Draw(0.05f);
        }

        /// <summary>Keeps the ring at full size, not fading, until the object is destroyed. For lasting area markers.</summary>
        public void Hold() { hold = true; Draw(radius); }

        private void Draw(float r)
        {
            for (int i = 0; i < Segments; i++)
            {
                float a = i * Mathf.PI * 2f / Segments;
                line.SetPosition(i, new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r));
            }
        }

        private void Update()
        {
            if (hold) return;
            age += Time.deltaTime;
            float t = Mathf.Clamp01(age / life);
            float eased = 1f - (1f - t) * (1f - t);
            Draw(Mathf.Lerp(0.05f, radius, eased));
            Color c = colour; c.a = colour.a * (1f - t * t);
            material.color = c;
            if (t >= 1f) Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
