using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class BarNpcBehaviorRules
    {
        private static readonly BarNpcAction[] BartenderActions =
        {
            BarNpcAction.Idle,
            BarNpcAction.WipeCounter,
            BarNpcAction.Serve,
            BarNpcAction.Gesture
        };

        private static readonly BarNpcAction[] WalkerActions =
        {
            BarNpcAction.Idle,
            BarNpcAction.Walk,
            BarNpcAction.Walk,
            BarNpcAction.Listen
        };

        private static readonly BarNpcAction[] SeatedPatronActions =
        {
            BarNpcAction.Idle,
            BarNpcAction.Sip,
            BarNpcAction.Listen,
            BarNpcAction.Gesture
        };

        private static readonly BarNpcAction[] StandingPatronActions =
        {
            BarNpcAction.Talk,
            BarNpcAction.Listen,
            BarNpcAction.Sip,
            BarNpcAction.Gesture,
            BarNpcAction.Idle
        };

        private static readonly BarNpcAction[] PerformerActions =
        {
            BarNpcAction.Perform,
            BarNpcAction.Gesture,
            BarNpcAction.Perform,
            BarNpcAction.Idle
        };

        public static BarNpcAction SelectAction(
            BarNpcDefinition definition,
            int sequence)
        {
            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence));
            }

            BarNpcAction[] actions = GetActions(definition.Role);
            uint hash = BarNpcStableHash.Combine(
                definition.BehaviorSeed,
                unchecked((uint)sequence));
            BarNpcAction selected =
                actions[(int)(hash % (uint)actions.Length)];
            if (selected == BarNpcAction.Walk &&
                !definition.Mobile)
            {
                return BarNpcAction.Idle;
            }

            return selected;
        }

        public static int GetDurationTicks(
            BarNpcDefinition definition,
            BarNpcAction action,
            int sequence)
        {
            if (sequence < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(sequence));
            }

            uint hash = BarNpcStableHash.Combine(
                BarNpcStableHash.Combine(
                    definition.BehaviorSeed,
                    unchecked((uint)sequence)),
                unchecked((uint)action + 0x44555241u));
            int minimum;
            int range;
            switch (action)
            {
                case BarNpcAction.Walk:
                    minimum = 16;
                    range = 17;
                    break;
                case BarNpcAction.Talk:
                case BarNpcAction.Listen:
                case BarNpcAction.WatchActivity:
                case BarNpcAction.Perform:
                    minimum = 12;
                    range = 13;
                    break;
                default:
                    minimum = 8;
                    range = 13;
                    break;
            }

            return minimum + (int)(hash % (uint)range);
        }

        public static bool IsAllowed(
            BarNpcDefinition definition,
            BarNpcAction action)
        {
            if (action == BarNpcAction.Walk &&
                !definition.Mobile)
            {
                return false;
            }

            BarNpcAction[] actions = GetActions(definition.Role);
            for (int index = 0; index < actions.Length; index++)
            {
                if (actions[index] == action)
                {
                    return true;
                }
            }

            return action == BarNpcAction.Idle;
        }

        private static BarNpcAction[] GetActions(BarNpcRole role)
        {
            switch (role)
            {
                case BarNpcRole.Bartender:
                    return BartenderActions;
                case BarNpcRole.Walker:
                    return WalkerActions;
                case BarNpcRole.StandingPatron:
                    return StandingPatronActions;
                case BarNpcRole.Performer:
                    return PerformerActions;
                default:
                    return SeatedPatronActions;
            }
        }
    }

    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class BarNpcDirector : MonoBehaviour
    {
        public const float DecisionStepSeconds = 0.125f;

        private readonly List<BarNpcActor> actors =
            new List<BarNpcActor>();
        private readonly List<ActorState> states =
            new List<ActorState>();
        private float decisionAccumulator;
        private Camera depthCamera;
        private Transform depthReference;

        public bool IsInitialized { get; private set; }
        public BarNpcPlan Plan { get; private set; }
        public IReadOnlyList<BarNpcActor> Actors => actors;
        public int DecisionTickCount { get; private set; }

        public void Initialize(
            BarNpcPlan plan,
            IReadOnlyList<BarNpcActor> createdActors)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The bar NPC director is already initialized.");
            }

            Plan = plan ?? throw new ArgumentNullException(
                nameof(plan));
            if (createdActors == null)
            {
                throw new ArgumentNullException(
                    nameof(createdActors));
            }

            if (createdActors.Count != plan.Count)
            {
                throw new ArgumentException(
                    "The actor count must match the NPC plan.",
                    nameof(createdActors));
            }

            for (int index = 0;
                 index < createdActors.Count;
                 index++)
            {
                BarNpcActor actor = createdActors[index];
                if (actor == null ||
                    !actor.IsInitialized ||
                    actor.Definition != plan.Definitions[index])
                {
                    throw new ArgumentException(
                        "Actors must be initialized in plan order.",
                        nameof(createdActors));
                }

                actors.Add(actor);
                BarNpcAction action =
                    BarNpcBehaviorRules.SelectAction(
                        actor.Definition,
                        0);
                int duration =
                    BarNpcBehaviorRules.GetDurationTicks(
                        actor.Definition,
                        action,
                        0);
                states.Add(new ActorState(
                    0,
                    duration,
                    action));
                actor.SetAction(action);
            }

            IsInitialized = true;
        }

        public void ConfigureDepthSorting(
            Camera camera,
            Transform reference)
        {
            depthCamera = camera;
            depthReference = reference;
            UpdateDepthSorting();
        }

        public int GetActionSequence(int actorIndex)
        {
            ValidateActorIndex(actorIndex);
            return states[actorIndex].Sequence;
        }

        public int GetRemainingActionTicks(int actorIndex)
        {
            ValidateActorIndex(actorIndex);
            return states[actorIndex].RemainingTicks;
        }

        public void Advance(float deltaTime)
        {
            if (!IsInitialized)
            {
                return;
            }

            float safeDeltaTime =
                float.IsNaN(deltaTime) ||
                float.IsInfinity(deltaTime)
                    ? 0f
                    : Mathf.Max(0f, deltaTime);
            decisionAccumulator += safeDeltaTime;
            while (decisionAccumulator + 0.000001f >=
                   DecisionStepSeconds)
            {
                decisionAccumulator -= DecisionStepSeconds;
                TickDecisions();
            }

            for (int index = 0; index < actors.Count; index++)
            {
                actors[index].AdvancePresentation(safeDeltaTime);
            }

            UpdateDepthSorting();
        }

        private void LateUpdate()
        {
            Advance(Time.deltaTime);
        }

        private void UpdateDepthSorting()
        {
            if (depthCamera == null || depthReference == null)
            {
                return;
            }

            Transform cameraTransform = depthCamera.transform;
            Vector3 cameraPosition = cameraTransform.position;
            Vector3 cameraForward = cameraTransform.forward;
            float referenceDepth = Vector3.Dot(
                depthReference.position - cameraPosition,
                cameraForward);
            for (int index = 0; index < actors.Count; index++)
            {
                BarNpcActor actor = actors[index];
                float actorDepth = Vector3.Dot(
                    actor.transform.position - cameraPosition,
                    cameraForward);
                actor.Renderer.sortingOrder =
                    actorDepth < referenceDepth
                        ? 10
                        : -10;
            }
        }

        private void TickDecisions()
        {
            DecisionTickCount++;
            for (int index = 0; index < states.Count; index++)
            {
                ActorState state = states[index];
                state.RemainingTicks--;
                if (state.RemainingTicks <= 0)
                {
                    state.Sequence++;
                    state.Action =
                        BarNpcBehaviorRules.SelectAction(
                            actors[index].Definition,
                            state.Sequence);
                    state.RemainingTicks =
                        BarNpcBehaviorRules.GetDurationTicks(
                            actors[index].Definition,
                            state.Action,
                            state.Sequence);
                    actors[index].SetAction(state.Action);
                }

                states[index] = state;
            }
        }

        private void ValidateActorIndex(int actorIndex)
        {
            if (actorIndex < 0 || actorIndex >= states.Count)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(actorIndex));
            }
        }

        private struct ActorState
        {
            public ActorState(
                int sequence,
                int remainingTicks,
                BarNpcAction action)
            {
                Sequence = sequence;
                RemainingTicks = remainingTicks;
                Action = action;
            }

            public int Sequence;
            public int RemainingTicks;
            public BarNpcAction Action;
        }
    }
}
