using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Triangle = BarPromenade.GroundSurfacePartitioner.Triangle;

namespace BarPromenade
{
    internal sealed partial class CityGroundSurfaceSystem
    {
        private const float FacadeBucketSize = 8f;
        private readonly List<Facade> facades = new();
        private readonly CityGroundedWindowMeshes groundedWindowMeshes = new();

        private sealed class Facade
        {
            internal Facade(CityBuildingAssetRegistry registry, BuildingLot lot, int seed)
            { Registry = registry; Lot = lot; Seed = seed; }

            internal readonly CityBuildingAssetRegistry Registry;
            internal readonly BuildingLot Lot;
            internal readonly int Seed;
            internal ulong Mask;
        }

        private readonly struct FacadeBlock
        {
            internal FacadeBlock(Facade facade, ulong bit, Vector3 start, Vector3 end)
            {
                Facade = facade;
                Bit = bit;
                Start = start;
                End = end;
                Bounds = Rect.MinMaxRect(Mathf.Min(start.x, end.x), Mathf.Min(start.z, end.z),
                    Mathf.Max(start.x, end.x), Mathf.Max(start.z, end.z));
            }

            internal readonly Facade Facade;
            internal readonly ulong Bit;
            internal readonly Vector3 Start, End;
            internal readonly Rect Bounds;
        }

        internal static void RegisterFacade(CityBuildingAssetRegistry registry, BuildingLot lot, int seed)
        {
            var ground = registry.GetComponentInParent<CityGroundSurfaceSystem>(true);
            if (ground != null) ground.facades.Add(new Facade(registry, lot, seed));
        }

        private IEnumerator ApplyGroundedFacades(List<Triangle> surface, List<Triangle>[] localSurfaces, bool incremental)
        {
            if (facades.Count == 0) yield break;
            var buckets = new Dictionary<Vector2Int, List<FacadeBlock>>();
            var localBlocks = new Dictionary<string, (int slot, Vector3 start, Vector3 end)[]>();
            Matrix4x4 toGround = transform.worldToLocalMatrix;
            foreach (Facade facade in facades)
            {
                var registry = facade.Registry;
                if (!localBlocks.TryGetValue(registry.StableId, out var blocks))
                {
                    var prototype = new List<(int, Vector3, Vector3)>();
                    foreach (var slot in registry.WindowSlots)
                    {
                        if (slot.OpeningKind != CityBuildingOpeningKind.Window) continue;
                        CityFacadeGroundContact.BlockBottom(registry, slot, out Vector3 start, out Vector3 end);
                        prototype.Add((slot.Uv2SlotId, start, end));
                    }
                    localBlocks.Add(registry.StableId, blocks = prototype.ToArray());
                }
                Matrix4x4 placement = toGround * registry.transform.localToWorldMatrix;
                foreach (var block in blocks)
                {
                    var placed = new FacadeBlock(facade, 1UL << block.slot,
                        placement.MultiplyPoint3x4(block.start), placement.MultiplyPoint3x4(block.end));
                    Vector2Int first = FacadeBucket(placed.Bounds.min), last = FacadeBucket(placed.Bounds.max);
                    for (int z = first.y; z <= last.y; z++)
                    for (int x = first.x; x <= last.x; x++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!buckets.TryGetValue(key, out var bucket)) buckets.Add(key, bucket = new List<FacadeBlock>());
                        bucket.Add(placed);
                    }
                }
            }

            yield return Survey(surface);
            foreach (List<Triangle> local in localSurfaces) yield return Survey(local);
            // The Ridge banks and saddle keep their own meshes. Their visible
            // cuts and grades still participate, in the same frame as the facade.
            var surveyed = new HashSet<Renderer>();
            yield return SurveyScenery(jointScenery);
            yield return SurveyScenery(grassScenery);
            foreach (Facade facade in facades)
            {
                if (facade.Mask == 0) continue;
                var registry = facade.Registry;
                registry.GroundedWindowMask = facade.Mask;
                groundedWindowMeshes.Apply(registry, facade.Mask);
                CityBuildingWindowSlotAppearance.Apply(registry.WindowGlassRenderer, registry, facade.Lot, facade.Seed);
                if (incremental) yield return new CompositionStep("ground_facade_mesh", .98f);
            }
            facades.Clear();
            groundedWindowMeshes.ReleaseSourceData();

            IEnumerator SurveyScenery(List<Source> scenery)
            {
                foreach (Source source in scenery)
                {
                    if (!surveyed.Add(source.Renderer)) continue;
                    if (source.Top == null) Capture(source);
                    yield return Survey(source.Top);
                }
            }

            IEnumerator Survey(List<Triangle> triangles)
            {
                for (int i = 0; i < triangles.Count; i++)
                {
                    if (incremental && i != 0 && i % 4096 == 0)
                        yield return new CompositionStep("ground_facade_survey", .98f);
                    Triangle face = triangles[i];
                    Rect bounds = Bounds(face);
                    Vector2Int first = FacadeBucket(bounds.min), last = FacadeBucket(bounds.max);
                    // A block may straddle a bucket. Test it once, in the
                    // first bucket that the triangle and the block share.
                    for (int z = first.y; z <= last.y; z++)
                    for (int x = first.x; x <= last.x; x++)
                    {
                        if (!buckets.TryGetValue(new Vector2Int(x, z), out var bucket)) continue;
                        foreach (FacadeBlock block in bucket)
                        {
                            if ((block.Facade.Mask & block.Bit) != 0 || !Touches(bounds, block.Bounds)) continue;
                            Vector2Int from = FacadeBucket(block.Bounds.min);
                            if (x != Mathf.Max(first.x, from.x) || z != Mathf.Max(first.y, from.y)) continue;
                            if (CityFacadeGroundContact.ReachesBlock(block.Start, block.End,
                                    face.A.Position, face.B.Position, face.C.Position))
                                block.Facade.Mask |= block.Bit;
                        }
                    }
                }
            }
        }

