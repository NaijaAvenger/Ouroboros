using UnityEngine;
using Fusion;

namespace Ouroboros.Equipment
{
    /// <summary>
    /// Server-simulated projectile. Moves with optional gravity, sweeps a ray each tick for collisions,
    /// and either deals direct damage to what it hits or explodes radially (optionally after a fuse).
    /// Position is replicated through NetworkTransform so proxies see it fly.
    /// </summary>
    public class Projectile : NetworkBehaviour
    {
        [SerializeField] private LayerMask hitMask = ~0;
        [SerializeField] private float radius = 0.1f;

        [Networked] private Vector3 Velocity { get; set; }
        [Networked] private TickTimer LifeTimer { get; set; }
        [Networked] private TickTimer FuseTimer { get; set; }
        [Networked] private PlayerRef OwnerRef { get; set; }

        // Stats are only needed on the state authority (set before Spawned via Configure).
        private Network.NetworkPlayer shooter;
        private float damage;
        private float gravity;
        private float explosionRadius;
        private float fuseTime;
        private float lifetime = 6f;
        private bool configured;

        /// <summary>Fired on every peer when the projectile detonates / impacts (position, radius).</summary>
        public static event System.Action<Vector3, float> Exploded;

        public void Configure(Network.NetworkPlayer owner, Vector3 velocity, Data.EquipmentData data)
        {
            shooter = owner;
            Velocity = velocity;
            OwnerRef = owner != null ? owner.PlayerRef : default;
            damage = data.damage;
            gravity = data.projectileGravity;
            explosionRadius = data.explosionRadius;
            fuseTime = data.fuseTime;
            lifetime = data.projectileLifetime > 0f ? data.projectileLifetime : 6f;
            hitMask = data.hitMask.value == ~0 ? (LayerMask)Physics.DefaultRaycastLayers : data.hitMask;
            configured = true;
        }

        public override void Spawned()
        {
            if (!Object.HasStateAuthority) return;
            LifeTimer = TickTimer.CreateFromSeconds(Runner, lifetime);
            if (fuseTime > 0f) FuseTimer = TickTimer.CreateFromSeconds(Runner, fuseTime);
            if (shooter == null) shooter = Network.NetworkPlayer.Resolve(OwnerRef);
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (LifeTimer.Expired(Runner))
            {
                Runner.Despawn(Object);
                return;
            }

            if (FuseTimer.IsRunning && FuseTimer.Expired(Runner))
            {
                Detonate(transform.position, null);
                return;
            }

            float dt = Runner.DeltaTime;
            Vector3 v = Velocity;
            v.y += gravity * dt;
            Velocity = v;

            Vector3 step = v * dt;
            float dist = step.magnitude;
            if (dist > 0f)
            {
                Vector3 dir = step / dist;
                if (Physics.SphereCast(transform.position, radius, dir, out RaycastHit hit, dist, hitMask, QueryTriggerInteraction.Ignore))
                {
                    var target = hit.collider.GetComponentInParent<Combat.IDamageable>();
                    bool isShooter = shooter != null && ReferenceEquals(target, shooter);
                    if (!isShooter)
                    {
                        if (fuseTime > 0f && explosionRadius > 0f && target == null)
                        {
                            // Fused explosive bounces to a stop on world geometry instead of detonating.
                            transform.position = hit.point + hit.normal * radius;
                            Velocity = Vector3.Reflect(v, hit.normal) * 0.35f;
                            return;
                        }
                        Detonate(hit.point, target);
                        return;
                    }
                }
                transform.position += step;
                if (v.sqrMagnitude > 0.01f) transform.rotation = Quaternion.LookRotation(v);
            }
        }

        private void Detonate(Vector3 point, Combat.IDamageable directTarget)
        {
            if (explosionRadius > 0f)
            {
                Combat.DamageUtil.ApplyRadialDamage(point, explosionRadius, damage, shooter, shooter != null ? shooter.Team : Core.TeamID.None, falloff: true);
            }
            else if (directTarget != null && directTarget.IsAlive)
            {
                directTarget.ApplyDamage(damage, shooter);
            }

            RPC_Exploded(point, explosionRadius);
            Runner.Despawn(Object);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Exploded(Vector3 point, float blastRadius)
        {
            Exploded?.Invoke(point, blastRadius);
        }
    }
}
