using System;
using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Tests
{
    public class CombatRulesTests
    {
        // ---- DamageFalloff ----

        [TestCase(0f, 1.0f)]
        [TestCase(20f, 1.0f)]
        [TestCase(40f, 0.75f)]
        [TestCase(60f, 0.5f)]
        [TestCase(500f, 0.5f)]
        public void DamageFalloff_IsLinearBetweenStartAndEnd(float distance, float expected)
        {
            Assert.That(CombatRules.DamageFalloff(distance, 20f, 60f, 0.5f), Is.EqualTo(expected).Within(1e-5f));
        }

        [Test]
        public void DamageFalloff_EndNotAfterStart_MeansNoFalloff()
        {
            Assert.That(CombatRules.DamageFalloff(999f, 0f, 0f, 0.5f), Is.EqualTo(1f));
            Assert.That(CombatRules.DamageFalloff(999f, 30f, 10f, 0.1f), Is.EqualTo(1f));
        }

        [Test]
        public void ShotDamage_HeadshotMultiplies_AndNeverReducesDamage()
        {
            Assert.That(CombatRules.ShotDamage(20f, 1f, false, 1.5f), Is.EqualTo(20f).Within(1e-5f));
            Assert.That(CombatRules.ShotDamage(20f, 1f, true, 1.5f), Is.EqualTo(30f).Within(1e-5f));
            Assert.That(CombatRules.ShotDamage(20f, 0.5f, true, 2f), Is.EqualTo(20f).Within(1e-5f));
            // A mis-authored multiplier below 1 must not make headshots weaker than body shots.
            Assert.That(CombatRules.ShotDamage(20f, 1f, true, 0.2f), Is.EqualTo(20f).Within(1e-5f));
        }

        [TestCase(0.0f, false)]
        [TestCase(1.0f, false)]
        [TestCase(1.59f, false)]
        [TestCase(1.6f, true)]
        [TestCase(1.95f, true)]
        public void IsHeadHeight_TopFifthOfATwoMetreBody(float height, bool expected)
        {
            Assert.That(CombatRules.IsHeadHeight(height, 2f), Is.EqualTo(expected));
        }

        // ---- Bloom ----

        [Test]
        public void Bloom_GrowsPerShot_UpToTheCap()
        {
            float bloom = 0f;
            for (int i = 0; i < 3; i++) bloom = CombatRules.BloomAfterShot(bloom, 0.5f, 3f);
            Assert.That(bloom, Is.EqualTo(1.5f).Within(1e-5f));

            for (int i = 0; i < 50; i++) bloom = CombatRules.BloomAfterShot(bloom, 0.5f, 3f);
            Assert.That(bloom, Is.EqualTo(3f).Within(1e-5f));
        }

        [Test]
        public void Bloom_RecoversLinearly_AndStopsAtZero()
        {
            Assert.That(CombatRules.DecayedBloom(3f, 0f, 6f), Is.EqualTo(3f).Within(1e-5f));
            Assert.That(CombatRules.DecayedBloom(3f, 0.25f, 6f), Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(CombatRules.DecayedBloom(3f, 10f, 6f), Is.EqualTo(0f));
        }

        [Test]
        public void EffectiveSpread_AddsBloom_ThenAppliesStance()
        {
            Assert.That(CombatRules.EffectiveSpread(1.5f, 0f, 1f), Is.EqualTo(1.5f).Within(1e-5f));
            Assert.That(CombatRules.EffectiveSpread(1.5f, 0.5f, 2f), Is.EqualTo(4f).Within(1e-5f));
        }

        [Test]
        public void StanceSpread_StandingStillIsTheBaseline_AndEveryPenaltyStacks()
        {
            Assert.That(CombatRules.StanceSpreadMultiplier(grounded: true, sprinting: false, sliding: false), Is.EqualTo(1f));
            float airborne = CombatRules.StanceSpreadMultiplier(false, false, false);
            float airborneSprint = CombatRules.StanceSpreadMultiplier(false, true, false);
            Assert.That(airborne, Is.GreaterThan(1f));
            Assert.That(airborneSprint, Is.GreaterThan(airborne));
        }

        // ---- Deterministic spread ----

        [Test]
        public void SpreadOffset_SameSeed_SameDirection()
        {
            // The shooter's client and the server compute this independently; they must agree.
            int seed = CombatRules.ShotSeed(tick: 12345, playerId: 3, slot: 0, pellet: 2);
            CombatRules.SpreadOffset(seed, 4f, out float yawA, out float pitchA);
            CombatRules.SpreadOffset(seed, 4f, out float yawB, out float pitchB);
            Assert.That(yawA, Is.EqualTo(yawB));
            Assert.That(pitchA, Is.EqualTo(pitchB));
        }

        [Test]
        public void SpreadOffset_StaysInsideTheCone()
        {
            for (int i = 0; i < 5000; i++)
            {
                CombatRules.SpreadOffset(CombatRules.ShotSeed(i, 1, 0, i % 8), 3f, out float yaw, out float pitch);
                Assert.That(Math.Sqrt(yaw * yaw + pitch * pitch), Is.LessThanOrEqualTo(3f + 1e-4));
            }
        }

        [Test]
        public void SpreadOffset_ZeroSpread_IsDeadCentre()
        {
            CombatRules.SpreadOffset(42, 0f, out float yaw, out float pitch);
            Assert.That(yaw, Is.EqualTo(0f));
            Assert.That(pitch, Is.EqualTo(0f));
        }

        [Test]
        public void SpreadOffset_IsNotBiased()
        {
            // Mean of many samples should sit near the centre of the cone, and pellets of one shot should differ.
            double sumYaw = 0, sumPitch = 0;
            const int n = 20000;
            for (int i = 0; i < n; i++)
            {
                CombatRules.SpreadOffset(CombatRules.ShotSeed(i, 2, 1, 0), 2f, out float yaw, out float pitch);
                sumYaw += yaw; sumPitch += pitch;
            }
            Assert.That(sumYaw / n, Is.EqualTo(0).Within(0.05));
            Assert.That(sumPitch / n, Is.EqualTo(0).Within(0.05));

            CombatRules.SpreadOffset(CombatRules.ShotSeed(100, 1, 0, 0), 6f, out float y0, out float p0);
            CombatRules.SpreadOffset(CombatRules.ShotSeed(100, 1, 0, 1), 6f, out float y1, out float p1);
            Assert.That(y0 != y1 || p0 != p1, Is.True, "pellets of the same shot must not overlap");
        }

        [Test]
        public void Hash01_IsInUnitRange()
        {
            foreach (int seed in new[] { 0, 1, -1, int.MaxValue, int.MinValue, 123456789 })
            {
                float h = CombatRules.Hash01(seed);
                Assert.That(h, Is.GreaterThanOrEqualTo(0f));
                Assert.That(h, Is.LessThan(1f));
            }
        }

        // ---- Recoil ----

        [Test]
        public void RecoilKick_PitchIsUpward_GrowsEarlyInABurst_ThenPlateaus()
        {
            CombatRules.RecoilKick(0, 0.6f, 0.25f, 1, out float first, out _);
            CombatRules.RecoilKick(3, 0.6f, 0.25f, 2, out float fourth, out _);
            CombatRules.RecoilKick(5, 0.6f, 0.25f, 3, out float sixth, out _);
            CombatRules.RecoilKick(40, 0.6f, 0.25f, 4, out float late, out _);

            Assert.That(first, Is.EqualTo(0.6f).Within(1e-5f));
            Assert.That(fourth, Is.GreaterThan(first));
            Assert.That(late, Is.EqualTo(sixth).Within(1e-5f));
        }

        [Test]
        public void RecoilKick_YawStaysInRange()
        {
            for (int i = 0; i < 2000; i++)
            {
                CombatRules.RecoilKick(i, 0.5f, 0.3f, i * 7919, out _, out float yaw);
                Assert.That(Math.Abs(yaw), Is.LessThanOrEqualTo(0.3f + 1e-5f));
            }
        }

        [Test]
        public void RecoverRecoil_DecaysTowardZero_WithoutOvershooting()
        {
            float offset = -4f;
            float after = CombatRules.RecoverRecoil(offset, 8f, 0.1f);
            Assert.That(after, Is.LessThan(0f));
            Assert.That(after, Is.GreaterThan(offset));

            // Frame-rate independent: one 100 ms step equals ten 10 ms steps.
            float stepped = offset;
            for (int i = 0; i < 10; i++) stepped = CombatRules.RecoverRecoil(stepped, 8f, 0.01f);
            Assert.That(stepped, Is.EqualTo(after).Within(1e-4f));
        }

        // ---- Guards ----

        [Test]
        public void GuardHitChance_CloseAndStill_IsTheBaseAccuracy()
        {
            Assert.That(CombatRules.GuardHitChance(0.55f, 0f, 12f, 0f, 0), Is.EqualTo(0.55f).Within(1e-5f));
        }

        [Test]
        public void GuardHitChance_DropsWithDistanceAndTargetSpeed()
        {
            float close = CombatRules.GuardHitChance(0.55f, 2f, 12f, 0f, 0);
            float far = CombatRules.GuardHitChance(0.55f, 12f, 12f, 0f, 0);
            float farSprinting = CombatRules.GuardHitChance(0.55f, 12f, 12f, 8f, 0);

            Assert.That(far, Is.LessThan(close));
            Assert.That(farSprinting, Is.LessThan(far));
        }

        [Test]
        public void GuardHitChance_RisesWithAlarmTier()
        {
            float quiet = CombatRules.GuardHitChance(0.5f, 6f, 12f, 4f, 0);
            float lockdown = CombatRules.GuardHitChance(0.5f, 6f, 12f, 4f, 3);
            Assert.That(lockdown, Is.GreaterThan(quiet));
        }

        [Test]
        public void GuardHitChance_IsNeverCertainAndNeverZero()
        {
            Assert.That(CombatRules.GuardHitChance(5f, 0f, 12f, 0f, 3), Is.EqualTo(0.95f).Within(1e-5f));
            Assert.That(CombatRules.GuardHitChance(0f, 50f, 12f, 20f, 0), Is.EqualTo(0.05f).Within(1e-5f));
        }
    }
}
