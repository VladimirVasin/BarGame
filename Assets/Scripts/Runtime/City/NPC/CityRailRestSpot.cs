using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    // Feet are on the land side. RailRight follows the actual grade of the beam.
    internal readonly struct CityRailRestSpot
    {
        internal readonly Vector3 Feet, Facing, RailTop, RailRight;

        private CityRailRestSpot(Vector3 feet, Vector3 facing, Vector3 top, Vector3 railRight)
        {
            Feet = feet;
            Facing = facing;
            RailTop = top;
            RailRight = railRight;
        }

        internal static CityRailRestSpot AtLevelRail(Vector3 feet, Vector3 facing, Vector3 top) =>
            new CityRailRestSpot(feet, facing, top, Vector3.Cross(Vector3.up, facing));

        internal static CityRailRestSpot OnPublicEdge(Vector3 railBase, Vector3 facing, float height) =>
            AtLevelRail(railBase - facing * .53f, facing, railBase + Vector3.up * height);

        internal static List<CityRailRestSpot> AlongQuays(CityLayout layout)
        {
            var spots = new List<CityRailRestSpot>();
            var lamps = CityRiverWorldBuilder.CreatePromenadeLampPositions(layout);
            foreach (var bank in layout.River.Promenades)
            {
                float railX = CityRiverWorldBuilder.QuayRailX(layout.River, bank);
                Vector3 facing = bank.WestBank ? Vector3.right : Vector3.left;
                float grade = (bank.NorthY - bank.SouthY) / bank.Bounds.height;
                Vector3 railRight = new Vector3(0f, grade, 1f).normalized * -facing.x;
                float landEdge = bank.WestBank ? bank.Bounds.xMin : bank.Bounds.xMax;
                foreach (var span in CityRiverWorldBuilder.QuayRailSpans(layout.River, bank))
                {
                    // Keep both ends of the bridge/stair approaches and the
                    // opposing lanes behind the entire leaning body free.
                    float length = span.Maximum - span.Minimum;
                    if (length < 10f) continue;
                    int count = 1 + Mathf.FloorToInt((length - 10f) / 24f);
                    for (int i = 0; i < count; i++)
                    {
                        float z = span.Minimum + (i + .5f) * length / count;
                        Vector3 basePoint = new Vector3(railX, bank.SampleTop(z), z);
                        Vector3 feet = basePoint - facing * .53f;
                        if (Mathf.Abs(feet.x - landEdge) < 2.4f || NearLamp(feet, lamps)) continue;
                        spots.Add(new CityRailRestSpot(feet, facing, basePoint + Vector3.up *
                            CityRiverWorldBuilder.QuayRailTop, railRight));
                    }
                }
            }
            return spots;
        }

        internal static void AddRidgePorches(CityLayout layout, List<CityRailRestSpot> spots)
        {
            foreach (var house in CityHillsideNeighborhoodPlan.ForLayout(layout).AllBuildings)
            {
                Rect court = house.CourtBounds;
                for (int i = 1; i < house.EntryRail.Count; i++)
                {
                    if (i == house.SideOpeningSegment) continue;
                    Vector3 start = house.EntryRail[i - 1], end = house.EntryRail[i];
                    Vector3 edge = end - start;
                    if (edge.magnitude < 4.4f || Mathf.Abs(edge.y) > .015f) continue;
                    Vector3 mid = (start + end) * .5f;
                    Vector3 along = Vector3.ProjectOnPlane(edge, Vector3.up).normalized;
                    Vector3 normal = Vector3.Cross(along, Vector3.up);
                    Vector3 centre = new Vector3(court.center.x, mid.y, court.center.y);
                    if (Vector3.Dot(normal, mid - centre) < 0f) normal = -normal;
                    var spot = OnPublicEdge(mid, normal, 1.05f);
                    // Require a public strip on this porch, clear of the door and the street approach.
                    Vector3 behind = spot.Feet - normal * 1.9f;
                    if (!OnPorch(spot.Feet) || !OnPorch(behind - along * 1.8f) ||
                        !OnPorch(behind + along * 1.8f) || (spot.Feet - house.DoorWorld).sqrMagnitude < 2.25f ||
                        house.ApproachBounds.Contains(new Vector2(spot.Feet.x, spot.Feet.z))) continue;
                    spots.Add(spot);

                    bool OnPorch(Vector3 point) => court.Contains(new Vector2(point.x, point.z));
                }
            }
        }

        private static bool NearLamp(Vector3 feet, IReadOnlyList<Vector3> lamps)
        {
            for (int i = 0; i < lamps.Count; i++)
                if ((lamps[i] - feet).sqrMagnitude < 1.5f * 1.5f) return true;
            return false;
        }

        internal bool HasClearApproach()
        {
            // From just inside the rail to the far shoulder of the passing
            // strip. This includes the torso, elbows and swinging hand.
            Vector3 centre = Feet - Facing * .86f + Vector3.up * 1.02f;
            return !Physics.CheckBox(centre, new Vector3(2.1f, .80f, 1.27f),
                Quaternion.LookRotation(Facing), CityPedestrianCollision.NonPedestrianMask,
                QueryTriggerInteraction.Ignore);
        }
    }
}
