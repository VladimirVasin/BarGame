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
    }
}
