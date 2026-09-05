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

            // The lodge lights its own doorstep: exactly one porch
            // bulb, hanging under the roof it belongs to and on the
            // wall beside the doorway rather than out in the plot.
            Assert.That(
                plan.GetLampCount(CityCemeteryLampKind.LodgePorch),
                Is.EqualTo(1));
            CityCemeteryLampDescriptor porch = plan.Lamps.Single(
                lamp => lamp.Kind == CityCemeteryLampKind.LodgePorch);
            Assert.That(porch.StableId, Is.EqualTo("cemetery-lodge-lamp"));
            CityCemeteryPartDescriptor roof = plan.Parts.Single(
                part => part.StableId == "cemetery-lodge-roof");
            Assert.That(
                ToXZRect(roof).Contains(new Vector2(
                    porch.GroundPosition.x,
                    porch.GroundPosition.z)),
                Is.True,
                "The bulb hangs under the lodge's own eave.");
            // Beside the opening, over the solid stretch of the rear
            // wall — the only side with wall to carry a bracket. The
            // doorway's own centre line would fail this check.
            CityCemeteryPartDescriptor rearWall = plan.Parts.Single(
                part => part.StableId == "cemetery-lodge-wall-rear");
            Rect wallReach = Expand(ToXZRect(rearWall), 0.30f);
            Assert.That(
                wallReach.Contains(new Vector2(
                    porch.GroundPosition.x,
                    porch.GroundPosition.z)),
                Is.True,
                "...hung on the wall beside the door.");
            CityCemeteryPartDescriptor doorstep = plan.Parts.Single(
                part => part.StableId == "cemetery-lodge-step");
            Assert.That(
                wallReach.Contains(new Vector2(
                    doorstep.Center.x,
                    doorstep.Center.z)),
                Is.False,
                "The doorway line itself is not where it hangs.");
            Assert.That(
                Vector2.Distance(
                    new Vector2(
                        porch.GroundPosition.x,
                        porch.GroundPosition.z),
                    new Vector2(doorstep.Center.x, doorstep.Center.z)),
                Is.LessThan(1.2f),
                "...but still right at the entrance.");
            Assert.That(
                porch.GroundPosition.y,
                Is.EqualTo(plan.GroundTopY).Within(0.001f));

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
        public void Plan_StandsTheWatchmanOnHisDoorstepFacingTheAlley()
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
                "He stands outside his own booth, at its door.");
            Assert.That(
                Vector3.Distance(stance.Position, lodgeBase.Center),
                Is.LessThan(2.6f),
                "...but right beside it.");

            // He holds the doorway, one short pace out and one step
            // aside toward the alley — so he stands in front of the
            // open leaf, never inside its sweep.
            CityCemeteryPartDescriptor step = plan.Parts.Single(
                part => part.StableId == "cemetery-lodge-step");
            var toStep = new Vector2(
                step.Center.x - stance.Position.x,
                step.Center.z - stance.Position.z);
            float expectedOffset = Mathf.Sqrt(
                CemeteryWatchmanPlan.DoorStandOffMeters *
                CemeteryWatchmanPlan.DoorStandOffMeters +
                CemeteryWatchmanPlan.AlleyStepMeters *
                CemeteryWatchmanPlan.AlleyStepMeters);
            Assert.That(
                toStep.magnitude,
                Is.EqualTo(expectedOffset).Within(0.001f));
            CityCemeteryPartDescriptor doorLeaf = plan.Parts.Single(
                part => part.StableId == "cemetery-lodge-door-leaf");
            Assert.That(
                ToXZRect(doorLeaf).Contains(new Vector2(
                    stance.Position.x,
                    stance.Position.z)),
                Is.False,
                "The ajar leaf never sweeps through him.");

            // The porch bulb hangs beside that door, so the old man
            // is lit rather than a silhouette.
            CityCemeteryLampDescriptor porch = plan.Lamps.Single(
                lamp => lamp.Kind == CityCemeteryLampKind.LodgePorch);
            Assert.That(
                Vector2.Distance(
                    new Vector2(
                        porch.GroundPosition.x,
                        porch.GroundPosition.z),
                    new Vector2(
                        stance.Position.x,
                        stance.Position.z)),
                Is.LessThan(1.3f),
                "The watchman stands in his porch lamp's pool.");

            // He watches the main alley, not the arch behind his own
            // booth: his heading runs along the rear wall, and the
            // gate side of the plot is the side he is turned to.
            CityCemeteryPartDescriptor rearWall = plan.Parts.Single(
                part => part.StableId == "cemetery-lodge-wall-rear");
            Vector3 doorNormal = rearWall.Size.x < rearWall.Size.z
                ? Vector3.right
                : Vector3.forward;
            Assert.That(
                Mathf.Abs(Vector3.Dot(stance.Facing, doorNormal)),
                Is.LessThan(0.001f),
                "He looks along the wall, across the plot.");

            CityCemeteryPartDescriptor arch = plan.Parts.First(
                part => part.StableId == "cemetery-gate-arch");
            Vector3 toArch = arch.Center - stance.Position;
            toArch.y = 0f;
            Assert.That(
                Vector3.Dot(stance.Facing, toArch.normalized),
                Is.GreaterThan(0.3f),
                "The gate and its alley lie on the side he faces.");

            // And the lodge is behind that line, not in it: he never
            // stares into his own wall.
            Assert.That(
                ToXZRect(lodgeBase).Contains(new Vector2(
                    stance.Position.x + stance.Facing.x * 2f,
                    stance.Position.z + stance.Facing.z * 2f)),
                Is.False,
                "Two metres along his gaze is still open ground.");
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

        [Test]
        public void Presentation_TurnsHisHeadAfterTheHeroInFrontAndNotBehind()
        {
            // The old man looks back: the hero's own notice rule run from
            // the doorstep. Behind him nothing happens; on his right the
            // face (measured from the eye bones, which ride the head)
            // swings right, on his left it swings left, and once the hero
            // walks off behind him the glance blends out to nothing.
            (CityLayout _, CityCemeteryPlan plan) = GenerateCemetery();
            CemeteryWatchmanPlan watchmanPlan =
                CemeteryWatchmanPlan.Create(plan);
            Assert.That(watchmanPlan.IsPresent, Is.True);
            CemeteryWatchmanStance stance = watchmanPlan.Stance;
            Vector3 right = Vector3.Cross(Vector3.up, stance.Facing).normalized;
            var root = new GameObject("Watchman Attention Root");
            try
            {
                var heroObject = new GameObject("Hero Root");
                heroObject.transform.SetParent(root.transform, false);
                Transform hero = heroObject.transform;
                hero.position = stance.Position - (stance.Facing * 2f);

                CemeteryWatchmanPresentation watchman =
                    CemeteryWatchmanFactory.Create(
                        root.transform,
                        watchmanPlan,
                        Seed,
                        hero);
                Assert.That(
                    watchman,
                    Is.Not.Null,
                    "The provider asset must resolve in edit mode.");
                Step(watchman, 40);
                Assert.That(watchman.IsAttending, Is.False);
                Assert.That(watchman.AttentionFocus, Is.Null);
                Assert.That(watchman.AttentionWeight, Is.EqualTo(0f));

                hero.position = stance.Position +
                                (stance.Facing * 1.5f) +
                                (right * 2f);
                Step(watchman, 40);
                Assert.That(watchman.IsAttending, Is.True);
                Assert.That(
                    watchman.AttentionWeight,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(watchman.AttentionFocus, Is.Not.Null);
                Assert.That(
                    Vector3.Distance(
                        watchman.AttentionFocus.Value,
                        hero.position +
                        (Vector3.up * HeroAttentionFocus.FallbackHeight)),
                    Is.LessThan(0.001f),
                    "A bare hero root is looked at face-high over its feet.");
                float rightYaw = FaceYaw(watchman, stance.Facing);
                Assert.That(
                    rightYaw,
                    Is.GreaterThan(15f),
                    "A hero on his right turns the face right.");

                hero.position = stance.Position +
                                (stance.Facing * 1.5f) -
                                (right * 2f);
                Step(watchman, 60);
                Assert.That(watchman.IsAttending, Is.True);
                float leftYaw = FaceYaw(watchman, stance.Facing);
                Assert.That(
                    leftYaw,
                    Is.LessThan(-15f),
                    "A hero on his left turns the face left.");
                Assert.That(rightYaw - leftYaw, Is.GreaterThan(45f));

                hero.position = stance.Position - (stance.Facing * 2f);
                Step(watchman, 40);
                Assert.That(
                    watchman.IsAttending,
                    Is.False,
                    "Straight behind him the hero is dropped.");
                Assert.That(watchman.AttentionWeight, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void Step(
            CemeteryWatchmanPresentation watchman,
            int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                watchman.Advance(0.02f);
            }
        }

        /// <summary>
        /// Where his face points relative to his stance, in the plane:
        /// from the head bone to the midpoint of the eye bones.
        /// </summary>
        private static float FaceYaw(
            CemeteryWatchmanPresentation watchman,
            Vector3 facing)
        {
            var registry = watchman
                .GetComponentInChildren<CityPedestrianAssetRegistry>(true);
            Transform bones = registry.Animator.transform;
            Transform leftEye =
                NpcAttentionHeadLayer.FindBone(bones, "face.eye.L");
            Transform rightEye =
                NpcAttentionHeadLayer.FindBone(bones, "face.eye.R");
            Assert.That(leftEye, Is.Not.Null);
            Assert.That(rightEye, Is.Not.Null);
            Vector3 eyes = (leftEye.position + rightEye.position) * 0.5f;
            Vector3 face = eyes - registry.HeadAnchor.position;
            face.y = 0f;
            facing.y = 0f;
            return Vector3.SignedAngle(facing, face, Vector3.up);
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
