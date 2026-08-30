using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CemeteryRavenPlanTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        private readonly List<GameObject> spawned =
            new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < spawned.Count; index++)
            {
                if (spawned[index] != null)
                {
                    Object.DestroyImmediate(spawned[index]);
                }
            }

            spawned.Clear();
        }

        [Test]
        public void GroundPerch_IsVacantClearInBandAndFacesTheFirstGrave()
        {
            (CityCemeteryPlan cemetery,
             CemeteryGravediggingPlan grave) = CreatePlans();
            Vector3 crown = CityCemeterySealedGraveWorldBuilder
                .GetMoundCrownPoint(grave);

            CemeteryRavenPerch perch =
                CemeteryRavenPlan.SelectGroundPerch(
                    cemetery,
                    grave,
                    new List<string>(),
                    null);

            Assert.That(perch.IsPresent, Is.True,
                "The default yard must seat the ground bird.");
            CityCemeteryPlotDescriptor plot =
                FindPlot(cemetery, perch.PlotId);
            Assert.That(plot.IsVacant, Is.True);
            Assert.That(
                perch.PlotId,
                Is.Not.EqualTo(grave.Plot.StableId),
                "The bird never stands on the grave it flanks.");
            Assert.That(
                perch.Position.y,
                Is.EqualTo(cemetery.GroundTopY).Within(0.001f));
            Assert.That(
                perch.Position.x,
                Is.EqualTo(plot.Ground.x).Within(0.001f));
            Assert.That(
                perch.Position.z,
                Is.EqualTo(plot.Ground.z).Within(0.001f));

            // The band is honoured whenever the yard offers it: the
            // test recomputes the candidate set rather than trusting
            // the selector's own opinion of what was available.
            float distance = PlanarDistance(perch.Position, crown);
            if (AnyVacantInBand(cemetery, grave, crown))
            {
                Assert.That(
                    distance,
                    Is.InRange(
                        CemeteryRavenPlan
                            .GroundPerchBandMinimumMeters,
                        CemeteryRavenPlan
                            .GroundPerchBandMaximumMeters));
            }

            // It faces the grave: the user's rule.
            Vector3 forward =
                Quaternion.Euler(0f, perch.YawDegrees, 0f) *
                Vector3.forward;
            Vector3 toCrown = crown - perch.Position;
            toCrown.y = 0f;
            Assert.That(
                Vector3.Dot(forward, toCrown.normalized),
                Is.GreaterThan(0.99f));

            // Same inputs, same perch — the determinism the director
            // leans on across city rebuilds.
            CemeteryRavenPerch again =
                CemeteryRavenPlan.SelectGroundPerch(
                    cemetery,
                    grave,
                    new List<string>(),
                    null);
            Assert.That(again.PlotId, Is.EqualTo(perch.PlotId));
            Assert.That(again.Position, Is.EqualTo(perch.Position));
            Assert.That(
                again.YawDegrees,
                Is.EqualTo(perch.YawDegrees));
        }

        [Test]
        public void GroundPerch_ReselectsDeterministicallyWhenThePlotIsTaken()
        {
            (CityCemeteryPlan cemetery,
             CemeteryGravediggingPlan grave) = CreatePlans();

            CemeteryRavenPerch first =
                CemeteryRavenPlan.SelectGroundPerch(
                    cemetery,
                    grave,
                    new List<string>(),
                    null);
            Assert.That(first.IsPresent, Is.True);

            // The chosen plot enters the ledger: a chalk mark is no
            // ground for a bird, so the selection moves — and moves
            // the same way twice.
            var taken = new List<string> { first.PlotId };
            CemeteryRavenPerch second =
                CemeteryRavenPlan.SelectGroundPerch(
                    cemetery,
                    grave,
                    taken,
                    null);
            Assert.That(second.IsPresent, Is.True);
            Assert.That(
                second.PlotId,
                Is.Not.EqualTo(first.PlotId));
            Assert.That(
                FindPlot(cemetery, second.PlotId).IsVacant,
                Is.True);

            CemeteryRavenPerch secondAgain =
                CemeteryRavenPlan.SelectGroundPerch(
                    cemetery,
                    grave,
                    taken,
                    null);
            Assert.That(
                secondAgain.PlotId,
                Is.EqualTo(second.PlotId));
            Assert.That(
                secondAgain.Position,
                Is.EqualTo(second.Position));
        }

        [Test]
        public void GroundPerch_ExcludesPlotsUnderOpenJobRestPoints()
        {
            (CityCemeteryPlan cemetery,
             CemeteryGravediggingPlan grave) = CreatePlans();

            CemeteryRavenPerch first =
                CemeteryRavenPlan.SelectGroundPerch(
                    cemetery,
                    grave,
                    new List<string>(),
                    null);
            Assert.That(first.IsPresent, Is.True);

            // A neighbouring job's coffin legally rests PAST its own
            // plot's edge — the pinned case: a rest point landing on
            // the chosen plot's footprint must displace the bird even
            // though the plot itself is still vacant and unledgered.
            var restPoints = new List<Vector3>
            {
                new Vector3(
                    first.Position.x,
                    cemetery.GroundTopY,
                    first.Position.z)
            };
            CemeteryRavenPerch displaced =
                CemeteryRavenPlan.SelectGroundPerch(
                    cemetery,
                    grave,
                    new List<string>(),
                    restPoints);

            Assert.That(displaced.IsPresent, Is.True);
            Assert.That(
                displaced.PlotId,
                Is.Not.EqualTo(first.PlotId));
            Rect footprint =
                FindPlot(cemetery, displaced.PlotId).Footprint;
            for (int index = 0; index < restPoints.Count; index++)
            {
                Assert.That(
                    footprint.Contains(new Vector2(
                        restPoints[index].x,
                        restPoints[index].z)),
                    Is.False,
                    "No rest point may lie on the chosen ground.");
            }

            // A rest point in the open, off every vacant footprint,
            // displaces nothing.
            CemeteryRavenPerch untouched =
                CemeteryRavenPlan.SelectGroundPerch(
                    cemetery,
                    grave,
                    new List<string>(),
                    new List<Vector3>
                    {
                        new Vector3(9999f, 0f, 9999f)
                    });
            Assert.That(
                untouched.PlotId,
                Is.EqualTo(first.PlotId));
        }

        [Test]
        public void MoundPerch_SitsOnTheCrownFacingTheFoot()
        {
            (CityCemeteryPlan _,
             CemeteryGravediggingPlan grave) = CreatePlans();

            CemeteryRavenPerch perch =
                CemeteryRavenPlan.CreateMoundPerch(grave);
            Assert.That(perch.IsPresent, Is.True);
            Assert.That(
                perch.PlotId,
                Is.EqualTo(grave.Plot.StableId));
            Assert.That(
                perch.Position,
                Is.EqualTo(
                    CityCemeterySealedGraveWorldBuilder
                        .GetMoundCrownPoint(grave)));

            // Facing down the plot toward the foot: the exact
            // opposite of the plan's headward axis, so the monument
            // stands behind the bird rather than merged into it.
            Vector3 forward =
                Quaternion.Euler(0f, perch.YawDegrees, 0f) *
                Vector3.forward;
            Vector3 headward = grave.Heading * Vector3.forward;
            Assert.That(
                Vector3.Dot(forward, -headward),
                Is.GreaterThan(0.999f));
        }

        [Test]
        public void MoundCrownPoint_MatchesTheBuiltMoundTop()
        {
            (CityCemeteryPlan _,
             CemeteryGravediggingPlan grave) = CreatePlans();
            Vector3 crown = CityCemeterySealedGraveWorldBuilder
                .GetMoundCrownPoint(grave);

            // Build the real mound and measure it: the accessor is
            // pinned to the geometry forever, so a course-table edit
            // that moves the heap without moving the perch fails
            // here, not in a screenshot.
            var host = new GameObject("Test Raven Mound");
            spawned.Add(host);
            GameObject mound =
                CityCemeterySealedGraveWorldBuilder.BuildMound(
                    host.transform,
                    grave);
            Assert.That(mound, Is.Not.Null);

            Renderer[] renderers =
                mound.GetComponentsInChildren<Renderer>();
            Assert.That(renderers.Length, Is.GreaterThan(0));
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            Assert.That(
                crown.y,
                Is.EqualTo(bounds.max.y).Within(0.001f),
                "The perch height is the built heap's own top.");

            // And the crown sits over the top course, not out past
            // its rim: gather the mesh's own summit vertices and ask
            // whether the point lies inside their planar hull box.
            MeshFilter filter =
                mound.GetComponentInChildren<MeshFilter>();
            Assert.That(filter, Is.Not.Null);
            Mesh mesh = filter.sharedMesh;
            Matrix4x4 toWorld =
                filter.transform.localToWorldMatrix;
            Vector3[] vertices = mesh.vertices;
            float minX = float.MaxValue;
            float maxX = float.MinValue;
            float minZ = float.MaxValue;
            float maxZ = float.MinValue;
            int summitCount = 0;
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 world =
                    toWorld.MultiplyPoint3x4(vertices[index]);
                if (world.y < bounds.max.y - 0.001f)
                {
                    continue;
                }

                summitCount++;
                minX = Mathf.Min(minX, world.x);
                maxX = Mathf.Max(maxX, world.x);
                minZ = Mathf.Min(minZ, world.z);
                maxZ = Mathf.Max(maxZ, world.z);
            }

            Assert.That(summitCount, Is.GreaterThanOrEqualTo(4));
            Assert.That(crown.x, Is.InRange(minX, maxX));
            Assert.That(crown.z, Is.InRange(minZ, maxZ));
        }

        [Test]
        public void Seeds_SplitThePairDeterministically()
        {
            (CityCemeteryPlan _,
             CemeteryGravediggingPlan grave) = CreatePlans();
            string plotId = grave.Plot.StableId;

            int seedA = CemeteryRavenPlan.DeriveRavenSeed(
                Seed,
                plotId,
                CemeteryRavenDirectorModel.RavenAIndex);
            int seedB = CemeteryRavenPlan.DeriveRavenSeed(
                Seed,
                plotId,
                CemeteryRavenDirectorModel.RavenBIndex);
            Assert.That(seedA, Is.Not.EqualTo(seedB));
            Assert.That(
                CemeteryRavenPlan.DeriveRavenSeed(
                    Seed,
                    plotId,
                    CemeteryRavenDirectorModel.RavenAIndex),
                Is.EqualTo(seedA));

            double offsetA =
                CemeteryRavenPlan.DeriveIdleStartOffsetSeconds(
                    seedA);
            double offsetB =
                CemeteryRavenPlan.DeriveIdleStartOffsetSeconds(
                    seedB);
            Assert.That(
                offsetA,
                Is.InRange(
                    0d,
                    (double)CemeteryRavenPlan
                        .MaximumIdleStartOffsetSeconds));
            Assert.That(
                offsetB,
                Is.InRange(
                    0d,
                    (double)CemeteryRavenPlan
                        .MaximumIdleStartOffsetSeconds));
            Assert.That(offsetA, Is.Not.EqualTo(offsetB),
                "A pair breathing in unison reads as one animation " +
                "played twice.");

            Assert.Throws<System.ArgumentOutOfRangeException>(() =>
                CemeteryRavenPlan.DeriveRavenSeed(Seed, plotId, 2));
        }

        private static (CityCemeteryPlan, CemeteryGravediggingPlan)
            CreatePlans()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CityCemeteryPlan cemetery =
                CityCemeteryPlanner.Create(layout);
            Assert.That(cemetery, Is.Not.Null,
                "The default city must carry a dressable cemetery.");
            CemeteryWatchmanPlan watchman =
                CemeteryWatchmanPlan.Create(cemetery);
            CemeteryGravediggingPlan grave =
                CemeteryGravediggingPlan.Create(cemetery, watchman);
            Assert.That(grave.IsPresent, Is.True);
            return (cemetery, grave);
        }

        private static CityCemeteryPlotDescriptor FindPlot(
            CityCemeteryPlan cemetery,
            string stableId)
        {
            for (int index = 0;
                 index < cemetery.Plots.Count;
                 index++)
            {
                if (cemetery.Plots[index].StableId == stableId)
                {
                    return cemetery.Plots[index];
                }
            }

            Assert.Fail(
                "The perch names a plot the plan does not know: " +
                stableId);
            return default;
        }

        private static bool AnyVacantInBand(
            CityCemeteryPlan cemetery,
            CemeteryGravediggingPlan grave,
            Vector3 crown)
        {
            for (int index = 0;
                 index < cemetery.Plots.Count;
                 index++)
            {
                CityCemeteryPlotDescriptor plot =
                    cemetery.Plots[index];
                if (!plot.IsVacant ||
                    plot.StableId == grave.Plot.StableId)
                {
                    continue;
                }

                float distance = PlanarDistance(plot.Ground, crown);
                if (distance >=
                    CemeteryRavenPlan.GroundPerchBandMinimumMeters &&
                    distance <=
                    CemeteryRavenPlan.GroundPerchBandMaximumMeters)
                {
                    return true;
                }
            }

            return false;
        }

        private static float PlanarDistance(
            Vector3 left,
            Vector3 right)
        {
            return new Vector2(
                left.x - right.x,
                left.z - right.z).magnitude;
        }
    }
}
