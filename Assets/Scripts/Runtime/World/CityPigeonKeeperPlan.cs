using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The keeper's fixed address and sole route between the loft deck
    /// and his residential vestibule. Both commutes traverse these same feet in reverse.</summary>
    public sealed class CityPigeonKeeperPlan
    {
        private static readonly ConditionalWeakTable<CityLayout, CityPigeonKeeperPlan> Plans = new();
        private static readonly Vector2Int HomeCell = new(-23, -7);

        public CityHillsideBuilding House { get; }
        public string HomeStableId { get; }
        public Vector3 HomeDoor { get; }
        public Vector3 InsideHome { get; }
        public Vector3 GateOutsideStand { get; }
        public IReadOnlyList<Vector3> OutdoorRoute { get; } = Array.Empty<Vector3>();
        public IReadOnlyList<Vector3> WorkToGate { get; } = Array.Empty<Vector3>();
        public IReadOnlyList<Vector3> GateToHome { get; } = Array.Empty<Vector3>();

        public static CityPigeonKeeperPlan ForLayout(CityLayout layout) =>
            Plans.GetValue(layout, value => new CityPigeonKeeperPlan(value));

        private CityPigeonKeeperPlan(CityLayout layout)
        {
            var loft = CityPigeonLoftPlan.ForLayout(layout);
            if (!loft.IsEnabled) return;
            foreach (var building in CityHillsideNeighborhoodPlan.ForLayout(layout).Buildings)
                if (building.Bank == CityTerraceTransportBank.West && building.Lot.Cell == HomeCell &&
                    !building.Lot.IsPlayerHome) { House = building; break; }
            if (House == null) throw new InvalidOperationException("The keeper's west Ridge house has no measured entrance.");
            HomeStableId = $"ridge-west-house:{HomeCell.x}:{HomeCell.y}";
            HomeDoor = House.ThresholdWorld;
            // The outer leaf is parked against the side reveal. Stop fully inside
            // the opening, with room for the body before the closed inner leaf.
            InsideHome = HomeDoor - House.Lot.FacadeForward * (CityBuildingAssetProvider.EntranceDepth - .45f);

            var yard = loft.Courtyard;
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            Vector3 hinge = yard.FencePosts[0], strike = yard.FencePosts[yard.FencePosts.Count - 1];
            Vector3 across = Vector3.ProjectOnPlane(strike - hinge, Vector3.up).normalized;
            Vector3 outward = Vector3.ProjectOnPlane(yard.GateCenter - yard.Arrival, Vector3.up).normalized;
            GateOutsideStand = Earth(hinge + across * .42f + outward * .40f);
            var toGate = new List<Vector3>(27)
            {
                LoftPoint(0f, 2f, -2.0f), LoftPoint(-2.02f, 2f, -1.75f)
            };
            // The centre of each of the twelve imported treads. Upper deck, turn
            // and bottom footing are measured independently from the rig's stride.
            for (int step = 11; step >= 0; step--)
                toGate.Add(LoftPoint(-2.02f, (step + 1) / 6f, 1.70f - (step + .5f) * .25f));
            for (int i = yard.WalkPath.Count - 1; i >= 1; i--) toGate.Add(yard.WalkPath[i]);
            toGate.Add(GateOutsideStand);
            WorkToGate = toGate.AsReadOnly();

            CityHillsideStreetRoute west = null;
            foreach (var route in CityHillsideStreetPlan.ForLayout(layout).Routes)
                if (route.Bank == CityTerraceTransportBank.West) { west = route; break; }
            if (west == null) throw new InvalidOperationException("The keeper's west Ridge footway is missing.");
            var toHome = new List<Vector3>(32) { GateOutsideStand, yard.WalkPath[0] };
            // The opposite footway is on the INSIDE of the final western bend.
            // Follow its real mitred corners, then turn off at the assigned entrance.
            var walk = west.OppositeSidewalkPath;
            var from = Project(walk, yard.WalkPath[0]);
            var to = Project(walk, House.StreetContactWorld);
            if (from.segment <= to.segment || Vector3.Distance(to.point, House.StreetContactWorld) > .20f)
                throw new InvalidOperationException("The keeper's house no longer fronts the western inside footway.");
            const float interval = .30f;
            int vergeSteps = Mathf.Max(1, Mathf.CeilToInt(Vector3.Distance(yard.WalkPath[0], from.point) / interval));
            for (int i = 1; i < vergeSteps; i++)
            {
                Vector3 point = Vector3.Lerp(yard.WalkPath[0], from.point, i / (float)vergeSteps);
                toHome.Add(west.TrySampleTop(XZ(point), out float y, out _) ? new Vector3(point.x, y, point.z) : Earth(point));
            }
            toHome.Add(from.point);
            for (int i = from.segment; i > to.segment; i--) toHome.Add(walk[i]);
            toHome.Add(to.point);

            int apronSteps = Mathf.CeilToInt(Vector3.Distance(to.point, House.DoorWorld) / .65f);
            for (int i = 1; i <= apronSteps; i++)
            {
                Vector3 point = Vector3.Lerp(to.point, House.DoorWorld, i / (float)apronSteps);
                toHome.Add(west.TrySampleTop(XZ(point), out float top, out _) ? new Vector3(point.x, top, point.z) : Earth(point));
            }
            toHome.Add(HomeDoor);
            toHome.Add(InsideHome);
            GateToHome = toHome.AsReadOnly();

            var outwardRoute = new List<Vector3>(toGate.Count + toHome.Count - 1);
            outwardRoute.AddRange(toGate);
            for (int i = 1; i < toHome.Count; i++) outwardRoute.Add(toHome[i]);
            OutdoorRoute = outwardRoute.AsReadOnly();

            Vector3 LoftPoint(float x, float y, float z) => loft.Origin + loft.Rotation * new Vector3(x, y, z);
            Vector3 Earth(Vector3 p)
            {
                if (!ground.TrySampleTop(XZ(p), out float y))
                    throw new InvalidOperationException("The keeper's footway lost its ground at " + p);
                return new Vector3(p.x, y, p.z);
            }
        }

        private static (int segment, Vector3 point) Project(IReadOnlyList<Vector3> path, Vector3 target)
        {
            int segment = 0;
            Vector3 nearest = path[0];
            float best = float.PositiveInfinity;
            for (int i = 0; i + 1 < path.Count; i++)
            {
                Vector2 delta = XZ(path[i + 1]) - XZ(path[i]);
                float fraction = Vector2.Dot(XZ(target) - XZ(path[i]), delta) / Mathf.Max(.000001f, delta.sqrMagnitude);
                Vector3 point = Vector3.Lerp(path[i], path[i + 1], fraction);
                float distance = (XZ(point) - XZ(target)).sqrMagnitude;
                if (distance >= best) continue;
                best = distance;
                segment = i;
                nearest = point;
            }
            return (segment, nearest);
        }

        private static Vector2 XZ(Vector3 p) => new(p.x, p.z);
    }
}