        private static Vector2Int FacadeBucket(Vector2 point) => new(
            Mathf.FloorToInt(point.x / FacadeBucketSize), Mathf.FloorToInt(point.y / FacadeBucketSize));

        private static bool Touches(Rect a, Rect b) =>
            a.xMin <= b.xMax && a.xMax >= b.xMin && a.yMin <= b.yMax && a.yMax >= b.yMin;
    }

    internal static class CityFacadeGroundContact
    {
        // A normal foundation seam can meet the wall below the first storey.
        // The first block starts at most half a storey under its window centre.
        private const float LowestBlockAboveBase = .15f;
        private const float MaximumHalfStorey = 1.5f;
        private const float GroundTolerance = .002f;

        internal static void BlockBottom(CityBuildingAssetRegistry registry, CityBuildingWindowSlot slot,
            out Vector3 start, out Vector3 end)
        {
            bool acrossX = slot.Side is "Front" or "Rear";
            int across = acrossX ? 0 : 2;
            int depth = acrossX ? 2 : 0;
            Vector3 centre = slot.LocalCenter;
            Bounds face = registry.LocalBounds;
            foreach (var attachment in registry.FacadeAttachments)
                if (attachment.Side == slot.Side) { face = attachment.LocalBounds; break; }
            float left = face.min[across], right = face.max[across];
            float lowerTop = float.NegativeInfinity;
            float rowBottom = centre.y - slot.SizeMeters.y * .5f;
            float rowCenter = centre.y;
            float upperCenter = float.PositiveInfinity;
            foreach (var other in registry.WindowSlots)
            {
                Vector3 neighbour = other.LocalCenter;
                if (other.Side != slot.Side || Mathf.Abs(neighbour[depth] - centre[depth]) > .01f) continue;
                if (other.Floor == slot.Floor)
                {
                    float middle = (neighbour[across] + centre[across]) * .5f;
                    if (neighbour[across] < centre[across]) left = Mathf.Max(left, middle);
                    if (neighbour[across] > centre[across]) right = Mathf.Min(right, middle);
                    if (other.OpeningKind == CityBuildingOpeningKind.Window)
                    {
                        rowCenter = Mathf.Min(rowCenter, neighbour.y);
                        rowBottom = Mathf.Min(rowBottom, neighbour.y - other.SizeMeters.y * .5f);
                    }
                }
                else if (other.OpeningKind == CityBuildingOpeningKind.Window)
                {
                    if (other.Floor < slot.Floor) lowerTop = Mathf.Max(lowerTop, neighbour.y + other.SizeMeters.y * .5f);
                    else upperCenter = Mathf.Min(upperCenter, neighbour.y);
                }
            }
            float bottom = float.IsNegativeInfinity(lowerTop)
                ? Mathf.Max(registry.LocalBounds.min.y + LowestBlockAboveBase,
                    rowCenter - Mathf.Min(MaximumHalfStorey, (upperCenter - rowCenter) * .5f))
                : (lowerTop + rowBottom) * .5f;
            start = end = centre;
            start.y = end.y = Mathf.Min(bottom, rowBottom);
            start[across] = Mathf.Min(left, centre[across] - slot.SizeMeters.x * .5f);
            end[across] = Mathf.Max(right, centre[across] + slot.SizeMeters.x * .5f);
        }

        // Clip the entire lower block edge against the actual ground triangle
        // in XZ. Their height difference is linear over the clipped segment.
        internal static bool ReachesBlock(Vector3 start, Vector3 end, Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 normal = Vector3.Cross(b - a, c - a);
            if (normal.y <= .0000001f || Mathf.Max(a.y, b.y, c.y) <= Mathf.Min(start.y, end.y) + GroundTolerance)
                return false;
            float enter = 0f, exit = 1f;
            if (!Clip(a, b) || !Clip(b, c) || !Clip(c, a)) return false;
            return IsAbove(Vector3.LerpUnclamped(start, end, enter)) ||
                   IsAbove(Vector3.LerpUnclamped(start, end, exit));

            bool Clip(Vector3 edgeStart, Vector3 edgeEnd)
            {
                Vector3 edge = edgeEnd - edgeStart;
                float first = edge.z * (start.x - edgeStart.x) - edge.x * (start.z - edgeStart.z);
                float last = edge.z * (end.x - edgeStart.x) - edge.x * (end.z - edgeStart.z);
                if (first >= 0f && last >= 0f) return true;
                if (first < 0f && last < 0f) return false;
                float crossing = first / (first - last);
                if (first < 0f) enter = Mathf.Max(enter, crossing);
                else exit = Mathf.Min(exit, crossing);
                return enter <= exit;
            }

            bool IsAbove(Vector3 point) => Vector3.Dot(a - point, normal) > GroundTolerance * normal.y;
        }
    }
}
