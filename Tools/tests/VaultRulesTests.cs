using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Rules.Tests
{
    public class VaultRulesTests
    {
        [Test]
        public void Perks_AreSeparateBits_AndNoneIsNothing()
        {
            int mask = 0;
            Assert.That(VaultRules.PerkBit(VaultPerk.None), Is.EqualTo(0));
            foreach (VaultPerk perk in System.Enum.GetValues(typeof(VaultPerk)))
            {
                if (perk == VaultPerk.None) continue;
                Assert.That(VaultRules.Has(mask, perk), Is.False);
                Assert.That(mask & VaultRules.PerkBit(perk), Is.EqualTo(0), "every perk has its own bit");
                mask = VaultRules.With(mask, perk);
                Assert.That(VaultRules.Has(mask, perk), Is.True);
            }
            Assert.That(VaultRules.Has(mask, VaultPerk.None), Is.False);
            Assert.That(VaultRules.With(mask, VaultPerk.None), Is.EqualTo(mask));
        }

        [Test]
        public void Perks_OnlyChangeWhatTheySayTheyChange()
        {
            int iron = VaultRules.With(0, VaultPerk.IronSkin);
            Assert.That(VaultRules.DamageTaken(iron), Is.EqualTo(0.8f));
            Assert.That(VaultRules.DamageDealt(iron), Is.EqualTo(1f));
            Assert.That(VaultRules.ActionTime(iron), Is.EqualTo(1f));

            int hot = VaultRules.With(0, VaultPerk.HotShots);
            Assert.That(VaultRules.DamageDealt(hot), Is.EqualTo(1.15f));
            Assert.That(VaultRules.DamageTaken(hot), Is.EqualTo(1f));

            int wind = VaultRules.With(0, VaultPerk.SecondWind);
            Assert.That(VaultRules.RegenDelay(wind), Is.EqualTo(0.5f));
            Assert.That(VaultRules.RegenRate(wind), Is.EqualTo(2f));

            int hands = VaultRules.With(0, VaultPerk.QuickHands);
            Assert.That(VaultRules.ActionTime(hands), Is.EqualTo(0.6f));
            Assert.That(VaultRules.RegenRate(hands), Is.EqualTo(1f));

            Assert.That(VaultRules.DamageTaken(0), Is.EqualTo(1f));
            Assert.That(VaultRules.DamageDealt(0), Is.EqualTo(1f));
            Assert.That(VaultRules.RegenDelay(0), Is.EqualTo(1f));
            Assert.That(VaultRules.ActionTime(0), Is.EqualTo(1f));
        }

        [Test]
        public void EveryPerk_HasANameAndSaysWhatItDoes()
        {
            foreach (VaultPerk perk in System.Enum.GetValues(typeof(VaultPerk)))
            {
                if (perk == VaultPerk.None) { Assert.That(VaultRules.PerkName(perk), Is.Empty); continue; }
                Assert.That(VaultRules.PerkName(perk), Is.Not.Empty, perk.ToString());
                Assert.That(VaultRules.PerkEffect(perk), Does.StartWith("your crew"), perk.ToString());
            }
            Assert.That(VaultRules.PerkEffect(VaultPerk.IronSkin), Does.Contain("20%"));
            Assert.That(VaultRules.PerkEffect(VaultPerk.HotShots), Does.Contain("15%"));
            Assert.That(VaultRules.PerkEffect(VaultPerk.QuickHands), Does.Contain("40%"));
        }

        [Test]
        public void HoldStep_FillsWhileWorked_AndEmptiesWhenLetGo()
        {
            float p = 0f;
            for (int i = 0; i < 19; i++) p = VaultRules.HoldStep(p, true, 0.1f, 2f, 1f);
            Assert.That(p, Is.LessThan(1f));
            p = VaultRules.HoldStep(p, true, 0.1f, 2f, 1f);
            p = VaultRules.HoldStep(p, true, 0.1f, 2f, 1f);
            Assert.That(p, Is.EqualTo(1f));
            Assert.That(VaultRules.HoldStep(0.5f, false, 0.2f, 2f, 1f), Is.EqualTo(0.3f).Within(1e-5f));
            Assert.That(VaultRules.HoldStep(0.1f, false, 1f, 2f, 1f), Is.EqualTo(0f));
            Assert.That(VaultRules.HoldStep(0.2f, true, 0.1f, 0f, 1f), Is.EqualTo(1f));
            Assert.That(VaultRules.HoldStep(0.2f, true, 0f, 2f, 1f), Is.EqualTo(0.2f));
        }

        [Test]
        public void DrillStep_RunsWhileTheCrewIsNear_AndBackwardWhenItIsNot()
        {
            Assert.That(VaultRules.DrillStep(10f, true, 0.5f, 50f, 0.5f), Is.EqualTo(10.5f));
            Assert.That(VaultRules.DrillStep(10f, false, 2f, 50f, 0.5f), Is.EqualTo(9f));
            Assert.That(VaultRules.DrillStep(49.8f, true, 0.5f, 50f, 0.5f), Is.EqualTo(50f), "never past the end");
            Assert.That(VaultRules.DrillStep(0.2f, false, 2f, 50f, 0.5f), Is.EqualTo(0f), "never below zero");
            Assert.That(VaultRules.DrillStep(10f, true, 0f, 50f, 0.5f), Is.EqualTo(10f));
        }

        [Test]
        public void Drill_IsAbandonedOnlyWhenItHasRunAllTheWayBack()
        {
            Assert.That(VaultRules.DrillAbandoned(0f, false), Is.True);
            Assert.That(VaultRules.DrillAbandoned(0f, true), Is.False, "just started, crew at the door");
            Assert.That(VaultRules.DrillAbandoned(3f, false), Is.False, "the crew has time to get back");
        }

        [Test]
        public void TheKeyDrops_WhenAGuardedVaultHasNoGuardsLeft()
        {
            Assert.That(VaultRules.DetailCleared(2, 1), Is.False);
            Assert.That(VaultRules.DetailCleared(2, 0), Is.True);
            Assert.That(VaultRules.DetailCleared(0, 0), Is.False, "a vault that never had guards is handled separately");
        }

        [Test]
        public void HaulValue_AddsUpWhatIsLaidOut()
        {
            // [v0.22] this test asserted a vault was worth a whole quota (5500 to 6500 of 6000). It is now a share of it.
            Assert.That(VaultRules.HaulValue(3, 1150, 2, 500, 4, 400), Is.EqualTo(6050));
            Assert.That(VaultRules.HaulValue(-1, 1150, 0, 500, 0, 400), Is.EqualTo(0));
        }

        [Test]
        public void OneVault_IsMostOfAQuota_AndNeverAllOfIt()
        {
            int target = VaultRules.HaulTarget(6000, 0.8f);
            Assert.That(target, Is.EqualTo(4800));
            Assert.That(target, Is.LessThan(6000), "one vault alone must not call the getaway");
            Assert.That(6000 - target, Is.InRange(600, 1800), "the rest should be a safe or two, not another vault");
            Assert.That(VaultRules.HaulTarget(0, 0.8f), Is.EqualTo(0));
            Assert.That(VaultRules.HaulTarget(6000, 5f), Is.EqualTo(6000));
        }

        [Test]
        public void ItemsAreLaid_UntilTheNextOneWouldGoOverTheTarget()
        {
            int target = 4800, laid = 0, count = 0;
            foreach (int value in new[] { 1300, 1200, 1100, 600, 550, 520 })
            {
                if (!VaultRules.FitsHaul(laid, value, target)) continue;
                laid += value; count++;
            }
            Assert.That(laid, Is.LessThanOrEqualTo(target));
            Assert.That(count, Is.GreaterThanOrEqualTo(4));
            Assert.That(VaultRules.FitsHaul(0, 9999, 100), Is.True, "a vault is never empty");
        }

        [Test]
        public void TheRemainder_IsMadeUpInCash()
        {
            int piles = VaultRules.CashPiles(600, 250, 4, out int each);
            Assert.That(piles, Is.EqualTo(3));
            Assert.That(each, Is.EqualTo(200));
            Assert.That(VaultRules.CashPiles(2000, 250, 4, out each), Is.EqualTo(4), "never more piles than there is floor for");
            Assert.That(each, Is.EqualTo(250));
            Assert.That(VaultRules.CashPiles(30, 250, 4, out each), Is.EqualTo(0), "small change is not worth a pile");
            Assert.That(VaultRules.CashPiles(600, 0, 4, out each), Is.EqualTo(0));
        }
    }
}
