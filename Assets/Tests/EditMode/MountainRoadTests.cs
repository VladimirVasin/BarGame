using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class MountainRoadTests
    {
        [Test]
        [Category("MountainRoad")]
        public void DefaultPlan_BuildsAbsurdHighTenHairpinBridgeWorld()
        {
            MountainRoadPlan first = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            MountainRoadPlan second = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);

            Assert.DoesNotThrow(() =>
                MountainRoadValidator.ValidateOrThrow(first));
            Assert.That(
                first.Route.Length,
                Is.EqualTo(MountainRoadPlanner.OutdoorRouteLength)
                    .Within(0.01f));
            Assert.That(
                first.Route.ElevationGain,
                Is.EqualTo(26.1f).Within(0.01f));
            Assert.That(MountainRoadPlanner.RoadWidth, Is.EqualTo(4.8f));
            Assert.That(MountainRoadPlanner.HairpinWidth, Is.EqualTo(6.4f));
            Assert.That(MountainRoadPlanner.HairpinRadius, Is.EqualTo(7.5f));
            Assert.That(
                first.Route.Hairpins,
                Has.Count.EqualTo(MountainRoadPlanner.HairpinCount));
            Assert.That(first.Bridge.Length, Is.InRange(45f, 55f));
            Assert.That(
                Mathf.Min(first.Bridge.Start.y, first.Bridge.End.y) -
                first.Bridge.GorgeFloorY,
                Is.GreaterThanOrEqualTo(25f));
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
                Is.InRange(238f, 240f));
            Assert.That(
                first.Forest.Min(item => item.Height),
                Is.GreaterThanOrEqualTo(7f));
            Assert.That(
                first.Forest.Max(item => item.Height),
                Is.GreaterThan(16f));
            Assert.That(
                first.Forest.Count(item =>
                    item.Layer == MountainRoadForestLayer.Physical),
                Is.EqualTo(92));
            Assert.That(
                first.Forest.Count(item =>
                    item.Layer == MountainRoadForestLayer.Mid),
                Is.EqualTo(142));
            Assert.That(
                first.Forest.Count(item =>
                    item.Layer == MountainRoadForestLayer.Far),
                Is.EqualTo(186));
            foreach (MountainRoadForestLayer layer in
                     new[]
                     {
                         MountainRoadForestLayer.Physical,
                         MountainRoadForestLayer.Mid,
                         MountainRoadForestLayer.Far
                     })
            {
                Assert.That(
                    first.Forest
                        .Where(item => item.Layer == layer)
                        .Select(item => item.PaletteIndex)
                        .Distinct(),
                    Is.EquivalentTo(new[] { 0, 1, 2 }),
                    $"{layer} forest lost its three silhouette variants.");
            }

            Assert.That(
                first.Forest.All(tree =>
                    !MountainRoadCompositionRules.IsReservedForestOpening(
                        first.Route,
                        first.Plateau,
                        tree.Layer,
                        new Vector2(tree.Position.x, tree.Position.z),
                        tree.CrownRadius)),
                Is.True,
                "Near forest filled a hairpin, bridge or terminal reveal.");

            for (int index = 0;
                 index < first.Route.Hairpins.Count;
                 index++)
            {
                MountainRoadHairpinDescriptor hairpin =
                    first.Route.Hairpins[index];
                MountainRoadMiscDescriptor rail = first.Misc.Single(item =>
                    item.StableId == $"misc-guardrail-{index}");
                MountainRoadRouteSample railSample = first.Route.Sample(
                    hairpin.StartDistance + 4.2f);
                Vector3 offset = rail.Position - railSample.Position;
                Assert.That(
                    Mathf.Sign(Vector3.Dot(offset, railSample.Right)),
                    Is.EqualTo(-hairpin.TurnSide),
                    $"Hairpin {index} guardrail stands on the inner bank.");

                for (int across = -1; across <= 1; across += 2)
                {
                    for (int along = -1; along <= 1; along += 2)
                    {
                        Vector3 corner = rail.Position + rail.Rotation *
                            new Vector3(
                                across * rail.Size.x * 0.5f,
                                0f,
                                along * rail.Size.z * 0.5f);
                        MountainRoadTerrainSampler.FindClosest(
                            first.Route,
                            new Vector2(corner.x, corner.z),
                            out float distance,
                            out _,
                            out _,
                            out float halfWidth);
                        Assert.That(
                            distance,
                            Is.GreaterThanOrEqualTo(halfWidth + 0.05f),
                            $"Hairpin {index} guardrail enters the road.");
                    }
                }
            }

            MountainRoadMiscDescriptor abandonedChair = first.Misc.Single(
                item => item.StableId == "misc-abandoned-chair");
            MountainRoadRouteSample chairShelf = first.Route.Sample(
                MountainRoadCompositionRules.AbandonedChairDistance(
                    first.Route));
            Assert.That(
                chairShelf.Section,
                Is.EqualTo(MountainRoadRouteSection.UpperClimb));
            Assert.That(
                Vector3.Dot(
                    abandonedChair.Position - chairShelf.Position,
                    chairShelf.Right),
                Is.LessThan(-3.5f),
                "The abandoned chair fell back into the bridge gorge.");
            foreach (MountainRoadMiscDescriptor natural in first.Misc.Where(
                         item => MountainRoadCompositionRules
                             .IsNaturalMiscKind(item.Kind)))
            {
                if (natural.Kind == MountainRoadMiscKind.DeadTree)
                {
                    Assert.That(
                        MountainRoadCompositionRules.FootprintRadius(natural),
                        Is.GreaterThanOrEqualTo(
                            natural.Size.y *
                            MountainRoadCompositionRules
                                .DeadTreeFootprintRadiusPerHeight));
                }

                Assert.That(
                    first.Misc
                        .Where(item => item.StableId != natural.StableId)
                        .All(item => MountainRoadCompositionRules
                            .HaveMiscFootprintClearance(natural, item)),
                    Is.True,
                    $"{natural.StableId} overlaps roadside composition.");
            }
            // Five on the road and four on the summit. The rule has
            // not changed - every one still has something you can walk
            // up to and look at - only the summit now has furniture of
            // its own for them to belong to.
            Assert.That(first.SoundAnchors, Has.Count.EqualTo(9));
            Assert.That(
                first.SoundAnchors.All(sound =>
                    first.Misc.Any(item =>
                        item.StableId == sound.SourceObjectStableId &&
                        item.Position == sound.Position) ||
                    OwnedByTheSite(first.Terminal.Site, sound)),
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

            float gorgeFloor = MountainRoadTerrainSampler.SampleHeight(
                first.Route,
                first.Plateau,
                new Vector2(
                    first.Bridge.Center.x,
                    first.Bridge.Center.z));
            Assert.That(
                gorgeFloor,
                Is.EqualTo(first.Bridge.GorgeFloorY).Within(0.4f),
                "The terrain must open into a real gorge below the bridge.");
            Assert.That(
                first.Bridge.Center.y - gorgeFloor,
                Is.GreaterThanOrEqualTo(25f),
                "The bridge no longer communicates a high exposed drop.");

            for (int index = 0;
                 index < first.Route.Hairpins.Count;
                 index++)
            {
                MountainRoadHairpinDescriptor hairpin =
                    first.Route.Hairpins[index];
                Vector3 hole = new Vector3(
                    hairpin.CenterXZ.x,
                    hairpin.ApexPosition.y,
                    hairpin.CenterXZ.y);
                Assert.That(
                    walkable.Contains(hole, 0.32f),
                    Is.False,
                    $"Hairpin {index} centre became a route shortcut.");
            }

            MountainRoadHairpinDescriptor firstHairpin =
                first.Route.Hairpins[0];
            Vector3 firstHole = new Vector3(
                firstHairpin.CenterXZ.x,
                firstHairpin.ApexPosition.y,
                firstHairpin.CenterXZ.y);
            Vector3 constrained = walkable.Constrain(
                first.Route.Sample(firstHairpin.StartDistance - 1f).Position,
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
            Assert.That(roadMesh.vertexCount, Is.GreaterThan(2000));
            Vector3 tunnelRight = Vector3.Cross(
                Vector3.up,
                first.Tunnel.OutwardAxis).normalized;
            Assert.That(
                Vector3.Dot(roadMesh.normals[2], -tunnelRight),
                Is.GreaterThan(0.95f),
                "The left road kerb is wound inward and will be culled.");
            Assert.That(
                Vector3.Dot(roadMesh.normals[3], tunnelRight),
                Is.GreaterThan(0.95f),
                "The right road kerb is wound inward and will be culled.");
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
            AssertRidgesGroundedAndSeparated(first);

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
                Assert.That(result.TerminalApron, Is.Not.Null);
                Assert.That(
                    result.TerminalApron.transform.parent,
                    Is.EqualTo(result.PhysicalRoot.transform));
                Assert.That(
                    result.TerminalApron
                        .GetComponentsInChildren<Collider>(true),
                    Is.Empty,
                    "The visible apron must reuse the continuous road and " +
                    "plateau collision instead of adding a second skin.");
                Mesh apronMesh = result.TerminalApron
                    .GetComponent<MeshFilter>().sharedMesh;
                MountainRoadVehicleApronPlan apron =
                    first.Terminal.VehicleApron;
                Assert.That(apronMesh.vertexCount, Is.GreaterThan(24));
                Assert.That(
                    apronMesh.vertices.Min(vertex => Vector3.Dot(
                        vertex - apron.EntryCenter,
                        apron.Forward)),
                    Is.LessThanOrEqualTo(
                        -MountainRoadSurfaceMeshFactory
                            .TerminalApronEntryOverlap + 0.001f),
                    "The visible apron must overlap the road seam.");
                Assert.That(
                    apronMesh.vertices.Max(vertex => Vector3.Dot(
                        vertex - apron.Center,
                        apron.Forward)),
                    Is.GreaterThan(apron.TurningRadius - 0.08f),
                    "The paved terminal marking must expose the full " +
                    "turning pocket.");
                Assert.That(
                    apronMesh.vertices.All(vertex => Mathf.Abs(
                        vertex.y - apron.Center.y -
                        MountainRoadSurfaceMeshFactory
                            .TerminalApronSurfaceOffset) < 0.001f),
                    Is.True,
                    "The apron overlay must stay just above the shared " +
                    "driving surface.");
                Assert.That(result.Bridge, Is.Not.Null);
                Assert.That(
                    result.Bridge.Root.transform.parent,
                    Is.EqualTo(result.PhysicalRoot.transform));
                Assert.That(
                    result.Bridge.Root.transform.position,
                    Is.EqualTo(first.Bridge.Center));
                Assert.That(result.Bridge.Piers, Has.Count.EqualTo(2));
                Assert.That(result.Bridge.Rails, Has.Count.EqualTo(2));
                Assert.That(
                    result.Bridge.ActiveColliderCount,
                    Is.EqualTo(
                        MountainRoadBridgeValidator
                            .MaximumActiveColliderCount));
                Assert.That(
                    result.Bridge.RendererCount,
                    Is.LessThanOrEqualTo(
                        MountainRoadBridgeValidator.MaximumRendererCount));

                // The deck is one oriented box in a combined batch, so its
                // slope and its offset live in the mesh rather than in the
                // transform. Both are read back off the vertices.
                Transform deck = result.Bridge.StructuralDeck.transform;
                Vector3 bridgeSpan = first.Bridge.End - first.Bridge.Start;
                Vector3 deckUp = Quaternion.LookRotation(
                    bridgeSpan.normalized,
                    Vector3.up) * Vector3.up;
                Vector3[] deckCorners = result.Bridge.StructuralDeck
                    .GetComponent<MeshFilter>()
                    .sharedMesh
                    .vertices
                    .Select(vertex => deck.TransformPoint(vertex))
                    .ToArray();
                Assert.That(
                    deckCorners,
                    Has.Length.EqualTo(24),
                    "The structural deck must stay one batched slab.");
                Assert.That(
                    deckCorners.Max(corner => Vector3.Dot(
                        corner - first.Bridge.Center,
                        bridgeSpan.normalized)),
                    Is.EqualTo(bridgeSpan.magnitude * 0.5f + 0.06f)
                        .Within(0.002f),
                    "The structural deck must follow the climbing road.");
                Assert.That(
                    deckCorners.Max(corner =>
                        Vector3.Dot(corner, deckUp)),
                    Is.EqualTo(Vector3.Dot(
                        first.Bridge.Center - deckUp *
                        MountainRoadBridgeWorldBuilder
                            .StructuralDeckSurfaceClearance,
                        deckUp)).Within(0.002f),
                    "The bridge deck must support the asphalt without a " +
                    "coplanar surface.");
                for (int index = 0;
                     index < result.Bridge.Piers.Count;
                     index++)
                {
                    Collider pierCollider = result.Bridge.Piers[index]
                        .GetComponentsInChildren<Collider>(true)
                        .Single(collider => collider.enabled);
                    Vector3 pierPosition =
                        result.Bridge.Piers[index].position;
                    float pierTerrain = MountainRoadTerrainSampler.SampleHeight(
                        first.Route,
                        first.Plateau,
                        new Vector2(pierPosition.x, pierPosition.z));
                    Assert.That(
                        pierCollider.bounds.min.y,
                        Is.LessThanOrEqualTo(pierTerrain - 0.05f),
                        $"Bridge pier {index} must be embedded in the " +
                        "uneven gorge floor.");
                }

                MeshFilter roadFilter =
                    result.RoadSurface.GetComponent<MeshFilter>();
                MeshCollider roadCollider =
                    result.RoadSurface.GetComponent<MeshCollider>();
                Assert.That(roadCollider, Is.Not.Null);
                Assert.That(
                    roadCollider.sharedMesh,
                    Is.SameAs(roadFilter.sharedMesh),
                    "The visible road and its collider must share one mesh.");
                MeshFilter[] terrainFilters = result.TerrainRoot
                    .GetComponentsInChildren<MeshFilter>(true);
                for (int index = 0;
                     index < terrainFilters.Length;
                     index++)
                {
                    MeshCollider terrainCollider =
                        terrainFilters[index].GetComponent<MeshCollider>();
                    Assert.That(terrainCollider, Is.Not.Null);
                    Assert.That(
                        terrainCollider.sharedMesh,
                        Is.SameAs(terrainFilters[index].sharedMesh),
                        "Visible terrain and collision must not diverge.");
                }

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
                    .Concat(result.Bridge.SemanticObjects.Keys)
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
                Assert.That(
                    camera.farClipPlane,
                    Is.EqualTo(RuntimeSceneSetup.MountainRoadFarClipPlane));

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
                    Is.EqualTo(
                        MountainRoadCafeCollisionWorldBuilder
                            .ExpectedColliderCount),
                    "The authored cafe must use only its exact plan-owned " +
                    "shell, counter, service and seven-stool obstacles.");
                // Exactly the station's own obstacle plan, and nothing else.
                // A magic range used to stand here; it read `20` as a ceiling
                // and said nothing about WHICH solids those were - which is
                // how a drive hut came to be standing across the boarding
                // lane with the suite green. The list is now the contract:
                // the builder places these and the site validator floods with
                // them, so a collider that is in the world and not in the
                // plan is exactly the kind of thing nothing would notice.
                Assert.That(
                    stationColliders,
                    Is.EqualTo(
                        MountainCablewayObstaclePlan.Create(
                            first.Terminal.Cableway,
                            MountainCablewayStationKind.Drive).Count),
                    "The station's solids must be its obstacle plan.");
                Assert.That(
                    stationColliders,
                    Is.InRange(8, 30),
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
                // A `230 m` line now: nine towers and eight cabins, each a
                // handful of boxes, so the budget follows the plan rather
                // than the old `58 m` line's `150`.
                int cablewayRenderers = result.Cableway.Root
                    .GetComponentsInChildren<MeshRenderer>(true).Length;
                Assert.That(
                    cablewayRenderers,
                    Is.LessThan(
                        60 +
                        (22 * result.Cableway.Supports.Count) +
                        (12 * result.Cableway.Cabins.Count)),
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

        [Test]
        [Category("MountainRoad")]
        public void TunnelToCafe_IsOneUnbrokenDrivableSurface()
        {
            const float carHalfWidth = 1.05f;
            const float minimumRoadBed = 0.1f;
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            var walkable = new MountainRoadWalkableArea(plan);

            // The ribbon mesh is only half the road: the terrain carries the
            // collision the car actually rests on and drives past. Wherever
            // the ground rises above the asphalt the road is buried, and no
            // amount of correct ribbon geometry gets a car through it.
            for (int index = 0; index < plan.Route.Samples.Count; index++)
            {
                MountainRoadRouteSample sample = plan.Route.Samples[index];
                for (int lane = -4; lane <= 4; lane++)
                {
                    Vector3 probe = sample.Position +
                        sample.Right * (sample.Width * 0.5f * (lane / 4f));
                    float ground = MountainRoadTerrainSampler.SampleHeight(
                        plan.Route,
                        plan.Plateau,
                        new Vector2(probe.x, probe.z));
                    Assert.That(
                        ground,
                        Is.LessThanOrEqualTo(
                            sample.Position.y - minimumRoadBed),
                        $"The road is buried at {sample.StableId} " +
                        $"({sample.Distance:0.0} m, {sample.Section}) near " +
                        $"X {probe.x:0.0} Z {probe.z:0.0}: the ground sits " +
                        $"at {ground:0.00} and the surface at " +
                        $"{sample.Position.y:0.00}.");
                }
            }

            // The reported break: the terminal pad used to reach back over
            // the outer arc of the last switchback.
            MountainRoadHairpinDescriptor last =
                plan.Route.Hairpins[plan.Route.Hairpins.Count - 2];
            Assert.That(
                plan.Plateau.Contains(new Vector2(
                    last.ApexPosition.x,
                    last.ApexPosition.z)),
                Is.False,
                "The terminal pad swallowed the last switchback apex.");
            for (int index = 0; index < plan.Route.Samples.Count; index++)
            {
                MountainRoadRouteSample sample = plan.Route.Samples[index];
                if (sample.Section ==
                    MountainRoadRouteSection.UpperApproach)
                {
                    continue;
                }

                Assert.That(
                    plan.Plateau.Contains(new Vector2(
                        sample.Position.x,
                        sample.Position.z)),
                    Is.False,
                    $"The terminal pad covers {sample.StableId}, which is " +
                    "climbing road and not its own approach.");
            }

            // ...and the whole drive, tunnel mouth to cafe door.
            for (float distance = 0f;
                 distance <= plan.Route.Length;
                 distance += 1f)
            {
                Assert.That(
                    walkable.Contains(
                        plan.Route.Sample(distance).Position,
                        carHalfWidth),
                    Is.True,
                    $"The car corridor breaks at {distance:0.0} m.");
            }

            MountainRoadCafePlan cafe = plan.Terminal.Cafe;
            Vector3 apronEntry = plan.Terminal.VehicleApron.EntryCenter;
            Vector3 doorApproach = new Vector3(
                cafe.DoorCenter.x,
                cafe.FloorY,
                cafe.DoorCenter.z) - cafe.DoorForward * carHalfWidth;
            for (int step = 0; step <= 32; step++)
            {
                Vector3 point = Vector3.Lerp(
                    apronEntry,
                    doorApproach,
                    step / 32f);
                Assert.That(
                    walkable.Contains(point, carHalfWidth),
                    Is.True,
                    $"The drive from the apron to the cafe door breaks at " +
                    $"step {step}.");
                float ground = MountainRoadTerrainSampler.SampleHeight(
                    plan.Route,
                    plan.Plateau,
                    new Vector2(point.x, point.z));
                Assert.That(
                    ground,
                    Is.LessThanOrEqualTo(cafe.FloorY - minimumRoadBed),
                    $"The terminal pad is buried at step {step}.");
            }
        }

        private static void AssertRidgesGroundedAndSeparated(
            MountainRoadPlan plan)
        {
            for (int ridgeIndex = 0;
                 ridgeIndex < plan.Ridges.Count;
                 ridgeIndex++)
            {
                MountainRoadRidgeDescriptor ridge = plan.Ridges[ridgeIndex];
                float expectedBase = MountainRoadPlanner.CalculateRidgeBaseY(
                    plan.Route,
                    plan.Plateau,
                    new Vector2(ridge.Center.x, ridge.Center.z),
                    ridge.Size,
                    ridge.YawDegrees);
                Assert.That(
                    ridge.Center.y - ridge.Size.y * 0.5f,
                    Is.EqualTo(expectedBase).Within(0.001f),
                    $"{ridge.StableId} floats above its local terrain.");

                for (int routeIndex = 0;
                     routeIndex < plan.Route.Samples.Count;
                     routeIndex++)
                {
                    MountainRoadRouteSample sample =
                        plan.Route.Samples[routeIndex];
                    Assert.That(
                        MountainRoadRidgeGeometry.DistanceToFootprint(
                            new Vector2(
                                sample.Position.x,
                                sample.Position.z),
                            ridge),
                        Is.GreaterThanOrEqualTo(
                            sample.Width * 0.5f +
                            MountainRoadPlanner.RidgeRoadClearance - 0.03f),
                        $"{ridge.StableId} crosses {sample.StableId}.");
                }

                for (int treeIndex = 0;
                     treeIndex < plan.Forest.Count;
                     treeIndex++)
                {
                    MountainRoadForestDescriptor tree =
                        plan.Forest[treeIndex];
                    Assert.That(
                        MountainRoadRidgeGeometry.DistanceToFootprint(
                            new Vector2(tree.Position.x, tree.Position.z),
                            ridge),
                        Is.GreaterThanOrEqualTo(
                            tree.CrownRadius +
                            MountainRoadPlanner.RidgeTreeClearance - 0.03f),
                        $"{ridge.StableId} clips {tree.StableId}.");
                }
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
                    groundCenter),
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

        /// <summary>
        /// A summit sound hangs on a batched site part or on one of
        /// the two cloths, neither of which is roadside misc.
        /// </summary>
        private static bool OwnedByTheSite(
            MountainRoadTerminalSitePlan site,
            MountainRoadSoundAnchor sound)
        {
            if (site == null)
            {
                return false;
            }

            if (site.TryGetPart(
                    sound.SourceObjectStableId,
                    out MountainRoadSitePartDescriptor part))
            {
                return part.Center == sound.Position;
            }

            for (int index = 0; index < site.Cloth.Count; index++)
            {
                if (site.Cloth[index].StableId ==
                    sound.SourceObjectStableId)
                {
                    return site.Cloth[index].Anchor == sound.Position;
                }
            }

            return false;
        }
    }
}
