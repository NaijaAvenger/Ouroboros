using System;
using System.Collections;
using UnityEngine;

namespace Ouroboros.Audio
{
    /// <summary>
    /// Music that follows the heist (v0.12, milestone M4). Four loops of the same length are synthesised in
    /// code at start-up and played in sync; their volumes track the alarm tier and the match state, so the
    /// score thickens as things go wrong:
    ///
    ///   0  air      a held chord and a shaker                         always (lobby, quiet heist)
    ///   1  pulse    kick on every beat and a sub bass                 once the match is live
    ///   2  log      a pitched, sliding log-drum line and a clap       alarm Suspicious and up
    ///   3  alarm    off-beat stabs, a drum roll and a riser           Lockdown and the getaway
    ///
    /// The groove (about 113 BPM, a syncopated low drum that carries the tune) borrows from amapiano. It is a
    /// placeholder made of sine waves and noise: the point is the layering system and the mood, and every
    /// layer is meant to be replaced by a commissioned stem of the same length. Assign clips to
    /// <see cref="layerOverrides"/> to do that; anything left empty keeps its synthesised loop.
    /// Added at runtime by <c>UI.MatchFeedback</c>.
    /// </summary>
    public class MusicDirector : MonoBehaviour
    {
        public const int LayerCount = 4;
        private const int SampleRate = 44100;
        /// <summary>Samples per sixteenth note: 113 BPM to the nearest sample, so every layer is exactly the same length.</summary>
        private const int Step = 5854;
        private const int StepsPerBar = 16, Bars = 4;
        private const int Length = Step * StepsPerBar * Bars;

        [Range(0f, 1f)] [SerializeField] private float masterVolume = 0.3f;
        [SerializeField] private bool playMusic = true;
        [Tooltip("Optional real stems, in layer order (air, pulse, log, alarm). All must be the same length to stay in sync.")]
        [SerializeField] private AudioClip[] layerOverrides = new AudioClip[LayerCount];
        [Tooltip("How fast a layer fades in or out, in volume per second.")]
        [SerializeField] private float fadeSpeed = 0.6f;

        private readonly AudioSource[] sources = new AudioSource[LayerCount];
        private readonly float[] volumes = new float[LayerCount];
        private bool started;

        // Bar chords (root, fifth, seventh, ninth-ish), in Hz. A minor, A minor, F, G.
        private static readonly float[][] Chords =
        {
            new[] { 110.00f, 164.81f, 196.00f, 261.63f },
            new[] { 110.00f, 164.81f, 196.00f, 246.94f },
            new[] { 87.31f, 130.81f, 164.81f, 220.00f },
            new[] { 98.00f, 146.83f, 174.61f, 246.94f },
        };
        // The log drum: step in the bar, and which note of the scale. Syncopated against the kick.
        private static readonly int[] LogSteps = { 0, 3, 6, 10, 12, 14 };
        private static readonly float[] LogScale = { 55.00f, 65.41f, 73.42f, 82.41f, 98.00f }; // A minor pentatonic, low
        private static readonly int[][] LogNotes =
        {
            new[] { 0, 0, 2, 1, 0, 3 },
            new[] { 0, 0, 2, 1, 3, 4 },
            new[] { 1, 1, 0, 2, 1, 0 },
            new[] { 2, 2, 3, 4, 3, 2 },
        };

        private void Start()
        {
            if (!playMusic) return;
            for (int i = 0; i < LayerCount; i++)
            {
                var src = gameObject.AddComponent<AudioSource>();
                src.playOnAwake = false;
                src.loop = true;
                src.spatialBlend = 0f;
                src.volume = 0f;
                src.priority = 0;
                sources[i] = src;
            }
            StartCoroutine(BuildAndPlay());
        }

