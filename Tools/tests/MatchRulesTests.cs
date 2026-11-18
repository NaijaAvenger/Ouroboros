using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Tests
{
    public class MatchRulesTests
    {
        private static readonly bool[] AllTeams = { true, true, true, true };

        // ---- PickWinner ----

        [Test]
        public void PickWinner_HighestScoreWins()
        {
            int winner = MatchRules.PickWinner(new[] { 100, 900, 300, 0 }, new[] { 0, 0, 0, 0 }, new[] { 0, 0, 0, 0 }, AllTeams);
            Assert.That(winner, Is.EqualTo(1));
        }

        [Test]
        public void PickWinner_ScoreTie_GoesToMoreBankedLoot()
        {
            int winner = MatchRules.PickWinner(new[] { 500, 500, 0, 0 }, new[] { 200, 800, 0, 0 }, new[] { 4, 1, 0, 0 }, AllTeams);
            Assert.That(winner, Is.EqualTo(1));
        }

        [Test]
        public void PickWinner_ScoreAndLootTie_GoesToMoreExtractedMembers()
        {
            int winner = MatchRules.PickWinner(new[] { 500, 500, 500, 0 }, new[] { 300, 300, 300, 0 }, new[] { 1, 2, 3, 0 }, AllTeams);
            Assert.That(winner, Is.EqualTo(2));
        }

        [Test]
        public void PickWinner_FullTie_GoesToLowestIndex()
        {
            int winner = MatchRules.PickWinner(new[] { 0, 0, 0, 0 }, new[] { 0, 0, 0, 0 }, new[] { 0, 0, 0, 0 }, AllTeams);
            Assert.That(winner, Is.EqualTo(0));
        }

        [Test]
        public void PickWinner_IgnoresTeamsWithoutPlayers()
        {
            // Team 1 has the best numbers but nobody on it (e.g. everyone left).
            int winner = MatchRules.PickWinner(new[] { 100, 900, 300, 0 }, new[] { 0, 0, 0, 0 }, new[] { 0, 0, 0, 0 },
                new[] { true, false, true, false });
            Assert.That(winner, Is.EqualTo(2));
        }

        [Test]
        public void PickWinner_NoTeamsWithPlayers_ReturnsMinusOne()
        {
            int winner = MatchRules.PickWinner(new[] { 5, 5, 5, 5 }, new[] { 0, 0, 0, 0 }, new[] { 0, 0, 0, 0 },
                new[] { false, false, false, false });
            Assert.That(winner, Is.EqualTo(-1));
        }

        [Test]
        public void PickWinner_ASingleTeamWithZeroScoreStillWins()
        {
            // Solo dev match: one team, nothing scored. Someone must still be named.
            int winner = MatchRules.PickWinner(new[] { 0, 0, 0, 0 }, new[] { 0, 0, 0, 0 }, new[] { 0, 0, 0, 0 },
                new[] { false, false, true, false });
            Assert.That(winner, Is.EqualTo(2));
        }

        [Test]
        public void PickWinner_NullHasPlayers_TreatsEveryTeamAsActive()
        {
            int winner = MatchRules.PickWinner(new[] { 1, 2, 3, 4 }, null, null, null);
            Assert.That(winner, Is.EqualTo(3));
        }

        // ---- AlarmTier (default GameModeConfig thresholds: 25 / 50 / 80) ----

        [TestCase(0f, 0)]
        [TestCase(24.9f, 0)]
        [TestCase(25f, 1)]
        [TestCase(49.9f, 1)]
        [TestCase(50f, 2)]
        [TestCase(79.9f, 2)]
        [TestCase(80f, 3)]
        [TestCase(100f, 3)]
        public void AlarmTier_ThresholdsAreInclusive(float level, int expectedTier)
        {
            Assert.That(MatchRules.AlarmTier(level, 25f, 50f, 80f), Is.EqualTo(expectedTier));
        }

        // ---- SplitLoot ----

        [TestCase(500, 1, 500, 0)]
        [TestCase(500, 2, 250, 0)]
        [TestCase(500, 3, 166, 2)]
        [TestCase(500, 4, 125, 0)]
        [TestCase(1, 4, 0, 1)]
        public void SplitLoot_NothingIsLost(int total, int workers, int expectedShare, int expectedRemainder)
        {
            MatchRules.SplitLoot(total, workers, out int share, out int remainder);
            Assert.That(share, Is.EqualTo(expectedShare));
            Assert.That(remainder, Is.EqualTo(expectedRemainder));
            Assert.That(share * workers + remainder, Is.EqualTo(total));
        }

        [Test]
        public void SplitLoot_NoWorkers_DoesNotDivideByZero()
        {
            MatchRules.SplitLoot(500, 0, out int share, out int remainder);
            Assert.That(share, Is.EqualTo(500));
            Assert.That(remainder, Is.EqualTo(0));
        }

        // ---- CrackRate ----

        [TestCase(0, 1.00f)]
        [TestCase(1, 1.00f)]
        [TestCase(2, 1.25f)]
        [TestCase(3, 1.50f)]
        [TestCase(4, 1.75f)]
        [TestCase(16, 1.75f)]
        public void CrackRate_ExtraWorkersHelpUpToThree(int workers, float expected)
        {
            Assert.That(MatchRules.CrackRate(workers), Is.EqualTo(expected).Within(1e-5f));
        }

        // ---- CarrySpeedMultiplier (default config: 25% at 1500 loot, case 0.7) ----

        [TestCase(0, false, 1.000f)]
        [TestCase(750, false, 0.875f)]
        [TestCase(1500, false, 0.750f)]
        [TestCase(9000, false, 0.750f)]   // capped at the reference amount
        [TestCase(0, true, 0.700f)]
        [TestCase(1500, true, 0.525f)]
        public void CarrySpeedMultiplier_DefaultConfig(int loot, bool carryingCase, float expected)
        {
            Assert.That(MatchRules.CarrySpeedMultiplier(loot, 1500, 0.25f, carryingCase, 0.7f), Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void CarrySpeedMultiplier_NeverBelowThirtyPercent()
        {
            Assert.That(MatchRules.CarrySpeedMultiplier(5000, 1000, 0.9f, true, 0.2f), Is.EqualTo(0.3f).Within(1e-5f));
        }

        [Test]
        public void CarrySpeedMultiplier_ZeroReference_DisablesLootWeight()
        {
            Assert.That(MatchRules.CarrySpeedMultiplier(5000, 0, 0.25f, false, 0.7f), Is.EqualTo(1f).Within(1e-5f));
        }

        // ---- LowestSetBit ----

        [TestCase(0, -1)]
        [TestCase(-4, -1)]
        [TestCase(1, 0)]
        [TestCase(6, 1)]
        [TestCase(1 << 14, 14)]
        public void LowestSetBit(int flags, int expected)
        {
            Assert.That(MatchRules.LowestSetBit(flags), Is.EqualTo(expected));
        }
    }
}
