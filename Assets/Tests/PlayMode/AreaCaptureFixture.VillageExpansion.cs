using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused road/junction ground fit, snow rebuild and route preservation.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageRoadSurface()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            VillageRoadSnowAudit snow = null;
            yield return Capture(SceneIds.AlpineVillage, () =>
            {
                root = Object.FindAnyObjectByType<AlpineVillageRoot>();
                return root != null && root.IsInitialized ? root : null;
            }, () =>
            {
                snow = RebuildVillageRoadSnow(root);
                return VillageRoadSurfaceShots(root);
            });
            // Retain the diagnostic frames even if the geometry contract fails.
            VerifyVillageRoadSurface(root);
            VerifyVillageRoadPathJoins(root, snow.Road);
            VerifyVillageJunctionSurfaces(root, snow.Approaches);
            Assert.That(snow.Cleared, Is.GreaterThan(0), "ClearPatch must synchronously rebuild positive-depth snow.");
            Assert.That(snow.DepthAfter, Is.LessThan(snow.DepthBefore * .1f));
            Assert.That(snow.Before.Probes, Is.GreaterThan(0));
            Assert.That(snow.After.Probes, Is.EqualTo(snow.Before.Probes));
            Assert.That(snow.Before.MaximumLift, Is.LessThan(.003f),
                "Fresh snow triangles cover a road or junction at " + snow.Before.Worst);
            Assert.That(snow.After.MaximumLift, Is.LessThan(.003f),
                "Rebuilt snow triangles cover a road or junction at " + snow.After.Worst);
        }

        private static Shot[] VillageRoadSurfaceShots(AlpineVillageRoot root)
        {
            var shots = new List<Shot>();
            AlpineVillageExpansionPlan expansion = root.Plan.Expansion;
            Add("road-00-bend-ground", new Vector2(-66f, 61f), .7f,
                new Vector2(-71f, 67f), .02f, 66f);
            Add("road-01-bend-overhead", new Vector2(-64f, 61f), 16f,
                new Vector2(-70f, 65f), 0f, 65f);
            Add("road-02-trade-yard-junction", new Vector2(-126f, -23f), 2f,
                new Vector2(-132f, -28f), .02f, 70f);
            Add("road-03-near-broken-lip", new Vector2(-124.8f, -52.7f), .9f,
                new Vector2(-130f, -53.8f), .02f, 70f);
            Vector2 farView = expansion.ToLocal(expansion.FarRoadPoint(-73f)) + Vector2.right * 3.5f;
            Add("road-04-opposite-broken-lip", farView, 1.1f,
                expansion.ToLocal(expansion.FarRoadEdge), .02f, 70f, true);
            Add("road-05-separated-shelves", new Vector2(-128.6f, -50f), EyeHeight,
                expansion.ToLocal(expansion.FarRoadEdge), .25f, 60f);
            int junctionIndex = 6;
            foreach (AlpineVillageJunctionPlan junction in expansion.Junctions)
            {
                var port = junction.Ports[0];
                foreach (AlpineVillageJunctionPort candidate in junction.Ports)
                    if (candidate.Kind != AlpineVillagePathKind.AbandonedRoad) { port = candidate; break; }
                Vector2 entrance = port.MouthCenter + port.Direction * 2.5f;
                Vector2 centreLocal = Local(junction.Center);
                Add($"road-{junctionIndex:00}-{junction.StableId}-walk", Local(entrance), EyeHeight,
                    centreLocal, .04f, 66f);
                Add($"road-{junctionIndex++:00}-{junction.StableId}-overhead",
                    Local(junction.Center + port.Direction), Mathf.Max(10f, junction.Bounds.size.magnitude),
                    centreLocal, 0f, 64f);
            }
            foreach (AlpineVillageJunctionPlan junction in expansion.Junctions)
            {
                if (junction.StableId != "forest-loop-road-junction") continue;
                AlpineVillageJunctionPort port = junction.Ports[0];
                foreach (AlpineVillageJunctionPort candidate in junction.Ports)
                    if (candidate.Kind != AlpineVillagePathKind.AbandonedRoad) { port = candidate; break; }
                Vector2 entrance = Local(port.MouthCenter + port.Direction * 2.5f);
                Add("road-10-loop-without-snow", entrance, EyeHeight, Local(junction.Center), .04f, 66f,
                    prepare: () => root.World.SnowTreading.GetComponent<MeshRenderer>().enabled = false);
                Add("road-11-loop-without-snap", entrance, EyeHeight, Local(junction.Center), .04f, 66f,
                    prepare: () =>
                    {
                        Camera camera = Camera.main;
                        Assert.That(camera, Is.Not.Null);
                        if (camera.GetComponent<BarPromenade.Rendering.Ps1VertexJitterExclusion>() == null)
                            camera.gameObject.AddComponent<BarPromenade.Rendering.Ps1VertexJitterExclusion>();
                    });
                break;
            }
            return shots.ToArray();

            Vector2 Local(Vector2 point) => expansion.ToLocal(new Vector3(point.x, 0f, point.y));

            void Add(string name, Vector2 from, float eyeLift, Vector2 toward,
                float targetLift, float fov, bool farSide = false, System.Action prepare = null)
            {
                Vector3 foot = Ground(from);
                Vector3 target = Ground(toward);
                // The opposite shelf is scenery; the hero remains behind the
                // barrier while the diagnostic camera inspects its asphalt edge.
                Vector3 playerFoot = Ground(farSide ? new Vector2(-128.6f, -50f) : from);
                bool moved = false;
                int frames = 0;
                shots.Add(Shot.At(name, foot + Vector3.up * eyeLift,
                    target + Vector3.up * targetLift, fov, 0, () =>
                    {
                        if (!moved)
                        {
                            prepare?.Invoke();
                            root.SetWarmthGrade(0f);
                            root.Player.Motor.Teleport(playerFoot + Vector3.up * PlayerFactory.GroundedRootOffset);
                            moved = true;
                        }
                        bool ready = ++frames > 12 && root.StormWave <= GustTroughWave;
                        if (ready)
                            foreach (Renderer renderer in root.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                                renderer.enabled = false;
                        return ready;
                    }));
            }

            Vector3 Ground(Vector2 local)
            {
                Vector3 point = expansion.ToWorld(local);
                point.y = AlpineVillageTerrainSampler.SampleMeshHeight(root.Plan, new Vector2(point.x, point.z));
                return point;
            }
        }

        private static VillageRoadSnowAudit RebuildVillageRoadSnow(AlpineVillageRoot root)
        {
            var road = new VillageRoadMeshProbe(root.World.TerrainRoot.GetComponent<MeshFilter>());
            var approaches = new List<VillageRoadMeshProbe>();
            foreach (MeshFilter path in root.World.Root.GetComponentsInChildren<MeshFilter>())
                if (path.name.StartsWith("Visible Path - ", System.StringComparison.Ordinal))
                    approaches.Add(new VillageRoadMeshProbe(path, 0));
            AlpineVillageSnowTreading snow = root.World.SnowTreading;
            MeshFilter filter = snow.GetComponent<MeshFilter>();
            var before = MeasureVillageRoadSnow(filter, road, approaches, root.Plan.Expansion.Junctions);
            Vector3 target = root.Plan.Expansion.ToWorld(new Vector2(-70f, 65f));
            Vector3 patch = default;
            float nearest = float.PositiveInfinity;
            foreach (Vector3 local in filter.sharedMesh.vertices)
            {
                Vector3 point = filter.transform.TransformPoint(local);
                float distance = Vector3.ProjectOnPlane(point - target, Vector3.up).sqrMagnitude;
                if (distance >= nearest || distance > 144f || snow.SampleVisibleDepth(point) < .15f) continue;
                nearest = distance;
                patch = point;
            }
            bool found = !float.IsInfinity(nearest);
            float depth = found ? snow.SampleVisibleDepth(patch) : 0f;
            int cleared = found ? snow.ClearPatch(patch, root.Plan.SlopeRight, new Vector2(.45f, .45f), 1f) : 0;
            var after = MeasureVillageRoadSnow(filter, road, approaches, root.Plan.Expansion.Junctions);
            Debug.Log($"Road/junction snow triangle maximum lift before/after ClearPatch: " +
                $"{before.MaximumLift:F6}/{after.MaximumLift:F6} m; worst {before.Worst:F3}/{after.Worst:F3}; " +
                $"road probes {before.AsphaltProbes}/{after.AsphaltProbes}, " +
                $"junction/approach probes {before.JunctionAndApproachProbes}/{after.JunctionAndApproachProbes}.");
            return new VillageRoadSnowAudit { Road = road, Approaches = approaches, Before = before, After = after,
                Cleared = cleared, DepthBefore = depth, DepthAfter = found ? snow.SampleVisibleDepth(patch) : 0f };
        }

        private static VillageRoadSnowMeasurement MeasureVillageRoadSnow(MeshFilter snow, VillageRoadMeshProbe road,
            IReadOnlyList<VillageRoadMeshProbe> approaches, IReadOnlyList<AlpineVillageJunctionPlan> junctions)
        {
            Vector3[] vertices = snow.sharedMesh.vertices;
            int[] triangles = snow.sharedMesh.triangles;
            for (int index = 0; index < vertices.Length; index++)
                vertices[index] = snow.transform.TransformPoint(vertices[index]);
            var result = new VillageRoadSnowMeasurement { MaximumLift = float.NegativeInfinity };
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]], c = vertices[triangles[index + 2]];
                Probe(a); Probe(b); Probe(c);
                Probe((a + b) * .5f); Probe((b + c) * .5f); Probe((c + a) * .5f);
                Probe((a + b + c) / 3f);
            }
            return result;

            void Probe(Vector3 point)
            {
                if (!road.TryHeight(point, out float ground, out int material))
                {
                    if (!NearVillageJunction(point, junctions)) return;
                    bool found = false;
                    foreach (VillageRoadMeshProbe approach in approaches)
                        if (approach.TryHeight(point, out ground, out material)) { found = true; break; }
                    if (!found) return;
                }
                result.Probes++;
                if (material == AlpineVillageWorldBuilder.TerrainAsphaltMaterialIndex) result.AsphaltProbes++;
                else result.JunctionAndApproachProbes++;
                float lift = point.y - ground;
                if (lift <= result.MaximumLift) return;
                result.MaximumLift = lift;
                result.Worst = point;
            }
        }

        private static void VerifyVillageRoadPathJoins(AlpineVillageRoot root, VillageRoadMeshProbe road)
        {
            int boundaryVertices = 0, overlappingFaces = 0;
            float maximumBoundaryLift = 0f;
            Vector3 worst = default;
            float maximumBurial = 0f;
            Vector3 worstBurial = default;
            foreach (MeshFilter path in root.World.Root.GetComponentsInChildren<MeshFilter>())
            {
                if (!path.name.StartsWith("Visible Path - ", System.StringComparison.Ordinal)) continue;
                Vector3[] vertices = path.sharedMesh.vertices;
                int[] triangles = path.sharedMesh.triangles;
                var used = new bool[vertices.Length];
                foreach (int vertex in triangles) used[vertex] = true;
                for (int index = 0; index < vertices.Length; index++)
                {
                    Vector3 point = vertices[index] = path.transform.TransformPoint(vertices[index]);
                    if (!used[index]) continue;
                    if (!road.Near(point, .012f)) continue;
                    float gap = Mathf.Abs(point.y - AlpineVillageTerrainSampler.SampleMeshHeight(
                        root.Plan, new Vector2(point.x, point.z)));
                    boundaryVertices++;
                    if (gap <= maximumBoundaryLift) continue;
                    maximumBoundaryLift = gap;
                    worst = point;
                }
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]], c = vertices[triangles[index + 2]];
                    if (Vector3.Cross(b - a, c - a).sqrMagnitude < .00000001f) continue;
                    Vector3 centre = (a + b + c) / 3f;
                    if (road.Interior(centre) || road.Interior(Vector3.Lerp(a, centre, .1f)) ||
                        road.Interior(Vector3.Lerp(b, centre, .1f)) || road.Interior(Vector3.Lerp(c, centre, .1f)))
                        overlappingFaces++;
                    ProbeApproach(centre);
                    ProbeApproach((a + b) * .5f); ProbeApproach((b + c) * .5f); ProbeApproach((c + a) * .5f);
                }
            }
            Debug.Log($"Path/road-and-junction joins: {boundaryVertices} boundary vertices; maximum ground gap " +
                $"{maximumBoundaryLift:F6} m at {worst:F3}; overlapping faces {overlappingFaces}; " +
                $"maximum approach burial {maximumBurial:F6} m at {worstBurial:F3}.");
            Assert.That(boundaryVertices, Is.GreaterThan(0), "No actual path/junction boundary was checked.");
            Assert.That(overlappingFaces, Is.Zero, "A raised path still extends over a road or junction.");
            Assert.That(maximumBoundaryLift, Is.LessThan(.004f),
                "A path boundary remains above or below the actual junction ground at " + worst);
            Assert.That(maximumBurial, Is.LessThan(.003f),
                "Terrain pokes through an incoming path at " + worstBurial);

            void ProbeApproach(Vector3 point)
            {
                if (!NearVillageJunction(point, root.Plan.Expansion.Junctions)) return;
                float burial = AlpineVillageTerrainSampler.SampleMeshHeight(root.Plan, new Vector2(point.x, point.z)) - point.y;
                if (burial <= maximumBurial) return;
                maximumBurial = burial;
                worstBurial = point;
            }
        }

        private static bool NearVillageJunction(Vector3 point, IReadOnlyList<AlpineVillageJunctionPlan> junctions)
        {
            foreach (AlpineVillageJunctionPlan junction in junctions)
            {
                Rect bounds = junction.Bounds;
                if (point.x >= bounds.xMin - 2f && point.x <= bounds.xMax + 2f &&
                    point.z >= bounds.yMin - 2f && point.z <= bounds.yMax + 2f) return true;
            }
            return false;
        }

        private sealed class VillageRoadSnowAudit
        {
            internal VillageRoadMeshProbe Road;
            internal IReadOnlyList<VillageRoadMeshProbe> Approaches;
            internal VillageRoadSnowMeasurement Before, After;
            internal int Cleared;
            internal float DepthBefore, DepthAfter;
        }

        private sealed class VillageRoadSnowMeasurement
        {
            internal int Probes;
            internal int AsphaltProbes, JunctionAndApproachProbes;
            internal float MaximumLift;
            internal Vector3 Worst;
        }

        // Index the actual painted terrain and incoming path triangles, not
        // a parallel analytic mask: rendering and snow rebuilds own the result.
        private sealed class VillageRoadMeshProbe
        {
            private const float Cell = 2f;
            private readonly Vector3[] vertices;
            private readonly Vector3[] normals;
            private readonly int[] triangles;
            private readonly int[] materials;
            private readonly Dictionary<Vector2Int, List<int>> cells = new Dictionary<Vector2Int, List<int>>();

            internal VillageRoadMeshProbe(MeshFilter terrain, params int[] slots)
            {
                vertices = terrain.sharedMesh.vertices;
                normals = terrain.sharedMesh.normals;
                if (slots.Length == 0) slots = new[] { AlpineVillageWorldBuilder.TerrainAsphaltMaterialIndex,
                    AlpineVillageWorldBuilder.TerrainJunctionMaterialIndex };
                var indices = new List<int>();
                var sourceMaterials = new List<int>();
                foreach (int slot in slots)
                {
                    int[] surface = terrain.sharedMesh.GetTriangles(slot);
                    indices.AddRange(surface);
                    for (int index = 0; index < surface.Length; index += 3) sourceMaterials.Add(slot);
                }
                triangles = indices.ToArray();
                materials = sourceMaterials.ToArray();
                for (int index = 0; index < vertices.Length; index++)
                {
                    vertices[index] = terrain.transform.TransformPoint(vertices[index]);
                    normals[index] = terrain.transform.TransformDirection(normals[index]);
                }
                for (int index = 0; index < triangles.Length; index += 3)
                {
                    Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]], c = vertices[triangles[index + 2]];
                    int minX = Mathf.FloorToInt(Mathf.Min(a.x, b.x, c.x) / Cell), maxX = Mathf.FloorToInt(Mathf.Max(a.x, b.x, c.x) / Cell);
                    int minZ = Mathf.FloorToInt(Mathf.Min(a.z, b.z, c.z) / Cell), maxZ = Mathf.FloorToInt(Mathf.Max(a.z, b.z, c.z) / Cell);
                    for (int x = minX; x <= maxX; x++)
                    for (int z = minZ; z <= maxZ; z++)
                    {
                        var key = new Vector2Int(x, z);
                        if (!cells.TryGetValue(key, out List<int> entries)) cells.Add(key, entries = new List<int>());
                        entries.Add(index);
                    }
                }
            }

            internal bool TryHeight(Vector3 point, out float height) => TryHeight(point, out height, out _);

            internal bool TryHeight(Vector3 point, out float height, out int material)
                => TrySample(point, out height, out material, out _);

            internal bool TryNormal(Vector3 point, out Vector3 normal)
                => TrySample(point, out _, out _, out normal);

            private bool TrySample(Vector3 point, out float height, out int material, out Vector3 normal)
            {
                height = 0f;
                material = -1;
                normal = Vector3.up;
                if (!cells.TryGetValue(new Vector2Int(Mathf.FloorToInt(point.x / Cell),
                    Mathf.FloorToInt(point.z / Cell)), out List<int> entries)) return false;
                foreach (int index in entries)
                {
                    Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]], c = vertices[triangles[index + 2]];
                    float area = Cross(a, b, c);
                    if (Mathf.Abs(area) < .0000001f) continue;
                    float first = Cross(point, b, c) / area, second = Cross(a, point, c) / area;
                    if (first < -.00001f || second < -.00001f || first + second > 1.00001f) continue;
                    height = first * a.y + second * b.y + (1f - first - second) * c.y;
                    material = materials[index / 3];
                    normal = (first * normals[triangles[index]] + second * normals[triangles[index + 1]] +
                        (1f - first - second) * normals[triangles[index + 2]]).normalized;
                    return true;
                }
                return false;
            }

            internal bool Near(Vector3 point, float reach) => TryHeight(point, out _) ||
                TryHeight(point + Vector3.right * reach, out _) || TryHeight(point + Vector3.left * reach, out _) ||
                TryHeight(point + Vector3.forward * reach, out _) || TryHeight(point + Vector3.back * reach, out _);

            internal bool Interior(Vector3 point) => TryHeight(point, out _) &&
                TryHeight(point + Vector3.right * .003f, out _) && TryHeight(point + Vector3.left * .003f, out _) &&
                TryHeight(point + Vector3.forward * .003f, out _) && TryHeight(point + Vector3.back * .003f, out _);

            private static float Cross(Vector3 a, Vector3 b, Vector3 c) =>
                (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
        }

        private static void VerifyVillageJunctionSurfaces(AlpineVillageRoot root, IReadOnlyList<VillageRoadMeshProbe> approaches)
        {
            VerifyVillageJunctionAtlas(root.Plan);
            MeshFilter terrain = root.World.TerrainRoot.GetComponent<MeshFilter>();
            MeshCollider ground = root.World.TerrainRoot.GetComponent<MeshCollider>();
            var asphalt = new VillageRoadMeshProbe(terrain, AlpineVillageWorldBuilder.TerrainAsphaltMaterialIndex);
            var coating = new VillageRoadMeshProbe(terrain, AlpineVillageWorldBuilder.TerrainJunctionMaterialIndex);
            var soil = new VillageRoadMeshProbe(terrain, AlpineVillageWorldBuilder.TerrainSoilMaterialIndex);
            float maximumGap = 0f;
            foreach (AlpineVillageJunctionPlan junction in root.Plan.Expansion.Junctions)
            {
                int asphaltPoints = 0, soilPoints = 0, blendPoints = 0;
                foreach (IReadOnlyList<Vector2> triangle in junction.OuterTriangles)
                {
                    Vector2 centre = (triangle[0] + triangle[1] + triangle[2]) / 3f;
                    Check(centre, "inner surface");
                    foreach (Vector2 corner in triangle) Check(Vector2.Lerp(corner, centre, .2f), "inner surface");
                }
                foreach (AlpineVillageJunctionPort port in junction.Ports)
                {
                    Vector2 right = new Vector2(port.Direction.y, -port.Direction.x);
                    foreach (float side in new[] { -.8f, 0f, .8f })
                    {
                        Vector2 point = port.MouthCenter - port.Direction * .12f +
                            right * (port.HalfWidth * side);
                        Check(point, "route mouth " + port.PathStableId);
                        float weight = junction.SampleAsphaltWeight(point);
                        if (port.Kind == AlpineVillagePathKind.AbandonedRoad)
                            Assert.That(weight, Is.GreaterThanOrEqualTo(.95f), port.PathStableId + " lost its asphalt mouth.");
                        else
                        {
                            Assert.That(weight, Is.LessThanOrEqualTo(.05f), port.PathStableId + " lost its soil mouth.");
                            Vector2 rim = port.MouthCenter + port.Direction * .002f + right * (port.HalfWidth * side);
                            var rimWorld = new Vector3(rim.x, 0f, rim.y);
                            Assert.That(soil.TryNormal(rimWorld, out Vector3 floorNormal), Is.True);
                            bool foundNormal = false;
                            foreach (VillageRoadMeshProbe approach in approaches)
                                if (approach.TryNormal(rimWorld, out Vector3 pathNormal))
                                {
                                    Assert.That(Vector3.Angle(floorNormal, pathNormal), Is.LessThan(2f),
                                        port.PathStableId + " has a lighting seam at its ground-level mouth.");
                                    foundNormal = true;
                                    break;
                                }
                            Assert.That(foundNormal, Is.True, port.PathStableId + " lost its path mouth.");
                        }
                        foreach (float distance in new[] { .05f, .15f, .3f, .5f })
                        {
                            Vector2 outside = port.MouthCenter + port.Direction * distance + right * (port.HalfWidth * side);
                            var world = new Vector3(outside.x, 0f, outside.y);
                            bool covered = asphalt.TryHeight(world, out _) || coating.TryHeight(world, out _);
                            foreach (VillageRoadMeshProbe approach in approaches) covered |= approach.TryHeight(world, out _);
                            Assert.That(covered, Is.True, junction.StableId + " has a gap outside " + port.PathStableId + " at " + outside);
                        }
                    }
                }
                if (junction.StableId == "trade-yard-junction")
                    foreach (Vector2 local in new[] { new Vector2(-133f, -31.5f),
                        new Vector2(-133.5f, -32f), new Vector2(-133f, -33f) })
                    {
                        Vector3 point = root.Plan.Expansion.ToWorld(local);
                        Vector2 xz = new Vector2(point.x, point.z);
                        Check(xz, "continuous warehouse apron");
                        Assert.That(junction.SampleAsphaltWeight(xz), Is.GreaterThanOrEqualTo(.95f),
                            "The warehouse apron lost its asphalt appearance.");
                    }
                // A material slot cannot prove a visual blend: sample the pure
                // mask across the interior, including the narrow transitions.
                for (float x = junction.Bounds.xMin + .125f; x < junction.Bounds.xMax; x += .25f)
                for (float z = junction.Bounds.yMin + .125f; z < junction.Bounds.yMax; z += .25f)
                {
                    var point = new Vector2(x, z);
                    if (!junction.Contains(point)) continue;
                    float weight = junction.SampleAsphaltWeight(point);
                    Assert.That(weight, Is.InRange(0f, 1f), junction.StableId + " has an invalid blend weight.");
                    if (weight > .05f && weight < .95f) blendPoints++;
                }
                Assert.That(asphaltPoints, Is.GreaterThan(0), junction.StableId + " lost its road entrance.");
                bool mixed = false;
                foreach (AlpineVillageJunctionPort port in junction.Ports)
                    mixed |= port.Kind != AlpineVillagePathKind.AbandonedRoad;
                if (mixed)
                {
                    Assert.That(soilPoints, Is.GreaterThan(0), junction.StableId + " lost its soil entrances.");
                    Assert.That(blendPoints, Is.GreaterThan(0), junction.StableId + " has a hard texture seam.");
                }
                else
                {
                    Assert.That(soilPoints, Is.Zero, junction.StableId + " gained soil inside an asphalt-only junction.");
                    Assert.That(blendPoints, Is.Zero, junction.StableId + " gained a blend inside an asphalt-only junction.");
                }
                Debug.Log($"Junction {junction.StableId}: asphalt/soil/blended samples {asphaltPoints}/{soilPoints}/{blendPoints}.");

                void Check(Vector2 point, string label)
                {
                    var world = new Vector3(point.x, 0f, point.y);
                    Assert.That(coating.TryHeight(world, out float height), Is.True,
                        $"{junction.StableId} lost its atlas coating at {label} {point:F3}.");
                    Assert.That(asphalt.Interior(world), Is.False,
                        junction.StableId + " overlays the junction atlas with ordinary asphalt at " + point);
                    float original = AlpineVillageTerrainSampler.SampleMeshHeight(root.Plan, point);
                    maximumGap = Mathf.Max(maximumGap, Mathf.Abs(height - original));
                    Assert.That(ground.Raycast(new Ray(new Vector3(point.x, height + 3f, point.y), Vector3.down),
                        out RaycastHit hit, 6f), Is.True);
                    maximumGap = Mathf.Max(maximumGap, Mathf.Abs(height - hit.point.y));
                    float weight = junction.SampleAsphaltWeight(point);
                    if (weight >= .95f) asphaltPoints++;
                    if (weight <= .05f) soilPoints++;
                }
            }
            Assert.That(maximumGap, Is.LessThan(.003f), "A junction left the original terrain/collider plane.");
            foreach (Vector2 local in new[] { new Vector2(-37f, -3f), new Vector2(-120f, 22f),
                new Vector2(-112f, 93f), new Vector2(-130f, -28f) })
            {
                Vector3 world = root.Plan.Expansion.ToWorld(local);
                bool found = false;
                foreach (AlpineVillageJunctionPlan junction in root.Plan.Expansion.Junctions)
                    found |= Vector2.Distance(junction.Center, new Vector2(world.x, world.z)) < .1f;
                Assert.That(found, Is.True, "Missing planned junction at " + local);
            }
            Debug.Log($"Junction maximum original-ground/collider gap: {maximumGap:F6} m.");
        }

        private static void VerifyVillageJunctionAtlas(AlpineVillagePlan plan)
        {
            Texture2D atlas = AlpineVillageJunctionAppearance.Texture;
            Assert.That(atlas, Is.Not.Null, "The junction atlas was not imported.");
            Assert.That(atlas, Is.SameAs(Resources.Load<Texture2D>(AlpineVillageJunctionAppearance.TextureResourcePath)));
            // Production keeps the atlas GPU-only. Read the same imported PNG
            // without altering its importer merely for this explicit capture.
            string path = System.IO.Path.Combine(Application.dataPath, "Resources",
                AlpineVillageJunctionAppearance.TextureResourcePath + ".png");
            Assert.That(System.IO.File.Exists(path), Is.True, "The junction atlas source is missing.");
            var readable = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            try
            {
                Assert.That(readable.LoadImage(System.IO.File.ReadAllBytes(path)), Is.True);
                Assert.That(readable.width, Is.EqualTo(atlas.width));
                Assert.That(readable.height, Is.EqualTo(atlas.height));
                var previous = new List<Color[]>();
                var centres = new List<Vector2>();
                foreach (AlpineVillageJunctionPlan junction in plan.Expansion.Junctions)
                {
                    Vector2 centreUv = AlpineVillageJunctionAppearance.Uv(plan, junction.Center);
                    foreach (Vector2 prior in centres)
                        Assert.That(Vector2.Distance(prior, centreUv), Is.GreaterThan(.05f),
                            junction.StableId + " reuses another junction's atlas region.");
                    centres.Add(centreUv);
                    var samples = new Color[9];
                    int index = 0;
                    for (int x = -1; x <= 1; x++)
                    for (int z = -1; z <= 1; z++)
                    {
                        Vector2 uv = AlpineVillageJunctionAppearance.Uv(plan,
                            junction.Center + new Vector2(x * 1.1f, z * 1.1f));
                        Assert.That(uv.x, Is.InRange(0f, 1f));
                        Assert.That(uv.y, Is.InRange(0f, 1f));
                        samples[index++] = readable.GetPixelBilinear(uv.x, uv.y);
                    }
                    foreach (Color[] prior in previous)
                    {
                        float difference = 0f;
                        for (int sample = 0; sample < samples.Length; sample++)
                            difference += Mathf.Abs(samples[sample].r - prior[sample].r) +
                                Mathf.Abs(samples[sample].g - prior[sample].g) + Mathf.Abs(samples[sample].b - prior[sample].b);
                        Assert.That(difference, Is.GreaterThan(.01f),
                            junction.StableId + " repeats another junction's baked drawing.");
                    }
                    previous.Add(samples);
                }
            }
            finally { Object.Destroy(readable); }
        }

        private static void VerifyVillageRoadSurface(AlpineVillageRoot root)
        {
            AlpineVillagePlan plan = root.Plan;
            AlpineVillageExpansionPlan expansion = plan.Expansion;
            Transform world = root.World.Root.transform;
            foreach (Transform candidate in world.GetComponentsInChildren<Transform>(true))
                Assert.That(candidate.name, Is.Not.EqualTo("Old Asphalt"), "Raised road blocks returned.");
            Transform road = world.Find("Village Expansion/Former City Road");
            Assert.That(road, Is.Not.Null);
            Assert.That(world.Find("Village Expansion/Broken Road Edge"), Is.Not.Null);
            Assert.That(world.Find("Village Expansion/Opposite Broken Road Edge"), Is.Not.Null);

            GameObject terrain = root.World.TerrainRoot;
            Mesh mesh = terrain.GetComponent<MeshFilter>().sharedMesh;
            MeshCollider ground = terrain.GetComponent<MeshCollider>();
            AlpineVillageTerrainGrid originalGrid = AlpineVillageTerrainGrid.Get(plan);
            int originalVertices = originalGrid.XCoordinates.Length * originalGrid.ZCoordinates.Length;
            Assert.That(mesh.vertexCount, Is.LessThan(originalVertices * 4),
                "Local junction fitting fragmented the village terrain into an excessive mesh.");
            Assert.That(ground.sharedMesh, Is.SameAs(mesh));
            Assert.That(terrain.GetComponentsInChildren<Collider>(true).Length, Is.EqualTo(1),
                "Painting asphalt must preserve the single ground collider.");
            int slot = AlpineVillageWorldBuilder.TerrainAsphaltMaterialIndex;
            int[] asphalt = mesh.GetTriangles(slot);
            int junctionSlot = AlpineVillageWorldBuilder.TerrainJunctionMaterialIndex;
            int[] junctionCoating = mesh.GetTriangles(junctionSlot);
            int soilSlot = AlpineVillageWorldBuilder.TerrainSoilMaterialIndex;
            int[] soilApproaches = mesh.GetTriangles(soilSlot);
            Vector3[] vertices = mesh.vertices;
            Assert.That(asphalt, Is.Not.Empty);
            Assert.That(junctionCoating, Is.Not.Empty);
            Assert.That(soilApproaches, Is.Not.Empty);
            Vector2[] uv = mesh.uv;
            foreach (int vertex in new HashSet<int>(junctionCoating))
            {
                Vector2 expected = AlpineVillageJunctionAppearance.Uv(plan, new Vector2(vertices[vertex].x, vertices[vertex].z));
                Assert.That(Vector2.Distance(uv[vertex], expected), Is.LessThan(.0001f),
                    "A junction vertex uses a repeating surface UV instead of its atlas region.");
            }
            float soilPitch = MountainRoadSurfaceAppearance.GetRecipe(MountainRoadSurfaceKind.ForestFloor).MetersPerTile;
            foreach (int vertex in new HashSet<int>(soilApproaches))
                Assert.That(Vector2.Distance(uv[vertex], new Vector2(vertices[vertex].x, vertices[vertex].z) / soilPitch),
                    Is.LessThan(.0001f), "Soil beneath the path mouth lost its world texture phase.");
            var soilProbe = new VillageRoadMeshProbe(terrain.GetComponent<MeshFilter>(), soilSlot);
            foreach (AlpineVillageJunctionPlan junction in expansion.Junctions)
            foreach (AlpineVillageJunctionPort port in junction.Ports)
            {
                if (port.Kind == AlpineVillagePathKind.AbandonedRoad) continue;
                Vector2 side = new Vector2(-port.Direction.y, port.Direction.x) * port.HalfWidth;
                foreach (float along in new[] { .05f, .5f, 1.5f, 1.95f })
                foreach (float across in new[] { -.9f, 0f, .9f })
                {
                    Vector2 point = port.MouthCenter + port.Direction * along + side * across;
                    Assert.That(soilProbe.TryHeight(new Vector3(point.x, 0f, point.y), out _),
                        Is.True, port.PathStableId + " exposes bright terrain beneath its taper at " + point);
                }
            }
            var painted = new VillageRoadMeshProbe(terrain.GetComponent<MeshFilter>());
            long renderedIndices = 0, renderedJunctionIndices = 0, renderedSoilIndices = 0;
            foreach (MeshFilter sector in terrain.GetComponentsInChildren<MeshFilter>(true))
            {
                if (sector.gameObject == terrain) continue;
                renderedIndices += (long)sector.sharedMesh.GetIndexCount(slot);
                renderedJunctionIndices += (long)sector.sharedMesh.GetIndexCount(junctionSlot);
                renderedSoilIndices += (long)sector.sharedMesh.GetIndexCount(soilSlot);
                var properties = new MaterialPropertyBlock();
                sector.GetComponent<MeshRenderer>().GetPropertyBlock(properties, slot);
                Assert.That(properties.GetTexture("_BaseMap"),
                    Is.SameAs(MountainRoadSurfaceAppearance.GetTexture(MountainRoadSurfaceKind.Asphalt)),
                    sector.name + " lost the asphalt appearance.");
                sector.GetComponent<MeshRenderer>().GetPropertyBlock(properties, junctionSlot);
                Assert.That(properties.GetTexture("_BaseMap"),
                    Is.SameAs(AlpineVillageJunctionAppearance.Texture),
                    sector.name + " lost the junction atlas appearance.");
                Assert.That(properties.GetColor("_BaseColor"), Is.EqualTo(Color.white),
                    sector.name + " tints the baked junction albedo twice.");
                Assert.That(properties.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)),
                    sector.name + " transforms the baked atlas UV twice.");
                sector.GetComponent<MeshRenderer>().GetPropertyBlock(properties, soilSlot);
                Assert.That(properties.GetTexture("_BaseMap"),
                    Is.SameAs(MountainRoadSurfaceAppearance.GetTexture(MountainRoadSurfaceKind.ForestFloor)));
                Color soilTint = MountainRoadSurfaceAppearance.CreateDisplayTint(
                    AlpineVillageRoadSurfaceBuilder.JunctionSoilTint, MountainRoadSurfaceKind.ForestFloor);
                Assert.That(Vector4.Distance(properties.GetColor("_BaseColor"), soilTint), Is.LessThan(.00001f),
                    sector.name + " no longer matches the soil path's appearance.");
            }
            Assert.That(renderedIndices, Is.EqualTo(asphalt.Length), "Render sectors lost part of the painted road.");
            Assert.That(renderedJunctionIndices, Is.EqualTo(junctionCoating.Length), "Render sectors lost part of the junction coating.");
            Assert.That(renderedSoilIndices, Is.EqualTo(soilApproaches.Length), "Render sectors lost part of a soil approach.");

            Physics.SyncTransforms();
            float maximumGap = 0f;
            float colliderGap = 0f;
            foreach (int[] surface in new[] { asphalt, junctionCoating, soilApproaches })
            for (int index = 0; index < surface.Length; index += 3)
            {
                Vector3 a = vertices[surface[index]], b = vertices[surface[index + 1]], c = vertices[surface[index + 2]];
                foreach (Vector3 point in new[] { a, b, c, (a + b + c) / 3f })
                    maximumGap = Mathf.Max(maximumGap, Mathf.Abs(point.y -
                        AlpineVillageTerrainSampler.SampleMeshHeight(plan, new Vector2(point.x, point.z))));
                if (index % 51 != 0 || Vector3.Cross(b - a, c - a).sqrMagnitude < .000001f) continue;
                Vector3 centre = (a + b + c) / 3f;
                Assert.That(ground.Raycast(new Ray(centre + Vector3.up * 5f, Vector3.down),
                    out RaycastHit hit, 10f), Is.True, "A road/junction has no physical ground.");
                colliderGap = Mathf.Max(colliderGap, Mathf.Abs(centre.y - hit.point.y));
            }
            Assert.That(maximumGap, Is.LessThan(.003f), "A road/junction lifted or flattened the original ground triangles.");
            Assert.That(colliderGap, Is.LessThan(.003f), "A visible road/junction and the physical ground disagree.");
            Debug.Log($"Road/junction coating maximum original-ground/collider gaps: {maximumGap:F6}/{colliderGap:F6} m.");
            double area = 0d;
            for (int material = 0; material < mesh.subMeshCount; material++)
            {
                int[] surface = mesh.GetTriangles(material);
                for (int index = 0; index < surface.Length; index += 3)
                {
                    Vector3 a = vertices[surface[index]], b = vertices[surface[index + 1]], c = vertices[surface[index + 2]];
                    area += System.Math.Abs(((double)b.x - a.x) * ((double)c.z - a.z) -
                        ((double)b.z - a.z) * ((double)c.x - a.x)) * .5d;
                }
            }
            AlpineVillageTerrainGrid grid = AlpineVillageTerrainGrid.Get(plan);
            double expectedArea = ((double)grid.XCoordinates[grid.Columns] - grid.XCoordinates[0]) *
                ((double)grid.ZCoordinates[grid.Rows] - grid.ZCoordinates[0]);
            Assert.That(area, Is.EqualTo(expectedArea).Within(.1d), "Ground material regions overlap or leave a hole.");

            var paths = AlpineVillagePathPlanner.Create(plan);
            foreach (AlpineVillagePathDescriptor path in expansion.Paths)
            {
                if (path.Kind != AlpineVillagePathKind.AbandonedRoad) continue;
                Vector3 direction = (path.End - path.Start).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, direction).normalized;
                foreach (float amount in new[] { .05f, .5f, .95f })
                foreach (float side in new[] { -2.2f, 0f, 2.2f })
                {
                    Vector3 point = Vector3.Lerp(path.Start, path.End, amount) + right * side;
                    bool covered = VillageAsphaltCovers(point, vertices, asphalt);
                    bool returnedToSnow = false;
                    foreach (AlpineVillageJunctionPlan junction in expansion.Junctions)
                    {
                        Vector2 xz = new Vector2(point.x, point.z);
                        if (!junction.Owns(xz)) continue;
                        returnedToSnow = !junction.Contains(xz);
                        covered = returnedToSnow || painted.TryHeight(point, out _);
                        break;
                    }
                    Assert.That(covered, Is.True, path.StableId + " lost its road or junction surface at " + point);
                    if (returnedToSnow) continue;
                    Assert.That(AlpineVillageSnowDrift.SampleDepth(plan, paths, new Vector2(point.x, point.z)),
                        Is.Zero, "Painting asphalt changed road snow clearance.");
                }
            }
            foreach (Vector2 local in new[] { new Vector2(-70f, 65f), new Vector2(-130f, -53.75f) })
                Assert.That(VillageAsphaltCovers(expansion.ToWorld(local), vertices, asphalt), Is.True,
                    "Road bend or cliff approach has a hole at " + local);
            Assert.That(VillageAsphaltCovers(expansion.ToWorld(new Vector2(-130f, -28f)), vertices, junctionCoating),
                Is.True, "The warehouse junction has a hole in its atlas coating.");
            foreach (float along in new[] { -67.25f, -85f, -106f })
                Assert.That(VillageAsphaltCovers(expansion.FarRoadPoint(along), vertices, asphalt), Is.True,
                    "Opposite road lost its coating at " + along);
            Assert.That(VillageAsphaltCovers((expansion.CliffEdge + expansion.FarRoadEdge) * .5f,
                vertices, asphalt), Is.False, "Asphalt bridges the missing road shelf.");
            Assert.That(root.World.WalkableArea.Contains(expansion.FarRoadEdge, .35f), Is.False);
            for (float across = -5f; across <= 5f; across += 1f)
                Assert.That(root.World.WalkableArea.Contains(expansion.CliffBarrierCenter +
                    plan.SlopeRight * across, .35f), Is.False, "Painting asphalt opened the barrier.");
        }

        private static bool VillageAsphaltCovers(Vector3 point, Vector3[] vertices, int[] triangles)
        {
            for (int index = 0; index < triangles.Length; index += 3)
            {
                Vector3 a = vertices[triangles[index]], b = vertices[triangles[index + 1]], c = vertices[triangles[index + 2]];
                float area = Cross(a, b, c);
                if (Mathf.Abs(area) < .000001f) continue;
                float first = Cross(point, b, c) / area;
                float second = Cross(a, point, c) / area;
                if (first >= -.0001f && second >= -.0001f && first + second <= 1.0001f) return true;
            }
            return false;

            float Cross(Vector3 a, Vector3 b, Vector3 c) =>
                (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
        }

        [UnityTest]
        [Explicit("Focused expansion capture and traversal/asset contracts.")]
        [PrebuildSetup(typeof(VillageArtAssetsSetup))]
        public IEnumerator AlpineVillageExpansion()
        {
            GameSessionState.TryStartGameTimeFromWake();
            Assert.That(GameSessionState.TrySetDebugGameDay(2), Is.True);
            GameSessionState.AdvanceGameTime((float)(100f / GameTimeState.GameMinutesPerRealSecond));
            AlpineVillageRoot root = null;
            yield return Capture(SceneIds.AlpineVillage,
                () => root = Object.FindAnyObjectByType<AlpineVillageRoot>(), () =>
                {
                    AlpineVillageLaneSample foot = root.Plan.Lane.Sample(2f);
                    var shots = new List<Shot>
                    {
                        Shot.At("expansion-00-preserved-village-axis",
                            foot.Position - foot.Forward * PlatformApronSetback + Vector3.up * EyeHeight,
                            root.Plan.MothersHouse.GroundCenter + Vector3.up * LandmarkAimHeight,
                            50f, 0, () => root.StormWave <= GustTroughWave)
                    };
                    AppendVillageExpansionShots(root, shots);
                    return shots.ToArray();
                });
        }

        private static void AppendVillageExpansionShots(AlpineVillageRoot root, List<Shot> shots)
        {
            AlpineVillagePlan plan = root.Plan;
            AlpineVillageExpansionPlan expansion = plan.Expansion;
            int originalGround = 0, expandedGround = 0;
            Rect extent = plan.TerrainBounds;
            Rect core = plan.CoreTerrainBounds;
            for (float z = extent.yMin; z <= extent.yMax; z += 2f)
            for (float x = extent.xMin; x <= extent.xMax; x += 2f)
            {
                var point = new Vector2(x, z);
                bool wasGround = x >= core.xMin - 3f && x <= core.xMax + 3f &&
                    z >= core.yMin - 3f && z <= core.yMax + 3f;
                if (wasGround) originalGround++;
                if (wasGround || expansion.ContainsGround(point)) expandedGround++;
            }
            float areaRatio = expandedGround / (float)originalGround;
            Assert.That(areaRatio, Is.InRange(3f, 4f), "The expansion must materially enlarge the traversable ground.");
            TestContext.WriteLine("Expanded village ground ratio: " + areaRatio.ToString("F2"));
            IReadOnlyList<AlpineVillagePathDescriptor> paths = AlpineVillagePathPlanner.Create(plan);
            Assert.That(AlpineVillageSnowDrift.SampleDepth(plan, paths,
                new Vector2(expansion.LodgeCenter.x, expansion.LodgeCenter.z)), Is.Zero);
            Assert.That(root.World.TerrainRoot.GetComponentsInChildren<MeshRenderer>().Length,
                Is.GreaterThan(2), "The expanded ground needs spatial render batches.");
            Assert.That(root.World.WalkableArea.Contains(expansion.LodgeCenter, .35f), Is.False,
                "The central cold stove occupies the former straight aisle.");

            // Measure the actual imported building, not only its authoring anchors.
            Transform lodge = root.World.Root.transform.Find("Village Expansion/Ski Lodge");
            Assert.That(lodge, Is.Not.Null);
            Bounds bounds = default;
            bool first = true;
            foreach (Renderer renderer in lodge.GetComponentsInChildren<Renderer>())
            {
                if (first) { bounds = renderer.bounds; first = false; }
                else bounds.Encapsulate(renderer.bounds);
            }
            Assert.That(first, Is.False);
            Assert.That(bounds.size.y, Is.InRange(3.5f, 8f), "Imported lodge scale.");
            Assert.That(bounds.size.x, Is.GreaterThan(12f));
            Assert.That(bounds.size.z, Is.GreaterThan(10f));

            Physics.SyncTransforms();
            float previousFloor = float.NaN;
            for (float along = -10f; along <= 0f; along += .5f)
            {
                Vector3 point = expansion.LodgeCenter + expansion.LodgeForward * along +
                    plan.SlopeRight * (along > -3.5f ? 1.25f : 0f);
                Assert.That(Physics.Raycast(point + Vector3.up, Vector3.down,
                    out RaycastHit hit, 2f), Is.True, "The lodge entrance has no floor.");
                if (!float.IsNaN(previousFloor))
                    Assert.That(Mathf.Abs(hit.point.y - previousFloor), Is.LessThan(.25f),
                        "A floor or terrain lip blocks the lodge doorway.");
                previousFloor = hit.point.y;
                if (along >= -5.5f)
                    Assert.That(hit.point.y, Is.EqualTo(expansion.LodgeFloorHeight + .02f).Within(.04f),
                        "The hero must stand on the imported lodge floor.");
                Assert.That(root.World.WalkableArea.Contains(point, .35f), Is.True,
                    "The physical doorway and movement mask disagree.");
            }

            // Exercise the expanded snow's spatial query on an actual rendered
            // vertex, including its mutable footprint rather than only plan depth.
            bool pressed = false;
            foreach (Vector3 vertex in root.World.SnowTreading.GetComponent<MeshFilter>().sharedMesh.vertices)
            {
                if (plan.CoreTerrainBounds.Contains(new Vector2(vertex.x, vertex.z)) ||
                    !expansion.ContainsGround(new Vector2(vertex.x, vertex.z))) continue;
                float before = root.World.SnowTreading.SampleVisibleDepth(vertex);
                if (before < .2f) continue;
                root.World.SnowTreading.Press(vertex);
                Assert.That(root.World.SnowTreading.SampleVisibleDepth(vertex), Is.LessThan(before * .5f));
                pressed = true;
                break;
            }
            Assert.That(pressed, Is.True, "The new forest lost its lying snow.");

            // A reachable eye-height view must clear both the rubble and the
            // upper road shelf, otherwise the broken descent reads as a dead end.
            Vector3 cliffView = expansion.ToWorld(new Vector2(-128.6f, -50f));
            cliffView.y = AlpineVillageTerrainSampler.SampleHeight(plan,
                new Vector2(cliffView.x, cliffView.z));
            Assert.That(root.World.WalkableArea.Contains(cliffView, .35f), Is.True);
            Vector3 oppositeRoad = expansion.FarRoadEdge + Vector3.up * .2f;
            Assert.That(Vector3.Distance(new Vector3(expansion.CliffEdge.x, 0f, expansion.CliffEdge.z),
                new Vector3(oppositeRoad.x, 0f, oppositeRoad.z)), Is.InRange(10f, 15f));
            Assert.That(expansion.CliffEdge.y - expansion.FarRoadEdge.y, Is.InRange(1f, 3f));
            Vector3 gap = (expansion.CliffEdge + expansion.FarRoadEdge) * .5f;
            Assert.That(AlpineVillageTerrainSampler.SampleHeight(plan, new Vector2(gap.x, gap.z)),
                Is.LessThan(expansion.CliffEdge.y - 15f), "A real void must separate the two shelves.");
            Assert.That(Physics.Linecast(cliffView + Vector3.up * EyeHeight, oppositeRoad,
                out RaycastHit obstruction), Is.False,
                "The opposite road is hidden behind " + (obstruction.collider == null ? "terrain" : obstruction.collider.name));
            for (float across = -5f; across <= 5f; across += 1f)
                Assert.That(root.World.WalkableArea.Contains(expansion.CliffBarrierCenter +
                    plan.SlopeRight * across, .35f), Is.False, "The road barrier has a gap.");
            Assert.That(root.World.WalkableArea.Contains(expansion.CliffEdge - plan.Uphill * 4f, .35f), Is.False);
            Assert.That(root.World.WalkableArea.Contains(expansion.FarRoadEdge, .35f), Is.False);
            foreach (Vector2 local in new[] { new Vector2(-130f, -28f), new Vector2(-135f, -28f),
                new Vector2(-135f, -36f), new Vector2(-128.6f, -52.85f) })
            {
                Vector3 point = expansion.ToWorld(local);
                point.y = AlpineVillageTerrainSampler.SampleHeight(plan, new Vector2(point.x, point.z));
                Assert.That(root.World.WalkableArea.Contains(point, .35f), Is.True, "Trade yard access " + local);
                Assert.That(Physics.Raycast(point + Vector3.up * 1.5f, Vector3.down, out RaycastHit ground, 3f), Is.True);
                Assert.That(ground.point.y, Is.EqualTo(point.y).Within(.25f), "Cargo blocks the intended walking route.");
            }
            Transform warehouse = root.World.Root.transform.Find("Village Expansion/Former Trade Warehouse");
            Assert.That(warehouse, Is.Not.Null);
            Bounds warehouseBounds = warehouse.GetComponentInChildren<Renderer>().bounds;
            foreach (Renderer renderer in warehouse.GetComponentsInChildren<Renderer>()) warehouseBounds.Encapsulate(renderer.bounds);
            Assert.That(warehouseBounds.size.y, Is.InRange(4f, 7f), "Imported warehouse metre scale.");
            Transform wreck = root.World.Root.transform.Find("Village Expansion/Abandoned Truck Wreck");
            Transform chairs = root.World.Root.transform.Find("Village Expansion/Discarded Wooden Chairs");
            Assert.That(wreck, Is.Not.Null);
            Assert.That(chairs, Is.Not.Null);
            Bounds wreckBounds = LocalRendererBounds(wreck);
            Bounds chairBounds = LocalRendererBounds(chairs);
            Assert.That(wreckBounds.size.z, Is.InRange(6f, 6.8f), "Wreck retained donor metre scale.");
            Assert.That(wreckBounds.size.y, Is.InRange(2.2f, 2.95f), "Wreck sits low without wheels.");
            Assert.That(chairBounds.size.x, Is.InRange(4.8f, 6.2f), "Chair heap must read as a substantial pile.");
            Assert.That(chairBounds.size.y, Is.InRange(2.7f, 2.8f), "Supported chair heap must retain its original height.");
            foreach (string part in new[] { "CabShell", "BentNose", "BentArches", "CargoFrame", "FloorRemnants" })
            {
                var properties = new MaterialPropertyBlock();
                wreck.Find(part).GetComponent<Renderer>().GetPropertyBlock(properties);
                string resource = part == "CabShell" || part == "BentNose"
                    ? VillageExpansionAssetProvider.WreckPaintTexturePath : VillageExpansionAssetProvider.WreckRustTexturePath;
                Assert.That(properties.GetTexture("_BaseMap"), Is.SameAs(Resources.Load<Texture2D>(resource)),
                    "Wreck must use its corrosion albedo, not the active truck paint.");
                Assert.That(properties.GetFloat("_Smoothness"), Is.LessThan(.1f));
            }
            foreach (Vector2 local in new[] { new Vector2(-139f, -39f), new Vector2(-144f, -36.3f),
                new Vector2(-139f, -17f), new Vector2(-144f, -19.8f) })
            {
                Vector3 point = expansion.ToWorld(local);
                point.y = AlpineVillageTerrainSampler.SampleHeight(plan, new Vector2(point.x, point.z));
                Assert.That(root.World.WalkableArea.Contains(point, .35f), Is.True, "Warehouse remnant bypass " + local);
                Assert.That(Physics.CheckCapsule(point + Vector3.up * .5f, point + Vector3.up * 1.5f, .3f,
                    ~0, QueryTriggerInteraction.Ignore), Is.False, "Wreck/chairs block the warehouse bypass " + local);
            }
            Transform repair = root.World.Root.transform.Find("Village Expansion/Conserved Road Repair");
            Assert.That(repair, Is.Not.Null);
            MeshFilter anchorMesh = repair.Find("CappedAnchors").GetComponent<MeshFilter>();
            bool seesRepair = false;
            foreach (Vector3 vertex in anchorMesh.sharedMesh.vertices)
            {
                Vector3 point = anchorMesh.transform.TransformPoint(vertex) + Vector3.up * .04f;
                if (!Physics.Linecast(cliffView + Vector3.up * EyeHeight, point,
                    ~0, QueryTriggerInteraction.Ignore)) { seesRepair = true; break; }
            }
            Assert.That(seesRepair, Is.True, "The conserved anchors must be visible above the old lip from safe ground.");
            Transform distance = root.World.Root.transform.Find("Village Expansion/" + AlpineVillageDistanceWorldBuilder.ObjectName);
            Assert.That(distance, Is.Not.Null);
            var checkpointMeshes = new HashSet<Mesh>();
            foreach (MeshFilter filter in Resources.Load<GameObject>(CityEastDistanceWorldBuilder.ResourcePath)
                .GetComponentsInChildren<MeshFilter>(true)) checkpointMeshes.Add(filter.sharedMesh);
            foreach (MeshFilter filter in distance.GetComponentsInChildren<MeshFilter>(true))
                Assert.That(checkpointMeshes.Contains(filter.sharedMesh), Is.True,
                    "The village must show the checkpoint's same authored city and valley.");
            Assert.That(distance.GetComponent<CityEastDistanceTraffic>(), Is.Null);
            Assert.That(AlpineVillageSnowDrift.SampleDepth(plan, paths,
                new Vector2(expansion.YardPropsCenter.x, expansion.YardPropsCenter.z)), Is.LessThanOrEqualTo(.12f));

            Add("40-station-forest-entry", new Vector2(-12f, -2f), new Vector2(-55f, 0f), 65f);
            Add("41-deep-forest-trail", new Vector2(-78f, 6f), new Vector2(-120f, 22f), 64f);
            Add("42-ski-lodge-front", new Vector2(-137f, 43f), new Vector2(-137f, 56f), 76f);
            Add("43-ski-lodge-inside", new Vector2(-137f, 52f), new Vector2(-134f, 60f), 78f);
            Add("44-abandoned-ski-tow", new Vector2(-157f, 81f), new Vector2(-151f, 113f), 64f);
            Add("45-old-road-descent", new Vector2(-127f, -5f), new Vector2(-130f, -48f), 62f);
            Add("46-broken-city-road", new Vector2(-128.6f, -50f), new Vector2(-130f, -67f), 60f,
                expansion.FarRoadEdge.y + .8f - expansion.ToWorld(new Vector2(-130f, -67f)).y);
            Add("47-forest-return", new Vector2(-75f, 69f), new Vector2(-49f, 25f), 64f);
            Add("48-former-trade-warehouse", new Vector2(-127f, -37f), new Vector2(-145f, -28f), 62f, 2.4f);
            Add("49-unused-loading-yard", new Vector2(-130f, -27f), new Vector2(-136f, -20f), 62f, .8f);
            Add("50-conserved-repair", new Vector2(-131f, -47f), new Vector2(-135f, -51f), 64f, .3f);
            Add("51-road-city-gust", new Vector2(-128.6f, -50f), new Vector2(-130f, -67f), 60f,
                expansion.FarRoadEdge.y + .8f - expansion.ToWorld(new Vector2(-130f, -67f)).y, true);
            Add("52-unfinished-abutments", new Vector2(-124.8f, -52.7f), new Vector2(-128f, -55.2f), 70f, -1.8f);
            Add("53-abandoned-truck", new Vector2(-135f, -35f), new Vector2(-145f, -39f), 62f, 1.1f);
            Add("54-open-rusted-cab", new Vector2(-139f, -40.5f), new Vector2(-142.5f, -39f), 65f, 1.2f);
            Add("55-discarded-chair-heap", new Vector2(-136f, -12f), new Vector2(-144f, -17f), 61f, 1.35f);
            Add("56-chair-frames-close", new Vector2(-140f, -14f), new Vector2(-144f, -17f), 65f, 1.25f);
            Add("57-chair-supports-reverse", new Vector2(-150f, -17f), new Vector2(-144f, -17f), 65f, 1.1f);
            Add("58-truck-chassis-rear", new Vector2(-151f, -41f), new Vector2(-145f, -39f), 68f, 1.1f);

            void Add(string name, Vector2 from, Vector2 toward, float fov, float targetLift = 1.8f, bool gust = false)
            {
                Vector3 foot = expansion.ToWorld(from);
                foot.y = AlpineVillageTerrainSampler.SampleHeight(plan, new Vector2(foot.x, foot.z));
                if (expansion.IsInterior(foot)) foot.y = expansion.LodgeFloorHeight + .02f;
                Vector3 target = expansion.ToWorld(toward) + Vector3.up * targetLift;
                bool moved = false;
                int frames = 0;
                shots.Add(Shot.At(name, foot + Vector3.up * EyeHeight, target, fov, 0, () =>
                {
                    if (!moved)
                    {
                        root.SetWarmthGrade(0f);
                        root.Player.Motor.Teleport(foot + Vector3.up * PlayerFactory.GroundedRootOffset);
                        moved = true;
                    }
                    bool ready = ++frames > 12 && (gust ? root.StormWave >= GustCrestWave : root.StormWave <= GustTroughWave);
                    // A teleport/camera mode update can restore worn renderers after
                    // Capture's initial hide. These are world reviews, so renew it at exposure.
                    if (ready)
                        foreach (Renderer renderer in root.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                            renderer.enabled = false;
                    return ready;
                }));
            }
        }

        private static Bounds LocalRendererBounds(Transform root)
        {
            Bounds bounds = default;
            bool first = true;
            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>())
            foreach (Vector3 vertex in filter.sharedMesh.vertices)
            {
                Vector3 point = root.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                if (first) { bounds = new Bounds(point, Vector3.zero); first = false; }
                else bounds.Encapsulate(point);
            }
            Assert.That(first, Is.False, "Placed prop has no actual mesh vertices.");
            return bounds;
        }
    }
}
