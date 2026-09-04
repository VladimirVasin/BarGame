using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The mother's authored contract: where she sits, how the chair rocks,
    /// and whether her face can actually be addressed.
    ///
    /// Everything here reads assets or pure data. What she looks like in the
    /// room is judged by the PlayMode test and by a rendered frame, because a
    /// number cannot tell you a woman is sitting badly.
    /// </summary>
    public sealed class MothersHouseMotherTests
    {
        private const float CushionTopY = 0.57f;

        [Test]
        public void ShePutsHerHipsOnTheDrawnCushion()
        {
            MothersHouseMotherPlan plan = MothersHouseMotherPlan.Create();

            // The cushion is drawn from x -0.27..0.31 and z 1.26..1.80.
            Assert.That(plan.SeatPosition.x, Is.InRange(-0.27f, 0.31f));
            Assert.That(plan.SeatPosition.z, Is.InRange(1.26f, 1.80f));

            // Settled BACK against the rest, not perched on the front edge.
            Assert.That(
                plan.SeatPosition.z,
                Is.GreaterThan(1.53f),
                "She is settled back in the chair, not about to stand up.");
            Assert.That(plan.SeatPosition.y, Is.EqualTo(0f));
            Assert.That(
                Vector3.Distance(plan.Facing, Vector3.back),
                Is.LessThan(0.0001f),
                "The chair's back is to the hearth and she faces the room.");
        }

        [Test]
        public void TheRockCentreIsTheRunnersOwnCurvature()
        {
            // y = 0.055 + 0.2520 dz^2, so the radius of curvature at the
            // vertex is 1 / (2 * 0.2520). The constant must be the geometry's,
            // not a number that happened to look right.
            const float curvature = 0.2520f;
            float expectedRadius = 1f / (2f * curvature);
            Assert.That(
                MothersHouseRockingChairMotion.RunnerRadius,
                Is.EqualTo(expectedRadius).Within(0.001f));

            Vector3 centre =
                MothersHouseRockingChairMotion.GetRockCenter(null);
            Assert.That(centre.x, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(centre.z, Is.EqualTo(1.55f).Within(0.0001f));
            Assert.That(
                centre.y,
                Is.EqualTo(0.055f + expectedRadius).Within(0.001f));
        }

        [Test]
        public void TheRockStaysInsideTheChairsOwnCollider()
        {
            // The fixture blocker is 0.8 x 1.3 m centred on (0, 1.55) and it
            // does not move. Rolling the runners must not push the chair out
            // of it, or the hero would walk through timber.
            float travel = Mathf.Abs(
                MothersHouseRockingChairMotion.RunnerRadius *
                Mathf.Sin(
                    MothersHouseRockingChairMotion.AmplitudeDegrees *
                    Mathf.Deg2Rad));
            Assert.That(
                travel,
                Is.LessThan(0.13f),
                "A 1.3 m deep blocker leaves the runners no more than this.");
            Assert.That(
                travel,
                Is.GreaterThan(0.02f),
                "Below this the rock is not visible and not worth having.");
        }

        [Test]
        public void HerStagedPrefabCarriesTheWholeExpressionGrid()
        {
            MothersHouseMotherProvider provider =
                MothersHouseMotherProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "Her provider must live at " +
                MothersHouseMotherProvider.ResourcePath);
            provider.ValidateOrThrow();

            CityPedestrianAssetRegistry registry =
                provider.StagedPrefab
                    .GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.DesignId,
                Is.EqualTo(MothersHouseMotherProvider.DesignId));
            Assert.That(
                registry.HasFaceAtlas,
                Is.True,
                "All five canonical cells must resolve or the binding " +
                "reports itself unconfigured and she loses her face.");

            Player3DFaceAtlasBinding atlas = registry.FaceAtlas;
            Assert.That(atlas.Columns, Is.EqualTo(4));
            Assert.That(atlas.Rows, Is.EqualTo(4));
            Assert.That(
                atlas.Renderer.name,
                Is.EqualTo("GEO_FaceSurface"));

            foreach (PlayerFacialExpression expression in
                     System.Enum.GetValues(typeof(PlayerFacialExpression))
                         .Cast<PlayerFacialExpression>()
                         // The drink's four faces are the hero's; every
                         // rig carries the five sober ones.
                         .Where(PlayerFacialExpressionRules.IsCanonical))
            {
                Assert.That(
                    atlas.TryGetTextureTransform(expression, out _),
                    Is.True,
                    $"'{expression}' has no cell.");
            }
        }

        [Test]
        public void NeutralLandsOnTheTopRowOfTheAtlas()
        {
            // THE ROW FLIP, pinned. The generator paints top-down and flips
            // into Unity's bottom-up order before writing the manifest; a
            // second flip anywhere in the chain produces NO error, because
            // every spare cell repeats Neutral - she would simply wear one
            // face forever and nothing would say why. Neutral is drawn in the
            // top-left cell, so Unity must address it as row 3.
            CityPedestrianAssetRegistry registry =
                MothersHouseMotherProvider.Load().StagedPrefab
                    .GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(
                registry.FaceAtlas.TryGetTextureTransform(
                    PlayerFacialExpression.Neutral,
                    out Vector4 transform),
                Is.True);
            Assert.That(transform.x, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(transform.y, Is.EqualTo(0.25f).Within(0.0001f));
            Assert.That(transform.z, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(
                transform.w,
                Is.EqualTo(0.75f).Within(0.0001f),
                "Neutral is drawn top-left, which Unity addresses as row 3.");
        }

        [Test]
        public void SheIsAsDetailedAsTheHeroAndCarriesNoDetailAtlas()
        {
            CityPedestrianAssetRegistry registry =
                MothersHouseMotherProvider.Load().StagedPrefab
                    .GetComponent<CityPedestrianAssetRegistry>();

            // The hero is 34 meshes and 1984 triangles. "No less detailed
            // than the hero" is the whole point of her budget.
            Assert.That(
                registry.SourceTriangleCount,
                Is.InRange(1700, 2500));
            Assert.That(
                registry.Renderers.Count,
                Is.GreaterThanOrEqualTo(34),
                "Fewer parts than the hero would read as a cheaper figure.");

            // A face atlas and a detail atlas are opposites: full colour
            // chosen at runtime against grey baked into the UVs. She wears
            // the first and must not also wear the second.
            Assert.That(registry.DetailAtlas, Is.Null);
            Assert.That(
                registry.RendererBindings.Any(
                    binding => binding.UsesDetailAtlas),
                Is.False);
        }

        [Test]
        public void HerOnlyClipIsTheSeatedLoopAndSheNeverWalks()
        {
            CityPedestrianAssetRegistry registry =
                MothersHouseMotherProvider.Load().StagedPrefab
                    .GetComponent<CityPedestrianAssetRegistry>();
            Assert.That(registry.IdleClip, Is.Not.Null);
            Assert.That(registry.IdleClip.name, Is.EqualTo("MotherRock"));
            Assert.That(registry.IdleClip.isLooping, Is.True);
            Assert.That(
                registry.WalkClip,
                Is.Null,
                "She has never walked and the slot must say so.");
            Assert.That(registry.SitClip, Is.Null);
            Assert.That(registry.ActionClip, Is.Null);
        }

        [Test]
        public void HerPrefabIsPassiveAndSilent()
        {
            GameObject prefab =
                MothersHouseMotherProvider.Load().StagedPrefab;

            // The room holds exactly three AudioSources and a counted set of
            // colliders. She adds to neither, and she has no voice by canon.
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
        }

        [Test]
        public void ThePresentationsCushionMatchesTheDrawnChair()
        {
            // Two files, two tools, one number. The chair is drawn by the
            // room generator and she is measured by the pedestrian generator,
            // and nothing but this would notice them drifting apart.
            Assert.That(
                MothersHouseMotherPresentation.CushionTopY,
                Is.EqualTo(CushionTopY).Within(0.0001f));
            Assert.That(
                MothersHouseMotherPresentation.PerchPelvisLiftMeters,
                Is.EqualTo(0.0526f).Within(0.0005f));
        }
    }
}
