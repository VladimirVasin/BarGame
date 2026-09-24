using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed class VillageNarrativePoint
    {
        public int Number { get; internal set; }
        public string Id => "village.narrative." + Number.ToString("00");
        public int Sector => (Number - 1) / 4;
        public bool Existing => Number == 26 || Number == 27;
        public bool Document => Number == 4 || Number == 16;
        public bool GroundProp => !Document && Number != 11;
        public string HouseId { get; internal set; }
        public Vector3 Position { get; internal set; }
        public Vector3 AuthoredPosition { get; internal set; }
        public Quaternion Rotation { get; internal set; }
        public Vector3 DiscoveryPosition { get; internal set; }
        public Vector3 DiscoveryLookDirection { get; internal set; }
        public Vector3 RoadDirection { get; internal set; }
        public string RoadId { get; internal set; }
        public bool Asphalt { get; internal set; }
        public Vector2 Footprint { get; internal set; }
        public Rect LocalFootprint { get; internal set; }
    }

    /// <summary>Authored places, with discovery on an existing road and a separate
    /// off-road approach. Shared by forest clearance, snow and scene construction.</summary>
    public sealed class AlpineVillageNarrativePlan
    {
        public const float MinimumRoadClearance = .35f;
        private readonly List<VillageNarrativePoint> points = new List<VillageNarrativePoint>();
        public IReadOnlyList<VillageNarrativePoint> Points => points;

        public static AlpineVillageNarrativePlan Create(AlpineVillagePlan village)
        {
            var result = new AlpineVillageNarrativePlan();
            IReadOnlyList<AlpineVillagePathDescriptor> paths = AlpineVillagePathPlanner.Create(village);
            void Add(int id, Vector3 position, Vector2 footprint, string house = null, Vector3? facing = null)
            {
                position = Ground(village, position);
                var point = new VillageNarrativePoint
                {
                    Number = id, Position = position, AuthoredPosition = position,
                    LocalFootprint = ModelFootprint(id), HouseId = house
                };
                point.Footprint = Vector2.Max(footprint, new Vector2(
                    Mathf.Max(Mathf.Abs(point.LocalFootprint.xMin), Mathf.Abs(point.LocalFootprint.xMax)) * 2f,
                    Mathf.Max(Mathf.Abs(point.LocalFootprint.yMin), Mathf.Abs(point.LocalFootprint.yMax)) * 2f));
                FindRoad(village, paths, point);
                SetRotation(village, point, facing);
                if (point.GroundProp && !point.Existing) KeepOffRoad(village, paths, point, facing);
                result.points.Add(point);
            }
            void Local(int id, float x, float z, float width, float depth, string house = null)
                => Add(id, village.Expansion.ToWorld(new Vector2(x, z)), new Vector2(width, depth), house);
            void Core(int id, int home, float across, float outwards, float width, float depth, bool door = false)
            {
                string house = "village-house-" + home.ToString("00");
                AlpineVillagePlotDescriptor plot = null;
                foreach (var item in village.Plots) if (item.StableId == house) { plot = item; break; }
                if (plot == null) throw new InvalidOperationException(house);
                Vector3 right = Vector3.Cross(Vector3.up, plot.Facing);
                Vector3 origin = door ? plot.DoorGroundPosition :
                    plot.GroundCenter + plot.Facing * (plot.FootprintSize.y * .5f);
                Add(id, origin + right * across + plot.Facing * outwards,
                    new Vector2(width, depth), house, plot.Facing);
            }
            void Outer(int id, int home, float across, float outwards, float width, float depth)
            {
                string house = "homestead-" + home.ToString("00");
                foreach (var plot in village.Expansion.Abandonment.Plots)
                    if (plot.Id == house)
                    {
                        Add(id, plot.World(new Vector2(across, plot.Size.y * .5f + outwards)),
                            new Vector2(width, depth), house, plot.Forward);
                        return;
                    }
                throw new InvalidOperationException(house);
            }

            Local(1, 4.3f, -1.6f, .9f, .7f);
            var lower = village.Lane.Sample(4f);
            Add(2, lower.Position - lower.Right * 3.15f, new Vector2(.9f, .8f));
            Core(3, 0, -1.6f, .7f, 1.1f, .7f);
            Core(4, 2, 0f, .14f, .75f, .15f, true);
            Core(5, 4, -3.65f, 2.5f, 1.45f, .85f);
            Core(6, 8, -2.6f, 1.25f, 1.9f, .9f);
            Core(7, 8, 2.5f, .65f, 1.4f, .5f);
            Core(8, 11, -2.6f, 1.2f, 1.25f, .75f);
            Core(9, 11, 2.5f, 2.9f, .8f, .8f);
            // The folded furnishings stand by their own former yard gates,
            // where their silhouettes can be noticed from the existing road.
            Outer(10, 2, 2f, 4.9f, 1.1f, .6f);
            Core(11, 10, -1.5f, .5f, 1.35f, .5f);
            Outer(12, 3, -3.1f, 5f, .7f, .8f);
            Local(13, -45f, 3f, 1.35f, .8f);
            Local(14, -91f, .5f, 1f, .8f, "shop-bakery");
            Local(15, -97.3f, 18.7f, 1.25f, .7f, "town-hall");
            Outer(16, 16, -.30f, .15f, .75f, .15f);
            Local(17, -142.3f, 48.8f, 1.5f, 1.5f, "ski-lodge");
            Local(18, -147.2f, 53f, 1.6f, .65f, "ski-lodge");
            Local(19, -129.4f, 48.2f, 2f, .8f, "ski-lodge");
            Local(20, -171f, 60f, .85f, .65f, "mountain-rescue");
            Local(21, -155.6f, 103f, 1.5f, .65f);
            Local(22, -139f, 89.8f, 1.65f, 1.25f);
            Local(23, -146.4f, 100.2f, .75f, .65f, "homestead-15");
            Local(24, -106f, 96f, 1.3f, .8f, "school");
            Local(25, -124.4f, -18f, 1.15f, 1.1f, "workshop");
            Add(26, village.Expansion.TruckWreckCenter, new Vector2(7f, 2.9f));
            Add(27, village.Expansion.ChairPileCenter, new Vector2(6.2f, 4.2f));
            Local(28, -137f, -24f, .95f, .7f, "trade-warehouse");
            Local(29, -133.9f, -44f, 1.3f, 1f);
            Local(30, -125.6f, -37.5f, 1.45f, 1.1f);
            Local(31, -125.9f, -47f, 1.3f, .9f);
            Local(32, -133.8f, -50f, .85f, .55f);
            result.ValidateRoadClearance(village, paths);
            return result;
        }

        private static void SetRotation(AlpineVillagePlan village, VillageNarrativePoint point, Vector3? facing)
        {
            if (point.Existing)
            {
                // These are the transforms of the existing expansion meshes,
                // independent of which side supplies their discovery view.
                point.Rotation = Quaternion.LookRotation(village.Uphill) *
                    Quaternion.Euler(0f, point.Number == 26 ? 90f : 0f, 0f);
                return;
            }
            Vector3 forward = facing ?? (point.DiscoveryPosition - point.Position);
            forward.y = 0f;
            point.Rotation = Quaternion.LookRotation(forward.sqrMagnitude > .01f ? forward : village.Uphill);
        }

        private static void KeepOffRoad(AlpineVillagePlan village,
            IReadOnlyList<AlpineVillagePathDescriptor> paths, VillageNarrativePoint point, Vector3? facing)
        {
            if (MeasureRoadClearance(village, paths, point.Position, point.Rotation, point.LocalFootprint)
                >= MinimumRoadClearance) return;
            Vector3 origin = point.Position;
            Quaternion originalRotation = point.Rotation;
            // A seeded doorstep/work path may differ slightly between plans.
            // Move only a conflicting new prop, by the smallest bounded ring;
            // the existing roads and large legacy objects remain authored.
            for (float radius = .25f; radius <= 3f; radius += .25f)
            {
                int count = Mathf.Max(12, Mathf.CeilToInt(2f * Mathf.PI * radius / .25f));
                for (int index = 0; index < count; index++)
                {
                    float angle = index * (2f * Mathf.PI / count);
                    point.Position = origin + (village.SlopeRight * Mathf.Cos(angle) +
                        village.Uphill * Mathf.Sin(angle)) * radius;
                    point.Rotation = originalRotation;
                    if (MeasureRoadClearance(village, paths, point.Position, point.Rotation, point.LocalFootprint)
                        < MinimumRoadClearance || !ClearPlacement(village, point)) continue;
                    try { FindRoad(village, paths, point); }
                    catch (InvalidOperationException) { continue; }
                    SetRotation(village, point, facing);
                    if (MeasureRoadClearance(village, paths, point.Position, point.Rotation, point.LocalFootprint)
                        < MinimumRoadClearance || !ClearPlacement(village, point)) continue;
                    point.Position = Ground(village, point.Position);
                    return;
                }
            }
            throw new InvalidOperationException("No off-road place within its authored yard for " + point.Id);
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

        private static bool ClearPlacement(AlpineVillagePlan village, VillageNarrativePoint point)
        {
            Vector2[] footprint = Corners(point.Position, point.Rotation, point.LocalFootprint);
            foreach (var plot in village.Plots)
            {
                if (plot.Kind == AlpineVillagePlotKind.Spring) continue;
                Vector2[] body = Corners(plot.GroundCenter, Quaternion.LookRotation(plot.Facing),
                    new Rect(-plot.FootprintSize * .5f, plot.FootprintSize));
                if (PolygonDistance(footprint, body) < .1f) return false;
            }
            foreach (var plot in village.Expansion.Abandonment.Plots)
            {
                if (!plot.Closed) continue;
                Vector2[] body = Corners(plot.GroundCenter, plot.Rotation, new Rect(-plot.Size * .5f, plot.Size));
                if (PolygonDistance(footprint, body) < .1f) return false;
            }
            foreach (var box in village.Expansion.LocalObstacles)
            {
                Vector2[] body = Corners(village.Expansion.ToWorld(new Vector2(box.center.x, box.center.z)),
                    Quaternion.LookRotation(village.Uphill), new Rect(-box.size.x * .5f, -box.size.z * .5f,
                        box.size.x, box.size.z));
                if (PolygonDistance(footprint, body) < .1f) return false;
            }
            return true;
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
                Vector2 closest = Closest(XZ(point.DiscoveryPosition), target, position);
                if (Vector2.Distance(position, closest) < crownRadius + .8f) return false;
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
            return point;
        }

        private static void FindRoad(AlpineVillagePlan village,
            IReadOnlyList<AlpineVillagePathDescriptor> paths, VillageNarrativePoint point)
        {
            float best = float.PositiveInfinity;
            void Candidate(Vector3 road, Vector3 direction, string id, bool asphalt)
            {
                Vector3 toward = point.Position - road;
                toward.y = 0f;
                float distance = toward.magnitude;
                if (distance < 1f || distance >= 31f) return;
                // The broad, low hood sits close to the path: notice it a few
                // strides earlier, instead of hiding half of it behind the legs.
                if (point.Number == 22 && distance < 4f) return;
                direction.y = 0f;
                direction.Normalize();
                if (Vector3.Dot(direction, toward) < 0f) direction = -direction;
                float angle = Vector3.Angle(direction, toward);
                float preferredDistance = point.Existing ? 10f : 5.5f;
                // Discovery happens while travelling along the road. A nearest
                // point aimed straight at a small prop hides it behind the hero.
                // Prefer a diagonal reveal; retain a finite fallback for a short
                // threshold path where no longer stretch is available.
                float anglePenalty = Mathf.Max(0f, 23f - angle) * 1.7f +
                    Mathf.Max(0f, angle - 54f) * 1.3f;
                float score = Mathf.Abs(distance - preferredDistance) * 2f +
                    Mathf.Abs(angle - 38f) * .1f + anglePenalty;
                if (score >= best || !ClearBuildings(village, road, point.Position, point.HouseId)) return;
                float signed = Vector3.SignedAngle(direction, toward, Vector3.up);
                float glance = Mathf.Sign(signed) * Mathf.Clamp(Mathf.Abs(signed) - 26f, 0f, 20f);
                Vector3 look = Quaternion.AngleAxis(glance, Vector3.up) * direction;
                // A clear hero-to-object segment is insufficient at a cottage
                // corner: the normal camera is another 2.6 m behind the hero.
                // Both views must clear the actual building footprints.
                if (!ClearBuildings(village, road - look * 2.6f, point.Position, point.HouseId)) return;
                best = score;
                point.DiscoveryPosition = Ground(village, road);
                point.RoadDirection = direction;
                // An optional glance of at most twenty degrees, still leaving
                // the object off the hero's central silhouette. Camera distance,
                // pitch and field of view remain the ordinary walking settings.
                point.DiscoveryLookDirection = look;
                point.RoadId = id;
                point.Asphalt = asphalt;
            }
            void RoadSamples(Vector3 center, Vector3 direction, float halfWidth, string id, bool asphalt)
            {
                direction.y = 0f;
                direction.Normalize();
                Vector3 right = Vector3.Cross(Vector3.up, direction);
                float offset = Mathf.Min(.65f, Mathf.Max(0f, halfWidth - .35f));
                Candidate(center, direction, id, asphalt);
                if (offset < .05f) return;
                Candidate(center + right * offset, direction, id, asphalt);
                Candidate(center - right * offset, direction, id, asphalt);
            }
            foreach (var lane in village.Lane.Samples)
                RoadSamples(lane.Position, lane.Forward, lane.Width * .5f, "main-lane", false);
            foreach (var path in paths)
            {
                int steps = Mathf.Max(1, Mathf.CeilToInt(path.LengthXZ / .8f));
                for (int i = 0; i <= steps; i++)
                    RoadSamples(Vector3.Lerp(path.Start, path.End, i / (float)steps),
                        path.End - path.Start, path.SurfaceHalfWidth, path.StableId,
                        path.Kind == AlpineVillagePathKind.AbandonedRoad);
            }
            if (float.IsPositiveInfinity(best))
                throw new InvalidOperationException("No road sightline for " + point.Id);
        }

        private static bool ClearBuildings(AlpineVillagePlan village, Vector3 a, Vector3 b, string targetHouse)
        {
            float length = Vector2.Distance(XZ(a), XZ(b));
            int steps = Mathf.CeilToInt(length / .6f);
            for (int i = 1; i < steps; i++)
            {
                Vector3 sample = Vector3.Lerp(a, b, i / (float)steps);
                foreach (var plot in village.Plots)
                {
                    if (plot.Kind == AlpineVillagePlotKind.Spring) continue;
                    Vector3 delta = sample - plot.GroundCenter;
                    float x = Vector3.Dot(delta, Vector3.Cross(Vector3.up, plot.Facing));
                    float z = Vector3.Dot(delta, plot.Facing);
                    if (Mathf.Abs(x) < plot.FootprintSize.x * .5f &&
                        Mathf.Abs(z) < plot.FootprintSize.y * .5f) return false;
                }
                Vector2 local = village.Expansion.ToLocal(sample);
                foreach (var plot in village.Expansion.Abandonment.Plots)
                    if (plot.Closed && plot.OutsideBody(local) < .01f) return false;
                foreach (var box in village.Expansion.LocalObstacles)
                    if (local.x > box.min.x && local.x < box.max.x &&
                        local.y > box.min.z && local.y < box.max.z) return false;
            }
            return true;
        }
        private static Vector2 XZ(Vector3 point) => new Vector2(point.x, point.z);
        private static Vector2 Closest(Vector2 a, Vector2 b, Vector2 point)
        {
            Vector2 segment = b - a;
            return a + segment * Mathf.Clamp01(Vector2.Dot(point - a, segment) / Mathf.Max(.0001f, segment.sqrMagnitude));
        }
    }
}
