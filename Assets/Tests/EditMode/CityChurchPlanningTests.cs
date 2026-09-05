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

            // The authored basilica is 23 x 32 x 44 m; the City places
            // that same prefab shrunk, so the town keeps a church it can
            // be looked at from its own pavement.
            Assert.That(
                new Vector3(
                    CityChurchPlanner.SourceModelWidth,
                    CityChurchPlanner.SourceModelHeight,
                    CityChurchPlanner.SourceModelLength),
                Is.EqualTo(new Vector3(23f, 32f, 44f)));
            Assert.That(
                CityChurchPlanner.ExteriorModelScale,
                Is.LessThan(1f));
            Assert.That(
                CityChurchPlanner.ModelLocalSize,
                Is.EqualTo(
                    new Vector3(23f, 32f, 44f) *
                    CityChurchPlanner.ExteriorModelScale));
            Assert.That(
                Vector3.Distance(
                    plan.ModelRotation * Vector3.forward,
                    Vector3.left),
                Is.LessThan(Tolerance));
            Assert.That(plan.AltarDirection, Is.EqualTo(Vector3.right));
            Assert.That(
                plan.ModelFootprint.width,
                Is.EqualTo(CityChurchPlanner.ModelLength).Within(Tolerance));
            Assert.That(
                plan.ModelFootprint.height,
                Is.EqualTo(CityChurchPlanner.ModelWidth).Within(Tolerance));
            Assert.That(
                plan.ModelFootprint.yMin - plan.Grounds.yMin,
                Is.GreaterThanOrEqualTo(
                    CityChurchPlanner.MinimumCemeteryClearance - Tolerance));
            Assert.That(
                plan.CemeteryClearance,
                Is.GreaterThanOrEqualTo(
                    CityChurchPlanner.MinimumCemeteryClearance - Tolerance));
            Vector3 transformedEntranceAnchor = plan.ModelRootPosition +
                plan.ModelRotation *
                CityChurchPlanner.ExteriorEntranceModelOffset;
            Assert.That(
                CityChurchPlanner.ExteriorEntranceAnchorLocalPosition,
                Is.EqualTo(new Vector3(0f, 0f, 22.05f)));
            Assert.That(
                CityChurchPlanner.ExteriorEntranceModelOffset,
                Is.EqualTo(
                    new Vector3(0f, 0f, 22.05f) *
                    CityChurchPlanner.ExteriorModelScale));
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
            // The nave is laid on the frontage's axis, so the walk in
            // from the street is straight rather than a dog-leg across
            // the forecourt.
            Assert.That(
                plan.DoorGroundPosition.z,
                Is.EqualTo(plan.ReturnPosition.z).Within(Tolerance));
            // Leaving the church stands the hero on its own forecourt,
            // a stride in from the frontage: the access point itself is
            // on the street's outer edge, where the pavement is still
            // two decimetres above the church ground.
            Assert.That(
                plan.ReturnPosition,
                Is.EqualTo(new Vector3(
                    plan.Access.Center.x +
                    CityChurchPlanner.CityReturnInsetFromFrontage,
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

        /// <summary>
        /// The forecourt paving carries no collider, so a dock measured
        /// from its top stands where nobody can. The door action refuses
        /// an entry pose further than InteractionVerticalTolerance from
        /// the hero's own root, so four centimetres of decorative slab
        /// is the whole difference between a door that opens and a
        /// prompt that does nothing when it is pressed.
        /// </summary>
        [Test]
        [Category("CityChurch")]
        public void DoorDock_StandsOnTheCollideredChurchGround()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityChurchPlan plan = CityChurchPlanner.Create(layout);

            CitySurfaceDescriptor ground = layout.Surfaces.First(surface =>
                surface.Kind == CitySurfaceKind.ChurchGround);
            float standing =
                ground.PhysicalTopY + PlayerFactory.GroundedRootOffset;

            Assert.That(
                plan.GroundTopY,
                Is.EqualTo(ground.PhysicalTopY).Within(Tolerance));
            Assert.That(
                plan.DoorDockPosition.y,
                Is.EqualTo(standing).Within(
                    PlayerMotor.InteractionVerticalTolerance),
                "The hero can only start the door action from a dock " +
                "within the interaction tolerance of where he stands.");
            Assert.That(
                plan.ReturnPosition.y,
                Is.EqualTo(standing).Within(
                    PlayerMotor.InteractionVerticalTolerance));
            Assert.That(
                CityChurchPlanner.ApproachSurfaceTopAboveGround,
                Is.LessThan(PlayerFactory.GroundedRootOffset),
                "Collider-free paving must stay under the controller's " +
                "own skin, or the hero visibly wades through it.");

            // Standing at the dock, the hero's whole capsule has to be
            // inside the forecourt rather than balanced on its lip.
            Assert.That(
                plan.ApproachBounds.xMin +
                CityGroundTraversalPlanner.MaximumAgentRadius,
                Is.LessThanOrEqualTo(plan.DoorDockPosition.x));
            Assert.That(
                plan.ApproachBounds.xMax -
                CityGroundTraversalPlanner.MaximumAgentRadius,
                Is.GreaterThanOrEqualTo(plan.DoorDockPosition.x));
            Assert.That(
                Vector3.Distance(
                    plan.DoorDockPosition,
                    plan.InteractionPosition) -
                Mathf.Abs(
                    plan.DoorDockPosition.y -
                    plan.InteractionPosition.y),
                Is.LessThan(Tolerance),
                "The City's ordinary doors put the dock and the prompt " +
                "at one point; the church now does too.");
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
        [Category("CityChurchCourtyard")]
        public void GardenGrade_JoinsNorthYardAndClosesOnlyRealBoundaries()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityChurchPlan church = CityChurchPlanner.Create(layout);
            CitySurfaceDescriptor[] grounds = layout.Surfaces.Where(
                surface => surface.Kind == CitySurfaceKind.ChurchGround)
                .ToArray();
            RoadWalkableArea walkable = RoadWalkableArea.FromLayout(layout);

            // The complete occupied garden, including bench approaches,
            // remains level; the reserve behind it pays for the grade.
            for (float x = 0.5f; x <= 44f; x += 2.5f)
            {
                for (float z = 0.5f; z <= 38f; z += 2.5f)
                {
                    Vector2 point = church.Grounds.min + new Vector2(x, z);
                    Assert.That(CityTerrainSurfacePlan.TrySampleGroundTop(
                        layout, point, out float top, out _), Is.True);
                    Assert.That(top, Is.EqualTo(church.GroundTopY)
                        .Within(Tolerance));
                }
            }

            CitySurfaceDescriptor[] north = grounds.Where(surface =>
                Mathf.Abs(surface.WorldBounds.yMax - church.Grounds.yMax) <
                Tolerance).ToArray();
            Assert.That(north, Has.Length.EqualTo(4));
            foreach (CitySurfaceDescriptor surface in north)
            {
                CitySurfaceDescriptor neighbour = layout.Surfaces.Single(
                    candidate => candidate.Cell == surface.Cell + Vector2Int.up);
                for (int sample = 0; sample <= 8; sample++)
                {
                    Vector2 point = new Vector2(Mathf.Lerp(
                        surface.WorldBounds.xMin, surface.WorldBounds.xMax,
                        sample / 8f), church.Grounds.yMax);
                    float top = CityTerrainSurfacePlan.SampleTop(layout,
                        surface, point);
                    Assert.That(top, Is.EqualTo(CityTerrainSurfacePlan
                        .SampleTop(layout, neighbour, point)).Within(Tolerance),
                        "The north seam must meet real terrain on both sides.");
                    if (sample > 0 && sample < 8)
                    {
                        Assert.That(walkable.Contains(
                            new Vector3(point.x, top, point.y),
                            CityGroundTraversalPlanner.MaximumAgentRadius),
                            Is.True, "Open grass cannot hide a traversal seam.");
                    }
                }
            }

            var fences = CityChurchGroundPlan.CreateFenceSpans(layout, church);
            Assert.That(fences, Is.Not.Empty);
            float openingMinimum = church.Access.Center.z -
                church.Access.Width * 0.5f;
            float openingMaximum = church.Access.Center.z +
                church.Access.Width * 0.5f;
            float eastLength = 0f;
            foreach (CityChurchGroundFenceSpan span in fences)
            {
                if (span.First.x - church.Grounds.xMin < 0.2f &&
                    span.Second.x - church.Grounds.xMin < 0.2f)
                {
                    Assert.That(span.Second.z + 0.09f <=
                        openingMinimum + Tolerance ||
                        span.First.z - 0.09f >= openingMaximum - Tolerance,
                        Is.True, "The sole west aperture stays fully clear.");
                }

                if (church.Grounds.xMax - span.First.x < 0.2f &&
                    church.Grounds.xMax - span.Second.x < 0.2f)
                {
                    eastLength += span.Second.z - span.First.z;
                }

                Assert.That(Mathf.Abs(span.First.z - church.Grounds.yMax) <
                    Tolerance && Mathf.Abs(span.Second.z -
                    church.Grounds.yMax) < Tolerance, Is.False,
                    "The healed northern seam must not acquire a fence.");
            }

            Assert.That(eastLength, Is.EqualTo(church.Grounds.height)
                .Within(Tolerance), "The east map limit is physically readable.");

            var owner = new GameObject("Church Garden Grade Test");
            try
            {
                GameObject built = CityChurchGroundWorldBuilder.Build(
                    owner.transform, layout);
                Mesh mesh = built.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(built.GetComponent<MeshCollider>().sharedMesh,
                    Is.SameAs(mesh));
                Vector3[] vertices = mesh.vertices;
                Vector3[] normals = mesh.normals;
                for (int index = 0; index < vertices.Length; index++)
                {
                    if (normals[index].y <= 0f)
                    {
                        continue;
                    }

                    Vector3 point = vertices[index];
                    CitySurfaceDescriptor source = grounds.First(surface =>
                        point.x >= surface.WorldBounds.xMin - Tolerance &&
                        point.x <= surface.WorldBounds.xMax + Tolerance &&
                        point.z >= surface.WorldBounds.yMin - Tolerance &&
                        point.z <= surface.WorldBounds.yMax + Tolerance);
                    float top = CityTerrainSurfacePlan.SampleTop(layout,
                        source, new Vector2(point.x, point.z));
                    Assert.That(point.y, Is.EqualTo(top).Within(Tolerance),
                        "Rendered and collider terrain use the sampled grade.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
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
                // The placer shrinks the prefab; the anchor stays at its
                // authored prefab-local position and travels with it.
                model.transform.localScale =
                    Vector3.one * CityChurchPlanner.ExteriorModelScale;
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
