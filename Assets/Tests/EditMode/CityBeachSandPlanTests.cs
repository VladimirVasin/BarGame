using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// Proves the closed-form gradient of the loose-sand depth field
    /// against a central difference of the field itself, everywhere on
    /// the default city's beach: the plateau where the envelope is one,
    /// both SmoothStep ramps, and the compacted surf band where it is
    /// zero.
    /// </summary>
    public sealed class CityBeachSandPlanTests
    {
        // Central difference over h: truncation error h²/6·f''' (about
        // 1e-6 for the 1.31 rad/m grain at 0.075 m) and, across the
        // SmoothStep clamp edges where f'' jumps by up to 6/2²·0.1,
        // h/4·Δf'' ≈ 2e-4. Float rounding of the sine phases at |x| of
        // some hundred metres adds about 1e-4 in slope. The tolerance
        // sits three times above the sum; a wrong sign, factor or
        // missing chain-rule term is off by whole units of slope.
        private const float Step = 0.005f;
        private const float SlopeTolerance = 1e-3f;

        [Test]
        public void LooseDepthGradient_MatchesCentralDifferenceAcrossTheBeach()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            int sampled = 0;
            int loose = 0;
            for (int surfaceIndex = 0;
                 surfaceIndex < layout.Surfaces.Count;
                 surfaceIndex++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[surfaceIndex];
                if (surface.Kind != CitySurfaceKind.Beach ||
                    surface.Feature != CityAreaFeatureKind.NorthWaterfront)
                {
                    continue;
                }

                var field = new CityBeachSandPlan.Field(
                    layout.ElevationPlan,
                    surface);
                Rect bounds = surface.WorldBounds;
                // Prime pitches, so the grid never rides a ramp edge or a
                // sine node and the 0.40 m mesh pitch is not what is proved.
                for (float z = bounds.yMin + 0.05f;
                     z <= bounds.yMax - 0.05f;
                     z += 0.37f)
                {
                    for (float x = bounds.xMin + 0.05f;
                         x <= bounds.xMax - 0.05f;
                         x += 0.53f)
                    {
                        var point = new Vector2(x, z);
                        Vector2 analytic =
                            CityBeachSandPlan.SampleLooseDepthGradient(
                                in field,
                                point);
                        Vector2 numeric = CentralDifference(in field, point);
                        Assert.That(
                            analytic.x,
                            Is.EqualTo(numeric.x).Within(SlopeTolerance),
                            $"dDepth/dx at {point}");
                        Assert.That(
                            analytic.y,
                            Is.EqualTo(numeric.y).Within(SlopeTolerance),
                            $"dDepth/dz at {point}");
                        sampled++;
                        if (field.SampleLooseDepth(point) > 0f)
                        {
                            loose++;
                        }
                    }
                }
            }

            Assert.That(sampled, Is.GreaterThan(1000),
                "The default city must expose a north-waterfront beach.");
            Assert.That(loose, Is.GreaterThan(sampled / 10),
                "The grid must cross the loose band, not only the surf.");
        }

        [Test]
        public void LooseDepthGradient_IsZeroWhereTheDepthIs()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            int checkedSurfaces = 0;
            for (int surfaceIndex = 0;
                 surfaceIndex < layout.Surfaces.Count;
                 surfaceIndex++)
            {
                CitySurfaceDescriptor surface = layout.Surfaces[surfaceIndex];
                if (surface.Kind == CitySurfaceKind.Beach &&
                    surface.Feature == CityAreaFeatureKind.NorthWaterfront)
                {
                    continue;
                }

                var field = new CityBeachSandPlan.Field(
                    layout.ElevationPlan,
                    surface);
                Vector2 centre = surface.WorldBounds.center;
                Assert.That(field.SampleLooseDepth(centre), Is.EqualTo(0f));
                Assert.That(
                    CityBeachSandPlan.SampleLooseDepthGradient(in field, centre),
                    Is.EqualTo(Vector2.zero));
                checkedSurfaces++;
            }

            Assert.That(checkedSurfaces, Is.GreaterThan(0));
        }

        private static Vector2 CentralDifference(
            in CityBeachSandPlan.Field field,
            Vector2 point)
        {
            Vector2 east = point + Vector2.right * Step;
            Vector2 west = point - Vector2.right * Step;
            Vector2 north = point + Vector2.up * Step;
            Vector2 south = point - Vector2.up * Step;
            // Divide by the step the floats actually took, not the one
            // asked for: at a few hundred metres the coordinate's ulp is
            // a visible fraction of five millimetres.
            return new Vector2(
                (field.SampleLooseDepth(east) - field.SampleLooseDepth(west)) /
                (east.x - west.x),
                (field.SampleLooseDepth(north) - field.SampleLooseDepth(south)) /
                (north.y - south.y));
        }
    }
}
