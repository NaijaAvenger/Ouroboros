using UnityEngine;
using Fusion;

namespace Ouroboros.Interaction
{
    /// <summary>
    /// Saboteur trap. Arms after a short delay, then detonates on the first enemy (or AI) that comes
    /// within <see cref="triggerRadius"/>. Networked so every peer sees it; only the state authority
    /// evaluates triggering.
    /// </summary>
    // [v0.10] public class ProximityTrap : NetworkBehaviour
    public class ProximityTrap : NetworkBehaviour, IQuickHackable
    {
        // ---- IQuickHackable (v0.11): a rival's trap can be turned against the crew that laid it ----
        public Vector3 HackPoint => transform.position + Vector3.up * 0.2f;
        public float HackSeconds => 1.6f;
        public string HackPrompt(Network.NetworkPlayer by) => by != null && IsArmed && by.Team != OwnerTeam ? "Hijack trap" : null;
        public void CompleteHack(Network.NetworkPlayer by)
        {
            if (!Object.HasStateAuthority || by == null) return;
            ownerClass?.OnTrapConsumed(); // the Saboteur who laid it gets the slot back
            ownerClass = null;
            OwnerTeam = by.Team;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            QuickHack.Unregister(this);
        }

        [SerializeField] private float armDelay = 1.5f;
        [SerializeField] private float lifetime = 180f;
        [SerializeField] private float triggerRadius = 1.5f;
        [SerializeField] private float damage = 40f;
        [SerializeField] private float blastRadius = 2.5f;

        [Networked] public Core.TeamID OwnerTeam { get; set; }
        [Networked] public NetworkBool IsArmed { get; set; }
        [Networked] private TickTimer ArmTimer { get; set; }
        [Networked] private TickTimer LifetimeTimer { get; set; }

        private Classes.SaboteurClass ownerClass;

        /// <summary>Called from the spawn callback before <see cref="Spawned"/>.</summary>
        public void Configure(Core.TeamID team, float trapDamage, float radius, Classes.SaboteurClass owner)
        {
            OwnerTeam = team;
            damage = trapDamage;
            triggerRadius = radius;
            ownerClass = owner;
        }

        /// <summary>(v0.13) Fired on every peer when a trap goes off (position, blast radius). Cosmetic.</summary>
        public static event System.Action<Vector3, float> Detonated;

        // (v0.13) code-built model: a disc with a sensor light, ringed in the owner's crew colour
        private Renderer sensorLight;
        private Core.TeamID modelTeam = Core.TeamID.None;
        private Transform modelRoot;

        private void BuildModel()
        {
            if (modelRoot != null) Destroy(modelRoot.gameObject);
            foreach (var r in GetComponentsInChildren<Renderer>()) r.enabled = false; // the prefab's placeholder shape
            modelTeam = OwnerTeam;
            Color crew = Core.TeamUtil.IsValid(OwnerTeam) ? Art.CrewStyle.Get(OwnerTeam).Primary : Color.gray;
            modelRoot = Art.GearBuilder.BuildTrap(transform, crew, out sensorLight);
            // The prefab may be scaled; the model is built in metres.
            Vector3 s = transform.lossyScale;
            modelRoot.localScale = new Vector3(1f / Mathf.Max(0.01f, s.x), 1f / Mathf.Max(0.01f, s.y), 1f / Mathf.Max(0.01f, s.z));
            modelRoot.position = Art.AbilityEffects.Ground(transform.position + Vector3.up * 0.3f);
        }

        public override void Render()
        {
            if (modelRoot == null || modelTeam != OwnerTeam) BuildModel(); // also after a hijack
            if (sensorLight == null) return;
            // Arming: steady. Armed: a slow blink. Something to spot, and something to shoot at.
            sensorLight.enabled = !IsArmed || Mathf.Repeat(Time.time, 1.2f) < 0.6f;
        }

        public override void Spawned()
        {
            QuickHack.Register(this); // (v0.11)
            BuildModel(); // (v0.13)
            if (Object.HasStateAuthority)
            {
                ArmTimer = TickTimer.CreateFromSeconds(Runner, armDelay);
                LifetimeTimer = TickTimer.CreateFromSeconds(Runner, lifetime);
                IsArmed = false;
            }
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority) return;

            if (LifetimeTimer.Expired(Runner))
            {
                Consume();
                return;
            }

            if (!IsArmed)
            {
                if (ArmTimer.Expired(Runner)) IsArmed = true;
                return;
            }

            if (((int)Runner.Tick) % Core.GameConstants.ZONE_SCAN_INTERVAL_TICKS != 0) return;

            var victims = Network.NetworkPlayer.FindPlayersInRadius(transform.position, triggerRadius, OwnerTeam, aliveOnly: true);
            bool triggered = victims.Count > 0;

            if (!triggered)
            {
                for (int i = 0; i < AI.BaseAIAgent.All.Count; i++)
                {
                    var ai = AI.BaseAIAgent.All[i];
                    if (ai != null && ai.IsAlive && (ai.Position - transform.position).sqrMagnitude <= triggerRadius * triggerRadius)
                    {
                        triggered = true;
                        break;
                    }
                }
            }

            if (triggered) Detonate();
        }

        private void Detonate()
        {
            var owner = ownerClass != null ? Network.NetworkPlayer.Resolve(ownerClass.Context) : null;
            Combat.DamageUtil.ApplyRadialDamage(transform.position, blastRadius, damage, owner, OwnerTeam, falloff: true);
            RPC_Detonated();
            Consume();
        }

        private void Consume()
        {
            ownerClass?.OnTrapConsumed();
            Runner.Despawn(Object);
        }

        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_Detonated()
        {
            Debug.Log($"[ProximityTrap] Trap of {OwnerTeam} detonated");
            Detonated?.Invoke(transform.position, blastRadius); // (v0.13) blast effect and sound
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = Color.red;
            Gizmos.DrawWireSphere(transform.position, triggerRadius);
        }
    }
}
