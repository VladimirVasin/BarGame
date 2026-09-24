using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed class VillageNarrativePoint
    {
        public int Number { get; internal set; }
        public string Id => "village.narrative." + Number.ToString("00");
        public bool Existing => Number == 26 || Number == 27;
        public bool Document => Number == 4 || Number == 16;
        public bool GroundProp => !Document && Number != 11;
        public string HouseId { get; internal set; }
        public Vector3 Position { get; internal set; }
        public Vector3 AuthoredPosition { get; internal set; }
        public float PreferredApproachSide { get; internal set; }
        public Quaternion Rotation { get; internal set; }
        public Vector2 Footprint { get; internal set; }
        public Rect LocalFootprint { get; internal set; }
    }

    /// <summary>Objects authored at their real places of work and storage,
    /// independent of road distance. Shared by forest, snow and scene construction.</summary>
    public sealed class AlpineVillageNarrativePlan
    {
        public const float MinimumRoadClearance = .35f;
        private readonly List<VillageNarrativePoint> points = new List<VillageNarrativePoint>();
        public IReadOnlyList<VillageNarrativePoint> Points => points;

        public static AlpineVillageNarrativePlan Create(AlpineVillagePlan village)
        {
            var result = new AlpineVillageNarrativePlan();
            void Add(int id, Vector3 position, Vector2 footprint, Vector3 facing, string house = null)
            {
                position = Ground(village, position);
                var point = new VillageNarrativePoint
                {
                    Number = id, Position = position, AuthoredPosition = position,
                    LocalFootprint = ModelFootprint(id), HouseId = house,
                    // Leave the basket stand (built later by Life) and the
                    // rescue sledge's protruding handles beside the hero.
                    PreferredApproachSide = id == 5 || id == 20 ? -.6f : 0f,
                    Rotation = Quaternion.LookRotation(facing)
                };
                point.Footprint = Vector2.Max(footprint, new Vector2(
                    Mathf.Max(Mathf.Abs(point.LocalFootprint.xMin), Mathf.Abs(point.LocalFootprint.xMax)) * 2f,
                    Mathf.Max(Mathf.Abs(point.LocalFootprint.yMin), Mathf.Abs(point.LocalFootprint.yMax)) * 2f));
                result.points.Add(point);
            }
            void Local(int id, float x, float z, float width, float depth, float yaw, string house = null)
                => Add(id, village.Expansion.ToWorld(new Vector2(x, z)), new Vector2(width, depth),
                    Quaternion.AngleAxis(yaw, Vector3.up) * village.Uphill, house);
            AlpineVillagePlotDescriptor House(int home)
            {
                string id = "village-house-" + home.ToString("00");
                foreach (var plot in village.Plots) if (plot.StableId == id) return plot;
                throw new InvalidOperationException(id);
            }
            void Core(int id, int home, float x, float z, float width, float depth, bool door = false)
            {
                var plot = House(home);
                Vector3 origin = door ? plot.DoorGroundPosition : plot.GroundCenter;
                Add(id, origin + Vector3.Cross(Vector3.up, plot.Facing) * x + plot.Facing * z,
                    new Vector2(width, depth), plot.Facing, plot.StableId);
            }
            void Yard(int id, string house, float x, float z, float width, float depth, float yaw = 0f)
            {
                foreach (var plot in village.Expansion.Abandonment.Plots)
                    if (plot.Id == house)
                    {
                        Add(id, plot.World(new Vector2(x, z)), new Vector2(width, depth),
                            Quaternion.AngleAxis(yaw, Vector3.up) * plot.Forward, house);
                        return;
                    }
                throw new InvalidOperationException(house);
            }

            // Stable IDs belong to the content catalog. Repeated exhibits are
            // omitted; the remaining objects belong to actual work/storage places.
            var life = AlpineVillageLifePlan.Create(village);
            Add(1, life.StationCrate - village.Station.Cableway.LineRight * .95f +
                village.Station.Cableway.LineForward * .70f,
                new Vector2(.9f, .7f), life.StationForward);
            Core(4, 2, 0f, .14f, .75f, .15f, true);
            Core(5, 4, -2.80f, 3.52f, 1.45f, .85f);
            Core(6, 8, -2.40f, 3.08f, 1.9f, .9f);
            Core(8, 11, -2.55f, 3.71f, 1.25f, .75f);
            var neighbourhood = new VillageNeighbourhoodPlan(village);
            var quietHouse = neighbourhood.QuietHouse;
            Add(9, neighbourhood.GatePosition - Vector3.Cross(Vector3.up, quietHouse.Facing) * 1.18f -
                quietHouse.Facing * .45f, new Vector2(.8f, .8f), quietHouse.Facing, quietHouse.StableId);
            Yard(10, "homestead-02", 2.30f, 4.35f, 1.1f, .6f);
            Core(11, 10, -1.5f, House(10).FootprintSize.y * .5f + .5f, 1.35f, .5f);
            Yard(14, "shop-bakery", -2f, 6.5f, 1f, .8f);
            // Meeting lectern rests at the edge of the paved civic forecourt.
            Yard(15, "town-hall", 6.4f, 6.7f, 1.25f, .7f);
            Yard(16, "homestead-16", -.30f, 4.15f, .75f, .15f);
            Add(17, village.Expansion.LodgeEntrance - village.SlopeRight * 5.3f - village.Uphill * .36f,
                new Vector2(1.5f, 1.5f), -village.Uphill, "ski-lodge");
            // Stored against the rescue building under the surviving sled canopy.
            Yard(20, "mountain-rescue", 6.65f, 2.6f, .85f, .65f, 90f);
            Vector2 avalanche = AlpineVillageAvalanchePlan.Origin;
            Local(21, avalanche.x + 2f, avalanche.y - 1.8f, 1.5f, .65f, 90f);
            Local(22, avalanche.x + 10.6f, avalanche.y - .3f, 1.65f, 1.25f, 150f);
            // Beside the school bench, away from the central gate/door axis.
            Yard(24, "school", 4f, 12.4f, 1.3f, .8f, 90f);
            // Between the wall and workbench, beneath the intact workshop roof.
            Yard(25, "workshop", 6.1f, 1.85f, 1.15f, 1.1f, 180f);
            Add(26, village.Expansion.TruckWreckCenter, new Vector2(7f, 2.9f), village.SlopeRight);
            Add(27, village.Expansion.ChairPileCenter, new Vector2(6.2f, 4.2f), village.Uphill);
            Local(28, -137f, -24f, .95f, .7f, 90f, "trade-warehouse");
            Local(29, -133.9f, -44f, 1.3f, 1f, 90f);
            Local(30, -125.6f, -37.5f, 1.45f, 1.1f, -90f);
            Local(31, -125.9f, -47f, 1.3f, .9f, -90f);
            Local(32, -133.8f, -50f, .85f, .55f, 90f);
            return result;
        }

        public void ValidateRoadClearance(AlpineVillagePlan village,
            IReadOnlyList<AlpineVillagePathDescriptor> paths)
        {
            foreach (VillageNarrativePoint point in points)
            {
                if (!point.GroundProp) continue; // Paper/bicycle are mounted above a door path.
                float clearance = MeasureRoadClearance(village, paths,
                    point.Position, point.Rotation, point.LocalFootprint);
                if (clearance < MinimumRoadClearance)
                    throw new InvalidOperationException($"{point.Id} has only {clearance:F3} m road clearance.");
            }
        }

        /// <summary>Conservative horizontal clearance of the whole oriented
        /// footprint. Segment distance catches a road crossing between corners;
        /// lane widths, both path kinds and actual junction contours are read
        /// from the same pure plan that draws the surfaces. No asset loads.</summary>
        public static float MeasureRoadClearance(AlpineVillagePlan village,
            IReadOnlyList<AlpineVillagePathDescriptor> paths,
            Vector3 position, Quaternion rotation, Rect localFootprint)
        {
            Vector2[] footprint = Corners(position, rotation, localFootprint);
            float best = float.PositiveInfinity;
            void Strip(Vector3 a, Vector3 b, float radius)
                => best = Mathf.Min(best, SegmentPolygonDistance(XZ(a), XZ(b), footprint) - radius);
            var lane = village.Lane.Samples;
            for (int index = 1; index < lane.Count; index++)
                Strip(lane[index - 1].Position, lane[index].Position,
                    Mathf.Max(lane[index - 1].Width, lane[index].Width) * .5f);
            foreach (var path in paths) Strip(path.Start, path.End, path.SurfaceHalfWidth);
            foreach (var junction in village.Expansion.Junctions)
                best = Mathf.Min(best, PolygonDistance(footprint, junction.Contour));
            // The last two asphalt metres and the disconnected opposite shelf
            // are painted by the road builder outside the traversable paths.
            var expansion = village.Expansion;
            Strip(expansion.ToWorld(new Vector2(-130f, -52f)), expansion.CliffEdge, expansion.RoadWidth * .5f);
            Vector3 previous = expansion.FarRoadEdge;
            for (float along = -69f; along >= -107f; along -= 2f)
            {
                Vector3 next = expansion.FarRoadPoint(along);
                Strip(previous, next, expansion.RoadWidth * .5f);
                previous = next;
            }
            return best;
        }

        private static Vector2[] Corners(Vector3 position, Quaternion rotation, Rect rect)
        {
            Vector2 Corner(float x, float z) => XZ(position + rotation * new Vector3(x, 0f, z));
            return new[] { Corner(rect.xMin, rect.yMin), Corner(rect.xMax, rect.yMin),
                Corner(rect.xMax, rect.yMax), Corner(rect.xMin, rect.yMax) };
        }

        private static float PolygonDistance(IReadOnlyList<Vector2> a, IReadOnlyList<Vector2> b)
        {
            if (Contains(a, b[0]) || Contains(b, a[0])) return 0f;
            float best = float.PositiveInfinity;
            for (int index = 0; index < a.Count; index++)
                best = Mathf.Min(best, SegmentPolygonDistance(a[index], a[(index + 1) % a.Count], b));
            return best;
        }

        private static float SegmentPolygonDistance(Vector2 a, Vector2 b, IReadOnlyList<Vector2> polygon)
        {
            if (Contains(polygon, a) || Contains(polygon, b)) return 0f;
            float best = float.PositiveInfinity;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 c = polygon[index], d = polygon[(index + 1) % polygon.Count];
                float denominator = Cross(b - a, d - c);
                if (Mathf.Abs(denominator) > .000001f)
                {
                    float alongA = Cross(c - a, d - c) / denominator;
                    float alongB = Cross(c - a, b - a) / denominator;
                    if (alongA >= 0f && alongA <= 1f && alongB >= 0f && alongB <= 1f) return 0f;
                }
                best = Mathf.Min(best, Vector2.Distance(a, Closest(c, d, a)),
                    Vector2.Distance(b, Closest(c, d, b)), Vector2.Distance(c, Closest(a, b, c)),
                    Vector2.Distance(d, Closest(a, b, d)));
            }
            return best;
        }

        private static bool Contains(IReadOnlyList<Vector2> polygon, Vector2 point)
        {
            bool inside = false;
            for (int index = 0, previous = polygon.Count - 1; index < polygon.Count; previous = index++)
            {
                Vector2 a = polygon[index], b = polygon[previous];
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < a.x + (b.x - a.x) * (point.y - a.y) / (b.y - a.y)) inside = !inside;
            }
            return inside;
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static Rect ModelFootprint(int id)
        {
            // Outward-rounded millimetre bounds from the two Blender manifests.
            // The capture independently measures the placed meshes, so a stale
            // entry cannot silently make the off-road contract pass.
            switch (id)
            {
                case 1: return Rect.MinMaxRect(-.291f, -.291f, .291f, .291f);
                case 2: return Rect.MinMaxRect(-.391f, -.276f, .391f, .411f);
                case 3: return Rect.MinMaxRect(-.451f, -.287f, .451f, .297f);
                case 4: return Rect.MinMaxRect(-.276f, .018f, .276f, .071f);
                case 5: return Rect.MinMaxRect(-.591f, -.331f, .591f, .331f);
                case 6: return Rect.MinMaxRect(-.826f, -.231f, .826f, .291f);
                case 7: return Rect.MinMaxRect(-.488f, -.108f, .488f, .033f);
                case 8: return Rect.MinMaxRect(-.381f, -.301f, .381f, .407f);
                case 9: return Rect.MinMaxRect(-.445f, -.272f, .445f, .321f);
                case 10: return Rect.MinMaxRect(-.396f, -.214f, .396f, .170f);
                case 11: return Rect.MinMaxRect(-.713f, -.178f, .713f, .261f);
                case 12: return Rect.MinMaxRect(-.318f, -.375f, .318f, .375f);
                case 13: return Rect.MinMaxRect(-.611f, -.402f, .631f, .342f);
                case 14: return Rect.MinMaxRect(-.396f, -.301f, .396f, .621f);
                case 15: return Rect.MinMaxRect(-.421f, -.320f, .421f, .334f);
                case 16: return Rect.MinMaxRect(-.276f, .018f, .276f, .071f);
                case 17: return Rect.MinMaxRect(-.426f, -.231f, .611f, .476f);
                case 18: return Rect.MinMaxRect(-.641f, -.201f, .641f, .233f);
                case 19: return Rect.MinMaxRect(-.951f, -.253f, .967f, .253f);
                case 20: return Rect.MinMaxRect(-.374f, -.106f, .374f, .156f);
                case 21: return Rect.MinMaxRect(-.640f, -.628f, .666f, .254f);
                case 22: return Rect.MinMaxRect(-.795f, -.445f, .854f, .455f);
                case 23: return Rect.MinMaxRect(-.413f, -.354f, .410f, .176f);
                case 24: return Rect.MinMaxRect(-.273f, -.561f, .271f, .721f);
                case 25: return Rect.MinMaxRect(-.531f, -.426f, .531f, .426f);
                case 26: return Rect.MinMaxRect(-1.226f, -3.228f, 1.246f, 3.258f);
                case 27: return Rect.MinMaxRect(-2.598f, -1.889f, 2.858f, 1.660f);
                case 28: return Rect.MinMaxRect(-.451f, -.311f, .451f, .361f);
                case 29: return Rect.MinMaxRect(-.511f, -.521f, .801f, .697f);
                case 30: return Rect.MinMaxRect(-.491f, -.363f, .731f, .488f);
                case 31: return Rect.MinMaxRect(-.401f, -.541f, .656f, .651f);
                case 32: return Rect.MinMaxRect(-.350f, -.057f, .450f, .107f);
                default: throw new ArgumentOutOfRangeException(nameof(id));
            }
        }

        public bool ClearsForest(Vector2 position, float crownRadius)
        {
            foreach (VillageNarrativePoint point in points)
            {
                Vector2 target = XZ(point.Position);
                float radius = Mathf.Max(point.Footprint.x, point.Footprint.y) * .5f;
                if (Vector2.Distance(position, target) < radius + crownRadius + .8f) return false;
            }
            return true;
        }

        public float LimitSnow(Vector2 position, float depth)
        {
            foreach (VillageNarrativePoint point in points)
            {
                if (point.Existing || point.Document) continue;
                Vector3 local = Quaternion.Inverse(point.Rotation) *
                    new Vector3(position.x - point.Position.x, 0f, position.y - point.Position.z);
                float outside = new Vector2(Mathf.Max(0f, Mathf.Abs(local.x) - point.Footprint.x * .5f),
                    Mathf.Max(0f, Mathf.Abs(local.z) - point.Footprint.y * .5f)).magnitude;
                if (outside < .6f)
                    depth = Mathf.Min(depth, Mathf.Lerp(.07f, depth, Mathf.SmoothStep(0f, 1f, outside / .6f)));
            }
            return depth;
        }

        internal static Vector3 Ground(AlpineVillagePlan plan, Vector3 point)
        {
            var xz = XZ(point);
            point.y = Mathf.Max(AlpineVillageTerrainSampler.SampleHeight(plan, xz),
                AlpineVillageTerrainSampler.SampleMeshHeight(plan, xz));
            if (plan.Station.PadArea.ContainsXZ(point))
                point.y = Mathf.Max(point.y, plan.Station.PadArea.Center.y + AlpineVillagePlanner.StationPadTopOffset);
            return point;
        }

        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
        private static Vector2 Closest(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 segment = b - a;
            return a + segment * Mathf.Clamp01(Vector2.Dot(point - a, segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
        }
    }
}