        private IEnumerator BuildAndPlay()
        {
            for (int i = 0; i < LayerCount; i++)
            {
                var clip = layerOverrides != null && i < layerOverrides.Length ? layerOverrides[i] : null;
                if (clip == null)
                {
                    var data = new float[Length];
                    switch (i)
                    {
                        case 0: BuildAir(data); break;
                        case 1: BuildPulse(data); break;
                        case 2: BuildLog(data); break;
                        default: BuildAlarm(data); break;
                    }
                    for (int s = 0; s < data.Length; s++) data[s] = Mathf.Clamp(data[s], -1f, 1f);
                    clip = AudioClip.Create("music_layer_" + i, Length, 1, SampleRate, false);
                    clip.SetData(data, 0);
                }
                sources[i].clip = clip;
                yield return null; // one layer per frame: no hitch on scene start
            }

            double at = AudioSettings.dspTime + 0.25;
            for (int i = 0; i < LayerCount; i++) sources[i].PlayScheduled(at);
            started = true;
        }

        private void Update()
        {
            if (!started) return;

            float air = 1f, pulse = 0f, log = 0f, alarm = 0f;
            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (gm != null && gm.Object != null && gm.IsMatchLive)
            {
                var alarmSystem = GameMode.AlarmSystem.Instance;
                int tier = alarmSystem != null && alarmSystem.Object != null ? alarmSystem.Tier : 0;
                bool getaway = gm.IsGetaway;
                pulse = 0.85f;
                log = getaway || tier >= 2 ? 1f : (tier == 1 ? 0.6f : 0f);
                alarm = getaway || tier >= 3 ? 1f : 0f;
            }

            Fade(0, air * 0.8f);
            Fade(1, pulse);
            Fade(2, log);
            Fade(3, alarm);
        }

        private void Fade(int layer, float target)
        {
            volumes[layer] = Mathf.MoveTowards(volumes[layer], target, fadeSpeed * Time.unscaledDeltaTime);
            // [v0.12] sources[layer].volume = volumes[layer] * masterVolume;
            sources[layer].volume = volumes[layer] * masterVolume * (Core.GameSettings.MusicVolume / 0.7f); // (v0.13) the music slider; 0.7 is its default
        }

        // ------------------------------------------------------------------
        // Synthesis. Sounds are added with a wrapped index, so a tail that runs past the end of the loop
        // continues at its start and the loop has no seam.

