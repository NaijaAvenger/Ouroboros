using System.Collections.Generic;
using UnityEngine;
using Fusion;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// Instant-hit weapon (rifles, pistols, shotguns).
    ///
    /// v0.9 (M1):
    ///  - <see cref="OnUse"/> runs on the state authority and, as prediction, on the shooter's own client. Both
    ///    spend the round and register the shot (see <see cref="NetworkLoadout.RegisterShot"/>); only the state
    ///    authority traces for damage.
    ///  - Spread is a deterministic function of (tick, player, slot, pellet), so the predicted tracer and the
    ///    authoritative hit use the same direction. Spread grows with sustained fire (bloom) and with movement.
    ///  - With Lag Compensation enabled in the NetworkProjectConfig and <c>HitboxRoot</c>/<c>Hitbox</c> on the
    ///    targets, hits are tested against where the shooter saw the target. <see cref="Combat.HitZone"/> on a
    ///    hitbox marks the head; damage also falls off with distance.
    ///  - Without lag compensation it falls back to a plain physics ray (present-time positions).
    /// </summary>
    public class HitscanWeapon : BaseEquipment
    {
        private NetworkRunner hitboxRunner;
        private HitboxManager hitboxManager;
        private readonly List<LagCompensatedHit> hitBuffer = new List<LagCompensatedHit>(16);

        /// <summary>True when the current runner carries a HitboxManager, without touching Runner.LagCompensation.</summary>
        private bool HasHitboxManager()
        {
            var runner = Runner;
            if (runner != hitboxRunner)
            {
                hitboxRunner = runner;
                hitboxManager = runner != null ? runner.GetComponent<HitboxManager>() : null;
            }
            return hitboxManager != null;
        }

        /// <summary>Start the ray this far ahead of the eyes so the shooter's own capsule is skipped.</summary>
        private const float MuzzleOffset = 0.6f;
        /// <summary>Height used by the head-zone fallback for targets without hitboxes.</summary>
        private const float FallbackBodyHeight = 2f;

        /// <summary>What one pellet hit.</summary>
        private struct PelletHit
        {
            public bool Hit;
            public Vector3 Point;
            public float Distance;
            public Combat.IDamageable Target;
            public bool Head;
        }

        protected override bool OnUse()
        {
            if (data == null || Runner == null) return false;
            if (!loadout.ConsumeAmmo(slotIndex)) return false;

            bool authority = HasAuthority;
            Vector3 aim = owner.AimDirection;
            Vector3 origin = owner.EyePosition + aim * MuzzleOffset;
            float range = Mathf.Max(1f, data.range);
            int mask = data.hitMask.value == ~0 ? Physics.DefaultRaycastLayers : data.hitMask.value;

            // Spread for this trigger pull, read before RegisterShot adds this shot's bloom.
            float spread = loadout.CurrentSpread(slotIndex);
            int playerId = owner.Object.InputAuthority.PlayerId;
            int tick = (int)Runner.Tick;

            bool hitAnything = false;
            Vector3 lastPoint = origin + aim * range;
            float damageDealt = 0f;
            bool anyHeadshot = false, anyKill = false;

            int pellets = Mathf.Max(1, data.pelletCount);
            for (int i = 0; i < pellets; i++)
            {
                Vector3 dir = ApplySpread(aim, spread, Core.CombatRules.ShotSeed(tick, playerId, slotIndex, i));
                PelletHit hit = authority ? TraceAuthoritative(origin, dir, range, mask) : TracePredicted(origin, dir, range, mask);

                lastPoint = hit.Hit ? hit.Point : origin + dir * range;
                if (!hit.Hit) continue;
                hitAnything = true;

                // Only the state authority deals damage.
                if (!authority || hit.Target == null || ReferenceEquals(hit.Target, owner) || !hit.Target.IsAlive) continue;

                float falloff = Core.CombatRules.DamageFalloff(hit.Distance, data.falloffStart, data.falloffEnd, data.falloffMinFraction);
                float damage = Core.CombatRules.ShotDamage(data.damage, falloff, hit.Head, data.headshotMultiplier);

                float healthBefore = HealthOf(hit.Target);
                // [v0.13] hit.Target.ApplyDamage(damage, owner, origin);
                using (Combat.DamageContext.Begin(means: data.equipmentName, headshot: hit.Head, point: hit.Point)) // (v0.14)
                {
                    hit.Target.ApplyDamage(damage, owner, origin);
                }
                float lost = healthBefore - HealthOf(hit.Target);

                if (lost > 0f || !hit.Target.IsAlive)
                {
                    damageDealt += Mathf.Max(lost, 0.01f);
                    anyHeadshot |= hit.Head;
                    anyKill |= !hit.Target.IsAlive;
                }
            }

            // [v0.8] loadout.NotifyFired(slotIndex, lastPoint, hitAnything);
            loadout.RegisterShot(slotIndex, lastPoint, hitAnything);
            if (authority && damageDealt > 0f) owner.NotifyHitConfirm(damageDealt, anyHeadshot, anyKill);
            return true;
        }

        // ------------------------------------------------------------------
        // Tracing

        /// <summary>State authority: lag-compensated when available, plain physics otherwise.</summary>
        private PelletHit TraceAuthoritative(Vector3 origin, Vector3 dir, float range, int mask)
        {
            // Probe for the component first: Fusion's Runner.LagCompensation getter logs an error on every
            // call when lag compensation is off (v0.8).
            var lagComp = HasHitboxManager() ? Runner.LagCompensation : null;
            if (lagComp == null) return TracePhysics(origin, dir, range, mask, findTarget: true);

            hitBuffer.Clear();
            lagComp.RaycastAll(origin, dir, range, owner.Object.InputAuthority, hitBuffer, mask, true,
                HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority, QueryTriggerInteraction.Ignore);

            // Nearest valid hit. A lag-compensated target is hit through its Hitboxes (rewound to what the
            // shooter saw); its ordinary collider sits at the present-time position and must be ignored, or
            // shots would also land where the target is "now" on the server.
            var result = default(PelletHit);
            float best = float.MaxValue;
            for (int i = 0; i < hitBuffer.Count; i++)
            {
                var h = hitBuffer[i];
                if (h.Distance >= best || h.GameObject == null) continue;

                if (h.Hitbox != null)
                {
                    var target = h.Hitbox.GetComponentInParent<Combat.IDamageable>();
                    if (ReferenceEquals(target, owner)) continue;
                    var zone = h.Hitbox.GetComponent<Combat.HitZone>();
                    best = h.Distance;
                    result = new PelletHit { Hit = true, Point = h.Point, Distance = h.Distance, Target = target, Head = zone != null && zone.zone == Combat.HitZoneType.Head };
                }
                else
                {
                    if (h.GameObject.GetComponentInParent<HitboxRoot>() != null) continue; // raw collider of a hitbox target
                    var target = h.GameObject.GetComponentInParent<Combat.IDamageable>();
                    if (ReferenceEquals(target, owner)) continue;
                    best = h.Distance;
                    result = new PelletHit { Hit = true, Point = h.Point, Distance = h.Distance, Target = target, Head = IsHead(h.GameObject, target, h.Point) };
                }
            }
            return result;
        }

        /// <summary>Owning client: where the tracer should end. Never deals damage.</summary>
        private PelletHit TracePredicted(Vector3 origin, Vector3 dir, float range, int mask)
        {
            return TracePhysics(origin, dir, range, mask, findTarget: false);
        }

        private PelletHit TracePhysics(Vector3 origin, Vector3 dir, float range, int mask, bool findTarget)
        {
            if (!Physics.Raycast(origin, dir, out RaycastHit hit, range, mask, QueryTriggerInteraction.Ignore)) return default;

            var result = new PelletHit { Hit = true, Point = hit.point, Distance = hit.distance };
            if (findTarget)
            {
                result.Target = hit.collider.GetComponentInParent<Combat.IDamageable>();
                result.Head = IsHead(hit.collider.gameObject, result.Target, hit.point);
            }
            return result;
        }

        /// <summary>Head test for plain colliders: an explicit <see cref="Combat.HitZone"/>, else the top of the body.</summary>
        private static bool IsHead(GameObject hitObject, Combat.IDamageable target, Vector3 point)
        {
            var zone = hitObject.GetComponent<Combat.HitZone>();
            if (zone != null) return zone.zone == Combat.HitZoneType.Head;
            if (target == null) return false;
            return Core.CombatRules.IsHeadHeight(point.y - target.Position.y, FallbackBodyHeight);
        }

        private static float HealthOf(Combat.IDamageable target)
        {
            // [v0.10] if (target is Network.NetworkPlayer player) return player.Health;
            // [v0.10] if (target is AI.BaseAIAgent agent) return agent.Health;
            // [v0.10] return 0f;
            return target != null ? target.Health : 0f; // (v0.11) part of IDamageable, so turrets count too
        }

        // ------------------------------------------------------------------
        // [v0.8] Previous OnUse, kept for reference. It ran on the state authority only, used server-side random
        // [v0.8] spread, took the first lag-compensated hit (including a target's present-time collider) and had no
        // [v0.8] hit zones, falloff or hit confirmation.
        // [v0.8] protected override bool OnUse()
        // [v0.8] {
        // [v0.8]     if (data == null || Runner == null) return false;
        // [v0.8]     if (!loadout.ConsumeAmmo(slotIndex)) return false;
        // [v0.8]
        // [v0.8]     Vector3 aim = owner.AimDirection;
        // [v0.8]     Vector3 origin = owner.EyePosition + aim * MuzzleOffset;
        // [v0.8]     float range = Mathf.Max(1f, data.range);
        // [v0.8]     int mask = data.hitMask.value == ~0 ? Physics.DefaultRaycastLayers : data.hitMask.value;
        // [v0.8]     float damagePerPellet = data.damage;
        // [v0.8]
        // [v0.8]     bool hitAnything = false;
        // [v0.8]     Vector3 lastPoint = origin + aim * range;
        // [v0.8]     int pellets = Mathf.Max(1, data.pelletCount);
        // [v0.8]
        // [v0.8]     for (int i = 0; i < pellets; i++)
        // [v0.8]     {
        // [v0.8]         Vector3 dir = ApplySpread(aim, data.spreadDegrees);
        // [v0.8]
        // [v0.8]         GameObject hitObject = null;
        // [v0.8]         Vector3 hitPoint = origin + dir * range;
        // [v0.8]         bool didHit;
        // [v0.8]
        // [v0.8]         // Lag-compensated when the runner has a HitboxManager (Lag Compensation enabled in NetworkProjectConfig);
        // [v0.8]         // plain physics otherwise.
                // [v0.4] previously called Runner.LagCompensation unconditionally → NullReferenceException on runners without it.
                // [v0.8] var lagComp = Runner.LagCompensation;  ← the getter itself logs "[Fusion] Failed to find HitboxManager"
        // [v0.8]         //        on every shot when lag compensation is off, so probe for the component first (cached per runner).
        // [v0.8]         var lagComp = HasHitboxManager() ? Runner.LagCompensation : null;
        // [v0.8]         if (lagComp != null)
        // [v0.8]         {
        // [v0.8]             didHit = lagComp.Raycast(origin, dir, range, owner.Object.InputAuthority, out LagCompensatedHit hit, mask,
        // [v0.8]                 HitOptions.IncludePhysX | HitOptions.IgnoreInputAuthority);
        // [v0.8]             if (didHit) { hitPoint = hit.Point; hitObject = hit.GameObject; }
        // [v0.8]         }
        // [v0.8]         else
        // [v0.8]         {
        // [v0.8]             didHit = Physics.Raycast(origin, dir, out RaycastHit hit, range, mask, QueryTriggerInteraction.Ignore);
        // [v0.8]             if (didHit) { hitPoint = hit.point; hitObject = hit.collider.gameObject; }
        // [v0.8]         }
        // [v0.8]
        // [v0.8]         lastPoint = hitPoint;
        // [v0.8]         if (didHit)
        // [v0.8]         {
        // [v0.8]             hitAnything = true;
        // [v0.8]             var target = hitObject != null ? hitObject.GetComponentInParent<Combat.IDamageable>() : null;
        // [v0.8]             if (target != null && !ReferenceEquals(target, owner) && target.IsAlive)
        // [v0.8]             {
        // [v0.8]                 target.ApplyDamage(damagePerPellet, owner);
        // [v0.8]             }
        // [v0.8]         }
        // [v0.8]     }
        // [v0.8]
        // [v0.8]     loadout.NotifyFired(slotIndex, lastPoint, hitAnything);
        // [v0.8]     return true;
        // [v0.8] }

        // [v0.8] private static Vector3 ApplySpread(Vector3 direction, float degrees)
        // [v0.8] {
        // [v0.8]     if (degrees <= 0f) return direction;
        // [v0.8]     Vector2 offset = Random.insideUnitCircle * degrees;   // server-only randomness: could not be predicted
        // [v0.8]     return Quaternion.AngleAxis(offset.x, Vector3.up) * Quaternion.AngleAxis(offset.y, Vector3.right) * direction;
        // [v0.8] }
        /// <summary>Rotates <paramref name="direction"/> by a seeded offset inside a cone of <paramref name="degrees"/>.</summary>
        private static Vector3 ApplySpread(Vector3 direction, float degrees, int seed)
        {
            if (degrees <= 0f) return direction;
            Core.CombatRules.SpreadOffset(seed, degrees, out float yaw, out float pitch);
            return Quaternion.LookRotation(direction) * Quaternion.Euler(pitch, yaw, 0f) * Vector3.forward;
        }
    }
}
