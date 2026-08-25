using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The second half of the journey: out of the mountain's tunnel, up the
    /// whole climb, and onto the terrace by the cafe.
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
        public static LastRouteCarDrivePath Create(MountainRoadPlan plan)
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
