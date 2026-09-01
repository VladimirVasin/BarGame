using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The water where the mountain road crosses it.
    ///
    /// The road has had a `CulvertWater` sound anchor since it was built and
    /// nothing making the sound; these hold the water that answers it to the
    /// two things it cannot get wrong - it goes DOWN, and it goes UNDER the
    /// road rather than across it.
    /// </summary>
    public sealed class MountainRoadBrookTests
    {
        private static MountainRoadBrookPlan Brook(out MountainRoadPlan plan)
        {
            plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            return MountainRoadBrookPlanner.Create(plan);
        }

        [Test]
        [Category("MountainRoad")]
        public void BothReaches_OnlyEverDescend()
        {
            MountainRoadBrookPlan brook = Brook(out _);
            AssertDescends(brook.Inlet, "inlet");
            AssertDescends(brook.Outlet, "outlet");
        }

        private static void AssertDescends(
            IReadOnlyList<MountainRoadBrookSample> samples,
            string name)
        {
            Assert.That(samples.Count, Is.GreaterThan(8), name);
            for (int index = 1; index < samples.Count; index++)
            {
                Assert.That(
                    samples[index].Position.y,
                    Is.LessThan(samples[index - 1].Position.y),
                    $"The {name} rises at sample {index}.");
            }
        }

        /// <summary>
        /// THE WHOLE POINT OF A CULVERT. If the inlet did not stand above the
        /// outlet the water would be running up through the bore, and the one
        /// sentence this feature exists to say would be back to front.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void TheWaterCrossesUnderTheRoadDownhill()
        {
            MountainRoadBrookPlan brook = Brook(out MountainRoadPlan plan);

            Assert.That(
                brook.InletMouth.y,
                Is.GreaterThan(brook.OutletMouth.y),
                "The culvert's inlet must stand above its outlet.");

            // And the two mouths are on opposite sides of the carriageway,
            // or it is not a crossing at all.
            MountainRoadRouteSample road = plan.Route.Sample(
                plan.Route.Length *
                MountainRoadBrookPlanner.CulvertRouteFraction);
            float inletSide = Vector3.Dot(
                brook.InletMouth - road.Position,
                road.Right);
            float outletSide = Vector3.Dot(
                brook.OutletMouth - road.Position,
                road.Right);
            Assert.That(
                inletSide * outletSide,
                Is.LessThan(0f),
                "Both mouths are on the same side: the water never crosses.");
        }

        /// <summary>
        /// A brook over the carriageway is a ford, and this road has none -
        /// it has a culvert, which is the entire reason the water is here.
        /// </summary>
        [Test]
        [Category("MountainRoad")]
        public void NeitherReach_RunsOntoTheCarriageway()
        {
            MountainRoadBrookPlan brook = Brook(out MountainRoadPlan plan);
            AssertOffTheRoad(plan, brook.Inlet, "inlet");
            AssertOffTheRoad(plan, brook.Outlet, "outlet");
        }

        private static void AssertOffTheRoad(
            MountainRoadPlan plan,
            IReadOnlyList<MountainRoadBrookSample> samples,
            string name)
        {
            for (int index = 0; index < samples.Count; index++)
            {
                MountainRoadBrookSample sample = samples[index];
                var point = new Vector2(
                    sample.Position.x,
                    sample.Position.z);
                float best = float.MaxValue;
                float width = 0f;
                for (int probe = 0;
                     probe < plan.Route.Samples.Count;
                     probe++)
                {
                    MountainRoadRouteSample road = plan.Route.Samples[probe];
                    float distance = Vector2.Distance(
                        point,
                        new Vector2(road.Position.x, road.Position.z));
                    if (distance < best)
                    {
                        best = distance;
                        width = road.Width;
                    }
                }

                Assert.That(
                    best,
                    Is.GreaterThan(width * 0.5f + sample.HalfWidth),
                    $"The {name} runs onto the road at sample {index}.");
            }
        }

        [Test]
        [Category("MountainRoad")]
        public void ThePourStandsInTheBoreItComesOutOf()
        {
            MountainRoadBrookPlan brook = Brook(out MountainRoadPlan plan);

            Assert.That(
                brook.CulvertStableId,
                Is.EqualTo(MountainRoadBrookPlanner.CulvertStableId));
            Assert.That(
                brook.Bore.y,
                Is.GreaterThan(brook.OutletMouth.y),
                "A pour has to fall.");

            MountainRoadMiscDescriptor culvert = default;
            bool found = false;
            for (int index = 0; index < plan.Misc.Count; index++)
            {
                if (plan.Misc[index].StableId == brook.CulvertStableId)
                {
                    culvert = plan.Misc[index];
                    found = true;
                    break;
                }
            }

            Assert.That(found, Is.True, "The culvert went missing.");
            // The two mouths of one bore, which is the road's width plus
            // both shoulders - not a distance either end gets to choose.
            Assert.That(
                Vector3.Distance(brook.Bore, culvert.Position),
                Is.LessThan(11f),
                "The pour must come out of the culvert, not near it.");
            Assert.That(
                Vector3.Distance(brook.Bore, culvert.Position),
                Is.GreaterThan(4f),
                "A bore that short would not reach under the road.");
        }

        [Test]
        [Category("MountainRoad")]
        public void Planner_IsDeterministicForOneSeed()
        {
            MountainRoadBrookPlan first = Brook(out _);
            MountainRoadBrookPlan second = Brook(out _);

            Assert.That(
                second.Inlet.Count,
                Is.EqualTo(first.Inlet.Count));
            for (int index = 0; index < first.Inlet.Count; index++)
            {
                Assert.That(
                    Vector3.Distance(
                        first.Inlet[index].Position,
                        second.Inlet[index].Position),
                    Is.LessThan(0.0001f));
            }
        }
    }
}
