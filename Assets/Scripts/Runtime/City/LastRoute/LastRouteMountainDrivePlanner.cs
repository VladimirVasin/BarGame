using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The mountain's half of the journey, in both directions: out of the
    /// tunnel, up the whole climb and onto the terrace by the cafe - and, once
    /// he has been asked a second time, back off that terrace and down.
    ///
    /// Almost all of this already existed and is only being read.
    /// <see cref="MountainRoadRoutePlan"/> is a centreline sampled by arc
    /// length with the drivable surface height already in it, and
    /// `MountainRoadTests.TunnelToCafe_IsOneUnbrokenDrivableSurface` has been
    /// asserting since the road was widened that a body `1.05 m` to either side
    /// of it stays on that surface for all six hundred metres. What is added
    /// here is a lead-in from inside the tunnel and the last few metres onto
    /// the apron.
    ///
    /// **The car parks nose-in and does not turn round.** The apron is a
    /// turning pocket and the temptation is to use it, but the cafe's nearest
    /// corner stands `8.24 m` from the apron centre against a validated
    /// clearance of `TurningRadius + 0.55 = 8.05` - nineteen centimetres - and
    /// a U-turn of any usable radius sweeps through either the cafe or the
    /// cableway station. Stopping in the middle of the pocket is provably
    /// inside ground the terminal validator already guarantees.
    ///
    /// **Which is why leaving again is a two-point turn and not a loop.**
    /// <see cref="CreateDeparture"/> backs the car round on lock and then
    /// pulls it away - the move a driver actually makes in a pocket this size,
    /// and the only one that fits. Both arcs are measured in
    /// <see cref="AppendApronManoeuvre"/> against the ground they cross.
    /// </summary>
    public static class LastRouteMountainDrivePlanner
    {
        /// <summary>
        /// How finely the route is read out. The plan's own samples sit at
        /// about a metre, so this neither invents detail nor throws any away.
        /// </summary>
        public const float SampleStepMeters = 1f;

        /// <summary>
        /// Builds the whole climb as one drivable path: the tunnel, the road
        /// and the run onto the apron, with no seam the car can notice.
        /// </summary>
        public static LastRouteCarDrivePath CreateArrival(MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var points = new List<Vector3>();

            // Inside the tunnel, where the hero would have spawned on foot.
            // He arrives already moving, so the car is simply further back
            // along the axis it is about to drive out on.
            points.Add(plan.Tunnel.SpawnPosition);

            MountainRoadRoutePlan route = plan.Route;
            for (float distance = 0f;
                 distance < route.Length;
                 distance += SampleStepMeters)
            {
                points.Add(route.Sample(distance).Position);
            }

            points.Add(route.End);

            // The apron centre is the route end carried five and a half metres
            // further along the road's own last forward - the plateau is
            // centred four metres past the end and the apron a metre and a
            // half past that - so this is a straight continuation and not a
            // manoeuvre.
            points.Add(plan.Terminal.VehicleApron.Center);
            return new LastRouteCarDrivePath(points);
        }

        /// <summary>
        /// How tight the two-point turn off the apron is cut.
        ///
        /// `5 m` and not the apron's own `7.5`, and both halves of that are
        /// measured. The car can do it - a `2.7 m` wheelbase at the drive
        /// model's `33` degree lock turns inside `4.16 m` - and at `5 m` the
        /// whole manoeuvre stays on ground that is already paved: the cusp
        /// lands `7.07 m` from the apron centre, inside its `7.5 m` disc, and
        /// the second arc crosses the apron's rim exactly where the road
        /// leaves it, never more than `2.9 m` off the centreline of a `6 m`
        /// carriageway. Widen it and the tail swings onto the terminal's
        /// snow; tighten it and the front wheels sit on the lock stop.
        /// </summary>
        public const float ApronTurnRadiusMeters = 5f;

        /// <summary>
        /// How many segments each quarter of the turn is cut into. Sixteen
        /// puts a vertex every `49 cm` of arc, which reports the turn to
        /// <see cref="LastRouteCarDrivePath"/> as `11.5` degrees per metre -
        /// the radius it actually is - rather than as a kink.
        /// </summary>
        public const int ApronTurnSegments = 16;

        /// <summary>
        /// The descent, from the bonnet he has been sitting on back down to
        /// the tunnel he came out of.
        ///
        /// It is NOT <see cref="CreateArrival"/> read backwards. The climb
        /// begins with a car that is already moving and ends with one parked
        /// nose-in; the descent has to get that parked car pointed the other
        /// way first, and the pocket has no room for a U-turn. So it opens
        /// with the two-point turn and then reads the same centreline out in
        /// the other direction, into the tunnel, and stops where the arrival
        /// starts - the two halves of the round trip meet at one point rather
        /// than at two that have to be kept in step.
        /// </summary>
        public static LastRouteCarDrivePath CreateDeparture(
            MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var points = new List<Vector3>();
            AppendApronManoeuvre(
                points,
                plan.Terminal.VehicleApron,
                out int reversePointCount,
                out float joinDistanceFromEnd);

            // The road, read out the other way. The manoeuvre already ended on
            // its centreline, so this picks the centreline up from there and
            // walks it down; nothing is invented and no seam is crossed.
            MountainRoadRoutePlan route = plan.Route;
            for (float distance = route.Length - joinDistanceFromEnd -
                                  SampleStepMeters;
                 distance > 0f;
                 distance -= SampleStepMeters)
            {
                points.Add(route.Sample(distance).Position);
            }

            points.Add(route.Start);

            // And into the dark, stopping exactly where the arrival begins.
            points.Add(plan.Tunnel.SpawnPosition);
            return new LastRouteCarDrivePath(points, reversePointCount);
        }

        /// <summary>
        /// The two-point turn: back round on lock, then away.
        ///
        /// Both arcs are quarter turns of the same radius about centres on
        /// opposite flanks, which is what makes them one continuous swing of
        /// the body with a change of gear in the middle. The car starts on the
        /// apron centre pointing at the cafe, ends on the road's own
        /// centreline pointing down the mountain, and never once points
        /// anywhere the pocket does not have room for.
        ///
        /// In the apron's own frame - `right` across it, `forward` on past the
        /// road's last heading - with `R` the turn radius:
        ///
        /// * it starts at `(0, 0)` facing `+forward`;
        /// * it backs to `(R, -R)` facing `-right`, the centre on its right;
        /// * it pulls away to `(0, -2R)` facing `-forward`, the centre on its
        ///   left.
        ///
        /// The exit therefore lands `2R` back down the plateau's own axis,
        /// which is `2R - 5.5 m` past the end of the road - and the last
        /// `25 m` of that road is one straight, level run, so the join is a
        /// point on the centreline rather than a seam that has to be searched
        /// for.
        /// </summary>
        public static void AppendApronManoeuvre(
            ICollection<Vector3> points,
            MountainRoadVehicleApronPlan apron,
            out int reversePointCount,
            out float joinDistanceFromEnd)
        {
            if (points == null)
            {
                throw new ArgumentNullException(nameof(points));
            }

            if (apron == null)
            {
                throw new ArgumentNullException(nameof(apron));
            }

            float radius = ApronTurnRadiusMeters;
            Vector3 forward = apron.Forward;
            Vector3 right = apron.Right;
            Vector3 start = apron.Center;

            // Backing with the centre on the right: the nose swings toward the
            // cafe and the tail toward the cableway station, and neither
            // reaches either.
            Vector3 reverseCenter = start + (right * radius);
            points.Add(start);
            AppendQuarterTurn(
                points,
                reverseCenter,
                -right * radius,
                -forward * radius);

            // Exactly one point per arc vertex so far, and the last of them is
            // the cusp: the reverse leg is everything up to and including it.
            reversePointCount = ApronTurnSegments + 1;

            Vector3 cusp = reverseCenter - (forward * radius);
            Vector3 forwardCenter = cusp - (forward * radius);
            AppendQuarterTurn(
                points,
                forwardCenter,
                forward * radius,
                -right * radius);

            // Where the manoeuvre leaves the road's own end behind it. The
            // apron centre stands `1.5 m` past the plateau centre and the
            // plateau centre `4 m` past the end of the road, so the exit is
            // `2R - 5.5` further down again.
            joinDistanceFromEnd = (radius * 2f) - 5.5f;
        }

        /// <summary>
        /// A quarter of a circle, from one offset to another, both measured
        /// from the centre and both of the same length. The end offset is
        /// appended and the start is not: every arc here follows a point that
        /// is already in the list.
        /// </summary>
        private static void AppendQuarterTurn(
            ICollection<Vector3> points,
            Vector3 center,
            Vector3 fromOffset,
            Vector3 toOffset)
        {
            for (int step = 1; step <= ApronTurnSegments; step++)
            {
                float t = (float)step / ApronTurnSegments;
                // Slerp rather than a rotation about a named axis: the two
                // offsets are a quarter turn apart by construction, so the
                // shortest arc between them IS the arc, and nothing here has
                // to know which way round the world's up points.
                points.Add(center + Vector3.Slerp(fromOffset, toOffset, t));
            }
        }

        /// <summary>
        /// Where the car stands once the journey is over, and which way it
        /// points. Read by the arrival and by every later load of the mountain
        /// road, so the two cannot disagree about where he ended up.
        /// </summary>
        public static void ResolveParkedPose(
            MountainRoadPlan plan,
            out Vector3 position,
            out Vector3 facing)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadVehicleApronPlan apron = plan.Terminal.VehicleApron;
            position = apron.Center;
            facing = apron.Forward;
        }

        /// <summary>
        /// Where the car is standing the moment the mountain road finishes
        /// loading - back inside the tunnel, pointing out of it.
        /// </summary>
        public static void ResolveArrivalPose(
            MountainRoadPlan plan,
            out Vector3 position,
            out Vector3 facing)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            position = plan.Tunnel.SpawnPosition;
            facing = plan.Tunnel.SpawnForward;
        }
    }
}
