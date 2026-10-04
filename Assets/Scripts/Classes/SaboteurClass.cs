using Fusion;
using UnityEngine;

namespace Ouroboros.Classes
{
    /// <summary>
    /// Saboteur class - Specializes in stealth, traps, and environmental manipulation.
    /// </summary>
    public class SaboteurClass : Core.BasePlayerClass
    {
        [Header("Saboteur Abilities")]
        [SerializeField] private float trapPlacementRange = 5f;
        [SerializeField] private int maxTraps = 3;
        [SerializeField] private float trapDamage = 40f;
        [SerializeField] private float trapTriggerRadius = 1.5f;
        [SerializeField] private float stealthDuration = 10f;
        [SerializeField] private float sabotageRange = 6f;
        [SerializeField] private float sabotageDuration = 8f;
        [SerializeField] private float smokeRadius = 6f;
        [SerializeField] private float smokeDuration = 8f;

        [Header("Prefabs (optional)")]
        [Tooltip("Networked trap prefab. If unset, traps are simulated without a visual.")]
        [SerializeField] private NetworkPrefabRef trapPrefab;

        private int currentTrapsPlaced = 0;
        private bool isInStealth = false;
        private TickTimer stealthTimer;

        public bool IsInStealth => isInStealth;

        protected override void OnInitialize()
        {
            className = "Saboteur";
            description = "Stealth operative skilled in setting traps, sabotaging equipment, and silent elimination.";

            // Saboteur has balanced stats with high stealth capabilities
            maxHealth = 90f;
            movementSpeed = 6f;
            sprintSpeed = 8.5f;

            DefineAbility(0, "Place Trap",   cooldown: 6f,  staminaCost: 10f);
            DefineAbility(1, "Stealth Mode", cooldown: 25f, staminaCost: 25f, duration: stealthDuration);
            DefineAbility(2, "Sabotage",     cooldown: 18f, staminaCost: 15f, duration: sabotageDuration);
            DefineAbility(3, "Smoke Bomb",   cooldown: 15f, staminaCost: 10f, duration: smokeDuration);
        }

        // [v0.1] public override void UseAbility(int abilityIndex) { switch ... }
        protected override bool ExecuteAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0: return PlaceTrap();
                case 1: return ActivateStealth();
                case 2: return SabotageEquipment();
                case 3: return SmokeBomb();
            }
            return false;
        }

        private bool PlaceTrap()
        {
            if (currentTrapsPlaced >= maxTraps)
            {
                Log("Maximum traps already placed");
                return false;
            }

            Log($"Placing trap {currentTrapsPlaced + 1}/{maxTraps}");
            currentTrapsPlaced++;

            if (context != null && context.Runner != null && trapPrefab.IsValid)
            {
                Vector3 pos = context.Transform.position + context.Transform.forward * Mathf.Min(1.5f, trapPlacementRange);
                var owner = context;
                context.Runner.Spawn(trapPrefab, pos, Quaternion.identity, owner.PlayerRef, (runner, obj) =>
                {
                    var trap = obj.GetComponent<Interaction.ProximityTrap>();
                    if (trap != null)
                    {
                        trap.Configure(owner.Team, trapDamage, trapTriggerRadius, this);
                    }
                });
            }
            return true;
        }

        /// <summary>Called by a trap when it is consumed or destroyed so the Saboteur can place another.</summary>
        public void OnTrapConsumed()
        {
            currentTrapsPlaced = Mathf.Max(0, currentTrapsPlaced - 1);
        }

        private bool ActivateStealth()
        {
            if (isInStealth) return false;

            Log($"Activating stealth for {stealthDuration} seconds");
            isInStealth = true;
            stealthTimer = StartTimer(stealthDuration);
            SetStatus(Core.StatusFlags.Stealthed, true);
            // [v0.1] Invoke(nameof(DeactivateStealth), stealthDuration);
            return true;
        }

        private void DeactivateStealth()
        {
            isInStealth = false;
            SetStatus(Core.StatusFlags.Stealthed, false);
            Log("Stealth deactivated");
        }

        private bool SabotageEquipment()
        {
            Log("Sabotaging nearby equipment");
            if (context == null) return true;

            int count = 0;
            foreach (var device in Core.SceneUtil.FindAllInRadius<Interaction.ISecurityDevice>(context.Transform.position, sabotageRange))
            {
                device.Disable(sabotageDuration);
                count++;
            }
            // Sabotage also EMP-locks enemy players who are standing on the equipment
            foreach (var enemy in Network.NetworkPlayer.FindPlayersInRadius(context.Transform.position, sabotageRange * 0.5f, context.Team, aliveOnly: true))
            {
                enemy.ApplyTimedStatus(Core.StatusFlags.EMPDisabled, sabotageDuration * 0.5f);
                count++;
            }
            return count > 0;
        }

        private bool SmokeBomb()
        {
            Log("Deploying smoke bomb");
            if (context == null) return true;

            // Smoke breaks enemy reveals and gives the Saboteur's team a short concealment window.
            foreach (var player in Network.NetworkPlayer.FindPlayersInRadius(context.Transform.position, smokeRadius, Core.TeamID.None, aliveOnly: true))
            {
                if (player.Team == context.Team)
                {
                    player.ApplyTimedStatus(Core.StatusFlags.Stealthed, smokeDuration * 0.5f);
                    player.ApplyTimedStatus(Core.StatusFlags.Revealed, 0f);
                }
            }
            return true;
        }

        protected override void OnUpdate(float deltaTime)
        {
            if (isInStealth && TimerExpired(stealthTimer)) DeactivateStealth();
        }

        protected override void OnDeath()
        {
            if (isInStealth) DeactivateStealth();
        }
    }
}
