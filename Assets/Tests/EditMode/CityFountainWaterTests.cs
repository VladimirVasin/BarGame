using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityFountainWaterTests
    {
        private static CityLayout CreateDefaultLayout()
        {
            return CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
        }

        private static CityDecorationPlan CreatePlan(CityLayout layout)
        {
            return CityDecorationPlanner.CreatePlan(
                layout,
                RoadFencePlanner.CreatePlan(layout),
                CityNightFixturePlanner.CreatePlan(layout));
        }

        [Test]
        public void Build_StandsWaterInEveryFountain()
        {
            CityLayout layout = CreateDefaultLayout();
            CityDecorationPlan plan = CreatePlan(layout);

            var fountains = new List<CityDecorationDescriptor>();
            foreach (CityDecorationDescriptor descriptor
                     in plan.Descriptors)
            {
                if (descriptor.Kind ==
                    CityDecorationKind.ParkFountainAndStatue)
                {
                    fountains.Add(descriptor);
                }
            }

            Assert.That(
                fountains,
                Is.Not.Empty,
                "The default city lost its park fountain.");

            var parent = new GameObject("Fountain Water Test");
            try
            {
                GameObject root = CityFountainWaterBuilder.Build(
                    parent.transform,
                    layout,
                    plan);
                Assert.That(root, Is.Not.Null);

                // One basin sheet, two streams and two splash rings
                // per fountain, plus the one shared reflection
                // mirror, nothing else.
                Assert.That(
                    root.transform.childCount,
                    Is.EqualTo(fountains.Count * 5 + 1));

                // The Morrowind mirror: one controller, bound to the
                // shared basin material, camera prepared but never
                // enabled - Update re-renders it as the night turns.
                var mirror = root.GetComponentInChildren<
                    CityFountainReflectionController>();
                Assert.That(mirror, Is.Not.Null);
                var mirrorCamera =
                    mirror.GetComponentInChildren<Camera>();
                Assert.That(mirrorCamera, Is.Not.Null);
                Assert.That(mirrorCamera.enabled, Is.False);
                Assert.That(
                    CityFountainWaterResources.BasinMaterial
                        .GetTexture("_ReflectionCube"),
                    Is.Not.Null,
                    "The basin material lost its mirror cube.");

                int basins = 0;
                int streams = 0;
                int splashes = 0;
                foreach (Transform child in root.transform)
                {
                    if (child.GetComponent<
                            CityFountainReflectionController>() !=
                        null)
                    {
                        continue;
                    }

                    var renderer =
                        child.GetComponent<MeshRenderer>();
                    Assert.That(renderer, Is.Not.Null, child.name);
                    Assert.That(
                        child.GetComponent<Collider>(),
                        Is.Null,
                        $"{child.name} must stay presentation-only.");
                    if (renderer.sharedMaterial ==
                        CityFountainWaterResources.BasinMaterial)
                    {
                        basins++;
                    }
                    else if (renderer.sharedMaterial ==
                             CityFountainWaterResources.StreamMaterial)
                    {
                        streams++;
                    }
                    else if (renderer.sharedMaterial ==
                             CityFountainWaterResources.SplashMaterial)
                    {
                        splashes++;
                    }
                }

                Assert.That(basins, Is.EqualTo(fountains.Count));
                Assert.That(streams, Is.EqualTo(fountains.Count * 2));
                Assert.That(splashes, Is.EqualTo(fountains.Count * 2));

                // The water stands in the batched fountain's own
                // frame: sheet at the basin's level inside the rim,
                // streams from the spout arms down past the surface.
                CityDecorationWorldBuilder.GetDecorationFrame(
                    layout,
                    fountains[0],
                    out Vector3 origin,
                    out Vector3 tangent,
                    out _);
                float top = origin.y +
                            CityFountainWaterBuilder.BasinWaterTopY;

                Transform basin = root.transform.Find(
                    "Fountain Basin Water 0");
                Assert.That(basin, Is.Not.Null);
                Assert.That(
                    basin.position.y,
                    Is.EqualTo(top).Within(0.001f));
                Bounds basinBounds =
                    basin.GetComponent<MeshFilter>().sharedMesh.bounds;
                Assert.That(
                    basinBounds.extents.x,
                    Is.EqualTo(CityFountainWaterBuilder.BasinWaterHalf)
                        .Within(0.01f));
                Assert.That(
                    basinBounds.extents.x,
                    Is.LessThan(3.20f - 0.40f),
                    "The sheet must stay inside the basin rim.");

                for (int side = -1; side <= 1; side += 2)
                {
                    Transform stream = root.transform.Find(
                        $"Fountain Stream 0 {side}");
                    Assert.That(stream, Is.Not.Null);
                    Vector3 expected = origin + tangent *
                        (side *
                         CityFountainWaterBuilder.SpoutTipOffset);
                    Assert.That(
                        stream.position.x,
                        Is.EqualTo(expected.x).Within(0.001f));
                    Assert.That(
                        stream.position.z,
                        Is.EqualTo(expected.z).Within(0.001f));
                    float streamTop = stream.position.y +
                                      stream.localScale.y * 0.5f;
                    float streamBottom = stream.position.y -
                                         stream.localScale.y * 0.5f;
                    Assert.That(
                        streamTop,
                        Is.EqualTo(
                            origin.y +
                            CityFountainWaterBuilder.SpoutMouthY)
                            .Within(0.001f),
                        "The pour must leave the spout arm.");
                    Assert.That(
                        streamBottom,
                        Is.LessThan(top),
                        "The pour must dip below the surface.");

                    Transform splash = root.transform.Find(
                        $"Fountain Splash 0 {side}");
                    Assert.That(splash, Is.Not.Null);
                    Assert.That(
                        splash.position.y,
                        Is.GreaterThan(top),
                        "The splash ring rides on the surface.");
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void BasinMaterial_KeepsPondCalmContracts()
        {
            Material basin = CityFountainWaterResources.BasinMaterial;

            // Standing water: no current, and the summed crest stays
            // inside the surface factory's culling headroom - the
            // sea test's contract, restated for the bowl.
            Vector4 flow = basin.GetVector("_FlowDirection");
            Assert.That(flow.x, Is.Zero);
            Assert.That(flow.y, Is.Zero);
            Assert.That(
                basin.GetFloat("_WaveHeight") * 1.73f,
                Is.LessThan(CityWaterSurfaceFactory.CrestAllowance));

            // The lamps must be able to glint on it - that is what a
            // still pool in a lit park is for.
            Assert.That(
                basin.GetFloat("_AdditionalSpecular"),
                Is.GreaterThan(0f));

            // Shallower than the bowl is deep, so the whole basin
            // reads as water over stone rather than an abyss.
            Assert.That(
                basin.GetFloat("_FoamDistance"),
                Is.LessThan(basin.GetFloat("_DepthFadeDistance")));

            // The Morrowind mirror is the fountain's alone: the
            // basin turns it on, the river and the sea keep the
            // shader's zero and render exactly as they always did.
            Assert.That(
                basin.GetFloat("_ReflectionStrength"),
                Is.GreaterThan(0f));
            Assert.That(
                CityRiverResources.WaterMaterial
                    .GetFloat("_ReflectionStrength"),
                Is.Zero);
            Assert.That(
                CitySeaResources.WaterMaterial
                    .GetFloat("_ReflectionStrength"),
                Is.Zero);
        }

        [Test]
        public void StreamShader_CompilesAndScrollsApart()
        {
            Material stream = CityFountainWaterResources.StreamMaterial;
            Material splash = CityFountainWaterResources.SplashMaterial;

            Shader shader = stream.shader;
            Assert.That(shader, Is.EqualTo(splash.shader));
            Assert.That(
                UnityEditor.ShaderUtil.ShaderHasError(shader),
                Is.False,
                "The fountain stream shader fails to compile.");

            // The column falls along world Y; the ring creeps along
            // the plane, slower - one shader, two motions.
            Assert.That(
                stream.GetFloat("_PlanarScroll"),
                Is.EqualTo(0f).Within(0.001f));
            Assert.That(
                splash.GetFloat("_PlanarScroll"),
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                splash.GetFloat("_ScrollSpeed"),
                Is.LessThan(stream.GetFloat("_ScrollSpeed")));
        }
    }
}
