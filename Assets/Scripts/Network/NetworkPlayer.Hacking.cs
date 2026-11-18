using System;
using UnityEngine;
using Fusion;

namespace Ouroboros.Network
{
    /// <summary>
    /// Hacking state and rules for a player (v0.11, milestone M3). Kept in its own file; it is the same
    /// component as <c>NetworkPlayer.cs</c>, so the player prefab needs no changes.
    ///
    /// <b>Quick hack</b>: hold Interact while aiming at a device (<see cref="Interaction.IQuickHackable"/>).
    /// <see cref="HackTarget"/> and <see cref="HackProgress"/> are replicated, so every peer can draw the tether
    /// and the hacker's HUD can show the bar. Letting go, losing the line, or taking damage breaks it.
    ///
    /// <b>Deep hack</b>: press Interact at a vault to jack in (<see cref="JackStation"/>). The player is then
    /// locked at the console and answers sign puzzles built from <see cref="JackSeed"/> and <see cref="JackStep"/>
    /// (<see cref="Core.HackRules"/>). Answers are checked on the state authority. Press Interact again, walk
    /// off, or die to jack out.
    /// </summary>
    public partial class NetworkPlayer
    {
        // ---- Quick hack ----
        /// <summary>Device currently tethered, or default when not hacking.</summary>
        [Networked] public NetworkId HackTarget { get; set; }
        /// <summary>Seconds the tether has been held.</summary>
        [Networked] public float HackProgress { get; set; }
        /// <summary>Seconds the current hack needs.</summary>
        [Networked] public float HackDuration { get; set; }
        /// <summary>While running, the specialist's next quick hack takes half as long (chaining).</summary>
        [Networked] private TickTimer HackChainTimer { get; set; }
        /// <summary>Brief pause after a completed hack so one hold does not re-toggle the same device.</summary>
        [Networked] private TickTimer HackRestTimer { get; set; }
        /// <summary>Tick of the most recent damage taken. Damage breaks a tether.</summary>
        [Networked] public int LastDamageTick { get; set; }

        // ---- Deep hack ----
        /// <summary>Vault this player is jacked into, or default.</summary>
        [Networked] public NetworkId JackStation { get; set; }
        [Networked] public int JackSeed { get; set; }
        /// <summary>Index of the puzzle currently on screen; goes up by one with every answer.</summary>
        [Networked] public int JackStep { get; set; }
        /// <summary>Running while input is refused after an answer (longer after a wrong one).</summary>
        [Networked] public TickTimer JackLockout { get; set; }

        /// <summary>Fired on every peer when a player answers a sign puzzle (player, was it right).</summary>
        public static event Action<NetworkPlayer, bool> HackAnswered;

        private const float ChainSeconds = 4f;
        private const float DamageBreakSeconds = 0.4f;

        private GameMode.LootObjective jackedObjective;

        public bool IsJackedIn => JackStation.IsValid;
        public bool IsQuickHacking => HackTarget.IsValid;
        /// <summary>The Hacker class (the Cipher) is the hack specialist: faster, further, and reads signs more easily.</summary>
        public bool IsHackSpecialist => ClassType == Core.PlayerClassType.Hacker;
        public float QuickHackRange => Core.HackRules.QuickHackRange(Interaction.QuickHack.BaseRange, IsHackSpecialist);
        public float HackProgress01 => HackDuration > 0f ? Mathf.Clamp01(HackProgress / HackDuration) : 0f;
        public bool IsJackLockedOut => JackLockout.IsRunning && !JackLockout.Expired(Runner);

        /// <summary>The vault this player is jacked into, resolved on this peer (null when none).</summary>
        public GameMode.LootObjective JackedObjective
        {
            get
            {
                if (!JackStation.IsValid || Runner == null) { jackedObjective = null; return null; }
                if (jackedObjective != null && jackedObjective.Object != null && jackedObjective.Object.Id == JackStation) return jackedObjective;
                jackedObjective = Runner.TryFindObject(JackStation, out NetworkObject obj) && obj != null ? obj.GetComponent<GameMode.LootObjective>() : null;
                return jackedObjective;
            }
        }

        /// <summary>The device this player is tethered to, resolved on this peer (null when none).</summary>
        public Interaction.IQuickHackable HackTargetDevice
        {
            get
            {
                if (!HackTarget.IsValid || Runner == null) return null;
                return Runner.TryFindObject(HackTarget, out NetworkObject obj) && obj != null ? obj.GetComponent<Interaction.IQuickHackable>() : null;
            }
        }

        // ------------------------------------------------------------------
        // Simulation (state authority, once per tick, from PlayerController)

