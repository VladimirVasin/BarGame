using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityPuddleWaterTests
    {
        [Test]
        public void PuddleMaterial_KeepsStandingFilmContracts()
        {
            Material puddle = CityPuddleWaterResources.Material;
            Assert.That(
                puddle.shader.name,
                Is.EqualTo("Bar Promenade/City River Water"));

            // A puddle is standing film: no current, no per-triangle
            // facets (a two-triangle patch would hand the mirror one
            // flat jump), nothing to refract through three
            // millimetres, no fetch for a whitecap.
            Assert.That(
                (Vector4)puddle.GetVector("_FlowDirection"),
                Is.EqualTo(Vector4.zero));
            Assert.That(puddle.GetFloat("_FacetStrength"), Is.Zero);
            Assert.That(puddle.GetFloat("_RefractionStrength"), Is.Zero);
            Assert.That(puddle.GetFloat("_CrestFoamStrength"), Is.Zero);

            // Edge foam keys off the measured water depth, and the
            // film measures ~3 mm everywhere: a foam distance at or
            // above the film would whitewash the whole patch.
            Assert.That(
                puddle.GetFloat("_FoamDistance"),
                Is.LessThan(CityPuddlePlanner.Thickness * 0.5f),
                "Edge foam must sit below the film's own thickness.");

            // The same crest budget every water pays.
            Assert.That(
                puddle.GetFloat("_WaveHeight") * 1.73f,
                Is.LessThan(CityWaterSurfaceFactory.CrestAllowance));

            // What the user asked of a puddle: the environment mirror
            // and the street lamps' glints.
            Assert.That(
                puddle.GetFloat("_ReflectionStrength"),
                Is.GreaterThan(0f));
            Assert.That(
                puddle.GetFloat("_AdditionalSpecular"),
                Is.GreaterThan(0f));

            // The rim eater is on — the flag that tells the shader
            // this material's meshes carry a rim mask in TEXCOORD0.
            Assert.That(
                puddle.GetVector("_EdgeNoiseParams").z,
                Is.EqualTo(1f).Within(0.0001f));
        }

        [Test]
        public void SurfaceWetness_ReachesOnlyTheDryingWater()
        {
            Material puddle = CityPuddleWaterResources.Material;
            Material river = CityRiverResources.WaterMaterial;

            CityWaterResources.SetSurfaceWetness(0.7f);
            Assert.That(
                puddle.GetFloat("_SurfaceWetness"),
                Is.EqualTo(0.7f).Within(0.0001f),
                "The puddle film must follow the street wetness.");
            Assert.That(
                river.GetFloat("_SurfaceWetness"),
                Is.EqualTo(1f).Within(0.0001f),
                "The river must never dry out.");

            CityWaterResources.SetSurfaceWetness(0f);
            Assert.That(
                puddle.GetFloat("_SurfaceWetness"),
                Is.Zero,
                "A dry street means an invisible puddle.");
        }

        [Test]
        public void WetSurfaceRegistry_DrivesThePuddleFilm()
        {
            CityWetSurfaceRegistry.ResetForTests();
            try
            {
                Material puddle = CityPuddleWaterResources.Material;
                CityWetSurfaceRegistry.SetImmediate(1f);
                Assert.That(
                    puddle.GetFloat("_SurfaceWetness"),
                    Is.EqualTo(1f).Within(0.0001f),
                    "Full rain must fill the puddles.");
                CityWetSurfaceRegistry.SetImmediate(0f);
                Assert.That(
                    puddle.GetFloat("_SurfaceWetness"),
                    Is.Zero,
                    "A dried street must empty them.");
            }
            finally
            {
                CityWetSurfaceRegistry.ResetForTests();
            }
        }

        [Test]
        public void Build_BatchesPatchesIntoOneMirroredWaterSheet()
        {
            var patches = new List<RuntimeOrientedBox>
            {
                new RuntimeOrientedBox(
                    new Vector3(4f, 0.1f, 2f),
                    Quaternion.identity,
                    new Vector3(1.6f, CityPuddlePlanner.Thickness, 0.6f)),
                new RuntimeOrientedBox(
                    new Vector3(-6f, 0.2f, 9f),
                    Quaternion.Euler(0f, 90f, 0f),
                    new Vector3(2.2f, CityPuddlePlanner.Thickness, 0.8f))
            };

            var parent = new GameObject("Puddle Water Test");
            try
            {
                GameObject root = CityPuddleWorldBuilder.Build(
                    parent.transform,
                    patches);
                Assert.That(root, Is.Not.Null);

                var renderer = root.GetComponent<MeshRenderer>();
                Assert.That(renderer, Is.Not.Null);
                Assert.That(
                    renderer.sharedMaterial,
                    Is.SameAs(CityPuddleWaterResources.Material),
                    "Every puddle shares the one water material.");
                Assert.That(
                    renderer.HasPropertyBlock(),
                    Is.False,
                    "Water carries no per-renderer variation — the " +
                    "drive writes the material whole.");
                Assert.That(
                    root.GetComponentInChildren<Collider>(),
                    Is.Null,
                    "A three-millimetre film must never trip a walker.");

                // One 3x3 grid per patch: the centre vertex carries the
                // rim mask the shader erodes from the edge inward.
                var filter = root.GetComponent<MeshFilter>();
                Assert.That(
                    filter.sharedMesh.vertexCount,
                    Is.EqualTo(patches.Count * 9));
                foreach (RuntimeOrientedBox patch in patches)
                {
                    Assert.That(
                        filter.sharedMesh.bounds.Contains(patch.Center),
                        Is.True,
                        "The combined sheet must cover every patch.");
                }

                // The Morrowind mirror: one controller for the whole
                // city's puddles, camera prepared but never enabled.
                var mirror = root.GetComponentInChildren<
                    CityFountainReflectionController>();
                Assert.That(mirror, Is.Not.Null);
                var mirrorCamera =
                    mirror.GetComponentInChildren<Camera>();
                Assert.That(mirrorCamera, Is.Not.Null);
                Assert.That(mirrorCamera.enabled, Is.False);
                Assert.That(
                    CityPuddleWaterResources.Material
                        .GetTexture("_ReflectionCube"),
                    Is.Not.Null,
                    "The puddle material lost its mirror cube.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
