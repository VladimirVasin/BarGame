using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityOpenAreaDecorationPlannerTests
    {
        /// <summary>
        /// The pass-wide contract, now that every dressed precinct has
        /// moved out to its own planner the way the cemetery and the
        /// seacoast did: this plan is deterministic, inside its budget,
        /// valid, and never stands anything in a street approach. The
        /// seacoast's own version of the same guarantees lives in
        /// CitySeacoastPlannerTests.
        /// </summary>
        [Test]
        public void DefaultCity_CreatesDeterministicClearYardDressing()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            CityOpenAreaDecorationPlan first =
                CityOpenAreaDecorationPlanner.Create(layout);
            CityOpenAreaDecorationPlan second =
                CityOpenAreaDecorationPlanner.Create(layout);

            CollectionAssert.AreEqual(
                first.Descriptors,
                second.Descriptors);
            Assert.That(first.Count, Is.GreaterThan(0));
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(
                    CityOpenAreaDecorationPlan.MaximumPartCount));

            // Nothing coast-shaped survives in this pass.
            Assert.That(
                first.Descriptors.Any(item =>
                    item.Feature ==
                    CityAreaFeatureKind.NorthWaterfront),
                Is.False,
                "The seacoast dresses itself.");

            CityOpenAreaDecorationPlanner.ValidateOrThrow(layout, first);
            foreach (CityOpenAreaDecorationDescriptor descriptor in
                     first.Descriptors.Where(item => item.BlocksMovement))
            {
                // Several yards share one feature, so every access of the
                // matching feature has to stay clear, not just the first.
                foreach (CityOpenAreaAccessDescriptor access in
                         layout.OpenAreaAccesses.Where(item =>
                             item.Feature == descriptor.Feature))
                {
                    Assert.That(
                        Overlaps(
                            ToXZRect(descriptor.Bounds),
                            access.ApproachBounds),
                        Is.False,
                        descriptor.StableId);
                }
            }
        }

        [Test]
        public void DefaultCity_DressesTheRoadlessGapDirectlyLeftOfABar()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);

            CityOpenAreaDecorationPlan first =
                CityOpenAreaDecorationPlanner.Create(layout);
            CityOpenAreaDecorationPlan second =
                CityOpenAreaDecorationPlanner.Create(layout);

            CollectionAssert.AreEqual(
                first.Descriptors,
                second.Descriptors);
            Assert.That(first.HomeYardSite.HasValue, Is.True);
            Assert.That(first.YardSpotlight.HasValue, Is.True);
            Assert.That(
                first.HomeYardSite,
                Is.EqualTo(second.HomeYardSite));
            Assert.That(
                first.YardSpotlight,
                Is.EqualTo(second.YardSpotlight));
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(
                    CityOpenAreaDecorationPlan.MaximumPartCount));

            HomeYardSitePlan site = first.HomeYardSite.Value;
            HomeYardSpotlightDescriptor spotlight =
                first.YardSpotlight.Value;
            Assert.That(site.Home, Is.SameAs(layout.PlayerHome));
            Assert.That(site.Anchor, Is.Not.Null);
            Assert.That(site.Anchor.IsBar, Is.True);
            Assert.That(
                layout.BuildingLots.Single(lot =>
                    lot.Cell == site.AnchorCell),
                Is.SameAs(site.Anchor));
            Assert.That(layout.PlayerHome, Is.Not.Null);
            Assert.That(
                site.AnchorCell,
                Is.EqualTo(
                    layout.PlayerHome.Cell +
                    layout.PlayerHome.FrontageDirection));
            Assert.That(
                site.Anchor.FrontageDirection,
                Is.EqualTo(-layout.PlayerHome.FrontageDirection));
            Assert.That(
                layout.HasRoad(RoadEdge.ForCellFrontage(
                    layout.PlayerHome.Cell,
                    layout.PlayerHome.FrontageDirection)),
                Is.True,
                "The selected bar must share the player's home frontage.");
            Assert.That(site.HasNeighbourBuilding, Is.True);
            Assert.That(site.Neighbour, Is.Not.Null);
            Assert.That(site.Neighbour.IsSupermarket, Is.True);
            Assert.That(site.Neighbour.Cell, Is.EqualTo(site.NeighbourCell));
            Vector2Int neighbourOffset =
                site.NeighbourCell - site.AnchorCell;
            Assert.That(
                neighbourOffset,
                Is.EqualTo(Vector2Int.left));
            Assert.That(
                site.DirectionFromAnchorToNeighbour,
                Is.EqualTo(Vector2Int.left));
            Assert.That(
                layout.HasRoad(RoadEdge.ForCellFrontage(
                    site.AnchorCell,
                    site.DirectionFromAnchorToNeighbour)),
                Is.False,
                "The authored yard must remain directly left of the bar " +
                "on its roadless side.");
            Assert.That(
                site.Neighbour.Center.x,
                Is.LessThan(site.Anchor.Center.x));
            float neighbourFace = site.Neighbour.Center.x +
                                  site.Neighbour.Size.x * 0.5f;
            float barFace = site.Anchor.Center.x -
                            site.Anchor.Size.x * 0.5f;
            Assert.That(
                site.GroundBounds.xMin,
                Is.GreaterThan(neighbourFace));
            Assert.That(site.GroundBounds.xMax, Is.LessThan(barFace));
            Assert.That(
                site.GroundBounds.Contains(new Vector2(
                    site.RingCenter.x,
                    site.RingCenter.z)),
                Is.True);
            Assert.That(
                site.RingCenter.y,
                Is.EqualTo(site.GroundY).Within(0.0001f));

            Assert.That(
                spotlight.StableId,
                Is.EqualTo(HomeYardSpotlightPlanner.StableId));
            Assert.That(
                spotlight.NeighbourCell,
                Is.EqualTo(site.NeighbourCell));
            Assert.That(
                spotlight.TargetPosition,
                Is.EqualTo(
                    site.RingCenter +
                    Vector3.up * HomeYardSpotlightPlanner.AimHeight));
            Vector3 facadeNormal = spotlight.FacadeNormal.normalized;
            Assert.That(
                facadeNormal,
                Is.EqualTo(site.NeighbourFacadeNormal.normalized));
            Vector3 neighbourFrontage = new Vector3(
                site.Neighbour.FrontageDirection.x,
                0f,
                site.Neighbour.FrontageDirection.y).normalized;
            Assert.That(
                Mathf.Abs(Vector3.Dot(facadeNormal, neighbourFrontage)),
                Is.LessThan(0.001f));
            float facadeHalfExtent = Mathf.Abs(facadeNormal.x) > 0.5f
                ? site.Neighbour.Size.x * 0.5f
                : site.Neighbour.Size.y * 0.5f;
            float authoredFacadeInset = site.Neighbour.IsSupermarket
                ? SupermarketEntranceGeometry.ExteriorWallInset
                : 0f;
            Assert.That(
                Vector3.Dot(
                    spotlight.MountPosition - site.Neighbour.Center,
                    facadeNormal),
                Is.EqualTo(
                    facadeHalfExtent -
                    authoredFacadeInset +
                    HomeYardSpotlightPlanner.MountProudOffset)
                    .Within(0.001f));
            Assert.That(
                spotlight.MountPosition.y,
                Is.GreaterThanOrEqualTo(
                    site.Neighbour.Center.y +
                    HomeYardSpotlightPlanner.MinimumMountHeight));
            Assert.That(
                spotlight.MountPosition.y,
                Is.LessThanOrEqualTo(
                    site.Neighbour.Center.y +
                    site.Neighbour.Height -
                    HomeYardSpotlightPlanner.RoofClearance + 0.001f));
            Vector3 aimDirection =
                (spotlight.TargetPosition - spotlight.MountPosition)
                .normalized;
            Assert.That(
                Vector3.Angle(
                    spotlight.Rotation * Vector3.forward,
                    aimDirection),
                Is.LessThan(0.05f));
            Assert.That(
                spotlight.Intensity,
                Is.EqualTo(HomeYardSpotlightPlanner.Intensity));
            Assert.That(
                HomeYardSpotlightPlanner.Intensity,
                Is.EqualTo(240f),
                "The yard key must stay far stronger than any ordinary " +
                "street practical.");
            Assert.That(spotlight.Color.r, Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(spotlight.Color.g, Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(spotlight.Color.b, Is.GreaterThanOrEqualTo(0.9f));
            Assert.That(spotlight.Range, Is.GreaterThan(0f));
            Assert.That(
                HomeYardSpotlightPlanner.RangeMultiplier,
                Is.EqualTo(1.5f),
                "The far arc must stay comfortably ahead of range falloff.");
            Assert.That(spotlight.InnerSpotAngle, Is.GreaterThan(0f));
            Assert.That(
                spotlight.SpotAngle,
                Is.GreaterThan(spotlight.InnerSpotAngle));
            Assert.That(
                spotlight.SpotAngle - spotlight.InnerSpotAngle,
                Is.EqualTo(HomeYardSpotlightPlanner.ConeFeatherAngle)
                    .Within(0.001f),
                "The full circuit needs a broad hot core and only a " +
                "narrow theatrical feather.");
            Assert.That(spotlight.SpotAngle, Is.LessThan(180f));
            AssertSpotlightCoversCircuit(site, spotlight);

            CityOpenAreaDecorationDescriptor[] yardParts =
                first.Descriptors
                    .Where(item =>
                        item.Feature == CityAreaFeatureKind.Yard)
                    .ToArray();
            Assert.That(yardParts, Is.Not.Empty);
            Assert.That(
                yardParts.All(item =>
                    item.StableId.StartsWith(
                        "home-yard-",
                        StringComparison.Ordinal)),
                Is.True,
                "Only the single authored bar-side yard is dressed in " +
                "this pass.");

            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardDeadTree),
                Is.EqualTo(3));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardChildToy),
                Is.EqualTo(1));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardBottle),
                Is.EqualTo(1));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardBench),
                Is.EqualTo(3));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardCarpetFrame),
                Is.EqualTo(3));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardSandpit),
                Is.EqualTo(4));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardDeadLamp),
                Is.EqualTo(2));
            Assert.That(
                first.GetCount(CityOpenAreaDecorationKind.YardBin),
                Is.EqualTo(2));

            // Nothing may stand inside either building: the yard is the
            // gap between them.
            Rect barFootprint = ToXZRect(site.Anchor);
            Rect neighbourFootprint = ToXZRect(site.Neighbour);
            foreach (CityOpenAreaDecorationDescriptor part in yardParts)
            {
                Assert.That(
                    Overlaps(ToXZRect(part.Bounds), barFootprint),
                    Is.False,
                    $"{part.StableId} stands inside the bar.");
                Assert.That(
                    Overlaps(ToXZRect(part.Bounds), neighbourFootprint),
                    Is.False,
                    $"{part.StableId} stands inside the neighbouring " +
                    "building.");
            }

            // The middle of the circuit stays free for the rider: only the
            // dead tree stands inside the ring.
            CityOpenAreaDecorationDescriptor trunk = yardParts.Single(item =>
                string.Equals(
                    item.StableId,
                    "home-yard-tree-trunk",
                    StringComparison.Ordinal));
            var ringCenter = new Vector2(
                trunk.Bounds.center.x,
                trunk.Bounds.center.z);
            foreach (CityOpenAreaDecorationDescriptor part in yardParts)
            {
                if (part.Kind == CityOpenAreaDecorationKind.YardDeadTree)
                {
                    continue;
                }

                float distance = Vector2.Distance(
                    ringCenter,
                    new Vector2(part.Bounds.center.x, part.Bounds.center.z));
                Assert.That(
                    distance,
                    Is.GreaterThan(3.4f),
                    $"{part.StableId} stands on the circuit.");
            }

            // This is the yard against the bar's left wall: everything
            // must be within a few steps of it.
            var bar = new Vector2(
                site.Anchor.Center.x,
                site.Anchor.Center.z);
            foreach (CityOpenAreaDecorationDescriptor part in yardParts)
            {
                float distance = Vector2.Distance(
                    bar,
                    new Vector2(part.Bounds.center.x, part.Bounds.center.z));
                Assert.That(
                    distance,
                    Is.LessThan(22f),
                    $"{part.StableId} is not directly left of the bar.");
            }

            Assert.That(
                Vector2.Distance(
                    bar,
                    new Vector2(trunk.Bounds.center.x, trunk.Bounds.center.z)),
                Is.LessThan(16f),
                "The dead tree must stand directly left of the bar.");

            GameObject owner = new GameObject("Home Yard Test World");
            try
            {
                GameObject world = CityOpenAreaWorldBuilder.Build(
                    owner.transform,
                    first);
                Transform collisionProxies = world.transform.Find(
                    "Open Area Collision Proxies");
                Assert.That(collisionProxies, Is.Not.Null);
                int expectedBlockingParts = first.Descriptors.Count(
                    descriptor => descriptor.BlocksMovement);
                BoxCollider[] yardColliders = collisionProxies
                    .GetComponents<BoxCollider>();
                Assert.That(
                    yardColliders,
                    Has.Length.EqualTo(expectedBlockingParts));
                Assert.That(
                    yardColliders.All(collider =>
                        collider.enabled && !collider.isTrigger),
                    Is.True);

                Transform fixture = world.transform.Find(
                    "Home Yard Spotlight");
                Assert.That(fixture, Is.Not.Null);
                Assert.That(
                    Vector3.Distance(
                        fixture.position,
                        spotlight.MountPosition),
                    Is.LessThan(0.001f));

                Light[] lights = world.GetComponentsInChildren<Light>(true);
                Assert.That(lights, Has.Length.EqualTo(1));
                Light light = lights[0];
                Assert.That(light.name, Is.EqualTo(
                    "Home Yard Spotlight Light"));
                Assert.That(light.type, Is.EqualTo(LightType.Spot));
                Assert.That(light.enabled, Is.True);
                Assert.That(light.color, Is.EqualTo(spotlight.Color));
                Assert.That(
                    light.intensity,
                    Is.EqualTo(spotlight.Intensity).Within(0.001f));
                Assert.That(
                    light.range,
                    Is.EqualTo(spotlight.Range).Within(0.001f));
                Assert.That(
                    light.spotAngle,
                    Is.EqualTo(spotlight.SpotAngle).Within(0.001f));
                Assert.That(
                    light.innerSpotAngle,
                    Is.EqualTo(spotlight.InnerSpotAngle).Within(0.001f));
                Assert.That(light.shadows, Is.EqualTo(LightShadows.Hard));
                Assert.That(light.shadowStrength, Is.EqualTo(0.95f));
                Assert.That(light.shadowBias, Is.EqualTo(0.05f));
                Assert.That(light.shadowNormalBias, Is.EqualTo(0.25f));
                Assert.That(light.shadowNearPlane, Is.EqualTo(0.20f));
                UniversalAdditionalLightData additionalLightData =
                    light.GetComponent<UniversalAdditionalLightData>();
                Assert.That(additionalLightData, Is.Not.Null);
                Assert.That(
                    additionalLightData
                        .additionalLightsShadowResolutionTier,
                    Is.EqualTo(UniversalAdditionalLightData
                        .AdditionalLightsShadowResolutionTierHigh));
                Assert.That(
                    light.renderMode,
                    Is.EqualTo(LightRenderMode.ForcePixel));
                Assert.That(
                    light.lightmapBakeType,
                    Is.EqualTo(LightmapBakeType.Realtime));
                Assert.That(light.bounceIntensity, Is.EqualTo(0.1f));
                Assert.That(light.cullingMask, Is.EqualTo(~0));
                Assert.That(
                    Vector3.Distance(
                        light.transform.position,
                        spotlight.MountPosition),
                    Is.LessThan(0.001f));
                Assert.That(
                    Vector3.Angle(light.transform.forward, aimDirection),
                    Is.LessThan(0.01f));

                Transform lens = fixture.Find(
                    "Spotlight Head/Spotlight Lens");
                Assert.That(lens, Is.Not.Null);
                Renderer lensRenderer = lens.GetComponent<Renderer>();
                Assert.That(
                    lensRenderer.sharedMaterial,
                    Is.SameAs(CityNightResources.EmissiveMaterial));
                var lensProperties = new MaterialPropertyBlock();
                lensRenderer.GetPropertyBlock(lensProperties);
                Assert.That(
                    lensProperties.GetColor(
                        Shader.PropertyToID("_BaseColor"))
                        .maxColorComponent,
                    Is.GreaterThanOrEqualTo(4.75f));
                CityLightHalo halo =
                    light.GetComponentInChildren<CityLightHalo>(true);
                Assert.That(halo, Is.Not.Null);
                Assert.That(halo.IsVisible, Is.True);
                Assert.That(halo.IntensityFactor, Is.EqualTo(1f));
                Assert.That(
                    halo.HaloRenderer.sharedMaterial,
                    Is.SameAs(CityNightResources.AtmosphereMaterial));
                var haloParticles = new ParticleSystem.Particle[
                    CityLightHalo.ParticleCount];
                int haloParticleCount = halo.Particles.GetParticles(
                    haloParticles);
                Assert.That(
                    haloParticleCount,
                    Is.EqualTo(CityLightHalo.ParticleCount));
                Assert.That(
                    haloParticles.Max(item => item.startSize),
                    Is.GreaterThanOrEqualTo(1.79f));
                Assert.That(
                    haloParticles.Max(item => item.startColor.a),
                    Is.GreaterThanOrEqualTo(45));
                Assert.That(
                    fixture.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                foreach (Renderer renderer in
                         fixture.GetComponentsInChildren<Renderer>(true))
                {
                    Assert.That(
                        renderer.shadowCastingMode,
                        Is.EqualTo(ShadowCastingMode.Off),
                        renderer.name);
                    Assert.That(renderer.receiveShadows, Is.False);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }

            Assert.DoesNotThrow(() =>
                CityOpenAreaDecorationPlanner.ValidateOrThrow(layout, first));
        }

        [Test]
        public void BarSideYard_KeepsItsDressingOffTheLeaningUtilities()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityOpenAreaDecorationPlan plan =
                CityOpenAreaDecorationPlanner.Create(layout);
            Assert.That(plan.HomeYardSite.HasValue, Is.True);
            HomeYardSitePlan site = plan.HomeYardSite.Value;

            // The same anchors the city decoration planner consumes:
            // the yard dressing must have kept every slot object off
            // that reserved ground.
            var reserved = new List<Rect>(2);
            Assert.That(
                HomeYardUtilityPlanner.TryCreatePhoneBooth(
                    layout,
                    site,
                    out HomeYardUtilityAnchor booth),
                Is.True,
                "The default yard must host its phone booth.");
            reserved.Add(booth.Footprint);
            Assert.That(
                HomeYardUtilityPlanner.TryCreateDumpster(
                    layout,
                    site,
                    out HomeYardUtilityAnchor dumpster),
                Is.True,
                "The default yard must host its dumpster.");
            reserved.Add(dumpster.Footprint);

            foreach (CityOpenAreaDecorationDescriptor part in
                     plan.Descriptors.Where(item =>
                         item.Feature == CityAreaFeatureKind.Yard))
            {
                foreach (Rect footprint in reserved)
                {
                    Assert.That(
                        Overlaps(ToXZRect(part.Bounds), footprint),
                        Is.False,
                        $"{part.StableId} stands on utility ground.");
                }
            }
        }

        private static void AssertSpotlightCoversCircuit(
            HomeYardSitePlan site,
            HomeYardSpotlightDescriptor spotlight)
        {
            Vector3 forward = spotlight.Rotation * Vector3.forward;
            float hotHalfAngle = spotlight.InnerSpotAngle * 0.5f;
            float protectedRange = Mathf.Min(
                spotlight.Range - HomeYardSpotlightPlanner.RangeMargin,
                spotlight.Range / HomeYardSpotlightPlanner.RangeMultiplier);
            float coverageRadius =
                site.RingRadius +
                HomeYardSpotlightPlanner.RadialCoverageMargin;
            for (int index = 0; index < 32; index++)
            {
                float angle = Mathf.PI * 2f * index / 32f;
                var radial = new Vector3(
                    Mathf.Cos(angle) * coverageRadius,
                    0f,
                    Mathf.Sin(angle) * coverageRadius);
                for (int heightIndex = 0; heightIndex < 2; heightIndex++)
                {
                    Vector3 sample =
                        site.RingCenter +
                        radial +
                        Vector3.up * (
                            heightIndex *
                            HomeYardSpotlightPlanner.ChairCoverageHeight);
                    Vector3 delta =
                        sample - spotlight.MountPosition;
                    Assert.That(
                        delta.magnitude,
                        Is.LessThanOrEqualTo(protectedRange + 0.001f),
                        $"Circuit sample {index}/{heightIndex} escaped the " +
                        "protected hot range.");
                    Assert.That(
                        Vector3.Angle(forward, delta),
                        Is.LessThanOrEqualTo(hotHalfAngle + 0.001f),
                        $"Circuit sample {index}/{heightIndex} is outside " +
                        "the bright inner cone.");
                }
            }
        }

        private static Rect ToXZRect(Bounds bounds)
        {
            return Rect.MinMaxRect(
                bounds.min.x,
                bounds.min.z,
                bounds.max.x,
                bounds.max.z);
        }

        private static Rect ToXZRect(BuildingLot lot)
        {
            return Rect.MinMaxRect(
                lot.Center.x - lot.Size.x * 0.5f,
                lot.Center.z - lot.Size.y * 0.5f,
                lot.Center.x + lot.Size.x * 0.5f,
                lot.Center.z + lot.Size.y * 0.5f);
        }

        private static bool Overlaps(Rect left, Rect right)
        {
            return left.xMin < right.xMax &&
                   left.xMax > right.xMin &&
                   left.yMin < right.yMax &&
                   left.yMax > right.yMin;
        }

        private static float RectDistance(Rect left, Rect right)
        {
            float x = Mathf.Max(
                0f,
                Mathf.Max(right.xMin - left.xMax, left.xMin - right.xMax));
            float y = Mathf.Max(
                0f,
                Mathf.Max(right.yMin - left.yMax, left.yMin - right.yMax));
            return Mathf.Sqrt(x * x + y * y);
        }
    }
}
