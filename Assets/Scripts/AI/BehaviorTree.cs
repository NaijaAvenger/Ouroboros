using UnityEngine;
using System.Collections.Generic;

namespace Ouroboros.AI
{
    /// <summary>
    /// AI behavior tree node system for modular AI decision-making.
    /// Designed for easy expansion of AI behaviors.
    /// </summary>
    public abstract class BehaviorNode
    {
        public enum NodeState
        {
            Running,
            Success,
            Failure
        }
        
        protected NodeState state;
        public NodeState State => state;
        
        public abstract NodeState Evaluate();
    }
    
    /// <summary>
    /// Composite node that executes children in sequence.
    /// </summary>
    public class SequenceNode : BehaviorNode
    {
        private List<BehaviorNode> children = new List<BehaviorNode>();
        private int currentChildIndex = 0;
        
        public SequenceNode(params BehaviorNode[] nodes)
        {
            children.AddRange(nodes);
        }
        
        public override NodeState Evaluate()
        {
            if (currentChildIndex >= children.Count)
            {
                currentChildIndex = 0;
                state = NodeState.Success;
                return state;
            }
            
            NodeState childState = children[currentChildIndex].Evaluate();
            
            switch (childState)
            {
                case NodeState.Running:
                    state = NodeState.Running;
                    break;
                case NodeState.Success:
                    currentChildIndex++;
                    state = currentChildIndex >= children.Count ? NodeState.Success : NodeState.Running;
                    break;
                case NodeState.Failure:
                    currentChildIndex = 0;
                    state = NodeState.Failure;
                    break;
            }
            
            return state;
        }
    }
    
    /// <summary>
    /// Composite node that tries children until one succeeds.
    /// </summary>
    public class SelectorNode : BehaviorNode
    {
        private List<BehaviorNode> children = new List<BehaviorNode>();
        
        public SelectorNode(params BehaviorNode[] nodes)
        {
            children.AddRange(nodes);
        }
        
        public override NodeState Evaluate()
        {
            foreach (var child in children)
            {
                NodeState childState = child.Evaluate();
                
                switch (childState)
                {
                    case NodeState.Running:
                    case NodeState.Success:
                        state = childState;
                        return state;
                }
            }
            
            state = NodeState.Failure;
            return state;
        }
    }
    
    /// <summary>
    /// Base class for condition checks in behavior trees.
    /// </summary>
    public abstract class ConditionNode : BehaviorNode
    {
        protected abstract bool CheckCondition();
        
        public override NodeState Evaluate()
        {
            state = CheckCondition() ? NodeState.Success : NodeState.Failure;
            return state;
        }
    }
    
    /// <summary>
    /// Base class for action nodes in behavior trees.
    /// </summary>
    public abstract class ActionNode : BehaviorNode
    {
        protected abstract NodeState ExecuteAction();
        
        public override NodeState Evaluate()
        {
            state = ExecuteAction();
            return state;
        }
    }
}
