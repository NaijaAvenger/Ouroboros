using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Rules.Tests
{
    public class ProfileRulesTests
    {
        [Test]
        public void MatchXp_RewardsGettingOutMoreThanKilling()
        {
            int quiet = ProfileRules.MatchXp(3000, extracted: true, won: true, kills: 0);
            int loud = ProfileRules.MatchXp(0, extracted: false, won: false, kills: 10);
            Assert.That(quiet, Is.GreaterThan(loud));
        }

        [Test]
        public void MatchXp_IsNeverNegativeAndIsCapped()
        {
            Assert.That(ProfileRules.MatchXp(-500, false, false, -3), Is.EqualTo(50));
            Assert.That(ProfileRules.MatchXp(10_000_000, true, true, 999), Is.EqualTo(2500));
        }

        [Test]
        public void Levels_StartAtOneAndRiseWithXp()
        {
            Assert.That(ProfileRules.LevelForXp(0), Is.EqualTo(1));
            Assert.That(ProfileRules.XpToReach(1), Is.EqualTo(0));
            Assert.That(ProfileRules.XpToReach(2), Is.EqualTo(500));
            Assert.That(ProfileRules.LevelForXp(499), Is.EqualTo(1));
            Assert.That(ProfileRules.LevelForXp(500), Is.EqualTo(2));
        }

        [Test]
        public void XpToReach_IsTheSumOfTheStepsBelowIt()
        {
            int total = 0;
            for (int level = 1; level < ProfileRules.MaxLevel; level++)
            {
                Assert.That(ProfileRules.XpToReach(level), Is.EqualTo(total), "level " + level);
                Assert.That(ProfileRules.LevelForXp(total), Is.EqualTo(level));
                total += ProfileRules.XpForNextLevel(level);
            }
        }

        [Test]
        public void Levels_StopAtTheCap()
        {
            Assert.That(ProfileRules.LevelForXp(int.MaxValue / 2), Is.EqualTo(ProfileRules.MaxLevel));
            Assert.That(ProfileRules.LevelProgress(int.MaxValue / 2), Is.EqualTo(1f));
        }

        [Test]
        public void LevelProgress_RunsFromZeroToOneInsideALevel()
        {
            Assert.That(ProfileRules.LevelProgress(0), Is.EqualTo(0f));
            Assert.That(ProfileRules.LevelProgress(250), Is.EqualTo(0.5f).Within(1e-4f));
            Assert.That(ProfileRules.LevelProgress(500), Is.EqualTo(0f).Within(1e-4f));
        }

        [Test]
        public void Titles_ChangeAtTheirLevelsAndNeverGoBackwards()
        {
            Assert.That(ProfileRules.TitleForLevel(1), Is.EqualTo("Runner"));
            Assert.That(ProfileRules.TitleForLevel(3), Is.EqualTo("Lookout"));
            Assert.That(ProfileRules.NextTitleLevel(1), Is.EqualTo(3));
            Assert.That(ProfileRules.NextTitleLevel(45), Is.EqualTo(0));
            for (int level = 1; level < ProfileRules.MaxLevel; level++)
            {
                int next = ProfileRules.NextTitleLevel(level);
                if (next == 0) continue;
                Assert.That(ProfileRules.TitleForLevel(next), Is.Not.EqualTo(ProfileRules.TitleForLevel(next - 1)), "a new title at level " + next);
            }
        }

        [TestCase("  Ada  ", "Ada")]
        [TestCase("Ada   Lovelace", "Ada Lovelace")]
        [TestCase("<b>Ada</b>", "bAdab")]
        [TestCase("", "Runner")]
        [TestCase("   ", "Runner")]
        [TestCase("!!!", "Runner")]
        [TestCase("crew_one-2", "crew_one-2")]
        public void SanitiseName_KeepsOnlySafeCharacters(string raw, string expected)
        {
            Assert.That(ProfileRules.SanitiseName(raw), Is.EqualTo(expected));
        }

        [Test]
        public void SanitiseName_CutsLongNames()
        {
            string name = ProfileRules.SanitiseName(new string('a', 80));
            Assert.That(name.Length, Is.EqualTo(ProfileRules.MaxNameLength));
        }

        [Test]
        public void PartyCode_IsStableValidAndVaried()
        {
            var seen = new System.Collections.Generic.HashSet<string>();
            for (int seed = 0; seed < 500; seed++)
            {
                string code = ProfileRules.PartyCode(seed);
                Assert.That(code, Is.EqualTo(ProfileRules.PartyCode(seed)));
                Assert.That(ProfileRules.IsValidCode(code), Is.True, code);
                seen.Add(code);
            }
            Assert.That(seen.Count, Is.GreaterThan(490), "codes from different seeds should almost never collide");
        }

        [TestCase("ab2cd", "AB2CD")]
        [TestCase(" a-b c d e f", "ABCDE")]
        [TestCase("o0i1x", "QQLLX")]
        [TestCase("", "")]
        public void NormaliseCode_AcceptsWhatPeopleType(string raw, string expected)
        {
            Assert.That(ProfileRules.NormaliseCode(raw), Is.EqualTo(expected));
        }

        [Test]
        public void IsValidCode_RejectsShortAndForeignCodes()
        {
            Assert.That(ProfileRules.IsValidCode("ABCD"), Is.False);
            Assert.That(ProfileRules.IsValidCode("ABCDO"), Is.False);
            Assert.That(ProfileRules.IsValidCode(null), Is.False);
        }
    }
}
