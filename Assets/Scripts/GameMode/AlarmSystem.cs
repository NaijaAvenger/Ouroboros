using System;
using UnityEngine;
using Fusion;

namespace Ouroboros.GameMode
{
    /// <summary>
    /// Facility-wide alarm. Cameras, guards, cracked vaults, breaches and kills raise <see cref="Level"/>;
    /// it decays over time. The level maps to a <see cref="Tier"/>:
    ///   0 Quiet · 1 Suspicious · 2 Alert (guards see further) · 3 Lockdown (reinforcement waves,
    ///   longer extractions, level stops decaying for a while).
    /// Replicated so every peer can show the meter; raises happen on the state authority only.
    /// Thresholds and amounts live in <see cref="Data.GameModeConfig"/>.
    /// </summary>
    public class AlarmSystem : NetworkBehaviour
    {
        public static AlarmSystem Instance { get; private set; }

        public const int MaxTier = 3;

        [Networked] public float Level { get; set; }
        [Networked] public int Tier { get; set; }
        [Networked] private TickTimer LockdownHold { get; set; }

        private ChangeDetector changeDetector;

        /// <summary>Fired on every peer when the tier changes (new tier).</summary>
        public static event Action<int> TierChanged;
        /// <summary>Fired on every peer when something raised the alarm (amount, reason). Cosmetic.</summary>
        public static event Action<float, string> Raised;

        public bool Lockdown => Tier >= MaxTier;
        public float Normalized => Mathf.Clamp01(Level / 100f);
        /// <summary>Guard / camera detection range scale for the current tier.</summary>
        public float DetectionMultiplier => 1f + 0.25f * Tier;
        /// <summary>Extraction duration scale (longer under lockdown).</summary>
        public float ExtractionTimeMultiplier => Lockdown ? Config.lockdownExtractionTimeMultiplier : 1f;

        private static Data.GameModeConfig Config
        {
            get
            {
                var gm = ExtractionHeistGameMode.Instance;
                return gm != null ? gm.Config : Data.GameModeConfig.CreateDefault();
            }
        }

        public override void Spawned()
        {
            Instance = this;
            changeDetector = GetChangeDetector(ChangeDetector.Source.SimulationState);
            if (Object.HasStateAuthority)
            {
                Level = 0f;
                Tier = 0;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (Instance == this) Instance = null;
        }

        public override void Render()
        {
            if (changeDetector == null) return;
            foreach (var change in changeDetector.DetectChanges(this))
            {
                if (change == nameof(Tier)) TierChanged?.Invoke(Tier);
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            var cfg = Config;
            bool holding = LockdownHold.IsRunning && !LockdownHold.Expired(Runner);
            if (!holding && Level > 0f)
            {
                Level = Mathf.Max(0f, Level - cfg.alarmDecayPerSecond * Runner.DeltaTime);
            }

            int tier = TierFor(Level, cfg);
            if (tier != Tier)
            {
                if (tier == MaxTier) LockdownHold = TickTimer.CreateFromSeconds(Runner, cfg.lockdownHoldSeconds);
                Tier = tier;
                RPC_TierChanged(tier);
            }
        }

        private static int TierFor(float level, Data.GameModeConfig cfg)
        {
            if (level >= cfg.alarmLockdownThreshold) return 3;
            if (level >= cfg.alarmAlertThreshold) return 2;
            if (level >= cfg.alarmSuspiciousThreshold) return 1;
            return 0;
        }

        /// <summary>Raises (or lowers, with a negative amount) the alarm. No-op off the state authority or with no alarm in the scene.</summary>
        public static void Raise(float amount, string reason)
        {
            var a = Instance;
            if (a == null || a.Object == null || !a.Object.HasStateAuthority || Mathf.Approximately(amount, 0f)) return;
            var gm = ExtractionHeistGameMode.Instance;
            if (gm != null && gm.Object != null && !gm.IsMatchLive) return;

            a.Level = Mathf.Clamp(a.Level + amount, 0f, 100f);
            a.RPC_Raised(amount, reason);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Raised(float amount, string reason)
        {
            Raised?.Invoke(amount, reason);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_TierChanged(int tier)
        {
            Debug.Log($"[Alarm] Tier {tier}: {TierName(tier)}");
        }

        public static string TierName(int tier)
        {
            switch (tier)
            {
                case 1: return "Suspicious";
                case 2: return "Alert";
                case 3: return "LOCKDOWN";
                default: return "Quiet";
            }
        }
    }
}
