using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SeacoastFishermanTests
    {
        private const int Seed = 20260818;

        private static CitySeacoastPlan GenerateCoast()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                Seed);
            CitySeacoastPlan plan = CitySeacoastPlanner.Create(layout);
            Assert.That(plan, Is.Not.Null,
                "The default city must carry a dressable coast.");
            return plan;
        }

        [Test]
        public void Plan_AbsentWithoutACoastOrAPier()
        {
            Assert.That(
                SeacoastFishermanPlan.Create(null).IsPresent,
                Is.False);
        }

        [Test]
        public void Plan_StandsHimAtTheEndBoardWithHisBackToTheShore()
        {
            CitySeacoastPlan coast = GenerateCoast();
            SeacoastFishermanPlan plan = SeacoastFishermanPlan.Create(coast);

            Assert.That(plan.IsPresent, Is.True);
            SeacoastFishermanStance stance = plan.Stance;

            // He stands on boards, not on water: his stance has to fall
            // inside a deck footprint, so a plan that moved the pier
            // can never leave him out over the pond.
            var footing = new Vector2(stance.Position.x, stance.Position.z);
            bool onDeck = coast.Parts
                .Where(part => part.Kind == CitySeacoastPartKind.PierDeck)
                .Any(part => new Rect(
                        part.Center.x - (part.Size.x + part.Size.z) * 0.5f,
                        part.Center.z - (part.Size.x + part.Size.z) * 0.5f,
                        part.Size.x + part.Size.z,
                        part.Size.x + part.Size.z)
                    .Contains(footing));
            Assert.That(onDeck, Is.True,
                "The fisherman must stand on the pier deck.");
            Assert.That(
                stance.Position.y,
                Is.GreaterThan(coast.Frame.SeaTopY + 0.4f),
                "He must stand above the water, not in it.");

            // Facing out along the pier, away from the bank: the whole
            // character is that the player arrives behind him.
            coast.TryGetPart(
                CitySeacoastPlanner.PierDeckHeadId,
                out CitySeacoastPartDescriptor head);
            coast.TryGetPart(
                CitySeacoastPlanner.PierDeckRootId,
                out CitySeacoastPartDescriptor root);
            Vector3 outward = head.Center - root.Center;
            outward.y = 0f;
            outward.Normalize();
            Assert.That(
                Vector3.Dot(stance.Facing.normalized, outward),
                Is.GreaterThan(0.9f),
                "He looks at the water, not at the shore.");

            // And he is leaning ON the end board, which means the board
            // has to be in front of him and within reach. A man a stride
            // short of a parapet is not leaning on anything, and one
            // past it is standing in the pond.
            Assert.That(
                coast.TryGetPart(
                    CitySeacoastPlanner.PierHeadBoardId,
                    out CitySeacoastPartDescriptor board),
                Is.True);
            float reach = Vector3.Dot(
                board.Center - stance.Position,
                outward);
            Assert.That(
                reach,
                Is.InRange(0.05f, 0.70f),
                "The end board must be just in front of his boots.");
        }

        [Test]
        public void Quips_AreDeterministicAndNeverRepeatBackToBack()
        {
            uint firstState = SeacoastFishermanQuips.CreateState(Seed);
            uint secondState = SeacoastFishermanQuips.CreateState(Seed);
            int previousFirst = -1;
            int previousSecond = -1;
            var seen = new HashSet<int>();
            int drawsUntilFullCoverage = -1;
            for (int draw = 0; draw < 200; draw++)
            {
                int first = SeacoastFishermanQuips.NextIndex(
                    ref firstState,
                    previousFirst);
                int second = SeacoastFishermanQuips.NextIndex(
                    ref secondState,
                    previousSecond);
                Assert.That(first, Is.EqualTo(second),
                    "The same seed serves the same repertoire.");
                Assert.That(first, Is.Not.EqualTo(previousFirst),
                    "He never answers the same way twice running.");
                Assert.That(
                    first,
                    Is.InRange(
                        0,
                        SeacoastFishermanQuips.LineKeys.Length - 1));
                previousFirst = first;
                previousSecond = second;
                seen.Add(first);
                if (drawsUntilFullCoverage < 0 &&
                    seen.Count == SeacoastFishermanQuips.LineKeys.Length)
                {
                    drawsUntilFullCoverage = draw + 1;
                }
            }

            Assert.That(drawsUntilFullCoverage, Is.InRange(1, 200),
                "The whole repertoire comes up in ordinary play.");
            Assert.That(
                SeacoastFishermanQuips.LineKeys.Distinct().Count(),
                Is.EqualTo(SeacoastFishermanQuips.LineKeys.Length));
        }

        [Test]
        public void Quips_KeysExistInBothLocalizationCatalogs()
        {
            foreach (string language in new[] { "ru", "en" })
            {
                TextAsset catalog = Resources.Load<TextAsset>(
                    $"Localization/{language}");
                Assert.That(catalog, Is.Not.Null);
                foreach (string key in SeacoastFishermanQuips.LineKeys)
                {
                    Assert.That(
                        catalog.text.Contains($"\"{key}\""),
                        Is.True,
                        $"{language}.json is missing '{key}'.");
                }

                Assert.That(
                    catalog.text.Contains(
                        $"\"{SeacoastFishermanInteraction.TalkPromptKey}\""),
                    Is.True,
                    $"{language}.json is missing the talk prompt.");
            }
        }


        /// <summary>
        /// The line has to end in the water, and only the coast frame
        /// knows where the water is: the sea's top is plan data, so a
        /// depth guessed anywhere else would be a depth for a sea that
        /// is not there.
        /// </summary>
        [Test]
        public void Plan_ReadsTheWaterlineOffTheCoastItSitsOn()
        {
            CitySeacoastPlan coast = GenerateCoast();
            SeacoastFishermanPlan plan = SeacoastFishermanPlan.Create(coast);

            Assert.That(plan.IsPresent, Is.True);
            Assert.That(
                plan.WaterTopY,
                Is.EqualTo(coast.Frame.SeaTopY).Within(0.0001f));
            Assert.That(
                plan.Stance.Position.y,
                Is.GreaterThan(plan.WaterTopY),
                "He sits on boards over the water, not in it.");
            Assert.That(
                SeacoastFishermanPlan.Create(null).WaterTopY,
                Is.EqualTo(0f));
        }

        /// <summary>
        /// The whole point of the pipe: the ember and the smoke are read
        /// off the clip that is moving his chest, so four breaths of
        /// animation are four breaths of smoke. This proves the mapping
        /// the art build's key grid promises.
        /// </summary>
        [Test]
        public void Breath_RunsFourClosedCyclesPerLoopAndPeaksMidBreath()
        {
            Assert.That(
                SeacoastFishermanPresentation.BreathsPerLoop,
                Is.EqualTo(4));

            // Exhaled at every quarter of the loop, full at every eighth
            // between them: exactly where FishermanLean keys them.
            for (int quarter = 0; quarter < 4; quarter++)
            {
                float rest = quarter / 4f;
                float draw = rest + 0.125f;
                Assert.That(
                    SeacoastFishermanPresentation.BreathAmountAt(
                        SeacoastFishermanPresentation.BreathPhaseAt(rest)),
                    Is.EqualTo(0f).Within(0.0001f),
                    $"The chest must be closed at {rest:0.###}.");
                Assert.That(
                    SeacoastFishermanPresentation.BreathAmountAt(
                        SeacoastFishermanPresentation.BreathPhaseAt(draw)),
                    Is.EqualTo(1f).Within(0.0001f),
                    $"The chest must be open at {draw:0.###}.");
            }

            // And it closes on itself: a loop that ended mid-breath
            // would jerk the ember once per lap.
            Assert.That(
                SeacoastFishermanPresentation.BreathPhaseAt(1f),
                Is.EqualTo(SeacoastFishermanPresentation.BreathPhaseAt(0f))
                    .Within(0.0001f));

            int rises = 0;
            float previous = SeacoastFishermanPresentation.BreathAmountAt(
                SeacoastFishermanPresentation.BreathPhaseAt(0f));
            bool climbing = false;
            for (int step = 1; step <= 960; step++)
            {
                float amount = SeacoastFishermanPresentation.BreathAmountAt(
                    SeacoastFishermanPresentation.BreathPhaseAt(step / 960f));
                if (amount > previous && !climbing)
                {
                    climbing = true;
                    rises++;
                }
                else if (amount < previous)
                {
                    climbing = false;
                }

                Assert.That(amount, Is.InRange(0f, 1f));
                previous = amount;
            }

            Assert.That(
                rises,
                Is.EqualTo(SeacoastFishermanPresentation.BreathsPerLoop),
                "One lap of the loop is four draws on the pipe.");
        }

        /// <summary>
        /// The plume is the same curve, one beat late. Emitting it in
        /// phase with the ribs is the mistake this test exists to stop:
        /// smoke that swells while the chest is still filling reads as a
        /// particle system, not as smoking.
        /// </summary>
        [Test]
        public void Plume_FollowsTheBreathButLagsBehindIt()
        {
            Assert.That(
                SeacoastFishermanPipeEffect.PlumeBreathLag,
                Is.GreaterThan(0f));

            float peakPhase = 0f;
            float peakRate = float.MinValue;
            for (int step = 0; step < 720; step++)
            {
                float phase = step / 720f;
                float rate = SeacoastFishermanPipeEffect.PlumeRateAt(phase);
                Assert.That(
                    rate,
                    Is.InRange(
                        SeacoastFishermanPipeEffect.PlumeRestRate,
                        SeacoastFishermanPipeEffect.PlumeDrawRate));
                if (rate > peakRate)
                {
                    peakRate = rate;
                    peakPhase = phase;
                }
            }

            float expected =
                SeacoastFishermanPresentation.InhalePeakPhase +
                SeacoastFishermanPipeEffect.PlumeBreathLag;
            Assert.That(peakPhase, Is.EqualTo(expected).Within(0.01f));
            Assert.That(
                SeacoastFishermanPipeEffect.PlumeRateAt(
                    SeacoastFishermanPresentation.InhalePeakPhase),
                Is.LessThan(peakRate),
                "The plume must still be rising when the chest is full.");
        }

        /// <summary>
        /// The staged art actually reaches a build, is the loop the
        /// presentation expects, ships empty-handed, and takes the rod
        /// and the pipe as hand props whose anchors land on the right
        /// bones. Since 2026-09-05 neither the rod nor the pipe is part
        /// of the body: an anchor measured on the wrong socket still
        /// resolves at runtime and leaves the ember floating beside his
        /// head, so the distances are judged in world space here.
        /// </summary>
        [Test]
        public void StagedPrefab_IsBoundPassiveAndTakesItsPipeAndRodProps()
        {
            SeacoastFishermanProvider provider = SeacoastFishermanProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                $"Missing provider at Resources/" +
                $"{SeacoastFishermanProvider.ResourcePath}.");
            GameObject prefab = provider.StagedPrefab;
            Assert.That(prefab, Is.Not.Null);

            var registry =
                prefab.GetComponentInChildren<CityPedestrianAssetRegistry>(
                    true);
            Assert.That(registry, Is.Not.Null);
            Assert.That(
                registry.DesignId,
                Is.EqualTo(SeacoastFishermanProvider.DesignId));
            Assert.That(registry.IdleClip, Is.Not.Null);
            Assert.That(registry.IdleClip.name, Does.Contain("FishermanLean"));
            Assert.That(registry.IdleClip.isLooping, Is.True);
            Assert.That(
                registry.IdleClip.length,
                Is.EqualTo(8f).Within(0.05f),
                "The leaning loop's length is what the breath grid " +
                "divides; a different length is a different rhythm.");
            Assert.That(
                registry.SitClip,
                Is.Null,
                "He does not ride the bus.");

            // Passive: the ember light, the plume and the talk stub are
            // all raised by the factory, never authored into the art.
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<ParticleSystem>(true),
                Is.Empty);

            // Empty-handed: the rod and the pipe are hand-prop prefabs,
            // and a renderer with one of their part names on the body
            // is the old skinned prop back in the FBX.
            string[] propParts =
            {
                "ACC_RodGrip",
                "ACC_RodReel",
                "ACC_RodButt",
                "ACC_RodMid",
                "ACC_RodTip",
                "ACC_PipeStem",
                "ACC_PipeBowl",
                "ACC_PipeEmber"
            };
            var rendererNames = new HashSet<string>();
            foreach (Renderer renderer in
                     prefab.GetComponentsInChildren<Renderer>(true))
            {
                rendererNames.Add(renderer.name);
            }

            foreach (string part in propParts)
            {
                Assert.That(
                    rendererNames.Contains(part),
                    Is.False,
                    $"The fisherman body still carries '{part}'.");
            }

            var parent = new GameObject("Fisherman Hand Prop Test");
            try
            {
                GameObject instance = Object.Instantiate(
                    prefab,
                    parent.transform);
                var body = instance
                    .GetComponentInChildren<CityPedestrianAssetRegistry>(
                        true);
                Assert.That(body, Is.Not.Null);
                Transform head = CityPedestrianHandProps.FindSocket(
                    body.ModelRoot,
                    "head");
                Transform rightHand = CityPedestrianHandProps.FindSocket(
                    body.ModelRoot,
                    "hand.R");
                Assert.That(head, Is.Not.Null);
                Assert.That(rightHand, Is.Not.Null);

                CityPedestrianHandPropRegistry pipe =
                    CityPedestrianHandProps.Attach(
                        body,
                        CityPedestrianHandPropId.SmokingPipe);
                CityPedestrianHandPropRegistry rod =
                    CityPedestrianHandProps.Attach(
                        body,
                        CityPedestrianHandPropId.FishingRod);
                Assert.That(
                    pipe.transform.parent.name,
                    Is.EqualTo(CityPedestrianHandProps.MouthSocketName));
                Assert.That(
                    rod.transform.parent.name,
                    Is.EqualTo(CityPedestrianHandProps.GripRightSocketName));

                Renderer emberRenderer = pipe.FindRenderer(
                    SeacoastFishermanFactory.PipeEmberRendererName);
                Assert.That(
                    emberRenderer,
                    Is.Not.Null,
                    "The pipe effect swaps this renderer's material.");
                Assert.That(emberRenderer, Is.InstanceOf<MeshRenderer>());

                Transform ember = pipe.RequireAnchor(
                    CityPedestrianHandProps.PipeEmberAnchorName);
                Transform rodTip = rod.RequireAnchor(
                    CityPedestrianHandProps.RodTipAnchorName);

                // The bowl is in his teeth, so its anchor is a hand's
                // breadth from the head bone and no further.
                Assert.That(
                    Vector3.Distance(ember.position, head.position),
                    Is.InRange(0.10f, 0.40f));

                // And the rod is a two-metre stick: an anchor that landed
                // on the grip would still look plausible in the inspector.
                Assert.That(
                    Vector3.Distance(rodTip.position, rightHand.position),
                    Is.InRange(1.4f, 2.6f));
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        /// <summary>
        /// He is not a second watchman. The register is a rule, not a
        /// mood, so it is checked: never the second person, never a
        /// question, and short.
        /// </summary>
        [Test]
        public void Quips_KeepTheirRegisterAndNeverAddressThePlayer()
        {
            TextAsset catalog = Resources.Load<TextAsset>(
                "Localization/ru");
            Assert.That(catalog, Is.Not.Null);

            string[] forbidden = { " ты ", " тебе ", " тебя ", " твой " };
            foreach (string key in SeacoastFishermanQuips.LineKeys)
            {
                int at = catalog.text.IndexOf($"\"{key}\"");
                Assert.That(at, Is.GreaterThanOrEqualTo(0));
                int valueAt = catalog.text.IndexOf(
                    "\"value\"", at);
                int open = catalog.text.IndexOf('"', valueAt + 7) + 1;
                int close = catalog.text.IndexOf('"', open);
                string line = catalog.text.Substring(open, close - open);

                Assert.That(line.Length, Is.LessThanOrEqualTo(55),
                    $"'{key}' runs long for this man: {line}");
                Assert.That(line.Contains("?"), Is.False,
                    $"'{key}' asks the player something: {line}");
                string padded = $" {line.ToLowerInvariant()} ";
                foreach (string word in forbidden)
                {
                    Assert.That(
                        padded.Contains(word),
                        Is.False,
                        $"'{key}' addresses the player: {line}");
                }
            }
        }
    }
}
