using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityMountainBoundaryTests
    {
        [Test]
        [Category("CityMountain")]
        public void DefaultCoastal_PlansDeterministicWestAndSouthRim()
        {
            CityLayout layout = CreateDefaultLayout();

            CityMountainBoundaryPlan first =
                CityMountainBoundaryPlanner.Create(layout);
            CityMountainBoundaryPlan second =
                CityMountainBoundaryPlanner.Create(layout);

            Assert.That(first.IsEnabled, Is.True);
            Assert.That(first.RidgeCount, Is.EqualTo(6));
            Assert.That(
                first.GetRidgeCount(CityMountainBoundarySide.West),
                Is.EqualTo(3));
            Assert.That(
                first.GetRidgeCount(CityMountainBoundarySide.South),
                Is.EqualTo(3));
            Assert.That(first.HasRiverNotch, Is.True);
            Assert.That(first.HasTunnel, Is.True);
            Assert.That(
                first.Ridges.Count(item => item.IsSouthWestJoin),
                Is.EqualTo(1));
            AssertPlansEqual(first, second);
            Assert.DoesNotThrow(() =>
                CityMountainBoundaryValidator.ValidateOrThrow(
                    layout,
                    first));

            foreach (CityMountainRidgeDescriptor ridge in first.Ridges)
            {
                Assert.That(
                    ridge.Side,
                    Is.EqualTo(CityMountainBoundarySide.West)
                        .Or.EqualTo(CityMountainBoundarySide.South));
                Assert.That(ridge.Stations.Count, Is.GreaterThanOrEqualTo(2));
                foreach (CityMountainRidgeStation station in ridge.Stations)
                {
                    Assert.That(
                        station.PeakY,
                        Is.GreaterThan(station.BaseY + 9.9f));
                }
            }
        }

        [Test]
        [Category("CityMountain")]
        public void SouthRim_LeavesTunnelAndRiverOpenings()
        {
            CityLayout layout = CreateDefaultLayout();
            CityMountainBoundaryPlan plan =
                CityMountainBoundaryPlanner.Create(layout);
            CityMountainTunnelDescriptor tunnel = plan.Tunnel;
            CityMountainRiverNotchDescriptor notch = plan.RiverNotch;

            CityOpenAreaAccessDescriptor access = layout.OpenAreaAccesses
                .Single(item =>
                    item.Id ==
                    CityMountainBoundaryDefinition.TunnelAccessId);
            Assert.That(
                tunnel.TargetAccessId,
                Is.EqualTo(access.Id));
            Assert.That(
                tunnel.AreaId,
                Is.EqualTo(
                    CityMountainBoundaryDefinition.SouthWestAreaId));
            Assert.That(tunnel.Axis, Is.EqualTo(Vector3.back));
            Assert.That(tunnel.IsSealed, Is.True);
            Assert.That(
                tunnel.PortalGroundCenter.x,
                Is.EqualTo(access.Center.x).Within(0.01f));
            Assert.That(
                tunnel.OpeningWidth,
                Is.EqualTo(8f).Within(0.01f));
            Assert.That(
                tunnel.OpeningHeight,
                Is.EqualTo(5.5f).Within(0.01f));

            Assert.That(notch.Side, Is.EqualTo(
                CityMountainBoundarySide.South));
            Assert.That(notch.ChannelAxis, Is.EqualTo(Vector3.forward));
            Assert.That(
                notch.ClearWidth,
                Is.GreaterThan(layout.River.Definition.ChannelWidth));
            Assert.That(
                OverlapsStrict(tunnel.PortalBounds, notch.OpeningBounds),
                Is.False);

            AssertSouthOpeningShoulders(
                plan,
                tunnel.PortalBounds.xMin,
                tunnel.PortalBounds.xMax,
                tunnel.PortalGroundCenter.z);
            AssertSouthOpeningShoulders(
                plan,
                notch.OpeningBounds.xMin,
                notch.OpeningBounds.xMax,
                tunnel.PortalGroundCenter.z);

            foreach (CityMountainRidgeDescriptor ridge in plan.Ridges)
            {
                for (int index = 1; index < ridge.Stations.Count; index++)
                {
                    Vector2 first = ridge.Stations[index - 1].WorldXZ;
                    Vector2 second = ridge.Stations[index].WorldXZ;
                    Assert.That(
                        SegmentCrossesRect(
                            first,
                            second,
                            tunnel.PortalBounds),
                        Is.False,
                        $"{ridge.StableId} closes the tunnel.");
                    Assert.That(
                        SegmentCrossesRect(
                            first,
                            second,
                            notch.OpeningBounds),
                        Is.False,
                        $"{ridge.StableId} closes the river gorge.");
                }
            }
        }

        [Test]
        [Category("CityMountain")]
        public void SouthWestJoin_WeldsPhysicalCrossSectionsAtBothEnds()
        {
            CityMountainBoundaryPlan plan =
                CityMountainBoundaryPlanner.Create(CreateDefaultLayout());
            CityMountainRidgeDescriptor join = plan.Ridges.Single(ridge =>
                ridge.IsSouthWestJoin);
            CityMountainRidgeStation westJoin = join.Stations[0];
            CityMountainRidgeStation southJoin =
                join.Stations[join.Stations.Count - 1];
            CityMountainRidgeStation westEndpoint = FindMatchingEndpoint(
                plan,
                CityMountainBoundarySide.West,
                westJoin.WorldXZ);
            CityMountainRidgeStation southEndpoint = FindMatchingEndpoint(
                plan,
                CityMountainBoundarySide.South,
                southJoin.WorldXZ);

            AssertCrossSectionsWelded(westEndpoint, westJoin);
            AssertCrossSectionsWelded(southEndpoint, southJoin);
        }

        [Test]
        [Category("CityMountain")]
        public void SouthWestCornerClosure_WeldsSampledGroundAndCollision()
        {
            CityLayout layout = CreateDefaultLayout();
            CityMountainBoundaryPlan plan =
                CityMountainBoundaryPlanner.Create(layout);
            CityMountainCornerClosureDescriptor closure =
                plan.SouthWestCornerClosure;
            CityMountainRidgeDescriptor join = plan.Ridges.Single(ridge =>
                ridge.IsSouthWestJoin);

            Assert.That(plan.HasSouthWestCornerClosure, Is.True);
            Assert.That(closure, Is.Not.Null);
            Assert.That(
                closure.StableId,
                Is.EqualTo(
                    CityMountainCornerClosureDescriptor.SouthWestStableId));
            Assert.That(closure.VoidCell, Is.EqualTo(new Vector2Int(-1, -1)));
            Assert.That(
                layout.Surfaces.Any(surface =>
                    surface.Cell == closure.VoidCell),
                Is.False);
            Assert.That(plan.RidgeCount, Is.EqualTo(6));

            float[] westCoordinates =
            {
                -182f, -180f, -178f, -175f, -172f,
                -169f, -166f, -163f, -160f
            };
            float[] southCoordinates =
            {
                -160f, -163f, -166f, -169f, -172f,
                -175f, -178f, -180f, -182f
            };
            Assert.That(
                closure.WestBoundarySamples.Select(item => item.x).ToArray(),
                Is.EqualTo(westCoordinates).Within(0.001f));
            Assert.That(
                closure.SouthBoundarySamples.Select(item => item.z).ToArray(),
                Is.EqualTo(southCoordinates).Within(0.001f));
            AssertBoundaryMatchesTerrain(
                layout,
                closure.WestAreaId,
                closure.WestBoundarySamples);
            AssertBoundaryMatchesTerrain(
                layout,
                closure.SouthAreaId,
                closure.SouthBoundarySamples);

            Assert.That(closure.WestToe, Is.EqualTo(join.Stations[0].Toe));
            Assert.That(
                closure.JoinMiddleToe,
                Is.EqualTo(join.Stations[1].Toe));
            Assert.That(
                closure.SouthToe,
                Is.EqualTo(join.Stations[2].Toe));
            Vector3 roadNode = layout.GetNodeWorldPosition(Vector2Int.zero);
            Assert.That(
                closure.RoadCorner,
                Is.EqualTo(new Vector3(
                    roadNode.x - layout.RoadWidth * 0.5f,
                    roadNode.y + CityStreetSurfacePlanner.RoadTop,
                    roadNode.z - layout.RoadWidth * 0.5f)));
            Assert.That(closure.ProjectedArea, Is.EqualTo(322f).Within(0.01f));
            Assert.That(
                closure.SurfaceTriangleIndices.Count,
                Is.EqualTo(18 * 3));
            Assert.That(closure.Vertices.Count, Is.EqualTo(20));
            Assert.That(
                closure.SupportTriangleIndices.Count,
                Is.Zero,
                "Plain corner ground must not contain steps or walls.");
            Assert.That(
                closure.TriangleIndices.Count,
                Is.EqualTo(closure.SurfaceTriangleIndices.Count));
            Assert.That(
                closure.NaturalGroundSlopeDegrees,
                Is.LessThanOrEqualTo(
                    CityMountainCornerClosureDescriptor
                        .MaximumNaturalGroundSlopeDegrees));
            Assert.That(
                closure.NaturalGroundSlopeDegrees,
                Is.EqualTo(16.159f).Within(0.05f),
                "The ordinary corner ground should keep its natural grade.");

            float projectedArea = 0f;
            for (int index = 0;
                 index < closure.SurfaceTriangleIndices.Count;
                 index += 3)
            {
                Vector3 first = closure.Vertices[
                    closure.SurfaceTriangleIndices[index]];
                Vector3 second = closure.Vertices[
                    closure.SurfaceTriangleIndices[index + 1]];
                Vector3 third = closure.Vertices[
                    closure.SurfaceTriangleIndices[index + 2]];
                Vector3 normal = Vector3.Cross(
                    second - first,
                    third - first);
                Assert.That(
                    normal.y,
                    Is.GreaterThan(0.001f),
                    $"Corner surface {index / 3} points down.");
                projectedArea += normal.y * 0.5f;
            }

            Assert.That(
                projectedArea,
                Is.EqualTo(closure.ProjectedArea).Within(0.02f));
            RoadWalkableArea originalWalkable =
                RoadWalkableArea.FromLayout(layout);
            Vector2[] blockedGround =
            {
                new Vector2(-160.4f, -160.4f),
                new Vector2(-163f, -163f),
                new Vector2(-168.6f, -168.6f)
            };
            for (int index = 0; index < blockedGround.Length; index++)
            {
                Vector2 sample = blockedGround[index];
                Assert.That(
                    originalWalkable.Contains(
                        new Vector3(sample.x, 0f, sample.y),
                        CityGroundTraversalPlanner.MaximumAgentRadius),
                    Is.False,
                    $"Fenced corner ground became walkable at {sample}.");
            }

            Assert.That(
                originalWalkable.Contains(
                    new Vector3(-175f, 0f, -175f),
                    CityGroundTraversalPlanner.MaximumAgentRadius),
                Is.False,
                "The road mask must stop cityward of the rock toe.");

            RoadFencePlan fencePlan = RoadFencePlanner.CreatePlan(layout);
            Vector2 roadCornerXZ = new Vector2(
                closure.RoadCorner.x,
                closure.RoadCorner.z);
            RoadFenceSegmentDescriptor[] cornerLegs = fencePlan.Segments
                .Where(segment =>
                    segment.Purpose ==
                    RoadFenceSegmentPurpose.MapBoundary &&
                    (Vector2.Distance(
                         new Vector2(segment.Start.x, segment.Start.z),
                         roadCornerXZ) < 0.001f ||
                     Vector2.Distance(
                         new Vector2(segment.End.x, segment.End.z),
                         roadCornerXZ) < 0.001f))
                .ToArray();
            Assert.That(
                cornerLegs,
                Has.Length.EqualTo(2),
                "The two ordinary fence remnants must meet at the corner.");
            Assert.That(
                cornerLegs.Any(segment =>
                    segment.IsHorizontal &&
                    segment.OutwardNormal == Vector3.back),
                Is.True);
            Assert.That(
                cornerLegs.Any(segment =>
                    !segment.IsHorizontal &&
                    segment.OutwardNormal == Vector3.left),
                Is.True);
            foreach (RoadFenceSegmentDescriptor leg in cornerLegs)
            {
                Vector3 cornerDatum = Vector2.Distance(
                        new Vector2(leg.Start.x, leg.Start.z),
                        roadCornerXZ) < 0.001f
                    ? leg.Start
                    : leg.End;
                Assert.That(
                    cornerDatum.y,
                    Is.EqualTo(roadNode.y).Within(0.001f),
                    "An L-corner rail endpoint fell below its road node.");
            }

            var host = new GameObject("Corner Closure Test Host");
            try
            {
                GameObject mountain = CityMountainBoundaryWorldBuilder.Build(
                    host.transform,
                    layout,
                    plan);
                GameObject fence = RoadFenceWorldBuilder.Build(
                    host.transform,
                    fencePlan);
                GameObject built = FindChild(
                        mountain.transform,
                        closure.StableId)
                    .gameObject;
                Mesh mesh = built.GetComponent<MeshFilter>().sharedMesh;
                MeshCollider collider = built.GetComponent<MeshCollider>();
                MeshRenderer renderer = built.GetComponent<MeshRenderer>();
                Assert.That(collider, Is.Not.Null);
                Assert.That(collider.isTrigger, Is.False);
                Assert.That(collider.sharedMesh, Is.SameAs(mesh));
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.On));
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
                var properties = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(Shader.PropertyToID("_BaseMap")),
                    Is.SameAs(
                        CityFringeYardSurfaceAppearance.GetTexture(
                            CityFringeYardSurfaceKind.ForefieldGround)));
                CollectionAssert.AreEqual(
                    closure.Vertices,
                    mesh.vertices);
                CollectionAssert.AreEqual(
                    closure.TriangleIndices,
                    mesh.triangles);
                Assert.That(
                    mesh.vertexCount,
                    Is.LessThan(mesh.triangles.Length),
                    "Natural ground should share vertices and smooth " +
                    "across its small triangles.");

                Physics.SyncTransforms();
                Collider[] fenceColliders =
                    fence.GetComponentsInChildren<Collider>(true);
                foreach (RoadFenceSegmentDescriptor leg in cornerLegs)
                {
                    bool startsAtCorner = Vector2.Distance(
                        new Vector2(leg.Start.x, leg.Start.z),
                        roadCornerXZ) < 0.001f;
                    Vector3 cornerDatum = startsAtCorner
                        ? leg.Start
                        : leg.End;
                    Vector3 other = startsAtCorner ? leg.End : leg.Start;
                    Vector3 lowerRailProbe = cornerDatum +
                        (other - cornerDatum).normalized * 0.25f +
                        leg.OutwardNormal * 0.08f +
                        Vector3.up * 0.60f;
                    Collider visibleRail = fenceColliders.FirstOrDefault(
                        item => item.bounds.Contains(lowerRailProbe));
                    Assert.That(
                        visibleRail,
                        Is.Not.Null,
                        "A physical rail is missing beside the L corner.");
                    Assert.That(
                        visibleRail.GetComponent<MeshRenderer>(),
                        Is.Not.Null,
                        "The L-corner collider has no visible rail mesh.");
                }
                for (int index = 0;
                     index < closure.SurfaceTriangleIndices.Count;
                     index += 3)
                {
                    Vector3 centroid = (
                        closure.Vertices[
                            closure.SurfaceTriangleIndices[index]] +
                        closure.Vertices[
                            closure.SurfaceTriangleIndices[index + 1]] +
                        closure.Vertices[
                            closure.SurfaceTriangleIndices[index + 2]]) /
                        3f;
                    Assert.That(
                        RaycastDown(collider, centroid, out _),
                        Is.True,
                        $"Corner surface {index / 3} has no " +
                        "collision.");
                }

                Assert.That(
                    RaycastDown(
                        collider,
                        new Vector3(-161f, 0f, -161f),
                        out _),
                    Is.True);
                Assert.That(
                    RaycastDown(
                        collider,
                        new Vector3(-175f, 0f, -175f),
                        out _),
                    Is.False,
                    "The closure must not extend behind the rock toe.");
                for (int sampleIndex = 0; sampleIndex <= 32; sampleIndex++)
                {
                    Vector2 sample = Vector2.Lerp(
                        blockedGround[0],
                        blockedGround[blockedGround.Length - 1],
                        sampleIndex / 32f);
                    Assert.That(
                        RaycastDown(
                            collider,
                            new Vector3(sample.x, 0f, sample.y),
                            out RaycastHit groundHit),
                        Is.True,
                        $"Plain ground is missing at {sample}.");
                    Assert.That(
                        Vector3.Angle(groundHit.normal, Vector3.up),
                        Is.LessThanOrEqualTo(
                            CityMountainCornerClosureDescriptor
                                .MaximumNaturalGroundSlopeDegrees + 0.1f),
                        $"Plain ground is too steep at {sample}.");
                }

                foreach (CityMountainRidgeStation station in join.Stations)
                {
                    Vector3 buried = station.Toe -
                        station.OutwardNormal *
                        CityMountainBoundaryMeshFactory.ToeGroundOverlap +
                        Vector3.down *
                        CityMountainBoundaryMeshFactory.ToeGroundBurial;
                    Vector3 interiorProbe = Vector3.Lerp(
                        buried,
                        closure.RoadCorner,
                        0.001f);
                    Assert.That(
                        RaycastDown(
                            collider,
                            interiorProbe,
                            out RaycastHit hit),
                        Is.True,
                        station.StableId);
                    Assert.That(
                        hit.point.y - buried.y,
                        Is.GreaterThanOrEqualTo(0.02f),
                        $"{station.StableId} rock seam is exposed.");
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        [Test]
        [Category("CityMountain")]
        public void DifferentSeed_KeepsOpeningsButVariesPeaks()
        {
            CityMountainBoundaryPlan first =
                CityMountainBoundaryPlanner.Create(CreateDefaultLayout());
            CityLayout otherLayout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed + 1);
            CityMountainBoundaryPlan second =
                CityMountainBoundaryPlanner.Create(otherLayout);

            Assert.That(second.RidgeCount, Is.EqualTo(first.RidgeCount));
            Assert.That(
                second.Tunnel.PortalBounds,
                Is.EqualTo(first.Tunnel.PortalBounds));
            Assert.That(
                second.RiverNotch.OpeningBounds,
                Is.EqualTo(first.RiverNotch.OpeningBounds));
            Assert.That(
                first.Ridges
                    .SelectMany(item => item.Stations)
                    .Zip(
                        second.Ridges.SelectMany(item => item.Stations),
                        (left, right) =>
                            Mathf.Abs(left.PeakY - right.PeakY))
                    .Any(delta => delta > 0.01f),
                Is.True);
        }

        [Test]
        [Category("CityMountain")]
        public void LegacyAndCustomBlueprints_StayOptOut()
        {
            CityGenerationSettings settings = CityGenerationSettings.Default;
            CityLayout legacy = CityLayoutGenerator.Generate(
                settings,
                GameSessionState.DefaultCitySeed);
            CityBlueprint custom = CityBlueprintBuilder.From(
                    CityBlueprintCatalog.Default,
                    "custom-coastal-copy")
                .Build();
            CityLayout customLayout = CityLayoutGenerator.Generate(
                custom,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            AssertEmpty(CityMountainBoundaryPlanner.Create(legacy));
            AssertEmpty(CityMountainBoundaryPlanner.Create(customLayout));
            Assert.DoesNotThrow(() =>
                CityMountainBoundaryValidator.ValidateOrThrow(
                    legacy,
                    CityMountainBoundaryPlan.Empty));
            Assert.DoesNotThrow(() =>
                CityMountainBoundaryValidator.ValidateOrThrow(
                    customLayout,
                CityMountainBoundaryPlan.Empty));
        }

        [Test]
        [Category("CityMountain")]
        public void MountainFog_KeepsDistantSilhouetteFaintAndLetsCloseDetailEmerge()
        {
            float closeVisibility = CityMountainSurfaceAppearance
                .EvaluatePhysicalFogVisibility(
                    CityMountainSurfaceAppearance.NativeFogNearDistance *
                    0.5f);
            float nativeNearVisibility = CityMountainSurfaceAppearance
                .EvaluatePhysicalFogVisibility(
                    CityMountainSurfaceAppearance.NativeFogNearDistance);
            float nativeFarVisibility = CityMountainSurfaceAppearance
                .EvaluatePhysicalFogVisibility(
                    CityMountainSurfaceAppearance.NativeFogFarDistance);
            Assert.That(closeVisibility, Is.GreaterThan(0.85f));
            Assert.That(
                nativeNearVisibility,
                Is.GreaterThan(nativeFarVisibility));

            float[] distantSamples =
            {
                CityMountainSurfaceAppearance.PhysicalHandoffNearDistance,
                CityMountainBackdropWorldBuilder.NearLayerRadius,
                CityMountainSurfaceAppearance.PhysicalHandoffFarDistance,
                CityMountainBackdropWorldBuilder.FarLayerRadius,
                RuntimeSceneSetup.CityFarClipPlane
            };
            for (int index = 0; index < distantSamples.Length; index++)
            {
                Assert.That(
                    CityMountainSurfaceAppearance
                        .EvaluatePhysicalFogVisibility(
                            distantSamples[index]),
                    Is.EqualTo(
                        CityMountainSurfaceAppearance
                            .PhysicalVisibilityFloor)
                        .Within(0.001f),
                    distantSamples[index].ToString("F2"));
            }

            Assert.That(
                CityMountainSurfaceAppearance.PhysicalVisibilityFloor,
                Is.LessThanOrEqualTo(0.10f));
            Assert.That(
                closeVisibility -
                CityMountainSurfaceAppearance.PhysicalVisibilityFloor,
                Is.GreaterThan(0.70f));

            Assert.That(
                CityMountainSurfaceAppearance.PhysicalHandoffNearDistance,
                Is.LessThan(
                    CityMountainBackdropWorldBuilder.NearLayerRadius));
            Assert.That(
                CityMountainSurfaceAppearance.PhysicalHandoffFarDistance,
                Is.GreaterThan(
                    CityMountainBackdropWorldBuilder.NearLayerRadius));
            Assert.That(
                CityMountainSurfaceAppearance.PhysicalHandoffFarDistance,
                Is.LessThan(RuntimeSceneSetup.CityFarClipPlane));

            const float epsilon = 0.001f;
            float nearLeft = CityMountainSurfaceAppearance
                .EvaluatePhysicalFogVisibility(
                    CityMountainSurfaceAppearance.NativeFogNearDistance -
                    epsilon);
            float nearRight = CityMountainSurfaceAppearance
                .EvaluatePhysicalFogVisibility(
                    CityMountainSurfaceAppearance.NativeFogNearDistance +
                    epsilon);
            float farLeft = CityMountainSurfaceAppearance
                .EvaluatePhysicalFogVisibility(
                    CityMountainSurfaceAppearance.NativeFogFarDistance -
                    epsilon);
            float farRight = CityMountainSurfaceAppearance
                .EvaluatePhysicalFogVisibility(
                    CityMountainSurfaceAppearance.NativeFogFarDistance +
                    epsilon);
            Assert.That(Mathf.Abs(nearLeft - nearRight), Is.LessThan(0.01f));
            Assert.That(Mathf.Abs(farLeft - farRight), Is.LessThan(0.01f));

            Material physicalMaterial =
                CityMountainSurfaceAppearance.PhysicalRidgeMaterial;
            Assert.That(
                physicalMaterial.HasProperty("_VisibilityFloor"),
                Is.True);
            Assert.That(
                physicalMaterial.GetFloat("_VisibilityFloor"),
                Is.EqualTo(
                    CityMountainSurfaceAppearance.PhysicalVisibilityFloor)
                    .Within(0.001f));

            Material backdropMaterial =
                CityMountainBackdropResources.SharedMaterial;
            Assert.That(
                backdropMaterial.HasProperty("_HazeStrength"),
                Is.True);
            Assert.That(
                backdropMaterial.GetFloat("_HazeStrength"),
                Is.EqualTo(CityMountainBackdropResources.HazeStrength)
                    .Within(0.001f));
        }

        [Test]
        [Category("CityMountain")]
        public void WorldBuilders_CreatePhysicalClosureAndPresentationOnlyRim()
        {
            CityLayout layout = CreateDefaultLayout();
            CityMountainBoundaryPlan plan =
                CityMountainBoundaryPlanner.Create(layout);
            var host = new GameObject("Mountain Boundary Test Host");
            var cameraObject = new GameObject("Mountain Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.transform.position = new Vector3(12f, 7f, -18f);

            try
            {
                GameObject physical =
                    CityMountainBoundaryWorldBuilder.Build(
                        host.transform,
                        layout,
                        plan);
                CityMountainBackdropWorldResult backdrop =
                    CityMountainBackdropWorldBuilder.Build(
                        host.transform,
                        camera);

                Assert.That(physical, Is.Not.Null);
                Transform ridges = FindChild(
                    physical.transform,
                    "Physical Ridges");
                MeshRenderer[] ridgeRenderers =
                    ridges.GetComponentsInChildren<MeshRenderer>(true);
                Assert.That(ridgeRenderers.Length, Is.GreaterThanOrEqualTo(6));
                Assert.That(ridgeRenderers.Length, Is.LessThanOrEqualTo(24));
                Material ridgeMaterial =
                    CityMountainSurfaceAppearance.PhysicalRidgeMaterial;
                Assert.That(
                    ridgeRenderers.All(item =>
                        item.sharedMaterial ==
                        ridgeMaterial &&
                        item.shadowCastingMode == ShadowCastingMode.Off),
                    Is.True);
                Assert.That(
                    ridgeRenderers.Select(item => item.sharedMaterial)
                        .Distinct()
                        .Count(),
                    Is.EqualTo(1));
                Assert.That(
                    ridgeMaterial.shader.name,
                    Is.EqualTo("Bar Promenade/City Mountain Physical"));
                Assert.That(
                    ridgeMaterial.FindPass("MountainPhysical"),
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    ridgeMaterial.FindPass("DepthOnly"),
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    ridgeMaterial.FindPass("DepthNormalsOnly"),
                    Is.GreaterThanOrEqualTo(0));
                Assert.That(
                    ridgeMaterial.GetFloat("_VisibilityFloor"),
                    Is.EqualTo(
                        CityMountainSurfaceAppearance
                            .PhysicalVisibilityFloor)
                        .Within(0.001f));
                Assert.That(
                    ridgeMaterial.GetFloat("_HandoffNear"),
                    Is.EqualTo(
                        CityMountainSurfaceAppearance
                            .PhysicalHandoffNearDistance)
                        .Within(0.001f));
                Assert.That(
                    ridgeMaterial.GetFloat("_HandoffFar"),
                    Is.EqualTo(
                        CityMountainSurfaceAppearance
                            .PhysicalHandoffFarDistance)
                        .Within(0.001f));
                MeshCollider[] toeColliders =
                    ridges.GetComponentsInChildren<MeshCollider>(true);
                Assert.That(
                    toeColliders.Length,
                    Is.EqualTo(ridgeRenderers.Length));
                Assert.That(
                    toeColliders.All(item =>
                        item.GetComponent<Renderer>() == null),
                    Is.True,
                    "Only the invisible near-toe surface may collide.");

                Transform gate = FindChild(
                    physical.transform,
                    "Sealed Mountain Gate");
                Assert.That(gate.GetComponent<MeshCollider>(), Is.Not.Null);
                Transform portal = FindChild(
                    physical.transform,
                    "Mountain Tunnel Portal");
                Assert.That(
                    portal.GetComponent<MeshRenderer>().sharedMaterial,
                    Is.EqualTo(RuntimePrimitiveFactory.DefaultMaterial),
                    "The close tunnel stub must not dither with the ridge.");
                Assert.That(
                    physical.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    physical.GetComponentsInChildren<MonoBehaviour>(true)
                        .Any(item =>
                            item is IInteractable ||
                            item is SceneTransitionService),
                    Is.False);

                Assert.That(backdrop.Root, Is.Not.Null);
                Assert.That(backdrop.RidgeRenderers.Count, Is.EqualTo(4));
                Assert.That(
                    backdrop.Root.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(
                    backdrop.Root.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    backdrop.RidgeRenderers
                        .Select(item => item.sharedMaterial)
                        .Distinct()
                        .Count(),
                    Is.EqualTo(1));
                Assert.That(
                    backdrop.RidgeRenderers.All(item =>
                        item.shadowCastingMode == ShadowCastingMode.Off &&
                        (item.name.Contains("West") ||
                         item.name.Contains("South")) &&
                        !item.name.Contains("East") &&
                        !item.name.Contains("North")),
                    Is.True);
                foreach (MeshRenderer south in backdrop.RidgeRenderers
                             .Where(item => item.name.Contains("South")))
                {
                    Vector3[] vertices = south
                        .GetComponent<MeshFilter>()
                        .sharedMesh
                        .vertices;
                    float[] riverAxisHeights = vertices
                        .Where(item =>
                            item.z < 0f && Mathf.Abs(item.x) < 0.05f)
                        .Select(item => item.y)
                        .ToArray();
                    Assert.That(riverAxisHeights.Length, Is.GreaterThan(1));
                    Assert.That(
                        riverAxisHeights.Max() - riverAxisHeights.Min(),
                        Is.LessThan(0.02f),
                        "The distant south rim closes the river gorge.");
                }

                backdrop.Follower.AlignToCamera(camera);
                Assert.That(
                    backdrop.Root.transform.position,
                    Is.EqualTo(camera.transform.position));
            }
            finally
            {
                Object.DestroyImmediate(host);
                Object.DestroyImmediate(cameraObject);
            }
        }

        [Test]
        [Category("CityMountain")]
        public void PhysicalRidges_BuryRockSeamAcrossEveryOwnedTerrainToe()
        {
            CityLayout layout = CreateDefaultLayout();
            CityMountainBoundaryPlan mountainPlan =
                CityMountainBoundaryPlanner.Create(layout);
            CityFringeYardPlan fringePlan =
                CityFringeYardPlanner.Create(layout, mountainPlan);
            var host = new GameObject("Mountain Toe Seam Test Host");

            try
            {
                CityFringeYardGroundWorldResult ground =
                    CityFringeYardGroundWorldBuilder.Build(
                        host.transform,
                        layout,
                        fringePlan);
                GameObject physical =
                    CityMountainBoundaryWorldBuilder.Build(
                        host.transform,
                        layout,
                        mountainPlan);

                Assert.That(ground.MountainGround, Is.Not.Null);
                Assert.That(
                    ground.MountainGround.GetComponent<MeshCollider>(),
                    Is.Not.Null);
                Transform ridges = FindChild(
                    physical.transform,
                    "Physical Ridges");
                string[] ownerAreaIds = mountainPlan.Ridges
                    .Where(ridge => !ridge.IsSouthWestJoin)
                    .Select(ridge => ridge.SourceAreaId)
                    .Distinct()
                    .ToArray();
                Assert.That(
                    ownerAreaIds,
                    Is.EquivalentTo(new[]
                    {
                        CityMountainBoundaryDefinition.WestSouthAreaId,
                        CityMountainBoundaryDefinition.WestNorthAreaId,
                        CityMountainBoundaryDefinition.SouthWestAreaId,
                        CityMountainBoundaryDefinition.SouthEastAreaId
                    }));

                foreach (CityMountainRidgeDescriptor ridge in
                         mountainPlan.Ridges.Where(item =>
                             !item.IsSouthWestJoin))
                {
                    Transform ridgeRoot = FindChild(
                        ridges,
                        ridge.StableId);
                    MeshRenderer[] renderers = ridgeRoot
                        .GetComponentsInChildren<MeshRenderer>(true)
                        .OrderBy(item => item.name)
                        .ToArray();
                    MeshCollider[] colliders = ridgeRoot
                        .GetComponentsInChildren<MeshCollider>(true)
                        .OrderBy(item => item.name)
                        .ToArray();
                    Assert.That(
                        colliders,
                        Has.Length.EqualTo(renderers.Length),
                        ridge.StableId);

                    Vector3[] colliderVertices = colliders
                        .SelectMany(collider =>
                            collider.sharedMesh.vertices.Select(vertex =>
                                collider.transform.TransformPoint(vertex)))
                        .ToArray();
                    foreach (CityMountainRidgeStation station in
                             ridge.Stations)
                    {
                        Vector3 anchor = station.Toe;
                        Vector3 buried = anchor -
                            station.OutwardNormal *
                            CityMountainBoundaryMeshFactory
                                .ToeGroundOverlap +
                            Vector3.down *
                            CityMountainBoundaryMeshFactory
                                .ToeGroundBurial;
                        // Physical render meshes discard their CPU copy
                        // after upload. Assert the exact cross-section fed
                        // to every rendered quad, then prove below that all
                        // five of its bands were emitted for every chunk.
                        Vector3[] renderedCross =
                            CityMountainBoundaryMeshFactory
                                .CreateCrossSection(station);
                        AssertContainsVertex(
                            renderedCross,
                            anchor,
                            $"{station.StableId} rendered toe anchor");
                        AssertContainsVertex(
                            renderedCross,
                            buried,
                            $"{station.StableId} rendered buried seam");
                        AssertContainsVertex(
                            colliderVertices,
                            anchor,
                            $"{station.StableId} exact toe anchor");
                        AssertContainsVertex(
                            colliderVertices,
                            buried,
                            $"{station.StableId} buried seam");

                        CitySurfaceDescriptor owner = layout.Surfaces.First(
                            surface =>
                                surface.Kind ==
                                CitySurfaceKind.OpenGround &&
                                surface.AreaId == ridge.SourceAreaId &&
                                ContainsInclusive(
                                    surface.WorldBounds,
                                    new Vector2(
                                        buried.x,
                                        buried.z)));
                        float terrainTop = CityTerrainSurfacePlan.SampleTop(
                            layout,
                            owner,
                            new Vector2(buried.x, buried.z));
                        Assert.That(
                            terrainTop - buried.y,
                            Is.GreaterThanOrEqualTo(0.02f),
                            $"{station.StableId} seam must stay buried " +
                            "below its owner terrain.");
                    }

                    for (int chunkIndex = 0;
                         chunkIndex < renderers.Length;
                         chunkIndex++)
                    {
                        Mesh renderedMesh = renderers[chunkIndex]
                            .GetComponent<MeshFilter>()
                            .sharedMesh;
                        int renderedTriangles = checked((int)
                            (renderedMesh.GetIndexCount(0) / 3u));
                        const int renderedTrianglesPerSegment = 10;
                        const int endCapTriangles = 8;
                        int segmentTriangles =
                            renderedTriangles - endCapTriangles;
                        Assert.That(
                            segmentTriangles,
                            Is.GreaterThan(0),
                            renderers[chunkIndex].name);
                        Assert.That(
                            segmentTriangles %
                            renderedTrianglesPerSegment,
                            Is.Zero,
                            $"{renderers[chunkIndex].name} must close both " +
                            "six-point end caps.");
                        int segmentCount = segmentTriangles /
                                           renderedTrianglesPerSegment;
                        int colliderTriangles = checked((int)
                            (colliders[chunkIndex].sharedMesh
                                 .GetIndexCount(0) / 3u));
                        Assert.That(
                            colliderTriangles,
                            Is.EqualTo(segmentCount * 6),
                            $"{colliders[chunkIndex].name} must retain all " +
                            "three near-toe collision bands.");
                    }
                }
            }
            finally
            {
                Object.DestroyImmediate(host);
            }
        }

        private static CityLayout CreateDefaultLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        private static void AssertPlansEqual(
            CityMountainBoundaryPlan expected,
            CityMountainBoundaryPlan actual)
        {
            Assert.That(actual.RidgeCount, Is.EqualTo(expected.RidgeCount));
            for (int ridgeIndex = 0;
                 ridgeIndex < expected.Ridges.Count;
                 ridgeIndex++)
            {
                CityMountainRidgeDescriptor left =
                    expected.Ridges[ridgeIndex];
                CityMountainRidgeDescriptor right =
                    actual.Ridges[ridgeIndex];
                Assert.That(right.StableId, Is.EqualTo(left.StableId));
                Assert.That(right.SourceAreaId, Is.EqualTo(left.SourceAreaId));
                Assert.That(right.Side, Is.EqualTo(left.Side));
                Assert.That(
                    right.IsSouthWestJoin,
                    Is.EqualTo(left.IsSouthWestJoin));
                CollectionAssert.AreEqual(left.Stations, right.Stations);
            }

            Assert.That(actual.Tunnel, Is.EqualTo(expected.Tunnel));
            Assert.That(actual.RiverNotch, Is.EqualTo(expected.RiverNotch));
            Assert.That(
                actual.HasSouthWestCornerClosure,
                Is.EqualTo(expected.HasSouthWestCornerClosure));
            Assert.That(
                actual.SouthWestCornerClosure,
                Is.EqualTo(expected.SouthWestCornerClosure));
        }

        private static void AssertEmpty(CityMountainBoundaryPlan plan)
        {
            Assert.That(plan.IsEnabled, Is.False);
            Assert.That(plan.RidgeCount, Is.Zero);
            Assert.That(plan.HasSouthWestCornerClosure, Is.False);
            Assert.That(plan.SouthWestCornerClosure, Is.Null);
            Assert.That(plan.HasTunnel, Is.False);
            Assert.That(plan.HasRiverNotch, Is.False);
        }

        private static void AssertSouthOpeningShoulders(
            CityMountainBoundaryPlan plan,
            float firstX,
            float secondX,
            float z)
        {
            Assert.That(HasSouthEndpoint(plan, new Vector2(firstX, z)),
                Is.True);
            Assert.That(HasSouthEndpoint(plan, new Vector2(secondX, z)),
                Is.True);
        }

        private static bool HasSouthEndpoint(
            CityMountainBoundaryPlan plan,
            Vector2 target)
        {
            return plan.Ridges.Any(ridge =>
                ridge.Side == CityMountainBoundarySide.South &&
                (Vector2.Distance(ridge.StartXZ, target) < 0.02f ||
                 Vector2.Distance(ridge.EndXZ, target) < 0.02f));
        }

        private static CityMountainRidgeStation FindMatchingEndpoint(
            CityMountainBoundaryPlan plan,
            CityMountainBoundarySide side,
            Vector2 target)
        {
            return plan.Ridges
                .Where(ridge => !ridge.IsSouthWestJoin && ridge.Side == side)
                .SelectMany(ridge => new[]
                {
                    ridge.Stations[0],
                    ridge.Stations[ridge.Stations.Count - 1]
                })
                .Single(station =>
                    Vector2.Distance(station.WorldXZ, target) < 0.02f);
        }

        private static void AssertCrossSectionsWelded(
            CityMountainRidgeStation expected,
            CityMountainRidgeStation actual)
        {
            const float tolerance = 0.001f;
            Assert.That(
                Vector3.Distance(
                    expected.OutwardNormal,
                    actual.OutwardNormal),
                Is.LessThanOrEqualTo(tolerance));
            Vector3[] expectedCross =
                CityMountainBoundaryMeshFactory.CreateCrossSection(expected);
            Vector3[] actualCross =
                CityMountainBoundaryMeshFactory.CreateCrossSection(actual);
            Assert.That(actualCross, Has.Length.EqualTo(expectedCross.Length));
            for (int index = 0; index < expectedCross.Length; index++)
            {
                Assert.That(
                    Vector3.Distance(expectedCross[index], actualCross[index]),
                    Is.LessThanOrEqualTo(tolerance),
                    $"South-west join cross-section vertex {index} is open.");
            }
        }

        private static void AssertBoundaryMatchesTerrain(
            CityLayout layout,
            string areaId,
            System.Collections.Generic.IReadOnlyList<Vector3> samples)
        {
            foreach (Vector3 sample in samples)
            {
                CitySurfaceDescriptor owner = layout.Surfaces.First(surface =>
                    surface.Kind == CitySurfaceKind.OpenGround &&
                    surface.AreaId == areaId &&
                    ContainsInclusive(
                        surface.WorldBounds,
                        new Vector2(sample.x, sample.z)));
                float terrainTop = CityTerrainSurfacePlan.SampleTop(
                    layout,
                    owner,
                    new Vector2(sample.x, sample.z));
                Assert.That(
                    sample.y,
                    Is.EqualTo(terrainTop).Within(0.001f),
                    $"Corner closure boundary {sample} left its terrain.");
            }
        }

        private static bool RaycastDown(
            Collider collider,
            Vector3 point,
            out RaycastHit hit)
        {
            return collider.Raycast(
                new Ray(point + Vector3.up * 50f, Vector3.down),
                out hit,
                100f);
        }

        private static bool SegmentCrossesRect(
            Vector2 first,
            Vector2 second,
            Rect bounds)
        {
            const float tolerance = 0.02f;
            return Mathf.Min(first.x, second.x) <
                       bounds.xMax - tolerance &&
                   Mathf.Max(first.x, second.x) >
                       bounds.xMin + tolerance &&
                   Mathf.Min(first.y, second.y) <
                       bounds.yMax - tolerance &&
                   Mathf.Max(first.y, second.y) >
                       bounds.yMin + tolerance;
        }

        private static bool OverlapsStrict(Rect left, Rect right)
        {
            return left.xMin < right.xMax &&
                   left.xMax > right.xMin &&
                   left.yMin < right.yMax &&
                   left.yMax > right.yMin;
        }

        private static bool ContainsInclusive(Rect bounds, Vector2 point)
        {
            const float tolerance = 0.001f;
            return point.x >= bounds.xMin - tolerance &&
                   point.x <= bounds.xMax + tolerance &&
                   point.y >= bounds.yMin - tolerance &&
                   point.y <= bounds.yMax + tolerance;
        }

        private static void AssertContainsVertex(
            Vector3[] vertices,
            Vector3 expected,
            string message)
        {
            const float tolerance = 0.001f;
            Assert.That(
                vertices.Any(vertex =>
                    Vector3.Distance(vertex, expected) <= tolerance),
                Is.True,
                message);
        }

        private static Transform FindChild(Transform root, string name)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(
                true);
            return children.Single(item => item.name == name);
        }
    }
}
