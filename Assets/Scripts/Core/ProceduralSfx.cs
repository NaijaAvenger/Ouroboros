using System;
using UnityEngine;

namespace Ouroboros.Core
{
    /// <summary>
    /// Placeholder sound effects synthesised in code (v0.9), so every gameplay event is audible before any audio
    /// asset exists. Feedback components use these only when the matching <c>FeedbackLibrary</c> slot is empty;
    /// assign a real clip there and the placeholder is never played. Clips are built once, on first use.
    /// </summary>
    public static class ProceduralSfx
    {
        private const int SampleRate = 44100;

        private static AudioClip gunshot, reload, hurt, death, hitTick, headshotTick, killTick, explosion, guardShot, emptyClick;

        /// <summary>Short noise crack with a low thump.</summary>
        public static AudioClip Gunshot => gunshot != null ? gunshot : (gunshot = Make("sfx_gunshot", 0.16f, 11,
            (t, n) => n * Decay(t, 34f) * 0.85f + Sine(95f, t) * Decay(t, 22f) * 0.6f));

        /// <summary>Duller, lower shot so guard fire is told apart from player fire by ear.</summary>
        public static AudioClip GuardShot => guardShot != null ? guardShot : (guardShot = Make("sfx_guard_shot", 0.2f, 12,
            (t, n) => n * Decay(t, 20f) * 0.6f + Sine(70f, t) * Decay(t, 16f) * 0.7f));

        /// <summary>Two mechanical clicks.</summary>
        public static AudioClip Reload => reload != null ? reload : (reload = Make("sfx_reload", 0.32f, 13,
            (t, n) => Click(t, 0.02f, n) + Click(t, 0.22f, n) * 0.8f));

        public static AudioClip EmptyClick => emptyClick != null ? emptyClick : (emptyClick = Make("sfx_empty", 0.06f, 14,
            (t, n) => Click(t, 0.005f, n) * 0.7f));

        /// <summary>Low body thud.</summary>
        public static AudioClip Hurt => hurt != null ? hurt : (hurt = Make("sfx_hurt", 0.18f, 15,
            (t, n) => Sine(150f - 300f * t, t) * Decay(t, 18f) * 0.8f + n * Decay(t, 45f) * 0.25f));

        /// <summary>Falling tone.</summary>
        public static AudioClip Death => death != null ? death : (death = Make("sfx_death", 0.6f, 16,
            (t, n) => Sine(320f - 380f * t, t) * Decay(t, 5f) * 0.6f + n * Decay(t, 9f) * 0.15f));

        /// <summary>Hit marker: a short, bright tick.</summary>
        public static AudioClip HitTick => hitTick != null ? hitTick : (hitTick = Make("sfx_hit", 0.05f, 17,
            (t, n) => Sine(1900f, t) * Decay(t, 70f) * 0.5f));

        /// <summary>Headshot marker: higher and a touch longer than the body tick.</summary>
        public static AudioClip HeadshotTick => headshotTick != null ? headshotTick : (headshotTick = Make("sfx_headshot", 0.09f, 18,
            (t, n) => (Sine(2600f, t) + Sine(3900f, t) * 0.5f) * Decay(t, 45f) * 0.45f));

        /// <summary>Kill confirm: two rising notes.</summary>
        public static AudioClip KillTick => killTick != null ? killTick : (killTick = Make("sfx_kill", 0.22f, 19,
            (t, n) => Sine(880f, t) * Window(t, 0f, 0.09f) * 0.5f + Sine(1320f, t) * Window(t, 0.09f, 0.22f) * 0.5f));

        public static AudioClip Explosion => explosion != null ? explosion : (explosion = Make("sfx_explosion", 0.9f, 20,
            (t, n) => n * Decay(t, 5f) * 0.9f + Sine(55f, t) * Decay(t, 4f) * 0.8f));

        // ------------------------------------------------------------------

        /// <summary>Builds a mono clip from a per-sample function of (time in seconds, white noise in -1..1).</summary>
        private static AudioClip Make(string name, float seconds, int seed, Func<float, float, float> sample)
        {
            int count = Mathf.Max(1, Mathf.RoundToInt(seconds * SampleRate));
            var data = new float[count];
            var rng = new System.Random(seed);
            for (int i = 0; i < count; i++)
            {
                float t = (float)i / SampleRate;
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0);
                float fadeOut = Mathf.Clamp01((count - i) / 200f); // no click at the end
                data[i] = Mathf.Clamp(sample(t, noise) * fadeOut, -1f, 1f);
            }
            var clip = AudioClip.Create(name, count, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private static float Sine(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);
        private static float Decay(float t, float rate) => Mathf.Exp(-rate * t);
        private static float Window(float t, float from, float to) => t >= from && t < to ? Decay(t - from, 14f) : 0f;
        private static float Click(float t, float at, float noise) => t >= at ? noise * Decay(t - at, 160f) : 0f;
    }
}
