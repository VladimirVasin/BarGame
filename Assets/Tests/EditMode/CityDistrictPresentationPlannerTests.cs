using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityDistrictPresentationPlannerTests
    {
        [TestCase(
            CityDistrictKind.OldTown,
            CityDistrictMassFamily.FragmentedPerimeter,
            CityDistrictFrontageFamily.NarrowLayered)]
        [TestCase(
            CityDistrictKind.Residential,
            CityDistrictMassFamily.SetbackCourtyard,
            CityDistrictFrontageFamily.DomesticBalcony)]
        [TestCase(
            CityDistrictKind.Industrial,
            CityDistrictMassFamily.LowWideProcess,
            CityDistrictFrontageFamily.ProcessGate)]
        [TestCase(
            CityDistrictKind.Nightlife,
            CityDistrictMassFamily.TallDense,
            CityDistrictFrontageFamily.ActiveGroundFloor)]
        public void Catalog_DefinesCanonicalUrbanProfiles(
            CityDistrictKind district,
            CityDistrictMassFamily mass,
            CityDistrictFrontageFamily frontage)
        {
            CityDistrictArtProfile profile =
                CityDistrictPresentationPlanner.GetProfile(district);

            Assert.That(profile.District, Is.EqualTo(district));
            Assert.That(profile.Mass.Family, Is.EqualTo(mass));
            Assert.That(profile.Frontage.Family, Is.EqualTo(frontage));
            Assert.That(profile.StableId, Is.Not.Empty);
        }

        [Test]
        public void Catalog_PreservesMassingAndPresentationHierarchy()
        {
            CityDistrictArtProfile oldTown = Profile(
                CityDistrictKind.OldTown);
            CityDistrictArtProfile residential = Profile(
                CityDistrictKind.Residential);
            CityDistrictArtProfile industrial = Profile(
                CityDistrictKind.Industrial);
            CityDistrictArtProfile nightlife = Profile(
                CityDistrictKind.Nightlife);

            Assert.That(residential.Mass.FootprintMinimum, Is.EqualTo(0.76f));
            Assert.That(oldTown.Mass.FootprintMinimum, Is.EqualTo(0.92f));
            Assert.That(industrial.Mass.HeightMaximum, Is.EqualTo(0.32f));
            Assert.That(nightlife.Mass.HeightMinimum, Is.EqualTo(0.56f));
            Assert.That(
                residential.Window.LitWindowRatio,
                Is.GreaterThan(oldTown.Window.LitWindowRatio));
            Assert.That(
                industrial.Window.LitWindowRatio,
                Is.LessThan(oldTown.Window.LitWindowRatio));
            Assert.That(
                nightlife.Light.SignalShare,
                Is.GreaterThan(residential.Light.SignalShare));
        }

        [Test]
        public void BoundaryBlock_IsDeterministicAndKeepsDominantMass()
        {
            CityDistrictPresentationPlan first =
                CityDistrictPresentationPlanner.Create(
                    190734,
                    3,
                    7,
                    CityDistrictKind.OldTown,
                    CityDistrictKind.Residential,
                    0);
            CityDistrictPresentationPlan second =
                CityDistrictPresentationPlanner.Create(
                    190734,
                    3,
                    7,
                    CityDistrictKind.OldTown,
                    CityDistrictKind.Residential,
                    0);

            Assert.That(first.IsTransitionBlock, Is.True);
            Assert.That(
                first.Transition.SpanBlocks,
                Is.EqualTo(
                    CityDistrictPresentationPlanner.TransitionBlockSpan));
            Assert.That(first.Transition, Is.EqualTo(second.Transition));
            Assert.That(first.Frontage, Is.EqualTo(second.Frontage));
            Assert.That(first.Mass, Is.EqualTo(second.Mass));
            Assert.That(first.Window, Is.EqualTo(second.Window));
            Assert.That(first.Light, Is.EqualTo(second.Light));
            Assert.That(first.Wear, Is.EqualTo(second.Wear));
            Assert.That(first.Mass.NeighbourInfluence, Is.Zero);
            Assert.That(
                first.Light.NeighbourInfluence,
                Is.GreaterThan(first.Transition.MotifInfluence));
            Assert.That(CountSecondaryMotifs(first), Is.EqualTo(1));
        }

        [Test]
        public void InteriorBlock_HasNoNeighbourInfluence()
        {
            CityDistrictPresentationPlan plan =
                CityDistrictPresentationPlanner.Create(
                    190734,
                    3,
                    7,
                    CityDistrictKind.OldTown,
                    CityDistrictKind.Residential,
                    1);

            Assert.That(plan.IsTransitionBlock, Is.False);
            Assert.That(plan.Frontage.NeighbourInfluence, Is.Zero);
            Assert.That(plan.Mass.NeighbourInfluence, Is.Zero);
            Assert.That(plan.Window.NeighbourInfluence, Is.Zero);
            Assert.That(plan.Light.NeighbourInfluence, Is.Zero);
            Assert.That(plan.Wear.NeighbourInfluence, Is.Zero);
        }

        [Test]
        public void DirectTransitionPairs_AreSymmetricAndDiagonalsAreRejected()
        {
            Assert.That(
                CityDistrictPresentationPlanner.CanTransition(
                    CityDistrictKind.OldTown,
                    CityDistrictKind.Residential),
                Is.True);
            Assert.That(
                CityDistrictPresentationPlanner.CanTransition(
                    CityDistrictKind.Residential,
                    CityDistrictKind.OldTown),
                Is.True);
            Assert.That(
                CityDistrictPresentationPlanner.CanTransition(
                    CityDistrictKind.OldTown,
                    CityDistrictKind.Nightlife),
                Is.False);
            Assert.That(
                CityDistrictPresentationPlanner.CanTransition(
                    CityDistrictKind.Residential,
                    CityDistrictKind.Industrial),
                Is.False);

            Assert.Throws<ArgumentException>(() =>
                CityDistrictPresentationPlanner.Create(
                    1,
                    0,
                    0,
                    CityDistrictKind.OldTown,
                    CityDistrictKind.Nightlife,
                    0));
        }

        [Test]
        public void VariationKeys_ChangeByBlockWithoutChangingTheProfile()
        {
            CityDistrictPresentationPlan first =
                CityDistrictPresentationPlanner.Create(
                    7719,
                    2,
                    4,
                    CityDistrictKind.Industrial);
            CityDistrictPresentationPlan second =
                CityDistrictPresentationPlanner.Create(
                    7719,
                    3,
                    4,
                    CityDistrictKind.Industrial);

            Assert.That(
                first.DominantProfile,
                Is.SameAs(second.DominantProfile));
            Assert.That(
                first.Frontage.VariationKey,
                Is.Not.EqualTo(second.Frontage.VariationKey));
            Assert.That(
                first.Wear.VariationKey,
                Is.Not.EqualTo(second.Wear.VariationKey));
            Assert.That(
                CityDistrictPresentationPlanner.ResolveWindowVariationKey(
                    7719,
                    2,
                    4,
                    CityDistrictKind.Industrial),
                Is.EqualTo(first.Window.VariationKey));
            Assert.That(
                first.Window.SelectVariant(4),
                Is.InRange(0, 3));
        }

        [Test]
        public void WindowResolver_UsesDistrictSchedulesAndActiveNightlifeBase()
        {
            float oldTown = SampleLitRatio(CityDistrictKind.OldTown);
            float residential = SampleLitRatio(
                CityDistrictKind.Residential);
            float industrial = SampleLitRatio(
                CityDistrictKind.Industrial);
            float nightlife = SampleLitRatio(CityDistrictKind.Nightlife);
            float nightlifeBase = SampleLitRatio(
                CityDistrictKind.Nightlife,
                0,
                1,
                0,
                1);
            float nightlifeRearBase = SampleLitRatio(
                CityDistrictKind.Nightlife,
                0,
                1,
                1,
                2);
            float nightlifeUpper = SampleLitRatio(
                CityDistrictKind.Nightlife,
                1,
                4);

            Assert.That(residential, Is.GreaterThan(oldTown));
            Assert.That(oldTown, Is.GreaterThan(industrial));
            Assert.That(nightlife, Is.GreaterThan(industrial));
            Assert.That(nightlifeBase, Is.GreaterThan(0.45f));
            Assert.That(nightlifeRearBase, Is.LessThan(0.22f));
            Assert.That(nightlifeUpper, Is.LessThan(0.24f));
            Assert.That(
                nightlifeBase,
                Is.GreaterThan(nightlifeRearBase * 2.5f));
        }

        [TestCase(CityDistrictKind.OldTown)]
        [TestCase(CityDistrictKind.Residential)]
        [TestCase(CityDistrictKind.Industrial)]
        [TestCase(CityDistrictKind.Nightlife)]
        public void WindowResolver_FollowsDistrictTemperatureShare(
            CityDistrictKind district)
        {
            float actualWarmShare = SampleWarmShare(district);
            float authoredWarmShare = Profile(district).Window.WarmShare;

            Assert.That(
                actualWarmShare,
                Is.EqualTo(authoredWarmShare).Within(0.09f));
        }

        private static CityDistrictArtProfile Profile(
            CityDistrictKind district)
        {
            return CityDistrictPresentationPlanner.GetProfile(district);
        }

        private static float SampleLitRatio(
            CityDistrictKind district,
            int firstFloor = 0,
            int exclusiveLastFloor = 4,
            int firstSide = 0,
            int exclusiveLastSide = 2)
        {
            int lit = 0;
            int total = 0;
            for (int block = 0; block < 64; block++)
            {
                BuildingLot lot = CreateOrdinaryLot(
                    district,
                    new Vector2Int((block % 8) - 4, (block / 8) - 4));
                for (int floor = firstFloor;
                     floor < exclusiveLastFloor;
                     floor++)
                {
                    for (int pane = 0; pane < 8; pane++)
                    {
                        for (int side = firstSide;
                             side < exclusiveLastSide;
                             side++)
                        {
                            CityWindowFamily family =
                                CityExteriorAppearance.ResolveWindowFamily(
                                    lot,
                                    3000,
                                    floor,
                                    pane,
                                    side,
                                    out _);
                            if (family != CityWindowFamily.Off)
                            {
                                lit++;
                            }

                            total++;
                        }
                    }
                }
            }

            return lit / (float)total;
        }

        private static float SampleWarmShare(CityDistrictKind district)
        {
            int warm = 0;
            int lit = 0;
            for (int block = 0; block < 64; block++)
            {
                BuildingLot lot = CreateOrdinaryLot(
                    district,
                    new Vector2Int((block % 8) - 4, (block / 8) - 4));
                for (int floor = 0; floor < 4; floor++)
                {
                    for (int pane = 0; pane < 8; pane++)
                    {
                        for (int side = 0; side < 2; side++)
                        {
                            CityWindowFamily family =
                                CityExteriorAppearance.ResolveWindowFamily(
                                    lot,
                                    3000,
                                    floor,
                                    pane,
                                    side,
                                    out _);
                            if (family == CityWindowFamily.Off)
                            {
                                continue;
                            }

                            lit++;
                            if (family == CityWindowFamily.Warm)
                            {
                                warm++;
                            }
                        }
                    }
                }
            }

            Assert.That(lit, Is.GreaterThan(100));
            return warm / (float)lit;
        }

        private static BuildingLot CreateOrdinaryLot(
            CityDistrictKind district,
            Vector2Int cell)
        {
            return new BuildingLot(
                cell,
                Vector3.zero,
                new Vector2(14f, 14f),
                42f,
                Color.gray,
                district.ToString(),
                district,
                CityLandUseKind.Building,
                false,
                false,
                false,
                string.Empty,
                BarActivityKind.None,
                Vector2Int.right,
                Vector3.zero,
                Vector3.zero,
                Vector3.zero);
        }

        private static int CountSecondaryMotifs(
            CityDistrictPresentationPlan plan)
        {
            int count = 0;
            if (plan.Frontage.UsesNeighbour)
            {
                count++;
            }

            if (plan.Window.UsesNeighbour)
            {
                count++;
            }

            if (plan.Wear.UsesNeighbour)
            {
                count++;
            }

            return count;
        }
    }
}
