using System.Collections.Generic;
using NUnit.Framework;
using Ouroboros.AI;
using State = Ouroboros.AI.BehaviorNode.NodeState;

namespace Ouroboros.Tests
{
    public class BehaviorTreeTests
    {
        /// <summary>Action that returns a scripted state and records each call and reset.</summary>
        private sealed class Probe
        {
            public State Result;
            public int Calls;
            public int Resets;
            public readonly ActionFunc Node;
            public Probe(State result, List<string> log = null, string name = null)
            {
                Result = result;
                Node = new ActionFunc(() => { Calls++; log?.Add(name); return Result; }, () => Resets++);
            }
        }

        // ---- Sequence ----

        [Test]
        public void Sequence_RunsChildrenInOrder_AndSucceeds()
        {
            var log = new List<string>();
            var a = new Probe(State.Success, log, "a");
            var b = new Probe(State.Success, log, "b");
            var seq = new SequenceNode(a.Node, b.Node);

            Assert.That(seq.Evaluate(), Is.EqualTo(State.Success));
            Assert.That(log, Is.EqualTo(new[] { "a", "b" }));
        }

        [Test]
        public void Sequence_AfterAFullPass_EvaluatesItsChildrenAgain()
        {
            // Regression for the v0.1 bug: the call after a full pass returned Success without running anything.
            var a = new Probe(State.Success);
            var b = new Probe(State.Success);
            var seq = new SequenceNode(a.Node, b.Node);

            seq.Evaluate();
            seq.Evaluate();

            Assert.That(a.Calls, Is.EqualTo(2));
            Assert.That(b.Calls, Is.EqualTo(2));
        }

        [Test]
        public void Sequence_ResumesAtTheRunningChild()
        {
            var a = new Probe(State.Success);
            var b = new Probe(State.Running);
            var c = new Probe(State.Success);
            var seq = new SequenceNode(a.Node, b.Node, c.Node);

            Assert.That(seq.Evaluate(), Is.EqualTo(State.Running));
            Assert.That(seq.Evaluate(), Is.EqualTo(State.Running));
            Assert.That(a.Calls, Is.EqualTo(1), "a completed; it must not run again while b is still running");
            Assert.That(b.Calls, Is.EqualTo(2));
            Assert.That(c.Calls, Is.EqualTo(0));

            b.Result = State.Success;
            Assert.That(seq.Evaluate(), Is.EqualTo(State.Success));
            Assert.That(c.Calls, Is.EqualTo(1));
        }

        [Test]
        public void Sequence_FailureStopsThePass_AndRestartsFromTheFirstChild()
        {
            var a = new Probe(State.Success);
            var b = new Probe(State.Failure);
            var c = new Probe(State.Success);
            var seq = new SequenceNode(a.Node, b.Node, c.Node);

            Assert.That(seq.Evaluate(), Is.EqualTo(State.Failure));
            Assert.That(c.Calls, Is.EqualTo(0));

            seq.Evaluate();
            Assert.That(a.Calls, Is.EqualTo(2), "the next pass starts over at the first child");
        }

        [Test]
        public void Sequence_Empty_Succeeds()
        {
            Assert.That(new SequenceNode().Evaluate(), Is.EqualTo(State.Success));
        }

        // ---- Selector ----

        [Test]
        public void Selector_StopsAtTheFirstChildThatDoesNotFail()
        {
            var a = new Probe(State.Failure);
            var b = new Probe(State.Success);
            var c = new Probe(State.Success);
            var sel = new SelectorNode(a.Node, b.Node, c.Node);

            Assert.That(sel.Evaluate(), Is.EqualTo(State.Success));
            Assert.That(a.Calls, Is.EqualTo(1));
            Assert.That(b.Calls, Is.EqualTo(1));
            Assert.That(c.Calls, Is.EqualTo(0));
        }

        [Test]
        public void Selector_AllChildrenFail_Fails()
        {
            var sel = new SelectorNode(new Probe(State.Failure).Node, new Probe(State.Failure).Node);
            Assert.That(sel.Evaluate(), Is.EqualTo(State.Failure));
            Assert.That(new SelectorNode().Evaluate(), Is.EqualTo(State.Failure));
        }

        [Test]
        public void Selector_IsReactive_AHigherPriorityChildInterruptsARunningOne()
        {
            // Guard pattern: [fight visible target] > [patrol]. Patrol is running; then a target appears.
            var fight = new Probe(State.Failure);
            var patrol = new Probe(State.Running);
            var sel = new SelectorNode(fight.Node, patrol.Node);

            Assert.That(sel.Evaluate(), Is.EqualTo(State.Running));
            Assert.That(patrol.Resets, Is.EqualTo(0));

            fight.Result = State.Running;
            Assert.That(sel.Evaluate(), Is.EqualTo(State.Running));
            Assert.That(patrol.Calls, Is.EqualTo(1), "patrol must not be evaluated once fight takes over");
            Assert.That(patrol.Resets, Is.EqualTo(1), "the displaced branch is reset so it starts clean next time");
        }

        [Test]
        public void Selector_DoesNotResetTheChildThatKeepsRunning()
        {
            var a = new Probe(State.Failure);
            var b = new Probe(State.Running);
            var sel = new SelectorNode(a.Node, b.Node);

            sel.Evaluate();
            sel.Evaluate();
            sel.Evaluate();

            Assert.That(b.Resets, Is.EqualTo(0));
        }

        [Test]
        public void Selector_Reset_ResetsEveryChild()
        {
            var a = new Probe(State.Failure);
            var b = new Probe(State.Running);
            var sel = new SelectorNode(a.Node, b.Node);
            sel.Evaluate();

            sel.Reset();

            Assert.That(a.Resets, Is.EqualTo(1));
            Assert.That(b.Resets, Is.EqualTo(1));
        }

        // ---- Decorators and leaves ----

        [TestCase(State.Success, State.Failure)]
        [TestCase(State.Failure, State.Success)]
        [TestCase(State.Running, State.Running)]
        public void Inverter_FlipsSuccessAndFailure(State child, State expected)
        {
            Assert.That(new InverterNode(new Probe(child).Node).Evaluate(), Is.EqualTo(expected));
        }

        [Test]
        public void ConditionFunc_MapsBoolToState_AndNullIsFailure()
        {
            Assert.That(new ConditionFunc(() => true).Evaluate(), Is.EqualTo(State.Success));
            Assert.That(new ConditionFunc(() => false).Evaluate(), Is.EqualTo(State.Failure));
            Assert.That(new ConditionFunc(null).Evaluate(), Is.EqualTo(State.Failure));
        }

        [Test]
        public void NestedTree_SequenceInsideSelector_ResumesAndCompletes()
        {
            // selector( sequence(canSee, shoot[running->success]), idle )
            bool canSee = true;
            var shoot = new Probe(State.Running);
            var idle = new Probe(State.Success);
            var tree = new SelectorNode(new SequenceNode(new ConditionFunc(() => canSee), shoot.Node), idle.Node);

            Assert.That(tree.Evaluate(), Is.EqualTo(State.Running));
            Assert.That(idle.Calls, Is.EqualTo(0));

            canSee = false; // target lost: the sequence is still parked on "shoot", so it keeps running it
            Assert.That(tree.Evaluate(), Is.EqualTo(State.Running));

            shoot.Result = State.Success;
            Assert.That(tree.Evaluate(), Is.EqualTo(State.Success));

            // New pass: condition is re-checked first, fails, selector falls through to idle.
            Assert.That(tree.Evaluate(), Is.EqualTo(State.Success));
            Assert.That(idle.Calls, Is.EqualTo(1));
        }
    }
}
