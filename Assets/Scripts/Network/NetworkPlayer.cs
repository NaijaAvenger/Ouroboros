using UnityEngine;
using Fusion;

namespace Ouroboros.Network
{
    /// <summary>
    /// Base networked player class using Photon Fusion 2.
    /// Handles network synchronization for player state and class abilities.
    /// </summary>
    public class NetworkPlayer : NetworkBehaviour
    {
        [Header("Network Configuration")]
        [Networked] public PlayerRef PlayerRef { get; set; }
        [Networked] public Core.TeamID Team { get; set; }
        [Networked] public Core.PlayerClassType ClassType { get; set; }
        [Networked] public float Health { get; set; }
        [Networked] public float Stamina { get; set; }
        [Networked] public NetworkBool IsAlive { get; set; }
        
        [Header("Player Components")]
        [SerializeField] private Transform playerModel;
        
        private Core.BasePlayerClass currentClass;
        
        public override void Spawned()
        {
            if (Object.HasStateAuthority)
            {
                IsAlive = true;
                Health = 100f;
                Stamina = 100f;
            }
        }
        
        public override void FixedUpdateNetwork()
        {
            if (!IsAlive) return;
            
            if (currentClass != null)
            {
                currentClass.UpdateClass(Runner.DeltaTime);
            }
        }
        
        /// <summary>
        /// Assigns a class to this networked player.
        /// </summary>
        public void AssignClass(Core.PlayerClassType classType)
        {
            if (Object.HasStateAuthority)
            {
                ClassType = classType;
                RPC_AssignClass(classType);
            }
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_AssignClass(Core.PlayerClassType classType)
        {
            // Instantiate the appropriate class component
            if (currentClass != null)
            {
                Destroy(currentClass);
            }
            
            switch (classType)
            {
                case Core.PlayerClassType.Hacker:
                    currentClass = gameObject.AddComponent<Classes.HackerClass>();
                    break;
                case Core.PlayerClassType.Saboteur:
                    currentClass = gameObject.AddComponent<Classes.SaboteurClass>();
                    break;
                case Core.PlayerClassType.Demolitions:
                    currentClass = gameObject.AddComponent<Classes.DemolitionsClass>();
                    break;
                case Core.PlayerClassType.Agent:
                    currentClass = gameObject.AddComponent<Classes.AgentClass>();
                    break;
            }
            
            if (currentClass != null)
            {
                currentClass.Initialize();
                currentClass.OnClassSelected();
                Health = currentClass.MaxHealth;
                Stamina = currentClass.MaxStamina;
            }
        }
        
        /// <summary>
        /// Triggers a class ability across the network.
        /// </summary>
        public void UseAbility(int abilityIndex)
        {
            if (Object.HasInputAuthority && currentClass != null)
            {
                RPC_UseAbility(abilityIndex);
            }
        }
        
        [Rpc(RpcSources.InputAuthority, RpcTargets.StateAuthority)]
        private void RPC_UseAbility(int abilityIndex)
        {
            if (currentClass != null && IsAlive)
            {
                currentClass.UseAbility(abilityIndex);
            }
        }
        
        /// <summary>
        /// Applies damage to the networked player.
        /// </summary>
        public void TakeDamage(float damage)
        {
            if (Object.HasStateAuthority)
            {
                if (currentClass != null)
                {
                    currentClass.TakeDamage(damage);
                    Health = Mathf.Max(0, Health - damage);
                    
                    if (Health <= 0)
                    {
                        IsAlive = false;
                        RPC_OnPlayerDeath();
                    }
                }
            }
        }
        
        [Rpc(RpcSources.StateAuthority, RpcTargets.All)]
        private void RPC_OnPlayerDeath()
        {
            Debug.Log($"[NetworkPlayer] Player {PlayerRef} died");
            // Handle death animations, respawn logic, etc.
        }
    }
}
