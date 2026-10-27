using System;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEditor;

namespace BarPromenade.Tests.EditMode
{
    public sealed class ChurchInteriorLayoutTests
    {
        [Test]
        public void Generate_CreatesReachableNaveAndSealedSanctuary()
        {
            ChurchInteriorLayoutPlan plan =
                ChurchInteriorLayoutPlanner.Generate(20260826);

            Assert.That(
                plan.RoomSize,
                Is.EqualTo(new Vector2(12.1f, 24.2f)));
            Assert.That(plan.RoomHeight, Is.EqualTo(7.6f));
            AssertBoundsNear(plan.ModelLocalBounds,
                ChurchInteriorLayoutPlanner.ModelLocalBounds.min,
                ChurchInteriorLayoutPlanner.ModelLocalBounds.max);
            Assert.That(
                plan.ModelResourcePath,
                Is.EqualTo("Church/ChurchInterior3D"));
            Assert.That(
                plan.Zones,
                Has.Count.EqualTo(
                    ChurchInteriorLayoutValidator.RequiredZoneCount));
            Assert.That(
                plan.Paths,
                Has.Count.EqualTo(
                    ChurchInteriorLayoutValidator.RequiredPathCount));
            Assert.That(
                plan.Fixtures,
                Has.Count.EqualTo(
                    ChurchInteriorLayoutValidator.RequiredFixtureCount));
            Assert.That(plan.PlayerSpawn.z, Is.EqualTo(-10.1f));
            Assert.That(plan.ExitPosition.z, Is.EqualTo(-11.2f));
            Assert.That(plan.ExitTriggerSize.x, Is.EqualTo(1.8f));

            foreach (ChurchInteriorZoneKind kind in
                     Enum.GetValues(typeof(ChurchInteriorZoneKind)))
            {
                Assert.That(
                    plan.TryGetZone(kind, out _),
                    Is.True,
                    $"Missing zone {kind}.");
            }

            Assert.That(
                plan.TryGetZone(
                    ChurchInteriorZoneKind.Sanctuary,
                    out ChurchInteriorZonePlan sanctuary),
                Is.True);
            Assert.That(sanctuary.IsAccessible, Is.False);
            Assert.That(
                plan.WalkableBounds.yMax,
                Is.EqualTo(
                    ChurchInteriorLayoutPlanner
                        .SanctuaryBoundaryZ)
                    .Within(0.001f));

            ChurchInteriorFixturePlan rail = SingleFixture(
                plan,
                ChurchInteriorFixtureKind.AltarRail);
            Assert.That(
                rail.Bounds,
                Is.EqualTo(new Rect(-5.75f, 7.86f, 11.5f, .28f)));
            Assert.That(rail.BlocksMovement, Is.True);
            Assert.DoesNotThrow(
                () => ChurchInteriorLayoutValidator
                    .ValidateOrThrow(plan));
        }

