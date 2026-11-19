using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Rules.Tests
{
    public class AssistRulesTests
    {
        [Test]
        public void CanPing_RespectsTheCooldown()
        {
            Assert.That(AssistRules.CanPing(100, 90, 15), Is.False);
            Assert.That(AssistRules.CanPing(105, 90, 15), Is.True);
            Assert.That(AssistRules.CanPing(0, -9999, 15), Is.True, "the first ping of a match is always allowed");
            Assert.That(AssistRules.CanPing(100, 100, -5), Is.True);
        }

        [Test]
        public void PingLifetime_EnemiesGoStaleFirst()
        {
            Assert.That(AssistRules.PingLifetime(PingKind.Enemy), Is.LessThan(AssistRules.PingLifetime(PingKind.Go)));
            Assert.That(AssistRules.PingLifetime(PingKind.Go), Is.LessThan(AssistRules.PingLifetime(PingKind.Loot)));
            foreach (PingKind kind in System.Enum.GetValues(typeof(PingKind))) Assert.That(AssistRules.PingLifetime(kind), Is.GreaterThan(0f));
        }

        [Test]
        public void DistanceFromRay_MeasuresToTheNearestPointAhead()
        {
            float d = AssistRules.DistanceFromRay(0, 0, 0, 0, 0, 2, 1, 0, 5, out float along);
            Assert.That(d, Is.EqualTo(1f).Within(1e-5f));
            Assert.That(along, Is.EqualTo(5f).Within(1e-5f), "the direction need not be unit length");

            d = AssistRules.DistanceFromRay(0, 0, 0, 0, 0, 1, 0, 0, -3, out along);
            Assert.That(d, Is.EqualTo(3f).Within(1e-5f), "a point behind the pinger is measured from the pinger");
            Assert.That(along, Is.LessThan(0f));

            d = AssistRules.DistanceFromRay(1, 1, 1, 0, 0, 0, 1, 1, 4, out along);
            Assert.That(d, Is.EqualTo(3f).Within(1e-5f), "a zero direction does not divide by zero");
        }

        [Test]
        public void ReviveStep_FillsInTheStatedTime_AndDrainsWhenLetGo()
        {
            float p = 0f;
            for (int i = 0; i < 34; i++) p = AssistRules.ReviveStep(p, true, 0.1f, 3.5f, 0.6f);
            Assert.That(p, Is.LessThan(1f));
            for (int i = 0; i < 2; i++) p = AssistRules.ReviveStep(p, true, 0.1f, 3.5f, 0.6f);
            Assert.That(p, Is.EqualTo(1f));

            Assert.That(AssistRules.ReviveStep(0.5f, false, 0.5f, 3.5f, 0.6f), Is.EqualTo(0.2f).Within(1e-5f));
            Assert.That(AssistRules.ReviveStep(0.1f, false, 1f, 3.5f, 0.6f), Is.EqualTo(0f));
            Assert.That(AssistRules.ReviveStep(0.5f, true, 0f, 3.5f, 0.6f), Is.EqualTo(0.5f));
            Assert.That(AssistRules.ReviveStep(0.2f, true, 0.1f, 0f, 0.6f), Is.EqualTo(1f), "an instant revive is instant");
        }

        [Test]
        public void ReviveHealth_IsAFractionOfFull_AndNeverZero()
        {
            Assert.That(AssistRules.ReviveHealth(150f, 0.4f), Is.EqualTo(60f).Within(1e-4f));
            Assert.That(AssistRules.ReviveHealth(150f, 0f), Is.EqualTo(1f));
            Assert.That(AssistRules.ReviveHealth(150f, 3f), Is.EqualTo(150f));
            Assert.That(AssistRules.ReviveHealth(0f, 0.4f), Is.EqualTo(0f));
        }

        [Test]
        public void RegenStep_WaitsOutTheDelay_ThenClimbsToTheCap()
        {
            Assert.That(AssistRules.RegenStep(40f, 100f, 3f, 6f, 12f, 1f, 0.5f), Is.EqualTo(40f), "still in combat");
            Assert.That(AssistRules.RegenStep(40f, 100f, 6f, 6f, 12f, 1f, 0.5f), Is.EqualTo(46f).Within(1e-4f));
            Assert.That(AssistRules.RegenStep(98f, 100f, 9f, 6f, 12f, 1f, 0.5f), Is.EqualTo(100f));
            Assert.That(AssistRules.RegenStep(70f, 100f, 9f, 6f, 12f, 0.75f, 0.5f), Is.EqualTo(75f));
        }

        [Test]
        public void RegenStep_LeavesTheDeadAndTheOverhealedAlone()
        {
            Assert.That(AssistRules.RegenStep(0f, 100f, 60f, 6f, 12f, 1f, 0.5f), Is.EqualTo(0f), "regeneration never raises the dead");
            Assert.That(AssistRules.RegenStep(90f, 100f, 60f, 6f, 12f, 0.75f, 0.5f), Is.EqualTo(90f), "health above the cap is not pulled down");
            Assert.That(AssistRules.RegenStep(40f, 100f, 60f, 6f, 0f, 1f, 0.5f), Is.EqualTo(40f), "a rate of zero switches it off");
            Assert.That(AssistRules.RegenStep(40f, 100f, 60f, 6f, 12f, 1f, 0f), Is.EqualTo(40f));
        }

        [Test]
        public void IsRegenerating_MatchesRegenStep()
        {
            Assert.That(AssistRules.IsRegenerating(40f, 100f, 3f, 6f, 12f, 1f), Is.False);
            Assert.That(AssistRules.IsRegenerating(40f, 100f, 7f, 6f, 12f, 1f), Is.True);
            Assert.That(AssistRules.IsRegenerating(100f, 100f, 7f, 6f, 12f, 1f), Is.False);
            Assert.That(AssistRules.IsRegenerating(0f, 100f, 7f, 6f, 12f, 1f), Is.False);
            Assert.That(AssistRules.IsRegenerating(40f, 100f, 7f, 6f, 0f, 1f), Is.False);
        }
    }
}
