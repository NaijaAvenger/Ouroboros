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
        // (v0.20) Movement, money and crew cues. The same rule as above: placeholders until real audio exists.

        private static AudioClip[] steps, boots;
        private static AudioClip land, slide, pickup, coinDrop, cashStart, cashBanked, cashAlarm, cashTick, liftDing, pingBlip, pingEnemy, reviveChime;

        /// <summary>A player's footfall on a hard floor. Four takes, so a run does not sound like a machine.</summary>
        public static AudioClip Footstep(int take)
        {
            if (steps == null)
            {
                steps = new AudioClip[4];
                for (int i = 0; i < steps.Length; i++)
                {
                    float tone = 105f + i * 9f, snap = 58f + i * 6f;
                    steps[i] = Make("sfx_step_" + i, 0.12f, 31 + i,
                        (t, n) => n * Decay(t, snap) * 0.5f + Sine(tone - 180f * t, t) * Decay(t, 40f) * 0.55f);
                }
            }
            return steps[((take % steps.Length) + steps.Length) % steps.Length];
        }

        /// <summary>A guard's boot: lower and heavier than a player's step, so the two are told apart by ear.</summary>
        public static AudioClip BootStep(int take)
        {
            if (boots == null)
            {
                boots = new AudioClip[3];
                for (int i = 0; i < boots.Length; i++)
                {
                    float tone = 74f + i * 6f;
                    boots[i] = Make("sfx_boot_" + i, 0.16f, 41 + i,
                        (t, n) => n * Decay(t, 42f) * 0.42f + Sine(tone - 110f * t, t) * Decay(t, 28f) * 0.75f);
                }
            }
            return boots[((take % boots.Length) + boots.Length) % boots.Length];
        }

        /// <summary>Landing from a drop: a thud with some grit.</summary>
        public static AudioClip Land => land != null ? land : (land = Make("sfx_land", 0.3f, 45,
            (t, n) => n * Decay(t, 22f) * 0.6f + Sine(72f - 90f * t, t) * Decay(t, 14f) * 0.9f));

        /// <summary>The scrape of a slide.</summary>
        public static AudioClip Slide => slide != null ? slide : (slide = Make("sfx_slide", 0.55f, 46,
            (t, n) => n * Decay(t, 4.5f) * 0.32f * (0.6f + 0.4f * Sine(27f, t))));

        /// <summary>Loot picked up: two quick bright notes.</summary>
        public static AudioClip Pickup => pickup != null ? pickup : (pickup = Make("sfx_pickup", 0.2f, 47,
            (t, n) => Bell(t, 0f, 1320f, 26f) * 0.35f + Bell(t, 0.06f, 1760f, 20f) * 0.35f));

        /// <summary>Loot going into a station: three coins on metal.</summary>
        public static AudioClip CoinDrop => coinDrop != null ? coinDrop : (coinDrop = Make("sfx_coin_drop", 0.45f, 48,
            (t, n) => Bell(t, 0f, 2100f, 22f) * 0.3f + Bell(t, 0.08f, 2650f, 24f) * 0.28f + Bell(t, 0.17f, 2350f, 18f) * 0.3f));

        /// <summary>A cash-out starting: the drawer of a till, then its bell.</summary>
        public static AudioClip CashStart => cashStart != null ? cashStart : (cashStart = Make("sfx_cash_start", 0.8f, 49,
            (t, n) => Click(t, 0.005f, n) * 0.8f + Click(t, 0.07f, n) * 0.5f + Bell(t, 0.1f, 1568f, 5f) * 0.42f + Bell(t, 0.1f, 2349f, 7f) * 0.2f));

        /// <summary>Money banked: a rising run of four bells and a shimmer. The best sound in the game should be this one.</summary>
        public static AudioClip CashBanked => cashBanked != null ? cashBanked : (cashBanked = Make("sfx_cash_banked", 1.25f, 50,
            (t, n) => Bell(t, 0f, 1047f, 6f) * 0.3f + Bell(t, 0.09f, 1319f, 6f) * 0.3f + Bell(t, 0.18f, 1568f, 6f) * 0.3f
                    + Bell(t, 0.27f, 2093f, 3.5f) * 0.36f + (t > 0.27f ? n * Decay(t - 0.27f, 7f) * 0.07f : 0f)));

        /// <summary>A cash-out stolen: a two-tone alarm.</summary>
        public static AudioClip CashAlarm => cashAlarm != null ? cashAlarm : (cashAlarm = Make("sfx_cash_alarm", 0.72f, 51,
            (t, n) => Mathf.Sign(Sine(((int)(t / 0.12f) % 2 == 0) ? 740f : 554f, t)) * 0.2f * Mathf.Clamp01((0.72f - t) * 8f)));

        /// <summary>The last seconds of a cash-out, one per second.</summary>
        public static AudioClip CashTick => cashTick != null ? cashTick : (cashTick = Make("sfx_cash_tick", 0.07f, 52,
            (t, n) => Sine(1250f, t) * Decay(t, 55f) * 0.5f + Click(t, 0f, n) * 0.3f));

        /// <summary>A lift arriving: two soft bells.</summary>
        public static AudioClip LiftDing => liftDing != null ? liftDing : (liftDing = Make("sfx_lift_ding", 1.2f, 53,
            (t, n) => Bell(t, 0f, 1319f, 3.2f) * 0.4f + Bell(t, 0.28f, 1047f, 2.8f) * 0.4f));

        /// <summary>A crewmate's ping.</summary>
        public static AudioClip PingBlip => pingBlip != null ? pingBlip : (pingBlip = Make("sfx_ping", 0.16f, 54,
            (t, n) => Sine(1480f, t) * Window(t, 0f, 0.05f) * 0.4f + Sine(1976f, t) * Window(t, 0.05f, 0.16f) * 0.4f));

        /// <summary>A crewmate's ping on a threat: lower and falling, so it is told apart without looking.</summary>
        public static AudioClip PingEnemy => pingEnemy != null ? pingEnemy : (pingEnemy = Make("sfx_ping_enemy", 0.2f, 55,
            (t, n) => Sine(880f, t) * Window(t, 0f, 0.07f) * 0.45f + Sine(660f, t) * Window(t, 0.07f, 0.2f) * 0.45f));

        /// <summary>A crewmate back on their feet: three rising notes.</summary>
        public static AudioClip ReviveChime => reviveChime != null ? reviveChime : (reviveChime = Make("sfx_revive", 0.7f, 56,
            (t, n) => Bell(t, 0f, 660f, 7f) * 0.32f + Bell(t, 0.11f, 880f, 7f) * 0.32f + Bell(t, 0.22f, 1320f, 4.5f) * 0.36f));

        // ---- (v0.21) Grand Vaults
        private static AudioClip drillBuzz, vaultOpen, keyJingle, perkChord;

        /// <summary>Half a second of drill: a rasp over a motor hum. Played end to end while a drill runs.</summary>
        public static AudioClip DrillBuzz => drillBuzz != null ? drillBuzz : (drillBuzz = Make("sfx_drill", 0.5f, 61,
            (t, n) => (Mathf.Sign(Sine(118f, t)) * 0.16f + n * 0.22f * (0.6f + 0.4f * Sine(31f, t))) * Mathf.Clamp01(t * 30f) * Mathf.Clamp01((0.5f - t) * 30f)));

        /// <summary>A vault door giving way: bolts, a deep roll, and a bell over it.</summary>
        public static AudioClip VaultOpen => vaultOpen != null ? vaultOpen : (vaultOpen = Make("sfx_vault_open", 2.2f, 62,
            (t, n) => Click(t, 0.01f, n) * 0.9f + Click(t, 0.16f, n) * 0.8f + Click(t, 0.3f, n) * 0.7f
                    + (t > 0.35f ? (Sine(48f, t) * 0.6f + n * 0.18f) * Decay(t - 0.35f, 1.6f) : 0f)
                    + Bell(t, 0.5f, 523f, 2.2f) * 0.22f + Bell(t, 0.5f, 784f, 2.6f) * 0.16f));

        /// <summary>A key picked up: metal on metal, twice.</summary>
        public static AudioClip KeyJingle => keyJingle != null ? keyJingle : (keyJingle = Make("sfx_key", 0.4f, 63,
            (t, n) => Bell(t, 0f, 3100f, 30f) * 0.3f + Bell(t, 0.07f, 3700f, 26f) * 0.28f + Bell(t, 0.16f, 2800f, 20f) * 0.26f));

        /// <summary>A perk taken: a held chord that swells.</summary>
        public static AudioClip PerkChord => perkChord != null ? perkChord : (perkChord = Make("sfx_perk", 1.4f, 64,
            (t, n) => (Sine(392f, t) + Sine(494f, t) + Sine(587f, t) + 0.5f * Sine(784f, t)) * 0.13f * Mathf.Clamp01(t * 6f) * Decay(t, 2.2f)));

        /// <summary>A struck note starting at <paramref name="at"/>: a tone, an inharmonic overtone, and a decay.</summary>
        private static float Bell(float t, float at, float hz, float decay)
        {
            if (t < at) return 0f;
            float u = t - at;
            return (Sine(hz, u) + 0.4f * Sine(hz * 2.76f, u)) * Decay(u, decay);
        }

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
