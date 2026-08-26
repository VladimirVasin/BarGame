using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The Ferryman looking left before he turns across the road.
    ///
    /// The turn off the street into the tunnel forecourt is the only place on
    /// either leg where the car leaves its own lane: it crosses the oncoming
    /// carriageway and the pavement in front of the opening. So it is the
    /// only place either leg gives way, and this is the thing that decides
    /// whether it has to.
    ///
    /// It owns no geometry and no policy. The crossing came from the planner
    /// that laid the road
    /// (<see cref="LastRouteCarDrivePath.GiveWay"/>), the wait-or-go decision
    /// is <see cref="LastRouteCarGiveWayModel"/>, and all this does is answer
    /// one question from the live city each frame and hand the answer down.
    ///
    /// The rules are the bus's own, deliberately. Route 01 already yields to
    /// walkers by predicting them a second and a bit ahead and asking whether
    /// they land inside its swept corridor
    /// (<c>CityBusDirector.ResolveObstacleState</c>); this asks the same
    /// question about the same actors against the segment the car is about to
    /// sweep, and asks it of the bus too - because on this junction the bus
    /// is the traffic.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(265)]
    public sealed class LastRouteCarGiveWay : MonoBehaviour
    {
        /// <summary>
        /// How far ahead of itself the bus is taken to reach. Route 01 cruises
        /// at `6 m/s` and brakes at `2.35`, so this is comfortably longer than
        /// its own stopping distance: the question is not whether the bus
        /// COULD stop, it is whether a driver would pull out in front of it.
        /// </summary>
        public const float BusLookAheadSeconds = 4.5f;

        /// <summary>The reach a stationary or dwelling bus still gets, so one
        /// stopped across the mouth is not invisible.</summary>
        public const float MinimumBusReachMeters = 14f;

        /// <summary>
        /// How near the crossing the bus has to come to count. Half a bus is
        /// `1.25 m` and half this car is `1.05 m`; the rest is the margin a
        /// driver actually leaves.
        /// </summary>
        public const float BusClearanceMeters = 2.7f;

        /// <summary>
        /// How closely a vehicle has to be pointing the car's own way before
        /// it stops being traffic to cross and starts being traffic to join.
        /// Sixty degrees: anything squarer than that to the street is turning
        /// through the junction and still has to be waited for.
        /// </summary>
        public const float SameWayDot = 0.5f;

        /// <summary>
        /// The bus director's own number, and the same reason: a walker is
        /// slow enough that where he will be in a second is a better question
        /// than where he is.
        /// </summary>
        public const float PedestrianPredictionSeconds = 1.15f;

        /// <summary>Half this car plus the room a driver leaves a person.
        /// The walker's own radius is added on top of it.</summary>
        public const float PedestrianClearanceMeters = 1.35f;

        /// <summary>A hitch longer than this is stepped rather than
        /// swallowed, the drive model's own convention.</summary>
        public const float MaximumStepSeconds = 0.1f;

        private LastRouteCarDriver driver;
        private CityBusDirector buses;
        private CityPedestrianDirector pedestrians;
        private LastRouteCarGiveWayPoint crossing;
        private LastRouteCarGiveWayModel decision;
        private Vector3 approach = Vector3.forward;
        private bool finished;

        /// <summary>Raised once, when he pulls out. The reason is the drive
        /// model's own vocabulary: `clear`, `too_late` or `waited_out`.
        /// </summary>
        public event Action<string> Committed;

        public bool IsGivingWay => decision != null && decision.IsGivingWay;
        public bool IsCommitted => decision != null && decision.IsCommitted;
        public LastRouteCarGiveWayPoint Crossing => crossing;
        public LastRouteCarGiveWayModel Decision => decision;

        /// <summary>
        /// Puts one on the car for the road it is about to drive, or returns
        /// null if that road never asks it to give way - which is every road
        /// but one.
        /// </summary>
        public static LastRouteCarGiveWay Attach(
            LastRouteCarDriver carDriver,
            LastRouteCarDrivePath path,
            CityBusDirector busDirector,
            CityPedestrianDirector pedestrianDirector)
        {
            if (carDriver == null || path == null || !path.GiveWay.IsPresent)
            {
                return null;
            }

            var giveWay =
                carDriver.gameObject.AddComponent<LastRouteCarGiveWay>();
            giveWay.driver = carDriver;
            giveWay.buses = busDirector;
            giveWay.pedestrians = pedestrianDirector;
            giveWay.crossing = path.GiveWay;
            giveWay.decision =
                new LastRouteCarGiveWayModel(path.GiveWay.Distance);

            // Which way the car is pointing while it waits, taken once. It is
            // what tells a bus coming the other way from one going the same
            // way as him, and the road does not move.
            path.Sample(
                path.GiveWay.Distance,
                out _,
                out Vector3 heading);
            heading.y = 0f;
            giveWay.approach = heading.sqrMagnitude > 0.000001f
                ? heading.normalized
                : Vector3.forward;
            return giveWay;
        }

        /// <summary>
        /// Whether the crossing is clear right now, exposed so it can be
        /// asserted without a clock.
        /// </summary>
        public bool IsWayClear()
        {
            return !IsBusCrossing() && !IsAnyoneCrossing();
        }

        private void Update()
        {
            if (finished || driver == null || decision == null)
            {
                return;
            }

            LastRouteCarDriveModel model = driver.Model;
            if (model == null || !driver.IsDriving)
            {
                return;
            }

            float hold = decision.Advance(
                Mathf.Min(Time.deltaTime, MaximumStepSeconds),
                model.Distance,
                model.Speed,
                model.Profile.Braking,
                IsWayClear());
            model.SetHold(hold);
            if (!decision.IsCommitted)
            {
                return;
            }

            // The decision is made once. Past it the car is in the turn and
            // the crossing is behind it, so there is nothing left to watch
            // and nothing this could usefully do but get in the way.
            finished = true;
            model.ReleaseHold();
            GameLog.Info(
                "lastroute",
                "car_gave_way",
                GameLog.Field("reason", decision.CommitReason),
                GameLog.Field("waited", decision.WaitedSeconds));
            Committed?.Invoke(decision.CommitReason);
        }

        private bool IsBusCrossing()
        {
            CityBusActor bus = buses != null ? buses.Actor : null;
            if (bus == null || !bus.IsSpawned)
            {
                return false;
            }

            return IsCrossedByVehicle(
                crossing,
                approach,
                bus.Position,
                bus.TravelDirection,
                bus.Speed,
                ResolveHalfLength(bus));
        }

        private bool IsAnyoneCrossing()
        {
            if (pedestrians == null)
            {
                return false;
            }

            IReadOnlyList<CityPedestrianActor> actors = pedestrians.Actors;
            for (int index = 0; index < actors.Count; index++)
            {
                CityPedestrianActor walker = actors[index];
                // A walker bound to Route 01 is waiting for a bus on the
                // pavement, which is the bus director's own exclusion and the
                // same reasoning: he is not going anywhere near the road.
                if (walker == null ||
                    !walker.IsSpawned ||
                    walker.IsRouteBound)
                {
                    continue;
                }

                if (IsWalkedInto(
                        crossing,
                        walker.Position,
                        walker.TravelDirection,
                        walker.MovementSpeed,
                        walker.AgentRadius))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Whether one vehicle would be across the turn, swept forward along
        /// its own heading. It travels on baked links and could be asked
        /// exactly where it will be, but the answer a DRIVER has is the one
        /// this uses: it is pointing that way and moving at that speed.
        ///
        /// A vehicle going the same way as the car is not counted, and that
        /// is the rule rather than an optimisation. `From` is the point in
        /// the car's OWN lane where the turn starts, and Route 01 lays its
        /// links at the same `1.5 m` off the same crown - so a bus simply
        /// following the car down the street sweeps straight through the
        /// crossing, reads as traffic, and holds the car at the line until
        /// the wait runs out. What the turn crosses is the lane coming the
        /// other way.
        ///
        /// Pure and static so the geometry can be asserted without a city.
        /// </summary>
        public static bool IsCrossedByVehicle(
            LastRouteCarGiveWayPoint crossing,
            Vector3 approach,
            Vector3 position,
            Vector3 travelDirection,
            float speed,
            float halfLength)
        {
            Vector3 travel = Flatten(travelDirection);
            if (travel.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            travel = travel.normalized;
            Vector3 heading = Flatten(approach);
            if (heading.sqrMagnitude > 0.000001f &&
                Vector3.Dot(travel, heading.normalized) > SameWayDot)
            {
                return false;
            }

            // Swept from the TAIL, not from the middle. A dwelling bus is
            // reported at the centre of eight metres of body, and the reach
            // that exists to catch one stopped across the mouth would
            // otherwise only ever cover its nose.
            float reach = Mathf.Max(
                MinimumBusReachMeters,
                Mathf.Max(0f, speed) * BusLookAheadSeconds);
            Vector3 from = position - (travel * Mathf.Max(0f, halfLength));
            Vector3 to = position + (travel * reach);
            return SegmentDistance(from, to, crossing.From, crossing.To) <
                   BusClearanceMeters;
        }

        /// <summary>
        /// Whether one walker is on the crossing, or will be within the
        /// second and a bit a driver looks ahead for people.
        ///
        /// His OWN speed rather than the planner's ceiling: the archetypes
        /// run from a shuffle to a stride, and predicting a babushka at a
        /// young man's pace holds the car up for somebody who is nowhere near
        /// the road.
        /// </summary>
        public static bool IsWalkedInto(
            LastRouteCarGiveWayPoint crossing,
            Vector3 position,
            Vector3 travelDirection,
            float speed,
            float radius)
        {
            float limit = PedestrianClearanceMeters + Mathf.Max(0f, radius);
            if (PointDistance(crossing, position) < limit)
            {
                return true;
            }

            Vector3 travel = Flatten(travelDirection);
            if (travel.sqrMagnitude < 0.000001f)
            {
                return false;
            }

            Vector3 predicted = position +
                                (travel.normalized *
                                 (Mathf.Max(0f, speed) *
                                  PedestrianPredictionSeconds));
            return PointDistance(crossing, predicted) < limit;
        }

        /// <summary>
        /// Half a bus, off its own body box. Falls back to nothing rather
        /// than to a guess: a missing collider should shrink the sweep, never
        /// invent a bus that is not there.
        /// </summary>
        private static float ResolveHalfLength(CityBusActor bus)
        {
            BoxCollider body = bus.BodyCollider;
            return body != null ? body.size.z * 0.5f : 0f;
        }

        private static float PointDistance(
            LastRouteCarGiveWayPoint crossing,
            Vector3 point)
        {
            return Vector3.Distance(
                Flatten(point),
                ClosestOnSegment(
                    Flatten(crossing.From),
                    Flatten(crossing.To),
                    Flatten(point)));
        }

        /// <summary>
        /// The gap between two segments on the ground plane. Everything here
        /// is planar on purpose: the forecourt sits about eight centimetres
        /// below the street it opens off, and a car does not drive under a
        /// bus.
        /// </summary>
        public static float SegmentDistance(
            Vector3 firstFrom,
            Vector3 firstTo,
            Vector3 secondFrom,
            Vector3 secondTo)
        {
            Vector3 a = Flatten(firstFrom);
            Vector3 b = Flatten(firstTo);
            Vector3 c = Flatten(secondFrom);
            Vector3 d = Flatten(secondTo);
            if (Intersects(a, b, c, d))
            {
                return 0f;
            }

            float best = Vector3.Distance(a, ClosestOnSegment(c, d, a));
            best = Mathf.Min(best, Vector3.Distance(b, ClosestOnSegment(c, d, b)));
            best = Mathf.Min(best, Vector3.Distance(c, ClosestOnSegment(a, b, c)));
            best = Mathf.Min(best, Vector3.Distance(d, ClosestOnSegment(a, b, d)));
            return best;
        }

        private static bool Intersects(
            Vector3 a,
            Vector3 b,
            Vector3 c,
            Vector3 d)
        {
            float first = Side(a, b, c);
            float second = Side(a, b, d);
            float third = Side(c, d, a);
            float fourth = Side(c, d, b);
            return first * second < 0f && third * fourth < 0f;
        }

        private static float Side(Vector3 from, Vector3 to, Vector3 point)
        {
            return ((to.x - from.x) * (point.z - from.z)) -
                   ((to.z - from.z) * (point.x - from.x));
        }

        private static Vector3 ClosestOnSegment(
            Vector3 from,
            Vector3 to,
            Vector3 point)
        {
            Vector3 run = to - from;
            float lengthSquared = run.sqrMagnitude;
            if (lengthSquared < 0.000001f)
            {
                return from;
            }

            float t = Mathf.Clamp01(
                Vector3.Dot(point - from, run) / lengthSquared);
            return from + (run * t);
        }

        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
