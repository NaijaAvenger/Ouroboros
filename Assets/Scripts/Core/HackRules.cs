using System;

namespace Ouroboros.Core
{
    /// <summary>
    /// Pure hacking arithmetic (v0.11, milestone M3). No Unity or Fusion types; unit-tested in Tools/tests.
    ///
    /// Two kinds of hack share these rules:
    ///  - <b>Quick hack</b>: aim at a device and hold Interact. A tether links you to it for a second or two.
    ///  - <b>Deep hack</b>: jack into a vault and solve sign-matching puzzles. Each right answer drives the
    ///    crack forward; a rival jacked into the same vault drives it back and can take it over.
    ///
    /// A "sign" is eight marks, each open or closed: one byte. The puzzle shows a target sign and four
    /// candidates; exactly one candidate matches. Client and server build the same puzzle from the same seed,
    /// so the server can check an answer without trusting the client.
    /// </summary>
    public static class HackRules
    {
        public const int MarkCount = 8;
        public const int CandidateCount = 4;
        /// <summary>0 = decoys differ from the target by three marks, 2 = by a single mark.</summary>
        public const int MaxDifficulty = 2;

        // ------------------------------------------------------------------
        // Sign puzzles

        /// <summary>
        /// Builds puzzle number <paramref name="step"/> of a session. Fills <paramref name="candidates"/>
        /// (length <see cref="CandidateCount"/>) with signs, one of which equals <paramref name="target"/>.
        /// </summary>
        public static void BuildPuzzle(int seed, int step, int difficulty, int[] candidates, out int target, out int correctIndex)
        {
            if (candidates == null || candidates.Length < CandidateCount) throw new ArgumentException("candidates must hold " + CandidateCount + " entries");

            uint state = Mix(unchecked((uint)seed * 2654435761u) ^ unchecked((uint)step * 40503u + 0x9E3779B9u));
            target = (int)(Next(ref state) & 0xFF);
            correctIndex = (int)(Next(ref state) % CandidateCount);

            int flips = DecoyDifference(difficulty);
            for (int i = 0; i < CandidateCount; i++)
            {
                if (i == correctIndex) { candidates[i] = target; continue; }

                // Flip `flips` distinct marks; retry until this decoy is different from every earlier candidate.
                int decoy;
                int guard = 0;
                do
                {
                    decoy = target;
                    int flipped = 0, used = 0;
                    while (flipped < flips)
                    {
                        int bit = (int)(Next(ref state) % MarkCount);
                        if ((used & (1 << bit)) != 0) continue;
                        used |= 1 << bit;
                        decoy ^= 1 << bit;
                        flipped++;
                    }
                }
                while (Contains(candidates, i, correctIndex, target, decoy) && ++guard < 64);
                candidates[i] = decoy;
            }
        }

        /// <summary>True when <paramref name="choice"/> is the matching candidate of that puzzle.</summary>
        public static bool IsCorrect(int seed, int step, int difficulty, int choice)
        {
            var scratch = new int[CandidateCount];
            BuildPuzzle(seed, step, difficulty, scratch, out _, out int correct);
            return choice == correct;
        }

        /// <summary>How many marks a decoy differs from the target by at a given difficulty.</summary>
        public static int DecoyDifference(int difficulty)
        {
            int d = difficulty < 0 ? 0 : (difficulty > MaxDifficulty ? MaxDifficulty : difficulty);
            return 3 - d;
        }

        /// <summary>Number of marks that differ between two signs.</summary>
        public static int MarkDifference(int a, int b)
        {
            int x = (a ^ b) & 0xFF, count = 0;
            while (x != 0) { count += x & 1; x >>= 1; }
            return count;
        }

        /// <summary>Whether mark <paramref name="index"/> (0..7) of <paramref name="sign"/> is closed.</summary>
        public static bool IsMarkClosed(int sign, int index) => ((sign >> index) & 1) != 0;

