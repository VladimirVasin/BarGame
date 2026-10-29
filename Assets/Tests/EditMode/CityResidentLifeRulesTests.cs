using System;
using System.Collections.Generic;
using NUnit.Framework;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityResidentLifeRulesTests
    {
        [TestCase(120d, 5)]
        [TestCase(420d, 28)]
        [TestCase(720d, 22)]
        [TestCase(1140d, 36)]
        [TestCase(1380d, 10)]
        public void Population_UsesTimeWindowsAndBoundedRamps(double minute, int expected)
        {
            Assert.That(CityResidentLifeRules.TargetStreetPopulation(minute), Is.EqualTo(expected));
            for (int time = 0; time < CityResidentLifeRules.MinutesPerDay; time++)
            {
                int target = CityResidentLifeRules.TargetStreetPopulation(time);
                int next = CityResidentLifeRules.TargetStreetPopulation((time + 1) % 1440);
                Assert.That(target, Is.InRange(5, 36));
                Assert.That(Math.Abs(target - next), Is.LessThanOrEqualTo(2),
                    "Clock boundaries must not replace the crowd in one frame.");
            }
        }

        [Test]
        public void Schedule_TimeJumpsResolveAbsoluteIntervalsAndTheirSuccessors()
        {
            foreach (int seed in new[] { int.MinValue, 0, 71, int.MaxValue })
            for (int resident = 0; resident < CityResidentLifeRules.ResidentCount; resident++)
            foreach (double minute in new[] { 0d, 359.75d, 720d, 1020d, 1439.99d })
            {
                const int day = 3;
                CityResidentLifeSample sample = CityResidentLifeRules.Evaluate(resident, day, minute, seed);
                double absolute = day * 1440d + minute;
                Assert.That(sample.StartedAt, Is.LessThanOrEqualTo(absolute));
                Assert.That(sample.EndsAt, Is.GreaterThan(absolute));
                Assert.That(sample.EndsAt - sample.StartedAt, Is.LessThan(1440d));
                Assert.That(CityResidentLifeRules.ActivityAt(resident, day, minute, seed),
                    Is.EqualTo(sample.Activity));

                int nextDay = (int)Math.Floor(sample.EndsAt / 1440d);
                double nextMinute = sample.EndsAt - nextDay * 1440d;
                CityResidentLifeSample next = CityResidentLifeRules.Evaluate(resident,
                    nextDay, nextMinute, seed);
                Assert.That(next.StartedAt, Is.EqualTo(sample.EndsAt));
                Assert.That(next.PreviousActivity, Is.EqualTo(sample.Activity));
                Assert.That(next.Activity, Is.Not.EqualTo(sample.Activity));
            }
        }

        [Test]
        public void Schedule_OvernightContinuityAndStableIdentityDoNotDependOnFrameHistory()
        {
            var offsets = new HashSet<int>();
            var priorities = new HashSet<uint>();
            for (int resident = 0; resident < CityResidentLifeRules.ResidentCount; resident++)
            {
                offsets.Add(CityResidentLifeRules.ScheduleOffsetMinutes(resident, 19));
                uint priority = CityResidentLifeRules.StreetPriority(resident, 2, 19);
                priorities.Add(priority);
                Assert.That(CityResidentLifeRules.StreetPriority(resident, 2, 19), Is.EqualTo(priority));
                CityResidentLifeSample first = CityResidentLifeRules.Evaluate(resident, 2, 1439.5d, 19);
                CityResidentLifeSample second = CityResidentLifeRules.Evaluate(resident, 3, 0d, 19);
                if (first.EndsAt > 3 * 1440d)
                {
                    Assert.That(second.StartedAt, Is.EqualTo(first.StartedAt));
                    Assert.That(second.EndsAt, Is.EqualTo(first.EndsAt));
                    Assert.That(second.Activity, Is.EqualTo(first.Activity));
                }
                else
                {
                    Assert.That(second.StartedAt, Is.EqualTo(first.EndsAt));
                    Assert.That(second.PreviousActivity, Is.EqualTo(first.Activity));
                }
            }

            Assert.That(offsets.Count, Is.GreaterThan(1));
            Assert.That(priorities.Count, Is.GreaterThan(1));
            Assert.That(CityResidentLifeRules.IsOnStreet(
                CityResidentLifeRules.ActivityAt(7, 0, 120d, 19)), Is.True,
                "The sparse night population must have scheduled residents to represent.");
            Assert.That(CityResidentLifeRules.ActivityAt(0, 0, 720d, 19),
                Is.EqualTo(CityResidentActivity.Work));
            Assert.That(CityResidentLifeRules.ActivityAt(3, 0, 720d, 19),
                Is.EqualTo(CityResidentActivity.Walking));
        }

        [Test]
        public void Rules_RejectInvalidCalendarAndResidentInputs()
        {
            foreach (double minute in new[] { -1d, 1440d, double.NaN, double.NegativeInfinity,
                         double.PositiveInfinity })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    CityResidentLifeRules.TargetStreetPopulation(minute));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    CityResidentLifeRules.Evaluate(0, 0, minute, 0));
            }

            foreach (int resident in new[] { -1, CityResidentLifeRules.ResidentCount })
            {
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    CityResidentLifeRules.Evaluate(resident, 0, 0d, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    CityResidentLifeRules.ScheduleOffsetMinutes(resident, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() =>
                    CityResidentLifeRules.StreetPriority(resident, 0, 0));
            }
            Assert.Throws<ArgumentOutOfRangeException>(() => CityResidentLifeRules.Evaluate(0, -1, 0d, 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => CityResidentLifeRules.StreetPriority(0, -1, 0));
        }
    }
}
