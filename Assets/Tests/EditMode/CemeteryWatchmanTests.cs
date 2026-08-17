using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CemeteryWatchmanTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        [Test]
        public void Planner_BuildsTheGateLodgeInItsPocket()
        {
            (CityLayout layout, CityCemeteryPlan plan) =
                GenerateCemetery();
            List<CityCemeteryPartDescriptor> lodgeParts = plan.Parts
                .Where(part =>
                    part.Kind == CityCemeteryPartKind.Lodge)
                .ToList();

            Assert.That(lodgeParts, Has.Count.EqualTo(15),
                "The default cemetery carries the full lodge.");
            Assert.That(
                lodgeParts.All(part =>
                    part.StableId.StartsWith(
                        "cemetery-lodge-",
                        System.StringComparison.Ordinal)),
                Is.True);
            Assert.That(
                lodgeParts.All(part => part.GraveOrdinal == -1),
                Is.True,
                "Lodge parts carry no grave identity.");
            Assert.That(
                plan.GetCount(CityCemeteryPartKind.Lodge),
                Is.EqualTo(15));

            // The lodge stands aside from the gate opening: every
            // blocking part clears the raw approach rectangle (the
            // stricter of the two clearance rules).
            CemeteryMournerPlan.TryGetAccess(
                layout,
                out CityOpenAreaAccessDescriptor access);
            foreach (CityCemeteryPartDescriptor part in lodgeParts)
            {
                Assert.That(part.BlocksMovement, Is.True,
                    "Lodge styles are all solid.");
                Rect footprint = ToXZRect(part);
                Assert.That(
                    footprint.Overlaps(access.ApproachBounds),
                    Is.False,
                    $"'{part.StableId}' must keep the canonical " +
                    "street approach untouched.");
                Assert.That(
                    Expand(plan.Grounds, 0.65f).Contains(footprint.min) &&
                    Expand(plan.Grounds, 0.65f).Contains(footprint.max),
                    Is.True,
                    $"'{part.StableId}' stays inside the grounds.");
            }

            // The reserved pocket kept the rest of the dressing out:
            // no grave, tree, bush or bench part stands inside the
            // lodge footprint band.
            Rect lodgeBounds = lodgeParts
                .Select(ToXZRect)
                .Aggregate((left, right) => Union(left, right));
            foreach (CityCemeteryPartDescriptor part in plan.Parts)
            {
                // Graves, trees and benches consult the reserved
                // pocket; the fence/gate family legitimately borders
                // it and bushes only hug grave enclosures far deeper
                // in, so the sweep checks exactly the reserved kinds.
                if (part.Kind != CityCemeteryPartKind.GraveSlab &&
                    part.Kind != CityCemeteryPartKind.GraveMonument &&
                    part.Kind != CityCemeteryPartKind.GraveEnclosure &&
                    part.Kind != CityCemeteryPartKind.GraveOffering &&
                    part.Kind != CityCemeteryPartKind.TreeTrunk &&
                    part.Kind != CityCemeteryPartKind.TreeCrown &&
                    part.Kind != CityCemeteryPartKind.Bench)
                {
                    continue;
                }

                Assert.That(
                    ToXZRect(part).Overlaps(lodgeBounds),
                    Is.False,
                    $"'{part.StableId}' invades the watchman's pocket.");
            }
        }

        [Test]
        public void Plan_AbsentWithoutACemeteryOrALodge()
        {
            Assert.That(
                CemeteryWatchmanPlan.Create(null).IsPresent,
                Is.False);
        }

        [Test]
        public void Plan_StandsTheWatchmanAtHisWindowFacingTheGate()
        {
            (CityLayout _, CityCemeteryPlan plan) = GenerateCemetery();
            CemeteryWatchmanPlan watchmanPlan =
                CemeteryWatchmanPlan.Create(plan);

            Assert.That(watchmanPlan.IsPresent, Is.True);
            CemeteryWatchmanStance stance = watchmanPlan.Stance;
            Assert.That(
                stance.Position.y,
                Is.EqualTo(plan.GroundTopY).Within(0.001f));
            Assert.That(
                stance.Facing.magnitude,
                Is.EqualTo(1f).Within(0.001f));
            Assert.That(
                Mathf.Abs(stance.Facing.y),
                Is.LessThan(0.001f));

            CityCemeteryPartDescriptor lodgeBase = plan.Parts.Single(
                part => part.StableId == "cemetery-lodge-base");
            Rect baseRect = ToXZRect(lodgeBase);
            Assert.That(
                baseRect.Contains(new Vector2(
                    stance.Position.x,
                    stance.Position.z)),
                Is.False,
                "He stands outside his own booth, at the window.");
            Assert.That(
                Vector3.Distance(stance.Position, lodgeBase.Center),
                Is.LessThan(2.6f),
                "...but right beside it.");

            // He literally watches the gate arch.
            CityCemeteryPartDescriptor arch = plan.Parts.First(
                part => part.StableId == "cemetery-gate-arch");
            Vector3 toArch = arch.Center - stance.Position;
            toArch.y = 0f;
            Assert.That(
                Vector3.Dot(stance.Facing, toArch.normalized),
                Is.GreaterThan(0.9f));
        }

        [Test]
        public void Quips_AreDeterministicAndNeverRepeatBackToBack()
        {
            uint firstState = CemeteryWatchmanQuips.CreateState(Seed);
            uint secondState = CemeteryWatchmanQuips.CreateState(Seed);
            int previousFirst = -1;
            int previousSecond = -1;
            var seen = new HashSet<int>();
            int drawsUntilFullCoverage = -1;
            for (int draw = 0; draw < 200; draw++)
            {
                int first = CemeteryWatchmanQuips.NextIndex(
                    ref firstState,
                    previousFirst);
                int second = CemeteryWatchmanQuips.NextIndex(
                    ref secondState,
                    previousSecond);
                Assert.That(first, Is.EqualTo(second),
                    "The same seed serves the same repertoire.");
                Assert.That(first, Is.Not.EqualTo(previousFirst),
                    "He never says the same thing twice running.");
                Assert.That(
                    first,
                    Is.InRange(
                        0,
                        CemeteryWatchmanQuips.LineKeys.Length - 1));
                previousFirst = first;
                previousSecond = second;
                seen.Add(first);
                if (drawsUntilFullCoverage < 0 &&
                    seen.Count ==
                    CemeteryWatchmanQuips.LineKeys.Length)
                {
                    drawsUntilFullCoverage = draw + 1;
                }
            }

            Assert.That(drawsUntilFullCoverage, Is.InRange(1, 200),
                "The whole repertoire comes up in ordinary play.");
            Assert.That(
                CemeteryWatchmanQuips.LineKeys.Distinct().Count(),
                Is.EqualTo(CemeteryWatchmanQuips.LineKeys.Length));
        }

        [Test]
        public void Quips_KeysExistInBothLocalizationCatalogs()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                TextAsset catalog = Resources.Load<TextAsset>(
                    $"Localization/{language}");
                Assert.That(catalog, Is.Not.Null);
                foreach (string key in CemeteryWatchmanQuips.LineKeys)
                {
                    Assert.That(
                        catalog.text.Contains($"\"{key}\""),
                        Is.True,
                        $"{language}.json is missing '{key}'.");
                }

                Assert.That(
                    catalog.text.Contains(
                        $"\"{CemeteryWatchmanInteraction.TalkPromptKey}\""),
                    Is.True,
                    $"{language}.json is missing the talk prompt.");
            }
        }

        private static (CityLayout, CityCemeteryPlan) GenerateCemetery()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CityCemeteryPlan plan = CityCemeteryPlanner.Create(layout);
            Assert.That(plan, Is.Not.Null,
                "The default city must carry a dressable cemetery.");
            return (layout, plan);
        }

        private static Rect ToXZRect(CityCemeteryPartDescriptor part)
        {
            Vector3 right = part.Rotation * Vector3.right;
            Vector3 up = part.Rotation * Vector3.up;
            Vector3 forward = part.Rotation * Vector3.forward;
            float halfX =
                Mathf.Abs(right.x) * part.Size.x * 0.5f +
                Mathf.Abs(up.x) * part.Size.y * 0.5f +
                Mathf.Abs(forward.x) * part.Size.z * 0.5f;
            float halfZ =
                Mathf.Abs(right.z) * part.Size.x * 0.5f +
                Mathf.Abs(up.z) * part.Size.y * 0.5f +
                Mathf.Abs(forward.z) * part.Size.z * 0.5f;
            return Rect.MinMaxRect(
                part.Center.x - halfX,
                part.Center.z - halfZ,
                part.Center.x + halfX,
                part.Center.z + halfZ);
        }

        private static Rect Union(Rect left, Rect right)
        {
            return Rect.MinMaxRect(
                Mathf.Min(left.xMin, right.xMin),
                Mathf.Min(left.yMin, right.yMin),
                Mathf.Max(left.xMax, right.xMax),
                Mathf.Max(left.yMax, right.yMax));
        }

        private static Rect Expand(Rect source, float amount)
        {
            return new Rect(
                source.x - amount,
                source.y - amount,
                source.width + amount * 2f,
                source.height + amount * 2f);
        }
    }
}
