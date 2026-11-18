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
        /// <summary>(v0.12) Tick at which the puzzle on screen appeared. Pulse puzzles are timed from it.</summary>
        [Networked] public int JackStepTick { get; set; }

        // ---- Being hacked (v0.12) ----
        /// <summary>Running after this player was jammed: they cannot be jammed again until it expires.</summary>
        [Networked] private TickTimer JamImmunity { get; set; }

        /// <summary>Fired on every peer when a player's gear is jammed by a rival (victim, hacker).</summary>
        public static event Action<NetworkPlayer, NetworkPlayer> Jammed;

        public bool IsJamImmune => JamImmunity.IsRunning && !JamImmunity.Expired(Runner);

        // IQuickHackable: the "device" is this player's gear.
        public Vector3 HackPoint => transform.position + Vector3.up * 1.2f;
        public float HackSeconds => Core.HackRules.JamHackSeconds;

        public string HackPrompt(NetworkPlayer by)
        {
            if (by == null || by == this || by.Team == Team || !IsActiveInMatch) return null;
            if (HasStatus(Core.StatusFlags.EMPDisabled) || IsJamImmune) return null;
            if (HasStatus(Core.StatusFlags.Stealthed) && !HasStatus(Core.StatusFlags.Revealed)) return null;
            return "Jam gear";
        }

        /// <summary>
        /// A rival finished a tether on this player: abilities, hacking and interaction are dead for a few
        /// seconds, weapons for a shorter moment, and any hack this player had running is cut.
        /// </summary>
        public void CompleteHack(NetworkPlayer by)
        {
            if (!Object.HasStateAuthority || string.IsNullOrEmpty(HackPrompt(by))) return;
            ApplyTimedStatus(Core.StatusFlags.EMPDisabled, Core.HackRules.JamSeconds);
            var gear = Loadout;
            if (gear != null) gear.Jam(Core.HackRules.JamWeaponSeconds);
            ClearHackState();
            JamImmunity = TickTimer.CreateFromSeconds(Runner, Core.HackRules.JamImmunitySeconds);
            RPC_Jammed(by.PlayerRef);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Jammed(PlayerRef by)
        {
            Jammed?.Invoke(this, Resolve(by));
        }

        /// <summary>The rival currently tethering this player, if any (for the victim's warning). Works on every peer.</summary>
        public NetworkPlayer FindHacker()
        {
            if (Object == null) return null;
            for (int i = 0; i < All.Count; i++)
            {
                var p = All[i];
                if (p != null && p != this && p.Object != null && p.HackTarget == Object.Id) return p;
            }
            return null;
        }

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
            JackStepTick = (int)Runner.Tick; // (v0.12)
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
            if (station.PuzzleKindFor(this) != Core.HackRules.PuzzleKind.Signs) return; // (v0.12) wrong kind of answer
            bool correct = Core.HackRules.IsCorrect(JackSeed, JackStep, station.HackDifficultyFor(this), choice);
            ResolveAnswer(station, correct);
        }

        // ---- Pulse puzzles (v0.12) ----

        /// <summary>Seconds the puzzle on screen has been up, as this peer's simulation sees it (smoothed between ticks).</summary>
        public float JackStepSeconds => Runner != null ? ((int)Runner.Tick - JackStepTick + Runner.LocalAlpha) * Runner.DeltaTime : 0f;

        /// <summary>Call from the owning client's UI: strike the pulse now.</summary>
        public void SubmitPulseStrike()
        {
            if (!IsJackedIn) return;
            int tick = (int)Runner.Tick;
            if (Object.HasStateAuthority) HandlePulseStrike(JackStep, tick);
            else if (Object.HasInputAuthority) RPC_PulseStrike(JackStep, tick);
        }

        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_PulseStrike(int step, int tick)
        {
            HandlePulseStrike(step, tick);
        }

        private void HandlePulseStrike(int step, int tick)
        {
            if (!IsJackedIn || step != JackStep || IsJackLockedOut || !IsActiveInMatch) return;
            var station = JackedObjective;
            if (station == null) { JackOut(); return; }
            if (station.PuzzleKindFor(this) != Core.HackRules.PuzzleKind.Pulse) return;

            // The client reports the tick it struck on. Its simulation runs slightly ahead of ours, so accept a
            // tick near the present; anything further off is judged at the present instead.
            int now = (int)Runner.Tick;
            int maxSkew = Mathf.CeilToInt(0.25f / Runner.DeltaTime);
            if (Mathf.Abs(tick - now) > maxSkew) tick = now;

            int difficulty = station.HackDifficultyFor(this);
            float seconds = Mathf.Max(0, tick - JackStepTick) * Runner.DeltaTime;
            float position = Core.HackRules.PulsePosition(JackSeed, JackStep, seconds, Core.HackRules.PulsePeriodSeconds(difficulty));
            bool hit = Core.HackRules.IsPulseHit(position, Core.HackRules.PulseWindowCentre(JackSeed, JackStep),
                Core.HackRules.PulseWindowWidth(difficulty), tolerance: 0.035f);
            ResolveAnswer(station, hit);
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
            JackStepTick = (int)Runner.Tick; // (v0.12)
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
