using System;

namespace BarPromenade
{
    /// <summary>
    /// Adapts the validated mountain descriptor into the independent gameplay
    /// boundary. No scene id is invented while the destination is absent.
    /// </summary>
    public static class CityTunnelTravelPlanner
    {
        public static bool TryCreate(
            CityMountainBoundaryPlan mountainPlan,
            out CityTunnelTravelPlan travelPlan)
        {
            if (mountainPlan == null)
            {
                throw new ArgumentNullException(nameof(mountainPlan));
            }

            if (!mountainPlan.HasTunnel)
            {
                travelPlan = default;
                return false;
            }

            travelPlan = Create(mountainPlan.Tunnel);
            return true;
        }

        public static CityTunnelTravelPlan Create(
            CityMountainTunnelDescriptor tunnel)
        {
            return new CityTunnelTravelPlan(
                $"{tunnel.StableId}-travel",
                tunnel.PortalGroundCenter,
                tunnel.Axis,
                tunnel.OpeningWidth,
                tunnel.DecisionDistance,
                tunnel.ReturnDistance,
                tunnel.WalkableDepth,
                tunnel.PortalGroundCenter.y +
                CityMountainBoundaryWorldBuilder.ThroatFloorSurfaceLift,
                tunnel.TravelAvailable);
        }
    }
}
