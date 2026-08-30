using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityCourtyardResidentTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        [Test]
        public void Plan_MapsOnlyActiveCourtyardAndFringeScenesUnderCap()
        {
            CityLayout layout = CreateLayout();
            CityDecorationPlan decorations = CreateDecorations(layout);
            CityFringeYardPlan fringe = CreateFringe(layout);

            CityCourtyardResidentPlan first =
                CityCourtyardResidentPlan.Create(
                    layout,
                    decorations,
                    fringe);
            CityCourtyardResidentPlan second =
                CityCourtyardResidentPlan.Create(
                    layout,
                    decorations,
                    fringe);

            Assert.That(first.IsPresent, Is.True);
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(
                    CityCourtyardResidentPlan.MaximumResidentCount));
            CollectionAssert.AreEqual(
                first.Residents.Select(item => item.StableId),
                second.Residents.Select(item => item.StableId));

            CityCourtyardResidentActivity[] fringeActivities =
            {
                CityCourtyardResidentActivity.Masonry,
                CityCourtyardResidentActivity.WinchService,
                CityCourtyardResidentActivity.FloodMaintenance,
                CityCourtyardResidentActivity.OpenHoodMechanic
            };
            foreach (CityCourtyardResidentActivity activity in
                     fringeActivities)
            {
                Assert.That(
                    first.Residents.Count(item =>
                        item.Activity == activity),
                    Is.EqualTo(1),
                    $"Fringe activity '{activity}' needs one resident.");
            }

            foreach (CityDecorationDescriptor pocket in
                     decorations.Descriptors.Where(item =>
                         item.Kind ==
                             CityDecorationKind.ResidentialCourtyardPocket))
            {
                int expected;
                switch (pocket.Variant)
                {
                    case CityCourtyardPocketGeometry.NardiVariant:
                        expected = 2;
                        break;
                    case CityCourtyardPocketGeometry.BicycleVariant:
                    case CityCourtyardPocketGeometry.ChairRepairVariant:
                    case CityCourtyardPocketGeometry.SweepingVariant:
                        expected = 1;
                        break;
                    default:
                        expected = 0;
                        break;
                }

                Assert.That(
                    first.Residents.Count(item =>
                        item.SourceStableId == pocket.StableId),
                    Is.EqualTo(expected),
                    $"Pocket variant {pocket.Variant} has the wrong cast.");
            }

            Assert.That(
                first.Residents.Any(item =>
                    SourceIsFringeKind(
                        fringe,
                        item.SourceStableId,
                        CityFringeYardPartKind.TunnelServiceSet)),
                Is.False,
                "The tunnel service set stays an empty still life.");

            var stableIds = new HashSet<string>();
            foreach (CityCourtyardResidentDescriptor resident in
                     first.Residents)
            {
                Assert.That(stableIds.Add(resident.StableId), Is.True);
                Assert.That(
                    CityCourtyardResidentPlan.IsAllowedDesignId(
                        resident.DesignId),
                    Is.True);
                Assert.That(
                    resident.DesignId,
                    Is.Not.EqualTo(
                        CityPedestrianResources.HelmetLampDesignId));
                Assert.That(
                    resident.DesignId,
                    Is.Not.EqualTo(
                        CityPedestrianResources.KettleHatDesignId));
                Assert.That(resident.Facing.y, Is.EqualTo(0f));
                Assert.That(
                    resident.Facing.magnitude,
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(resident.PaletteVariant, Is.InRange(0, 3));
                Assert.That(
                    CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout,
                        new Vector2(
                            resident.Position.x,
                            resident.Position.z),
                        out float ground,
                        out _),
                    Is.True);
                Assert.That(
                    resident.Position.y,
                    Is.EqualTo(ground).Within(0.001f));
            }
        }

        [Test]
        public void Factory_CreatesOneSilentColliderlessGenericResident()
        {
            CityLayout layout = CreateLayout();
            BuildingLot lot = layout.BuildingLots.First(item =>
                item.IsOrdinaryBuilding &&
                item.District == CityDistrictKind.Residential);
            var source = new CityDecorationDescriptor(
                "resident-factory-pocket",
                CityDecorationKind.ResidentialCourtyardPocket,
                CityDecorationAnchorKind.BuildingFrontage,
                CityDistrictKind.Residential,
                lot.Cell,
                lot.Center,
                Vector3.forward,
                CityCourtyardPocketGeometry.BicycleVariant,
                CityDecorationPalette.ResidentialCool,
                CityDecorationVisibilityTier.Near,
                CityDecorationCollisionTier.Blocking);
            var decorations = new CityDecorationPlan(
                Seed,
                new[] { source });
            CityCourtyardResidentPlan plan =
                CityCourtyardResidentPlan.Create(
                    layout,
                    decorations,
                    CityFringeYardPlan.Empty);
            var parent = new GameObject("Courtyard Resident Factory Test");
            try
            {
                IReadOnlyList<CityCourtyardResidentPresentation>
                    presentations = CityCourtyardResidentFactory.Create(
                        parent.transform,
                        plan);

                Assert.That(presentations, Has.Count.EqualTo(1));
                CityCourtyardResidentPresentation presentation =
                    presentations[0];
                Assert.That(presentation.IsInitialized, Is.True);
                Assert.That(presentation.Pedestrian.IsInitialized, Is.True);
                Assert.That(presentation.Pedestrian.IsMoving, Is.False);
                Assert.That(
                    presentation.GetComponentsInChildren<Collider>(true),
                    Is.Empty);
                Assert.That(
                    presentation.GetComponentsInChildren<Collider2D>(true),
                    Is.Empty);
                Assert.That(
                    presentation.GetComponentsInChildren<Rigidbody>(true),
                    Is.Empty);
                Assert.That(
                    presentation.GetComponentsInChildren<Rigidbody2D>(true),
                    Is.Empty);
                Assert.That(
                    presentation.GetComponentsInChildren<AudioSource>(true),
                    Is.Empty);
                Assert.That(
                    presentation.GetComponentsInChildren<Light>(true),
                    Is.Empty);
                Assert.That(
                    presentation.GetComponentsInChildren<Camera>(true),
                    Is.Empty);
                Assert.That(
                    presentation
                        .GetComponentsInChildren<MonoBehaviour>(true)
                        .Any(item => item is IInteractable),
                    Is.False);

                presentation.Shutdown();
                Assert.That(presentation.IsInitialized, Is.False);
                Assert.That(presentation.Pedestrian, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        private static CityLayout CreateLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
        }

        private static CityDecorationPlan CreateDecorations(
            CityLayout layout)
        {
            RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            return CityDecorationPlanner.CreatePlan(layout, fence, night);
        }

        private static CityFringeYardPlan CreateFringe(CityLayout layout)
        {
            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            return CityFringeYardPlanner.Create(layout, mountains);
        }

        private static bool SourceIsFringeKind(
            CityFringeYardPlan fringe,
            string stableId,
            CityFringeYardPartKind kind)
        {
            for (int yardIndex = 0;
                 yardIndex < fringe.Yards.Count;
                 yardIndex++)
            {
                foreach (CityFringeYardPartDescriptor part in
                         fringe.Yards[yardIndex].Parts)
                {
                    if (part.Kind == kind && part.StableId == stableId)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