        /// <summary>The hack specialist reads signs one difficulty step easier than everyone else.</summary>
        public static int EffectiveDifficulty(int stationDifficulty, bool specialist)
        {
            int d = stationDifficulty - (specialist ? 1 : 0);
            return d < 0 ? 0 : (d > MaxDifficulty ? MaxDifficulty : d);
        }

        // ------------------------------------------------------------------
        // Pulse puzzles (v0.12): the second deep-hack task. A marker sweeps back and forth along a bar; strike
        // while it is inside the lit window. Signs test reading, pulses test timing, and a vault can mix both.

        public enum PuzzleKind
        {
            Signs,
            Pulse
        }

        /// <summary>Station setting: which puzzle kinds a vault serves.</summary>
        public const int ModeSigns = 0, ModePulse = 1, ModeMixed = 2;

        /// <summary>Which puzzle is shown at <paramref name="step"/>. Mixed vaults alternate, starting on either kind.</summary>
        public static PuzzleKind KindFor(int mode, int seed, int step)
        {
            if (mode == ModeSigns) return PuzzleKind.Signs;
            if (mode == ModePulse) return PuzzleKind.Pulse;
            int first = (int)(Mix(unchecked((uint)seed)) & 1u);
            return ((step + first) & 1) == 0 ? PuzzleKind.Signs : PuzzleKind.Pulse;
        }

        /// <summary>Seconds for the marker to cross the bar and come back. Faster on harder vaults.</summary>
        public static float PulsePeriodSeconds(int difficulty)
        {
            int d = difficulty < 0 ? 0 : (difficulty > MaxDifficulty ? MaxDifficulty : difficulty);
            return 1.6f - 0.25f * d;
        }

        /// <summary>Width of the window as a fraction of the bar. Narrower on harder vaults.</summary>
        public static float PulseWindowWidth(int difficulty)
        {
            int d = difficulty < 0 ? 0 : (difficulty > MaxDifficulty ? MaxDifficulty : difficulty);
            return 0.24f - 0.05f * d;
        }

        /// <summary>Centre of the window (0..1 along the bar), kept clear of both ends.</summary>
        public static float PulseWindowCentre(int seed, int step)
        {
            uint state = Mix(unchecked((uint)seed * 2246822519u) ^ unchecked((uint)step * 3266489917u + 0x85EBCA6Bu));
            float r = (Next(ref state) >> 8) / 16777216f;
            return 0.18f + r * 0.64f;
        }

        /// <summary>
        /// Marker position (0..1) a given time after the puzzle appeared. A triangle wave with a per-puzzle
        /// starting phase, so the right moment is never "the same beat as last time".
        /// </summary>
        public static float PulsePosition(int seed, int step, float secondsSinceStart, float periodSeconds)
        {
            if (periodSeconds <= 0f) return 0f;
            uint state = Mix(unchecked((uint)seed * 374761393u) ^ unchecked((uint)step * 668265263u + 0xC2B2AE35u));
            float phase = (Next(ref state) >> 8) / 16777216f;
            float t = secondsSinceStart / periodSeconds + phase;
            t -= (float)Math.Floor(t);
            return t < 0.5f ? t * 2f : 2f - t * 2f;
        }

        /// <summary>True when a strike at <paramref name="position"/> lands in the window. <paramref name="tolerance"/> widens it a little for latency.</summary>
        public static bool IsPulseHit(float position, float centre, float width, float tolerance = 0f)
        {
            return Math.Abs(position - centre) <= width * 0.5f + Math.Max(0f, tolerance);
        }

        // ------------------------------------------------------------------
        // Jamming a rival (v0.12): a quick hack whose target is a player