        private static void Add(float[] buffer, int start, int count, Func<float, float> sample)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[(start + i) % Length] += sample((float)i / SampleRate);
            }
        }

        private static float Sin(float hz, float t) => Mathf.Sin(2f * Mathf.PI * hz * t);

        /// <summary>Layer 0: a soft chord that swells in and out each bar, and a shaker on the sixteenths.</summary>
        private static void BuildAir(float[] b)
        {
            int bar = Step * StepsPerBar;
            float barSeconds = (float)bar / SampleRate;
            for (int n = 0; n < Bars; n++)
            {
                foreach (float hz in Chords[n])
                {
                    float f = hz;
                    Add(b, n * bar, bar, t =>
                    {
                        float swell = Mathf.Sin(Mathf.PI * t / barSeconds); // 0 at both bar lines: no clicks
                        return (Sin(f, t) + 0.5f * Sin(f * 1.003f, t)) * 0.035f * swell;
                    });
                }
            }

            var rng = new System.Random(101);
            float previous = 0f;
            for (int s = 0; s < StepsPerBar * Bars; s++)
            {
                float level = s % 4 == 2 ? 0.085f : (s % 2 == 0 ? 0.04f : 0.055f);
                Add(b, s * Step, Step, t =>
                {
                    float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                    float bright = white - previous; // crude high-pass: a shaker, not a hiss
                    previous = white;
                    return bright * level * Mathf.Exp(-t * 38f);
                });
            }
        }

        /// <summary>Layer 1: a rounded kick on every beat, and a sub bass that ducks under it.</summary>
        private static void BuildPulse(float[] b)
        {
            int beat = Step * 4;
            for (int k = 0; k < Bars * 4; k++)
            {
                Add(b, k * beat, (int)(0.3f * SampleRate), t =>
                {
                    // Pitch falls from 124 Hz to 46 Hz. The phase is the integral of that curve, so the sweep is smooth.
                    float phase = 2f * Mathf.PI * (46f * t + 78f / 34f * (1f - Mathf.Exp(-t * 34f)));
                    return Mathf.Sin(phase) * Mathf.Exp(-t * 15f) * 0.55f;
                });
            }

            int bar = Step * StepsPerBar;
            float barSeconds = (float)bar / SampleRate;
            float beatSeconds = (float)beat / SampleRate;
            for (int n = 0; n < Bars; n++)
            {
                float root = Chords[n][0] * 0.5f;
                Add(b, n * bar, bar, t =>
                {
                    float sinceKick = t % beatSeconds;
                    float duck = 1f - 0.75f * Mathf.Exp(-sinceKick * 11f);
                    float edge = Mathf.Min(1f, Mathf.Min(t, barSeconds - t) * 40f); // 25 ms fades at the bar lines
                    return Sin(root, t) * 0.2f * duck * edge;
                });
            }
        }

        /// <summary>Layer 2: the log drum (a low note that slides down as it is struck) and a clap on two and four.</summary>
        private static void BuildLog(float[] b)
        {
            int bar = Step * StepsPerBar;
            for (int n = 0; n < Bars; n++)
            {
                for (int h = 0; h < LogSteps.Length; h++)
                {
                    float hz = LogScale[LogNotes[n][h]] * 2f;
                    Add(b, n * bar + LogSteps[h] * Step, (int)(0.55f * SampleRate), t =>
                    {
                        // Pitch starts 60% sharp and falls to the note in a few hundredths of a second.
                        float phase = 2f * Mathf.PI * hz * (t + 0.6f / 30f * (1f - Mathf.Exp(-t * 30f)));
                        float body = Mathf.Sin(phase) + 0.3f * Mathf.Sin(phase * 2f);
                        float env = (1f - Mathf.Exp(-t * 400f)) * Mathf.Exp(-t * 7.5f);
                        return (float)Math.Tanh(body * 2.2f) * env * 0.26f;
                    });
                }
            }

            var rng = new System.Random(202);
            for (int n = 0; n < Bars; n++)
            {
                foreach (int s in new[] { 4, 12 })
                {
                    Add(b, n * bar + s * Step, (int)(0.16f * SampleRate), t =>
                    {
                        // Three quick bursts: several hands, not one.
                        float env = Mathf.Exp(-t * 60f) + 0.8f * Mathf.Exp(-Mathf.Max(0f, t - 0.011f) * 60f) * (t > 0.011f ? 1f : 0f)
                                  + 0.7f * Mathf.Exp(-Mathf.Max(0f, t - 0.024f) * 28f) * (t > 0.024f ? 1f : 0f);
                        return (float)(rng.NextDouble() * 2.0 - 1.0) * env * 0.11f;
                    });
                }
            }
        }

        /// <summary>Layer 3: short chords on the off-beats, a drum roll into the turnaround, and a rising noise under the last two bars.</summary>
        private static void BuildAlarm(float[] b)
        {
            int bar = Step * StepsPerBar;
            for (int n = 0; n < Bars; n++)
            {
                foreach (int s in new[] { 2, 6, 10, 14 })
                {
                    foreach (float hz in Chords[n])
                    {
                        float f = hz * 2f;
                        Add(b, n * bar + s * Step, (int)(0.2f * SampleRate), t =>
                            (Sin(f, t) + 0.33f * Sin(f * 3f, t) + 0.2f * Sin(f * 5f, t)) * Mathf.Exp(-t * 24f) * 0.045f);
                    }
                }
            }

            // Roll: the last eight sixteenths, getting louder.
            for (int s = 0; s < 8; s++)
            {
                float level = 0.08f + 0.02f * s;
                Add(b, 3 * bar + (8 + s) * Step, (int)(0.14f * SampleRate), t =>
                {
                    float phase = 2f * Mathf.PI * (95f * t + 110f / 26f * (1f - Mathf.Exp(-t * 26f)));
                    return Mathf.Sin(phase) * Mathf.Exp(-t * 24f) * level;
                });
            }

            // Riser across bars three and four.
            var rng = new System.Random(303);
            int riserLength = bar * 2;
            float riserSeconds = (float)riserLength / SampleRate;
            Add(b, 2 * bar, riserLength, t =>
            {
                float x = t / riserSeconds;
                float tail = Mathf.Min(1f, (riserSeconds - t) * 30f); // quick close so it does not click into bar one
                return ((float)(rng.NextDouble() * 2.0 - 1.0) * 0.05f + Sin(300f + 500f * x, t) * 0.02f) * x * x * tail;
            });
        }
    }
}
