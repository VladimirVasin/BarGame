using System.Collections.Generic;
using System.Text;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The spring's water, held to the things a brook cannot do.
    ///
    /// The village shipped a spring plot, a stone catch and a running-water
    /// sound while the water itself was two boxes textured as STONE - the
    /// world builder said so in its own comment. Nothing failed, because
    /// nothing was measuring the water. These are what measure it.
    /// </summary>
    public sealed class AlpineVillageBrookTests
    {
        private static AlpineVillageBrookPlan Brook(out AlpineVillagePlan plan)
        {
            plan = AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
            Assert.That(
                plan.Brook,
                Is.Not.Null,
                "The planner must hand the village its water.");
            return plan.Brook;
        }

        /// <summary>
        /// The one thing a viewer cannot be talked out of. The surface is
        /// built as a running minimum precisely so this is true by
        /// construction; the test is here to catch the day someone decides to
        /// take the ground height straight from the trace instead.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Brook_OnlyEverDescends()
        {
            AlpineVillageBrookPlan brook = Brook(out _);
            IReadOnlyList<AlpineVillageBrookSample> samples = brook.Samples;

            Assert.That(samples.Count, Is.GreaterThan(20));
            for (int index = 1; index < samples.Count; index++)
            {
                float previous = samples[index - 1].Position.y;
                float current = samples[index].Position.y;
                Assert.That(
                    current,
                    Is.LessThan(previous),
                    $"Sample {index} of the brook stands at {current:0.###} " +
                    $"under a surface of {previous:0.###}: water running " +
                    "uphill.");
            }

            Assert.That(
                brook.OverflowLip.y,
                Is.GreaterThan(samples[samples.Count - 1].Position.y),
                "The whole brook must stand below the bowl it leaves.");
        }

        [Test]
        [Category("AlpineVillage")]
        public void Brook_RunsUnbrokenFromTheBowlToTheCablewayCut()
        {
            AlpineVillageBrookPlan brook = Brook(out AlpineVillagePlan plan);
            IReadOnlyList<AlpineVillageBrookSample> samples = brook.Samples;

            Assert.That(
                Vector2.Distance(
                    new Vector2(samples[0].Position.x, samples[0].Position.z),
                    new Vector2(brook.OverflowLip.x, brook.OverflowLip.z)),
                Is.LessThan(0.05f),
                "The brook must start at the bowl's own overflow lip.");

            float longest = 0f;
            for (int index = 1; index < samples.Count; index++)
            {
                longest = Mathf.Max(
                    longest,
                    Vector3.Distance(
                        samples[index - 1].Position,
                        samples[index].Position));
            }

            Assert.That(
                longest,
                Is.LessThan(AlpineVillageBrookPlanner.SampleStep * 1.6f),
                "A gap between samples is a gap in the water.");

            // It has to leave the bowl of the village by the only breach
            // there is, or it is a stream that stops in a field.
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            Vector3 outfall = brook.OutfallPoint;
            Vector3 offset = outfall - cableway.StationArea.Center;
            Vector3 across = Vector3.Cross(
                Vector3.up,
                cableway.LineForward).normalized;
            float lateral = Mathf.Abs(Vector3.Dot(offset, across));
            float along = Vector3.Dot(offset, cableway.LineForward);

            Assert.That(
                lateral,
                Is.LessThan(
                    AlpineVillageTerrainSampler.CablewayCutOuterHalfWidth),
                "The outfall must lie inside the cableway cut.");
            Assert.That(
                along,
                Is.GreaterThan(cableway.StationArea.Size.y * 0.5f),
                "The outfall must be past the station, not beside it.");
        }

        /// <summary>
        /// A brook through a doorstep is a defect nobody has to argue about,
        /// and a brook across the station pad would drown the one flat place
        /// in the village.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Brook_StaysOutOfPlotsAndTheStationPad()
        {
            AlpineVillageBrookPlan brook = Brook(out AlpineVillagePlan plan);

            for (int index = 0; index < brook.Samples.Count; index++)
            {
                AlpineVillageBrookSample sample = brook.Samples[index];
                var point = new Vector2(
                    sample.Position.x,
                    sample.Position.z);

                for (int other = 0; other < plan.Plots.Count; other++)
                {
                    AlpineVillagePlotDescriptor plot = plan.Plots[other];
                    if (plot.Kind == AlpineVillagePlotKind.Spring)
                    {
                        continue;
                    }

                    Assert.That(
                        AlpineVillageTerrainSampler.DistanceOutsidePlot(
                            plot,
                            point),
                        Is.GreaterThan(sample.HalfWidth),
                        $"The brook runs into '{plot.StableId}'.");
                }

                Assert.That(
                    AlpineVillageTerrainSampler.DistanceOutsideStation(
                        plan.Station,
                        point),
                    Is.GreaterThan(sample.HalfWidth),
                    "The brook runs across the station pad.");
            }
        }

        /// <summary>
        /// THE HERO MUST BE ABLE TO GET OUT AGAIN.
        ///
        /// `PlayerFactory` gives the controller a `0.28 m` step offset. A
        /// cascade or a channel deeper than that is a trench the player walks
        /// into and cannot leave - and §10g of the art bible is explicit that
        /// nothing in this village stops him except what he can see.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Channel_NeverTrapsTheHeroBelowHisStepOffset()
        {
            AlpineVillageBrookPlan brook = Brook(out AlpineVillagePlan plan);
            float step = PlayerFactory.StepOffset;

            for (int index = 0; index < brook.Cascades.Count; index++)
            {
                Assert.That(
                    brook.Cascades[index].Drop,
                    Is.LessThan(step),
                    "A cascade taller than the hero's step is a trap.");
            }

            // And the dished ground itself.
            //
            // MEASURED AS A STEP, NOT AS A DEPTH. The first version of this
            // compared the bed against the bank `2.6 m` away and failed at
            // `0.367 m` - which is a hillside crossing the section at eight
            // degrees, not a wall. A CharacterController does not care how
            // far below the far bank it stands; it cares whether the ground
            // in front of it rises faster than it can step or climb. So walk
            // the section in short spans and judge each one.
            const float Span = 0.25f;
            const float Reach =
                AlpineVillageTerrainSampler.BrookSwaleHalfWidth + 1f;
            float slopeCeiling = Mathf.Tan(
                PlayerFactory.SlopeLimitDegrees * Mathf.Deg2Rad);

            for (int index = 0; index < brook.Samples.Count; index += 3)
            {
                AlpineVillageBrookSample sample = brook.Samples[index];
                var centre = new Vector2(
                    sample.Position.x,
                    sample.Position.z);
                Vector2 across = new Vector2(
                    sample.Right.x,
                    sample.Right.z).normalized;

                for (int side = -1; side <= 1; side += 2)
                {
                    float previous = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        centre);
                    for (float out2 = Span; out2 <= Reach; out2 += Span)
                    {
                        float here = AlpineVillageTerrainSampler.SampleHeight(
                            plan,
                            centre + across * (side * out2));
                        float rise = here - previous;
                        previous = here;

                        Assert.That(
                            rise,
                            Is.LessThan(step),
                            $"Climbing out of the brook at " +
                            $"{sample.Distance:0.#} m means a " +
                            $"{rise:0.###} m step at {out2:0.##} m out.");
                        Assert.That(
                            rise / Span,
                            Is.LessThan(slopeCeiling),
                            $"The bank at {sample.Distance:0.#} m stands at " +
                            $"{Mathf.Atan(rise / Span) * Mathf.Rad2Deg:0.#} " +
                            "degrees; the hero's limit is 45.");
                    }
                }
            }
        }

        /// <summary>
        /// THE GROUND HAS TO ACTUALLY OPEN UNDER THE WATER.
        ///
        /// Everything else here would still pass if the terrain sampler's
        /// swale term were a no-op: the polyline would be a fine polyline and
        /// the water would be a sheet lying on an unbroken hillside, which is
        /// the exact "вода лежит поверх terrain" the brief called out. So this
        /// measures the ground the sampler really returns at the centreline
        /// against the surface the plan put there - it must be under it, and
        /// not so far under that the brook is running in a slot.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Water_SitsInGroundTheSamplerActuallyOpened()
        {
            AlpineVillageBrookPlan brook = Brook(out AlpineVillagePlan plan);
            float deepest = 0f;
            int dished = 0;

            for (int index = 0; index < brook.Samples.Count; index++)
            {
                AlpineVillageBrookSample sample = brook.Samples[index];
                var centre = new Vector2(
                    sample.Position.x,
                    sample.Position.z);
                float ground = AlpineVillageTerrainSampler.SampleHeight(
                    plan,
                    centre);
                float under = sample.Position.y - ground;

                Assert.That(
                    under,
                    Is.GreaterThan(-0.001f),
                    $"At {sample.Distance:0.#} m the ground stands " +
                    $"{-under:0.###} m ABOVE the water: the brook is buried.");
                Assert.That(
                    under,
                    Is.LessThan(AlpineVillageBrookPlanner.MaximumChannelCut),
                    $"At {sample.Distance:0.#} m the water hangs " +
                    $"{under:0.###} m over its own bed.");

                deepest = Mathf.Max(deepest, under);
                if (under > 0.02f)
                {
                    dished++;
                }
            }

            Assert.That(
                dished,
                Is.GreaterThan(brook.Samples.Count / 3),
                "Hardly any of the brook stands in an opened channel - the " +
                "swale term is doing nothing.");
            Assert.That(
                deepest,
                Is.GreaterThan(0.05f),
                "No part of the channel is cut at all.");
        }

        /// <summary>
        /// The swale has to be wider than the grid that has to draw it. The
        /// terrain is sampled every two metres; a hollow narrower than that
        /// is sampled straight across and the brook ends up lying on a flat.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void Swale_IsWiderThanTheTerrainCellThatDrawsIt()
        {
            Assert.That(
                AlpineVillageTerrainSampler.BrookSwaleHalfWidth,
                Is.GreaterThan(AlpineVillageTerrainSampler.TerrainCell),
                "A hollow this grid cannot resolve is not a hollow.");
        }

        /// <summary>
        /// The two stone catches sit `52 m` apart with `0.31 m` between them.
        /// That is a contour, not a gradient, and the plan says so out loud:
        /// they are level with each other because a spring line is level. If
        /// this ever reads like a stream's fall, someone has quietly turned
        /// the wet ground back into an aqueduct - which §10g forbids.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        public void SeepLine_HoldsAContourRatherThanAFall()
        {
            AlpineVillageBrookPlan brook = Brook(out _);
            IReadOnlyList<AlpineVillageBrookSample> seep = brook.SeepLine;

            Assert.That(seep.Count, Is.GreaterThan(4));
            float length = seep[seep.Count - 1].Distance;
            float fall = seep[0].Position.y -
                         seep[seep.Count - 1].Position.y;

            Assert.That(length, Is.GreaterThan(30f));
            Assert.That(
                fall,
                Is.GreaterThan(0f),
                "The chapel's basin must stand below the spring's own bowl.");
            Assert.That(
                fall / length,
                Is.LessThan(0.02f),
                $"The seep line falls {fall / length * 100f:0.##} % - that " +
                "is a channel, and this is meant to be wet ground.");
        }

        [Test]
        [Category("AlpineVillage")]
        public void Seeps_ArriveFromSeveralMouthsAtSeveralHeights()
        {
            AlpineVillageBrookPlan brook = Brook(out _);
            IReadOnlyList<AlpineVillageBrookSeep> seeps = brook.Seeps;

            Assert.That(
                seeps.Count,
                Is.GreaterThanOrEqualTo(3),
                "One tidy hole reads as a pipe, which is the whole thing " +
                "this is not allowed to look like.");

            float lowest = float.MaxValue;
            float highest = float.MinValue;
            for (int index = 0; index < seeps.Count; index++)
            {
                lowest = Mathf.Min(lowest, seeps[index].Mouth.y);
                highest = Mathf.Max(highest, seeps[index].Mouth.y);
                Assert.That(
                    seeps[index].Fall,
                    Is.GreaterThan(0f),
                    "A seep must stand above the water it feeds.");
            }

            Assert.That(
                highest - lowest,
                Is.GreaterThan(0.2f),
                "Mouths all at one height are a drilled row, not a hillside.");
        }

        [Test]
        [Category("AlpineVillage")]
        public void Planner_IsDeterministicForOneSeed()
        {
            AlpineVillagePlan first = AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);
            AlpineVillagePlan second = AlpineVillagePlanner.Create(
                GameSessionState.DefaultCitySeed);

            Assert.That(
                second.Brook.Samples.Count,
                Is.EqualTo(first.Brook.Samples.Count));
            for (int index = 0; index < first.Brook.Samples.Count; index++)
            {
                // Compared as a DISTANCE: Is.EqualTo on a Vector3 is bitwise
                // in NUnit and prints two identical numbers when it fails.
                Assert.That(
                    Vector3.Distance(
                        first.Brook.Samples[index].Position,
                        second.Brook.Samples[index].Position),
                    Is.LessThan(0.0001f));
            }
        }

        /// <summary>
        /// Not a test. Prints the traced route so a human can see where the
        /// water actually goes before anything is built on top of it.
        /// </summary>
        [Test]
        [Category("AlpineVillage")]
        [Explicit("Diagnostic print, not an assertion.")]
        public void Report_TheRouteTheWaterTakes()
        {
            AlpineVillageBrookPlan brook = Brook(out AlpineVillagePlan plan);
            var report = new StringBuilder();
            report.AppendLine(
                $"bowl {brook.BowlCenter} waterTop {brook.BowlWaterTopY:0.##}");
            report.AppendLine($"lip {brook.OverflowLip}");
            report.AppendLine(
                $"outfall {brook.OutfallPoint} length {brook.Length:0.#} m");
            report.AppendLine(
                $"cascades {brook.Cascades.Count} seeps {brook.Seeps.Count}");
            report.AppendLine(
                $"station pad {plan.Station.PadArea.Center} " +
                $"size {plan.Station.PadArea.Size}");
            report.AppendLine($"terrain bounds {plan.TerrainBounds}");

            for (int index = 0; index < brook.Samples.Count; index += 4)
            {
                AlpineVillageBrookSample sample = brook.Samples[index];
                report.AppendLine(
                    $"  {sample.Distance,6:0.#}  " +
                    $"({sample.Position.x,7:0.#},{sample.Position.y,7:0.##}," +
                    $"{sample.Position.z,7:0.#})  w{sample.Width:0.##}  " +
                    $"{sample.Reach}");
            }

            // Which lateral step is the worst, and what is standing there.
            float worst = 0f;
            AlpineVillageBrookSample worstSample = brook.Samples[0];
            float worstOut = 0f;
            int worstSide = 1;
            for (int index = 0; index < brook.Samples.Count; index++)
            {
                AlpineVillageBrookSample sample = brook.Samples[index];
                var centre = new Vector2(
                    sample.Position.x,
                    sample.Position.z);
                Vector2 across = new Vector2(
                    sample.Right.x,
                    sample.Right.z).normalized;
                for (int side = -1; side <= 1; side += 2)
                {
                    float previous = AlpineVillageTerrainSampler.SampleHeight(
                        plan,
                        centre);
                    for (float out2 = 0.25f; out2 <= 3.6f; out2 += 0.25f)
                    {
                        float here = AlpineVillageTerrainSampler.SampleHeight(
                            plan,
                            centre + across * (side * out2));
                        if (here - previous > worst)
                        {
                            worst = here - previous;
                            worstSample = sample;
                            worstOut = out2;
                            worstSide = side;
                        }

                        previous = here;
                    }
                }
            }

            report.AppendLine(
                $"worst lateral step {worst:0.###} m at " +
                $"{worstSample.Distance:0.#} m, {worstOut:0.##} m out, " +
                $"side {worstSide}");

            var worstCentre = new Vector2(
                worstSample.Position.x,
                worstSample.Position.z);
            Vector2 worstAcross = new Vector2(
                worstSample.Right.x,
                worstSample.Right.z).normalized;
            Vector2 spot = worstCentre + worstAcross * (worstSide * worstOut);
            report.AppendLine($"  at {spot}");
            report.AppendLine(
                $"  ridge rise {AlpineVillageTerrainSampler.SampleRidgeRise(plan, spot):0.###}");
            report.AppendLine(
                $"  station outside " +
                $"{AlpineVillageTerrainSampler.DistanceOutsideStation(plan.Station, spot):0.##}");
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                float outside = AlpineVillageTerrainSampler
                    .DistanceOutsidePlot(plan.Plots[index], spot);
                if (outside < 6f)
                {
                    report.AppendLine(
                        $"  plot {plan.Plots[index].StableId} outside " +
                        $"{outside:0.##} groundY " +
                        $"{plan.Plots[index].GroundCenter.y:0.##}");
                }
            }

            Debug.Log(report.ToString());
        }
    }
}