        /// <summary>Seconds a jammed player cannot use abilities, hack or interact.</summary>
        public const float JamSeconds = 4f;
        /// <summary>Seconds their weapons refuse to fire: shorter, so a jam opens a window rather than deciding the fight.</summary>
        public const float JamWeaponSeconds = 2f;
        /// <summary>Seconds after being jammed during which the same player cannot be jammed again.</summary>
        public const float JamImmunitySeconds = 14f;
        /// <summary>Seconds of tether needed on a moving, shooting target.</summary>
        public const float JamHackSeconds = 2.2f;

        // ------------------------------------------------------------------
        // Deep hack progress (in the same seconds as the vault's capture time)

        /// <summary>Progress added by one right answer: a bit over a fifth of the crack, nearly a third for the specialist.</summary>
        public static float CorrectGain(float captureTime, bool specialist) => captureTime * (specialist ? 0.30f : 0.22f);

        /// <summary>Progress lost by one wrong answer.</summary>
        public static float WrongLoss(float captureTime) => captureTime * 0.08f;

        /// <summary>
        /// Progress per second from simply being jacked in. Slow on purpose (about three times the capture
        /// time alone): it guarantees the vault opens eventually, while answering signs is what makes it fast.
        /// A rival standing in the zone stops it.
        /// </summary>
        public static float PassiveRate(int workers, bool contested)
        {
            if (contested || workers <= 0) return 0f;
            return 0.35f * MatchRules.CrackRate(workers);
        }

        /// <summary>Progress drained per second once nobody is jacked in.</summary>
        public const float AbandonDecayPerSecond = 0.5f;

        /// <summary>
        /// A rival's right answer pushes the owner's progress back by <paramref name="gain"/>. If that takes it
        /// below zero the vault changes hands and the remainder becomes the new owner's progress.
        /// </summary>
        public static void ApplyCounterHack(float ownerProgress, float gain, out float newProgress, out bool changedHands)
        {
            float p = ownerProgress - Math.Max(0f, gain);
            changedHands = p < 0f;
            newProgress = changedHands ? -p : p;
        }

        // ------------------------------------------------------------------
        // Quick hacks

        /// <summary>Seconds to hold the tether. Halved for the specialist, and halved again while chaining.</summary>
        public static float QuickHackSeconds(float deviceSeconds, bool specialist, bool chained)
        {
            float s = Math.Max(0f, deviceSeconds);
            if (specialist) s *= 0.5f;
            if (chained) s *= 0.5f;
            return Math.Max(0.15f, s);
        }

        public static float QuickHackRange(float baseRange, bool specialist) => Math.Max(0f, baseRange) * (specialist ? 1.6f : 1f);

        /// <summary>
        /// How far off-centre a device may be and still be the thing you are aiming at. Generous up close
        /// (a device at arm's length fills the view), tight at range.
        /// </summary>
        public static float AimConeDegrees(float distance)
        {
            if (distance <= 0.01f) return 35f;
            double degrees = Math.Asin(Math.Min(1.0, 0.75 / distance)) * (180.0 / Math.PI) + 2.0;
            return (float)Math.Max(4.0, Math.Min(35.0, degrees));
        }

        /// <summary>
        /// Ranks candidate devices: lower is better, negative means "not a valid target". The angle dominates;
        /// distance only breaks ties between things in the same direction.
        /// </summary>
        public static float AimScore(float angleDegrees, float distance, float range)
        {
            if (distance > range || angleDegrees > AimConeDegrees(distance)) return -1f;
            return angleDegrees + distance * 0.02f;
        }

        // ------------------------------------------------------------------

        private static bool Contains(int[] candidates, int filledBefore, int correctIndex, int target, int value)
        {
            if (value == target) return true;
            for (int i = 0; i < filledBefore; i++)
            {
                if (i != correctIndex && candidates[i] == value) return true;
            }
            return false;
        }

        private static uint Next(ref uint state)
        {
            state ^= state << 13; state ^= state >> 17; state ^= state << 5; // xorshift32
            return state;
        }

        private static uint Mix(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
            return x == 0 ? 0x1234567u : x; // xorshift must not start at zero
        }
    }
}
