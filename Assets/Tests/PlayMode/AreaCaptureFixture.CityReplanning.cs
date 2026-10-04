using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Replanned district streets through the production camera, light and fog.")]
        public IEnumerator CityReplanning()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeFromWake(), Is.True);
            GameSessionState.AdvanceGameTime((float)(360d / GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            var issues = new List<string>();
            System.Action restoreWalker = null;
            try
            {
                yield return Capture(SceneIds.City,
                    () =>
                    {
                        city = Object.FindAnyObjectByType<CityGameRoot>();
                        return city != null && city.Layout != null && city.World != null &&
                            city.BusPlan != null ? city : null;
                    }, () => CityReplanningShots(city, issues, out restoreWalker));
            }
            finally { restoreWalker?.Invoke(); }
            Assert.That(issues, Is.Empty, string.Join("\n", issues));
        }

        private static Shot[] CityReplanningShots(CityGameRoot city, List<string> issues, out System.Action restoreWalker)
        {
            CityLayout layout = city.Layout;
            Assert.That(layout.SpatialPlan.IsUniform, Is.False);
            LogReplanningRouteLengths(city);
            var shots = new List<Shot>();
            CityStreetSurfacePlan streetPlan = CityStreetSurfacePlanner.Create(layout);
            RoadWalkableArea pedestrianArea = CityPedestrianPlanner.CreateWalkableArea(city.PedestrianPlan);
            CityRoadJunction oblique = layout.RoadGeometry.ObliqueJunction;
            Assert.That(oblique, Is.Not.Null);
            foreach (CityRoadPath pavement in oblique.SidewalkPaths)
                for (float distance = .1f; distance < pavement.Length; distance += .5f)
                {
                    Vector2 point = pavement.SampleDistance(distance).Position;
                    float top = layout.ElevationPlan.GetNodeElevation(oblique.Node) + CityStreetSurfacePlanner.SidewalkTop;
                    Vector3 probe = new Vector3(point.x, top + .5f, point.y);
                    if (!Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 1f, ~0, QueryTriggerInteraction.Ignore) ||
                        Mathf.Abs(hit.point.y - top) > .025f)
                        issues.Add($"Oblique junction pavement at {point:F4}, expected={top:F4}");
                    Vector3 standing = new Vector3(point.x, top, point.y);
                    if (!city.World.WalkableArea.Contains(standing, .35f)) issues.Add($"Oblique hero navigation at {point:F4}");
                    if (!pedestrianArea.Contains(standing, .35f)) issues.Add($"Oblique pedestrian navigation at {point:F4}");
                }
            for (int index = 0; index < layout.RoadGeometry.CurvedEdges.Count; index++)
            {
                RoadEdge edge = layout.RoadGeometry.CurvedEdges[index];
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                for (float s = 6.5f; s < path.Length - 6f; s += 1f)
                {
                    CityRoadSample sample = path.SampleDistance(s);
                    float datum = layout.ElevationPlan.SampleRoadDatum(edge, s / path.Length);
                    foreach (float offset in new[] { 0f, -3.5f, 3.5f })
                    {
                        Vector2 point = sample.Position + sample.Right * offset;
                        if (offset != 0 && streetPlan.CurvedSidewalkRibbons.Any(ribbon =>
                            ribbon.Edge.Equals(edge) && NearPavementEnd(ribbon, point))) continue;
                        Vector3 probe = new Vector3(point.x, datum + 2f, point.y);
                        Assert.That(Physics.Raycast(probe, Vector3.down, out RaycastHit hit, 4f,
                            ~0, QueryTriggerInteraction.Ignore), Is.True, "Curved paving must have a physical surface.");
                        bool pavement = offset != 0 && streetPlan.CurvedSidewalkPolygons.Any(polygon =>
                            CityRoadPolygon.Contains(polygon, point));
                        float top = datum + (pavement ? CityStreetSurfacePlanner.SidewalkTop : CityStreetSurfacePlanner.RoadTop);
                        if (Mathf.Abs(hit.point.y - top) > .025f)
                            issues.Add($"Paving {edge.A}->{edge.B} s={s:F2} offset={offset:F1} point={point:F4} pavement={pavement} expected={top:F4} actual={hit.point.y:F4} collider={hit.collider.name}");
                        if (!city.World.WalkableArea.Contains(hit.point, .35f))
                            issues.Add($"Hero navigation at {point:F4}");
                        if (pavement && !pedestrianArea.Contains(hit.point, .35f))
                            issues.Add($"Pedestrian navigation at {point:F4}");
                    }
                }
                CityRoadSample eyeSample = path.SampleDistance(path.Length * .3f);
                Vector3 eye = ReplanningStreetEye(layout, new Vector3(eyeSample.Position.x, 0, eyeSample.Position.y));
                CityRoadSample targetSample = path.SampleDistance(Mathf.Min(path.Length - 2f, path.Length * .3f + 16f));
                shots.Add(Shot.At($"replanning-streetfront-street-{index + 1:00}-curve", eye,
                    new Vector3(targetSample.Position.x, eye.y - .7f, targetSample.Position.y), 78f));
            }
            foreach (CityPedestrianLink link in city.PedestrianPlan.Links.Where(link => link.Path != null))
                for (float distance = .5f; distance < link.Path.Length; distance += 1f)
                {
                    Vector2 point = link.Path.SampleDistance(distance).Position;
                    float top = link.PathHeightSampler(point);
                    Vector3 probe = new Vector3(point.x, top + .5f, point.y);
                    RaycastHit[] hits = Physics.RaycastAll(probe, Vector3.down, 1f, ~0, QueryTriggerInteraction.Ignore);
                    if (!hits.Any(hit => hit.collider is MeshCollider && Mathf.Abs(hit.point.y - top) < .025f))
                        issues.Add($"Pedestrian height {link.Id} at {point:F4}, expected={top:F4}, hits={string.Join(",", hits.Select(hit => hit.collider.name + ":" + hit.point.y.ToString("F4")))}");
                }
            RoadEdge branch = new RoadEdge(new Vector2Int(1, 8), new Vector2Int(2, 8));
            CityRoadPath branchPath = layout.RoadGeometry.Get(branch);
            CityRoadSample middle = branchPath.SampleDistance(branchPath.Length * .5f);
            Vector2 released = middle.Position - middle.Right * 5.2f;
            float branchDatum = layout.ElevationPlan.SampleRoadDatum(branch, .5f);
            Vector3 releasedProbe = new Vector3(released.x, branchDatum + 2f, released.y);
            Assert.That(Physics.Raycast(releasedProbe, Vector3.down, out RaycastHit releasedGround, 4f,
                ~0, QueryTriggerInteraction.Ignore), Is.True, "Released straight-road strip must be filled with ground.");
            Assert.That(releasedGround.point.y, Is.LessThan(branchDatum + .04f), "The obsolete straight road must not survive below the new bend.");
            Assert.That(city.World.WalkableArea.Contains(releasedGround.point, .35f), Is.True);
            Vector3 junction = layout.GetNodeWorldPosition(new Vector2Int(1, 8));
            Vector3 junctionEye = ReplanningStreetEye(layout, junction + Vector3.right * 5f);
            shots.Add(Shot.At("replanning-streetfront-street-04-t-junction", junctionEye,
                junction + Vector3.left * 6f + Vector3.up * (EyeHeight - .6f), 102f));
            CityRoadSample branchEye = branchPath.SampleDistance(11f);
            Vector3 obliqueEye = ReplanningStreetEye(layout,
                new Vector3(branchEye.Position.x, 0f, branchEye.Position.y));
            shots.Add(Shot.At("replanning-streetfront-street-05-approach", obliqueEye,
                junction + Vector3.forward * 4f + Vector3.up * (EyeHeight - .6f), 90f));
            VerifyReplanningStreetfronts(city, streetPlan, pedestrianArea, shots, issues);
            VerifyReplanningCourtyards(city, streetPlan, shots, issues);
            var walkerDemo = new ReplanningCourtyardWalkerDemo(city, streetPlan, issues);
            walkerDemo.AddShots(shots);
            walkerDemo.AddOffsetPairShots(shots, layout.CourtyardBlocks.Single(block => block.Kind == CityCourtyardBlockKind.OffsetPair));
            restoreWalker = walkerDemo.Restore;
            Debug.Log($"OldTown pilot: {layout.RoadGeometry.CurvedEdges.Count} shared road paths; physical probe issues={issues.Count}.");
            return shots.ToArray();
        }

        private static void VerifyReplanningStreetfronts(CityGameRoot city, CityStreetSurfacePlan streetPlan,
            RoadWalkableArea pedestrianArea, List<Shot> shots, List<string> issues)
        {
            CityLayout layout = city.Layout;
            CityBuildingAssetProvider provider = CityBuildingAssetProvider.LoadOrThrow();
            CityBuildingAssetRegistry[] models = city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>();
            int[] variants = { 0, 1, 0, 1 };
            float[] setbacks = { 2.4f, 1.15f, 1.7f, 1.2f };
            var selected = new List<BuildingLot>();
            for (int x = 0; x < variants.Length; x++)
            {
                Vector2Int cell = new Vector2Int(x, 11);
                BuildingLot lot = layout.BuildingLots.Single(candidate => candidate.Cell == cell);
                if (!lot.IsOrdinaryBuilding || lot.District != CityDistrictKind.OldTown ||
                    layout.PrimaryLandmarkCells.Values.Contains(cell) || layout.RoadGeometry.IsReplannedCell(cell)) continue;
                RoadEdge edge = RoadEdge.ForCellFrontage(cell, Vector2Int.up);
                if (!layout.HasRoad(edge) || layout.GetPathKind(edge) != CityPathKind.Street) continue;
                selected.Add(lot);
                Assert.That(lot.BuildingVariant, Is.EqualTo(variants[x]));
                Assert.That(lot.FrontageDirection, Is.EqualTo(Vector2Int.up));
                CityBuildingAssetRegistry model = models.Single(candidate => candidate.transform.parent.name ==
                    $"Building {cell.x}-{cell.y}");
                CityBuildingAssetRegistry source = provider.GetPrefabOrThrow(lot.District, variants[x])
                    .GetComponent<CityBuildingAssetRegistry>();
                Assert.That(model.StableId, Is.EqualTo(source.StableId));
                Assert.That(Vector3.Distance(model.transform.lossyScale, Vector3.one), Is.LessThan(.002f),
                    $"Streetfront {cell} must retain the imported metre scale.");
                Vector3 expectedAnchor = lot.DoorPosition + Vector3.up * CityFacadeGrid.MassBaseElevation;
                Assert.That(Vector3.Distance(model.FrontAnchor.position, expectedAnchor), Is.LessThan(.002f));
                Assert.That(Vector3.Angle(model.FrontAnchor.forward, Vector3.forward), Is.LessThan(.01f));
                CityRoadPath path = layout.RoadGeometry.Get(edge);
                CityRoadProjection front = path.Project(new Vector2(model.FrontAnchor.position.x, model.FrontAnchor.position.z));
                Assert.That(Mathf.Sqrt(front.DistanceSquared) - layout.GetTravelWidth(edge) * .5f,
                    Is.EqualTo(setbacks[x]).Within(.002f), $"Actual imported facade setback at {cell}.");

                // Read placed mesh vertices, including the import-root unit factor.
                // Registry bounds alone cannot detect a stretched or misplaced model.
                Bounds measured = default;
                bool firstVertex = true;
                foreach (CityBuildingPartBinding part in model.Parts)
                {
                    MeshFilter filter = part.Renderer.GetComponent<MeshFilter>();
                    Assert.That(filter, Is.Not.Null);
                    Assert.That(filter.sharedMesh, Is.Not.Null);
                    PlayerScarfCollisionWorld.ReadMesh(filter.sharedMesh, out Vector3[] vertices, out _);
                    foreach (Vector3 vertex in vertices)
                    {
                        Vector3 local = model.transform.InverseTransformPoint(filter.transform.TransformPoint(vertex));
                        if (firstVertex) { measured = new Bounds(local, Vector3.zero); firstVertex = false; }
                        else measured.Encapsulate(local);
                    }
                }
                Assert.That(firstVertex, Is.False);
                Assert.That(Vector3.Distance(measured.center, source.LocalBounds.center), Is.LessThan(.005f));
                Assert.That(Vector3.Distance(measured.size, source.LocalBounds.size), Is.LessThan(.005f));
                Assert.That(measured.size.y, Is.EqualTo(lot.Height).Within(.005f));

                BoxCollider body = model.transform.parent.GetComponentsInChildren<BoxCollider>().Single();
                Assert.That(body.isTrigger, Is.False);
                Vector2[] planned = lot.CreateCollisionPolygons().Single();
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 physical = body.transform.TransformPoint(body.center + new Vector3(
                        (corner & 1) == 0 ? -body.size.x * .5f : body.size.x * .5f, 0f,
                        (corner & 2) == 0 ? -body.size.z * .5f : body.size.z * .5f));
                    var point = new Vector2(physical.x, physical.z);
                    Assert.That(planned.Min(candidate => Vector2.Distance(candidate, point)), Is.LessThan(.002f),
                        $"Streetfront {cell} physical body disagrees with its authored footprint.");
                }
                // Compact retains the legacy full-height logical envelope;
                // the long house's authored solid ends below its 2.4 m roof.
                float collisionHeight = variants[x] == 0 ? lot.Height : CityBuildingAssetProvider
                    .GetExpectedCollisionBounds(lot.District, lot.BuildingVariant).Max(bounds => bounds.max.y);
                Assert.That(body.bounds.max.y, Is.EqualTo(lot.Center.y + collisionHeight + CityFacadeGrid.MassBaseElevation).Within(.002f));
                Debug.Log($"Streetfront physical model {cell}: id={model.StableId}, measured={measured.size:F3}, " +
                    $"anchor={model.FrontAnchor.position:F3}, setback={Mathf.Sqrt(front.DistanceSquared) - layout.GetTravelWidth(edge) * .5f:F3}.");

                Vector2 first = path.SampleDistance(5.5f).Position - Vector2.up * 3.5f;
                Vector2 last = path.SampleDistance(path.Length - 5.5f).Position - Vector2.up * 3.5f;
                VerifyReplanningWalkingPath($"North streetfront {cell}", new CityRoadPath(new[] { first, last }),
                    city, streetPlan, pedestrianArea, issues);
                bool reverse = x >= 2;
                Vector2 cameraPoint = path.SampleDistance(path.Length * (reverse ? .8f : .2f)).Position - Vector2.up * 3.5f;
                Vector3 eye = ReplanningCourtyardEye(layout, streetPlan, cameraPoint);
                Vector3 target = eye + Vector3.right * (reverse ? -18f : 18f) - Vector3.forward * 3f - Vector3.up * .45f;
                shots.Add(Shot.At($"replanning-streetfront-north-front-{x + 1:00}-along-row", eye, target, 84f));
            }
            Assert.That(selected.Count, Is.GreaterThanOrEqualTo(3), "The physical production row must contain at least three ordinary fronts.");
            Assert.That(selected.Select(lot => lot.BuildingVariant).Distinct(), Is.EquivalentTo(new[] { 0, 1 }));
        }

        private static void VerifyReplanningCourtyards(CityGameRoot city,
            CityStreetSurfacePlan streetPlan, List<Shot> shots, List<string> issues)
        {
            CityLayout layout = city.Layout;
            Assert.That(layout.CourtyardBlocks.Count, Is.GreaterThanOrEqualTo(5));
            Assert.That(layout.CourtyardBlocks.Count(block => block.Kind == CityCourtyardBlockKind.LRecess), Is.EqualTo(4));
            Assert.That(layout.BuildingMasses.Count, Is.GreaterThanOrEqualTo(147));
            var mapGround = new CityMapCityTeleportGround(layout);
            RoadWalkableArea pedestrianArea = CityPedestrianPlanner.CreateWalkableArea(city.PedestrianPlan);
            Physics.SyncTransforms();
            foreach (CityCourtyardBlock block in layout.CourtyardBlocks)
            {
                VerifyReplanningWalkingPath($"Courtyard {block.Cell}", block.Route, city, streetPlan, pedestrianArea, issues);
                foreach (Vector3 arrival in new[] { block.CourtCenter, block.PassageCenter })
                {
                    Vector2 point = new Vector2(arrival.x, arrival.z);
                    if (!mapGround.TryResolveStandingPosition(point, out Vector3 standing) ||
                        (new Vector2(standing.x, standing.z) - point).sqrMagnitude > .0001f ||
                        !TryReplanningSurfaceTop(layout, streetPlan, point, out float top) ||
                        Mathf.Abs(standing.y - top - PlayerFactory.GroundedRootOffset) > .025f)
                        issues.Add($"Courtyard {block.Cell} map arrival moved or missed the actual ground at {point:F4}.");
                }
                if (block.Kind == CityCourtyardBlockKind.OffsetPair)
                {
                    AddOffsetPairViews(layout, streetPlan, block, shots);
                    continue;
                }
                CityBuildingPrototypePose pose = CityBuildingPrototypePlacement.ResolveExpectedCityPose(block.Primary);
                Vector3 courtEye = ReplanningCourtyardEye(layout, streetPlan,
                    new Vector2(block.CourtCenter.x, block.CourtCenter.z));
                Vector3 courtTarget = pose.TransformPoint(new Vector3(9f, 1.15f, -2.5f));
                if (block.RearBuilding != null)
                {
                    Vector3 flank = pose.TransformPoint(new Vector3(8.1f, 0f, -1.5f));
                    courtEye = ReplanningCourtyardEye(layout, streetPlan, new Vector2(flank.x, flank.z));
                    courtTarget = block.CourtCenter + Vector3.up * 1.15f;
                }
                shots.Add(Shot.At($"replanning-streetfront-courtyard-{block.Cell.x}-{block.Cell.y}-court", courtEye,
                    courtTarget, 88f));
                if (block.RearBuilding != null)
                {
                    float passageDistance = block.Route.Project(new Vector2(block.PassageCenter.x,
                        block.PassageCenter.z)).DistanceAlong;
                    CityRoadSample eye = block.Route.SampleDistance(Mathf.Max(0f, passageDistance - 2f));
                    CityRoadSample target = block.Route.SampleDistance(Mathf.Min(block.Route.Length, passageDistance + 4f));
                    Vector3 passageEye = ReplanningCourtyardEye(layout, streetPlan, eye.Position);
                    shots.Add(Shot.At($"replanning-streetfront-courtyard-{block.Cell.x}-{block.Cell.y}-passage", passageEye,
                        new Vector3(target.Position.x, passageEye.y - .65f, target.Position.y), 78f));
                }
                if (block.Cell == new Vector2Int(0, 8))
                {
                    Vector3 entranceEye = ReplanningCourtyardEye(layout, streetPlan, block.Route.Vertices[0]);
                    CityRoadSample target = block.Route.SampleDistance(6f);
                    shots.Add(Shot.At("replanning-streetfront-courtyard-0-8-entrance", entranceEye,
                        new Vector3(target.Position.x, entranceEye.y - .55f, target.Position.y), 82f));
                }
            }
            foreach (CityCourtyardConnection connection in layout.CourtyardConnections)
            {
                VerifyReplanningWalkingPath($"Courtyard connection {connection.FirstCell}->{connection.SecondCell}",
                    connection.Path, city, streetPlan, pedestrianArea, issues);
                Vector2 middle = connection.Path.SampleDistance(connection.Path.Length * .5f).Position;
                if (!mapGround.TryResolveStandingPosition(middle, out Vector3 standing) ||
                    (new Vector2(standing.x, standing.z) - middle).sqrMagnitude > .0001f ||
                    !TryReplanningSurfaceTop(layout, streetPlan, middle, out float top) ||
                    Mathf.Abs(standing.y - top - PlayerFactory.GroundedRootOffset) > .025f)
                    issues.Add($"Courtyard connection map arrival moved or missed the ground at {middle:F4}.");
                foreach (bool reverse in new[] { false, true })
                {
                    float station = connection.Path.Length * (reverse ? .72f : .28f);
                    CityRoadSample eye = connection.Path.SampleDistance(station);
                    CityRoadSample target = connection.Path.SampleDistance(station + (reverse ? -9f : 9f));
                    Vector3 camera = ReplanningCourtyardEye(layout, streetPlan, eye.Position);
                    string connectionSuffix = connection.First.Kind == CityCourtyardBlockKind.LRecess &&
                        connection.Second.Kind == CityCourtyardBlockKind.LRecess ? string.Empty :
                        $"-{connection.FirstCell.x}-{connection.FirstCell.y}-{connection.SecondCell.x}-{connection.SecondCell.y}";
                    shots.Add(Shot.At($"replanning-streetfront-courtyard-connection{connectionSuffix}-{(reverse ? "reverse" : "forward")}", camera,
                        new Vector3(target.Position.x, camera.y - .65f, target.Position.y), 86f));
                }
            }
        }

        private static void AddOffsetPairViews(CityLayout layout, CityStreetSurfacePlan streetPlan,
            CityCourtyardBlock block, List<Shot> shots)
        {
            Vector3 courtEye = ReplanningCourtyardEye(layout, streetPlan,
                new Vector2(block.CourtCenter.x, block.CourtCenter.z));
            shots.Add(Shot.At("replanning-streetfront-0-9-open-court", courtEye,
                block.PassageCenter + Vector3.up * 1.05f, 88f));
            float passage = block.Route.Project(new Vector2(block.PassageCenter.x, block.PassageCenter.z)).DistanceAlong;
            CityRoadSample passageEye = block.Route.SampleDistance(passage - 2f);
            CityRoadSample passageTarget = block.Route.SampleDistance(passage + 5f);
            Vector3 eye = ReplanningCourtyardEye(layout, streetPlan, passageEye.Position);
            shots.Add(Shot.At("replanning-streetfront-0-9-narrow-passage", eye,
                new Vector3(passageTarget.Position.x, eye.y - .65f, passageTarget.Position.y), 78f));
            Vector3 entranceEye = ReplanningCourtyardEye(layout, streetPlan, block.Route.Vertices[0]);
            CityRoadSample entranceTarget = block.Route.SampleDistance(7f);
            shots.Add(Shot.At("replanning-streetfront-0-9-street-entrance", entranceEye,
                new Vector3(entranceTarget.Position.x, entranceEye.y - .55f, entranceTarget.Position.y), 82f));
        }

        private static void VerifyReplanningWalkingPath(string label, CityRoadPath path, CityGameRoot city,
            CityStreetSurfacePlan streetPlan, RoadWalkableArea pedestrianArea, List<string> issues)
        {
            CharacterController hero = city.Player.GameObject.GetComponent<CharacterController>();
            for (float distance = 0f; distance < path.Length + .5f; distance += .5f)
            {
                Vector2 point = path.SampleDistance(distance).Position;
                if (!TryReplanningSurfaceTop(city.Layout, streetPlan, point, out float top))
                { issues.Add($"{label} has no walking surface at {point:F4}."); continue; }
                Vector3 floor = new Vector3(point.x, top, point.y);
                if (!Physics.Raycast(floor + Vector3.up * 1.5f, Vector3.down,
                    out RaycastHit hit, 3f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore) ||
                    Mathf.Abs(hit.point.y - top) > .025f || hit.normal.y < .7f)
                {
                    issues.Add($"{label} physical floor at {point:F4}, expected={top:F4}, actual={hit.point.y:F4}, collider={hit.collider?.name}.");
                    Debug.Log($"COURT FLOOR {label} {point:F4}: " + string.Join("; ",
                        streetPlan.SidewalkGeometry.Where(box => box.TrySampleTop(floor, out _))
                            .Select(box => { box.TrySampleTop(floor, out float y); return $"sidewalk center={box.Center:F4}, size={box.Size:F4}, top={y:F4}"; })));
                    if (hit.collider is MeshCollider meshCollider && hit.triangleIndex >= 0)
                    {
                        Mesh mesh = meshCollider.sharedMesh;
                        int[] indices = mesh.triangles;
                        Vector3[] vertices = mesh.vertices;
                        int first = hit.triangleIndex * 3;
                        Debug.Log($"COURT TRIANGLE {label}: " + string.Join("; ",
                            Enumerable.Range(first, 3).Select(index => meshCollider.transform.TransformPoint(vertices[indices[index]]).ToString("F4"))));
                    }
                }
                if (!city.World.WalkableArea.Contains(floor, .35f)) issues.Add($"{label} hero capsule leaves navigation at {point:F4}.");
                if (!pedestrianArea.Contains(floor, .35f)) issues.Add($"{label} pedestrian capsule leaves navigation at {point:F4}.");
                // Full radius; the production step offset admits the existing curb.
                foreach (Collider obstacle in Physics.OverlapCapsule(
                    floor + Vector3.up * (hero.stepOffset + .35f + PlayerFactory.GroundedRootOffset),
                    floor + Vector3.up * (hero.height - .35f + PlayerFactory.GroundedRootOffset), .35f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (obstacle.transform.IsChildOf(city.Player.GameObject.transform) ||
                        obstacle.GetComponentInParent<DefaultNpcAppearance>() != null ||
                        obstacle.GetComponentInParent<CityPedestrianActor>() != null) continue;
                    issues.Add($"{label} route capsule blocked at {point:F4} by {obstacle.name}.");
                    if (obstacle.name.StartsWith("Ground sector"))
                        Debug.Log($"COURT SHOULDER {label} {point:F4}, centre={top:F4}: " + string.Join("; ",
                            Enumerable.Range(0, 16).Select(index =>
                            {
                                float angle = index * Mathf.PI / 8f;
                                Vector2 rim = point + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * .35f;
                                TryReplanningSurfaceTop(city.Layout, streetPlan, rim, out float planned);
                                Physics.Raycast(new Vector3(rim.x, top + 2f, rim.y), Vector3.down,
                                    out RaycastHit rimHit, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                                return $"{rim:F4}: planned={planned:F4}, physical={rimHit.point.y:F4}, collider={rimHit.collider?.name}";
                            })));
                }
            }
        }

        private sealed class ReplanningCourtyardWalkerDemo
        {
            private const float Tick = .05f;
            private readonly CityGameRoot city;
            private readonly CityStreetSurfacePlan surfaces;
            private readonly List<string> issues;
            private readonly CityRoadPath path;
            private readonly RoadWalkableArea area;
            private readonly int first, second;
            private readonly float[] toFirst, toSecond;
            private readonly int firstGate, secondGate;
            private readonly float[] toFirstGate, toSecondGate;
            private readonly CityCourtyardBlock firstBlock, secondBlock;
            private readonly List<KeyValuePair<Behaviour, bool>> paused = new List<KeyValuePair<Behaviour, bool>>();
            private readonly HashSet<string> capturedViews = new HashSet<string>();
            private readonly HashSet<string> observedRenderPhases = new HashSet<string>();
            private readonly ScarfContactProbe renderedContact = new ScarfContactProbe();
            private readonly ScarfContactReport renderedReport = new ScarfContactReport();
            private readonly Renderer[] buildings;
            private Renderer[] renderedWalker;
            private CityPedestrianActor walker;
            private Vector3 originalPosition;
            private Quaternion originalRotation;
            private int originalTarget, ticks, originalDetours;
            private bool originalCollision, reverseStarted, forwardCompleted, forwardExitCompleted;
            private bool originalAutomaticUpdatesSuspended, hasManualControl;
            private string originalDesign;
            private float travelled;
            private string stage = string.Empty;
            private int outgoingCorner = -1;
            private bool passedOutgoingCorner;
            private CityCourtyardBlock offsetBlock;
            private int offsetGate, offsetPassage, offsetCourt;
            private float[] toOffsetGate, toOffsetPassage, toOffsetCourt;
            private bool offsetStarted, offsetCourtCompleted;
            private float offsetTravelled;

            public ReplanningCourtyardWalkerDemo(CityGameRoot city, CityStreetSurfacePlan surfaces, List<string> issues)
            {
                this.city = city; this.surfaces = surfaces; this.issues = issues;
                CityCourtyardConnection connection = city.Layout.CourtyardConnections.Single(candidate =>
                    candidate.First.Kind == CityCourtyardBlockKind.LRecess &&
                    candidate.Second.Kind == CityCourtyardBlockKind.LRecess);
                path = connection.Path;
                firstBlock = connection.First; secondBlock = connection.Second;
                CityPedestrianPlan plan = city.PedestrianPlan;
                first = Enumerable.Range(0, plan.Nodes.Count).Single(index => plan.Nodes[index].Id ==
                    $"courtyard:{connection.FirstCell.x}:{connection.FirstCell.y}:court");
                second = Enumerable.Range(0, plan.Nodes.Count).Single(index => plan.Nodes[index].Id ==
                    $"courtyard:{connection.SecondCell.x}:{connection.SecondCell.y}:court");
                toFirst = CityBusStopWaitPlanner.CreateNodeDistances(plan, first);
                toSecond = CityBusStopWaitPlanner.CreateNodeDistances(plan, second);
                firstGate = Enumerable.Range(0, plan.Nodes.Count).Single(index => plan.Nodes[index].Id ==
                    $"courtyard:{connection.FirstCell.x}:{connection.FirstCell.y}:gate");
                secondGate = Enumerable.Range(0, plan.Nodes.Count).Single(index => plan.Nodes[index].Id ==
                    $"courtyard:{connection.SecondCell.x}:{connection.SecondCell.y}:gate");
                toFirstGate = CityBusStopWaitPlanner.CreateNodeDistances(plan, firstGate);
                toSecondGate = CityBusStopWaitPlanner.CreateNodeDistances(plan, secondGate);
                Assert.That(toSecond[first], Is.EqualTo(path.Length).Within(.01f), "The courtyard shortcut must be the actual shortest graph route.");
                area = CityPedestrianPlanner.CreateWalkableArea(plan);
                buildings = city.World.Root.GetComponentsInChildren<CityBuildingAssetRegistry>()
                    .SelectMany(registry => registry.Parts).Select(part => part.Renderer)
                    .Where(renderer => renderer != null && renderer.enabled).Distinct().ToArray();
            }

            public void AddShots(List<Shot> shots)
            {
                // Frame the walker on the longest real straight leg so a
                // courtyard corner cannot hide the body from the camera.
                float longest = 0f, along = 0f, middle = 0f;
                for (int i = 1; i < path.Vertices.Count; i++)
                {
                    float span = Vector2.Distance(path.Vertices[i - 1], path.Vertices[i]);
                    if (span > longest) { longest = span; middle = along + span * .5f; }
                    along += span;
                }
                foreach (bool reverse in new[] { false, true })
                {
                    foreach (bool arrival in new[] { false, true })
                    {
                        float station = arrival ? (reverse ? 0f : path.Length) : middle;
                        float cameraStation = arrival ? station + (reverse ? 2f : -2f)
                            : station + (reverse ? 1f : -1f) * Mathf.Min(3f, longest * .25f);
                        Vector3 eye = ReplanningCourtyardEye(city.Layout, surfaces, path.SampleDistance(cameraStation).Position);
                        Vector2 target = path.SampleDistance(station).Position;
                        TryReplanningSurfaceTop(city.Layout, surfaces, target, out float top);
                        shots.Add(Shot.At($"replanning-streetfront-courtyard-walker-{(reverse ? "reverse" : "forward")}-{(arrival ? "arrival" : "passage")}",
                            eye, new Vector3(target.x, top + .85f, target.y), 72f,
                            readyWhen: () => ReachCheckpoint(reverse, station, arrival)));
                    }
                    Vector3 gate = city.PedestrianPlan.Nodes[reverse ? firstGate : secondGate].Position;
                    Vector3 court = city.PedestrianPlan.Nodes[reverse ? first : second].Position;
                    Vector3 outward = gate - court; outward.y = 0f; outward.Normalize();
                    shots.Add(Shot.At($"replanning-streetfront-courtyard-walker-{(reverse ? "reverse" : "forward")}-gate-context",
                        gate + outward * 3f + Vector3.up * EyeHeight, gate + Vector3.up * .85f, 78f,
                        readyWhen: () => ReachExit(reverse)));
                }
            }

            public void AddOffsetPairShots(List<Shot> shots, CityCourtyardBlock block)
            {
                offsetBlock = block;
                Assert.That(block.Kind, Is.EqualTo(CityCourtyardBlockKind.OffsetPair));
                CityPedestrianPlan plan = city.PedestrianPlan;
                string prefix = $"courtyard:{block.Cell.x}:{block.Cell.y}";
                offsetGate = Enumerable.Range(0, plan.Nodes.Count).Single(index => plan.Nodes[index].Id == prefix + ":gate");
                offsetCourt = Enumerable.Range(0, plan.Nodes.Count).Single(index => plan.Nodes[index].Id == prefix + ":court");
                Vector2 passage = new Vector2(block.PassageCenter.x, block.PassageCenter.z);
                offsetPassage = Enumerable.Range(0, plan.Nodes.Count).Single(index =>
                    Vector2.Distance(new Vector2(plan.Nodes[index].Position.x, plan.Nodes[index].Position.z), passage) < .001f);
                toOffsetGate = CityBusStopWaitPlanner.CreateNodeDistances(plan, offsetGate);
                toOffsetPassage = CityBusStopWaitPlanner.CreateNodeDistances(plan, offsetPassage);
                toOffsetCourt = CityBusStopWaitPlanner.CreateNodeDistances(plan, offsetCourt);
                float station = block.Route.Project(passage).DistanceAlong;
                Vector3 eye = ReplanningCourtyardEye(city.Layout, surfaces, block.Route.SampleDistance(station - 2.5f).Position);
                shots.Add(Shot.At("replanning-streetfront-walker-pair-passage-context", eye,
                    block.PassageCenter + Vector3.up * .85f, 76f,
                    readyWhen: () => ReachOffsetPairGoal(false)));
                Vector3 gate = plan.Nodes[offsetGate].Position;
                shots.Add(Shot.At("replanning-streetfront-walker-pair-gate-context", gate + block.Primary.FacadeForward * 2.5f + Vector3.up * EyeHeight,
                    gate + Vector3.up * .85f, 78f,
                    readyWhen: () => ReachOffsetPairGoal(true) && ReachExit(false, true)));
            }

            private bool ReachOffsetPairGoal(bool court)
            {
                if (!offsetStarted)
                {
                    Assert.That(reverseStarted, Is.True, "Preserve both complete bridge direction trials before the new pair.");
                    RestoreWalkerPose();
                    PlaceTrialStart(offsetGate, 1f, "offset-pair-loop");
                    offsetStarted = true; offsetTravelled = 0f;
                }
                if (court && offsetCourtCompleted) return true;
                BeginStage(court ? "offset-pair-court" : "offset-pair-passage");
                int goal = court ? offsetCourt : offsetPassage;
                for (int batch = 0; batch < 40; batch++)
                {
                    if (AtNode(goal))
                    {
                        if (court) offsetCourtCompleted = true;
                        else CaptureActualViews("offset-pair-passage");
                        ObserveRenderedBody(stage + "-arrival");
                        Debug.Log($"Offset-pair walker physically reached {city.PedestrianPlan.Nodes[goal].Id}: " +
                            $"root={walker.Position:F4}, measuredLoopTravel={offsetTravelled:F3} m.");
                        return true;
                    }
                    Step(goal, court ? toOffsetCourt : toOffsetPassage, court ? offsetGate : offsetCourt,
                        court ? toOffsetGate : toOffsetCourt, offsetBlock.Route.Length, false);
                }
                return false;
            }

            private bool ReachCheckpoint(bool reverse, float station, bool arrival)
            {
                if (!Initialize()) return false;
                if (reverse && !reverseStarted)
                {
                    Assert.That(forwardExitCompleted, Is.True);
                    RestoreWalkerPose();
                    PlaceTrialStart(true);
                    reverseStarted = true;
                }
                BeginStage(reverse ? "reverse-connection" : "forward-connection");
                int goal = reverse ? first : second;
                Vector3 destination = city.PedestrianPlan.Nodes[goal].Position;
                float[] distances = reverse ? toFirst : toSecond;
                for (int batch = 0; batch < 40; batch++)
                {
                    Vector2 current = new Vector2(walker.Position.x, walker.Position.z);
                    float progress = path.Project(current).DistanceAlong;
                    bool reached = arrival ? Vector2.Distance(current, new Vector2(destination.x, destination.z)) < .06f
                        : reverse ? progress <= station : progress >= station;
                    if (reached)
                    {
                        if (arrival)
                        {
                            Assert.That(travelled, Is.GreaterThan(path.Length * .95f), "The walker must physically traverse the complete shortcut.");
                            Assert.That(walker.DesignId, Is.EqualTo(originalDesign));
                            if (!reverse) forwardCompleted = true;
                            Debug.Log($"Courtyard walker {originalDesign}, {(reverse ? "reverse" : "forward")}: measured travel={travelled:F3} m.");
                            ObserveRenderedBody(stage + "-arrival");
                        }
                        return true;
                    }
                    Step(goal, distances, reverse ? firstGate : secondGate,
                        reverse ? toFirstGate : toSecondGate, path.Length, true);
                }
                return false;
            }

            private bool Initialize()
            {
                if (walker != null) return true;
                walker = city.Pedestrians.Actors.FirstOrDefault(actor => actor.IsSpawned &&
                    actor.MotionState == CityPedestrianMotionState.Walking && !actor.IsPersonalSpaceReacting);
                if (walker == null) return false;
                originalPosition = walker.Position; originalRotation = walker.transform.rotation;
                originalTarget = walker.TargetNodeIndex; originalCollision = walker.CollisionEnabled;
                originalDesign = walker.DesignId;
                originalAutomaticUpdatesSuspended = city.Pedestrians.AutomaticUpdatesSuspended;
                city.Pedestrians.AutomaticUpdatesSuspended = true;
                hasManualControl = true;
                Pause(city.BusPassengers); Pause(city.BenchRests);
                Assert.That(walker.IsSpawned, Is.True, "Manual capture must retain the existing walker presentation.");
                Assert.That(walker.AgentRadius, Is.EqualTo(.35f).Within(.001f));
                renderedWalker = walker.Presentation.Registry.ModelRoot.GetComponentsInChildren<Renderer>()
                    .Where(renderer => renderer.enabled && renderer.gameObject.activeInHierarchy &&
                        (renderer is SkinnedMeshRenderer || renderer.GetComponent<MeshFilter>() != null)).ToArray();
                Assert.That(renderedWalker.Length, Is.GreaterThan(0), "The proof must inspect the real visible NPC model.");
                PlaceTrialStart(false);
                originalDetours = walker.DetourCount;
                return true;
            }

            private void PlaceTrialStart(bool reverse)
            {
                PlaceTrialStart(reverse ? second : first, reverse ? -1f : 1f, reverse ? "reverse" : "forward");
            }

            private void PlaceTrialStart(int start, float lateralBias, string label)
            {
                Assert.That(walker.IsSpawned, Is.True);
                Assert.That(walker.DesignId, Is.EqualTo(originalDesign), "All independent trials must reuse the same existing NPC.");
                // One initial fixture placement per independent trial.
                // Every measured bridge, turn and gate then stays continuous.
                walker.CharacterController.enabled = false;
                walker.transform.position = city.PedestrianPlan.Nodes[start].Position;
                walker.ResumeRoaming(start);
                walker.SetAvoidance(1f, lateralBias);
                Physics.SyncTransforms();
                Debug.Log($"Courtyard independent trial {label}: " +
                    $"existingWalker={originalDesign}, initialNode={city.PedestrianPlan.Nodes[start].Id}, " +
                    $"root={walker.Position:F4}; subsequent bridge, turn and exit retain physical graph movement.");
            }

            private void BeginStage(string name)
            {
                if (stage == name) return;
                stage = name; ticks = 0; travelled = 0f;
                outgoingCorner = -1; passedOutgoingCorner = false;
            }

            private bool AtNode(int node)
            {
                Vector3 delta = walker.Position - city.PedestrianPlan.Nodes[node].Position;
                delta.y = 0f;
                return delta.magnitude < .06f;
            }

            private bool ReachExit(bool reverse, bool offsetPair = false)
            {
                Assert.That(offsetPair ? offsetCourtCompleted : reverse ? reverseStarted : forwardCompleted, Is.True);
                int court = offsetPair ? offsetCourt : reverse ? first : second;
                int gate = offsetPair ? offsetGate : reverse ? firstGate : secondGate;
                float[] distances = offsetPair ? toOffsetGate : reverse ? toFirstGate : toSecondGate;
                CityCourtyardBlock block = offsetPair ? offsetBlock : reverse ? firstBlock : secondBlock;
                BeginStage(offsetPair ? "offset-pair-exit" : reverse ? "reverse-exit" : "forward-exit");
                for (int batch = 0; batch < 40; batch++)
                {
                    if (outgoingCorner < 0 && walker.PreviousNodeIndex == court && walker.TargetNodeIndex != court)
                    {
                        outgoingCorner = walker.TargetNodeIndex;
                        CityPedestrianLink outgoing = CurrentLink();
                        Assert.That(outgoing?.Kind, Is.EqualTo(CityPedestrianLinkKind.Courtyard));
                        Debug.Log($"Courtyard exit {stage}: link={outgoing.Id}; court={city.PedestrianPlan.Nodes[court].Id}; " +
                            $"corner={city.PedestrianPlan.Nodes[outgoingCorner].Id}; root={walker.Position:F4}; " +
                            $"forward={walker.transform.forward:F4}.");
                        ObserveRenderedBody(stage + "-turn-start");
                    }
                    if (travelled >= 5f && FacesCurrentCourtLeg())
                        CaptureActualViews(stage + "-after-turn");
                    if (AtNode(gate))
                    {
                        Assert.That(travelled, Is.GreaterThanOrEqualTo(5f), "The arrival proof must continue through the real court to its street gate.");
                        Assert.That(outgoingCorner, Is.GreaterThanOrEqualTo(0), "The proof must observe the actual outgoing court leg.");
                        Assert.That(passedOutgoingCorner, Is.True, "The walker must physically continue beyond the outgoing leg's next corner.");
                        Assert.That(capturedViews.Contains(stage + "-after-turn"), Is.True,
                            "The proof needs a real forward step after the court turn, before the exit arrival.");
                        CaptureActualViews(stage + "-gate");
                        if (offsetPair)
                            Assert.That(offsetTravelled, Is.GreaterThan(block.Route.Length * .95f),
                                "The existing walker must physically complete the route around the two staggered houses.");
                        else if (!reverse) forwardExitCompleted = true;
                        Debug.Log($"Courtyard exit {stage}: reached={city.PedestrianPlan.Nodes[gate].Id}; " +
                            $"measured travel={travelled:F3} m; root={walker.Position:F4}; " +
                            $"previous={walker.PreviousNodeIndex}; target={walker.TargetNodeIndex}; forward={walker.transform.forward:F4}.");
                        return true;
                    }
                    Step(gate, distances, offsetPair ? offsetCourt : reverse ? first : second,
                        offsetPair ? toOffsetCourt : reverse ? toFirst : toSecond,
                        block.Route.Length, false);
                }
                return false;
            }

            private CityPedestrianLink CurrentLink()
            {
                if (walker.PreviousNodeIndex < 0 || walker.TargetNodeIndex < 0) return null;
                CityPedestrianPlan plan = city.PedestrianPlan;
                return plan.GetLinkIndices(walker.PreviousNodeIndex).Select(index => plan.Links[index])
                    .FirstOrDefault(link => link.Other(walker.PreviousNodeIndex) == walker.TargetNodeIndex);
            }

            private bool FacesCurrentCourtLeg()
            {
                CityPedestrianLink link = CurrentLink();
                Vector3 motion = walker.LastDisplacement; motion.y = 0f;
                if (link?.Kind != CityPedestrianLinkKind.Courtyard || link.Path == null || motion.magnitude <= .002f) return false;
                Vector2 tangent = link.Path.SampleDistance(link.Path.Project(new Vector2(walker.Position.x, walker.Position.z)).DistanceAlong).Tangent;
                if (walker.TargetNodeIndex == link.FirstNodeIndex) tangent = -tangent;
                Vector3 forward = walker.transform.forward; forward.y = 0f; forward.Normalize();
                return Vector3.Dot(forward, new Vector3(tangent.x, 0f, tangent.y)) >= .88f;
            }

            private void Step(int goal, float[] distances, int nextGoal, float[] nextDistances,
                float lengthBudget, bool onConnection)
            {
                CityPedestrianPlan plan = city.PedestrianPlan;
                Vector3 destination = plan.Nodes[goal].Position;
                Vector2 current = new Vector2(walker.Position.x, walker.Position.z);
                CityRoadProjection projection = path.Project(current);
                int previousNode = walker.PreviousNodeIndex, targetNode = walker.TargetNodeIndex;
                string previousId = previousNode >= 0 ? plan.Nodes[previousNode].Id : "<none>";
                string targetId = targetNode >= 0 ? plan.Nodes[targetNode].Id : "<none>";
                Vector3 targetPosition = targetNode >= 0 ? plan.Nodes[targetNode].Position : walker.Position;
                Assert.That(++ticks, Is.LessThan((lengthBudget / walker.MovementSpeed + 20f) / Tick),
                    $"The production pedestrian stalled: stage={stage}, current={walker.Position:F4}, " +
                    $"target={targetPosition:F4}, destination={destination:F4}, " +
                    $"previousNode={previousNode}:{previousId}, targetNode={targetNode}:{targetId}, " +
                    $"progress={projection.DistanceAlong:F4}/{path.Length:F4}, " +
                    $"distanceGoal={Vector2.Distance(current, new Vector2(destination.x, destination.z)):F4}, " +
                    $"lastDisplacement={walker.LastDisplacement:F5}, motionState={walker.MotionState}.");
                Vector3 previous = walker.Position;
                Quaternion previousRotation = walker.transform.rotation;
                CityPedestrianLink walkingLink = CurrentLink();
                // Supply the next destination before the ordinary knot switch.
                // Finish the current edge; never reattach or reset the actor.
                bool nextGuidance = AtNode(goal) || Vector2.Distance(current,
                    new Vector2(destination.x, destination.z)) < walker.MovementSpeed * Tick + .06f;
                walker.Advance(Tick, initialApproachTarget: nextGuidance ? plan.Nodes[nextGoal].Position : destination,
                    initialApproachNodeDistances: nextGuidance ? nextDistances : distances);
                Vector3 displacement = walker.Position - previous; displacement.y = 0f;
                travelled += displacement.magnitude;
                if (offsetStarted) offsetTravelled += displacement.magnitude;
                if (outgoingCorner >= 0 && previousNode == outgoingCorner && displacement.magnitude > .002f)
                    passedOutgoingCorner = true;
                Assert.That(walker.CollisionEnabled, Is.True, "The demo must retain the real pedestrian capsule.");
                Assert.That(walker.DetourCount, Is.EqualTo(originalDetours), "The court must be traversable without a blocked escape.");
                if (walkingLink?.Kind == CityPedestrianLinkKind.Courtyard)
                {
                    Assert.That(Quaternion.Angle(previousRotation, walker.transform.rotation), Is.LessThanOrEqualTo(360f * Tick + .2f),
                        "A courtyard turn must retain the production yaw rate.");
                    if (displacement.magnitude > .002f)
                    {
                        Vector3 forward = walker.transform.forward; forward.y = 0f; forward.Normalize();
                        float facing = Vector3.Dot(forward, displacement.normalized);
                        Assert.That(facing, Is.GreaterThanOrEqualTo(.88f),
                            $"The visible walker must face its real step: stage={stage}, link={walkingLink.Id}, " +
                            $"root={walker.Position:F4}, displacement={displacement:F5}, forward={forward:F4}, dot={facing:F4}.");
                    }
                }
                current = new Vector2(walker.Position.x, walker.Position.z);
                float maximumOffset = CityPedestrianActor.MaximumLateralOffset + .02f;
                float pathDistance = onConnection ? path.Project(current).DistanceSquared :
                    city.Layout.CourtyardPaths.Min(route => route.Project(current).DistanceSquared);
                Assert.That(pathDistance, Is.LessThan(maximumOffset * maximumOffset),
                    "The pedestrian must stay on its authored court path with the production shoulder shift.");
                Assert.That(area.Contains(walker.Position, .35f), Is.True);
                Assert.That(city.World.WalkableArea.Contains(walker.Position, .35f), Is.True);
                bool hasGroundTop = TryReplanningSurfaceTop(city.Layout, surfaces, current, out float top);
                if (!hasGroundTop || walker.Position.y < top - .002f)
                    issues.Add($"Courtyard walker root below ground or missing ground at {current:F4}, " +
                        $"hasGroundTop={hasGroundTop}, root={walker.Position.y:F4}, ground={top:F4}.");
                VerifyControllerSupport();
                RaycastHit[] hits = Physics.RaycastAll(new Vector3(current.x, top + .5f, current.y), Vector3.down,
                    1f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                if (!hits.Any(hit => hit.collider is MeshCollider && Mathf.Abs(hit.point.y - top) < .025f))
                    issues.Add($"Courtyard walker has no physical ground at {current:F4}, expected={top:F4}.");
            }

            private void VerifyControllerSupport()
            {
                const float lift = .5f, precision = .002f;
                CharacterController controller = walker.CharacterController;
                Vector3 scale = controller.transform.lossyScale;
                float radius = controller.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                Vector3 lowerSphere = controller.transform.TransformPoint(controller.center -
                    Vector3.up * (controller.height * .5f - controller.radius));
                // Probe the real lower hemisphere, including lateral support
                // from a neighbouring curb. A centre-height subtraction cannot
                // represent this footprint on slopes or beside a raised strip.
                RaycastHit[] supports = Physics.SphereCastAll(lowerSphere + Vector3.up * lift, radius,
                    Vector3.down, lift + radius + controller.stepOffset + controller.skinWidth,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                RaycastHit? support = null;
                foreach (RaycastHit candidate in supports)
                {
                    if (!(candidate.collider is MeshCollider) || candidate.normal.y <= 0f ||
                        !candidate.transform.IsChildOf(city.World.Root.transform)) continue;
                    Vector2 contact = new Vector2(candidate.point.x, candidate.point.z);
                    if (!TryReplanningSurfaceTop(city.Layout, surfaces, contact, out float contactTop) ||
                        Mathf.Abs(candidate.point.y - contactTop) >= .025f) continue;
                    // Sphere casts report the contact direction at a curb edge,
                    // which can differ from the horizontal top's triangle normal.
                    if (!support.HasValue || candidate.distance < support.Value.distance) support = candidate;
                }
                if (!support.HasValue)
                {
                    issues.Add($"Courtyard walker has no physical capsule support: stage={stage}, " +
                        $"root={walker.Position:F4}, lowerSphere={lowerSphere:F4}, radius={radius:F4}.");
                    return;
                }
                RaycastHit hit = support.Value;
                float signedVerticalGap = hit.distance - lift;
                float signedNormalGap = signedVerticalGap * hit.normal.y;
                if (signedNormalGap < -precision || signedNormalGap > controller.skinWidth + precision)
                {
                    issues.Add($"Courtyard walker capsule outside controller ground-contact band: stage={stage}, " +
                        $"root={walker.Position:F4}, lowerSphere={lowerSphere:F4}, radius={radius:F4}, " +
                        $"collider={hit.collider.name}, contact={hit.point:F4}, normal={hit.normal:F5}, " +
                        $"verticalGap={signedVerticalGap:F5}, normalGap={signedNormalGap:F5}, skinWidth={controller.skinWidth:F4}.");
                }
            }

            private void ObserveRenderedBody(string phase)
            {
                if (!observedRenderPhases.Add(phase)) return;
                renderedContact.Origin = walker.Position;
                var nearby = new Dictionary<Renderer, ScarfContactSurface>();
                foreach (Renderer renderer in renderedWalker)
                {
                    ScarfContactSurface body = renderedContact.Read(renderer);
                    var worldBounds = new Bounds(body.Bounds.center + walker.Position, body.Bounds.size);
                    foreach (Renderer building in buildings)
                    {
                        if (!worldBounds.Intersects(building.bounds)) continue;
                        if (!nearby.TryGetValue(building, out ScarfContactSurface surface))
                            nearby.Add(building, surface = renderedContact.Read(building));
                        if (!renderedContact.Intersects(body, surface, phase, renderedReport)) continue;
                        issues.Add($"Rendered courtyard body intersects a building: phase={phase}, " +
                            $"body={renderer.name}, building={building.name}, root={walker.Position:F4}, " +
                            $"reason={renderedContact.Witness.reason}.");
                    }
                }
                Debug.Log($"Courtyard rendered contact proof: phase={phase}, bodyMeshes={renderedWalker.Length}, " +
                    $"nearbyBuildingMeshes={nearby.Count}, trianglePairs={renderedReport.near_triangle_pairs}.");
            }

            private void CaptureActualViews(string name)
            {
                if (!capturedViews.Add(name)) return;
                ObserveRenderedBody(name);
                Camera camera = Camera.main;
                Vector3 previousPosition = camera.transform.position;
                Quaternion previousRotation = camera.transform.rotation;
                float previousFov = camera.fieldOfView;
                Vector3 forward = walker.transform.forward, right = walker.transform.right;
                Debug.Log($"Courtyard actual view {name}: root={walker.Position:F4}, forward={forward:F4}, " +
                    $"lastDisplacement={walker.LastDisplacement:F5}, link={CurrentLink()?.Id}, " +
                    $"previous={walker.PreviousNodeIndex}, target={walker.TargetNodeIndex}, measuredTravel={travelled:F3} m.");
                try
                {
                    foreach (bool heading in new[] { false, true })
                    {
                        Vector3 target = walker.Position + Vector3.up * (heading ? 1.3f : .9f) +
                            (heading ? forward * 10f : Vector3.zero);
                        Vector3 eye = walker.Position - forward * (heading ? 1.8f : 3f) +
                            right * (heading ? .45f : 1.6f) + Vector3.up * 1.65f;
                        Vector3 focus = walker.Position + Vector3.up * .9f;
                        Vector3 view = eye - focus;
                        RaycastHit[] obstructions = Physics.RaycastAll(focus, view.normalized, view.magnitude,
                            Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                        float free = obstructions.Where(hit => !hit.transform.IsChildOf(walker.transform) &&
                            !hit.transform.IsChildOf(city.Player.GameObject.transform)).Select(hit => hit.distance)
                            .DefaultIfEmpty(view.magnitude + .2f).Min();
                        eye = focus + view.normalized * Mathf.Max(.35f, Mathf.Min(view.magnitude, free - .2f));
                        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, Vector3.up));
                        camera.fieldOfView = heading ? 86f : 72f;
                        CaptureCurrentCamera(camera, SceneIds.City,
                            $"replanning-streetfront-courtyard-walker-{name}-{(heading ? "forward-view" : "body")}");
                    }
                }
                finally
                {
                    camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                    camera.fieldOfView = previousFov;
                }
            }

            private void Pause(Behaviour behaviour)
            {
                if (behaviour == null) return;
                paused.Add(new KeyValuePair<Behaviour, bool>(behaviour, behaviour.enabled));
                behaviour.enabled = false;
            }

            private void RestoreWalkerPose()
            {
                if (walker != null && walker.IsSpawned)
                {
                    walker.CharacterController.enabled = false;
                    walker.transform.SetPositionAndRotation(originalPosition, originalRotation);
                    walker.ResumeRoaming(originalTarget);
                    walker.SetAvoidance(1f, 0f);
                    walker.CharacterController.enabled = originalCollision;
                }
            }

            public void Restore()
            {
                RestoreWalkerPose();
                foreach (KeyValuePair<Behaviour, bool> entry in paused)
                    if (entry.Key != null) entry.Key.enabled = entry.Value;
                if (hasManualControl && city != null && city.Pedestrians != null)
                    city.Pedestrians.AutomaticUpdatesSuspended = originalAutomaticUpdatesSuspended;
                renderedContact.Dispose();
            }
        }

        private static bool TryReplanningSurfaceTop(CityLayout layout, CityStreetSurfacePlan streetPlan,
            Vector2 point, out float top)
        {
            float exposedTop = float.NegativeInfinity;
            var position = new Vector3(point.x, 0f, point.y);
            foreach (RuntimeOrientedBox sidewalk in streetPlan.SidewalkGeometry)
                if (sidewalk.TrySampleTop(position, out float sidewalkTop))
                    exposedTop = Mathf.Max(exposedTop, sidewalkTop);
            foreach (CityStreetRibbonDescriptor ribbon in streetPlan.CurvedSidewalkRibbons)
                if (ribbon.Polygons.Any(polygon => CityRoadPolygon.Contains(polygon, point)))
                {
                    CityRoadPath path = layout.RoadGeometry.Get(ribbon.Edge);
                    float ribbonTop = (ribbon.FlatNode.HasValue ? layout.ElevationPlan.GetNodeElevation(ribbon.FlatNode.Value) :
                        layout.ElevationPlan.SampleRoadDatum(ribbon.Edge, path.Project(point).DistanceAlong / path.Length)) +
                        ribbon.TopOffset;
                    exposedTop = Mathf.Max(exposedTop, ribbonTop);
                }
            // Unified ground gives sidewalk coating priority 30, before road
            // priority 20, even when an approach slab crosses slightly above it.
            if (!float.IsNegativeInfinity(exposedTop)) { top = exposedTop; return true; }
            foreach (RuntimeOrientedBox street in streetPlan.StreetGeometry)
                if (street.TrySampleTop(position, out float streetTop))
                    exposedTop = Mathf.Max(exposedTop, streetTop);
            foreach (CityStreetRibbonDescriptor ribbon in streetPlan.CurvedStreetRibbons)
                if (ribbon.Polygons.Any(polygon => CityRoadPolygon.Contains(polygon, point)))
                {
                    CityRoadPath path = layout.RoadGeometry.Get(ribbon.Edge);
                    float ribbonTop = (ribbon.FlatNode.HasValue ? layout.ElevationPlan.GetNodeElevation(ribbon.FlatNode.Value) :
                        layout.ElevationPlan.SampleRoadDatum(ribbon.Edge, path.Project(point).DistanceAlong / path.Length)) +
                        ribbon.TopOffset;
                    exposedTop = Mathf.Max(exposedTop, ribbonTop);
                }
            if (!float.IsNegativeInfinity(exposedTop)) { top = exposedTop; return true; }
            if (CityTerrainSurfacePlan.TrySampleGroundTop(layout, point, out top, out _)) return true;
            return layout.ElevationPlan.TrySampleSurface(point, CitySurfaceRole.RoadTop, out top, out _);
        }

        private static Vector3 ReplanningCourtyardEye(CityLayout layout, CityStreetSurfacePlan streetPlan, Vector2 point)
        {
            Assert.That(TryReplanningSurfaceTop(layout, streetPlan, point, out float top), Is.True);
            return new Vector3(point.x, top + EyeHeight, point.y);
        }

        private static bool NearPavementEnd(CityStreetRibbonDescriptor ribbon, Vector2 point)
        {
            foreach (Vector2[] polygon in ribbon.Polygons)
                foreach (int side in new[] { 0, 2 })
                {
                    // Cross-sections are legitimate material/height boundaries;
                    // raycasts on their millimetre tolerance band are ambiguous.
                    Vector2 a = polygon[side], delta = polygon[(side + 1) % polygon.Length] - a;
                    Vector2 nearest = a + delta * Mathf.Clamp01(Vector2.Dot(point - a, delta) / delta.sqrMagnitude);
                    if ((point - nearest).sqrMagnitude < .000004f) return true;
                }
            return false;
        }

        private static void LogReplanningRouteLengths(CityGameRoot city)
        {
            Assert.That(city.World.FringeYardPlan.HasTunnelForecourt, Is.True);
            Assert.That(city.World.MountainBoundaryPlan.HasTunnel, Is.True);
            LastRouteCarPlan car = LastRouteCarPlan.Create(city.Layout);
            Assert.That(car.IsPresent, Is.True);
            CityTunnelTravelPlan tunnel = CityTunnelTravelPlanner.Create(
                city.World.MountainBoundaryPlan.Tunnel);
            CityTunnelForecourtDescriptor forecourt = city.World.FringeYardPlan.TunnelForecourt;
            LastRouteCarDrivePath departure = LastRouteCityDrivePlanner.CreateDeparture(
                car, city.Layout, forecourt, tunnel.FloorSurfaceY);
            LastRouteCarDrivePath arrival = LastRouteCityDrivePlanner.CreateReturn(
                car, city.Layout, forecourt, tunnel.FloorSurfaceY);
            float blackout = LastRouteCityDrivePlanner.TunnelBlackoutDepth;
            Debug.Log($"City replanning routes: departureTotal={departure.Length:F3} m; " +
                $"cityToPortal={departure.Length - blackout:F3} m; tunnel={blackout:F3} m; " +
                $"returnTotal={arrival.Length:F3} m; busLoop={city.BusPlan.LoopLength:F3} m; " +
                $"busStops={city.BusPlan.Stops.Count}.");
        }

        private static Vector3 ReplanningStreetEye(CityLayout layout, Vector3 position)
        {
            if (layout.ElevationPlan.TrySampleSurface(new Vector2(position.x, position.z),
                CitySurfaceRole.RoadTop, out float top, out _)) position.y = top;
            position.y += EyeHeight;
            return position;
        }
    }
}