        [Test]
        public void Generate_PinsCatholicFurnitureAndClearRoutes()
        {
            ChurchInteriorLayoutPlan plan = ChurchInteriorLayoutPlanner.Generate(17);
            TextAsset source = AssetDatabase.LoadAssetAtPath<TextAsset>("Assets/Church/Models/Church3D.json");
            Assert.That(source, Is.Not.Null);
            LayoutManifest manifest = JsonUtility.FromJson<LayoutManifest>(source.text);
            ShellContract shell = manifest.shell_contract;
            Assert.That(shell, Is.Not.Null);
            Assert.That(shell.room_size_xz_m, Is.EqualTo(new[] { plan.RoomSize.x, plan.RoomSize.y }));
            Assert.That(shell.narthex_clear_bounds_xz_m,
                Is.EqualTo(new[] { -ChurchInteriorLayoutPlanner.NarthexHalfWidth, plan.WalkableBounds.yMin,
                    ChurchInteriorLayoutPlanner.NarthexHalfWidth, ChurchInteriorLayoutPlanner.NarthexEndZ }));
            Assert.That(shell.aisle_clear_bounds_xz_m[0], Is.EqualTo(-ChurchInteriorLayoutPlanner.AisleHalfWidth));
            Assert.That(shell.aisle_clear_bounds_xz_m[1], Is.EqualTo(ChurchInteriorLayoutPlanner.NarthexEndZ));
            Assert.That(shell.aisle_clear_bounds_xz_m[2], Is.EqualTo(ChurchInteriorLayoutPlanner.AisleHalfWidth));
            Assert.That(shell.window_depths_m, Is.EqualTo(ChurchInteriorLayoutPlanner.WindowDepths));
            Assert.That(shell.window_plane_x_m, Is.EqualTo(ChurchInteriorLayoutPlanner.WindowApertureX));
            Assert.That(shell.window_center_height_m, Is.EqualTo(ChurchInteriorLayoutPlanner.WindowCenterY));
            Assert.That(shell.window_clear_size_m, Is.EqualTo(new[] {
                ChurchInteriorLayoutPlanner.WindowOpeningWidth, ChurchInteriorLayoutPlanner.WindowOpeningHeight }));
            Assert.That(shell.door_threshold_xz_m, Is.EqualTo(new[] { 0f, ChurchInteriorLayoutPlanner.DoorPlaneZ }));
            Assert.That(shell.rose_center_xyz_m, Is.EqualTo(new[] { ChurchInteriorLayoutPlanner.RoseCenter.x,
                ChurchInteriorLayoutPlanner.RoseCenter.z, ChurchInteriorLayoutPlanner.RoseCenter.y }),
                "The measured manifest stores the rose centre in Blender XYZ.");
            Assert.That(shell.rose_clear_diameter_m, Is.EqualTo(ChurchInteriorLayoutPlanner.RoseOpeningDiameter));
            LayoutAsset interior = manifest.assets.Single(asset => asset.kind == "Interior");
            Assert.That(interior.layout_contract, Has.Length.EqualTo(12));
            foreach (LayoutContract contract in interior.layout_contract)
            {
                ChurchInteriorFixtureKind kind = FixtureKind(contract.name);
                ChurchInteriorFixturePlan[] fixtures = Fixtures(plan, kind);
                Assert.That(fixtures, Has.Length.EqualTo(contract.count), contract.name);
                Assert.That(contract.centers_xz_flat, Has.Length.EqualTo(fixtures.Length * 2));
                for (int index = 0; index < fixtures.Length; index++)
                {
                    ChurchInteriorFixturePlan fixture = fixtures[index];
                    Assert.That(fixture.Bounds.center.x, Is.EqualTo(contract.centers_xz_flat[index * 2]).Within(.001f), fixture.Id);
                    Assert.That(fixture.Bounds.center.y, Is.EqualTo(contract.centers_xz_flat[index * 2 + 1]).Within(.001f), fixture.Id);
                    Assert.That(fixture.Bounds.width, Is.EqualTo(contract.footprint_xz_m[0]).Within(.001f), fixture.Id);
                    Assert.That(fixture.Bounds.height, Is.EqualTo(contract.footprint_xz_m[1]).Within(.001f), fixture.Id);
                    Assert.That(fixture.BaseHeight, Is.EqualTo(contract.vertical_envelope_m[0]).Within(.001f), fixture.Id);
                    Assert.That(fixture.BaseHeight + fixture.Height, Is.EqualTo(contract.vertical_envelope_m[1]).Within(.001f), fixture.Id);
                }
            }
            foreach (ChurchInteriorPathPlan path in plan.Paths)
            {
                Assert.That(plan.ContainsWalkable(path.Bounds), Is.True, path.Id);
                Assert.That(Mathf.Min(path.Bounds.width, path.Bounds.height),
                    Is.GreaterThanOrEqualTo(path.MinimumClearance - .001f), path.Id);
                AssertGroundRouteClear(plan, path);
            }
            ChurchInteriorFixturePlan pew = Fixtures(plan, ChurchInteriorFixtureKind.Pew)[0];
            Assert.That(pew.Bounds.width, Is.GreaterThan(2f), "Furniture retains human metres.");
            Assert.That(pew.Height, Is.InRange(.9f, 1.2f));
            Assert.That(plan.Contains(new Vector3(5f, 0f, -10f)), Is.False,
                "The smaller narthex cannot inherit the wider nave's rectangular bounds.");
            Assert.That(plan.Contains(new Vector3(5f, 0f, 0f)), Is.True);
            Vector3 constrained = plan.Constrain(Vector3.zero, new Vector3(5f, 0f, -10f), .25f);
            Assert.That(plan.Contains(constrained, .25f), Is.True);
            Assert.That(constrained.x, Is.EqualTo(4.35f).Within(.001f));
        }

