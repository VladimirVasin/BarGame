using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The hero is placed on the bed by pinning one bone to one authored
    /// point, and nothing at runtime pushes him back out of geometry he
    /// overlaps. So the bed has to be built around where the authored sleep
    /// pose actually puts him, and that agreement is what these tests own.
    /// </summary>
    public sealed class HomeBedDressingGeometryTests
    {
        private const float Tolerance = 0.0005f;

        // How wide a corridor the sleeper's body and arms occupy across the
        // mattress. Bedding inside it would be bedding he lies through.
        private const float SleeperHalfWidth = 0.32f;

        [Test]
        public void MattressSurfaceConstant_MatchesTheBuiltMattress()
        {
            var parent = new GameObject("Home Bed Geometry Test");
            try
            {
                Transform bed = BuildBed(parent);

                // Measured from the surface's own rest plane, not the
                // renderer AABB: the bounds carry welt/dent allowances
                // above and below the geometry on purpose.
                Assert.That(
                    FindSurfaceRestTop(bed, "Home Bed Mattress"),
                    Is.EqualTo(
                        HomeInteriorWorldBuilder
                            .BedMattressSurfaceHeight)
                        .Within(Tolerance),
                    "The constant the interaction plan places the hero " +
                    "against must be the top of the real mattress box.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void SleepingHip_RestsTheBodyExactlyOnTheMattress()
        {
            HomeInteriorLayoutPlan home =
                HomeInteriorLayoutPlanner.Generate();
            HomeBedInteractionPlan plan =
                HomeBedInteractionPlan.Create(home);

            float backPlane =
                plan.ActionHipPosition.y -
                PlayerCharacterDimensions.SupinePelvisSupportOffset;

            Assert.That(
                backPlane,
                Is.EqualTo(
                    HomeInteriorWorldBuilder.BedMattressSurfaceHeight -
                    HomeInteriorWorldBuilder.BedSleeperSinkDepth)
                    .Within(Tolerance),
                "The lowest point of the supine pose must land on the " +
                "dented mattress: the surface gives under him, and he " +
                "lies in that dent rather than hovering over it.");
        }

        [Test]
        public void SeatedHip_PlantsBothBootsAndKeepsHimOffTheMattress()
        {
            HomeInteriorLayoutPlan home =
                HomeInteriorLayoutPlanner.Generate();
            HomeBedInteractionPlan plan =
                HomeBedInteractionPlan.Create(home);

            float clearance =
                plan.SeatHipPosition.y -
                HomeInteriorWorldBuilder.BedMattressSurfaceHeight;

            Assert.That(
                clearance,
                Is.EqualTo(
                    PlayerCharacterDimensions.SeatedPelvisSupportOffset)
                    .Within(Tolerance));
            Assert.That(
                clearance,
                Is.InRange(0f, 0.04f),
                "Sitting on the edge must neither hover above the bedding " +
                "nor bury him in it.");

            var mattress = new Rect(
                plan.BedBounds.xMin + 0.08f,
                plan.BedBounds.yMin + 0.09f,
                plan.BedBounds.width - 0.16f,
                plan.BedBounds.height - 0.18f);
            Assert.That(
                mattress.Contains(
                    new Vector2(
                        plan.SeatHipPosition.x,
                        plan.SeatHipPosition.z)),
                Is.True,
                "He must sit on the mattress, not past its edge on the " +
                "bare frame.");
        }

        [Test]
        public void Pillow_MeetsTheBackOfHisHeadInsteadOfSwallowingIt()
        {
            var parent = new GameObject("Home Bed Geometry Test");
            try
            {
                Transform bed = BuildBed(parent);
                Bounds pillow = FindPart(bed, "Home Pillow");
                float pillowRestTop =
                    FindSurfaceRestTop(bed, "Home Pillow");
                HomeBedInteractionPlan plan =
                    HomeBedInteractionPlan.Create(
                        HomeInteriorLayoutPlanner.Generate());

                float headPlane =
                    plan.ActionHipPosition.y -
                    PlayerCharacterDimensions.SupineHeadSupportOffset;

                // The pillow dents too: its rest top rides one pillow-sink
                // above the head plane, so the head lies in the dented
                // pillow instead of inside a rigid one. Measured from the
                // surface's rest plane - the AABB carries welt and dent
                // allowances on purpose.
                Assert.That(
                    pillowRestTop,
                    Is.EqualTo(
                        headPlane +
                        HomeInteriorWorldBuilder.BedPillowSinkDepth)
                        .Within(Tolerance),
                    "The pillow's rest top must sit one dent above where " +
                    "the sunken pose puts the back of his head.");
                Assert.That(
                    pillowRestTop,
                    Is.GreaterThan(
                        HomeInteriorWorldBuilder
                            .BedMattressSurfaceHeight),
                    "A pillow below the mattress is not a pillow.");

                float headX =
                    plan.ActionHipPosition.x -
                    (plan.HeadToFootAxis.x * 0.85f);
                Assert.That(
                    headX,
                    Is.InRange(pillow.min.x, pillow.max.x),
                    "His head has to actually come down over the pillow.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Bedding_StaysOutOfTheSleepersCorridor()
        {
            var parent = new GameObject("Home Bed Geometry Test");
            try
            {
                Transform bed = BuildBed(parent);
                HomeBedInteractionPlan plan =
                    HomeBedInteractionPlan.Create(
                        HomeInteriorLayoutPlanner.Generate());

                float spineZ = plan.ActionHipPosition.z;
                Bounds blanket =
                    FindPart(bed, "Home Bed Crooked Blanket");

                Assert.That(
                    blanket.max.y,
                    Is.GreaterThan(
                        HomeInteriorWorldBuilder
                            .BedMattressSurfaceHeight),
                    "The blanket should still read as bedding heaped on " +
                    "the bed, not as a flat panel inside the mattress.");
                Assert.That(
                    blanket.min.z,
                    Is.GreaterThanOrEqualTo(spineZ + SleeperHalfWidth),
                    "The crooked blanket must be shoved clear of the " +
                    "sleeper; anything overlapping him is geometry he lies " +
                    "through.");
                Assert.That(
                    blanket.max.z,
                    Is.LessThanOrEqualTo(plan.BedBounds.yMax),
                    "and it must not hang off the far side of the bed.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        private static Transform BuildBed(GameObject parent)
        {
            HomeInteriorLayoutPlan plan =
                HomeInteriorLayoutPlanner.Generate();
            return HomeInteriorWorldBuilder.Build(
                parent.transform,
                plan);
        }

        private static Bounds FindPart(Transform root, string name)
        {
            var matches = new List<Renderer>();
            foreach (Renderer renderer in
                     root.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.gameObject.name == name)
                {
                    matches.Add(renderer);
                }
            }

            Assert.That(
                matches,
                Has.Count.EqualTo(1),
                $"Expected exactly one '{name}' in the built bed.");
            return matches[0].bounds;
        }

        private static float FindSurfaceRestTop(
            Transform root,
            string name)
        {
            foreach (HomeBedDeformableSurface surface in
                     root.GetComponentsInChildren<
                         HomeBedDeformableSurface>(true))
            {
                if (surface.gameObject.name == name)
                {
                    return surface.RestTopWorldY;
                }
            }

            Assert.Fail(
                $"Expected a deformable '{name}' in the built bed.");
            return 0f;
        }
    }
}
