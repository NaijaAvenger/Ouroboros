using System.Collections.Generic;
using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Tests
{
    public class HackRulesTests
    {
        // ---- Sign puzzles ----

        [Test]
        public void Puzzle_HasExactlyOneMatchingCandidate_AndDistinctDecoys()
        {
            var c = new int[HackRules.CandidateCount];
            for (int difficulty = 0; difficulty <= HackRules.MaxDifficulty; difficulty++)
            {
                for (int seed = -200; seed < 800; seed += 7)
                {
                    for (int step = 0; step < 6; step++)
                    {
                        HackRules.BuildPuzzle(seed, step, difficulty, c, out int target, out int correct);

                        Assert.That(correct, Is.InRange(0, HackRules.CandidateCount - 1));
                        Assert.That(c[correct], Is.EqualTo(target));
                        int matches = 0;
                        foreach (int sign in c) { Assert.That(sign, Is.InRange(0, 255)); if (sign == target) matches++; }
                        Assert.That(matches, Is.EqualTo(1), $"seed {seed} step {step}: more than one candidate matches");
                        Assert.That(new HashSet<int>(c).Count, Is.EqualTo(HackRules.CandidateCount), $"seed {seed} step {step}: duplicate candidates");
                    }
                }
            }
        }

        [TestCase(0, 3)]
        [TestCase(1, 2)]
        [TestCase(2, 1)]
        public void Puzzle_DecoysDifferByTheDifficultysMarkCount(int difficulty, int expectedDifference)
        {
            var c = new int[HackRules.CandidateCount];
            for (int seed = 0; seed < 300; seed++)
            {
                HackRules.BuildPuzzle(seed, 0, difficulty, c, out int target, out int correct);
                for (int i = 0; i < c.Length; i++)
                {
                    if (i == correct) continue;
                    Assert.That(HackRules.MarkDifference(c[i], target), Is.EqualTo(expectedDifference));
                }
            }
        }

        [Test]
        public void Puzzle_IsTheSameOnClientAndServer()
        {
            var a = new int[HackRules.CandidateCount];
            var b = new int[HackRules.CandidateCount];
            HackRules.BuildPuzzle(987654, 3, 1, a, out int targetA, out int correctA);
            HackRules.BuildPuzzle(987654, 3, 1, b, out int targetB, out int correctB);
            Assert.That(a, Is.EqualTo(b));
            Assert.That(targetA, Is.EqualTo(targetB));
            Assert.That(correctA, Is.EqualTo(correctB));
        }

        [Test]
        public void Puzzle_ChangesEveryStep_AndTheAnswerIsNotAlwaysInTheSamePlace()
        {
            var c = new int[HackRules.CandidateCount];
            var targets = new HashSet<int>();
            var positions = new HashSet<int>();
            for (int step = 0; step < 40; step++)
            {
                HackRules.BuildPuzzle(1234, step, 0, c, out int target, out int correct);
                targets.Add(target);
                positions.Add(correct);
            }
            Assert.That(targets.Count, Is.GreaterThan(25));
            Assert.That(positions.Count, Is.EqualTo(HackRules.CandidateCount), "every answer slot should come up");
        }

        [Test]
        public void IsCorrect_AcceptsOnlyTheMatchingCandidate()
        {
            var c = new int[HackRules.CandidateCount];
            for (int seed = 0; seed < 100; seed++)
            {
                HackRules.BuildPuzzle(seed, 2, 1, c, out _, out int correct);
                for (int choice = -1; choice <= HackRules.CandidateCount; choice++)
                {
                    Assert.That(HackRules.IsCorrect(seed, 2, 1, choice), Is.EqualTo(choice == correct));
                }
            }
        }

        [Test]
        public void GuessingAtRandom_LosesGround()
        {
            // One in four right, three in four wrong: mashing keys must be worse than doing nothing.
            const float captureTime = 8f;
            float expected = 0.25f * HackRules.CorrectGain(captureTime, false) - 0.75f * HackRules.WrongLoss(captureTime);
            Assert.That(expected, Is.LessThan(0f));
        }

        [Test]
        public void Marks_ReadBackBitByBit()
        {
            Assert.That(HackRules.IsMarkClosed(0b0000_0101, 0), Is.True);
            Assert.That(HackRules.IsMarkClosed(0b0000_0101, 1), Is.False);
            Assert.That(HackRules.IsMarkClosed(0b0000_0101, 2), Is.True);
            Assert.That(HackRules.MarkDifference(0b1111_0000, 0b0000_1111), Is.EqualTo(8));
            Assert.That(HackRules.MarkDifference(77, 77), Is.EqualTo(0));
        }

        [Test]
        public void Specialist_ReadsOneStepEasier_ButNeverBelowTheEasiest()
        {
            Assert.That(HackRules.EffectiveDifficulty(2, false), Is.EqualTo(2));
            Assert.That(HackRules.EffectiveDifficulty(2, true), Is.EqualTo(1));
            Assert.That(HackRules.EffectiveDifficulty(0, true), Is.EqualTo(0));
            Assert.That(HackRules.EffectiveDifficulty(9, false), Is.EqualTo(HackRules.MaxDifficulty));
        }

        // ---- Deep hack progress ----

        [Test]
        public void FiveRightAnswers_CrackAVault_FourForTheSpecialist()
        {
            const float captureTime = 8f;
            Assert.That(5 * HackRules.CorrectGain(captureTime, false), Is.GreaterThanOrEqualTo(captureTime));
            Assert.That(4 * HackRules.CorrectGain(captureTime, false), Is.LessThan(captureTime));
            Assert.That(4 * HackRules.CorrectGain(captureTime, true), Is.GreaterThanOrEqualTo(captureTime));
        }

        [Test]
        public void PassiveRate_IsSlow_StopsWhenContested_AndGrowsWithWorkers()
        {
            Assert.That(HackRules.PassiveRate(1, false), Is.EqualTo(0.35f).Within(1e-5f));
            Assert.That(HackRules.PassiveRate(3, false), Is.GreaterThan(HackRules.PassiveRate(1, false)));
            Assert.That(HackRules.PassiveRate(3, true), Is.EqualTo(0f));
            Assert.That(HackRules.PassiveRate(0, false), Is.EqualTo(0f));
        }

        [Test]
        public void CounterHack_PushesProgressBack()
        {
            HackRules.ApplyCounterHack(5f, 2f, out float progress, out bool changedHands);
            Assert.That(progress, Is.EqualTo(3f).Within(1e-5f));
            Assert.That(changedHands, Is.False);
        }

        [Test]
        public void CounterHack_PastZero_TakesTheVaultOver_WithTheRemainder()
        {
            HackRules.ApplyCounterHack(1.5f, 2f, out float progress, out bool changedHands);
            Assert.That(changedHands, Is.True);
            Assert.That(progress, Is.EqualTo(0.5f).Within(1e-5f));
        }

        [Test]
        public void CounterHack_ExactlyToZero_DoesNotChangeHands()
        {
            HackRules.ApplyCounterHack(2f, 2f, out float progress, out bool changedHands);
            Assert.That(changedHands, Is.False);
            Assert.That(progress, Is.EqualTo(0f).Within(1e-5f));
        }

        // ---- Quick hacks ----

        [Test]
        public void QuickHackSeconds_SpecialistAndChainingEachHalveIt()
        {
            Assert.That(HackRules.QuickHackSeconds(2f, false, false), Is.EqualTo(2f).Within(1e-5f));
            Assert.That(HackRules.QuickHackSeconds(2f, true, false), Is.EqualTo(1f).Within(1e-5f));
            Assert.That(HackRules.QuickHackSeconds(2f, true, true), Is.EqualTo(0.5f).Within(1e-5f));
            Assert.That(HackRules.QuickHackSeconds(0.1f, true, true), Is.EqualTo(0.15f).Within(1e-5f), "never instant");
        }

        [Test]
        public void QuickHackRange_IsLongerForTheSpecialist()
        {
            Assert.That(HackRules.QuickHackRange(18f, false), Is.EqualTo(18f).Within(1e-5f));
            Assert.That(HackRules.QuickHackRange(18f, true), Is.GreaterThan(18f));
        }

        [Test]
        public void AimCone_IsWideUpClose_AndNarrowAtRange()
        {
            float close = HackRules.AimConeDegrees(1.5f);
            float mid = HackRules.AimConeDegrees(8f);
            float far = HackRules.AimConeDegrees(30f);
            Assert.That(close, Is.GreaterThan(mid));
            Assert.That(mid, Is.GreaterThan(far));
            Assert.That(far, Is.GreaterThanOrEqualTo(4f));
            Assert.That(HackRules.AimConeDegrees(0f), Is.LessThanOrEqualTo(35f));
        }

        [Test]
        public void AimScore_RejectsOutOfRangeAndOffAim_AndPrefersTheBetterAimedDevice()
        {
            Assert.That(HackRules.AimScore(1f, 50f, 18f), Is.LessThan(0f), "out of range");
            Assert.That(HackRules.AimScore(40f, 10f, 18f), Is.LessThan(0f), "not being aimed at");

            float onTarget = HackRules.AimScore(1f, 15f, 18f);
            float nearMiss = HackRules.AimScore(4f, 6f, 18f);
            Assert.That(onTarget, Is.GreaterThanOrEqualTo(0f));
            Assert.That(nearMiss, Is.GreaterThanOrEqualTo(0f));
            Assert.That(onTarget, Is.LessThan(nearMiss), "aim beats proximity");
        }
    }
}
