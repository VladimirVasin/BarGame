using System;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CitySoundSchedulePlannerTests
    {
        [Test]
        public void Start_IsDeterministicAndInsideAuthoredInterval()
        {
            CitySoundscapePlan plan = CreateScheduledPlan(190734);

            CitySoundScheduleCursor first =
                CitySoundSchedulePlanner.Start(
                    plan,
                    SourceId,
                    10d);
            CitySoundScheduleCursor repeated =
                CitySoundSchedulePlanner.Start(
                    plan,
                    SourceId,
                    10d);

            Assert.That(first.CitySeed, Is.EqualTo(plan.CitySeed));
            Assert.That(first.SourceStableId, Is.EqualTo(SourceId));
            Assert.That(first.EventOrdinal, Is.Zero);
            Assert.That(
                repeated.NextEventTimeSeconds,
                Is.EqualTo(first.NextEventTimeSeconds));
            Assert.That(
                first.NextEventTimeSeconds,
                Is.InRange(14d, 19d));
            Assert.That(first.IsDue(10d), Is.False);
            Assert.That(
                first.IsDue(first.NextEventTimeSeconds),
                Is.True);
        }

        [Test]
        public void AdvanceAfterLateFiring_DoesNotAccumulateCatchUpDebt()
        {
            CitySoundscapePlan plan = CreateScheduledPlan(190734);
            CitySoundScheduleCursor due =
                CitySoundSchedulePlanner.Start(
                    plan,
                    SourceId,
                    0d);

            const double resumedAt = 10000d;
            Assert.That(due.IsDue(resumedAt), Is.True);

            CitySoundScheduleCursor next =
                CitySoundSchedulePlanner.AdvanceAfterFiring(
                    plan,
                    due,
                    resumedAt);

            Assert.That(next.EventOrdinal, Is.EqualTo(1u));
            Assert.That(
                next.NextEventTimeSeconds,
                Is.InRange(resumedAt + 4d, resumedAt + 9d));
            Assert.That(next.IsDue(resumedAt), Is.False);
        }

        [Test]
        public void Advance_IsDeterministicForTheSameObservedFireTime()
        {
            CitySoundscapePlan plan = CreateScheduledPlan(190734);
            CitySoundScheduleCursor first =
                CitySoundSchedulePlanner.Start(plan, SourceId, 0d);
            CitySoundScheduleCursor second =
                CitySoundSchedulePlanner.Start(plan, SourceId, 0d);
            double fireTime = first.NextEventTimeSeconds + 0.25d;

            CitySoundScheduleCursor firstNext =
                CitySoundSchedulePlanner.AdvanceAfterFiring(
                    plan,
                    first,
                    fireTime);
            CitySoundScheduleCursor secondNext =
                CitySoundSchedulePlanner.AdvanceAfterFiring(
                    plan,
                    second,
                    fireTime);

            Assert.That(
                secondNext.NextEventTimeSeconds,
                Is.EqualTo(firstNext.NextEventTimeSeconds));
        }

        [Test]
        public void Schedule_RejectsLoopsEarlyEventsAndForeignSeeds()
        {
            CitySoundscapePlan scheduledPlan = CreateScheduledPlan(100);
            CitySoundScheduleCursor cursor =
                CitySoundSchedulePlanner.Start(
                    scheduledPlan,
                    SourceId,
                    0d);

            Assert.Throws<InvalidOperationException>(() =>
                CitySoundSchedulePlanner.AdvanceAfterFiring(
                    scheduledPlan,
                    cursor,
                    0d));

            CitySoundscapePlan foreignPlan = CreateScheduledPlan(101);
            Assert.Throws<ArgumentException>(() =>
                CitySoundSchedulePlanner.AdvanceAfterFiring(
                    foreignPlan,
                    cursor,
                    cursor.NextEventTimeSeconds));

            CitySoundSourceDescriptor loop =
                new CitySoundSourceDescriptor(
                    "city.old-town.waterworks.pipe",
                    CityDistrictKind.OldTown,
                    CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                    CitySourceSoundId.WaterworksPipeLoop,
                    Vector3.zero,
                    new Bounds(Vector3.zero, Vector3.one),
                    14f,
                    CitySourceSoundPlayback.Loop,
                    CitySoundScheduleInterval.None);
            CitySoundscapePlan loopPlan = CitySoundscapePlanner.Create(
                100,
                new[] { loop });
            Assert.Throws<ArgumentException>(() =>
                CitySoundSchedulePlanner.Start(
                    loopPlan,
                    loop.StableId,
                    0d));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CitySoundSchedulePlanner.Start(
                    scheduledPlan,
                    SourceId,
                    double.NaN));
        }

        private const string SourceId =
            "city.old-town.waterworks.drip";

        private static CitySoundscapePlan CreateScheduledPlan(int seed)
        {
            var source = new CitySoundSourceDescriptor(
                SourceId,
                CityDistrictKind.OldTown,
                CitySoundPhysicalOwnerKind.OldTownWaterworksCourt,
                CitySourceSoundId.WaterworksDrip,
                new Vector3(1f, 2f, 3f),
                new Bounds(
                    new Vector3(1f, 2f, 3f),
                    new Vector3(0.4f, 0.4f, 0.4f)),
                10f,
                CitySourceSoundPlayback.OneShot,
                new CitySoundScheduleInterval(4f, 9f));
            return CitySoundscapePlanner.Create(seed, new[] { source });
        }
    }
}
