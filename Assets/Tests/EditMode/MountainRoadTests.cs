using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class MountainRoadTests
    {
        [Test]
        [Category("MountainRoad")]
        public void DefaultPlan_BuildsLongGroundedTwoHairpinWorld()
        {
            MountainRoadPlan first = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadPlan second = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);

            Assert.DoesNotThrow(() =>
                MountainRoadValidator.ValidateOrThrow(first));
            Assert.That(
                first.Route.Length,
                Is.EqualTo(82.7f).Within(0.01f));
            Assert.That(
                first.Route.ElevationGain,
                Is.EqualTo(8.7f).Within(0.01f));
            Assert.That(MountainRoadPlanner.RoadWidth, Is.EqualTo(4.8f));
            Assert.That(MountainRoadPlanner.HairpinWidth, Is.EqualTo(6.4f));
            Assert.That(MountainRoadPlanner.HairpinRadius, Is.EqualTo(7.5f));
            Assert.That(
                first.SpawnPosition,
                Is.EqualTo(new Vector3(0f, 0f, -6f)));
            Assert.That(first.SpawnForward, Is.EqualTo(Vector3.forward));
            Assert.That(
                MountainRoadTerrainSampler.SampleHeight(
                    first.Route,
                    first.Plateau,
                    new Vector2(
                        first.SpawnPosition.x,
                        first.SpawnPosition.z)),
                Is.EqualTo(
                    first.SpawnPosition.y -
                    MountainRoadTerrainSampler.RoadBedClearance).Within(0.001f));
            Assert.That(
                first.Route.Length / 2.6f,
                Is.InRange(31f, 33f));
            Assert.That(
                first.Forest.Min(item => item.Height),
                Is.GreaterThanOrEqualTo(7f));
            Assert.That(
                first.Forest.Max(item => item.Height),
                Is.GreaterThan(16f));
            Assert.That(
                first.Forest.Count(item =>
                    item.Layer == MountainRoadForestLayer.Physical),
                Is.EqualTo(46));
            Assert.That(
                first.Forest.Count(item =>
                    item.Layer == MountainRoadForestLayer.Mid),
                Is.EqualTo(84));
            Assert.That(
                first.Forest.Count(item =>
                    item.Layer == MountainRoadForestLayer.Far),
                Is.EqualTo(112));
            Assert.That(first.SoundAnchors, Has.Count.EqualTo(5));
            Assert.That(
                first.SoundAnchors.All(sound =>
                    first.Misc.Any(item =>
                        item.StableId == sound.SourceObjectStableId &&
                        item.Position == sound.Position)),
                Is.True,
                "Every positioned sound belongs to a visible semantic prop.");

            Assert.That(
                second.Route.Samples.Count,
                Is.EqualTo(first.Route.Samples.Count));
            for (int index = 0; index < first.Route.Samples.Count; index++)
            {
                Assert.That(
                    second.Route.Samples[index].Position,
                    Is.EqualTo(first.Route.Samples[index].Position));
                Assert.That(
                    second.Route.Samples[index].Width,
                    Is.EqualTo(first.Route.Samples[index].Width));
            }

            var walkable = new MountainRoadWalkableArea(first);
            for (float z = first.SpawnPosition.z; z <= 0f; z += 0.5f)
            {
                Assert.That(
                    walkable.Contains(new Vector3(0f, 0f, z), 0.32f),
                    Is.True,
                    $"Tunnel floor breaks at z={z:0.0}.");
            }
            for (float distance = 0f;
                 distance <= first.Route.Length;
                 distance += 3.5f)
            {
                Assert.That(
                    walkable.Contains(
                        first.Route.Sample(distance).Position,
                        1.05f),
                    Is.True,
                    $"The LastRouteCar-width corridor breaks at " +
                    $"{distance:0.0} m.");
            }

            MountainRoadRouteSample plateauEntry = first.Route.Sample(
                first.Plateau.EntryDistance);
            Vector3 entryLeft = plateauEntry.Position -
                                plateauEntry.Right *
                                (plateauEntry.Width * 0.5f);
            Vector3 entryRight = plateauEntry.Position +
                                 plateauEntry.Right *
                                 (plateauEntry.Width * 0.5f);
            for (int step = 0; step <= 14; step++)
            {
                Vector3 drivePoint = Vector3.Lerp(
                    plateauEntry.Position,
                    first.Plateau.Center,
                    step / 14f);
                Assert.That(
                    walkable.Contains(drivePoint, 1.05f),
                    Is.True,
                    $"The vehicle apron breaks at step {step}.");
            }
            Vector2 entryXZ = new Vector2(
                plateauEntry.Position.x,
                plateauEntry.Position.z);
            Vector2 entryForward = new Vector2(
                plateauEntry.Forward.x,
                plateauEntry.Forward.z).normalized;
            float beforeEntryTerrain = MountainRoadTerrainSampler.SampleHeight(
                first.Route,
                first.Plateau,
                entryXZ - entryForward * 0.05f);
            float afterEntryTerrain = MountainRoadTerrainSampler.SampleHeight(
                first.Route,
                first.Plateau,
                entryXZ + entryForward * 0.05f);
            Assert.That(
                Mathf.Abs(beforeEntryTerrain - afterEntryTerrain),
                Is.LessThan(0.03f),
                "Terrain must stay continuous below the driving seam.");

            Vector3 firstHole = new Vector3(
                MountainRoadPlanner.HairpinRadius,
                2.5f,
                16f);
            Vector3 secondStart = first.Route.Sample(
                first.Route.SecondHairpinStart).Position;
            Vector3 secondHole = new Vector3(
                secondStart.x + MountainRoadPlanner.HairpinRadius,
                secondStart.y,
                secondStart.z);
            Assert.That(walkable.Contains(firstHole, 0.32f), Is.False);
            Assert.That(walkable.Contains(secondHole, 0.32f), Is.False);
            Vector3 constrained = walkable.Constrain(
                first.Route.Sample(first.Route.FirstHairpinStart - 1f).Position,
                firstHole,
                0.32f);
            Assert.That(walkable.Contains(constrained, 0.319f), Is.True);
            Assert.That(
                Vector2.Distance(
                    new Vector2(constrained.x, constrained.z),
                    new Vector2(firstHole.x, firstHole.z)),
                Is.GreaterThan(4f),
                "The traversal mask cut across the hairpin centre.");

            MountainRoadTerrainMeshes terrainMeshes =
                MountainRoadTerrainMeshFactory.Create(first);
            Mesh roadMesh = MountainRoadSurfaceMeshFactory.Create(first);
            Assert.That(roadMesh.vertexCount, Is.GreaterThan(150));
            Assert.That(
                roadMesh.vertices.Count(vertex =>
                    Vector3.Distance(vertex, entryLeft) < 0.001f),
                Is.EqualTo(1),
                "Road and plateau must share one left entry vertex.");
            Assert.That(
                roadMesh.vertices.Count(vertex =>
                    Vector3.Distance(vertex, entryRight) < 0.001f),
                Is.EqualTo(1),
                "Road and plateau must share one right entry vertex.");
            Assert.That(
                roadMesh.vertices.Count(vertex =>
                    Mathf.Abs(vertex.z - first.SpawnPosition.z) < 0.001f &&
                    Mathf.Abs(vertex.y) < 0.001f),
                Is.EqualTo(2),
                "The continuous road mesh must own both tunnel-floor edges " +
                "at spawn.");
            Assert.That(terrainMeshes.Soil.triangles.Length, Is.GreaterThan(0));
            Assert.That(terrainMeshes.Snow.triangles.Length, Is.GreaterThan(0));

            var parent = new GameObject("Mountain Road Test Parent");
            var cameraObject = new GameObject("Mountain Road Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            try
            {
                MountainRoadWorldResult result = MountainRoadWorldBuilder.Build(
                    parent.transform,
                    first,
                    camera);
                Assert.That(result.Root, Is.Not.Null);
                Assert.That(result.PhysicalRoot, Is.Not.Null);
                Assert.That(result.BackdropRoot, Is.Not.Null);
                Assert.That(result.WalkableArea, Is.Not.Null);
                string[] requiredSemanticIds = first.SoundAnchors
                    .Select(sound => sound.SourceObjectStableId)
                    .Concat(new[]
                    {
                        first.Terminal.Cafe.StableId,
                        MountainRoadCafeWorldBuilder.EntranceAnchorId,
                        MountainRoadCafeWorldBuilder.CounterAnchorId,
                        MountainRoadCafeWorldBuilder.GlassAnchorId,
                        MountainRoadCafeWorldBuilder.LonePatronAnchorId,
                        MountainRoadCafeWorldBuilder.PairFirstAnchorId,
                        MountainRoadCafeWorldBuilder.PairSecondAnchorId,
                        MountainRoadCafeWorldBuilder.AttendantAnchorId,
                        first.Terminal.Cableway.StableId
                    })
                    .Concat(first.Terminal.Cableway.Nodes.Select(
                        node => node.StableId))
                    .Concat(first.Terminal.Cableway.Cabins.Select(
                        cabin => cabin.StableId))
                    .Distinct()
                    .ToArray();
                for (int index = 0;
                     index < requiredSemanticIds.Length;
                     index++)
                {
                    string stableId = requiredSemanticIds[index];
                    Assert.That(
                        result.SemanticObjects.ContainsKey(stableId),
                        Is.True,
                        $"World semantic object '{stableId}' is missing.");
                    Assert.That(
                        result.SemanticObjects.TryGetValue(
                            stableId,
                            out Transform semantic) &&
                        semantic != null,
                        Is.True,
                        $"World semantic object '{stableId}' is null.");
                }
                Assert.That(camera.farClipPlane, Is.EqualTo(120f));

                int cafePhysicalColliders = result.Cafe.PhysicalRoot
                    .GetComponentsInChildren<Collider>(true)
                    .Count(collider => collider.enabled);
                int stationColliders = result.Cableway.StationRoot
                    .GetComponentsInChildren<Collider>(true)
                    .Count(collider => collider.enabled);
                int cablewayColliders = result.Cableway.Root
                    .GetComponentsInChildren<Collider>(true)
                    .Count(collider => collider.enabled);
                Assert.That(
                    cafePhysicalColliders,
                    Is.InRange(35, 60),
                    "The cafe should own a bounded physical shell and " +
                    "furniture set.");
                Assert.That(
                    stationColliders,
                    Is.InRange(8, 20),
                    "Only the lower station needs a compact physical set.");
                Assert.That(
                    cablewayColliders,
                    Is.EqualTo(stationColliders),
                    "Remote towers, cables and cabins must remain " +
                    "presentation-only.");
                Assert.That(
                    result.Cafe.Root
                        .GetComponentsInChildren<MeshRenderer>(true).Length,
                    Is.LessThan(280),
                    "The cafe tableau exceeded its broad-strokes MVP " +
                    "renderer budget.");
                Assert.That(
                    result.Cableway.Root
                        .GetComponentsInChildren<MeshRenderer>(true).Length,
                    Is.LessThan(150),
                    "The operating cableway exceeded its low-poly MVP " +
                    "renderer budget.");

                AssertCafeEntranceClear(result, first.Terminal.Cafe);
            }
            finally
            {
                Object.DestroyImmediate(parent);
                Object.DestroyImmediate(cameraObject);
                Object.DestroyImmediate(terrainMeshes.Soil);
                Object.DestroyImmediate(terrainMeshes.Snow);
                Object.DestroyImmediate(roadMesh);
            }
        }

        private static void AssertCafeEntranceClear(
            MountainRoadWorldResult world,
            MountainRoadCafePlan cafe)
        {
            const float playerRadius = 0.28f;
            Collider[] cafeColliders = world.Cafe.PhysicalRoot
                .GetComponentsInChildren<Collider>(true)
                .Where(collider => collider.enabled)
                .ToArray();
            Vector3 groundCenter = new Vector3(
                cafe.DoorCenter.x,
                cafe.FloorY,
                cafe.DoorCenter.z);
            Assert.That(
                Vector3.Distance(
                    world.Cafe.Entrance.position,
                    groundCenter + Vector3.up * 0.04f),
                Is.LessThan(0.001f));

            for (int alongStep = -4; alongStep <= 4; alongStep++)
            {
                Vector3 corridorGround = groundCenter +
                    cafe.DoorForward * (alongStep * 0.20f);
                Assert.That(
                    world.WalkableArea.Contains(
                        corridorGround,
                        playerRadius),
                    Is.True,
                    $"Cafe threshold leaves the walkable plateau at " +
                    $"step {alongStep}.");
                float[] sampleHeights = { 0.42f, 1.0f, 1.62f };
                for (int heightIndex = 0;
                     heightIndex < sampleHeights.Length;
                     heightIndex++)
                {
                    Vector3 sample = corridorGround +
                        Vector3.up * sampleHeights[heightIndex];
                    for (int colliderIndex = 0;
                         colliderIndex < cafeColliders.Length;
                         colliderIndex++)
                    {
                        Collider collider = cafeColliders[colliderIndex];
                        Assert.That(
                            collider.bounds.SqrDistance(sample),
                            Is.GreaterThanOrEqualTo(
                                playerRadius * playerRadius),
                            $"Cafe entrance is blocked by " +
                            $"'{collider.name}' at threshold step " +
                            $"{alongStep}.");
                    }
                }
            }
        }
    }
}
