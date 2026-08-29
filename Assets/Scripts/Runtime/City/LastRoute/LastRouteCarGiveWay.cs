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

        /// <summary>
        /// How far short of the first conflict the car is asked to stand.
        /// The conflict point itself already sits <see
        /// cref="BusClearanceMeters"/> off the other body, so the standing
        /// nose-to-tail gap is this plus that, less whatever a late hold
        /// overspends at maximum braking.
        ///
        /// It is also half of what keeps the street deadlock-free, and the
        /// arithmetic is pinned by a test. The bus never brakes for this car
        /// - only for walkers and for the HERO, whom it looks for within
        /// `1.71 m` of its own lane line. The hero is riding this car, so a
        /// hold has to park him OUTSIDE that corridor or the two vehicles
        /// stand braked for each other at a junction with nobody crossing
        /// anything: `6 + 2.7` less two metres of worst overshoot leaves the
        /// nose `6.7 m` clear of the bus's line, and the hero behind it
        /// further still.
        /// </summary>
        public const float TrafficFollowGapMeters = 6f;

        /// <summary>
        /// How far down his own road the driver watches for traffic. The bus
        /// director's own shape: never less than a town block, and always
        /// past what the car needs to stop from its current speed.
        /// </summary>
        public const float MinimumTrafficHorizonMeters = 16f;

        /// <summary>
        /// The step the conflict probe walks the path at. The corridor
        /// threshold is `2.7 m`; on the tightest rounded corner (`~3.2 m`
        /// apex radius) a two-metre chord sags `0.16 m` under the arc, which
        /// the threshold absorbs without noticing.
        /// </summary>
        public const float TrafficProbeStepMeters = 2f;

        /// <summary>
        /// The room a driver leaves a walker who is IN THE ROAD, on top of
        /// the walker's own radius. Deliberately tighter than the crossing's
        /// `1.35`: that one guards a turn that legitimately sweeps the
        /// pavement, while this one guards the lane itself - and a walker
        /// waiting at a bus stop stands `1.74 m` off the lane line, so
        /// anything looser reads the whole pavement as jaywalkers. Half this
        /// car with mirrors is `1.05`; the rest is the margin.
        /// </summary>
        public const float LanePedestrianClearanceMeters = 1.2f;

        private LastRouteCarDriver driver;
        private CityBusDirector buses;
        private CityPedestrianDirector pedestrians;
        private LastRouteCarGiveWayPoint crossing;
        private LastRouteCarGiveWayModel decision;
        private LastRouteCarTrafficYieldModel traffic;
        private LastRouteCarDrivePath road;
        private Vector3 approach = Vector3.forward;
        private bool finished;
        private bool announcedWaitedOut;

        /// <summary>Raised once, when he pulls out. The reason is the drive
        /// model's own vocabulary: `clear`, `too_late` or `waited_out`.
        /// </summary>
        public event Action<string> Committed;

        public bool IsGivingWay => decision != null && decision.IsGivingWay;
        public bool IsCommitted => decision != null && decision.IsCommitted;
        public LastRouteCarGiveWayPoint Crossing => crossing;
        public LastRouteCarGiveWayModel Decision => decision;

        /// <summary>The everywhere-else rule: easing off for a bus or a
        /// walker on his own road, all the way down the leg.</summary>
        public LastRouteCarTrafficYieldModel Traffic => traffic;

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
            giveWay.road = path;
            giveWay.decision =
                new LastRouteCarGiveWayModel(path.GiveWay.Distance);
            giveWay.traffic = new LastRouteCarTrafficYieldModel();

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
            if (driver == null || decision == null)
            {
                return;
            }

            LastRouteCarDriveModel model = driver.Model;
            if (model == null || !driver.IsDriving)
            {
                return;
            }

            float step = Mathf.Min(Time.deltaTime, MaximumStepSeconds);

            // The crossing's one decision, until it is made; the traffic
            // rule for the whole of the leg. The model has ONE hold slot and
            // two writers calling SetHold are last-writer-wins, so this
            // component is the single writer and the slot takes the nearer
            // of the two answers.
            float crossingHold = float.PositiveInfinity;
            if (!finished)
            {
                crossingHold = decision.Advance(
                    step,
                    model.Distance,
                    model.Speed,
                    model.Profile.Braking,
                    IsWayClear());
            }

            float trafficHold = traffic.Advance(
                step,
                model.Distance,
                model.Speed,
                model.Profile.Braking,
                FindNearestTrafficConflict(model.Distance, model.Speed));
            if (traffic.IsWaitedOut && !announcedWaitedOut)
            {
                announcedWaitedOut = true;
                GameLog.Info(
                    "lastroute",
                    "car_traffic_waited_out",
                    GameLog.Field("waited", traffic.WaitedSeconds),
                    GameLog.Field("distance", model.Distance));
            }
            else if (!traffic.IsWaitedOut)
            {
                announcedWaitedOut = false;
            }

            model.SetHold(Mathf.Min(crossingHold, trafficHold));
            if (finished || !decision.IsCommitted)
            {
                return;
            }

            // The crossing's decision is made once. Past it the car is in
            // the turn and that segment is behind it; the traffic rule
            // drives on.
            finished = true;
            GameLog.Info(
                "lastroute",
                "car_gave_way",
                GameLog.Field("reason", decision.CommitReason),
                GameLog.Field("waited", decision.WaitedSeconds));
            Committed?.Invoke(decision.CommitReason);
        }

        /// <summary>
        /// The nearest thing on his own road he should not drive into, as a
        /// hold distance for the traffic model - or infinity when the road
        /// ahead is his.
        /// </summary>
        private float FindNearestTrafficConflict(
            float carDistance,
            float carSpeed)
        {
            float braking = driver.Model.Profile.Braking;
            float stopping = braking > 0.0001f
                ? (carSpeed * carSpeed) / (2f * braking)
                : 0f;
            float horizon = Mathf.Max(
                MinimumTrafficHorizonMeters,
                stopping + carSpeed + 4f);

            float conflict = float.PositiveInfinity;
            CityBusActor bus = buses != null ? buses.Actor : null;
            if (bus != null && bus.IsSpawned)
            {
                conflict = FindBusPathConflict(
                    road,
                    carDistance,
                    horizon,
                    bus.Position,
                    bus.TravelDirection,
                    bus.Speed,
                    ResolveHalfLength(bus));
            }

            if (pedestrians != null)
            {
                IReadOnlyList<CityPedestrianActor> actors =
                    pedestrians.Actors;
                for (int index = 0; index < actors.Count; index++)
                {
                    CityPedestrianActor walker = actors[index];
                    // Route-bound walkers wait on the pavement for a bus,
                    // the crossing's own exclusion for the same reason.
                    if (walker == null ||
                        !walker.IsSpawned ||
                        walker.IsRouteBound)
                    {
                        continue;
                    }

                    conflict = Mathf.Min(
                        conflict,
                        FindWalkerPathConflict(
                            road,
                            carDistance,
                            horizon,
                            walker.Position,
                            walker.TravelDirection,
                            walker.MovementSpeed,
                            walker.AgentRadius));
                }
            }

            return float.IsPositiveInfinity(conflict)
                ? float.PositiveInfinity
                : Mathf.Max(0f, conflict - TrafficFollowGapMeters);
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
        /// Where along his own road the bus first stands in the car's way,
        /// or infinity. The bus is taken as the segment from its tail to a
        /// nose swept a breath ahead - a driver reads a moving bus by where
        /// it is about to be - and the road as chords walked at
        /// <see cref="TrafficProbeStepMeters"/> from the car to the horizon.
        ///
        /// There is deliberately NO same-way exclusion here, unlike the
        /// crossing's sweep: on his own road the same-way bus ahead is the
        /// most probable collision partner - it dwells ten seconds in the
        /// very lane line he drives, and he closes on it at two metres a
        /// second. What keeps lawful oncoming traffic out is the corridor
        /// itself: the opposite lane runs three metres off his, past the
        /// `2.7 m` threshold.
        ///
        /// Pure and static so the geometry can be asserted without a city.
        /// </summary>
        public static float FindBusPathConflict(
            LastRouteCarDrivePath path,
            float carDistance,
            float horizonMeters,
            Vector3 busPosition,
            Vector3 busTravel,
            float busSpeed,
            float busHalfLength)
        {
            if (path == null)
            {
                return float.PositiveInfinity;
            }

            Vector3 travel = Flatten(busTravel);
            Vector3 tail;
            Vector3 nose;
            if (travel.sqrMagnitude < 0.000001f)
            {
                tail = busPosition;
                nose = busPosition;
            }
            else
            {
                travel = travel.normalized;
                tail = busPosition -
                       (travel * Mathf.Max(0f, busHalfLength));
                nose = busPosition +
                       (travel *
                        (Mathf.Max(0f, busHalfLength) +
                         (Mathf.Max(0f, busSpeed) *
                          PedestrianPredictionSeconds)));
            }

            float end = Mathf.Min(
                path.Length,
                carDistance + Mathf.Max(0f, horizonMeters));

            // The probe grid is anchored to the ROAD, never to the car. A
            // grid walked from the car's own distance creeps forward with
            // it, the conflict and the hold creep too, and the car chases
            // its own quantisation toward the bus at half a metre a second
            // instead of stopping - the test that pinned this watched it
            // crawl the whole follow gap.
            for (float along = GridStart(carDistance);
                 along < end;
                 along += TrafficProbeStepMeters)
            {
                float next = Mathf.Min(end, along + TrafficProbeStepMeters);
                path.Sample(along, out Vector3 from, out _);
                path.Sample(next, out Vector3 to, out _);
                if (SegmentDistance(from, to, tail, nose) <
                    BusClearanceMeters)
                {
                    return along;
                }
            }

            return float.PositiveInfinity;
        }

        /// <summary>The first road-anchored grid line at or behind the car.
        /// </summary>
        private static float GridStart(float carDistance)
        {
            return Mathf.Floor(
                       Mathf.Max(0f, carDistance) /
                       TrafficProbeStepMeters) *
                   TrafficProbeStepMeters;
        }

        /// <summary>
        /// Where along his own road a walker first stands in the car's way,
        /// or infinity. The walker counts where he is and where his own pace
        /// puts him a second and a bit on - the crossing's rule - against a
        /// corridor of <see cref="LanePedestrianClearanceMeters"/> plus his
        /// radius either side of the lane line. Pure and static, like the
        /// bus probe.
        /// </summary>
        public static float FindWalkerPathConflict(
            LastRouteCarDrivePath path,
            float carDistance,
            float horizonMeters,
            Vector3 position,
            Vector3 travelDirection,
            float speed,
            float radius)
        {
            if (path == null)
            {
                return float.PositiveInfinity;
            }

            float limit = LanePedestrianClearanceMeters +
                          Mathf.Max(0f, radius);
            Vector3 now = Flatten(position);
            Vector3 travel = Flatten(travelDirection);
            Vector3 predicted = travel.sqrMagnitude > 0.000001f
                ? now + (travel.normalized *
                         (Mathf.Max(0f, speed) *
                          PedestrianPredictionSeconds))
                : now;

            float end = Mathf.Min(
                path.Length,
                carDistance + Mathf.Max(0f, horizonMeters));

            // Road-anchored grid, the bus probe's own lesson.
            for (float along = GridStart(carDistance);
                 along < end;
                 along += TrafficProbeStepMeters)
            {
                float next = Mathf.Min(end, along + TrafficProbeStepMeters);
                path.Sample(along, out Vector3 from, out _);
                path.Sample(next, out Vector3 to, out _);
                Vector3 a = Flatten(from);
                Vector3 b = Flatten(to);
                if (Vector3.Distance(now, ClosestOnSegment(a, b, now)) <
                    limit ||
                    Vector3.Distance(
                        predicted,
                        ClosestOnSegment(a, b, predicted)) < limit)
                {
                    return along;
                }
            }

            return float.PositiveInfinity;
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