        private static ChurchInteriorFixtureKind FixtureKind(string name)
        {
            switch (name)
            {
                case "nave_piers": return ChurchInteriorFixtureKind.Pier;
                case "pew_halves": return ChurchInteriorFixtureKind.Pew;
                case "communion_rail": return ChurchInteriorFixtureKind.AltarRail;
                case "altar_table": return ChurchInteriorFixtureKind.AltarTable;
                case "high_altar": return ChurchInteriorFixtureKind.HighAltar;
                case "crucifix": return ChurchInteriorFixtureKind.Crucifix;
                case "confessionals": return ChurchInteriorFixtureKind.Confessional;
                case "votive_stands": return ChurchInteriorFixtureKind.VotiveCandleStand;
                case "baptismal_font": return ChurchInteriorFixtureKind.BaptismalFont;
                case "choir_loft": return ChurchInteriorFixtureKind.ChoirLoft;
                case "choir_loft_supports": return ChurchInteriorFixtureKind.ChoirLoftSupport;
                case "pipe_organ": return ChurchInteriorFixtureKind.Organ;
                default: throw new InvalidOperationException("Unknown church contract: " + name);
            }
        }

        [Serializable] private sealed class LayoutManifest { public LayoutAsset[] assets; public ShellContract shell_contract; }
        [Serializable] private sealed class ShellContract
        {
            public float[] room_size_xz_m, narthex_clear_bounds_xz_m, aisle_clear_bounds_xz_m,
                window_depths_m, window_clear_size_m, door_threshold_xz_m, rose_center_xyz_m;
            public float window_plane_x_m, window_center_height_m, rose_clear_diameter_m;
        }
        [Serializable] private sealed class LayoutAsset { public string kind; public LayoutContract[] layout_contract; }
        [Serializable] private sealed class LayoutContract
        {
            public string name;
            public int count;
            public float[] centers_xz_flat;
            public float[] footprint_xz_m;
            public float[] vertical_envelope_m;
        }

        [Test]
        public void InteriorPrefab_MatchesTheDataFirstLayoutContract()
        {
            ChurchInteriorLayoutPlan plan =
                ChurchInteriorLayoutPlanner.Generate(20260826);
            GameObject prefab = ChurchResources.LoadInteriorPrefab();

            Assert.That(prefab, Is.Not.Null);
            ChurchAssetRegistry registry =
                prefab.GetComponent<ChurchAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(registry.Kind, Is.EqualTo(ChurchAssetKind.Interior));
            Assert.That(
                registry.DesignId,
                Is.EqualTo(
                    "provincial_catholic_gothic_basilica_v1"));
            Assert.That(registry.BuildSignature, Is.Not.Empty);
            Assert.That(
                registry.Dimensions.Width,
                Is.EqualTo(plan.RoomSize.x).Within(0.001f));
            Assert.That(
                registry.Dimensions.Length,
                Is.EqualTo(plan.RoomSize.y).Within(0.001f));
            AssertBoundsNear(
                registry.LocalBounds,
                plan.ModelLocalBounds.min,
                plan.ModelLocalBounds.max);
            Assert.That(
                registry.LocalBounds.max.y,
                Is.EqualTo(
                    ChurchInteriorLayoutPlanner.ModelMaximumHeight)
                    .Within(0.01f));
            AssertAnchorXZ(
                registry,
                registry.SpawnAnchor,
                new Vector2(plan.PlayerSpawn.x, plan.PlayerSpawn.z));
            AssertAnchorXZ(
                registry,
                registry.ExitAnchor,
                new Vector2(plan.ExitPosition.x, plan.ExitPosition.z));
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty,
                "The prefab must stay passive; the layout builds collision.");
            GameObject host = new GameObject("Church Metric Contract");
            try
            {
                ChurchInteriorWorldResult placed = ChurchInteriorWorldBuilder.Build(host.transform, plan);
                Assert.That(placed.Model.localScale, Is.EqualTo(Vector3.one));
                Assert.That(placed.GameplayColliders.Count,
                    Is.EqualTo(10 + plan.Fixtures.Count(fixture => fixture.BlocksMovement)));
            }
            finally { UnityEngine.Object.DestroyImmediate(host); }

        }

