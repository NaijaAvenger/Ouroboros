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
        [Tooltip("(v0.11) Reach of Remote Hack: the device you are aiming at is hacked at once.")]
        [SerializeField] private float remoteHackRange = 40f;
        [Tooltip("(v0.11) Seconds Net Sight shows hackable devices through walls.")]
        [SerializeField] private float netSightDuration = 10f;
        [Tooltip("(v0.11) Rivals this close to a device are revealed while Net Sight runs.")]
        [SerializeField] private float netSightRevealRadius = 6f;
        // [v0.1] [SerializeField] private float abilityDMGBoost = 1.2f; // unused in v0.1, removed from inspector

        // [v0.1] private float lastHackTime;
        // [v0.1] private const float HACK_COOLDOWN = 15f;   // superseded by AbilitySlot cooldown

        protected override void OnInitialize()
        {
            // [v0.10] className = "Hacker";
            // [v0.10] description = "Electronic warfare specialist capable of disabling security systems and hacking enemy equipment.";
            // (v0.11) the Cipher: everyone can hack, this class is simply the best at it. Passive, from
            //         NetworkPlayer.IsHackSpecialist: quick hacks reach 60% further and take half as long (a quarter
            //         when chained), vault signs read one step easier and each right answer counts for more.
            className = "Cipher";
            description = "Reads the building like a pattern. Hacks faster and from further than anyone, chains one hack into the next, and sees the network through walls.";

            // Hacker has lower health but higher movement speed
            maxHealth = 85f;
            movementSpeed = 5.5f;
            sprintSpeed = 7.8f;
            movement.CanGlide = true; movement.GlideSeconds = 3f;           // v0.7: light frame, glides

            // [v0.10] DefineAbility(0, "System Hack",    cooldown: 15f, staminaCost: 15f, duration: hackDuration);
            // [v0.10] DefineAbility(1, "Disable Camera", cooldown: 20f, staminaCost: 10f);
            DefineAbility(0, "Remote Hack",    cooldown: 15f, staminaCost: 15f, duration: hackDuration);
            DefineAbility(1, "Net Sight",      cooldown: 22f, staminaCost: 10f, duration: netSightDuration);
            DefineAbility(2, "EMP Blast",      cooldown: 25f, staminaCost: 25f, duration: empDuration);
            DefineAbility(3, "Data Mine",      cooldown: 30f, staminaCost: 20f, duration: dataMineRevealDuration);
        }

        // [v0.1] public override void UseAbility(int abilityIndex) { switch ... }
        protected override bool ExecuteAbility(int abilityIndex)
        {
            switch (abilityIndex)
            {
                case 0: return HackSystem();
                // [v0.10] case 1: return DisableSecurityCamera();
                case 1: return NetSight();
                case 2: return EMPBlast();
                case 3: return DataMine();
            }
            return false;
        }

        /// <summary>(v0.13) What each ability shows in the world.</summary>
        public override Core.AbilityVisual GetAbilityVisual(int slot)
        {
            var cyan = new Color(0.3f, 0.95f, 1f);
            switch (slot)
            {
                case 0: return new Core.AbilityVisual(Core.AbilityVisualKind.Beam, remoteHackRange, 0.25f, cyan);
                case 1: return new Core.AbilityVisual(Core.AbilityVisualKind.Pulse, 30f, 1.1f, cyan);
                case 2: return new Core.AbilityVisual(Core.AbilityVisualKind.Shock, empRadius, 0.5f, new Color(0.35f, 0.6f, 1f));
                case 3: return new Core.AbilityVisual(Core.AbilityVisualKind.Pulse, dataMineRange, 1.4f, new Color(1f, 0.25f, 0.9f));
                default: return default;
            }
        }

        private bool HackSystem()
        {
            // [v0.1] if (Time.time - lastHackTime < HACK_COOLDOWN) return;
            Log($"Initiating system hack with range {hackRange}m");
            if (context == null) return true;

            // (v0.11) Remote Hack: whatever device is under the crosshair, at long range, with no tether to hold.
            var self = Network.NetworkPlayer.Resolve(context);
            if (self != null)
            {
                var device = Interaction.QuickHack.FindTarget(self, remoteHackRange);
                if (device != null && self.InstantHack(device)) return true;
            }
            // Nothing aimed at: fall back to the old behaviour for things that are not quick-hack devices
            // (vault doors, the security terminal).

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

        /// <summary>
        /// (v0.11) Shows every hackable device in range through walls on the Cipher's HUD, and reveals rivals
        /// standing next to one. Replaces Disable Camera: blinding a camera is now a quick hack anyone can do.
        /// </summary>
        private bool NetSight()
        {
            Log("Net Sight");
            if (context == null) return true;
            var self = Network.NetworkPlayer.Resolve(context);
            if (self == null) return false;

            self.ApplyTimedStatus(Core.StatusFlags.NetSight, netSightDuration);

            float sightRange = self.QuickHackRange * 2f;
            var devices = Interaction.QuickHack.Devices;
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d == null || d.Object == null) continue;
                if ((d.HackPoint - self.Position).sqrMagnitude > sightRange * sightRange) continue;
                foreach (var enemy in Network.NetworkPlayer.FindPlayersInRadius(d.HackPoint, netSightRevealRadius, context.Team, aliveOnly: true))
                {
                    enemy.ApplyTimedStatus(Core.StatusFlags.Revealed, netSightDuration * 0.5f);
                }
            }
            return true;
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
