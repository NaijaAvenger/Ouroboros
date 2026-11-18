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

        // (v0.13) code-built model and a smoke trail, so a grenade in the air can be seen and dodged
        private Transform modelRoot;

        private void BuildModel()
        {
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false; // the prefab's placeholder sphere
            modelRoot = Art.GearBuilder.BuildProjectile(transform, FuseTimer.IsRunning);
            Vector3 s = transform.lossyScale;
            modelRoot.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z)) * 1.4f;
            Color smoke = new Color(0.8f, 0.8f, 0.82f, 0.5f), thin = new Color(0.8f, 0.8f, 0.82f, 0.2f);
            Art.Fx.Particles("Trail", Vector3.zero, transform, smoke, thin, 40f, 0, 1f, true, 0.35f, 0.6f, 0f, 0.2f, 0.12f, 0.22f, 0.03f, 0f, 1.8f, false);
        }

        public override void Render()
        {
            if (modelRoot == null) return;
            Vector3 v = Velocity;
            if (v.sqrMagnitude > 0.05f) modelRoot.rotation = Quaternion.LookRotation(v);
        }

        public override void Spawned()
        {
            if (Object.HasStateAuthority && fuseTime > 0f) FuseTimer = TickTimer.CreateFromSeconds(Runner, fuseTime); // set before the model reads it
            BuildModel(); // (v0.13)
            if (!Object.HasStateAuthority) return;
            LifeTimer = TickTimer.CreateFromSeconds(Runner, lifetime);
            // [v0.12] if (fuseTime > 0f) FuseTimer = TickTimer.CreateFromSeconds(Runner, fuseTime);   // moved above BuildModel
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
