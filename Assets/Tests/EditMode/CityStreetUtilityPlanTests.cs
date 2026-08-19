using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityStreetUtilityPlanTests
    {
        [Test]
        public void CreateAll_DocksEveryBoothAndDumpster()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout);

            List<CityStreetUtilityDock> docks =
                CityStreetUtilityDock.CreateAll(layout, plan);
            List<CityStreetUtilityDock> again =
                CityStreetUtilityDock.CreateAll(layout, plan);

            int expected =
                plan.GetCount(CityDecorationKind.RoadsidePhoneBooth) +
                plan.GetCount(
                    CityDecorationKind.RoadsideDumpsterAndUtility);
            Assert.That(docks, Has.Count.EqualTo(expected));
            Assert.That(docks, Has.Count.GreaterThan(0));
            CollectionAssert.AreEqual(
                ToIds(docks),
                ToIds(again),
                "Docks must be deterministic for one plan.");

            var ids = new HashSet<string>();
            var decorationsById =
                new Dictionary<string, CityDecorationDescriptor>();
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                decorationsById.Add(
                    plan.Descriptors[index].StableId,
                    plan.Descriptors[index]);
            }

            for (int index = 0; index < docks.Count; index++)
            {
                CityStreetUtilityDock dock = docks[index];
                Assert.That(dock.IsPresent, Is.True);
                Assert.That(ids.Add(dock.Id), Is.True, dock.Id);
                Assert.That(
                    decorationsById.TryGetValue(
                        dock.DecorationId,
                        out CityDecorationDescriptor owner),
                    Is.True,
                    dock.Id);
                Assert.That(
                    owner.Kind,
                    Is.EqualTo(
                        dock.Kind == CityStreetUtilityKind.PhoneBooth
                            ? CityDecorationKind.RoadsidePhoneBooth
                            : CityDecorationKind
                                .RoadsideDumpsterAndUtility));

                // The docked body stands on the utility's own ground,
                // an arm's reach off the drawn door or lid, facing it.
                Assert.That(
                    dock.StandPosition.y,
                    Is.EqualTo(owner.Position.y).Within(0.001f));
                Assert.That(dock.Facing.y, Is.Zero);
                Assert.That(
                    dock.Facing.sqrMagnitude,
                    Is.EqualTo(1f).Within(0.001f));
                float planarDistance = new Vector2(
                    dock.StandPosition.x - owner.Position.x,
                    dock.StandPosition.z - owner.Position.z).magnitude;
                Assert.That(planarDistance, Is.InRange(0.9f, 1.6f));
            }
        }

        [Test]
        public void HomeYardDocks_OpenIntoTheYard()
        {
            CityDecorationPlan plan = CreateShippedPlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout);
            Assert.That(
                HomeYardSitePlanner.TryCreate(
                    layout,
                    out HomeYardSitePlan site),
                Is.True);

            List<CityStreetUtilityDock> docks =
                CityStreetUtilityDock.CreateAll(layout, plan);
            int yardDockCount = 0;
            for (int index = 0; index < docks.Count; index++)
            {
                CityStreetUtilityDock dock = docks[index];
                if (!dock.DecorationId.Contains("-homeyard-"))
                {
                    continue;
                }

                yardDockCount++;
                Assert.That(
                    site.GroundBounds.Contains(new Vector2(
                        dock.StandPosition.x,
                        dock.StandPosition.z)),
                    Is.True,
                    $"Dock '{dock.Id}' must stand inside the yard.");
            }

            Assert.That(yardDockCount, Is.EqualTo(2));
        }

        [Test]
        public void WorldBuilder_InstallsOnePlaceholderPerDock()
        {
            CityDecorationPlan plan = CreatePlan(
                GameSessionState.DefaultCitySeed,
                out CityLayout layout);
            List<CityStreetUtilityDock> docks =
                CityStreetUtilityDock.CreateAll(layout, plan);
            var parent = new GameObject(
                "Street Utility Test Parent");
            try
            {
                IReadOnlyList<CityStreetUtilityInteraction> interactions =
                    CityStreetUtilityWorldBuilder.Build(
                        parent.transform,
                        docks);
                Assert.That(
                    interactions,
                    Has.Count.EqualTo(docks.Count),
                    "Every dock must carry its placeholder.");
                for (int index = 0; index < interactions.Count; index++)
                {
                    CityStreetUtilityInteraction interaction =
                        interactions[index];
                    Assert.That(interaction.Dock.IsPresent, Is.True);
                    Assert.That(
                        interaction.PromptKey,
                        Is.EqualTo(
                            interaction.Dock.Kind ==
                            CityStreetUtilityKind.PhoneBooth
                                ? CityStreetUtilityInteraction
                                    .PhoneBoothPromptKey
                                : CityStreetUtilityInteraction
                                    .DumpsterPromptKey));
                    Assert.That(
                        interaction.InteractionPosition,
                        Is.EqualTo(interaction.Dock.StandPosition));
                    BoxCollider trigger =
                        interaction.GetComponent<BoxCollider>();
                    Assert.That(trigger, Is.Not.Null);
                    Assert.That(trigger.isTrigger, Is.True);
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// The shipped city. The legacy blueprint the other cases run
        /// on carries no home yard at every seed, so anything that
        /// asserts on the yard has to ask for the real one.
        /// </summary>
        private static CityDecorationPlan CreateShippedPlan(
            int seed,
            out CityLayout layout)
        {
            layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                seed);
            RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            return CityDecorationPlanner.CreatePlan(
                layout,
                fence,
                night);
        }

        private static CityDecorationPlan CreatePlan(
            int seed,
            out CityLayout layout)
        {
            layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                seed);
            RoadFencePlan fence = RoadFencePlanner.CreatePlan(layout);
            CityNightFixturePlan night =
                CityNightFixturePlanner.CreatePlan(layout);
            return CityDecorationPlanner.CreatePlan(
                layout,
                fence,
                night);
        }

        private static List<string> ToIds(
            IReadOnlyList<CityStreetUtilityDock> docks)
        {
            var ids = new List<string>(docks.Count);
            for (int index = 0; index < docks.Count; index++)
            {
                ids.Add(docks[index].Id);
            }

            return ids;
        }
    }
}
