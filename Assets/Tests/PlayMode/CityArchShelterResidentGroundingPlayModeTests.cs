using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    [Category("CityArchShelter")]
    public sealed class CityArchShelterResidentGroundingPlayModeTests
    {
        private const float MaximumSupportGap = 0.035f;
        private const float MaximumPenetration = 0.01f;

        private static readonly string[] TorsoSupportRenderers =
        {
            "CLO_BlanketChest",
            "CLO_BlanketWaist"
        };

        private static readonly string[] HipSupportRenderers =
        {
            "CLO_CoatSeat",
            "CLO_BlanketHipVolume",
            "CLO_BlanketHip.L",
            "CLO_BlanketHip.R"
        };

        [UnityTest]
        public IEnumerator BuiltSleeper_TorsoAndHipsRestOnVisibleMattress()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityArchShelterPlan plan = CityArchShelterPlanner.Create(layout);
            Assert.That(plan.IsEnabled, Is.True);

            var parent = new GameObject("Shelter Grounding Test Parent");
            try
            {
                CityArchShelterWorldResult result =
                    CityArchShelterWorldBuilder.Build(
                        parent.transform,
                        layout,
                        plan);
                int sleeperIndex = Enumerable.Range(
                        0,
                        plan.NpcAnchors.Count)
                    .Single(index =>
                        plan.NpcAnchors[index].Stage ==
                        CityArchShelterNpcStageKind.Sleeper);
                Animator sleeperAnimator = result.ResidentRoots[sleeperIndex]
                    .GetComponentInChildren<Animator>(true);
                Assert.That(sleeperAnimator, Is.Not.Null);
                sleeperAnimator.cullingMode =
                    AnimatorCullingMode.AlwaysAnimate;

                // Resume on the next frame after the manually driven resident
                // PlayableGraph has had an uncullable LateUpdate evaluation.
                // Headless test runs have no camera, so AlwaysAnimate is what
                // makes their bone pose match a visible in-game resident.
                yield return null;

                int beddingIndex = Enumerable.Range(0, plan.Props.Count)
                    .Single(index =>
                        plan.Props[index].Kind ==
                        CityArchShelterPropKind.Bedding);
                Renderer mattress = result.PropRoots[beddingIndex]
                    .GetComponentsInChildren<Renderer>(true)
                    .Single(renderer =>
                        renderer.name == "Mattress_Residential");
                Assert.That(mattress.enabled, Is.True);
                float mattressSupportTop = result.PropRoots[beddingIndex]
                    .TransformPoint(
                        Vector3.up *
                        CityArchShelterPlanner.BeddingMattressSupportTop)
                    .y;
                Assert.That(
                    mattress.bounds.max.y,
                    Is.GreaterThan(mattressSupportTop),
                    "The narrow mattress seams should remain above its " +
                    "broad support surface.");

                SkinnedMeshRenderer[] visibleSleeperMeshes =
                    result.ResidentRoots[sleeperIndex]
                        .GetComponentsInChildren<SkinnedMeshRenderer>(true)
                        .Where(renderer =>
                            renderer.enabled &&
                            renderer.gameObject.activeInHierarchy)
                        .ToArray();
                Assert.That(visibleSleeperMeshes, Is.Not.Empty);

                AssertSupportFamily(
                    visibleSleeperMeshes,
                    TorsoSupportRenderers,
                    mattressSupportTop,
                    "torso");
                AssertSupportFamily(
                    visibleSleeperMeshes,
                    HipSupportRenderers,
                    mattressSupportTop,
                    "hips");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        private static void AssertSupportFamily(
            IReadOnlyList<SkinnedMeshRenderer> renderers,
            IReadOnlyCollection<string> expectedNames,
            float mattressTop,
            string label)
        {
            var expected = new HashSet<string>(
                expectedNames,
                StringComparer.Ordinal);
            SkinnedMeshRenderer[] supportRenderers = renderers
                .Where(renderer => expected.Contains(renderer.name))
                .ToArray();
            Assert.That(
                supportRenderers.Select(renderer => renderer.name),
                Is.EquivalentTo(expectedNames),
                $"The sleeper {label} support meshes changed.");

            float lowest = float.PositiveInfinity;
            foreach (SkinnedMeshRenderer renderer in supportRenderers)
            {
                lowest = Mathf.Min(
                    lowest,
                    FindLowestSkinnedVertex(renderer));
            }

            Assert.That(float.IsPositiveInfinity(lowest), Is.False, label);
            float gap = lowest - mattressTop;
            Assert.That(
                gap,
                Is.InRange(-MaximumPenetration, MaximumSupportGap),
                $"The sleeper {label} appears unsupported: its broad " +
                $"support family is {gap:F4} m above the visible mattress.");
        }

        private static float FindLowestSkinnedVertex(
            SkinnedMeshRenderer renderer)
        {
            var baked = new Mesh();
            try
            {
                renderer.BakeMesh(baked, true);
                Vector3[] vertices = baked.vertices;
                Assert.That(vertices, Is.Not.Empty, renderer.name);
                float lowest = float.PositiveInfinity;
                for (int index = 0; index < vertices.Length; index++)
                {
                    lowest = Mathf.Min(
                        lowest,
                        renderer.transform.TransformPoint(vertices[index]).y);
                }

                return lowest;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(baked);
            }
        }
    }
}
