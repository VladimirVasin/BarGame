using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityLayoutCacheTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        // Another seed the default blueprint is known to generate for:
        // HomeBalconyLayoutTests runs the default-blueprint path on it.
        private const int OtherSeed = 73119;

        [SetUp]
        public void ClearCache()
        {
            CityLayoutCache.Reset();
        }

        [TearDown]
        public void DropCache()
        {
            CityLayoutCache.Reset();
            // A run the reset abandoned finishes on its pool thread; wait
            // for it here so it cannot overlap a fixture that plans directly.
            Assert.That(
                CityLayoutCache.AbandonedForeignAreaPlanWork.Wait(60_000),
                Is.True);
        }

        [Test]
        public void GetOrGenerate_SameBlueprintSeedAndDefaultSettings_ReturnsSameInstance()
        {
            CityBlueprint blueprint = CityBlueprintCatalog.Default;

            // Default is a fresh settings instance on every read, so a hit
            // proves the field comparison, not reference identity.
            CityLayout first = CityLayoutCache.GetOrGenerate(
                blueprint,
                CityGenerationSettings.Default,
                Seed);
            CityLayout second = CityLayoutCache.GetOrGenerate(
                blueprint,
                CityGenerationSettings.Default,
                Seed);

            Assert.That(second, Is.SameAs(first));
            Assert.That(first.Seed, Is.EqualTo(Seed));
            Assert.That(first.BlueprintId, Is.EqualTo(blueprint.Id));
        }

        [Test]
        public void GetOrGenerate_DifferentSettings_Misses()
        {
            CityBlueprint blueprint = CityBlueprintCatalog.Default;
            CityLayout cached = CityLayoutCache.GetOrGenerate(
                blueprint,
                CityGenerationSettings.Default,
                Seed);

            // A field that only sizes the masses, so the edited city still
            // satisfies every layout contract.
            CityGenerationSettings edited = CityGenerationSettings.Default;
            edited.MaximumOrdinaryBuildingHeight += 1f;
            CityLayout fresh = CityLayoutCache.GetOrGenerate(
                blueprint,
                edited,
                Seed);

            Assert.That(fresh, Is.Not.SameAs(cached));
            // The slot is single: the edited city now occupies it and the
            // default one has to be generated again.
            Assert.That(
                CityLayoutCache.GetOrGenerate(
                    blueprint,
                    CityGenerationSettings.Default,
                    Seed),
                Is.Not.SameAs(cached).And.Not.SameAs(fresh));
        }

        [Test]
        public void GetOrGenerate_DifferentSeed_Misses()
        {
            CityBlueprint blueprint = CityBlueprintCatalog.Default;
            CityLayout first = CityLayoutCache.GetOrGenerate(
                blueprint,
                CityGenerationSettings.Default,
                Seed);

            CityLayout second = CityLayoutCache.GetOrGenerate(
                blueprint,
                CityGenerationSettings.Default,
                OtherSeed);

            Assert.That(second, Is.Not.SameAs(first));
            Assert.That(second.Seed, Is.EqualTo(OtherSeed));
        }

        [Test]
        public void Reset_DropsTheCachedLayout()
        {
            CityBlueprint blueprint = CityBlueprintCatalog.Default;
            CityLayout first = CityLayoutCache.GetOrGenerate(
                blueprint,
                CityGenerationSettings.Default,
                Seed);

            CityLayoutCache.Reset();

            Assert.That(
                CityLayoutCache.GetOrGenerate(
                    blueprint,
                    CityGenerationSettings.Default,
                    Seed),
                Is.Not.SameAs(first));
        }

        [Test]
        public void GetOrCreateNightPlan_IsOneInstancePerLayout()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CityLayout other = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);

            CityNightFixturePlan first =
                CityLayoutCache.GetOrCreateNightPlan(layout);

            Assert.That(
                CityLayoutCache.GetOrCreateNightPlan(layout),
                Is.SameAs(first));
            Assert.That(
                CityLayoutCache.GetOrCreateNightPlan(other),
                Is.Not.SameAs(first));
        }

        [Test]
        public void CityWorldPlans_ReusePerLayoutAndReplanDecorationsPerNightPlan()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CityNightFixturePlan night =
                CityLayoutCache.GetOrCreateNightPlan(layout);

            Assert.That(CityWorldPlans.IsMemoised(layout), Is.False);
            CityWorldPlans first = CityWorldPlans.GetOrCreate(layout);
            CityDecorationPlan decorations = first.GetDecoration(night);

            Assert.That(CityWorldPlans.IsMemoised(layout), Is.True);
            Assert.That(CityWorldPlans.GetOrCreate(layout), Is.SameAs(first));
            Assert.That(first.GetDecoration(night), Is.SameAs(decorations));

            // A different night plan instance is a different input to the
            // dressing, even when its content is identical.
            CityNightFixturePlan otherNight =
                CityNightFixturePlanner.CreatePlan(layout);
            Assert.That(
                first.GetDecoration(otherNight),
                Is.Not.SameAs(decorations));
        }

        [Test]
        public void GetOrCreateForeignAreaPlans_AreOneInstancePerSeed()
        {
            MountainRoadPlan road = CityLayoutCache.GetOrCreateMountainRoad(Seed);
            AlpineVillagePlan village =
                CityLayoutCache.GetOrCreateAlpineVillage(Seed);

            Assert.That(
                CityLayoutCache.GetOrCreateMountainRoad(Seed),
                Is.SameAs(road));
            Assert.That(
                CityLayoutCache.GetOrCreateAlpineVillage(Seed),
                Is.SameAs(village));
            Assert.That(road.Seed, Is.EqualTo(Seed));
            Assert.That(village.Seed, Is.EqualTo(Seed));
        }

        [Test]
        public void PrimeForeignAreaPlans_JoinsPlansEqualToPlanningOnTheMainThread()
        {
            CityLayoutCache.PrimeForeignAreaPlans(Seed);
            Assert.That(CityLayoutCache.PendingForeignAreaPlanCount, Is.EqualTo(2));
            // Priming again starts nothing: the seed's work is already out.
            CityLayoutCache.PrimeForeignAreaPlans(Seed);
            Assert.That(CityLayoutCache.PendingForeignAreaPlanCount, Is.EqualTo(2));

            MountainRoadPlan road = CityLayoutCache.GetOrCreateMountainRoad(Seed);
            AlpineVillagePlan village =
                CityLayoutCache.GetOrCreateAlpineVillage(Seed);

            Assert.That(CityLayoutCache.PendingForeignAreaPlanCount, Is.Zero);
            // Joined once: the getters memoise what the task handed back.
            Assert.That(
                CityLayoutCache.GetOrCreateMountainRoad(Seed),
                Is.SameAs(road));
            Assert.That(
                CityLayoutCache.GetOrCreateAlpineVillage(Seed),
                Is.SameAs(village));
            AssertSamePlan(road, MountainRoadPlanner.Create(Seed));
            AssertSamePlan(village, AlpineVillagePlanner.Create(Seed));
        }

        [Test]
        public void PrimeForeignAreaPlans_SkipsThePlanTheSessionHolds()
        {
            // An exterior root holds its own plan before it primes; only
            // the other exterior's planner may start.
            MountainRoadPlan road = CityLayoutCache.GetOrCreateMountainRoad(Seed);

            CityLayoutCache.PrimeForeignAreaPlans(Seed);

            Assert.That(CityLayoutCache.PendingForeignAreaPlanCount, Is.EqualTo(1));
            Assert.That(
                CityLayoutCache.GetOrCreateMountainRoad(Seed),
                Is.SameAs(road));
            Assert.That(
                CityLayoutCache.GetOrCreateAlpineVillage(Seed).Seed,
                Is.EqualTo(Seed));
            Assert.That(CityLayoutCache.PendingForeignAreaPlanCount, Is.Zero);
        }

        [Test]
        public void Reset_DropsPendingForeignAreaPlansWithoutWaiting()
        {
            CityLayoutCache.PrimeForeignAreaPlans(Seed);
            Assert.That(CityLayoutCache.PendingForeignAreaPlanCount, Is.EqualTo(2));

            Assert.DoesNotThrow(CityLayoutCache.Reset);

            Assert.That(CityLayoutCache.PendingForeignAreaPlanCount, Is.Zero);
            // Nothing left to join: the getter plans on the main thread
            // again and memoises that.
            AlpineVillagePlan village =
                CityLayoutCache.GetOrCreateAlpineVillage(Seed);
            Assert.That(village.Seed, Is.EqualTo(Seed));
            Assert.That(
                CityLayoutCache.GetOrCreateAlpineVillage(Seed),
                Is.SameAs(village));
        }

        /// <summary>
        /// Exact, not approximate: a plan is a pure function of its seed and
        /// the memo's contract is that a hit is bit-identical to a fresh
        /// generation, on whichever thread the generation ran.
        /// </summary>
        private static void AssertSamePlan(
            MountainRoadPlan actual,
            MountainRoadPlan expected)
        {
            Assert.That(actual.Seed, Is.EqualTo(expected.Seed));
            Assert.That(actual.WorldBounds, Is.EqualTo(expected.WorldBounds));
            Assert.That(
                actual.Route.Samples.Count,
                Is.EqualTo(expected.Route.Samples.Count));
            Assert.That(actual.Misc.Count, Is.EqualTo(expected.Misc.Count));
            Assert.That(actual.Ridges.Count, Is.EqualTo(expected.Ridges.Count));
            Assert.That(actual.Forest.Count, Is.EqualTo(expected.Forest.Count));
            for (int index = 0; index < expected.Forest.Count; index++)
            {
                Assert.That(
                    actual.Forest[index].Position,
                    Is.EqualTo(expected.Forest[index].Position));
            }
        }

        private static void AssertSamePlan(
            AlpineVillagePlan actual,
            AlpineVillagePlan expected)
        {
            Assert.That(actual.Seed, Is.EqualTo(expected.Seed));
            Assert.That(actual.WorldBounds, Is.EqualTo(expected.WorldBounds));
            Assert.That(
                actual.Lane.Samples.Count,
                Is.EqualTo(expected.Lane.Samples.Count));
            Assert.That(actual.Ridges.Count, Is.EqualTo(expected.Ridges.Count));
            Assert.That(
                actual.MothersHouse.DoorGroundPosition,
                Is.EqualTo(expected.MothersHouse.DoorGroundPosition));
            Assert.That(actual.Plots.Count, Is.EqualTo(expected.Plots.Count));
            for (int index = 0; index < expected.Plots.Count; index++)
            {
                Assert.That(
                    actual.Plots[index].GroundCenter,
                    Is.EqualTo(expected.Plots[index].GroundCenter));
            }
        }
    }
}
