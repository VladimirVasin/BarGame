using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class CombatHurtboxes
    {
        private readonly Dictionary<SkinnedMeshRenderer, HeadSurface> headSurfaceCache = new Dictionary<SkinnedMeshRenderer, HeadSurface>();
        private HeadSurface[] intactHeadSurfaces, activeHeadSurfaces;
        private Matrix4x4 frozenHeadToLocal;
        private Vector3 frozenHeadCentre, frozenHeadForward, frozenHeadUp, frozenHeadRight;
        private Vector3 headLocalCentre, headLocalForward, headLocalUp;
        internal long HeadPoseCaptureTicks { get; private set; }
        internal int HeadSurfaceSnapshotBuilds { get; private set; }

        // These surfaces are anatomy. Hair/headwear and wound overlays must not
        // widen a bullet target, even when they travel with a fractured sector.
        internal static bool IsHeadFlesh(string name) => name == "GEO_Head" || name == "GEO_FaceSurface" ||
            name.StartsWith("GEO_Ear.", StringComparison.Ordinal) || name.StartsWith("GEO_EarFold.", StringComparison.Ordinal);

        private void InitializeHeadSurfaces(Transform rigRoot)
        {
            var surfaces = new List<HeadSurface>();
            foreach (SkinnedMeshRenderer skin in rigRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                if (IsHeadFlesh(skin.name)) surfaces.Add(RequireHeadSurface(skin));
            if (surfaces.Count == 0) throw new InvalidOperationException("Combat bullets require authored head surfaces.");
            intactHeadSurfaces = activeHeadSurfaces = surfaces.ToArray();
            headLocalForward = headBind.inverse.MultiplyVector(actorFrame.forward).normalized;
            headLocalUp = headBind.inverse.MultiplyVector(actorFrame.up).normalized;
            // Keep the anatomical classification frame independent of which
            // surviving sector happens to be first in the query list.
            Bounds bounds = default;
            bool measured = false;
            foreach (HeadSurface surface in intactHeadSurfaces)
            {
                surface.Capture();
                if (!surface.Active) continue;
                foreach (Vector3 point in surface.Points)
                {
                    Vector3 local = headBone.InverseTransformPoint(point);
                    if (!measured) { bounds = new Bounds(local, Vector3.zero); measured = true; }
                    else bounds.Encapsulate(local);
                }
            }
            if (!measured) throw new InvalidOperationException("Combat bullets require a visible anatomical head.");
            headLocalCentre = bounds.center;
        }

        private HeadSurface RequireHeadSurface(SkinnedMeshRenderer skin)
        {
            if (!headSurfaceCache.TryGetValue(skin, out HeadSurface surface))
            {
                headSurfaceCache.Add(skin, surface = new HeadSurface(skin));
                HeadSurfaceSnapshotBuilds++;
            }
            return surface;
        }

        // Loading prepares retained anatomy snapshots without enabling them or
        // changing the intact head's contacts. Activation only selects the cache.
        internal void PrepareHeadSurface(SkinnedMeshRenderer skin) => RequireHeadSurface(skin);

        private void SetHeadSurfaces(IReadOnlyList<SkinnedMeshRenderer> retained)
        {
            if (retained == null) { activeHeadSurfaces = intactHeadSurfaces; return; }
            var replacement = new HeadSurface[retained.Count];
            for (int i = 0; i < replacement.Length; i++) replacement[i] = RequireHeadSurface(retained[i]);
            activeHeadSurfaces = replacement;
        }

        private void CaptureHeadSurfaces()
        {
            long stamp = System.Diagnostics.Stopwatch.GetTimestamp();
            frozenHeadToLocal = headBone.worldToLocalMatrix;
            frozenHeadCentre = headBone.TransformPoint(headLocalCentre);
            frozenHeadForward = headBone.TransformVector(headLocalForward).normalized;
            frozenHeadUp = Vector3.ProjectOnPlane(headBone.TransformVector(headLocalUp), frozenHeadForward).normalized;
            frozenHeadRight = Vector3.Cross(frozenHeadUp, frozenHeadForward).normalized;
            foreach (HeadSurface surface in activeHeadSurfaces) surface.CapturePose();
            HeadPoseCaptureTicks += System.Diagnostics.Stopwatch.GetTimestamp() - stamp;
        }

        internal bool SweepProjectile(Vector3 from, Vector3 to, float radius, Vector3 direction, out Hit hit)
        {
            hit = default;
            if (!Finite(from) || !Finite(to) || !Finite(direction) || !float.IsFinite(radius) || radius < 0f) return false;
            Vector3 delta = to - from;
            direction = direction.sqrMagnitude > .000001f ? direction.normalized :
                delta.sqrMagnitude > .000001f ? delta.normalized : Vector3.forward;
            bool found = SweepSphere(from, to, radius, direction, false, out hit);
            float first = found ? hit.Fraction : float.PositiveInfinity;
            var query = new SegmentQuery(from, delta, radius);
            foreach (HeadSurface surface in activeHeadSurfaces)
            {
                if (!surface.Active || !query.Intersects(surface.Bounds, first)) continue;
                surface.PrepareQuery(query, first);
                for (int triangleIndex = 0; triangleIndex < surface.QueryTriangleCount; triangleIndex++)
                {
                    HeadTriangle triangle = surface.Triangles[triangleIndex];
                    if (!query.Intersects(triangle.Bounds, first) ||
                        !triangle.FirstContact(from, delta, radius, out float fraction) || fraction >= first) continue;
                    Vector3 sphereCentre = from + delta * fraction;
                    Vector3 point = triangle.Closest(sphereCentre);
                    Vector3 normal = sphereCentre - point;
                    if (normal.sqrMagnitude < .00000001f)
                    {
                        normal = Vector3.Cross(triangle.B - triangle.A, triangle.C - triangle.A).normalized;
                        if (Vector3.Dot(normal, direction) > 0f) normal = -normal;
                    }
                    else normal.Normalize();
                    Vector3 offset = point - frozenHeadCentre;
                    MeleeHitLocation location = MeleeHitLocation.FromLocalSurface(MeleeBodyRegion.Head,
                        Vector3.Dot(offset, frozenHeadRight), Vector3.Dot(offset, frozenHeadUp), Vector3.Dot(offset, frozenHeadForward));
                    hit = new Hit(point, normal, direction, fraction, location, Player3DAnatomicalPart.Head,
                        frozenHeadToLocal.MultiplyPoint3x4(point), frozenHeadToLocal.MultiplyVector(direction).normalized);
                    first = fraction; found = true;
                }
            }
            return found;
        }

        internal static bool IntersectsSegment(Bounds bounds, Vector3 from, Vector3 delta, float radius)
            => new SegmentQuery(from, delta, radius).Intersects(bounds);

        internal readonly struct SegmentQuery
        {
            private readonly double x, y, z, inverseX, inverseY, inverseZ, reach;

            internal SegmentQuery(Vector3 from, Vector3 delta, float radius)
            {
                x = from.x; y = from.y; z = from.z;
                inverseX = delta.x == 0f ? 0d : 1d / delta.x;
                inverseY = delta.y == 0f ? 0d : 1d / delta.y;
                inverseZ = delta.z == 0f ? 0d : 1d / delta.z;
                reach = Math.Sqrt((double)radius * radius + HeadTriangle.ContactSkinSquared);
            }

            // The broad phase must include the same contact skin as the exact
            // triangle query, including a zero-radius crosshair ray.
            internal bool Intersects(Bounds bounds, float maximumFraction = 1f) => TryIntersect(bounds, maximumFraction, out _);

            internal bool TryIntersect(Bounds bounds, float maximumFraction, out float entry)
            {
                entry = 0f;
                Vector3 min = bounds.min, max = bounds.max;
                double near = 0d, far = Math.Min(1d, maximumFraction);
                if (far < 0d || !Axis(x, inverseX, min.x - reach, max.x + reach, ref near, ref far) ||
                    !Axis(y, inverseY, min.y - reach, max.y + reach, ref near, ref far) ||
                    !Axis(z, inverseZ, min.z - reach, max.z + reach, ref near, ref far)) return false;
                entry = (float)near;
                return true;
            }

            private static bool Axis(double origin, double inverse, double low, double high, ref double near, ref double far)
            {
                if (inverse == 0d) return origin >= low && origin <= high;
                double a = (low - origin) * inverse, b = (high - origin) * inverse;
                near = Math.Max(near, Math.Min(a, b)); far = Math.Min(far, Math.Max(a, b));
                return near <= far;
            }
        }

        internal sealed class HeadSurface
        {
            private const int QueryLeafTriangleCount = 24;
            private readonly SkinnedMeshRenderer source;
            private readonly CombatBrainTissue brain;
            private readonly Vector3[] vertices;
            private readonly List<Vector3> deformed;
            private readonly List<Vector3> baseVertices;
            private readonly SurfaceBlendShape[] blendShapes;
            private Mesh blendShapeMesh;
            private readonly BoneWeight[] weights;
            private readonly Matrix4x4[] bind, posedBones;
            private readonly Transform[] bones;
            private int[] indices;
            private readonly int[] usedBones;
            private readonly int[] rigidBones;
            private readonly Bounds[] influenceBounds;
            private readonly Vector3[] influenceMinimum, influenceMaximum;
            private readonly bool[] measuredBones;
            private readonly float minimumWeight, maximumWeight;
            private readonly bool mutableTopology;
            private bool geometryReady;
            private bool queryBoundsDirty = true, queryWorldBoundsReady, queryPoseBuilt;
            private int queryPoseVersion;
            private readonly int[] vertexQueryVersions;
            private Bounds[] queryBounds, worldQueryBounds;
            private bool[] queryCandidates;
            private HeadTriangle[] fullTriangles, queryTriangles;
            private uint? capturedGeometryVersion;
            private int? capturedTopologyVersion;
            private Mesh capturedGeometryMesh;
            internal readonly Vector3[] Points;
            internal HeadTriangle[] Triangles;
            internal int QueryTriangleCount { get; private set; }
            internal Bounds Bounds;
            internal bool Active;

            internal HeadSurface(SkinnedMeshRenderer source, bool mutable = false, bool captureBlendShapes = false,
                bool mutableTopology = false, uint? initialGeometryVersion = null, int? initialTopologyVersion = null)
            {
                this.source = source;
                this.mutableTopology = mutableTopology;
                Mesh mesh = source.sharedMesh;
                brain = source.GetComponent<CombatBrainTissue>();
                vertices = mesh.vertices; weights = mesh.boneWeights; bind = mesh.bindposes;
                int shapeCount = captureBlendShapes ? mesh.blendShapeCount : 0;
                if (mutable || shapeCount > 0 || brain != null)
                    deformed = new List<Vector3>(vertices.Length);
                if (shapeCount > 0)
                {
                    baseVertices = new List<Vector3>(vertices.Length);
                    blendShapes = new SurfaceBlendShape[shapeCount];
                    for (int shape = 0; shape < shapeCount; shape++)
                        blendShapes[shape] = new SurfaceBlendShape(mesh, shape, vertices.Length);
                    blendShapeMesh = mesh;
                }
                bones = source.bones; posedBones = new Matrix4x4[bones.Length]; indices = mesh.triangles;
                Points = new Vector3[vertices.Length];
                fullTriangles = Triangles = new HeadTriangle[indices.Length / 3];
                queryTriangles = new HeadTriangle[indices.Length / 3];
                vertexQueryVersions = new int[vertices.Length];
                int leaves = (indices.Length / 3 + QueryLeafTriangleCount - 1) / QueryLeafTriangleCount;
                queryBounds = new Bounds[leaves]; queryCandidates = new bool[leaves];
                worldQueryBounds = new Bounds[leaves];
                if (weights.Length != vertices.Length || bind.Length != bones.Length)
                    throw new InvalidOperationException("Combat head surface requires complete skin weights: " + source.name);
                influenceBounds = new Bounds[bones.Length]; measuredBones = new bool[bones.Length];
                influenceMinimum = new Vector3[bones.Length]; influenceMaximum = new Vector3[bones.Length];
                capturedTopologyVersion = initialTopologyVersion;
                rigidBones = new int[weights.Length];
                minimumWeight = float.PositiveInfinity;
                for (int vertex = 0; vertex < weights.Length; vertex++)
                {
                    BoneWeight weight = weights[vertex];
                    rigidBones[vertex] = weight.weight0 == 1f && weight.weight1 == 0f && weight.weight2 == 0f && weight.weight3 == 0f ? weight.boneIndex0 :
                        weight.weight1 == 1f && weight.weight0 == 0f && weight.weight2 == 0f && weight.weight3 == 0f ? weight.boneIndex1 :
                        weight.weight2 == 1f && weight.weight0 == 0f && weight.weight1 == 0f && weight.weight3 == 0f ? weight.boneIndex2 :
                        weight.weight3 == 1f && weight.weight0 == 0f && weight.weight1 == 0f && weight.weight2 == 0f ? weight.boneIndex3 : -1;
                    float total = weight.weight0 + weight.weight1 + weight.weight2 + weight.weight3;
                    minimumWeight = Mathf.Min(minimumWeight, total); maximumWeight = Mathf.Max(maximumWeight, total);
                }
                if (initialGeometryVersion.HasValue && deformed != null)
                {
                    // Prepared body surfaces already have this exact mesh/version.
                    // Keep that snapshot so their first activation does not repeat
                    // native vertex/index copies and the full influence-bound scan.
                    baseVertices?.AddRange(vertices);
                    deformed.AddRange(vertices);
                    if (blendShapes != null)
                        for (int shape = 0; shape < blendShapes.Length; shape++)
                        {
                            blendShapes[shape].Weight = source.GetBlendShapeWeight(shape);
                            blendShapes[shape].Apply(deformed);
                        }
                    capturedGeometryMesh = mesh; capturedGeometryVersion = initialGeometryVersion;
                }
                RefreshInfluenceBounds();
                var used = new List<int>();
                for (int i = 0; i < measuredBones.Length; i++) if (measuredBones[i]) used.Add(i);
                usedBones = used.ToArray();
            }

            internal void Capture()
            {
                CapturePose();
                EnsureGeometry();
            }

            internal void CapturePose(Dictionary<Transform, Matrix4x4> worldBones = null, uint? geometryVersion = null,
                bool includeDisabled = false, int? topologyVersion = null)
            {
                geometryReady = false;
                queryPoseBuilt = false;
                queryWorldBoundsReady = false;
                unchecked { queryPoseVersion++; }
                if (queryPoseVersion == 0) { Array.Clear(vertexQueryVersions, 0, vertexQueryVersions.Length); queryPoseVersion = 1; }
                Active = source != null && source.gameObject.activeInHierarchy &&
                    (includeDisabled || source.enabled || Player3DHeadVisibility.IsTemporarilyHidden(source));
                if (!Active) return;
                // Freeze mutable vertices now: a later contact must not see the next cloth/tissue pose.
                CaptureVertices(geometryVersion, topologyVersion);
                bool measured = false;
                foreach (int i in usedBones)
                {
                    Matrix4x4 world;
                    if (worldBones == null) world = bones[i].localToWorldMatrix;
                    else if (!worldBones.TryGetValue(bones[i], out world))
                    { world = bones[i].localToWorldMatrix; worldBones.Add(bones[i], world); }
                    Matrix4x4 matrix = posedBones[i] = world * bind[i];
                    Bounds local = influenceBounds[i]; Vector3 extent = local.extents;
                    Vector3 transformedExtent = new Vector3(
                        Mathf.Abs(matrix.m00) * extent.x + Mathf.Abs(matrix.m01) * extent.y + Mathf.Abs(matrix.m02) * extent.z,
                        Mathf.Abs(matrix.m10) * extent.x + Mathf.Abs(matrix.m11) * extent.y + Mathf.Abs(matrix.m12) * extent.z,
                        Mathf.Abs(matrix.m20) * extent.x + Mathf.Abs(matrix.m21) * extent.y + Mathf.Abs(matrix.m22) * extent.z);
                    var candidate = new Bounds(matrix.MultiplyPoint3x4(local.center), transformedExtent * 2f);
                    if (!measured) { Bounds = candidate; measured = true; }
                    else { Bounds.Encapsulate(candidate.min); Bounds.Encapsulate(candidate.max); }
                }
                if (!measured) Bounds = new Bounds(Vector3.zero, Vector3.zero);
                else
                {
                    // Skin weights form a convex combination. Retain the small normalization
                    // error too, so even a zero-radius projectile has conservative bounds.
                    Vector3 min = Bounds.min, max = Bounds.max;
                    Bounds.SetMinMax(Vector3.Min(min * minimumWeight, min * maximumWeight),
                        Vector3.Max(max * minimumWeight, max * maximumWeight));
                }
            }

            private void CaptureVertices(uint? geometryVersion, int? topologyVersion)
            {
                if (deformed == null) return;
                if (!geometryVersion.HasValue && brain != null && brain.OwnsMesh(source.sharedMesh))
                    geometryVersion = brain.GeometryVersion;
                bool meshChanged = capturedGeometryMesh != source.sharedMesh;
                bool geometryChanged = !geometryVersion.HasValue || geometryVersion != capturedGeometryVersion ||
                    meshChanged;
                // Cloth can update vertices without removing any authored faces.
                // A null topology revision retains the older geometry-change contract.
                bool topologyChanged = mutableTopology && (meshChanged ||
                    (!topologyVersion.HasValue && geometryChanged) ||
                    (topologyVersion.HasValue && topologyVersion != capturedTopologyVersion));
                bool shapesChanged = false;
                if (blendShapes != null)
                {
                    if (blendShapeMesh != source.sharedMesh)
                    {
                        if (source.sharedMesh.blendShapeCount != blendShapes.Length)
                            throw new InvalidOperationException("Combat body source changed its blend shape topology: " + source.name);
                        for (int shape = 0; shape < blendShapes.Length; shape++)
                            blendShapes[shape] = new SurfaceBlendShape(source.sharedMesh, shape, vertices.Length);
                        blendShapeMesh = source.sharedMesh;
                    }
                    for (int shape = 0; shape < blendShapes.Length; shape++)
                    {
                        float weight = source.GetBlendShapeWeight(shape);
                        if (blendShapes[shape].Weight == weight) continue;
                        blendShapes[shape].Weight = weight; shapesChanged = true;
                    }
                }
                if (!geometryChanged && !shapesChanged && !topologyChanged) return;
                queryBoundsDirty = true;
                queryWorldBoundsReady = false;
                if (geometryChanged)
                {
                    source.sharedMesh.GetVertices(baseVertices ?? deformed);
                    capturedGeometryVersion = geometryVersion; capturedGeometryMesh = source.sharedMesh;
                }
                if (topologyChanged)
                {
                    indices = source.sharedMesh.triangles;
                    int count = indices.Length / 3;
                    if (fullTriangles.Length != count) fullTriangles = new HeadTriangle[count];
                    if (queryTriangles.Length < count) queryTriangles = new HeadTriangle[count];
                    int leaves = (count + QueryLeafTriangleCount - 1) / QueryLeafTriangleCount;
                    if (queryBounds.Length < leaves)
                    { queryBounds = new Bounds[leaves]; worldQueryBounds = new Bounds[leaves]; queryCandidates = new bool[leaves]; }
                    capturedTopologyVersion = topologyVersion;
                }
                if (!geometryChanged && !shapesChanged) return;
                if (blendShapes != null)
                {
                    deformed.Clear(); deformed.AddRange(baseVertices);
                    foreach (SurfaceBlendShape shape in blendShapes) shape.Apply(deformed);
                }
                RefreshInfluenceBounds();
            }

            private sealed class SurfaceBlendShape
            {
                private readonly float[] frames;
                private readonly Vector3[][] deltas;
                internal float Weight = float.NaN;

                internal SurfaceBlendShape(Mesh mesh, int shape, int vertexCount)
                {
                    int count = mesh.GetBlendShapeFrameCount(shape);
                    frames = new float[count]; deltas = new Vector3[count][];
                    for (int frame = 0; frame < count; frame++)
                    {
                        frames[frame] = mesh.GetBlendShapeFrameWeight(shape, frame);
                        deltas[frame] = new Vector3[vertexCount];
                        mesh.GetBlendShapeFrameVertices(shape, frame, deltas[frame], null, null);
                    }
                }

                internal void Apply(List<Vector3> output)
                {
                    if (Weight == 0f || frames.Length == 0) return;
                    // Authored grip frames interpolate from the zero-weight base, then
                    // between consecutive frames; weights beyond the ends extrapolate.
                    int high = 0;
                    while (high < frames.Length - 1 && Weight > frames[high]) high++;
                    int low = high - 1;
                    float lowerWeight = low < 0 ? 0f : frames[low];
                    float fraction = (Weight - lowerWeight) / (frames[high] - lowerWeight);
                    Vector3[] lower = low < 0 ? null : deltas[low], upper = deltas[high];
                    for (int vertex = 0; vertex < output.Count; vertex++)
                        output[vertex] += Vector3.LerpUnclamped(lower == null ? Vector3.zero : lower[vertex], upper[vertex], fraction);
                }
            }

            private void RefreshInfluenceBounds()
            {
                if (usedBones != null) foreach (int i in usedBones) measuredBones[i] = false;
                for (int i = 0; i < vertices.Length; i++)
                {
                    BoneWeight weight = weights[i];
                    Vector3 vertex = deformed != null && deformed.Count > 0 ? deformed[i] : vertices[i];
                    IncludeInfluence(weight.boneIndex0, weight.weight0, vertex);
                    IncludeInfluence(weight.boneIndex1, weight.weight1, vertex);
                    IncludeInfluence(weight.boneIndex2, weight.weight2, vertex);
                    IncludeInfluence(weight.boneIndex3, weight.weight3, vertex);
                }
                if (usedBones == null)
                {
                    for (int bone = 0; bone < measuredBones.Length; bone++)
                        if (measuredBones[bone]) influenceBounds[bone].SetMinMax(influenceMinimum[bone], influenceMaximum[bone]);
                }
                else foreach (int bone in usedBones) influenceBounds[bone].SetMinMax(influenceMinimum[bone], influenceMaximum[bone]);
            }

            private void IncludeInfluence(int bone, float weight, Vector3 vertex)
            {
                if (weight <= 0f) return;
                if (!measuredBones[bone])
                { influenceMinimum[bone] = influenceMaximum[bone] = vertex; measuredBones[bone] = true; }
                else
                {
                    influenceMinimum[bone] = Vector3.Min(influenceMinimum[bone], vertex);
                    influenceMaximum[bone] = Vector3.Max(influenceMaximum[bone], vertex);
                }
            }

            internal void EnsureGeometry()
            {
                Triangles = fullTriangles;
                QueryTriangleCount = fullTriangles.Length;
                if (!Active || geometryReady) return;
                Vector3 minimum = default, maximum = default;
                for (int i = 0; i < vertices.Length; i++)
                {
                    Vector3 point = Points[i] = SkinPoint(i);
                    vertexQueryVersions[i] = queryPoseVersion;
                    if (i == 0) minimum = maximum = point;
                    else { minimum = Vector3.Min(minimum, point); maximum = Vector3.Max(maximum, point); }
                }
                Bounds.SetMinMax(minimum, maximum);
                for (int i = 0; i < Triangles.Length; i++)
                    Triangles[i] = new HeadTriangle(Points[indices[i * 3]], Points[indices[i * 3 + 1]], Points[indices[i * 3 + 2]]);
                geometryReady = true;
            }

            internal bool PrepareQuery(SegmentQuery query, float maximumFraction = 1f)
            {
                Triangles = queryTriangles; QueryTriangleCount = 0;
                if (!Active) return false;
                int leaves = (indices.Length / 3 + QueryLeafTriangleCount - 1) / QueryLeafTriangleCount;
                if (queryBoundsDirty)
                {
                    for (int leaf = 0; leaf < leaves; leaf++)
                    {
                        int start = leaf * QueryLeafTriangleCount * 3, end = Math.Min(indices.Length, start + QueryLeafTriangleCount * 3);
                        Vector3 minimum = default, maximum = default;
                        for (int index = start; index < end; index++)
                        {
                            Vector3 vertex = deformed != null ? deformed[indices[index]] : vertices[indices[index]];
                            if (index == start) minimum = maximum = vertex;
                            else { minimum = Vector3.Min(minimum, vertex); maximum = Vector3.Max(maximum, vertex); }
                        }
                        queryBounds[leaf].SetMinMax(minimum, maximum);
                    }
                    queryBoundsDirty = false;
                    queryWorldBoundsReady = false;
                }
                if (!queryWorldBoundsReady)
                {
                    for (int leaf = 0; leaf < leaves; leaf++)
                    {
                        Bounds local = queryBounds[leaf], world = default;
                        Vector3 extent = local.extents;
                        bool measured = false;
                        foreach (int bone in usedBones)
                        {
                            Matrix4x4 matrix = posedBones[bone];
                            Vector3 transformedExtent = new Vector3(
                                Mathf.Abs(matrix.m00) * extent.x + Mathf.Abs(matrix.m01) * extent.y + Mathf.Abs(matrix.m02) * extent.z,
                                Mathf.Abs(matrix.m10) * extent.x + Mathf.Abs(matrix.m11) * extent.y + Mathf.Abs(matrix.m12) * extent.z,
                                Mathf.Abs(matrix.m20) * extent.x + Mathf.Abs(matrix.m21) * extent.y + Mathf.Abs(matrix.m22) * extent.z);
                            var candidate = new Bounds(matrix.MultiplyPoint3x4(local.center), transformedExtent * 2f);
                            if (!measured) { world = candidate; measured = true; }
                            else { world.Encapsulate(candidate.min); world.Encapsulate(candidate.max); }
                        }
                        if (measured)
                        {
                            Vector3 min = world.min, max = world.max;
                            world.SetMinMax(Vector3.Min(min * minimumWeight, min * maximumWeight),
                                Vector3.Max(max * minimumWeight, max * maximumWeight));
                        }
                        else world = new Bounds(Vector3.zero, Vector3.zero);
                        worldQueryBounds[leaf] = world;
                    }
                    queryWorldBoundsReady = true;
                }
                bool found = false;
                for (int leaf = 0; leaf < leaves; leaf++)
                    found |= queryCandidates[leaf] = query.Intersects(worldQueryBounds[leaf], maximumFraction);
                if (!found) return false;
                bool built = !queryPoseBuilt && !geometryReady;
                for (int leaf = 0; leaf < leaves; leaf++)
                {
                    if (!queryCandidates[leaf]) continue;
                    int start = leaf * QueryLeafTriangleCount * 3, end = Math.Min(indices.Length, start + QueryLeafTriangleCount * 3);
                    for (int index = start; index < end; index += 3)
                    {
                        int a = indices[index], b = indices[index + 1], c = indices[index + 2];
                        EnsureQueryPoint(a); EnsureQueryPoint(b); EnsureQueryPoint(c);
                        queryTriangles[QueryTriangleCount++] = new HeadTriangle(Points[a], Points[b], Points[c]);
                    }
                }
                queryPoseBuilt = true;
                return built;
            }

            private void EnsureQueryPoint(int index)
            {
                if (vertexQueryVersions[index] == queryPoseVersion) return;
                Points[index] = SkinPoint(index);
                vertexQueryVersions[index] = queryPoseVersion;
            }

            private Vector3 SkinPoint(int index)
            {
                Vector3 vertex = deformed != null ? deformed[index] : vertices[index];
                int rigid = rigidBones[index];
                if (rigid >= 0) return posedBones[rigid].MultiplyPoint3x4(vertex);
                BoneWeight weight = weights[index];
                Vector3 point = weight.weight0 > 0f ? posedBones[weight.boneIndex0].MultiplyPoint3x4(vertex) * weight.weight0 : Vector3.zero;
                if (weight.weight1 > 0f) point += posedBones[weight.boneIndex1].MultiplyPoint3x4(vertex) * weight.weight1;
                if (weight.weight2 > 0f) point += posedBones[weight.boneIndex2].MultiplyPoint3x4(vertex) * weight.weight2;
                if (weight.weight3 > 0f) point += posedBones[weight.boneIndex3].MultiplyPoint3x4(vertex) * weight.weight3;
                return point;
            }
        }

        internal readonly struct HeadTriangle
        {
            internal const double ContactSkinSquared = .00000001d;
            internal readonly Vector3 A, B, C;
            internal readonly Bounds Bounds;
            internal HeadTriangle(Vector3 a, Vector3 b, Vector3 c)
            {
                A = a; B = b; C = c;
                Bounds = default;
                Bounds.SetMinMax(Vector3.Min(a, Vector3.Min(b, c)), Vector3.Max(a, Vector3.Max(b, c)));
            }

            // Closest point on the actual triangle, including its edges/vertices.
            // Degenerate authored triangles reduce to their finite line segments.
            internal Vector3 Closest(Vector3 point)
            {
                Vector3 ab = B - A, ac = C - A;
                if (Vector3.Cross(ab, ac).sqrMagnitude < .0000000000000001f)
                {
                    Vector3 p = ClosestOnSegment(A, B, point), q = ClosestOnSegment(B, C, point), r = ClosestOnSegment(C, A, point);
                    if ((q - point).sqrMagnitude < (p - point).sqrMagnitude) p = q;
                    return (r - point).sqrMagnitude < (p - point).sqrMagnitude ? r : p;
                }
                Vector3 ap = point - A;
                float d1 = Vector3.Dot(ab, ap), d2 = Vector3.Dot(ac, ap);
                if (d1 <= 0f && d2 <= 0f) return A;
                Vector3 bp = point - B;
                float d3 = Vector3.Dot(ab, bp), d4 = Vector3.Dot(ac, bp);
                if (d3 >= 0f && d4 <= d3) return B;
                float vc = d1 * d4 - d3 * d2;
                if (vc <= 0f && d1 >= 0f && d3 <= 0f) return A + ab * (d1 / (d1 - d3));
                Vector3 cp = point - C;
                float d5 = Vector3.Dot(ab, cp), d6 = Vector3.Dot(ac, cp);
                if (d6 >= 0f && d5 <= d6) return C;
                float vb = d5 * d2 - d1 * d6;
                if (vb <= 0f && d2 >= 0f && d6 <= 0f) return A + ac * (d2 / (d2 - d6));
                float va = d3 * d6 - d5 * d4;
                if (va <= 0f && d4 - d3 >= 0f && d5 - d6 >= 0f)
                    return B + (C - B) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
                float inverse = 1f / (va + vb + vc);
                return A + ab * (vb * inverse) + ac * (vc * inverse);
            }

            internal bool FirstContact(Vector3 from, Vector3 delta, float radius, out float fraction)
            {
                fraction = 0f;
                if (!Finite(from) || !Finite(delta) || !float.IsFinite(radius) || radius < 0f) return false;
                double threshold = (double)radius * radius + ContactSkinSquared;
                var origin = new ContactVector(from); var velocity = new ContactVector(delta);
                var a = new ContactVector(A); var b = new ContactVector(B); var c = new ContactVector(C);
                ContactVector ab = b - a, ac = c - a, normal = ContactVector.Cross(ab, ac);
                double normalSquared = normal.Squared;
                if (DistanceSquared(origin, a, b, c, normal, normalSquared) <= threshold) return true;
                if (velocity.Squared < .0000000001d) return false;
                double first = double.PositiveInfinity;
                // The rounded triangle is exactly its two offset faces, three
                // finite edge cylinders and three vertex spheres. No iterative
                // distance minimization is needed for a grazing or long aim ray.
                if (normalSquared >= .0000000000000001d)
                {
                    double speed = ContactVector.Dot(normal, velocity);
                    if (speed != 0d)
                    {
                        double start = ContactVector.Dot(normal, origin - a);
                        double offset = Math.Sqrt(threshold * normalSquared);
                        if (Math.Abs(start) > offset)
                        {
                            double entry = ((start > 0d ? offset : -offset) - start) / speed;
                            FaceContact(entry, origin, velocity, a, ab, ac, normal, normalSquared, ref first);
                            // Every rounded feature lies inside this slab. An
                            // accepted entry face is already the first contact.
                            if (!double.IsPositiveInfinity(first)) { fraction = (float)first; return true; }
                        }
                        FaceContact((offset - start) / speed, origin, velocity, a, ab, ac, normal, normalSquared, ref first);
                        FaceContact((-offset - start) / speed, origin, velocity, a, ab, ac, normal, normalSquared, ref first);
                    }
                }
                EdgeContact(origin, velocity, a, b, threshold, ref first);
                EdgeContact(origin, velocity, b, c, threshold, ref first);
                EdgeContact(origin, velocity, c, a, threshold, ref first);
                SphereContact(origin - a, velocity, threshold, ref first);
                SphereContact(origin - b, velocity, threshold, ref first);
                SphereContact(origin - c, velocity, threshold, ref first);
                if (double.IsPositiveInfinity(first))
                {
                    // Retain an inclusive endpoint if roundoff put an analytic
                    // root just beyond the finite sweep.
                    if (DistanceSquared(origin + velocity, a, b, c, normal, normalSquared) > threshold) return false;
                    first = 1d;
                }
                fraction = (float)first;
                return true;
            }

            private static void FaceContact(double time, ContactVector origin, ContactVector velocity, ContactVector a,
                ContactVector ab, ContactVector ac, ContactVector normal, double normalSquared, ref double first)
            {
                if (time < 0d || time > 1d || time >= first) return;
                ContactVector point = origin + velocity * time - a;
                point -= normal * (ContactVector.Dot(normal, point) / normalSquared);
                if (InsideFace(point, ab, ac, normalSquared)) first = time;
            }

            private static bool InsideFace(ContactVector point, ContactVector ab, ContactVector ac, double determinant)
            {
                double abac = ContactVector.Dot(ab, ac), apab = ContactVector.Dot(point, ab), apac = ContactVector.Dot(point, ac);
                double u = (ac.Squared * apab - abac * apac) / determinant;
                double v = (ab.Squared * apac - abac * apab) / determinant;
                return u >= -1e-12d && v >= -1e-12d && u + v <= 1d + 1e-12d;
            }

            private static void EdgeContact(ContactVector origin, ContactVector velocity, ContactVector start,
                ContactVector end, double threshold, ref double first)
            {
                ContactVector edge = end - start, relative = origin - start;
                double lengthSquared = edge.Squared;
                if (lengthSquared == 0d) return;
                double alongStart = ContactVector.Dot(relative, edge) / lengthSquared;
                double alongVelocity = ContactVector.Dot(velocity, edge) / lengthSquared;
                ContactVector radialStart = relative - edge * alongStart, radialVelocity = velocity - edge * alongVelocity;
                if (!EntryTime(radialStart, radialVelocity, threshold, out double time) || time >= first) return;
                double along = alongStart + alongVelocity * time;
                if (along >= 0d && along <= 1d) first = time;
            }

            private static void SphereContact(ContactVector relative, ContactVector velocity, double threshold, ref double first)
            {
                if (EntryTime(relative, velocity, threshold, out double time) && time < first) first = time;
            }

            private static bool EntryTime(ContactVector relative, ContactVector velocity, double threshold, out double time)
            {
                time = 0d;
                double speedSquared = velocity.Squared;
                if (speedSquared == 0d) return false;
                // Closest approach avoids the subtraction of two almost equal
                // b*b and 4*a*c terms when a 250 m ray just grazes an edge.
                double middle = -ContactVector.Dot(relative, velocity) / speedSquared;
                double gap = threshold - (relative + velocity * middle).Squared;
                if (gap < 0d) return false;
                time = middle - Math.Sqrt(gap / speedSquared);
                return time >= 0d && time <= 1d;
            }

            private static double DistanceSquared(ContactVector point, ContactVector a, ContactVector b, ContactVector c,
                ContactVector normal, double normalSquared)
            {
                ContactVector relative = point - a;
                if (normalSquared >= .0000000000000001d)
                {
                    double height = ContactVector.Dot(relative, normal);
                    ContactVector projected = relative - normal * (height / normalSquared);
                    if (InsideFace(projected, b - a, c - a, normalSquared)) return height * height / normalSquared;
                }
                return Math.Min(SegmentDistanceSquared(point, a, b),
                    Math.Min(SegmentDistanceSquared(point, b, c), SegmentDistanceSquared(point, c, a)));
            }

            private static double SegmentDistanceSquared(ContactVector point, ContactVector start, ContactVector end)
            {
                ContactVector edge = end - start, relative = point - start;
                double lengthSquared = edge.Squared;
                double along = lengthSquared == 0d ? 0d : Math.Max(0d, Math.Min(1d, ContactVector.Dot(relative, edge) / lengthSquared));
                return (relative - edge * along).Squared;
            }

            private readonly struct ContactVector
            {
                private readonly double x, y, z;
                internal ContactVector(Vector3 value) { x = value.x; y = value.y; z = value.z; }
                private ContactVector(double x, double y, double z) { this.x = x; this.y = y; this.z = z; }
                internal double Squared => x * x + y * y + z * z;
                internal static double Dot(ContactVector a, ContactVector b) => a.x * b.x + a.y * b.y + a.z * b.z;
                internal static ContactVector Cross(ContactVector a, ContactVector b) => new ContactVector(
                    a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x);
                public static ContactVector operator +(ContactVector a, ContactVector b) => new ContactVector(a.x + b.x, a.y + b.y, a.z + b.z);
                public static ContactVector operator -(ContactVector a, ContactVector b) => new ContactVector(a.x - b.x, a.y - b.y, a.z - b.z);
                public static ContactVector operator *(ContactVector a, double value) => new ContactVector(a.x * value, a.y * value, a.z * value);
            }
        }
    }
}
