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

        // ---- The Run (v0.10): quota, richest crew, payouts, wipes, fair contests ----

        [TestCase(2999, 3000, false)]
        [TestCase(3000, 3000, true)]
        [TestCase(9000, 3000, true)]
        [TestCase(9000, 0, false)]     // quota 0 switches the rule off
        public void QuotaReached(int carried, int quota, bool expected)
        {
            Assert.That(MatchRules.QuotaReached(carried, quota), Is.EqualTo(expected));
        }

        [Test]
        public void RichestTeam_IsTheCrewCarryingTheMost()
        {
            Assert.That(MatchRules.RichestTeam(new[] { 200, 1500, 900, 0 }, AllTeams), Is.EqualTo(1));
        }

        [Test]
        public void RichestTeam_NobodyCarrying_IsNobody()
        {
            Assert.That(MatchRules.RichestTeam(new[] { 0, 0, 0, 0 }, AllTeams), Is.EqualTo(-1));
        }

        [Test]
        public void RichestTeam_IgnoresEmptyCrews_AndTiesGoToTheLowerIndex()
        {
            Assert.That(MatchRules.RichestTeam(new[] { 500, 900, 900, 0 }, new[] { true, false, true, true }), Is.EqualTo(2));
            Assert.That(MatchRules.RichestTeam(new[] { 700, 700, 0, 0 }, AllTeams), Is.EqualTo(0));
        }

        [TestCase(1000, 1.0f, 1000)]
        [TestCase(1000, 0.6f, 600)]
        [TestCase(333, 0.5f, 167)]     // rounds half away from zero
        [TestCase(0, 1.0f, 0)]
        [TestCase(500, -2f, 0)]
        public void BankedValue_AppliesTheExitPayout(int carried, float payout, int expected)
        {
            Assert.That(MatchRules.BankedValue(carried, payout), Is.EqualTo(expected));
        }

        [Test]
        public void BankedValue_SideExitNeverBeatsTheCentralGetaway()
        {
            for (int v = 0; v <= 5000; v += 137)
            {
                Assert.That(MatchRules.BankedValue(v, 0.6f), Is.LessThanOrEqualTo(MatchRules.BankedValue(v, 1f)));
            }
        }

        [Test]
        public void ResolveCrewWipe_RedeploysUntilTheLimit_ThenEliminates()
        {
            Assert.That(MatchRules.ResolveCrewWipe(wipesSoFar: 0, maxWipes: 1), Is.EqualTo(MatchRules.WipeOutcome.Redeploy));
            Assert.That(MatchRules.ResolveCrewWipe(wipesSoFar: 1, maxWipes: 1), Is.EqualTo(MatchRules.WipeOutcome.Eliminated));
            Assert.That(MatchRules.ResolveCrewWipe(wipesSoFar: 0, maxWipes: 0), Is.EqualTo(MatchRules.WipeOutcome.Eliminated));
            Assert.That(MatchRules.ResolveCrewWipe(wipesSoFar: 50, maxWipes: -1), Is.EqualTo(MatchRules.WipeOutcome.Redeploy));
        }

        [Test]
        public void RotatedIndex_VisitsEveryTeamOnce_AndTheFirstTeamChangesEachTick()
        {
            var firsts = new System.Collections.Generic.HashSet<int>();
            for (int tick = 100; tick < 104; tick++)
            {
                var seen = new System.Collections.Generic.HashSet<int>();
                for (int step = 0; step < 4; step++) seen.Add(MatchRules.RotatedIndex(tick, step, 4));
                Assert.That(seen.Count, Is.EqualTo(4), "every team must be considered exactly once per scan");
                firsts.Add(MatchRules.RotatedIndex(tick, 0, 4));
            }
            Assert.That(firsts.Count, Is.EqualTo(4), "over four ticks each team gets to go first once");
        }

        [Test]
        public void RotatedIndex_HandlesNegativeTicksAndEmptyCounts()
        {
            Assert.That(MatchRules.RotatedIndex(-3, 0, 4), Is.InRange(0, 3));
            Assert.That(MatchRules.RotatedIndex(5, 2, 0), Is.EqualTo(0));
        }

        [Test]
        public void PickDistinct_ReturnsDistinctIndicesInsideThePool()
        {
            var result = new int[4];
            for (int seed = 0; seed < 500; seed++)
            {
                int n = MatchRules.PickDistinct(seed, 9, 4, result);
                Assert.That(n, Is.EqualTo(4));
                Assert.That(new System.Collections.Generic.HashSet<int>(result).Count, Is.EqualTo(4), $"seed {seed} repeated an index");
                foreach (int i in result) Assert.That(i, Is.InRange(0, 8));
            }
        }

        [Test]
        public void PickDistinct_IsDeterministic_AndVariesWithTheSeed()
        {
            var a = new int[3]; var b = new int[3];
            MatchRules.PickDistinct(42, 12, 3, a);
            MatchRules.PickDistinct(42, 12, 3, b);
            Assert.That(a, Is.EqualTo(b));

            var seen = new System.Collections.Generic.HashSet<string>();
            for (int seed = 0; seed < 60; seed++)
            {
                MatchRules.PickDistinct(seed, 12, 3, a);
                seen.Add(string.Join(",", a));
            }
            Assert.That(seen.Count, Is.GreaterThan(20), "different vaults should not all hold the same items");
        }

        // ---- cash-out stations (v0.16)

        [Test]
        public void CrewHaul_CountsBankedMoneyOnlyWhenTheRuleIsOn()
        {
            Assert.That(MatchRules.CrewHaul(400, 1500, true), Is.EqualTo(1900));
            Assert.That(MatchRules.CrewHaul(400, 1500, false), Is.EqualTo(400));
            Assert.That(MatchRules.CrewHaul(-5, -10, true), Is.EqualTo(0), "bad values never count against a crew");
        }

        [Test]
        public void CrewHaul_CashingOut_NeverMovesACrewAwayFromTheQuota()
        {
            int before = MatchRules.CrewHaul(1200, 800, true);
            int after = MatchRules.CrewHaul(0, 800 + MatchRules.BankedValue(1200, 1f), true);
            Assert.That(after, Is.EqualTo(before));
        }

        [Test]
        public void CashOutTimeAfterDeposit_ExtendsButNeverPastAFullTimer()
        {
            Assert.That(MatchRules.CashOutTimeAfterDeposit(20f, 60f, 10f), Is.EqualTo(30f));
            Assert.That(MatchRules.CashOutTimeAfterDeposit(55f, 60f, 10f), Is.EqualTo(60f));
            Assert.That(MatchRules.CashOutTimeAfterDeposit(-3f, 60f, 10f), Is.EqualTo(10f));
            Assert.That(MatchRules.CashOutTimeAfterDeposit(20f, 60f, -10f), Is.EqualTo(20f));
        }

        [Test]
        public void CashOutTimeAfterHijack_TheThiefMustHoldItForAWhile()
        {
            Assert.That(MatchRules.CashOutTimeAfterHijack(2f, 18f), Is.EqualTo(18f), "no stealing a cash-out one second before it banks");
            Assert.That(MatchRules.CashOutTimeAfterHijack(40f, 18f), Is.EqualTo(40f), "a hijack never shortens the timer");
            Assert.That(MatchRules.CashOutTimeAfterHijack(-1f, -1f), Is.EqualTo(0f));
        }

        [Test]
        public void HijackStep_FillsWhileWorked_AndCompletesInTheStatedTime()
        {
            float p = 0f;
            for (int i = 0; i < 44; i++) p = MatchRules.HijackStep(p, true, false, 0.1f, 4.5f, 0.5f);
            Assert.That(p, Is.LessThan(1f));
            for (int i = 0; i < 2; i++) p = MatchRules.HijackStep(p, true, false, 0.1f, 4.5f, 0.5f);
            Assert.That(p, Is.EqualTo(1f));
        }

        [Test]
        public void HijackStep_HoldsWhileContested_AndDrainsWhenLetGo()
        {
            Assert.That(MatchRules.HijackStep(0.4f, true, true, 1f, 4.5f, 0.5f), Is.EqualTo(0.4f), "an owner at the station stops it");
            Assert.That(MatchRules.HijackStep(0.4f, false, false, 0.2f, 4.5f, 0.5f), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(MatchRules.HijackStep(0.1f, false, true, 1f, 4.5f, 0.5f), Is.EqualTo(0f));
            Assert.That(MatchRules.HijackStep(0.4f, true, false, 0f, 4.5f, 0.5f), Is.EqualTo(0.4f));
            Assert.That(MatchRules.HijackStep(0.4f, true, false, 1f, 0f, 0.5f), Is.EqualTo(0.4f), "a zero duration never divides by zero");
            Assert.That(MatchRules.HijackStep(7f, false, false, 0f, 4.5f, 0.5f), Is.EqualTo(1f));
        }

        [Test]
        public void CashOutCountdown_FreezesDuringAHijack()
        {
            Assert.That(MatchRules.CashOutCountdown(10f, false, 0.5f), Is.EqualTo(9.5f));
            Assert.That(MatchRules.CashOutCountdown(10f, true, 0.5f), Is.EqualTo(10f));
            Assert.That(MatchRules.CashOutCountdown(0.2f, false, 0.5f), Is.EqualTo(0f));
            Assert.That(MatchRules.CashOutCountdown(-4f, true, 0.5f), Is.EqualTo(0f));
        }

        [Test]
        public void PickDistinct_SmallPool_ReturnsWhatThereIs()
        {
            var result = new int[5];
            Assert.That(MatchRules.PickDistinct(7, 2, 5, result), Is.EqualTo(2));
            Assert.That(MatchRules.PickDistinct(7, 0, 5, result), Is.EqualTo(0));
        }
    }
}
