using Fusion;
using UnityEngine;

namespace Ouroboros.Classes
{
    /// <summary>
    /// Hacker class - Specializes in electronic warfare, system infiltration, and tech manipulation.
    /// </summary>
    public class HackerClass : Core.BasePlayerClass
    {
        [Header("Hacker Abilities")]
        [SerializeField] private float hackRange = 10f;
        [SerializeField] private float hackDuration = 5f;
        [SerializeField] private float cameraDisableRange = 15f;
        [SerializeField] private float empRadius = 8f;
        [SerializeField] private float empDuration = 4f;
        [SerializeField] private float dataMineRange = 40f;
        [SerializeField] private float dataMineRevealDuration = 5f;
        // [v0.1] [SerializeField] private float abilityDMGBoost = 1.2f; // unused in v0.1, removed from inspector

        // [v0.1] private float lastHackTime;
        // [v0.1] private const float HACK_COOLDOWN = 15f;   // superseded by AbilitySlot cooldown

        protected override void OnInitialize()
        {
            className = "Hacker";
            description = "Electronic warfare specialist capable of disabling security systems and hacking enemy equipment.";

            // Hacker has lower health but higher movement speed
            maxHealth = 85f;
            movementSpeed = 5.5f;
            sprintSpeed = 7.8f;

            DefineAbility(0, "System Hack",    cooldown: 15f, staminaCost: 15f, duration: hackDuration);
            DefineAbility(1, "Disable Camera", cooldown: 20f, staminaCost: 10f);
            DefineAbility(2, "EMP Blast",      cooldown: 25f, staminaCost: 25f, duration: empDuration);
            DefineAbility(3, "Data Mine",      cooldown: 30f, staminaCost: 20f, duration: dataMineRevealDuration);
        }

        // [v0.1] public override void UseAbility(int abilityIndex) { switch ... }
        protected override bool ExecuteAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0: return HackSystem();
                case 1: return DisableSecurityCamera();
                case 2: return EMPBlast();
                case 3: return DataMine();
            }
            return false;
        }

        private bool HackSystem()
        {
            // [v0.1] if (Time.time - lastHackTime < HACK_COOLDOWN) return;
            Log($"Initiating system hack with range {hackRange}m");
            if (context == null) return true;

            // Hack the nearest hackable object in range (doors, terminals, loot objectives...)
            var hackable = Core.SceneUtil.FindNearest<Interaction.IHackable>(context.Transform.position, hackRange);
            if (hackable == null)
            {
                Log("No hackable target in range");
                return false; // refund cooldown: nothing happened
            }

            hackable.OnHacked(context.Team, hackDuration);
            return true;
        }

        private bool DisableSecurityCamera()
        {
            Log("Disabling security cameras in area");
            if (context == null) return true;

            int count = 0;
            foreach (var cam in Core.SceneUtil.FindAllInRadius<Interaction.ISecurityDevice>(context.Transform.position, cameraDisableRange))
            {
                cam.Disable(hackDuration);
                count++;
            }
            return count > 0;
        }

        private bool EMPBlast()
        {
            Log("Deploying EMP blast");
            if (context == null) return true;

            foreach (var enemy in Network.NetworkPlayer.FindPlayersInRadius(context.Transform.position, empRadius, context.Team, aliveOnly: true))
            {
                enemy.ApplyTimedStatus(Core.StatusFlags.EMPDisabled, empDuration);
            }
            foreach (var device in Core.SceneUtil.FindAllInRadius<Interaction.ISecurityDevice>(context.Transform.position, empRadius))
            {
                device.Disable(empDuration);
            }
            return true;
        }

        private bool DataMine()
        {
            Log("Data mining enemy positions");
            if (context == null) return true;

            foreach (var enemy in Network.NetworkPlayer.FindPlayersInRadius(context.Transform.position, dataMineRange, context.Team, aliveOnly: true))
            {
                enemy.ApplyTimedStatus(Core.StatusFlags.Revealed, dataMineRevealDuration);
            }
            return true;
        }
    }
}
