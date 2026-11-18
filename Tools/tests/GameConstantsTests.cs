using System;
using NUnit.Framework;
using Ouroboros.Core;

namespace Ouroboros.Tests
{
    /// <summary>Invariants other code relies on but nothing in the compiler enforces.</summary>
    public class GameConstantsTests
    {
        /// <summary>NetworkPlayer keeps one expiry timer per status bit in a 16-slot array.</summary>
        private const int StatusTimerSlots = 16;

        [Test]
        public void InputButtonCount_MatchesTheEnum()
        {
            Assert.That(GameConstants.INPUT_BUTTON_COUNT, Is.EqualTo(Enum.GetValues(typeof(InputButtons)).Length),
                "GameConstants.INPUT_BUTTON_COUNT must be updated when InputButtons changes: input sampling loops over it.");
        }

        [Test]
        public void InputButtons_AreContiguousAndFitInNetworkButtons()
        {
            var values = (InputButtons[])Enum.GetValues(typeof(InputButtons));
            for (int i = 0; i < values.Length; i++)
            {
                Assert.That((int)values[i], Is.EqualTo(i), "InputButtons must be 0..N-1 with no gaps.");
            }
            Assert.That(values.Length, Is.LessThanOrEqualTo(32), "Fusion's NetworkButtons holds 32 bits.");
        }

        [Test]
        public void StatusFlags_AreSingleBitsWithATimerSlot()
        {
            foreach (StatusFlags flag in Enum.GetValues(typeof(StatusFlags)))
            {
                if (flag == StatusFlags.None) continue;
                int value = (int)flag;
                Assert.That(value & (value - 1), Is.EqualTo(0), $"{flag} must be a single bit.");
                Assert.That(MatchRules.LowestSetBit(value), Is.LessThan(StatusTimerSlots),
                    $"{flag} has no slot in NetworkPlayer.StatusTimers: raise STATUS_TIMER_SLOTS.");
            }
        }

        [Test]
        public void TeamUtil_RoundTripsEveryTeam()
        {
            for (int i = 0; i < GameConstants.MAX_TEAMS; i++)
            {
                TeamID team = TeamUtil.FromIndex(i);
                Assert.That(TeamUtil.IsValid(team), Is.True);
                Assert.That(TeamUtil.ToIndex(team), Is.EqualTo(i));
            }
            Assert.That(TeamUtil.IsValid(TeamID.None), Is.False);
        }

        [Test]
        public void MaxPlayers_IsTeamsTimesTeamSize()
        {
            Assert.That(GameConstants.MAX_PLAYERS, Is.EqualTo(GameConstants.MAX_TEAMS * GameConstants.PLAYERS_PER_TEAM));
        }

        [Test]
        public void MovementProfile_DescribesItsPerks()
        {
            Assert.That(MovementProfile.Default.Describe(), Is.EqualTo("Slide · Mantle"));

            var agent = MovementProfile.Default;
            agent.CanDoubleJump = true;
            Assert.That(agent.Describe(), Does.Contain("Double jump"));
        }
    }
}
