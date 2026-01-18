using UnityEngine;
using UnityEngine.AI;

namespace Ouroboros.AI
{
    /// <summary>
    /// Interface for AI agents. Designed for modularity and future AI expansion.
    /// </summary>
    public interface IAIAgent
    {
        AIAgentType AgentType { get; }
        AIBehaviorState CurrentState { get; }
        
        void Initialize();
        void UpdateBehavior(float deltaTime);
        void SetTarget(Transform target);
        void OnDamaged(float damage);
    }
    
    /// <summary>
    /// AI agent types for different NPC roles.
    /// </summary>
    public enum AIAgentType
    {
        Guard,
        Patrol,
        Elite,
        Boss,
        Civilian
    }
    
    /// <summary>
    /// AI behavior states.
    /// </summary>
    public enum AIBehaviorState
    {
        Idle,
        Patrol,
        Alert,
        Combat,
        Fleeing,
        Investigating
    }
    
    /// <summary>
    /// Base AI agent class providing common AI functionality.
    /// </summary>
    public abstract class BaseAIAgent : MonoBehaviour, IAIAgent
    {
        [Header("AI Configuration")]
        [SerializeField] protected AIAgentType agentType;
        [SerializeField] protected float detectionRange = 15f;
        [SerializeField] protected float attackRange = 5f;
        [SerializeField] protected float fieldOfView = 120f;
        
        [Header("AI Stats")]
        [SerializeField] protected float health = 100f;
        [SerializeField] protected float movementSpeed = 3.5f;
        [SerializeField] protected float damage = 15f;
        
        protected AIBehaviorState currentState;
        protected Transform currentTarget;
        protected NavMeshAgent navAgent;
        protected bool isInitialized;
        
        public AIAgentType AgentType => agentType;
        public AIBehaviorState CurrentState => currentState;
        
        protected virtual void Awake()
        {
            navAgent = GetComponent<NavMeshAgent>();
            if (navAgent != null)
            {
                navAgent.speed = movementSpeed;
            }
        }
        
        public virtual void Initialize()
        {
            if (isInitialized) return;
            
            currentState = AIBehaviorState.Idle;
            isInitialized = true;
            
            OnInitialize();
        }
        
        public virtual void UpdateBehavior(float deltaTime)
        {
            if (!isInitialized) return;
            
            switch (currentState)
            {
                case AIBehaviorState.Idle:
                    UpdateIdleBehavior(deltaTime);
                    break;
                case AIBehaviorState.Patrol:
                    UpdatePatrolBehavior(deltaTime);
                    break;
                case AIBehaviorState.Alert:
                    UpdateAlertBehavior(deltaTime);
                    break;
                case AIBehaviorState.Combat:
                    UpdateCombatBehavior(deltaTime);
                    break;
                case AIBehaviorState.Investigating:
                    UpdateInvestigatingBehavior(deltaTime);
                    break;
                case AIBehaviorState.Fleeing:
                    UpdateFleeingBehavior(deltaTime);
                    break;
            }
        }
        
        public virtual void SetTarget(Transform target)
        {
            currentTarget = target;
            if (target != null)
            {
                TransitionToState(AIBehaviorState.Combat);
            }
        }
        
        public virtual void OnDamaged(float damage)
        {
            health -= damage;
            
            if (health <= 0)
            {
                OnDeath();
            }
            else
            {
                OnTakeDamage();
            }
        }
        
        protected virtual void TransitionToState(AIBehaviorState newState)
        {
            if (currentState == newState) return;
            
            OnExitState(currentState);
            currentState = newState;
            OnEnterState(newState);
        }
        
        protected virtual void OnInitialize() { }
        protected virtual void OnEnterState(AIBehaviorState state) { }
        protected virtual void OnExitState(AIBehaviorState state) { }
        
        protected virtual void UpdateIdleBehavior(float deltaTime) { }
        protected virtual void UpdatePatrolBehavior(float deltaTime) { }
        protected virtual void UpdateAlertBehavior(float deltaTime) { }
        protected virtual void UpdateCombatBehavior(float deltaTime) { }
        protected virtual void UpdateInvestigatingBehavior(float deltaTime) { }
        protected virtual void UpdateFleeingBehavior(float deltaTime) { }
        
        protected virtual void OnTakeDamage()
        {
            TransitionToState(AIBehaviorState.Alert);
        }
        
        protected virtual void OnDeath()
        {
            Debug.Log($"[AI] {agentType} agent died");
            // Handle death animation, despawn, etc.
        }
    }
}
