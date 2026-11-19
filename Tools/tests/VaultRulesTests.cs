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
        public void TheDefaultHaul_IsAboutOneGalleriaQuota()
        {
            int haul = VaultRules.HaulValue(3, 1150, 2, 500, 4, 400);
            Assert.That(haul, Is.InRange(5500, 6500), "cracking one vault should all but call the getaway at a 6000 quota");
            Assert.That(VaultRules.HaulValue(-1, 1150, 0, 500, 0, 400), Is.EqualTo(0));
        }
    }
}