        [Test]
        public void Generate_KeepsSpawnToNaveRouteOpenThroughNarthex()
        {
            ChurchInteriorLayoutPlan plan =
                ChurchInteriorLayoutPlanner.Generate(20260826);
            Assert.That(
                plan.TryGetPath(
                    ChurchInteriorPathKind.MainNave,
                    out ChurchInteriorPathPlan mainNave),
                Is.True);
            Assert.That(
                plan.TryGetPath(
                    ChurchInteriorPathKind.NarthexCrossing,
                    out ChurchInteriorPathPlan narthexCrossing),
                Is.True);

            Assert.That(
                mainNave.Bounds.Contains(
                    new Vector2(
                        plan.PlayerSpawn.x,
                        plan.PlayerSpawn.z)),
                Is.True,
                "The spawn must stand inside the protected main nave.");
            Assert.That(
                mainNave.Bounds.Overlaps(narthexCrossing.Bounds),
                Is.True,
                "The main nave must remain open through the narthex.");
            Assert.That(
                mainNave.Bounds.width,
                Is.GreaterThanOrEqualTo(mainNave.MinimumClearance));
            Assert.That(
                narthexCrossing.Bounds.height,
                Is.GreaterThanOrEqualTo(
                    narthexCrossing.MinimumClearance));

            AssertGroundRouteClear(plan, mainNave);
            AssertGroundRouteClear(plan, narthexCrossing);
        }

        [Test]
        public void DoorDirections_AppendChurchWithoutRenumberingLegacy()
        {
            Assert.That((int)DoorTransitionDirection.EnterBar, Is.EqualTo(0));
            Assert.That((int)DoorTransitionDirection.ExitBar, Is.EqualTo(1));
            Assert.That(
                (int)DoorTransitionDirection.EnterBuilding,
                Is.EqualTo(2));
            Assert.That(
                (int)DoorTransitionDirection.ExitBuilding,
                Is.EqualTo(3));
            Assert.That(
                (int)DoorTransitionDirection.EnterApartment,
                Is.EqualTo(4));
            Assert.That(
                (int)DoorTransitionDirection.ExitApartment,
                Is.EqualTo(5));
            Assert.That(
                (int)DoorTransitionDirection.EnterChurch,
                Is.EqualTo(6));
            Assert.That(
                (int)DoorTransitionDirection.ExitChurch,
                Is.EqualTo(7));
        }

        private static ChurchInteriorFixturePlan SingleFixture(
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorFixtureKind kind)
        {
            return plan.Fixtures.Single(
                fixture => fixture.Kind == kind);
        }

        /// <summary>
        /// The quarter turn between the interior's own axes and the
        /// City's compass, derived rather than repeated.
        ///
        /// The interior is a scene of its own with the model at
        /// identity, so nothing in it knows which way north is. The
        /// old lighting simply used the world sun raw, which is how
        /// both aisles came to be lit equally at every hour. Getting
        /// this wrong is a quarter turn that looks like nothing in the
        /// source and puts the sun through the wrong wall, so it is
        /// pinned against the same Vector3.right that CityChurchPlan
        /// enforces as the altar direction.
        /// </summary>
        [Test]
        public void InteriorSunFrame_MatchesTheChurchsPlacementInTheCity()
        {
            Quaternion interiorToWorld = Quaternion.Inverse(
                ChurchInteriorSunRules.InteriorFromWorld);

            // The interior model's +Z is the altar, and the city puts
            // the altar along the access normal, due east.
            AssertDirection(
                interiorToWorld * Vector3.forward,
                Vector3.right,
                "the altar must face east");
            // Which leaves the +X aisle wall facing south - the wall
            // the sun spends the day on.
            AssertDirection(
                interiorToWorld * Vector3.right,
                Vector3.back,
                "the +X aisle wall must face south");
        }

        /// <summary>
        /// One wall is the sun wall and the other is not. A basilica
        /// standing east-west in the northern hemisphere never takes
        /// direct sun on its north aisle, and the reading of the whole
        /// room depends on that being true rather than assumed.
        /// </summary>
        [Test]
        public void LancetWalls_TakeTheSunOnTheSouthAisleOnly()
        {
            bool southWasEverLit = false;
            for (double minute = 0d; minute < 1440d; minute += 5d)
            {
                Vector3 travel =
                    ChurchInteriorSunRules.LocalTravelDirection(
                        GameTimeDayNightRules.SunRotationAt(minute));
                float north = ChurchInteriorSunRules.WallFacing(
                    ChurchInteriorSunRules.NorthWallSide,
                    travel);
                float south = ChurchInteriorSunRules.WallFacing(
                    ChurchInteriorSunRules.SouthWallSide,
                    travel);

                Assert.That(
                    north,
                    Is.LessThanOrEqualTo(0.0001f),
                    $"the north aisle took direct sun at minute {minute}");
                southWasEverLit |= south > 0.3f;
            }

            Assert.That(
                southWasEverLit,
                Is.True,
                "the south aisle never took the sun at all");
        }

