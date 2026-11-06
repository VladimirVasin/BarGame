using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The small pavilion inside the west hillside turn. The road and
    /// its inner pavement retain their surveyed contour and turning clearance.</summary>
    public sealed class CityPawnshopPlan
    {
        public const float Width = 8f, Depth = 9f, Height = 4f;
        private static readonly ConditionalWeakTable<CityLayout, CityPawnshopPlan> Plans = new();
        private static readonly CityPawnshopPlan Missing = new();
        public Vector3 Origin { get; }
        public Quaternion Rotation { get; }
        public Vector3 DoorOutward { get; }
        public Vector3 DoorWorld => Origin + DoorOutward * (Depth * .5f);
        public Vector3 EntrancePosition => DoorWorld + DoorOutward * .90f + Vector3.up * .82f;
        public Vector3 ReturnWorld => DoorWorld + DoorOutward * 1.10f + Vector3.up * PlayerFactory.GroundedRootOffset;
        public PlayerDoorActionPlan DoorAction { get; }
        public Vector2[] Footprint { get; }
        public Rect Bounds { get; }
        public const float ApproachWidth = 2.4f;
        public Vector2[] ApproachFootprint { get; }
        public IReadOnlyList<Vector3> ApproachPath { get; }

        private CityPawnshopPlan() { }

        private CityPawnshopPlan(CityLayout layout, Vector3 origin)
        {
            Origin = origin;
            // The player's reference position identifies the entrance side, not
            // the building centre or the ground height. Both come from the turn.
            DoorOutward = new Vector3(-45.5f - origin.x, 0f, -301.3f - origin.z).normalized;
            Rotation = Quaternion.LookRotation(-DoorOutward, Vector3.up);
            Footprint = new Vector2[4];
            Vector3[] corners = { new(-Width * .5f, 0f, -Depth * .5f), new(Width * .5f, 0f, -Depth * .5f),
                new(Width * .5f, 0f, Depth * .5f), new(-Width * .5f, 0f, Depth * .5f) };
            Vector2 lo = new(float.PositiveInfinity, float.PositiveInfinity), hi = -lo;
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 p = origin + Rotation * corners[i];
                Footprint[i] = new Vector2(p.x, p.z);
                lo = Vector2.Min(lo, Footprint[i]); hi = Vector2.Max(hi, Footprint[i]);
            }
            Bounds = Rect.MinMaxRect(lo.x, lo.y, hi.x, hi.y);
            DoorAction = PlayerDoorActionPlan.CreateStationary(EntrancePosition,
                DoorWorld + DoorOutward * .90f + Vector3.up * PlayerFactory.GroundedRootOffset, -DoorOutward);
            // The surface reaches the threshold; the walked route ends at the
            // existing door-action dock, clear of the closed building envelope.
            ApproachFootprint = CityRoadPolygon.CounterClockwise(new[] {
                XZ(origin + Rotation * new Vector3(-ApproachWidth * .5f, 0f, -8.65f)),
                XZ(origin + Rotation * new Vector3(ApproachWidth * .5f, 0f, -8.65f)),
                XZ(origin + Rotation * new Vector3(ApproachWidth * .5f, 0f, -4.49f)),
                XZ(origin + Rotation * new Vector3(-ApproachWidth * .5f, 0f, -4.49f)) });
            var path = new List<Vector3>();
            var support = CityStreetSupportPlan.ForLayout(layout);
            var ground = CityHillsideGroundPlan.ForLayout(layout);
            for (int step = 0; step <= 9; step++)
            {
                Vector3 point = origin + DoorOutward * Mathf.Lerp(8.65f, 5.4f, step / 9f);
                if (!support.TrySampleTop(XZ(point), out float top, out _) &&
                    !ground.TrySampleTop(XZ(point), out top))
                    throw new InvalidOperationException("Pawnshop door approach leaves supported ground.");
                path.Add(new Vector3(point.x, top, point.z));
            }
            ApproachPath = path.AsReadOnly();
        }

        private static Vector2 XZ(Vector3 point) => new(point.x, point.z);

        public static bool TryCreate(CityLayout layout, out CityPawnshopPlan plan)
        {
            plan = layout == null ? Missing : Plans.GetValue(layout, Survey);
            if (plan != Missing) return true;
            plan = null;
            return false;
        }

        private static CityPawnshopPlan Survey(CityLayout layout)
        {
            foreach (var road in CityHillsideStreetPlan.ForLayout(layout).Routes)
            {
                if (road.Bank != CityTerraceTransportBank.West) continue;
                // Derive the actual turn from its two parallel lanes. This stays
                // downstream of street planning and never asks the bus/ground
                // builders to recursively construct their own reservations.
                Vector2 centre = new(road.LaneMaxX, (road.GetLaneZ(4) + road.GetLaneZ(5)) * .5f);
                float floor = CityHillsideNeighborhoodPlan.SampleBank(layout, road, centre);
                var plan = new CityPawnshopPlan(layout, new Vector3(centre.x, floor, centre.y));
                foreach (Vector2 corner in plan.Footprint)
                    if ((corner - centre).sqrMagnitude > 6.1f * 6.1f ||
                        Mathf.Abs(CityHillsideNeighborhoodPlan.SampleBank(layout, road, corner) - floor) > .20f)
                        throw new InvalidOperationException("Pawnshop must fit the level inner turning island.");
                // The inner footway begins outside radius eight; include a
                // modest roof overhang in this bound, rather than only walls.
                if (Mathf.Sqrt(Width * Width + Depth * Depth) * .5f + .35f >= 7f)
                    throw new InvalidOperationException("Pawnshop eaves occupy the inner pavement.");
                return plan;
            }
            return Missing;
        }
    }
}
