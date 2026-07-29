using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarNpcPlannerTests
    {
        [Test]
        public void Create_IsStableAcrossRepeatedAndReorderedInput()
        {
            List<BarNpcAnchor> anchors = CreateAnchors(16);
            BarNpcPlan first = BarNpcPlanner.Create(
                71821,
                "bar-stable",
                BarActivityKind.BeerPong,
                anchors);
            anchors.Reverse();
            BarNpcPlan reordered = BarNpcPlanner.Create(
                71821,
                "bar-stable",
                BarActivityKind.BeerPong,
                anchors);

            Assert.That(
                reordered.StableSeed,
                Is.EqualTo(first.StableSeed));
            CollectionAssert.AreEqual(
                first.Definitions,
                reordered.Definitions);
        }

        [Test]
        public void Create_FromInteriorLayout_UsesItsNpcAnchors()
        {
            BarInteriorLayoutPlan layout =
                BarInteriorLayoutPlanner.Generate(
                    441,
                    "bar-layout-adapter",
                    BarActivityKind.BeerPong);

            BarNpcPlan plan = BarNpcPlanner.Create(layout);

            Assert.That(
                plan.CitySeed,
                Is.EqualTo(layout.CitySeed));
            Assert.That(plan.BarId, Is.EqualTo(layout.BarId));
            Assert.That(plan.Activity, Is.EqualTo(layout.Activity));
            Assert.That(
                plan.Count,
                Is.EqualTo(BarNpcPlanner.TargetNpcCount));
            Assert.That(
                plan.Definitions.All(
                    definition =>
                        layout.NpcAnchors.Any(
                            anchor =>
                                anchor.Id ==
                                definition.AnchorId)),
                Is.True);
        }

        [Test]
        public void Create_DefaultPlan_RespectsPopulationAndRoleBudgets()
        {
            BarNpcPlan plan = BarNpcPlanner.Create(
                4421,
                "bar-budget",
                BarActivityKind.Cocktail,
                CreateAnchors(18));

            Assert.That(
                plan.Count,
                Is.EqualTo(BarNpcPlanner.TargetNpcCount));
            Assert.That(
                plan.Definitions.Count(
                    definition =>
                        definition.Role ==
                        BarNpcRole.Bartender),
                Is.EqualTo(2));
            Assert.That(
                plan.Definitions.Count(
                    definition => definition.Mobile),
                Is.LessThanOrEqualTo(
                    BarNpcPlanner.MaximumMobileNpcCount));
            Assert.That(
                plan.Definitions.Select(
                        definition => definition.Id)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(plan.Count));
            Assert.That(
                plan.Definitions.Select(
                        definition => definition.AnchorId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                Is.EqualTo(plan.Count));

            foreach (BarNpcDefinition definition
                     in plan.Definitions)
            {
                Assert.That(
                    definition.VisualVariant,
                    Is.InRange(
                        0,
                        BarNpcSpriteLibrary.VariantCount - 1));
                Assert.That(
                    definition.AnimationPhase01,
                    Is.InRange(0f, 1f));
                Assert.That(
                    definition.Scale,
                    Is.InRange(0.95f, 1.05f));
                if (definition.Mobile)
                {
                    Assert.That(
                        definition.Role,
                        Is.EqualTo(BarNpcRole.Walker));
                    Assert.That(
                        definition.RouteEnd,
                        Is.Not.EqualTo(definition.Position));
                }
            }
        }

        [Test]
        public void Create_ClampsRequestedPopulationToHardMaximum()
        {
            BarNpcPlan plan = BarNpcPlanner.Create(
                19,
                "bar-maximum",
                BarActivityKind.TinctureMatch,
                CreateAnchors(20),
                100);

            Assert.That(
                plan.DesiredCount,
                Is.EqualTo(BarNpcPlanner.MaximumNpcCount));
            Assert.That(
                plan.Count,
                Is.EqualTo(BarNpcPlanner.MaximumNpcCount));
            Assert.That(
                plan.Definitions.Count(
                    definition => definition.Mobile),
                Is.LessThanOrEqualTo(2));
        }

        [Test]
        public void Create_PreservesLayoutRolesAndVisualVariants()
        {
            BarNpcAnchor[] anchors =
            {
                CreateAnchor(
                    "standing-a",
                    BarNpcRole.StandingPatron,
                    0),
                CreateAnchor(
                    "seat-b",
                    BarNpcRole.SeatedPatron,
                    1),
                CreateAnchor(
                    "performer-c",
                    BarNpcRole.Performer,
                    2)
            };

            BarNpcPlan plan = BarNpcPlanner.Create(
                8,
                "bar-fallback",
                BarActivityKind.SplitTheG,
                anchors);

            Assert.That(plan.Count, Is.EqualTo(anchors.Length));
            Assert.That(
                plan.Definitions.Select(
                    definition => definition.Role),
                Is.EquivalentTo(
                    anchors.Select(anchor => anchor.Role)));
            foreach (BarNpcDefinition definition
                     in plan.Definitions)
            {
                BarNpcAnchor source = anchors.Single(
                    anchor =>
                        anchor.Id == definition.AnchorId);
                Assert.That(
                    definition.VisualVariant,
                    Is.EqualTo(source.VisualVariant));
                Assert.That(
                    definition.AnimationPhase01,
                    Is.EqualTo(source.AnimationPhase));
            }
        }

        [Test]
        public void Create_DifferentStableBarIdVariesDefinitions()
        {
            IReadOnlyList<BarNpcAnchor> anchors =
                CreateAnchors(16);
            BarNpcPlan first = BarNpcPlanner.Create(
                9001,
                "bar-a",
                BarActivityKind.Cocktail,
                anchors);
            BarNpcPlan second = BarNpcPlanner.Create(
                9001,
                "bar-b",
                BarActivityKind.Cocktail,
                anchors);

            Assert.That(
                second.StableSeed,
                Is.Not.EqualTo(first.StableSeed));
            Assert.That(
                second.Definitions.SequenceEqual(
                    first.Definitions),
                Is.False);
        }

        [Test]
        public void Create_RejectsDuplicateAnchorIds()
        {
            BarNpcAnchor duplicate = CreateAnchor(
                "duplicate",
                BarNpcRole.SeatedPatron,
                0);
            BarNpcAnchor[] anchors =
            {
                duplicate,
                new BarNpcAnchor(
                    duplicate.Id,
                    BarNpcRole.StandingPatron,
                    new Vector3(2f, 0f, 1f),
                    180f,
                    2,
                    0.4f)
            };

            Assert.That(
                () => BarNpcPlanner.Create(
                    1,
                    "bar-duplicates",
                    BarActivityKind.Cocktail,
                    anchors),
                Throws.ArgumentException);
        }

        internal static List<BarNpcAnchor> CreateAnchors(
            int count)
        {
            var anchors = new List<BarNpcAnchor>(count);
            if (count > 0)
            {
                anchors.Add(CreateAnchor(
                    "bartender-main",
                    BarNpcRole.Bartender,
                    0));
            }

            for (int index = 1; index < count; index++)
            {
                if (index == 1)
                {
                    anchors.Add(CreateAnchor(
                        "bartender-second",
                        BarNpcRole.Bartender,
                        index));
                }
                else if (index <= 3)
                {
                    anchors.Add(new BarNpcAnchor(
                        $"walker-{index}",
                        BarNpcRole.Walker,
                        new Vector3(
                            -4f + index,
                            0f,
                            4f),
                        90f,
                        index %
                        BarNpcSpriteLibrary.VariantCount,
                        index * 0.07f,
                        $"route-{index}"));
                }
                else if (index == 4)
                {
                    anchors.Add(CreateAnchor(
                        "performer",
                        BarNpcRole.Performer,
                        index));
                }
                else
                {
                    BarNpcRole role =
                        index % 2 == 0
                            ? BarNpcRole.SeatedPatron
                            : BarNpcRole.StandingPatron;
                    anchors.Add(new BarNpcAnchor(
                        $"patron-{index}",
                        role,
                        new Vector3(
                            -6f + index,
                            0f,
                            -2f + index * 0.25f),
                        index * 31f,
                        index %
                        BarNpcSpriteLibrary.VariantCount,
                        Mathf.Repeat(index * 0.13f, 1f)));
                }
            }

            return anchors;
        }

        private static BarNpcAnchor CreateAnchor(
            string id,
            BarNpcRole role,
            int index)
        {
            return new BarNpcAnchor(
                id,
                role,
                new Vector3(index, 0f, index * 0.4f),
                180f,
                index % BarNpcSpriteLibrary.VariantCount,
                Mathf.Repeat(index * 0.17f, 1f));
        }
    }
}
