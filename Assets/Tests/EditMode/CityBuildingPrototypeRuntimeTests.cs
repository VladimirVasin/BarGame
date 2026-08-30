using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Focused runtime-integration proof for the four passive Blender
    /// building prototypes and their bounded Home reconstruction.
    /// </summary>
    public sealed class CityBuildingPrototypeRuntimeTests
    {
        private const float PositionTolerance = 0.003f;
        private const float AngleTolerance = 0.01f;

        private static readonly int BaseMapId =
            Shader.PropertyToID("_BaseMap");
        private static readonly int BaseMapTransformId =
            Shader.PropertyToID("_BaseMap_ST");

        private static readonly CityDistrictKind[] Districts =
        {
            CityDistrictKind.OldTown,
            CityDistrictKind.Residential,
            CityDistrictKind.Industrial,
            CityDistrictKind.Nightlife
        };

        [Test]
        public void
            DefaultCity_PlacesDistrictPrototypesAndClipsHomeExterior()
        {
            HomeExteriorContextPlan context =
                HomeExteriorContextPlanner.Generate(
                    GameSessionState.DefaultCitySeed);
            CityLayout layout = context.Layout;
            CityBuildingAssetProvider provider =
                CityBuildingAssetProvider.LoadOrThrow();
            Shader expectedWindowShader = Resources.Load<Shader>(
                CityBuildingWindowSlotAppearance.ShaderResourcePath);

            Assert.That(
                CityFacadeGrid.MassBaseElevation,
                Is.EqualTo(0.08f));
            Assert.That(expectedWindowShader, Is.Not.Null);
            Assert.That(
                expectedWindowShader.name,
                Is.EqualTo(
                    "Bar Promenade/City Building Window Slots"));

            var testRoot = new GameObject(
                "City Building Prototype Runtime Test");
            try
            {
                for (int index = 0; index < Districts.Length; index++)
                {
                    CityDistrictKind district = Districts[index];
                    BuildingLot lot = FindFrontagedOrdinaryLot(
                        layout,
                        district);
                    Transform building = new GameObject(
                        $"City Prototype Test {district}").transform;
                    building.SetParent(testRoot.transform, false);

                    float foundationDepth =
                        CityWorldBuilder.ResolveBuildingFoundationDepth(
                            layout,
                            lot);
                    CityBuildingAssetRegistry sourceRegistry = provider
                        .GetPrefabOrThrow(district)
                        .GetComponent<CityBuildingAssetRegistry>();
                    Assert.That(sourceRegistry, Is.Not.Null);
                    CityBuildingPrototypePose expectedPose =
                        CityBuildingPrototypePlacement.ResolveCityPose(
                            lot,
                            sourceRegistry);

                    CityBuildingAssetRegistry registry =
                        CityBuildingPrototypeWorldBuilder.BuildCity(
                            building,
                            lot,
                            layout.Seed,
                            foundationDepth);

                    AssertCityPrototype(
                        building,
                        lot,
                        registry,
                        sourceRegistry,
                        expectedPose,
                        layout.Seed,
                        foundationDepth,
                        expectedWindowShader);
                }

                AssertNoFrontagePlacement(provider);
                AssertSpecialCityBuildings(
                    testRoot.transform,
                    context);
                AssertDirectHomePlacement(
                    testRoot.transform,
                    context,
                    provider);
                AssertComposedHomeExterior(
                    testRoot.transform,
                    context,
                    provider);
            }
            finally
            {
                Object.DestroyImmediate(testRoot);
            }
        }

        private static void AssertCityPrototype(
            Transform building,
            BuildingLot lot,
            CityBuildingAssetRegistry registry,
            CityBuildingAssetRegistry sourceRegistry,
            CityBuildingPrototypePose expectedPose,
            int citySeed,
            float foundationDepth,
            Shader expectedWindowShader)
        {
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.District, Is.EqualTo(lot.District));
            Assert.That(
                registry.StableId,
                Is.EqualTo(sourceRegistry.StableId));
            Assert.That(
                registry.transform.name,
                Is.EqualTo(
                    CityBuildingPrototypeWorldBuilder
                        .PrototypeObjectName));
            AssertVectorNear(
                registry.transform.localPosition,
                expectedPose.Position);
            Assert.That(
                Quaternion.Angle(
                    registry.transform.localRotation,
                    expectedPose.Rotation),
                Is.LessThan(AngleTolerance));
            AssertVectorNear(
                registry.transform.localScale,
                Vector3.one);
            AssertVectorNear(
                registry.transform.lossyScale,
                Vector3.one);

            AssertAttachmentCatalogAndAnchors(
                lot,
                sourceRegistry,
                expectedPose);

            Vector3 expectedFrontAnchor =
                lot.DoorPosition +
                Vector3.up * CityFacadeGrid.MassBaseElevation;
            AssertVectorNear(
                registry.FrontAnchor.position,
                expectedFrontAnchor);
            Vector3 expectedForward = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y).normalized;
            Assert.That(
                Vector3.Angle(
                    registry.FrontAnchor.forward,
                    expectedForward),
                Is.LessThan(AngleTolerance));
            AssertVectorNear(
                CityBuildingPrototypePlacement.ResolveForward(lot),
                expectedForward);

            Transform collisionTransform = building.Find(
                CityBuildingPrototypeWorldBuilder
                    .LogicalCollisionObjectName);
            Assert.That(collisionTransform, Is.Not.Null);
            Assert.That(
                collisionTransform.GetComponent<Renderer>(),
                Is.Null,
                "The old building envelope must remain collision-only.");
            Assert.That(
                collisionTransform.GetComponent<MeshFilter>(),
                Is.Null,
                "The logical building envelope must own no primitive mesh.");
            BoxCollider collision =
                collisionTransform.GetComponent<BoxCollider>();
            Assert.That(collision, Is.Not.Null);
            Assert.That(collision.isTrigger, Is.False);
            Assert.That(
                building.GetComponentsInChildren<Collider>(true),
                Has.Length.EqualTo(1));
            AssertVectorNear(
                collision.bounds.center,
                lot.Center +
                Vector3.up *
                (lot.Height * 0.5f +
                 CityFacadeGrid.MassBaseElevation -
                 foundationDepth * 0.5f));
            AssertVectorNear(
                collision.bounds.size,
                new Vector3(
                    lot.Size.x,
                    lot.Height + foundationDepth,
                    lot.Size.y));

            Transform foundation = building.Find(
                CityBuildingPrototypeWorldBuilder.FoundationObjectName);
            Assert.That(foundation, Is.Not.Null);
            Renderer foundationRenderer =
                foundation.GetComponent<Renderer>();
            Assert.That(foundationRenderer, Is.Not.Null);
            Assert.That(foundation.GetComponent<Collider>(), Is.Null);
            Bounds visibleBounds = CityBuildingPrototypePlacement
                .TransformBounds(registry.LocalBounds, expectedPose);
            Assert.That(
                foundationRenderer.bounds.size.x,
                Is.EqualTo(
                    visibleBounds.size.x -
                    (CityBuildingPrototypeWorldBuilder
                        .FoundationHorizontalInset * 2f))
                    .Within(PositionTolerance));
            Assert.That(
                foundationRenderer.bounds.size.z,
                Is.EqualTo(
                    visibleBounds.size.z -
                    (CityBuildingPrototypeWorldBuilder
                        .FoundationHorizontalInset * 2f))
                    .Within(PositionTolerance));
            Assert.That(
                foundationRenderer.bounds.max.y,
                Is.EqualTo(visibleBounds.min.y + 0.04f)
                    .Within(PositionTolerance));
            AssertSurfaceBinding(
                foundationRenderer,
                lot.District,
                CityBuildingSurfaceKind.Plinth);

            int opaqueSurfaceCount = 0;
            for (int partIndex = 0;
                 partIndex < registry.Parts.Count;
                 partIndex++)
            {
                CityBuildingPartBinding binding =
                    registry.Parts[partIndex];
                if (binding.Role == CityBuildingMeshRole.WindowGlass)
                {
                    continue;
                }

                Assert.That(
                    CityBuildingSurfaceAppearance.TryResolveSurface(
                        lot.District,
                        binding.SurfaceKind,
                        out CityBuildingSurfaceKind surface),
                    Is.True,
                    binding.SourceName);
                AssertSurfaceBinding(
                    binding.Renderer,
                    lot.District,
                    surface);
                opaqueSurfaceCount++;
            }

            Assert.That(opaqueSurfaceCount, Is.EqualTo(6));

            Assert.That(
                registry.TryGetRenderer(
                    CityBuildingMeshRole.WindowGlass,
                    out Renderer windowGlass),
                Is.True);
            Assert.That(windowGlass, Is.Not.Null);
            Assert.That(windowGlass.sharedMaterial, Is.Not.Null);
            Assert.That(
                windowGlass.sharedMaterial.shader,
                Is.SameAs(expectedWindowShader));
            Assert.That(
                windowGlass.sharedMaterial.shader.name,
                Is.EqualTo(
                    "Bar Promenade/City Building Window Slots"));
            Assert.That(
                windowGlass.sharedMaterial.GetFloat(
                    "_EmissionStrength"),
                Is.EqualTo(CityWindowAppearance.EmissionStrength)
                    .Within(0.0001f));
            Assert.That(
                windowGlass.sharedMaterial.GetTexture("_BaseMap"),
                Is.SameAs(CityWindowAppearance.Texture));

            Assert.That(registry.WindowSlots, Is.Not.Empty);
            var slotIds = new HashSet<int>();
            for (int slotIndex = 0;
                 slotIndex < registry.WindowSlots.Count;
                 slotIndex++)
            {
                int slotId = registry.WindowSlots[slotIndex].Uv2SlotId;
                Assert.That(
                    slotId,
                    Is.EqualTo(
                        registry.WindowSlots[slotIndex].SlotId));
                Assert.That(
                    slotId,
                    Is.InRange(
                        1,
                        CityBuildingWindowSlotAppearance
                            .MaximumSlotCount - 1));
                Assert.That(
                    slotIds.Add(slotId),
                    Is.True,
                    $"Prototype '{registry.StableId}' repeats window " +
                    $"slot {slotId}.");
            }

            AssertWindowStateTable(
                windowGlass,
                registry,
                lot,
                citySeed);
        }

        private static void AssertSurfaceBinding(
            Renderer renderer,
            CityDistrictKind district,
            CityBuildingSurfaceKind surface)
        {
            Assert.That(renderer, Is.Not.Null);
            Assert.That(
                renderer.sharedMaterial,
                Is.SameAs(RuntimePrimitiveFactory.DefaultMaterial));
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Assert.That(
                properties.GetTexture(BaseMapId),
                Is.SameAs(
                    CityBuildingSurfaceAppearance.GetTexture(
                        district,
                        surface)));
            Assert.That(
                properties.GetTexture(BaseMapId),
                Is.Not.SameAs(Texture2D.whiteTexture));
            Assert.That(
                properties.GetVector(BaseMapTransformId),
                Is.EqualTo(new Vector4(1f, 1f, 0f, 0f)));
        }

        private static void AssertAttachmentCatalogAndAnchors(
            BuildingLot lot,
            CityBuildingAssetRegistry sourceRegistry,
            CityBuildingPrototypePose cityPose)
        {
            Bounds expectedRoof = CityBuildingPrototypePlacement
                .GetExpectedRoofAttachmentBounds(lot.District);
            AssertBoundsNear(
                sourceRegistry.RoofAttachmentBounds,
                expectedRoof);

            const float roofClearance = 0.18f;
            CityDecorationKind roofKind =
                GetExpectedRoofDecoration(lot.District);
            AssertVectorNear(
                CityBuildingPrototypePlacement.ResolveRoofAnchor(
                    lot,
                    roofKind,
                    roofClearance),
                cityPose.TransformPoint(
                    GetExpectedRoofMount(lot.District)) +
                Vector3.up * roofClearance);

            for (uint selector = 0u; selector < 2u; selector++)
            {
                AssertVectorNear(
                    CityBuildingPrototypePlacement.ResolveFacadeAnchor(
                        lot,
                        selector),
                    cityPose.TransformPoint(
                        GetExpectedFacadeMount(
                            lot.District,
                            selector)));
            }
        }

        private static CityDecorationKind GetExpectedRoofDecoration(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return CityDecorationKind
                        .OldTownChimneysAndDormers;
                case CityDistrictKind.Residential:
                    return CityDecorationKind
                        .ResidentialLaundryAndAntenna;
                case CityDistrictKind.Industrial:
                    return CityDecorationKind
                        .IndustrialStacksAndTanks;
                case CityDistrictKind.Nightlife:
                    return CityDecorationKind.NightlifeBillboard;
                default:
                    Assert.Fail(
                        $"District '{district}' has no roof decoration.");
                    return default;
            }
        }

        private static Vector3 GetExpectedRoofMount(
            CityDistrictKind district)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return new Vector3(-3.8f, 31.70f, 0f);
                case CityDistrictKind.Residential:
                    return new Vector3(-3.6f, 30.30f, -4.0f);
                case CityDistrictKind.Industrial:
                    return new Vector3(0f, 25.25f, 0f);
                case CityDistrictKind.Nightlife:
                    return new Vector3(-0.6f, 37.30f, 4.45f);
                default:
                    Assert.Fail(
                        $"District '{district}' has no roof mount.");
                    return default;
            }
        }

        private static Vector3 GetExpectedFacadeMount(
            CityDistrictKind district,
            uint selector)
        {
            switch (district)
            {
                case CityDistrictKind.OldTown:
                    return new Vector3(-3.8f, 4.2f, 6.79f);
                case CityDistrictKind.Residential:
                    return new Vector3(
                        (selector & 1u) == 0u ? -4.35f : 4.35f,
                        4.2f,
                        3.69f);
                case CityDistrictKind.Industrial:
                    return new Vector3(0f, 4.2f, 6.79f);
                case CityDistrictKind.Nightlife:
                    return new Vector3(0f, 4.2f, 6.04f);
                default:
                    Assert.Fail(
                        $"District '{district}' has no building prototype.");
                    return default;
            }
        }

        private static void AssertNoFrontagePlacement(
            CityBuildingAssetProvider provider)
        {
            var lot = new BuildingLot(
                new Vector2Int(101, 103),
                new Vector3(17f, 2.25f, -31f),
                new Vector2(14f, 13.5f),
                42f,
                Color.gray,
                "test.no-frontage",
                CityDistrictKind.OldTown,
                CityLandUseKind.Building,
                false,
                false,
                false,
                string.Empty,
                BarActivityKind.None,
                Vector2Int.zero,
                new Vector3(900f, 2.25f, 900f),
                Vector3.zero,
                Vector3.zero);
            CityBuildingAssetRegistry registry = provider
                .GetPrefabOrThrow(lot.District)
                .GetComponent<CityBuildingAssetRegistry>();
            Assert.That(registry, Is.Not.Null);

            CityBuildingPrototypePose pose =
                CityBuildingPrototypePlacement.ResolveCityPose(
                    lot,
                    registry);
            Assert.That(lot.HasRoadFrontage, Is.False);
            AssertVectorNear(
                pose.Position,
                lot.Center +
                Vector3.up * CityFacadeGrid.MassBaseElevation);
            AssertVectorNear(
                pose.Rotation * Vector3.forward,
                Vector3.back);
            AssertVectorNear(
                CityBuildingPrototypePlacement.ResolveForward(lot),
                Vector3.back);
        }

        private static void AssertWindowStateTable(
            Renderer windowGlass,
            CityBuildingAssetRegistry registry,
            BuildingLot lot,
            int citySeed)
        {
            var properties = new MaterialPropertyBlock();
            windowGlass.GetPropertyBlock(properties);
            float[] states = properties.GetFloatArray(
                Shader.PropertyToID("_CityBuildingWindowStates"));

            Assert.That(states, Is.Not.Null);
            Assert.That(
                states,
                Has.Length.EqualTo(
                    CityBuildingWindowSlotAppearance.MaximumSlotCount));
            Assert.That(states[0], Is.Zero);

            float maximumState =
                ((int)CityWindowFamily.Supermarket + 1) *
                CityWindowAppearance.VariantCount - 1;
            bool hasLitState = false;
            for (int index = 0;
                 index < registry.WindowSlots.Count;
                 index++)
            {
                CityBuildingWindowSlot slot =
                    registry.WindowSlots[index];
                int paneCount = registry.WindowSlots.Count(candidate =>
                    candidate.Floor == slot.Floor &&
                    string.Equals(
                        candidate.Side,
                        slot.Side,
                        System.StringComparison.Ordinal));
                CityWindowFamily family = CityExteriorAppearance
                    .ResolveWindowFamily(
                        lot,
                        citySeed,
                        slot.Floor,
                        slot.Bay,
                        paneCount,
                        ResolveWindowSide(slot.Side),
                        out uint paneHash);
                if (family != CityWindowFamily.Off)
                {
                    Assert.That(
                        family,
                        Is.EqualTo(CityWindowFamily.Warm),
                        $"Prototype '{registry.StableId}' gave slot " +
                        $"{slot.SlotId} a non-lantern colour.");
                }
                float expectedState =
                    ((int)family *
                     CityWindowAppearance.VariantCount) +
                    (int)((paneHash >> 8) %
                          (uint)CityWindowAppearance.VariantCount);
                float actualState = states[slot.Uv2SlotId];

                Assert.That(
                    actualState,
                    Is.InRange(0f, maximumState));
                Assert.That(
                    actualState,
                    Is.EqualTo(expectedState).Within(0.001f),
                    $"Window slot {slot.SlotId} in " +
                    $"'{registry.StableId}' lost its deterministic state.");
                hasLitState |= family != CityWindowFamily.Off;
            }

            Assert.That(
                hasLitState,
                Is.True,
                $"Prototype '{registry.StableId}' wrote no lit window " +
                "states into its material property block.");
            foreach (var row in registry.WindowSlots.GroupBy(slot =>
                         $"{slot.Side}:{slot.Floor}"))
            {
                int lit = row.Count(slot =>
                    Mathf.FloorToInt(
                        states[slot.Uv2SlotId] /
                        CityWindowAppearance.VariantCount) !=
                    (int)CityWindowFamily.Off);
                Assert.That(
                    lit,
                    Is.GreaterThanOrEqualTo(1),
                    $"Prototype '{registry.StableId}' left row " +
                    $"'{row.Key}' entirely dark.");
                if (row.Count() > 1)
                {
                    Assert.That(
                        lit,
                        Is.LessThan(row.Count()),
                        $"Prototype '{registry.StableId}' lit every pane " +
                        $"in row '{row.Key}'.");
                }
            }
        }

        private static int ResolveWindowSide(string side)
        {
            switch (side)
            {
                case "Front":
                    return 0;
                case "Rear":
                    return 1;
                case "Left":
                    return 2;
                case "Right":
                    return 3;
                default:
                    Assert.Fail($"Unknown window side '{side}'.");
                    return -1;
            }
        }

        private static void AssertDirectHomePlacement(
            Transform parent,
            HomeExteriorContextPlan context,
            CityBuildingAssetProvider provider)
        {
            BuildingLot lot = context.NearbyLots.FirstOrDefault(
                candidate =>
                    candidate.IsOrdinaryBuilding &&
                    CityBuildingPrototypeWorldBuilder
                        .ClassifyHomeExterior(context, candidate) ==
                    CityBuildingExteriorFit.Full);
            if (lot == null)
            {
                lot = context.NearbyLots.FirstOrDefault(
                    candidate => candidate.IsOrdinaryBuilding);
            }

            Assert.That(
                lot,
                Is.Not.Null,
                "The default Home context needs an ordinary building.");

            Transform directParent = new GameObject(
                "Direct Home Prototype Test").transform;
            directParent.SetParent(parent, false);
            CityBuildingAssetRegistry sourceRegistry = provider
                .GetPrefabOrThrow(lot.District)
                .GetComponent<CityBuildingAssetRegistry>();
            Assert.That(sourceRegistry, Is.Not.Null);
            CityBuildingPrototypePose cityPose =
                CityBuildingPrototypePlacement.ResolveCityPose(
                    lot,
                    sourceRegistry);
            CityBuildingPrototypePose homePose =
                CityBuildingPrototypePlacement.ResolveHomePose(
                    context.PlayerHome,
                    cityPose);
            float foundationDepth =
                CityWorldBuilder.ResolveBuildingFoundationDepth(
                    context.Layout,
                    lot);

            CityBuildingAssetRegistry registry =
                CityBuildingPrototypeWorldBuilder.BuildHomeExterior(
                    directParent,
                    context,
                    lot,
                    foundationDepth);

            Assert.That(registry.District, Is.EqualTo(lot.District));
            AssertVectorNear(
                registry.transform.localPosition,
                homePose.Position);
            Assert.That(
                Quaternion.Angle(
                    registry.transform.localRotation,
                    homePose.Rotation),
                Is.LessThan(AngleTolerance));
            AssertVectorNear(registry.transform.localScale, Vector3.one);
            AssertVectorNear(
                registry.FrontAnchor.position,
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    context.PlayerHome,
                    lot.DoorPosition +
                    Vector3.up *
                    CityFacadeGrid.MassBaseElevation));
            Assert.That(
                directParent.GetComponentsInChildren<Collider>(true),
                Is.Empty);
        }

        private static void AssertComposedHomeExterior(
            Transform parent,
            HomeExteriorContextPlan context,
            CityBuildingAssetProvider provider)
        {
            HomeInteriorLayoutPlan interior =
                HomeInteriorLayoutPlanner.Generate();
            HomeBalconyLayoutPlan balcony =
                HomeBalconyLayoutPlanner.Generate(interior);
            Transform exterior = HomeExteriorViewBuilder.Build(
                parent,
                balcony,
                context);
            Transform buildings = exterior.Find(
                "Home Exterior Building Silhouettes");

            Assert.That(buildings, Is.Not.Null);
            Assert.That(
                exterior.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The bounded Home street must stay presentation-only.");

            AssertSpecialHomeExterior(buildings, context);

            int expectedFull = 0;
            int expectedCrossing = 0;
            int expectedHidden = 0;
            int actualFull = 0;
            int actualCrossing = 0;
            int actualHidden = 0;
            for (int index = 0;
                 index < context.NearbyLots.Count;
                 index++)
            {
                BuildingLot lot = context.NearbyLots[index];
                if (!lot.IsOrdinaryBuilding)
                {
                    continue;
                }

                CityBuildingExteriorFit fit =
                    CityBuildingPrototypeWorldBuilder
                        .ClassifyHomeExterior(context, lot);
                Transform building = buildings.Find(
                    $"Exterior Building {lot.Cell.x}-{lot.Cell.y}");
                switch (fit)
                {
                    case CityBuildingExteriorFit.Full:
                        expectedFull++;
                        Assert.That(building, Is.Not.Null, lot.Cell.ToString());
                        CityBuildingAssetRegistry[] registries = building
                            .GetComponentsInChildren<
                                CityBuildingAssetRegistry>(true);
                        Assert.That(registries, Has.Length.EqualTo(1));
                        Assert.That(
                            registries[0].District,
                            Is.EqualTo(lot.District));
                        Assert.That(
                            registries[0].StableId,
                            Is.EqualTo(
                                provider.GetPrefabOrThrow(lot.District)
                                    .GetComponent<
                                        CityBuildingAssetRegistry>()
                                    .StableId));
                        Assert.That(
                            FindRenderer(
                                building,
                                "Exterior Building Mass"),
                            Is.Null,
                            $"Full Home prototype at {lot.Cell} retained " +
                            "the primitive mass renderer.");
                        AssertHomePrototypePose(
                            context,
                            lot,
                            provider,
                            registries[0]);
                        actualFull++;
                        break;

                    case CityBuildingExteriorFit.Crossing:
                        expectedCrossing++;
                        Assert.That(building, Is.Not.Null, lot.Cell.ToString());
                        Assert.That(
                            building.GetComponentsInChildren<
                                CityBuildingAssetRegistry>(true),
                            Is.Empty);
                        Assert.That(
                            FindRenderer(
                                building,
                                "Exterior Building Mass"),
                            Is.Not.Null,
                            $"Crossing Home lot {lot.Cell} lost its clipped " +
                            "fallback mass.");
                        actualCrossing++;
                        break;

                    default:
                        expectedHidden++;
                        Assert.That(
                            building,
                            Is.Null,
                            $"Hidden Home lot {lot.Cell} was composed.");
                        actualHidden++;
                        break;
                }
            }

            Assert.That(actualFull, Is.EqualTo(expectedFull));
            Assert.That(actualCrossing, Is.EqualTo(expectedCrossing));
            Assert.That(actualHidden, Is.EqualTo(expectedHidden));
            Assert.That(expectedFull, Is.GreaterThan(0));
            Assert.That(expectedHidden, Is.GreaterThan(0));
            Assert.That(
                expectedFull + expectedCrossing + expectedHidden,
                Is.EqualTo(
                    context.NearbyLots.Count(
                        lot => lot.IsOrdinaryBuilding)));
            Assert.That(
                buildings.GetComponentsInChildren<
                    CityBuildingAssetRegistry>(true),
                Has.Length.EqualTo(expectedFull));

            Renderer[] renderers =
                exterior.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers, Is.Not.Empty);
            float minimumX =
                HomeExteriorViewBuilder.ExteriorMinimumX -
                CityBuildingPrototypePlacement.BoundsTolerance;
            for (int index = 0; index < renderers.Length; index++)
            {
                Assert.That(
                    renderers[index].bounds.min.x,
                    Is.GreaterThanOrEqualTo(minimumX),
                    $"'{renderers[index].name}' crosses the Home facade.");
            }
        }

        private static void AssertSpecialCityBuildings(
            Transform parent,
            HomeExteriorContextPlan context)
        {
            BuildingLot[] lots = context.Layout.BuildingLots
                .Where(lot =>
                    lot.IsBar || lot.IsSupermarket || lot.IsPlayerHome)
                .ToArray();
            Assert.That(lots, Has.Length.EqualTo(3));
            for (int index = 0; index < lots.Length; index++)
            {
                BuildingLot lot = lots[index];
                Transform building = new GameObject(
                    $"Special Building Test {index}").transform;
                building.SetParent(parent, false);
                float foundationDepth = CityWorldBuilder
                    .ResolveBuildingFoundationDepth(
                        context.Layout,
                        lot);
                if (lot.IsBar)
                {
                    CitySpecialBuildingWorldBuilder
                        .BuildBarCityInfrastructure(
                            building,
                            lot,
                            foundationDepth);
                    CityBarFacadeWorldBuilder.BuildCity(building, lot);
                    AssertAuthoredBarCity(
                        building,
                        lot,
                        foundationDepth);
                    continue;
                }

                if (lot.IsSupermarket)
                {
                    CitySpecialBuildingWorldBuilder
                        .BuildSupermarketCityInfrastructure(
                            building,
                            lot,
                            foundationDepth);
                    CitySupermarketFacadeWorldBuilder.BuildCity(
                        building,
                        lot);
                    AssertAuthoredSupermarketCity(
                        building,
                        lot,
                        foundationDepth);
                    continue;
                }

                CitySpecialBuildingWorldBuilder
                    .BuildPlayerHomeCityInfrastructure(
                        building,
                        lot,
                        foundationDepth);
                PlayerHomeExteriorAssetRegistry registry =
                    CityPlayerHomeExteriorWorldBuilder.BuildCity(
                        building,
                        lot);
                AssertAuthoredPlayerHomeCity(
                    building,
                    lot,
                    foundationDepth,
                    registry);
            }
        }

        private static void AssertAuthoredPlayerHomeCity(
            Transform building,
            BuildingLot lot,
            float foundationDepth,
            PlayerHomeExteriorAssetRegistry registry)
        {
            Transform infrastructure = building.Find(
                CitySpecialBuildingWorldBuilder.ModelRootName);
            Assert.That(infrastructure, Is.Not.Null);
            Transform foundation = infrastructure.Find(
                CitySpecialBuildingWorldBuilder.FoundationObjectName);
            Assert.That(foundation, Is.Not.Null);
            Assert.That(foundation.GetComponent<Collider>(), Is.Null);
            Renderer foundationRenderer =
                foundation.GetComponent<Renderer>();
            Assert.That(foundationRenderer, Is.Not.Null);
            Assert.That(
                foundationRenderer.bounds.size.x,
                Is.EqualTo(
                        lot.Size.x -
                        CitySpecialBuildingWorldBuilder
                            .PlayerHomeFoundationInset * 2f)
                    .Within(PositionTolerance));
            Assert.That(
                foundationRenderer.bounds.size.z,
                Is.EqualTo(
                        lot.Size.y -
                        CitySpecialBuildingWorldBuilder
                            .PlayerHomeFoundationInset * 2f)
                    .Within(PositionTolerance));

            Assert.That(
                infrastructure.Find(
                    CitySpecialBuildingWorldBuilder.ShellObjectName),
                Is.Null);
            Assert.That(
                infrastructure.Find(
                    CitySpecialBuildingWorldBuilder.RoofObjectName),
                Is.Null);
            Assert.That(
                infrastructure.Find(
                    CitySpecialBuildingWorldBuilder.TrimObjectName),
                Is.Null);
            Assert.That(building.Find("Front Windows"), Is.Null);
            string[] obsoleteRuntimeParts =
            {
                "Home Roof Accent",
                "Home Chimney",
                "Home Balcony Slab",
                "Home Balcony Door",
                "Home Balcony Window",
                "Home Entrance Canopy"
            };
            for (int index = 0;
                 index < obsoleteRuntimeParts.Length;
                 index++)
            {
                Assert.That(
                    FindRenderer(building, obsoleteRuntimeParts[index]),
                    Is.Null,
                    $"Player home retained obsolete runtime geometry " +
                    $"'{obsoleteRuntimeParts[index]}'.");
            }

            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.transform.name,
                Is.EqualTo(
                    CityPlayerHomeExteriorWorldBuilder.CityObjectName));
            Assert.That(
                registry.DesignId,
                Is.EqualTo(
                    CityPlayerHomeExteriorWorldBuilder.DesignId));
            Assert.That(
                registry.Dimensions.Width,
                Is.EqualTo(13f).Within(0.0001f));
            Assert.That(
                registry.Dimensions.Depth,
                Is.EqualTo(12f).Within(0.0001f));
            Assert.That(
                registry.Dimensions.Height,
                Is.EqualTo(8.8f).Within(0.0001f));
            Assert.That(
                registry.TryGetAnchor(
                    CityPlayerHomeExteriorWorldBuilder.DoorAnchorRole,
                    out Transform doorAnchor),
                Is.True);
            AssertVectorNear(doorAnchor.position, lot.DoorPosition);

            PlayerHomeExteriorPartBinding litBinding = registry.Parts
                .Single(binding => binding.Emissive);
            Assert.That(
                litBinding.SourceName,
                Is.EqualTo("Front Lit Window Glass"));
            Assert.That(litBinding.Sheet, Is.EqualTo("WindowGlass"));
            Material homeLitMaterial =
                CityWindowAppearance.ResolveLitMaterial(
                    CityWindowFamily.Home);
            Assert.That(
                litBinding.Renderer.sharedMaterial,
                Is.SameAs(homeLitMaterial));
            Assert.That(
                registry.Parts.Count(binding =>
                    binding.Renderer != null &&
                    binding.Renderer.sharedMaterial == homeLitMaterial),
                Is.EqualTo(1),
                "Only the authored upper-left player-home pane may glow.");

            Transform collision = building.Find(
                CityBuildingPrototypeWorldBuilder
                    .LogicalCollisionObjectName);
            Assert.That(collision, Is.Not.Null);
            BoxCollider logical = collision.GetComponent<BoxCollider>();
            Assert.That(logical, Is.Not.Null);
            Assert.That(collision.GetComponent<Renderer>(), Is.Null);
            Assert.That(collision.GetComponent<MeshFilter>(), Is.Null);
            Assert.That(
                logical.size,
                Is.EqualTo(new Vector3(
                    lot.Size.x,
                    lot.Height + foundationDepth,
                    lot.Size.y)));
            Collider[] colliders =
                building.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders, Has.Length.EqualTo(1));
            Assert.That(colliders[0], Is.SameAs(logical));
            Assert.That(
                building.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                building.GetComponentsInChildren<Camera>(true),
                Is.Empty);
        }

        private static void AssertAuthoredBarCity(
            Transform building,
            BuildingLot lot,
            float foundationDepth)
        {
            Transform model = building.Find(
                CitySpecialBuildingWorldBuilder.ModelRootName);
            Assert.That(model, Is.Not.Null);
            Transform foundation = model.Find(
                CitySpecialBuildingWorldBuilder.FoundationObjectName);
            Assert.That(foundation, Is.Not.Null);
            Renderer foundationRenderer =
                foundation.GetComponent<Renderer>();
            Assert.That(foundationRenderer, Is.Not.Null);
            Assert.That(foundation.GetComponent<Collider>(), Is.Null);
            Assert.That(
                foundationRenderer.bounds.size.x,
                Is.LessThan(lot.Size.x - 0.07f));
            Assert.That(
                foundationRenderer.bounds.size.z,
                Is.LessThan(lot.Size.y - 0.07f));
            Assert.That(
                foundationRenderer.bounds.max.y,
                Is.EqualTo(lot.DoorPosition.y + 0.04f).Within(0.01f));
            var foundationProperties = new MaterialPropertyBlock();
            foundationRenderer.GetPropertyBlock(foundationProperties);
            Assert.That(
                foundationProperties.GetTexture(
                    Shader.PropertyToID("_BaseMap")),
                Is.EqualTo(
                    BarExteriorSurfaceAppearance.GetTexture(
                        BarExteriorSurfaceKind.Brick)));
            Assert.That(
                model.Find(
                    CitySpecialBuildingWorldBuilder.ShellObjectName),
                Is.Null,
                "The complete pub must not retain the old CityMisc shell.");
            Assert.That(
                model.Find(
                    CitySpecialBuildingWorldBuilder.RoofObjectName),
                Is.Null);
            Assert.That(
                model.Find(
                    CitySpecialBuildingWorldBuilder.TrimObjectName),
                Is.Null);
            Assert.That(building.Find("Front Windows"), Is.Null);

            string[] requiredParts =
            {
                "Pub Brick Shell",
                "Pub Rendered Upper Storey",
                "Pub Slate Roof",
                "Pub Ground Floor Glass",
                "Pub Upper Sash Frames",
                "Pub Upper Windows Warm",
                "Bar Door",
                "Bar Door Frame Left",
                "Bar Door Frame Right",
                "Bar Entrance Canopy",
                "Bar Landmark Marker"
            };
            for (int index = 0; index < requiredParts.Length; index++)
            {
                Assert.That(
                    building.Find(requiredParts[index]),
                    Is.Not.Null,
                    $"The authored pub is missing '{requiredParts[index]}'.");
            }

            GameObject prefab = BarModelResources.LoadFacadePrefab();
            Assert.That(prefab, Is.Not.Null);
            BarAssetRegistry registry =
                prefab.GetComponent<BarAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.DesignId, Is.EqualTo("bar_exterior_v2"));
            Assert.That(prefab.transform.localScale, Is.EqualTo(Vector3.one));
            Assert.That(registry.Dimensions.Width,
                Is.EqualTo(12.2645f).Within(0.0001f));
            Assert.That(registry.Dimensions.Depth,
                Is.EqualTo(13.5237f).Within(0.0001f));
            Assert.That(registry.Dimensions.Height,
                Is.EqualTo(9.3435f).Within(0.0001f));

            Vector3 frontage = new Vector3(
                lot.FrontageDirection.x,
                0f,
                lot.FrontageDirection.y);
            float lotFrontage = Mathf.Abs(frontage.x) > 0.5f
                ? lot.Size.y
                : lot.Size.x;
            float lotDepth = Mathf.Abs(frontage.x) > 0.5f
                ? lot.Size.x
                : lot.Size.y;
            Assert.That(registry.Dimensions.Width,
                Is.EqualTo(lotFrontage).Within(0.001f));
            Assert.That(registry.Dimensions.Depth,
                Is.EqualTo(lotDepth).Within(0.001f));
            Assert.That(registry.Dimensions.Height,
                Is.EqualTo(lot.Height).Within(0.001f));

            Renderer door = FindRenderer(building, "Bar Door");
            Assert.That(door, Is.Not.Null);
            Vector3 expectedDoorCentre = lot.DoorPosition -
                (frontage * 0.16f) +
                (Vector3.up * 1.17f);
            AssertVectorNear(door.bounds.center, expectedDoorCentre);

            Renderer canopy = FindRenderer(
                building,
                "Bar Entrance Canopy");
            Vector3 canopyOffset =
                canopy.bounds.center - lot.DoorPosition;
            canopyOffset.y = 0f;
            Assert.That(
                Vector3.Dot(canopyOffset.normalized, frontage),
                Is.GreaterThan(0.999f),
                "The complete pub frontage does not face its road.");

            Transform collision = building.Find(
                CityBuildingPrototypeWorldBuilder
                    .LogicalCollisionObjectName);
            Assert.That(collision, Is.Not.Null);
            BoxCollider logical = collision.GetComponent<BoxCollider>();
            Assert.That(logical, Is.Not.Null);
            Assert.That(
                logical.size,
                Is.EqualTo(new Vector3(
                    lot.Size.x,
                    lot.Height + foundationDepth,
                    lot.Size.y)));
            Collider[] colliders =
                building.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders, Has.Length.EqualTo(1));
            Assert.That(colliders[0], Is.SameAs(logical));
            Assert.That(
                building.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                building.GetComponentsInChildren<Camera>(true),
                Is.Empty);

            string[] authoredLitGlass =
            {
                "Pub Ground Floor Glass",
                "Pub Upper Windows Warm"
            };
            for (int index = 0; index < authoredLitGlass.Length; index++)
            {
                Renderer pane = FindRenderer(
                    building,
                    authoredLitGlass[index]);
                Assert.That(pane, Is.Not.Null);
                Assert.That(
                    pane.sharedMaterial,
                    Is.SameAs(
                        CityWindowAppearance.ResolveLitMaterial(
                            CityWindowFamily.Bar)));
                var properties = new MaterialPropertyBlock();
                pane.GetPropertyBlock(properties);
                Assert.That(
                    properties.GetTexture(
                        Shader.PropertyToID("_BaseMap")),
                    Is.SameAs(Texture2D.whiteTexture));
                Assert.That(
                    properties.GetTexture(
                        Shader.PropertyToID("_EmissionMap")),
                    Is.SameAs(Texture2D.whiteTexture));
            }
        }

        private static void AssertSpecialHomeExterior(
            Transform buildings,
            HomeExteriorContextPlan context)
        {
            int observed = 0;
            int authoredSupermarkets = 0;
            for (int index = 0;
                 index < context.NearbyLots.Count;
                 index++)
            {
                BuildingLot lot = context.NearbyLots[index];
                if ((!lot.IsBar && !lot.IsSupermarket) ||
                    lot.IsPlayerHome)
                {
                    continue;
                }

                observed++;
                float foundationDepth = CityWorldBuilder
                    .ResolveBuildingFoundationDepth(
                        context.Layout,
                        lot);
                CityBuildingExteriorFit fit =
                    CitySpecialBuildingWorldBuilder
                        .ClassifyHomeExterior(
                            context,
                            lot,
                            foundationDepth);
                Transform building = buildings.Find(
                    lot.IsBar
                        ? $"Exterior Bar {lot.BarId}"
                        : "Exterior Supermarket");
                if (fit == CityBuildingExteriorFit.Hidden)
                {
                    Assert.That(building, Is.Null);
                    continue;
                }

                Assert.That(building, Is.Not.Null, lot.Cell.ToString());
                if (fit == CityBuildingExteriorFit.Full)
                {
                    if (lot.IsBar)
                    {
                        AssertAuthoredBarHomeExterior(
                            building,
                            context,
                            lot);
                    }
                    else
                    {
                        AssertAuthoredSupermarketHomeExterior(
                            building,
                            context,
                            lot);
                        authoredSupermarkets++;
                    }

                    continue;
                }

                Transform model = building.Find(
                    CitySpecialBuildingWorldBuilder.ModelRootName);
                Transform shell = fit == CityBuildingExteriorFit.Full
                    ? model?.Find("Exterior Building Mass")
                    : building.Find("Exterior Building Mass");
                Assert.That(shell, Is.Not.Null, lot.Cell.ToString());
                MeshFilter filter = shell.GetComponent<MeshFilter>();
                Assert.That(filter, Is.Not.Null);
                Assert.That(filter.sharedMesh, Is.Not.Null);
                if (fit == CityBuildingExteriorFit.Full)
                {
                    Assert.That(
                        filter.sharedMesh.name,
                        Is.EqualTo(
                            CityMiscAssetProvider.GetExpectedMeshName(
                                SpecialKind(lot),
                                0,
                                "Shell_Masonry")));
                    Assert.That(
                        model,
                        Is.Not.Null);
                }
                else
                {
                    Assert.That(
                        model,
                        Is.Null);
                }
            }

            Assert.That(
                observed,
                Is.GreaterThan(0),
                "The canonical Home view must include a special neighbor.");
            Assert.That(
                authoredSupermarkets,
                Is.EqualTo(1),
                "Home must reuse the complete authored supermarket model.");
        }

        private static void AssertAuthoredSupermarketCity(
            Transform building,
            BuildingLot lot,
            float foundationDepth)
        {
            AssertAuthoredSupermarket(
                building,
                "Supermarket Exterior",
                lot.DoorPosition);

            Transform collision = building.Find(
                CityBuildingPrototypeWorldBuilder
                    .LogicalCollisionObjectName);
            Assert.That(collision, Is.Not.Null);
            BoxCollider logical = collision.GetComponent<BoxCollider>();
            Assert.That(logical, Is.Not.Null);
            Assert.That(
                logical.size,
                Is.EqualTo(new Vector3(
                    lot.Size.x,
                    lot.Height + foundationDepth,
                    lot.Size.y)));
            Collider[] colliders =
                building.GetComponentsInChildren<Collider>(true);
            Assert.That(colliders, Has.Length.EqualTo(1));
            Assert.That(colliders[0], Is.SameAs(logical));
        }

        private static void AssertAuthoredSupermarketHomeExterior(
            Transform building,
            HomeExteriorContextPlan context,
            BuildingLot lot)
        {
            Vector3 doorPosition =
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    context.PlayerHome,
                    lot.DoorPosition);
            AssertAuthoredSupermarket(
                building,
                "Exterior Supermarket Model",
                building.TransformPoint(doorPosition));
            Assert.That(
                building.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The bounded Home supermarket must remain presentation-only.");
        }

        private static void AssertAuthoredSupermarket(
            Transform building,
            string authoredRootName,
            Vector3 expectedDoorPosition)
        {
            Transform infrastructure = building.Find(
                CitySpecialBuildingWorldBuilder.ModelRootName);
            Assert.That(infrastructure, Is.Not.Null);
            Assert.That(
                infrastructure.Find(
                    CitySpecialBuildingWorldBuilder.ShellObjectName),
                Is.Null,
                "The authored supermarket must not retain its CityMisc shell.");
            Assert.That(
                infrastructure.Find(
                    CitySpecialBuildingWorldBuilder.RoofObjectName),
                Is.Null);
            Assert.That(
                infrastructure.Find(
                    CitySpecialBuildingWorldBuilder.TrimObjectName),
                Is.Null);
            Assert.That(building.Find("Front Windows"), Is.Null);

            Transform authored = building.Find(authoredRootName);
            Assert.That(authored, Is.Not.Null);
            SupermarketExteriorAssetRegistry registry =
                authored.GetComponent<SupermarketExteriorAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.DesignId,
                Is.EqualTo("supermarket_exterior_v1"));
            Assert.That(registry.Dimensions.Width,
                Is.EqualTo(15.5f).Within(0.0001f));
            Assert.That(registry.Dimensions.Depth,
                Is.EqualTo(15.5f).Within(0.0001f));
            Assert.That(registry.Dimensions.Height,
                Is.EqualTo(6.4f).Within(0.0001f));
            Assert.That(
                registry.TryGetAnchor(
                    "exterior_door",
                    out Transform doorAnchor),
                Is.True);
            AssertVectorNear(doorAnchor.position, expectedDoorPosition);
            Assert.That(
                building.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                building.GetComponentsInChildren<Camera>(true),
                Is.Empty);
        }

        private static void AssertAuthoredBarHomeExterior(
            Transform building,
            HomeExteriorContextPlan context,
            BuildingLot lot)
        {
            Transform model = building.Find(
                CitySpecialBuildingWorldBuilder.ModelRootName);
            Assert.That(model, Is.Not.Null);
            Transform foundation = model.Find(
                "Exterior Special Building Foundation");
            Assert.That(foundation, Is.Not.Null);
            Assert.That(foundation.GetComponent<Collider>(), Is.Null);
            Assert.That(
                model.Find("Exterior Building Mass"),
                Is.Null,
                "Home must reconstruct the complete pub, not its old shell.");
            Assert.That(model.Find("Exterior Roof"), Is.Null);
            Assert.That(building.Find("Front Windows"), Is.Null);

            string[] requiredParts =
            {
                "Pub Brick Shell",
                "Pub Rendered Upper Storey",
                "Pub Slate Roof",
                "Pub Ground Floor Glass",
                "Pub Upper Sash Frames",
                "Bar Door",
                "Bar Door Frame Left",
                "Bar Door Frame Right",
                "Bar Entrance Canopy",
                "Bar Landmark Marker"
            };
            for (int index = 0; index < requiredParts.Length; index++)
            {
                Assert.That(
                    building.Find(requiredParts[index]),
                    Is.Not.Null,
                    $"The Home pub is missing '{requiredParts[index]}'.");
            }

            GameObject prefab = BarModelResources.LoadFacadePrefab();
            Assert.That(prefab, Is.Not.Null);
            BarAssetRegistry registry =
                prefab.GetComponent<BarAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.DesignId, Is.EqualTo("bar_exterior_v2"));
            Assert.That(registry.Dimensions.Width,
                Is.EqualTo(12.2645f).Within(0.0001f));
            Assert.That(registry.Dimensions.Depth,
                Is.EqualTo(13.5237f).Within(0.0001f));
            Assert.That(registry.Dimensions.Height,
                Is.EqualTo(9.3435f).Within(0.0001f));

            Vector3 doorPosition =
                PlayerHomeBalconyGeometry.ToHomeLocal(
                    context.PlayerHome,
                    lot.DoorPosition);
            Vector3 frontage =
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    context.PlayerHome,
                    new Vector3(
                        lot.FrontageDirection.x,
                        0f,
                        lot.FrontageDirection.y));
            Renderer door = FindRenderer(building, "Bar Door");
            Assert.That(door, Is.Not.Null);
            Vector3 expectedDoorCentre = building.TransformPoint(
                doorPosition -
                (frontage * 0.16f) +
                (Vector3.up * 1.17f));
            AssertVectorNear(door.bounds.center, expectedDoorCentre);

            Renderer canopy = FindRenderer(
                building,
                "Bar Entrance Canopy");
            Vector3 canopyOffset = canopy.bounds.center -
                building.TransformPoint(doorPosition);
            canopyOffset.y = 0f;
            Vector3 worldFrontage =
                building.TransformDirection(frontage);
            worldFrontage.y = 0f;
            worldFrontage.Normalize();
            Assert.That(
                Vector3.Dot(canopyOffset.normalized, worldFrontage),
                Is.GreaterThan(0.999f));

            Assert.That(
                building.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The bounded Home pub must remain presentation-only.");
            Assert.That(
                building.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                building.GetComponentsInChildren<Camera>(true),
                Is.Empty);
        }

        private static CityMiscKind SpecialKind(BuildingLot lot)
        {
            if (lot.IsPlayerHome)
            {
                return CityMiscKind.PlayerHomeBuildingShell;
            }

            return lot.IsSupermarket
                ? CityMiscKind.SupermarketBuildingShell
                : CityMiscKind.BarBuildingShell;
        }

        private static void AssertHomePrototypePose(
            HomeExteriorContextPlan context,
            BuildingLot lot,
            CityBuildingAssetProvider provider,
            CityBuildingAssetRegistry registry)
        {
            CityBuildingAssetRegistry sourceRegistry = provider
                .GetPrefabOrThrow(lot.District)
                .GetComponent<CityBuildingAssetRegistry>();
            CityBuildingPrototypePose cityPose =
                CityBuildingPrototypePlacement.ResolveCityPose(
                    lot,
                    sourceRegistry);
            CityBuildingPrototypePose homePose =
                CityBuildingPrototypePlacement.ResolveHomePose(
                    context.PlayerHome,
                    cityPose);
            AssertVectorNear(
                registry.transform.localPosition,
                homePose.Position);
            Assert.That(
                Quaternion.Angle(
                    registry.transform.localRotation,
                    homePose.Rotation),
                Is.LessThan(AngleTolerance));
            AssertVectorNear(registry.transform.localScale, Vector3.one);
        }

        private static BuildingLot FindFrontagedOrdinaryLot(
            CityLayout layout,
            CityDistrictKind district)
        {
            BuildingLot lot = layout.BuildingLots.FirstOrDefault(
                candidate =>
                    candidate.IsOrdinaryBuilding &&
                    candidate.HasRoadFrontage &&
                    candidate.District == district);
            Assert.That(
                lot,
                Is.Not.Null,
                $"The default layout needs a frontaged {district} lot.");
            return lot;
        }

        private static Renderer FindRenderer(
            Transform root,
            string name)
        {
            Renderer[] renderers =
                root.GetComponentsInChildren<Renderer>(true);
            for (int index = 0; index < renderers.Length; index++)
            {
                if (renderers[index].name == name)
                {
                    return renderers[index];
                }
            }

            return null;
        }

        private static void AssertVectorNear(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(
                PositionTolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(
                PositionTolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(
                PositionTolerance));
        }

        private static void AssertBoundsNear(
            Bounds actual,
            Bounds expected)
        {
            AssertVectorNear(actual.center, expected.center);
            AssertVectorNear(actual.size, expected.size);
        }
    }
}
