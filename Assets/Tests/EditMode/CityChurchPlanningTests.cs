using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityChurchPlanningTests
    {
        private const float Tolerance = 0.001f;

        [Test]
        [Category("CityChurch")]
        public void DefaultBlueprint_SplitsChurchFromEastYardWithoutMovingCity()
        {
            CityGenerationSettings settings =
                CityGenerationSettings.Default;
            CityBlueprint blueprint = CityBlueprintCatalog.Default;

            Assert.That(
                blueprint.TryGetArea(
                    CityChurchPlanner.DefaultAreaId,
                    out CityAreaPlacement church),
                Is.True);
            Assert.That(
                church.Cells,
                Is.EquivalentTo(Cells(new RectInt(13, 2, 4, 2))));
            Assert.That(
                church.Definition.Archetype,
                Is.EqualTo(CityDistrictKind.Church));
            Assert.That(
                church.Definition.Feature,
                Is.EqualTo(CityAreaFeatureKind.Church));
            Assert.That(
                church.Cells.All(cell => !blueprint.CreatesLot(cell)),
                Is.True);

            Assert.That(
                blueprint.TryGetArea(
                    "yard-east",
                    out CityAreaPlacement yard),
                Is.True);
            Assert.That(
                yard.Cells,
                Is.EquivalentTo(Cells(new RectInt(13, 4, 4, 4))));
            Assert.That(
                blueprint.TryGetArea(
                    "cemetery",
                    out CityAreaPlacement cemetery),
                Is.True);
            Assert.That(
                cemetery.Cells,
                Is.EquivalentTo(Cells(new RectInt(13, 0, 3, 2))));
            Assert.That(blueprint.Cells, Has.Count.EqualTo(248));

            CityLayout layout = CityLayoutGenerator.Generate(
                blueprint,
                settings,
                GameSessionState.DefaultCitySeed);
            Assert.That(layout.BuildingLots, Has.Count.EqualTo(144));
            Assert.That(layout.BlockCount, Is.EqualTo(new Vector2Int(17, 14)));
            Assert.DoesNotThrow(layout.ValidateOrThrow);
        }

        [Test]
        [Category("CityChurch")]
        public void DefaultPlan_UsesSouthernWestStreetAndModelContract()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityChurchPlan plan = CityChurchPlanner.Create(layout);

            Assert.That(plan, Is.Not.Null);
            Assert.That(plan.Cells, Has.Count.EqualTo(8));
            Assert.That(plan.Access.Cell, Is.EqualTo(new Vector2Int(13, 2)));
            Assert.That(
                plan.Access.FrontageEdge,
                Is.EqualTo(new RoadEdge(
                    new Vector2Int(13, 2),
                    new Vector2Int(13, 3))));
            Assert.That(
                plan.Access.StreetSideDirection,
                Is.EqualTo(Vector2Int.left));
            Assert.That(
                layout.GetPathKind(plan.Access.FrontageEdge),
                Is.EqualTo(CityPathKind.Street));
            Assert.That(plan.Access.OutwardNormal, Is.EqualTo(Vector3.right));

            Assert.That(
                CityChurchPlanner.ModelLocalSize,
                Is.EqualTo(new Vector3(23f, 32f, 44f)));
            Assert.That(
                Vector3.Distance(
                    plan.ModelRotation * Vector3.forward,
                    Vector3.left),
                Is.LessThan(Tolerance));
            Assert.That(plan.AltarDirection, Is.EqualTo(Vector3.right));
            Assert.That(plan.ModelFootprint.width, Is.EqualTo(44f));
            Assert.That(plan.ModelFootprint.height, Is.EqualTo(23f));
            Assert.That(
                plan.ModelFootprint.yMin - plan.Grounds.yMin,
                Is.EqualTo(5f).Within(Tolerance));
            Assert.That(
                plan.CemeteryClearance,
                Is.EqualTo(5f).Within(Tolerance));
            Vector3 transformedEntranceAnchor = plan.ModelRootPosition +
                plan.ModelRotation *
                CityChurchPlanner.ExteriorEntranceAnchorLocalPosition;
            Assert.That(
                CityChurchPlanner.ExteriorEntranceAnchorLocalPosition,
                Is.EqualTo(new Vector3(0f, 0f, 22.05f)));
            Assert.That(
                new Vector2(
                    plan.DoorGroundPosition.x,
                    plan.DoorGroundPosition.z),
                Is.EqualTo(new Vector2(
                    transformedEntranceAnchor.x,
                    transformedEntranceAnchor.z)));
            Assert.That(
                plan.DoorGroundPosition.z,
                Is.EqualTo(plan.ModelFootprint.center.y).Within(Tolerance));
            Assert.That(
                plan.DoorGroundPosition.z,
                Is.Not.EqualTo(plan.ReturnPosition.z).Within(Tolerance));
            Assert.That(
                plan.ReturnPosition,
                Is.EqualTo(new Vector3(
                    plan.Access.Center.x,
                    plan.GroundTopY + PlayerFactory.GroundedRootOffset,
                    plan.Access.Center.z)));
            Assert.That(
                plan.ApproachBounds.Contains(new Vector2(
                    plan.ReturnPosition.x,
                    plan.ReturnPosition.z)),
                Is.True);
            Assert.That(
                plan.ApproachBounds.Contains(new Vector2(
                    plan.DoorDockPosition.x,
                    plan.DoorDockPosition.z)),
                Is.True);
            Assert.That(
                plan.DoorAction.InteractionPosition,
                Is.EqualTo(plan.InteractionPosition));
            Assert.That(
                plan.DoorAction.EntryRootPosition,
                Is.EqualTo(plan.DoorDockPosition));
            Assert.That(
                plan.DoorAction.EntryFacingDirection,
                Is.EqualTo(plan.AltarDirection));
            Assert.DoesNotThrow(
                () => CityChurchPlanner.ValidateOrThrow(layout, plan));
        }

        [Test]
        [Category("CityChurch")]
        public void DefaultPlan_UsesTypedWalkableGroundAndMapRegion()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityChurchPlan plan = CityChurchPlanner.Create(layout);
            CitySurfaceDescriptor[] surfaces = layout.Surfaces
                .Where(surface =>
                    surface.Feature == CityAreaFeatureKind.Church)
                .ToArray();

            Assert.That(surfaces, Has.Length.EqualTo(8));
            Assert.That(
                surfaces.All(surface =>
                    surface.Kind == CitySurfaceKind.ChurchGround &&
                    surface.IsWalkable),
                Is.True);
            RoadWalkableArea walkable =
                RoadWalkableArea.FromLayout(layout);
            Assert.That(
                walkable.Contains(
                    plan.ReturnPosition,
                    CityGroundTraversalPlanner.MaximumAgentRadius),
                Is.True);
            Assert.That(
                walkable.Contains(
                    plan.DoorDockPosition,
                    CityGroundTraversalPlanner.MaximumAgentRadius),
                Is.True);

            CityMapAreaRegion region = CityMapAreaOverlayBuilder
                .Create(layout)
                .Single(candidate =>
                    candidate.Feature == CityAreaFeatureKind.Church);
            Assert.That(region.AreaId, Is.EqualTo("church"));
            Assert.That(region.LandBounds, Has.Count.EqualTo(8));
            Assert.That(region.WaterBounds, Is.Empty);
            Assert.That(region.Gates, Has.Count.EqualTo(1));
            Assert.That(region.IsUrban, Is.False);
        }

        [Test]
        [Category("CityChurch")]
        public void ResidualEastYard_StaysValidAndClearOfChurchSite()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityChurchPlan church = CityChurchPlanner.Create(layout);
            CityMountainBoundaryPlan mountains =
                CityMountainBoundaryPlanner.Create(layout);
            CityFringeYardPlan fringe =
                CityFringeYardPlanner.Create(layout, mountains);

            Assert.That(
                fringe.TryGetYard(
                    "yard-east",
                    out CityFringeYardDescriptor eastYard),
                Is.True);
            Assert.That(
                eastYard.Kind,
                Is.EqualTo(CityFringeYardKind.EastUtilityEdge));
            Assert.That(
                eastYard.Parts.All(part =>
                    !part.Footprint.Overlaps(church.Grounds)),
                Is.True,
                "The residual utility yard cannot dress the church site.");
        }

        [Test]
        [Category("CityChurch")]
        public void ExteriorAnchorValidator_RejectsVisibleDoorDrift()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityChurchPlan plan = CityChurchPlanner.Create(layout);
            var model = new GameObject("Synthetic Church Exterior");
            var anchor = new GameObject("Synthetic Entrance Anchor");
            try
            {
                ChurchAssetRegistry registry =
                    model.AddComponent<ChurchAssetRegistry>();
                registry.Configure(
                    ChurchAssetKind.Exterior,
                    model.transform,
                    anchor.transform,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<Renderer>(),
                    Array.Empty<ChurchRendererBinding>(),
                    new Bounds(Vector3.zero, Vector3.one),
                    new ChurchDimensions(23f, 44f, 32f, 2f, 4f),
                    0,
                    "synthetic",
                    "catholic-basilica-test",
                    "synthetic-signature");
                model.transform.SetPositionAndRotation(
                    plan.ModelRootPosition,
                    plan.ModelRotation);
                anchor.transform.SetParent(model.transform, false);
                anchor.transform.localPosition =
                    CityChurchPlanner.ExteriorEntranceAnchorLocalPosition;

                Assert.DoesNotThrow(() =>
                    CityChurchWorldBuilder
                        .ValidateExteriorEntranceAnchor(registry, plan));

                anchor.transform.localPosition += Vector3.right * 0.01f;
                Assert.Throws<InvalidOperationException>(() =>
                    CityChurchWorldBuilder
                        .ValidateExteriorEntranceAnchor(registry, plan));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(model);
            }
        }

        [Test]
        [Category("CityChurch")]
        public void ChurchSessionReturn_IsExplicitAndConsumable()
        {
            GameSessionState.BeginNewGame();
            try
            {
                GameSessionState.EnterChurch();
                Assert.That(
                    GameSessionState.ReturnKind,
                    Is.EqualTo(CityReturnKind.None));
                GameSessionState.PrepareChurchReturn();
                Assert.That(
                    GameSessionState.ReturnKind,
                    Is.EqualTo(CityReturnKind.Church));
                Assert.That(GameSessionState.IsReturningToCity, Is.True);

                GameSessionState.CompleteCityReturn();
                Assert.That(
                    GameSessionState.ReturnKind,
                    Is.EqualTo(CityReturnKind.None));
            }
            finally
            {
                GameSessionState.BeginNewGame();
            }
        }

        [Test]
        public void ChurchEnums_AreAppendedWithoutRenumberingExistingValues()
        {
            Assert.That((int)CityDistrictKind.Yard, Is.EqualTo(8));
            Assert.That((int)CityDistrictKind.Church, Is.EqualTo(9));
            Assert.That((int)CityAreaFeatureKind.Yard, Is.EqualTo(5));
            Assert.That((int)CityAreaFeatureKind.Church, Is.EqualTo(6));
            Assert.That((int)CitySurfaceKind.RiverWater, Is.EqualTo(7));
            Assert.That((int)CitySurfaceKind.ChurchGround, Is.EqualTo(8));
            Assert.That((int)CityReturnKind.Supermarket, Is.EqualTo(3));
            Assert.That((int)CityReturnKind.Church, Is.EqualTo(4));
            Assert.That(
                CitySoundDistrictProfiles.Get(
                    CityDistrictKind.Church).AllowedCues,
                Is.Empty);
        }

        private static Vector2Int[] Cells(RectInt bounds)
        {
            return Enumerable.Range(bounds.yMin, bounds.height)
                .SelectMany(z => Enumerable.Range(bounds.xMin, bounds.width)
                    .Select(x => new Vector2Int(x, z)))
                .ToArray();
        }

    }
}
