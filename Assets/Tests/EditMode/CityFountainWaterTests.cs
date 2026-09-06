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

                // The water stands in the imported fountain's own
                // frame: a round sheet at the basin's level inside the
                // rim, streams from the spout arms down past the
                // surface.
                CityDecorationWorldBuilder.GetDecorationFrame(
                    layout,
                    fountains[0],
                    out Vector3 origin,
                    out Vector3 tangent,
                    out _);
                float top = origin.y +
                            CityFountainWaterBuilder.BasinWaterTopY;

                Assert.That(
                    CityFountainWaterBuilder.BasinWaterTopY,
                    Is.GreaterThan(
                        CityFountainWaterBuilder.BasinFloorTopY),
                    "The sheet must stand over the basin floor.");
                Assert.That(
                    CityFountainWaterBuilder.BasinWaterTopY,
                    Is.LessThan(
                        CityFountainWaterBuilder.BasinRimTopY - 0.10f),
                    "The rim must still stand out of the water.");

                Transform basin = root.transform.Find(
                    "Fountain Basin Water 0");
                Assert.That(basin, Is.Not.Null);
                Assert.That(
                    basin.position.y,
                    Is.EqualTo(top).Within(0.001f));

                // Every vertex inside the rim, and the sheet reaching
                // it. A square sheet in this round bowl hung its four
                // corners over the grass; a radius check on the mesh
                // itself is what catches that, not one on the bounds.
                Vector3[] basinVertices =
                    basin.GetComponent<MeshFilter>().sharedMesh.vertices;
                Assert.That(basinVertices.Length, Is.GreaterThan(3));
                float widest = 0f;
                for (int vertex = 0;
                     vertex < basinVertices.Length;
                     vertex++)
                {
                    Vector3 local = basinVertices[vertex];
                    Assert.That(
                        local.y,
                        Is.EqualTo(0f).Within(0.001f),
                        "The basin sheet is flat.");
                    float radius = new Vector2(local.x, local.z)
                        .magnitude;
                    Assert.That(
                        radius,
                        Is.LessThanOrEqualTo(
                            CityFountainWaterBuilder
                                .BasinInnerRadius + 0.001f),
                        "The sheet must stay inside the basin rim.");
                    widest = Mathf.Max(widest, radius);
                }

                Assert.That(
                    widest,
                    Is.EqualTo(
                        CityFountainWaterBuilder.BasinInnerRadius)
                        .Within(0.01f),
                    "The sheet must reach the rim, not puddle short.");

                for (int side = -1; side <= 1; side += 2)
                {
                    Transform stream = root.transform.Find(
                        $"Fountain Stream 0 {side}");
                    Assert.That(stream, Is.Not.Null);

                    // The pour leans, so its ends are the box's own
                    // axis, not its bounding height.
                    Vector3 half = stream.rotation *
                                   (Vector3.up *
                                    stream.localScale.y * 0.5f);
                    Vector3 streamMouth = stream.position + half;
                    Vector3 streamFoot = stream.position - half;

                    // The arm the pour hangs from is authored, not
                    // guessed: the imported statue's spout tubes end at
                    // 0.72 with their tip centre at 3.30 and a 0.07
                    // radius, so the mouth is inside that cross section
                    // and the water starts in the stone, not under it.
                    Assert.That(
                        CityFountainWaterBuilder.SpoutTipOffset,
                        Is.EqualTo(0.72f).Within(0.02f),
                        "The pour must fall from the spout tip.");
                    Assert.That(
                        CityFountainWaterBuilder.SpoutMouthY,
                        Is.InRange(3.23f, 3.37f),
                        "The pour must start inside the spout mouth.");

                    Vector3 expected = origin +
                        tangent *
                        (side *
                         CityFountainWaterBuilder.SpoutTipOffset) +
                        Vector3.up *
                        CityFountainWaterBuilder.SpoutMouthY;
                    Assert.That(
                        Vector3.Distance(streamMouth, expected),
                        Is.LessThan(0.001f),
                        "The pour must leave the spout arm.");
                    Assert.That(
                        streamFoot.y,
                        Is.LessThan(top),
                        "The pour must dip below the surface.");

                    // And it must land in the water, not on the stone:
                    // the pedestal flares back out under the statue.
                    float landingRadius = Vector3.ProjectOnPlane(
                        streamFoot - origin,
                        Vector3.up).magnitude;
                    Assert.That(
                        landingRadius -
                        CityFountainWaterBuilder.SplashSize * 0.5f,
                        Is.GreaterThan(
                            CityFountainWaterBuilder
                                .PedestalWaterlineRadius),
                        "The pour must clear the statue's pedestal.");
                    Assert.That(
                        landingRadius +
                        CityFountainWaterBuilder.SplashSize * 0.5f,
                        Is.LessThan(
                            CityFountainWaterBuilder.BasinInnerRadius),
                        "The pour must land inside the basin.");

                    Transform splash = root.transform.Find(
                        $"Fountain Splash 0 {side}");
                    Assert.That(splash, Is.Not.Null);
                    Assert.That(
                        splash.position.y,
                        Is.GreaterThan(top),
                        "The splash ring rides on the surface.");
                    Assert.That(
                        Vector3.ProjectOnPlane(
                            splash.position - streamFoot,
                            Vector3.up).magnitude,
                        Is.LessThan(0.001f),
                        "The splash ring sits where the pour lands.");
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
