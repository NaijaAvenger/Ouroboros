using System;
using UnityEngine;
using System.Collections.Generic;

namespace Ouroboros.AI
{
    /// <summary>
    /// AI behavior tree node system for modular AI decision-making.
    /// Designed for easy expansion of AI behaviors.
    ///
    /// v0.2: fixed the sequence-completion bug, added <see cref="Reset"/> so composites can abort running
    /// children when a higher-priority branch takes over, and added inverter / lambda nodes.
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

        /// <summary>Clears any in-progress bookkeeping (e.g. a sequence's current child).</summary>
        public virtual void Reset() { }
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
            // [v0.1] if (currentChildIndex >= children.Count) { currentChildIndex = 0; state = Success; return state; }
            // [v0.1] BUG: after a full pass the next Evaluate returned Success without running any child.
            if (children.Count == 0)
            {
                state = NodeState.Success;
                return state;
            }

            while (currentChildIndex < children.Count)
            {
                NodeState childState = children[currentChildIndex].Evaluate();

                switch (childState)
                {
                    case NodeState.Running:
                        state = NodeState.Running;
                        return state;
                    case NodeState.Failure:
                        ResetChildrenFrom(0);
                        currentChildIndex = 0;
                        state = NodeState.Failure;
                        return state;
                    case NodeState.Success:
                        currentChildIndex++;
                        break;
                }
            }

            // Every child succeeded this pass; start over next time.
            currentChildIndex = 0;
            state = NodeState.Success;
            return state;
        }

        public override void Reset()
        {
            ResetChildrenFrom(0);
            currentChildIndex = 0;
        }

        private void ResetChildrenFrom(int index)
        {
            for (int i = index; i < children.Count; i++) children[i].Reset();
        }
    }

    /// <summary>
    /// Composite node that tries children in priority order until one succeeds or is running.
    /// Re-evaluates from the first child every tick (reactive), resetting any lower-priority child that
    /// was previously running.
    /// </summary>
    public class SelectorNode : BehaviorNode
    {
        private List<BehaviorNode> children = new List<BehaviorNode>();
        private int runningChild = -1;

        public SelectorNode(params BehaviorNode[] nodes)
        {
            children.AddRange(nodes);
        }

        public override NodeState Evaluate()
        {
            for (int i = 0; i < children.Count; i++)
            {
                NodeState childState = children[i].Evaluate();

                switch (childState)
                {
                    case NodeState.Running:
                    case NodeState.Success:
                        if (runningChild >= 0 && runningChild != i) children[runningChild].Reset();
                        runningChild = childState == NodeState.Running ? i : -1;
                        state = childState;
                        return state;
                }
            }

            runningChild = -1;
            state = NodeState.Failure;
            return state;
        }

        public override void Reset()
        {
            foreach (var child in children) child.Reset();
            runningChild = -1;
        }
    }

    /// <summary>Flips Success and Failure; Running passes through.</summary>
    public class InverterNode : BehaviorNode
    {
        private readonly BehaviorNode child;

        public InverterNode(BehaviorNode child)
        {
            this.child = child;
        }

        public override NodeState Evaluate()
        {
            NodeState childState = child.Evaluate();
            state = childState == NodeState.Success ? NodeState.Failure
                  : childState == NodeState.Failure ? NodeState.Success
                  : NodeState.Running;
            return state;
        }

        public override void Reset() => child.Reset();
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

    /// <summary>Condition built from a delegate, for compact tree definitions.</summary>
    public class ConditionFunc : ConditionNode
    {
        private readonly Func<bool> check;
        public ConditionFunc(Func<bool> check) { this.check = check; }
        protected override bool CheckCondition() => check != null && check();
    }

    /// <summary>Action built from a delegate, for compact tree definitions.</summary>
    public class ActionFunc : ActionNode
    {
        private readonly Func<NodeState> action;
        private readonly Action onReset;
        public ActionFunc(Func<NodeState> action, Action onReset = null) { this.action = action; this.onReset = onReset; }
        protected override NodeState ExecuteAction() => action != null ? action() : NodeState.Failure;
        public override void Reset() => onReset?.Invoke();
    }
}
