using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Rules.Tests
{
    public class ObjectiveRulesTests
    {
        private static ObjectiveFacts Playing() => new ObjectiveFacts
        {
            MatchLive = true, Alive = true, StationAvailable = true, VaultAvailable = true, WorthBanking = 750
        };

        [Test]
        public void BeforeTheMatch_TheBriefing()
        {
            Assert.That(ObjectiveRules.Choose(new ObjectiveFacts { Alive = true }), Is.EqualTo(Objective.GetReady));
        }

        [Test]
        public void EmptyHanded_GoAndCrackAVault()
        {
            Assert.That(ObjectiveRules.Choose(Playing()), Is.EqualTo(Objective.CrackVault));
        }

        [Test]
        public void LootOnTheFloor_ComesBeforeTheNextVault()
        {
            var f = Playing(); f.LootInReach = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.GrabLoot));
        }

        [Test]
        public void CarryingEnough_CashOut_ButNotForPocketChange()
        {
            var f = Playing(); f.Carrying = 800;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.CashOut));
            f.Carrying = 200;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.CrackVault), "small change is not worth a minute's defence while vaults remain");
            f.StationAvailable = false; f.Carrying = 5000;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.CrackVault), "every station is taken by a rival");
        }

        [Test]
        public void NothingLeftToOpen_BankWhateverIsInHand()
        {
            var f = Playing(); f.VaultAvailable = false; f.Carrying = 100;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.CashOut));
            f.Carrying = 0;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.FindLoot));
        }

        [Test]
        public void MoneyAlreadyDown_ComesBeforeNewMoney()
        {
            var f = Playing(); f.OwnCashOutRunning = true; f.Carrying = 5000; f.RivalCashOutRunning = true; f.LootInReach = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.DefendCashOut));
            f.OwnCashOutBeingHijacked = true; f.CrewmateDownInReach = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.StopTheHijack), "a hijack in progress beats even a revive");
        }

        [Test]
        public void ADownedCrewmate_ComesBeforeHoldingAQuietCashOut()
        {
            var f = Playing(); f.OwnCashOutRunning = true; f.CrewmateDownInReach = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.ReviveCrewmate));
        }

        [Test]
        public void ARivalCashOut_IsWorthMoreThanTheNextVault_ButNotThanBankingAFullBag()
        {
            var f = Playing(); f.RivalCashOutRunning = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.StealCashOut));
            f.Carrying = 2000;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.CashOut));
        }

        [Test]
        public void TheGetaway_OverridesEverything()
        {
            var f = Playing(); f.GetawayOpen = true; f.OwnCashOutBeingHijacked = true; f.CrewmateDownInReach = true; f.Carrying = 9000;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.GetOut));
            f.InOpenExit = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.HoldTheLift));
        }

        [Test]
        public void Down_WaitsForARevive_OrARedeploy()
        {
            var f = Playing(); f.Alive = false; f.GetawayOpen = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.Redeploying));
            f.CanBeRevived = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.AwaitRevive));
        }

        [Test]
        public void Extracted_IsAway_WhateverElseIsTrue()
        {
            var f = Playing(); f.Extracted = true; f.Alive = false; f.MatchLive = false;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.Away));
        }

        // ---- (v0.21) Grand Vaults

        [Test]
        public void AVaultKeyInHand_GoesToTheDoor_BeforeAnythingThatOnlyMakesMoney()
        {
            var f = Playing(); f.CarryingVaultKey = true; f.Carrying = 5000; f.RivalCashOutRunning = true; f.LootInReach = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.UseVaultKey));
            f.OwnCashOutRunning = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.DefendCashOut), "money already down still comes first");
        }

        [Test]
        public void ARunningDrill_IsDefended_BeforeBankingOrStealing()
        {
            var f = Playing(); f.OwnDrillRunning = true; f.Carrying = 5000; f.RivalCashOutRunning = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.DefendDrill));
            f.CrewmateDownInReach = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.ReviveCrewmate));
        }

        [Test]
        public void InAnOpenVault_ThePerkComesBeforeTheLoot()
        {
            var f = Playing(); f.PerkInReach = true; f.LootInReach = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.ClaimPerk));
            f.PerkInReach = false;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.GrabLoot));
        }

        [Test]
        public void AKeyOnTheFloor_IsPickedUp_BeforeWalkingToAVault()
        {
            var f = Playing(); f.VaultKeyInReach = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.TakeVaultKey));
        }

        [Test]
        public void WithEveryVaultOpen_TheSafesAreWhatIsLeft()
        {
            var f = Playing(); f.VaultAvailable = false; f.SafeAvailable = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.CrackSafe));
            f.VaultAvailable = true;
            Assert.That(ObjectiveRules.Choose(f), Is.EqualTo(Objective.CrackVault), "a Grand Vault always comes before a safe");
        }

        [Test]
        public void WorthBanking_IsAnEighthOfTheQuota_WithAFloor()
        {
            Assert.That(ObjectiveRules.WorthBanking(6000), Is.EqualTo(750));
            Assert.That(ObjectiveRules.WorthBanking(1000), Is.EqualTo(300));
            Assert.That(ObjectiveRules.WorthBanking(0), Is.EqualTo(300));
        }

        [Test]
        public void Urgent_IsTheDropEverythingSet()
        {
            Assert.That(ObjectiveRules.IsUrgent(Objective.StopTheHijack), Is.True);
            Assert.That(ObjectiveRules.IsUrgent(Objective.GetOut), Is.True);
            Assert.That(ObjectiveRules.IsUrgent(Objective.CrackVault), Is.False);
            Assert.That(ObjectiveRules.IsUrgent(Objective.DefendCashOut), Is.False);
        }
    }
}