        /// <summary>
        /// The church's light is BAKED at one pose and does not track
        /// the sun. What still has to be true of that pose is that it
        /// comes from the south, points down hard enough to reach the
        /// ground inside the building, and lands its pools on floor a
        /// person actually walks on rather than up the far wall.
        /// </summary>
        [Test]
        public void BakedSun_LandsItsPoolsOnTheSouthAisleFloor()
        {
            Vector3 travel = ChurchInteriorSunRules.BakedLocalTravel;

            Assert.That(
                ChurchInteriorSunRules.WallFacing(
                    ChurchInteriorSunRules.SouthWallSide,
                    travel),
                Is.GreaterThan(
                    ChurchInteriorSunRules.FacingFadeEnd),
                "the baked pose must light the south aisle fully");
            Assert.That(
                ChurchInteriorSunRules.WallFacing(
                    ChurchInteriorSunRules.NorthWallSide,
                    travel),
                Is.LessThanOrEqualTo(0f),
                "and must never light the north one");

            foreach (float depth in
                     ChurchInteriorAtmosphere.WindowDepths)
            {
                Vector3 pool = ChurchInteriorSunRules.FloorPool(
                    new Vector3(
                        ChurchInteriorAtmosphere.ShaftApertureX,
                        ChurchInteriorAtmosphere.WindowCenterY,
                        depth),
                    travel);
                Assert.That(
                    pool.x,
                    Is.InRange(.8f, ChurchInteriorLayoutPlanner.AisleHalfWidth),
                    $"the pool from the lancet at z={depth} misses the " +
                    "aisle floor");
                Assert.That(
                    pool.z,
                    Is.InRange(-11.9f, ChurchInteriorLayoutPlanner.SanctuaryBoundaryZ),
                    $"the pool from the lancet at z={depth} falls " +
                    "outside the building");
            }
        }

        private static void AssertDirection(
            Vector3 actual,
            Vector3 expected,
            string because)
        {
            Assert.That(
                Vector3.Angle(actual, expected),
                Is.LessThan(0.01f),
                $"{because}: got {actual}, expected {expected}");
        }

        private static void AssertAnchorXZ(
            ChurchAssetRegistry registry,
            Transform anchor,
            Vector2 expected)
        {
            Assert.That(anchor, Is.Not.Null);
            Vector3 local = registry.transform.InverseTransformPoint(
                anchor.position);
            Assert.That(
                local.x,
                Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(
                local.z,
                Is.EqualTo(expected.y).Within(0.01f));
        }

        private static void AssertBoundsNear(
            Bounds actual,
            Vector3 expectedMin,
            Vector3 expectedMax)
        {
            AssertVectorNear(actual.min, expectedMin);
            AssertVectorNear(actual.max, expectedMax);
        }

        private static void AssertVectorNear(
            Vector3 actual,
            Vector3 expected)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(0.01f));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(0.01f));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(0.01f));
        }

        private static void AssertGroundRouteClear(
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorPathPlan path)
        {
            for (int index = 0; index < plan.Fixtures.Count; index++)
            {
                ChurchInteriorFixturePlan fixture = plan.Fixtures[index];
                if (!fixture.BlocksMovement ||
                    !path.Bounds.Overlaps(fixture.Bounds))
                {
                    continue;
                }

                Assert.That(
                    fixture.BaseHeight,
                    Is.GreaterThanOrEqualTo(
                        ChurchInteriorLayoutValidator
                            .PlayerRouteClearHeight),
                    $"{fixture.Id} blocks {path.Id} below player height.");
            }
        }

        private static ChurchInteriorFixturePlan[] Fixtures(
            ChurchInteriorLayoutPlan plan,
            ChurchInteriorFixtureKind kind)
        {
            return plan.Fixtures
                .Where(fixture => fixture.Kind == kind)
                .ToArray();
        }

    }
}
