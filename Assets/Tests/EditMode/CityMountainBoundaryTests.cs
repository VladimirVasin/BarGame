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
        public void PhysicalFogHandoff_KeepsReadableContrastAcrossBackdrop()
        {
            float[] distantSamples =
            {
                CityMountainSurfaceAppearance.NativeFogFarDistance,
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
                    Is.GreaterThanOrEqualTo(
                        CityMountainSurfaceAppearance
                            .PhysicalVisibilityFloor - 0.001f),
                    distantSamples[index].ToString("F2"));
            }

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
        }

        private static void AssertEmpty(CityMountainBoundaryPlan plan)
        {
            Assert.That(plan.IsEnabled, Is.False);
            Assert.That(plan.RidgeCount, Is.Zero);
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

        private static Transform FindChild(Transform root, string name)
        {
            Transform[] children = root.GetComponentsInChildren<Transform>(
                true);
            return children.Single(item => item.name == name);
        }
    }
}
