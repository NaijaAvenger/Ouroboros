using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// A fixed gun watching a vault front (v0.11, milestone M3). It belongs to the facility and fires on any
    /// player it can see, after a short lock-on. Quick-hack it and it works for your crew for a while: it then
    /// fires on rival players and on guards. It can be blinded by an EMP or a blackout, and shot until it
    /// shuts down to repair itself.
    /// </summary>
    public class SentryTurret : NetworkBehaviour, IQuickHackable, ISecurityDevice, Combat.IDamageable
    {
        public static readonly List<SentryTurret> All = new List<SentryTurret>();

        [Header("Gun")]
        [SerializeField] private float range = 22f;
        [SerializeField] private float damage = 7f;
        [SerializeField] private float fireInterval = 0.22f;
        [Tooltip("Seconds between seeing a target and the first shot. This is the warning.")]
        [SerializeField] private float lockSeconds = 0.8f;
        [Range(0f, 1f)] [SerializeField] private float baseAccuracy = 0.5f;
        [SerializeField] private float muzzleHeight = 1.3f;
        [SerializeField] private LayerMask occluders = ~0;

        [Header("Durability")]
        [SerializeField] private float maxHealth = 160f;
        [Tooltip("Seconds it stays shut down after being shot to zero.")]
        [SerializeField] private float repairSeconds = 30f;

        [Header("Hijack")]
        [SerializeField] private float hijackSeconds = 30f;

        [Header("Visuals")]
        [Tooltip("Part that turns to face the target.")]
        [SerializeField] private Transform head;
        [SerializeField] private Renderer indicator;

        [Networked] public Core.TeamID OwnerTeam { get; set; }
        [Networked] public float Health { get; set; }
        [Networked] public float AimYaw { get; set; }
        [Networked] public NetworkBool HasTarget { get; set; }
        [Networked] private TickTimer HijackTimer { get; set; }
        [Networked] private TickTimer DisabledTimer { get; set; }
        [Networked] private TickTimer FireTimer { get; set; }
        [Networked] private TickTimer LockTimer { get; set; }

        /// <summary>Fired on every peer for each shot (turret, muzzle, where it went, hit).</summary>
        public static event System.Action<SentryTurret, Vector3, Vector3, bool> Fired;

        private Network.NetworkPlayer targetPlayer;
        private AI.BaseAIAgent targetAgent;
        private MaterialPropertyBlock block;
        private int shownState = -1;

        public Vector3 MuzzlePosition => transform.position + Vector3.up * muzzleHeight;
        public bool IsHijacked => OwnerTeam != Core.TeamID.None;
        public bool IsDisabled => Object != null && DisabledTimer.IsRunning && !DisabledTimer.Expired(Runner);
        /// <summary>Not shooting: disabled, or without power.</summary>
        public bool IsOffline => IsDisabled || PowerNode.IsBlackedOut(transform.position);

        // ---- IDamageable ----
        public bool IsAlive => Object != null && !IsDisabled;
        public Core.TeamID Team => OwnerTeam;
        public Vector3 Position => transform.position;

        public override void Spawned()
        {
            if (!All.Contains(this)) All.Add(this);
            QuickHack.Register(this);
            if (Object.HasStateAuthority)
            {
                Health = maxHealth;
                OwnerTeam = Core.TeamID.None;
                AimYaw = transform.eulerAngles.y;
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            All.Remove(this);
            QuickHack.Unregister(this);
        }

        // ------------------------------------------------------------------

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (IsHijacked && HijackTimer.Expired(Runner)) { OwnerTeam = Core.TeamID.None; HijackTimer = TickTimer.None; ClearTarget(); }
            if (DisabledTimer.IsRunning && DisabledTimer.Expired(Runner)) { DisabledTimer = TickTimer.None; Health = maxHealth; }

            var gm = GameMode.ExtractionHeistGameMode.Instance;
            if (IsOffline || gm == null || gm.Object == null || !gm.IsMatchLive) { ClearTarget(); return; }

            if (((int)Runner.Tick) % 4 == 0) Acquire();

            Vector3 muzzle = MuzzlePosition;
            Vector3 aimPoint;
            float targetSpeed;
            if (targetPlayer != null && targetPlayer.Object != null && targetPlayer.IsActiveInMatch)
            {
                aimPoint = targetPlayer.Position + Vector3.up * 1.2f;
                targetSpeed = targetPlayer.Velocity.magnitude;
            }
            else if (targetAgent != null && targetAgent.Object != null && targetAgent.IsAlive)
            {
                aimPoint = targetAgent.Position;
                targetSpeed = 2f;
            }
            else { ClearTarget(); return; }

            Vector3 to = aimPoint - muzzle;
            float distance = to.magnitude;
            if (distance > range || distance < 0.1f) { ClearTarget(); return; }
            AimYaw = Mathf.Atan2(to.x, to.z) * Mathf.Rad2Deg;

            if (!LockTimer.ExpiredOrNotRunning(Runner) || !FireTimer.ExpiredOrNotRunning(Runner)) return;
            FireTimer = TickTimer.CreateFromSeconds(Runner, fireInterval);

            var alarm = GameMode.AlarmSystem.Instance;
            int tier = !IsHijacked && alarm != null && alarm.Object != null ? alarm.Tier : 0;
            bool hit = Random.value < Core.CombatRules.GuardHitChance(baseAccuracy, distance, range, targetSpeed, tier);
            Vector3 end = aimPoint;
            if (hit)
            {
                // [v0.13] if (targetPlayer != null) targetPlayer.ApplyDamage(damage, null, muzzle);
                // [v0.13] else targetAgent.ApplyDamage(damage, null);
                using (Combat.DamageContext.Begin(source: "Sentry Turret", means: IsHijacked ? "Hijacked" : "Security")) // (v0.14)
                {
                    if (targetPlayer != null) targetPlayer.ApplyDamage(damage, null, muzzle);
                    else targetAgent.ApplyDamage(damage, null);
                }
            }
            else
            {
                Vector3 dir = to / distance;
                Vector3 side = Vector3.Cross(dir, Vector3.up);
                if (side.sqrMagnitude < 0.0001f) side = Vector3.right;
                end += side.normalized * (Random.value < 0.5f ? -1f : 1f) * Random.Range(0.4f, 1f) + dir * 3f;
            }
            RPC_Fired(muzzle, end, hit);
        }

        /// <summary>Nearest thing this turret is allowed to shoot and can see.</summary>
        private void Acquire()
        {
            Vector3 muzzle = MuzzlePosition;
            Vector3 forward = Quaternion.Euler(0f, AimYaw, 0f) * Vector3.forward;

            var player = AI.Perception.FindVisiblePlayer(muzzle, forward, range, 360f, occluders, OwnerTeam);
            AI.BaseAIAgent agent = null;
            if (IsHijacked)
            {
                // Working for a crew: the facility's guards are fair game too.
                float bestSq = player != null ? (player.Position - transform.position).sqrMagnitude : range * range;
                var agents = AI.BaseAIAgent.All;
                for (int i = 0; i < agents.Count; i++)
                {
                    var a = agents[i];
                    if (a == null || a.Object == null || !a.IsAlive) continue;
                    float dSq = (a.Position - transform.position).sqrMagnitude;
                    if (dSq >= bestSq) continue;
                    if (!AI.Perception.CanSee(muzzle, forward, a.EyePosition, range, 360f, occluders)) continue;
                    agent = a; bestSq = dSq;
                }
                if (agent != null) player = null;
            }

            bool hadTarget = HasTarget;
            bool sameTarget = (player != null && player == targetPlayer) || (agent != null && agent == targetAgent);
            targetPlayer = player;
            targetAgent = agent;
            HasTarget = player != null || agent != null;

            if (HasTarget && (!hadTarget || !sameTarget))
            {
                LockTimer = TickTimer.CreateFromSeconds(Runner, lockSeconds);
                if (!IsHijacked)
                {
                    var gm = GameMode.ExtractionHeistGameMode.Instance;
                    GameMode.AlarmSystem.Raise((gm != null ? gm.Config.alarmOnGuardEngaged : 15f) * 0.5f, name);
                }
            }
        }

        private void ClearTarget()
        {
            targetPlayer = null;
            targetAgent = null;
            if (HasTarget) HasTarget = false;
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Fired(Vector3 from, Vector3 to, NetworkBool hit)
        {
            Fired?.Invoke(this, from, to, hit);
        }

        // ---- IQuickHackable ----
        public Vector3 HackPoint => MuzzlePosition;
        public float HackSeconds => 2.6f;
        public string HackPrompt(Network.NetworkPlayer by)
        {
            if (by == null || IsDisabled) return null;
            return by.Team == OwnerTeam ? null : "Hijack turret";
        }

        public void CompleteHack(Network.NetworkPlayer by)
        {
            if (!Object.HasStateAuthority || by == null) return;
            OwnerTeam = by.Team;
            HijackTimer = TickTimer.CreateFromSeconds(Runner, hijackSeconds);
            ClearTarget();
        }

        // ---- ISecurityDevice (EMP, sabotage) ----
        public void Disable(float duration)
        {
            if (!Object.HasStateAuthority) return;
            float remaining = DisabledTimer.RemainingTime(Runner) ?? 0f;
            if (duration > remaining) DisabledTimer = TickTimer.CreateFromSeconds(Runner, duration);
            ClearTarget();
        }

        // ---- IDamageable ----
        public void ApplyDamage(float amount, Network.NetworkPlayer attacker, Vector3 origin) => ApplyDamage(amount, attacker);

        public void ApplyDamage(float amount, Network.NetworkPlayer attacker)
        {
            if (!Object.HasStateAuthority || IsDisabled || amount <= 0f) return;
            // A crew cannot shoot the turret it hijacked.
            if (attacker != null && IsHijacked && attacker.Team == OwnerTeam) return;
            float healthBefore = Health; // (v0.14)
            Health = Mathf.Max(0f, Health - amount);
            if (attacker != null && healthBefore > Health)
            {
                var info = Combat.DamageContext.Current;
                attacker.NotifyDamageDealt(healthBefore - Health, info.HasPoint ? info.Point : transform.position + Vector3.up * 0.8f, false, Health <= 0f);
            }
            if (Health <= 0f)
            {
                DisabledTimer = TickTimer.CreateFromSeconds(Runner, repairSeconds);
                OwnerTeam = Core.TeamID.None;
                HijackTimer = TickTimer.None;
                ClearTarget();
            }
        }

        // ------------------------------------------------------------------

        public override void Render()
        {
            if (head != null) head.rotation = Quaternion.Slerp(head.rotation, Quaternion.Euler(0f, AimYaw, 0f), 1f - Mathf.Exp(-12f * Time.deltaTime));

            // 0 offline, 1 facility idle, 2 facility locked on, 3+ hijacked (by crew colour)
            int state = IsOffline ? 0 : (IsHijacked ? 3 + (int)OwnerTeam : (HasTarget ? 2 : 1));
            if (state == shownState || indicator == null) return;
            shownState = state;
            if (block == null) block = new MaterialPropertyBlock();
            Color c = state == 0 ? new Color(0.15f, 0.15f, 0.18f)
                    : state == 1 ? new Color(0.9f, 0.75f, 0.2f)
                    : state == 2 ? new Color(1f, 0.15f, 0.1f)
                    : Player.TeamSpawnPoint.TeamColor(OwnerTeam);
            block.SetColor("_Color", c); block.SetColor("_BaseColor", c);
            indicator.SetPropertyBlock(block);
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.3f, 0.2f, 0.6f);
            Gizmos.DrawWireSphere(transform.position, range);
        }
    }
}