        /// <summary>Advances both kinds of hack from this tick's Interact state.</summary>
        public void TickHacking(bool interactHeld, bool interactPressed)
        {
            if (!Object.HasStateAuthority) return;

            if (!IsActiveInMatch || HasStatus(Core.StatusFlags.EMPDisabled))
            {
                ClearHackState();
                return;
            }

            // ---- Deep hack ----
            if (IsJackedIn)
            {
                var station = JackedObjective;
                if (station == null || !station.CanJackIn(this) || interactPressed) JackOut();
                ClearQuickHack();
                return;
            }
            if (interactPressed)
            {
                var station = GameMode.LootObjective.FindJackable(this);
                if (station != null)
                {
                    JackIn(station);
                    ClearQuickHack();
                    return;
                }
            }

            // ---- Quick hack ----
            if (!interactHeld || !HackRestTimer.ExpiredOrNotRunning(Runner)) { ClearQuickHack(); return; }
            if (((int)Runner.Tick - LastDamageTick) * Runner.DeltaTime < DamageBreakSeconds) { ClearQuickHack(); return; }

            var device = Interaction.QuickHack.FindTarget(this, QuickHackRange);
            if (device == null) { ClearQuickHack(); return; }

            if (device.Object.Id != HackTarget)
            {
                HackTarget = device.Object.Id;
                HackProgress = 0f;
                bool chained = IsHackSpecialist && HackChainTimer.IsRunning && !HackChainTimer.Expired(Runner);
                HackDuration = Core.HackRules.QuickHackSeconds(device.HackSeconds, IsHackSpecialist, chained);
            }

            HackProgress += Runner.DeltaTime;
            if (HackProgress < HackDuration) return;

            string what = device.HackPrompt(this);
            device.CompleteHack(this);
            ClearQuickHack();
            HackRestTimer = TickTimer.CreateFromSeconds(Runner, 0.6f);
            if (IsHackSpecialist) HackChainTimer = TickTimer.CreateFromSeconds(Runner, ChainSeconds);
            RPC_QuickHackCompleted(what ?? "");
        }

        /// <summary>
        /// Completes a hack on <paramref name="device"/> at once (the Cipher's Remote Hack). State authority only.
        /// </summary>
        public bool InstantHack(Interaction.IQuickHackable device)
        {
            if (!Object.HasStateAuthority || device == null) return false;
            string what = device.HackPrompt(this);
            if (string.IsNullOrEmpty(what)) return false;
            device.CompleteHack(this);
            HackChainTimer = TickTimer.CreateFromSeconds(Runner, ChainSeconds);
            RPC_QuickHackCompleted(what);
            return true;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_QuickHackCompleted(string what)
        {
            Interaction.QuickHack.RaiseCompleted(this, what);
        }

        private void ClearQuickHack()
        {
            if (HackTarget.IsValid) HackTarget = default;
            if (HackProgress != 0f) HackProgress = 0f;
        }

        private void JackIn(GameMode.LootObjective station)
        {
            JackStation = station.Object.Id;
            jackedObjective = station;
            JackSeed = unchecked((int)Runner.Tick * 7919 + PlayerRef.RawEncoded * 104729 + station.ObjectiveName.GetHashCode());
            JackStep = 0;
            JackLockout = TickTimer.CreateFromSeconds(Runner, 0.25f); // a beat to read the first sign
        }

        private void JackOut()
        {
            JackStation = default;
            jackedObjective = null;
            JackLockout = TickTimer.None;
        }

        /// <summary>Drops any hack in progress (death, extraction, EMP). State authority only.</summary>
        public void ClearHackState()
        {
            if (!Object.HasStateAuthority) return;
            ClearQuickHack();
            if (IsJackedIn) JackOut();
        }

        // ------------------------------------------------------------------
        // Sign puzzle answers

        /// <summary>Call from the owning client's UI: answers the puzzle currently shown with candidate <paramref name="choice"/>.</summary>
        public void SubmitHackAnswer(int choice)
        {
            if (!IsJackedIn) return;
            if (Object.HasStateAuthority) HandleHackAnswer(JackStep, choice);
            else if (Object.HasInputAuthority) RPC_HackAnswer(JackStep, choice);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_HackAnswer(int step, int choice)
        {
            HandleHackAnswer(step, choice);
        }

        private void HandleHackAnswer(int step, int choice)
        {
            // A stale step means the answer was for a puzzle that is no longer on screen.
            if (!IsJackedIn || step != JackStep || IsJackLockedOut || !IsActiveInMatch) return;
            var station = JackedObjective;
            if (station == null) { JackOut(); return; }
            bool correct = Core.HackRules.IsCorrect(JackSeed, JackStep, station.HackDifficultyFor(this), choice);
            ResolveAnswer(station, correct);
        }

        /// <summary>A bot's answer: it has no screen, so its brain just reports whether it got the sign right. State authority only.</summary>
        public void SubmitBotAnswer(bool correct)
        {
            if (!Object.HasStateAuthority || !IsBot || !IsJackedIn || IsJackLockedOut) return;
            var station = JackedObjective;
            if (station == null) { JackOut(); return; }
            ResolveAnswer(station, correct);
        }

        private void ResolveAnswer(GameMode.LootObjective station, bool correct)
        {
            JackStep++;
            JackLockout = TickTimer.CreateFromSeconds(Runner, correct ? 0.15f : 0.8f);
            station.ApplyHackAnswer(this, correct);
            RPC_HackAnswered(correct);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_HackAnswered(NetworkBool correct)
        {
            HackAnswered?.Invoke(this, correct);
        }
    }
}
