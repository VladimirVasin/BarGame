using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityLighthouseIslandTests
    {
        private static CityLayout CreateDefaultLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        private static CityLighthouseIslandPlan CreateDefaultPlan(
            CityLayout layout,
            out CitySeacoastPlan seacoastPlan)
        {
            seacoastPlan = CitySeacoastPlanner.Create(layout);
            return CityLighthouseIslandPlanner.Create(
                layout.Seed,
                seacoastPlan);
        }

        [Test]
        public void DefaultCity_PlansDeterministicIsland()
        {
            CityLayout layout = CreateDefaultLayout();

            CityLighthouseIslandPlan first = CreateDefaultPlan(
                layout,
                out CitySeacoastPlan seacoastPlan);
            CityLighthouseIslandPlan second =
                CityLighthouseIslandPlanner.Create(
                    layout.Seed,
                    seacoastPlan);

            Assert.That(first, Is.Not.Null);
            Assert.That(second, Is.Not.Null);
            CollectionAssert.AreEqual(first.Parts, second.Parts);
            Assert.That(
                first.Count,
                Is.LessThanOrEqualTo(
                    CityLighthouseIslandPlan.MaximumPartCount));

            // The silhouette the concept is built on: a rock mound,
            // ruined shacks, the scatter of a working shore nobody
            // works, and the lighthouse over all of it.
            Assert.That(
                first.GetCount(CityLighthouseIslandPartKind.Rock),
                Is.GreaterThanOrEqualTo(10));
            Assert.That(
                first.GetCount(
                    CityLighthouseIslandPartKind.ShackWall),
                Is.GreaterThanOrEqualTo(6));
            Assert.That(
                first.GetCount(
                    CityLighthouseIslandPartKind.ShackRoof),
                Is.EqualTo(2));
            Assert.That(
                first.GetCount(CityLighthouseIslandPartKind.Timber),
                Is.GreaterThanOrEqualTo(6));
            Assert.That(
                first.GetCount(CityLighthouseIslandPartKind.Wreck),
                Is.GreaterThanOrEqualTo(4));
            Assert.That(
                first.GetCount(
                    CityLighthouseIslandPartKind.TowerShaft),
                Is.GreaterThanOrEqualTo(4));
            Assert.That(
                first.GetCount(
                    CityLighthouseIslandPartKind.LampRoom),
                Is.EqualTo(1));

            // "Much bigger" is a contract: the lantern stands at
            // least 12 m over the sea where the old mol beacon's
            // lens sat at ~5.
            CitySeacoastFrame frame = seacoastPlan.Frame;
            Assert.That(
                first.LanternPosition.y - frame.SeaTopY,
                Is.GreaterThanOrEqualTo(12f));

            // Offshore band: past the waterline far enough to sit at
            // the edge of visibility, inside the sheeted sea so
            // there is always water under the rocks — the apron
            // carries the water, so the bound derives from it.
            Assert.That(
                first.IslandCenter.z,
                Is.GreaterThan(frame.WaterlineZ + 24f));
            Assert.That(
                first.IslandCenter.z,
                Is.LessThan(
                    frame.SeaRowBounds.yMax +
                    CitySeacoastSeaLayout.ApronReach - 4f));

            // The map's marker derives the same lantern spot without
            // building the island.
            Assert.That(
                CityLighthouseIslandPlanner.TryResolveLanternPosition(
                    seacoastPlan,
                    out Vector3 lantern),
                Is.True);
            Assert.That(
                lantern,
                Is.EqualTo(first.LanternPosition));
        }

        [Test]
        public void Island_StandsOffshoreClearOfTheDecks()
        {
            CityLayout layout = CreateDefaultLayout();
            CityLighthouseIslandPlan plan = CreateDefaultPlan(
                layout,
                out CitySeacoastPlan seacoastPlan);
            CitySeacoastFrame frame = seacoastPlan.Frame;

            var walkable = new List<Rect>();
            CitySeacoastPlanner.AppendWalkableFootprints(
                layout,
                walkable);

            for (int index = 0; index < plan.Parts.Count; index++)
            {
                CityLighthouseIslandPartDescriptor part =
                    plan.Parts[index];
                float reach = Mathf.Max(
                    part.Size.x,
                    part.Size.z) * 0.5f;
                var footprint = Rect.MinMaxRect(
                    part.Center.x - reach,
                    part.Center.z - reach,
                    part.Center.x + reach,
                    part.Center.z + reach);

                // Clear of the mol's 16 m reach with margin, so no
                // deck a person walks ever touches the island.
                Assert.That(
                    footprint.yMin,
                    Is.GreaterThan(frame.WaterlineZ + 16f),
                    $"Part '{part.StableId}' crowds the shore.");

                for (int walkableIndex = 0;
                     walkableIndex < walkable.Count;
                     walkableIndex++)
                {
                    Assert.That(
                        footprint.Overlaps(walkable[walkableIndex]),
                        Is.False,
                        $"Part '{part.StableId}' overlaps a " +
                        "walkable deck.");
                }
            }
        }

        [Test]
        public void Lantern_RotatesUniformlyAndDeterministically()
        {
            const int seed = 40213;
            const double start = 1234.5;

            float reference =
                CityLighthouseLanternRules.BeamAzimuthDegreesAt(
                    seed,
                    start);
            Assert.That(
                CityLighthouseLanternRules.BeamAzimuthDegreesAt(
                    seed,
                    start),
                Is.EqualTo(reference));
            Assert.That(reference, Is.InRange(0f, 360f));

            // Uniform: any interval advances the beam by exactly its
            // rate, modulo the circle.
            foreach (double delta in new[] { 0.25, 3.0, 41.0, 400.0 })
            {
                float advanced =
                    CityLighthouseLanternRules.BeamAzimuthDegreesAt(
                        seed,
                        start + delta);
                float expected = Mathf.Repeat(
                    reference +
                    (float)(delta *
                            CityLighthouseLanternRules
                                .RotationDegreesPerMinute),
                    360f);
                Assert.That(
                    Mathf.Abs(Mathf.DeltaAngle(advanced, expected)),
                    Is.LessThan(0.001f),
                    $"Non-uniform rotation over {delta} minutes.");
            }

            // The seed only turns the phase: two cities sweep at the
            // same rate but not in unison.
            float otherSeed =
                CityLighthouseLanternRules.BeamAzimuthDegreesAt(
                    seed + 1,
                    start);
            Assert.That(
                Mathf.Abs(Mathf.DeltaAngle(reference, otherSeed)),
                Is.GreaterThan(0.5f));

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CityLighthouseLanternRules.BeamAzimuthDegreesAt(
                    seed,
                    double.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                CityLighthouseLanternRules.BeamAzimuthDegreesAt(
                    seed,
                    double.PositiveInfinity));
        }

        [Test]
        public void Lantern_FlashesTwicePerRevolution()
        {
            const int seed = 40213;
            const float observerAzimuth = 57f;
            double period =
                360d /
                CityLighthouseLanternRules.RotationDegreesPerMinute;
            const double step = 0.02;
            int samples = (int)Math.Round(period / step);

            int peaks = 0;
            bool sawFull = false;
            float previous = CityLighthouseLanternRules.FlashFactorAt(
                seed,
                -step,
                observerAzimuth);
            float current = CityLighthouseLanternRules.FlashFactorAt(
                seed,
                0d,
                observerAzimuth);
            for (int index = 0; index < samples; index++)
            {
                float next = CityLighthouseLanternRules.FlashFactorAt(
                    seed,
                    (index + 1) * step,
                    observerAzimuth);

                Assert.That(current, Is.InRange(0f, 1f));
                if (current >= 0.999f)
                {
                    sawFull = true;
                }

                if (current > previous && current >= next &&
                    current > 0.9f)
                {
                    peaks++;
                }

                previous = current;
                current = next;
            }

            // Two opposed beams: the fixed observer catches a flash
            // exactly twice per revolution, straight in the eyes at
            // the crest.
            Assert.That(peaks, Is.EqualTo(2));
            Assert.That(sawFull, Is.True);

            // Outside the flash half-width there is darkness, not a
            // dim tail.
            float offBeam = CityLighthouseLanternRules.FlashFactorAt(
                seed,
                0d,
                CityLighthouseLanternRules.BeamAzimuthDegreesAt(
                    seed,
                    0d) +
                CityLighthouseLanternRules.FlashHalfWidthDegrees +
                5f);
            Assert.That(offBeam, Is.EqualTo(0f));
        }

        [Test]
        public void NoSeacoast_PlansNoIsland()
        {
            // The legacy layouts without a dressed shore keep their
            // empty horizon: no coast plan, no island.
            Assert.That(
                CityLighthouseIslandPlanner.Create(
                    GameSessionState.DefaultCitySeed,
                    null),
                Is.Null);
            Assert.That(
                CityLighthouseIslandPlanner.TryResolveLanternPosition(
                    null,
                    out _),
                Is.False);
        }

        [Test]
        public void MeshFactory_BakesOneVertexColouredMesh()
        {
            CityLayout layout = CreateDefaultLayout();
            CityLighthouseIslandPlan plan = CreateDefaultPlan(
                layout,
                out _);

            var colors = new Color[plan.Parts.Count];
            for (int index = 0; index < colors.Length; index++)
            {
                colors[index] = Color.white;
            }

            Mesh mesh = null;
            try
            {
                mesh = CityLighthouseIslandMeshFactory
                    .CreateIslandMesh(
                        "Island Test Mesh",
                        plan.Parts,
                        colors);

                // Four vertices per face keep the baked face shades
                // apart: 24 per box, colours one to one.
                Assert.That(
                    mesh.vertexCount,
                    Is.EqualTo(plan.Parts.Count * 24));
                Assert.That(
                    mesh.colors.Length,
                    Is.EqualTo(mesh.vertexCount));
                Assert.That(
                    mesh.triangles.Length,
                    Is.EqualTo(plan.Parts.Count * 36));

                // The whole island is in there, lamp room to rocks.
                Bounds bounds = mesh.bounds;
                Assert.That(
                    bounds.max.y,
                    Is.GreaterThan(
                        plan.LanternPosition.y - 1.5f));
                Assert.That(
                    bounds.min.y,
                    Is.LessThan(plan.IslandCenter.y));
                Assert.That(
                    bounds.Contains(new Vector3(
                        plan.IslandCenter.x,
                        plan.IslandCenter.y + 0.5f,
                        plan.IslandCenter.z)),
                    Is.True);
            }
            finally
            {
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
        }
    }
}
