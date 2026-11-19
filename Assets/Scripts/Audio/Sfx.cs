using UnityEngine;

namespace Ouroboros.Audio
{
    /// <summary>
    /// Plays a sound at a place in the world so that it is heard from where it happened and from no further
    /// than it should be (v0.23).
    ///
    /// Unity's own <c>AudioSource.PlayClipAtPoint</c> uses a logarithmic falloff that never reaches silence
    /// before 500 m, so every shot, crate and coin on the map was faintly audible everywhere, and together
    /// they made a constant wash of noise. This uses a linear falloff to a stated distance instead: full
    /// volume close up, nothing at all beyond the reach. Each kind of sound has its own reach.
    /// </summary>
    public static class Sfx
    {
        /// <summary>How far each kind of sound carries, in metres.</summary>
        public const float Whisper = 14f;   // reloads, pickups, key jingles
        public const float Close = 22f;     // being hurt, a revive, a countdown tick, a guard's boots
        public const float Room = 32f;      // a till, coins, a lift bell, a drill, a crate breaking
        public const float Shot = 48f;      // gunfire: the next room and the one after, not the far wing
        public const float Blast = 75f;     // explosions, a wall coming down, a vault door

        /// <summary>
        /// Plays <paramref name="clip"/> at <paramref name="position"/>, audible out to <paramref name="reach"/> metres.
        /// </summary>
        public static void PlayAt(AudioClip clip, Vector3 position, float volume = 1f, float reach = Room, float pitch = 1f)
        {
            if (clip == null || volume <= 0f) return;
            var go = new GameObject("Sfx");
            go.transform.position = position;
            var source = go.AddComponent<AudioSource>();
            Configure(source, reach);
            source.clip = clip;
            source.volume = Mathf.Clamp01(volume);
            source.pitch = pitch;
            source.Play();
            Object.Destroy(go, clip.length / Mathf.Max(0.1f, Mathf.Abs(pitch)) + 0.15f);
        }

        /// <summary>Sets a source up as a positional one that is silent beyond <paramref name="reach"/> metres.</summary>
        public static void Configure(AudioSource source, float reach)
        {
            if (source == null) return;
            source.playOnAwake = false;
            source.spatialBlend = 1f;
            source.rolloffMode = AudioRolloffMode.Linear;
            source.minDistance = Mathf.Min(2.5f, reach * 0.12f);
            source.maxDistance = Mathf.Max(3f, reach);
            source.dopplerLevel = 0f;
            source.spread = 20f; // a little width, so a sound a metre away is not hard in one ear
        }
    }
}
