using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Lets the roaming population live a little: every few seconds one
    /// walker near a free bench may divert to it, sit for a while and
    /// walk on. Seats are claimed through the shared bench registry, so
    /// resters, other resters and the hero never share a plank.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityBenchNpcRestController : MonoBehaviour
    {
        public const string RuntimeRootName = "City Bench Rest Runtime";
        public const int MaximumSimultaneousResters = 2;
        public const float AttemptIntervalSeconds = 3.5f;
        public const float AttemptProbability = 0.4f;

        /// <summary>Graph walk a walker will accept for a rest.</summary>
        public const float MaximumApproachDistance = 30f;

        /// <summary>An approach that drags this long is abandoned.</summary>
        public const float ApproachTimeoutSeconds = 45f;

        public const float MinimumSitSeconds = 15f;
        public const float MaximumSitSeconds = 30f;

        /// <summary>A return crossing that drags this long is let go.</summary>
        public const float StandTimeoutSeconds = 10f;

        private enum RestPhase
        {
            Approaching = 0,
            Sitting = 1,
            Standing = 2
        }

        private sealed class Rest
        {
            public CityPedestrianActor Walker;
            public CityBenchRestPoint Point;
            public Transform SeatAnchor;
            public RestPhase Phase;
            public float PhaseSeconds;
            public float SitDuration;
        }

        private readonly List<Rest> rests = new List<Rest>();
        private CityBenchRestPlan plan;
        private CityPedestrianDirector director;
        private uint randomState;
        private float nextAttemptTime;

        public bool IsInitialized { get; private set; }
        public int ActiveResterCount => rests.Count;
        public CityBenchRestPlan Plan => plan;

        public static CityBenchNpcRestController Create(
            Transform parent,
            CityBenchRestPlan restPlan,
            CityPedestrianDirector pedestrianDirector,
            int seed)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            var root = new GameObject(RuntimeRootName);
            root.transform.SetParent(parent, false);
            var controller =
                root.AddComponent<CityBenchNpcRestController>();
            controller.Initialize(restPlan, pedestrianDirector, seed);
            return controller;
        }

        public void Initialize(
            CityBenchRestPlan restPlan,
            CityPedestrianDirector pedestrianDirector,
            int seed)
        {
            plan = restPlan ??
                   throw new ArgumentNullException(nameof(restPlan));
            director = pedestrianDirector ??
                       throw new ArgumentNullException(
                           nameof(pedestrianDirector));
            randomState = unchecked((uint)seed * 2654435761u) | 1u;
            IsInitialized = true;
            GameLog.Info(
                "city",
                "bench_rest_initialized",
                GameLog.Field("rest_point_count", plan.Points.Count));
        }

        private void Update()
        {
            if (!IsInitialized)
            {
                return;
            }

            AdvanceRests(Time.deltaTime);
            // Scaled time, like the rest phases: a paused simulation
            // must not keep claiming benches for frozen walkers on a
            // wall-clock cadence.
            if (Time.time >= nextAttemptTime)
            {
                nextAttemptTime =
                    Time.time + AttemptIntervalSeconds;
                TryStartRest();
            }
        }

        private void AdvanceRests(float deltaTime)
        {
            for (int index = rests.Count - 1; index >= 0; index--)
            {
                Rest rest = rests[index];
                rest.PhaseSeconds += deltaTime;
                if (rest.Walker == null || !rest.Walker.IsSpawned)
                {
                    EndRest(index, false);
                    continue;
                }

                if (rest.Phase == RestPhase.Approaching)
                {
                    AdvanceApproach(index, rest);
                }
                else if (rest.Phase == RestPhase.Sitting)
                {
                    AdvanceSitting(index, rest);
                }
                else
                {
                    AdvanceStanding(index, rest);
                }
            }
        }

        private void AdvanceApproach(int index, Rest rest)
        {
            CityPedestrianMotionState state = rest.Walker.MotionState;
            if (state == CityPedestrianMotionState.WaitingAtBench)
            {
                if (!TrySeat(rest))
                {
                    rest.Walker.CancelBenchActivity();
                    EndRest(index, false);
                }

                return;
            }

            bool approaching =
                state == CityPedestrianMotionState.ApproachingBench ||
                state == CityPedestrianMotionState.WalkingToBenchSeat;
            if (!approaching ||
                rest.PhaseSeconds >= ApproachTimeoutSeconds)
            {
                rest.Walker.CancelBenchActivity();
                EndRest(index, false);
            }
        }

        private void AdvanceSitting(int index, Rest rest)
        {
            if (rest.Walker.MotionState !=
                CityPedestrianMotionState.SittingOnBench)
            {
                EndRest(index, false);
                return;
            }

            if (rest.PhaseSeconds < rest.SitDuration)
            {
                return;
            }

            // The claim is held through the return crossing: releasing
            // here would re-arm the hero's sit prompt (and free the
            // bench for the next rester) while the previous sitter is
            // still standing on the seat-front slot.
            rest.Walker.StandUpFromBench(rest.Point.StandSlot);
            rest.Phase = RestPhase.Standing;
            rest.PhaseSeconds = 0f;
        }

        private void AdvanceStanding(int index, Rest rest)
        {
            if (rest.Walker.MotionState ==
                CityPedestrianMotionState.ReturningFromBench &&
                rest.PhaseSeconds < StandTimeoutSeconds)
            {
                return;
            }

            EndRest(index, true);
        }

        private bool TrySeat(Rest rest)
        {
            if (!CityPedestrianResources.TryGetArchetype(
                    rest.Walker.DesignId,
                    out CityPedestrianArchetype archetype) ||
                archetype.SeatedRide == null)
            {
                return false;
            }

            var anchor = new GameObject(
                $"Bench Seat Anchor {rest.Point.BenchId}");
            anchor.transform.SetParent(transform, false);
            anchor.transform.SetPositionAndRotation(
                rest.Point.SeatPosition,
                Quaternion.LookRotation(
                    rest.Point.SitFacing,
                    Vector3.up));
            if (!rest.Walker.BeginBenchSit(
                    rest.Point.SeatPosition,
                    anchor.transform.rotation,
                    anchor.transform,
                    archetype.SeatedRide))
            {
                Destroy(anchor);
                return false;
            }

            rest.SeatAnchor = anchor.transform;
            rest.Phase = RestPhase.Sitting;
            rest.PhaseSeconds = 0f;
            rest.SitDuration = Mathf.Lerp(
                MinimumSitSeconds,
                MaximumSitSeconds,
                NextRandom01());
            GameLog.Info(
                "city",
                "bench_rest_seated",
                GameLog.Field("bench_id", rest.Point.BenchId),
                GameLog.Field("sit_seconds", rest.SitDuration));
            return true;
        }

        private void TryStartRest()
        {
            if (plan.Points.Count == 0 ||
                rests.Count >= MaximumSimultaneousResters ||
                NextRandom01() > AttemptProbability)
            {
                return;
            }

            int pointOffset = (int)(NextRandomState() %
                                    (uint)plan.Points.Count);
            for (int probe = 0; probe < plan.Points.Count; probe++)
            {
                CityBenchRestPoint point = plan.Points[
                    (pointOffset + probe) % plan.Points.Count];
                if (CityBenchSeatClaims.IsClaimed(point.BenchId))
                {
                    continue;
                }

                CityPedestrianActor walker = FindWalkerFor(point);
                if (walker == null)
                {
                    continue;
                }

                var rest = new Rest
                {
                    Walker = walker,
                    Point = point,
                    Phase = RestPhase.Approaching
                };
                if (!CityBenchSeatClaims.TryClaim(point.BenchId, rest))
                {
                    continue;
                }

                if (!walker.BeginBenchApproach(
                        point.NodeIndex,
                        point.StandSlot,
                        -point.SitFacing,
                        point.NodeDistances))
                {
                    CityBenchSeatClaims.Release(point.BenchId, rest);
                    continue;
                }

                rests.Add(rest);
                return;
            }
        }

        private CityPedestrianActor FindWalkerFor(
            CityBenchRestPoint point)
        {
            CityPedestrianActor best = null;
            float bestDistance = MaximumApproachDistance;
            IReadOnlyList<CityPedestrianActor> actors = director.Actors;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor actor = actors[index];
                if (actor == null ||
                    !actor.IsSpawned ||
                    actor.MotionState !=
                    CityPedestrianMotionState.Walking ||
                    actor.TargetNodeIndex < 0 ||
                    actor.TargetNodeIndex >=
                    point.NodeDistances.Count ||
                    IsBusy(actor))
                {
                    continue;
                }

                float graphDistance =
                    point.NodeDistances[actor.TargetNodeIndex];
                if (float.IsPositiveInfinity(graphDistance))
                {
                    continue;
                }

                Vector3 toTarget = actor.Position - point.StandSlot;
                toTarget.y = 0f;
                float total = graphDistance + toTarget.magnitude;
                if (total >= bestDistance)
                {
                    continue;
                }

                if (!CityPedestrianResources.TryGetArchetype(
                        actor.DesignId,
                        out CityPedestrianArchetype archetype) ||
                    archetype.SeatedRide == null)
                {
                    continue;
                }

                bestDistance = total;
                best = actor;
            }

            return best;
        }

        private bool IsBusy(CityPedestrianActor actor)
        {
            for (int index = 0; index < rests.Count; index++)
            {
                if (ReferenceEquals(rests[index].Walker, actor))
                {
                    return true;
                }
            }

            return false;
        }

        private void EndRest(int index, bool stoodUp)
        {
            Rest rest = rests[index];
            CityBenchSeatClaims.Release(rest.Point.BenchId, rest);
            if (rest.SeatAnchor != null)
            {
                Destroy(rest.SeatAnchor.gameObject);
            }

            rests.RemoveAt(index);
            if (stoodUp)
            {
                GameLog.Info(
                    "city",
                    "bench_rest_finished",
                    GameLog.Field("bench_id", rest.Point.BenchId));
            }
        }

        private void OnDestroy()
        {
            for (int index = rests.Count - 1; index >= 0; index--)
            {
                CityBenchSeatClaims.Release(
                    rests[index].Point.BenchId,
                    rests[index]);
            }

            rests.Clear();
        }

        private uint NextRandomState()
        {
            uint state = randomState;
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            randomState = state;
            return state;
        }

        private float NextRandom01()
        {
            return (NextRandomState() & 0xFFFFFFu) / 16777216f;
        }
    }
}
