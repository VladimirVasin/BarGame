using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CitySoundscapePlannerTests
    {
        [Test]
        public void Create_OrdersStableIdsAndPartitionsPlayback()
        {
            CitySoundSourceDescriptor scheduled = Scheduled(
                "city.old-town.waterworks.drip",
                CityDistrictKind.OldTown,
                CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                CitySourceSoundId.WaterworksDrip);
            CitySoundSourceDescriptor loop = Loop(
                "city.old-town.waterworks.pipe",
                CityDistrictKind.OldTown,
                CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                CitySourceSoundId.WaterworksPipeLoop);

            CitySoundscapePlan plan = CitySoundscapePlanner.Create(
                190734,
                new[] { loop, scheduled });

            Assert.That(plan.CitySeed, Is.EqualTo(190734));
            Assert.That(plan.IsDistrictView, Is.False);
            Assert.That(plan.Sources.Count, Is.EqualTo(2));
            Assert.That(
                plan.Sources[0].StableId,
                Is.EqualTo("city.old-town.waterworks.drip"));
            Assert.That(
                plan.Sources[1].StableId,
                Is.EqualTo("city.old-town.waterworks.pipe"));
            Assert.That(plan.LoopingSources, Is.EqualTo(new[] { loop }));
            Assert.That(
                plan.ScheduledSources,
                Is.EqualTo(new[] { scheduled }));
            Assert.That(
                plan.GetRequiredSource(loop.StableId),
                Is.SameAs(loop));
        }

        [Test]
        public void Create_RejectsDuplicateStableIdsOrdinally()
        {
            CitySoundSourceDescriptor first = Loop(
                "city.old-town.waterworks.pipe",
                CityDistrictKind.OldTown,
                CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                CitySourceSoundId.WaterworksPipeLoop);
            CitySoundSourceDescriptor duplicate = Scheduled(
                first.StableId,
                CityDistrictKind.OldTown,
                CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                CitySourceSoundId.WaterworksDrip);

            Assert.Throws<ArgumentException>(() =>
                CitySoundscapePlanner.Create(
                    1,
                    new[] { first, duplicate }));
        }

        [Test]
        public void Descriptor_RejectsUnexplainedOrInvalidAnchors()
        {
            Assert.Throws<ArgumentException>(() =>
                new CitySoundSourceDescriptor(
                    "city.bad.owner-cue",
                    CityDistrictKind.Industrial,
                    CitySoundPhysicalOwnerKind.IndustrialWeighbridge,
                    CitySourceSoundId.WaterworksPipeLoop,
                    Vector3.zero,
                    new Bounds(Vector3.zero, Vector3.one),
                    10f,
                    CitySourceSoundPlayback.Loop,
                    CitySoundScheduleInterval.None));

            Assert.Throws<ArgumentException>(() =>
                new CitySoundSourceDescriptor(
                    "city.bad.outside-bounds",
                    CityDistrictKind.OldTown,
                    CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                    CitySourceSoundId.WaterworksPipeLoop,
                    Vector3.right * 3f,
                    new Bounds(Vector3.zero, Vector3.one),
                    10f,
                    CitySourceSoundPlayback.Loop,
                    CitySoundScheduleInterval.None));

            Assert.Throws<ArgumentException>(() =>
                new CitySoundSourceDescriptor(
                    "city.bad.district",
                    CityDistrictKind.Residential,
                    CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                    CitySourceSoundId.WaterworksPipeLoop,
                    Vector3.zero,
                    new Bounds(Vector3.zero, Vector3.one),
                    10f,
                    CitySourceSoundPlayback.Loop,
                    CitySoundScheduleInterval.None));

            Assert.Throws<ArgumentException>(() =>
                new CitySoundSourceDescriptor(
                    "city.bad.loop-schedule",
                    CityDistrictKind.OldTown,
                    CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                    CitySourceSoundId.WaterworksPipeLoop,
                    Vector3.zero,
                    new Bounds(Vector3.zero, Vector3.one),
                    10f,
                    CitySourceSoundPlayback.Loop,
                    new CitySoundScheduleInterval(2f, 4f)));

            var triggered = new CitySoundSourceDescriptor(
                "city.valid.triggered",
                CityDistrictKind.Industrial,
                CitySoundPhysicalOwnerKind.IndustrialWeighbridge,
                CitySourceSoundId.IndustrialMetalStress,
                Vector3.zero,
                new Bounds(Vector3.zero, Vector3.one),
                10f,
                CitySourceSoundPlayback.OneShot,
                CitySoundScheduleInterval.None);
            Assert.That(triggered.IsTriggered, Is.True);
            Assert.That(triggered.IsScheduled, Is.False);

            Assert.Throws<ArgumentException>(() => Scheduled(
                "city.bad.timed-scale",
                CityDistrictKind.Industrial,
                CitySoundPhysicalOwnerKind.IndustrialWeighbridge,
                CitySourceSoundId.IndustrialMetalStress));
            Assert.Throws<ArgumentException>(() => Triggered(
                "city.bad.unscheduled-drip",
                CityDistrictKind.OldTown,
                CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                CitySourceSoundId.WaterworksDrip));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new CitySoundSourceDescriptor(
                    "city.bad.radius",
                    CityDistrictKind.OldTown,
                    CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                    CitySourceSoundId.WaterworksPipeLoop,
                    Vector3.zero,
                    new Bounds(Vector3.zero, Vector3.one),
                    float.NaN,
                    CitySourceSoundPlayback.Loop,
                    CitySoundScheduleInterval.None));

            Assert.Throws<ArgumentException>(() =>
                new CitySoundSourceDescriptor(
                    "city.bad.bounds",
                    CityDistrictKind.OldTown,
                    CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                    CitySourceSoundId.WaterworksPipeLoop,
                    Vector3.zero,
                    new Bounds(Vector3.zero, Vector3.zero),
                    10f,
                    CitySourceSoundPlayback.Loop,
                    CitySoundScheduleInterval.None));
        }

        [Test]
        public void DistrictProfile_FiltersRealDescriptorsWithoutInventingAny()
        {
            CitySoundSourceDescriptor waterworks = Loop(
                "city.old-town.waterworks.pipe",
                CityDistrictKind.OldTown,
                CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                CitySourceSoundId.WaterworksPipeLoop);
            CitySoundSourceDescriptor mechanism = Loop(
                "city.industrial.weighbridge.mechanism",
                CityDistrictKind.Industrial,
                CitySoundPhysicalOwnerKind.IndustrialWeighbridge,
                CitySourceSoundId.IndustrialWeighbridgeMechanismLoop);

            CitySoundscapePlan oldTown =
                CitySoundscapePlanner.CreateForDistrict(
                    71,
                    CityDistrictKind.OldTown,
                    new[] { mechanism, waterworks });

            Assert.That(oldTown.IsDistrictView, Is.True);
            Assert.That(
                oldTown.Profile,
                Is.SameAs(
                    CitySoundDistrictProfiles.Get(
                        CityDistrictKind.OldTown)));
            Assert.That(oldTown.Sources.Count, Is.EqualTo(1));
            Assert.That(oldTown.Sources[0], Is.SameAs(waterworks));
            Assert.That(
                oldTown.Profile.AllowedCues,
                Does.Contain(CitySourceSoundId.WaterworksDrip),
                "The profile permits a drip but the plan still contains " +
                "only the real pipe descriptor supplied by the world.");

            CitySoundscapePlan park =
                CitySoundscapePlanner.CreateForDistrict(
                    71,
                    CityDistrictKind.CentralPark,
                    new[] { mechanism, waterworks });
            Assert.That(park.Sources, Is.Empty);
        }

        [TestCase(
            CityDistrictKind.OldTown,
            CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
            CitySourceSoundId.WaterworksPipeLoop,
            CitySourceSoundPlayback.Loop)]
        [TestCase(
            CityDistrictKind.OldTown,
            CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
            CitySourceSoundId.WaterworksDrip,
            CitySourceSoundPlayback.OneShot)]
        [TestCase(
            CityDistrictKind.Residential,
            CitySoundPhysicalOwnerKind.ResidentialDryingYard,
            CitySourceSoundId.DryingYardClothLoop,
            CitySourceSoundPlayback.Loop)]
        [TestCase(
            CityDistrictKind.Residential,
            CitySoundPhysicalOwnerKind.ResidentialDryingYard,
            CitySourceSoundId.DryingYardRopeCreak,
            CitySourceSoundPlayback.OneShot)]
        [TestCase(
            CityDistrictKind.Residential,
            CitySoundPhysicalOwnerKind.ResidentialDryingYard,
            CitySourceSoundId.DryingYardCarpetStrike,
            CitySourceSoundPlayback.OneShot)]
        [TestCase(
            CityDistrictKind.Industrial,
            CitySoundPhysicalOwnerKind.IndustrialWeighbridge,
            CitySourceSoundId.IndustrialWeighbridgeMechanismLoop,
            CitySourceSoundPlayback.Loop)]
        [TestCase(
            CityDistrictKind.Industrial,
            CitySoundPhysicalOwnerKind.IndustrialWeighbridge,
            CitySourceSoundId.IndustrialMetalStress,
            CitySourceSoundPlayback.OneShot)]
        [TestCase(
            CityDistrictKind.Nightlife,
            CitySoundPhysicalOwnerKind.NightlifeLastRouteIsland,
            CitySourceSoundId.LastRouteRelayLoop,
            CitySourceSoundPlayback.Loop)]
        [TestCase(
            CityDistrictKind.Nightlife,
            CitySoundPhysicalOwnerKind.NightlifeLastRouteIsland,
            CitySourceSoundId.LastRouteIncompleteChime,
            CitySourceSoundPlayback.OneShot)]
        [TestCase(
            CityDistrictKind.CentralPark,
            CitySoundPhysicalOwnerKind.ParkFountainAndStatue,
            CitySourceSoundId.ParkFountainLoop,
            CitySourceSoundPlayback.Loop)]
        [TestCase(
            CityDistrictKind.CentralPark,
            CitySoundPhysicalOwnerKind.ParkPlayground,
            CitySourceSoundId.ParkSwingCreak,
            CitySourceSoundPlayback.OneShot)]
        public void CatalogPairs_ArePhysicallyExplained(
            CityDistrictKind district,
            CitySoundPhysicalOwnerKind owner,
            CitySourceSoundId cue,
            CitySourceSoundPlayback playback)
        {
            CitySoundSourceDescriptor source =
                playback == CitySourceSoundPlayback.Loop
                    ? Loop("city.valid", district, owner, cue)
                    : RequiresPhysicalTrigger(cue)
                        ? Triggered("city.valid", district, owner, cue)
                        : Scheduled("city.valid", district, owner, cue);

            Assert.That(source.PhysicalOwner, Is.EqualTo(owner));
            Assert.That(source.Cue, Is.EqualTo(cue));
            Assert.That(source.Playback, Is.EqualTo(playback));
            Assert.That(
                CitySoundDistrictProfiles.Get(district).Allows(source),
                Is.True);
        }

        [Test]
        public void StableHash_IsRepeatableAndSensitiveToIdentity()
        {
            uint first = CitySoundStableHash.SourceEvent(
                190734,
                "city.old-town.waterworks.drip",
                4u);
            uint repeated = CitySoundStableHash.SourceEvent(
                190734,
                "city.old-town.waterworks.drip",
                4u);
            uint next = CitySoundStableHash.SourceEvent(
                190734,
                "city.old-town.waterworks.drip",
                5u);
            uint other = CitySoundStableHash.SourceEvent(
                190734,
                "city.industrial.weighbridge.stress",
                4u);

            Assert.That(repeated, Is.EqualTo(first));
            Assert.That(next, Is.Not.EqualTo(first));
            Assert.That(other, Is.Not.EqualTo(first));
            Assert.That(
                CitySoundStableHash.ToUnitFloat(first),
                Is.InRange(0f, 1f));
        }

        private static CitySoundSourceDescriptor Loop(
            string stableId,
            CityDistrictKind district,
            CitySoundPhysicalOwnerKind owner,
            CitySourceSoundId cue)
        {
            return new CitySoundSourceDescriptor(
                stableId,
                district,
                owner,
                cue,
                Vector3.zero,
                new Bounds(Vector3.zero, Vector3.one),
                14f,
                CitySourceSoundPlayback.Loop,
                CitySoundScheduleInterval.None);
        }

        private static CitySoundSourceDescriptor Scheduled(
            string stableId,
            CityDistrictKind district,
            CitySoundPhysicalOwnerKind owner,
            CitySourceSoundId cue)
        {
            return new CitySoundSourceDescriptor(
                stableId,
                district,
                owner,
                cue,
                Vector3.zero,
                new Bounds(Vector3.zero, Vector3.one),
                14f,
                CitySourceSoundPlayback.OneShot,
                new CitySoundScheduleInterval(4f, 9f));
        }

        private static CitySoundSourceDescriptor Triggered(
            string stableId,
            CityDistrictKind district,
            CitySoundPhysicalOwnerKind owner,
            CitySourceSoundId cue)
        {
            return new CitySoundSourceDescriptor(
                stableId,
                district,
                owner,
                cue,
                Vector3.zero,
                new Bounds(Vector3.zero, Vector3.one),
                14f,
                CitySourceSoundPlayback.OneShot,
                CitySoundScheduleInterval.None);
        }

        private static bool RequiresPhysicalTrigger(
            CitySourceSoundId cue)
        {
            return cue == CitySourceSoundId.DryingYardCarpetStrike ||
                   cue == CitySourceSoundId.IndustrialMetalStress ||
                   cue == CitySourceSoundId.ParkSwingCreak;
        }
    }
}
