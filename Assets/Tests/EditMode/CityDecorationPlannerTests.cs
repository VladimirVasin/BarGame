using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityDecorationPlannerTests
    {
        [Test]
        public void SameSeed_CreatesIdenticalOrderedPlan()
        {
            CityDecorationPlan first = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out _,
                out _,
                out _);
            CityDecorationPlan second = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out _,
                out _,
                out _);

            CollectionAssert.AreEqual(
                first.Descriptors,
                second.Descriptors);
            Assert.That(first.Count, Is.LessThanOrEqualTo(
                CityDecorationPlan.MaximumDescriptorCount));
            Assert.That(first.Count, Is.GreaterThan(120));
        }

        [Test]
        public void DifferentSeed_ChangesOptionalVisualChoices()
        {
            CityDecorationPlan first = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out _,
                out _,
                out _);
            CityDecorationPlan second = CreatePlan(
                GameSessionState.DefaultCitySeed + 1,
                out _,
                out _,
                out _);

            Assert.That(
                HaveSameVisualSignatures(
                    first.Descriptors,
                    second.Descriptors),
                Is.False);
        }

        [Test]
        public void DefaultCity_CoversEveryOrdinaryLotAndRequiredLandmarks()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout,
                out _,
                out _);

            var coreCounts = new Dictionary<Vector2Int, int>();
            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.AnchorKind !=
                        CityDecorationAnchorKind.BuildingRoof &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.BuildingFacade)
                {
                    continue;
                }

                coreCounts[descriptor.LotCell] =
                    coreCounts.TryGetValue(
                        descriptor.LotCell,
                        out int count)
                        ? count + 1
                        : 1;
            }

            int ordinaryLotCount = 0;
            for (int index = 0;
                 index < layout.BuildingLots.Count;
                 index++)
            {
                BuildingLot lot = layout.BuildingLots[index];
                if (!lot.HasBuilding || lot.IsBar || lot.IsPlayerHome)
                {
                    continue;
                }

                ordinaryLotCount++;
                Assert.That(
                    coreCounts.TryGetValue(lot.Cell, out int count)
                        ? count
                        : 0,
                    Is.EqualTo(1),
                    lot.Cell.ToString());
            }

            Assert.That(coreCounts, Has.Count.EqualTo(ordinaryLotCount));
            Assert.That(
                plan.GetCount(CityDecorationAnchorKind.UrbanLandmark),
                Is.EqualTo(4));
            Assert.That(
                plan.GetCount(CityDecorationAnchorKind.ParkLandmark),
                Is.EqualTo(2));
            Assert.That(
                plan.GetCount(CityDecorationKind.ParkFountainAndStatue),
                Is.EqualTo(1));
            Assert.That(
                plan.GetCount(CityDecorationKind.ParkBandstand),
                Is.EqualTo(1));
            foreach (CityDecorationKind kind in
                     Enum.GetValues(typeof(CityDecorationKind)))
            {
                Assert.That(
                    plan.GetCount(kind),
                    Is.GreaterThan(0),
                    $"The default city is missing '{kind}'.");
            }
        }

        [Test]
        public void GroundDetails_RespectEntrancesAndNightFixtures()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out _,
                out RoadFencePlan fence,
                out CityNightFixturePlan night);

            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                if (descriptor.AnchorKind !=
                        CityDecorationAnchorKind.BuildingFrontage &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.Roadside &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.ParkFeature &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.ParkLandmark)
                {
                    continue;
                }

                float radius =
                    CityDecorationValidator.ResolveProtectionRadius(
                        descriptor.Kind);
                Assert.That(
                    CityDecorationValidator.IsProtectedGroundAnchor(
                        descriptor.Position,
                        radius,
                        fence,
                        night),
                    Is.False,
                    descriptor.StableId);
            }
        }

        [Test]
        public void PlansAcrossSeeds_ExerciseCompleteRecipeCatalog()
        {
            var kinds = new HashSet<CityDecorationKind>();
            for (int seedOffset = 0; seedOffset < 8; seedOffset++)
            {
                CityDecorationPlan plan = CreatePlan(
                    GameSessionState.DefaultCitySeed + seedOffset,
                    out _,
                    out _,
                    out _);
                for (int index = 0;
                     index < plan.Descriptors.Count;
                     index++)
                {
                    kinds.Add(plan.Descriptors[index].Kind);
                }
            }

            Array expected = Enum.GetValues(typeof(CityDecorationKind));
            Assert.That(kinds.Count, Is.EqualTo(expected.Length));
            foreach (CityDecorationKind kind in expected)
            {
                Assert.That(kinds, Does.Contain(kind), kind.ToString());
            }
        }

        [Test]
        public void DescriptorData_IsStableFiniteAndNonInteractive()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout,
                out RoadFencePlan fence,
                out CityNightFixturePlan night);

            string previousId = null;
            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                Assert.That(descriptor.StableId, Is.Not.Empty);
                if (previousId != null)
                {
                    Assert.That(
                        string.CompareOrdinal(
                            previousId,
                            descriptor.StableId),
                        Is.LessThan(0));
                }

                previousId = descriptor.StableId;
                Assert.That(IsFinite(descriptor.Position), Is.True);
                Assert.That(IsFinite(descriptor.Forward), Is.True);
                Assert.That(
                    descriptor.CollisionTier,
                    Is.EqualTo(CityDecorationCollisionTier.None));
                Assert.That(descriptor.Forward.y, Is.EqualTo(0f));
                Assert.That(
                    descriptor.Forward.sqrMagnitude,
                    Is.EqualTo(1f).Within(0.002f));
                if (descriptor.HasLotAnchor &&
                    descriptor.AnchorKind !=
                        CityDecorationAnchorKind.UrbanLandmark)
                {
                    Assert.That(
                        descriptor.TryResolveLot(layout, out BuildingLot lot),
                        Is.True);
                    Assert.That(lot.IsBar || lot.IsPlayerHome, Is.False);
                }
            }

            Assert.DoesNotThrow(() =>
                plan.ValidateOrThrow(layout, fence, night));
        }

        private static CityDecorationPlan CreatePlan(
            int seed,
            out CityLayout layout,
            out RoadFencePlan fence,
            out CityNightFixturePlan night)
        {
            layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                seed);
            fence = RoadFencePlanner.CreatePlan(layout);
            night = CityNightFixturePlanner.CreatePlan(layout);
            return CityDecorationPlanner.CreatePlan(
                layout,
                fence,
                night);
        }

        private static bool HaveSameVisualSignatures(
            IReadOnlyList<CityDecorationDescriptor> first,
            IReadOnlyList<CityDecorationDescriptor> second)
        {
            if (first.Count != second.Count)
            {
                return false;
            }

            for (int index = 0; index < first.Count; index++)
            {
                CityDecorationDescriptor firstDescriptor = first[index];
                CityDecorationDescriptor secondDescriptor = second[index];
                if (firstDescriptor.Kind != secondDescriptor.Kind ||
                    firstDescriptor.AnchorKind !=
                    secondDescriptor.AnchorKind ||
                    firstDescriptor.LotCell != secondDescriptor.LotCell ||
                    firstDescriptor.Variant != secondDescriptor.Variant ||
                    firstDescriptor.Palette != secondDescriptor.Palette)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsFinite(Vector3 value)
        {
            return IsFinite(value.x) &&
                   IsFinite(value.y) &&
                   IsFinite(value.z);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) &&
                   !float.IsInfinity(value);
        }
    }
}
