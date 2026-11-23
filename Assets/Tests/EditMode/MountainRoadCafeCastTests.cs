using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class MountainRoadCafeCastTests
    {
        private static readonly string[] StableIds =
        {
            MountainRoadCafeWorldBuilder.LonePatronAnchorId,
            MountainRoadCafeWorldBuilder.PairFirstAnchorId,
            MountainRoadCafeWorldBuilder.PairSecondAnchorId,
            MountainRoadCafeWorldBuilder.AttendantAnchorId
        };

        [Test]
        [Category("MountainRoad")]
        public void Plan_OwnsFourUniqueRolesAndDeliberateEmptyStoolGap()
        {
            MountainRoadCafePlan cafe = CreateCafePlan();
            MountainRoadCafeCastPlan plan =
                MountainRoadCafeCastPlan.Create(cafe);

            Assert.That(
                plan.Members,
                Has.Count.EqualTo(
                    MountainRoadCafeWorldBuilder.TableauNpcCount));
            Assert.That(
                plan.Members.Select(member => member.Role).Distinct().Count(),
                Is.EqualTo(4));
            Assert.That(
                plan.Members.Select(member => member.StableId)
                    .Distinct(StringComparer.Ordinal).Count(),
                Is.EqualTo(4));
            CollectionAssert.AreEquivalent(
                StableIds,
                plan.Members.Select(member => member.StableId));

            var expected = new Dictionary<
                MountainRoadCafeCastRole,
                Vector2>
            {
                {
                    MountainRoadCafeCastRole.LonePatron,
                    new Vector2(-1.50f, -2.18f)
                },
                {
                    MountainRoadCafeCastRole.PairMan,
                    new Vector2(0.75f, -2.18f)
                },
                {
                    MountainRoadCafeCastRole.PairWoman,
                    new Vector2(1.80f, -2.18f)
                },
                {
                    MountainRoadCafeCastRole.Attendant,
                    new Vector2(2.10f, -0.16f)
                }
            };
            var expectedStableIds = new Dictionary<
                MountainRoadCafeCastRole,
                string>
            {
                {
                    MountainRoadCafeCastRole.LonePatron,
                    MountainRoadCafeWorldBuilder.LonePatronAnchorId
                },
                {
                    MountainRoadCafeCastRole.PairMan,
                    MountainRoadCafeWorldBuilder.PairFirstAnchorId
                },
                {
                    MountainRoadCafeCastRole.PairWoman,
                    MountainRoadCafeWorldBuilder.PairSecondAnchorId
                },
                {
                    MountainRoadCafeCastRole.Attendant,
                    MountainRoadCafeWorldBuilder.AttendantAnchorId
                }
            };

            foreach (MountainRoadCafeCastMemberPlan member in plan.Members)
            {
                Assert.That(
                    member.StableId,
                    Is.EqualTo(expectedStableIds[member.Role]));
                Vector3 offset = member.Position - cafe.Center;
                var local = new Vector2(
                    Vector3.Dot(offset, cafe.Right),
                    Vector3.Dot(offset, cafe.Forward));
                Assert.That(
                    local.x,
                    Is.EqualTo(expected[member.Role].x).Within(0.001f));
                Assert.That(
                    local.y,
                    Is.EqualTo(expected[member.Role].y).Within(0.001f));
                Assert.That(member.Facing.y, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(
                    member.Facing.magnitude,
                    Is.EqualTo(1f).Within(0.0001f));
            }

            float loneRight = LocalRight(
                cafe,
                Find(plan, MountainRoadCafeCastRole.LonePatron));
            float pairManRight = LocalRight(
                cafe,
                Find(plan, MountainRoadCafeCastRole.PairMan));
            float pairWomanRight = LocalRight(
                cafe,
                Find(plan, MountainRoadCafeCastRole.PairWoman));
            Assert.That(
                pairManRight - loneRight,
                Is.EqualTo(2.25f).Within(0.001f),
                "The empty stool is authored negative space, not a fifth " +
                "cast slot.");
            Assert.That(
                pairWomanRight - pairManRight,
                Is.EqualTo(1.05f).Within(0.001f),
                "The couple must read as one close composition after the " +
                "deliberate gap.");
        }

        [Test]
        [Category("MountainRoad")]
        public void Provider_BindsCafeClipsToFourAllocatedResidents()
        {
            var provider = MountainRoadCafeCastProvider.Load();
            Assert.That(provider.HasCompleteCast, Is.True);
            var parent = new GameObject("Cafe wardrobe and animation proof");
            try
            {
                var signatures = new HashSet<string>();
                foreach (MountainRoadCafeCastRole role in Enum.GetValues(typeof(MountainRoadCafeCastRole)))
                {
                    var body = MountainRoadCafeCastFactory.CreateBody(parent.transform, role, provider);
                    var assignment = DefaultNpcPopulation.GetAssignment(DefaultNpcPopulation.CafeCharacterId(role));
                    Assert.That(signatures.Add(assignment.VisibleSignature), Is.True);
                    Assert.That(body.GetComponent<NpcWardrobe>().EquippedItemIds, Is.EquivalentTo(assignment.ItemIds));
                    Assert.That(body.GetComponent<VillageResidentPresentation>().IsInitialized, Is.False);
                    Assert.That(body.ClipBindings.Count, Is.EqualTo(role == MountainRoadCafeCastRole.Attendant ? 7 : 2));
                    Assert.That(body.ClipBindings.All(binding => binding.Clip != null), Is.True);
                    Assert.That(body.FindModelTransform("SOCKET_Mouth"), Is.Not.Null);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(parent); }
        }

        [Test]
        [Category("MountainRoad")]
        public void BuiltCafe_ContainsFourAllocatedFiguresOnTheirMarks()
        {
            var parent = new GameObject("Cafe Cast Test Parent");
            try
            {
                MountainRoadCafeWorldResult result =
                    MountainRoadCafeWorldBuilder.Build(
                        parent.transform,
                        CreateCafePlan());
                MountainRoadCafeCastPresentation[] presentations =
                    result.NpcRoot.GetComponentsInChildren<
                        MountainRoadCafeCastPresentation>(true);

                Assert.That(presentations, Has.Length.EqualTo(4));
                Assert.That(
                    presentations.All(presentation =>
                        presentation.IsInitialized &&
                        presentation.Registry != null),
                    Is.True);
                Assert.That(
                    presentations.Select(presentation => presentation.Role)
                        .Distinct().Count(),
                    Is.EqualTo(4));
                Assert.That(
                    result.NpcRoot.GetComponentsInChildren<
                        CityPedestrianPresentation>(true),
                    Is.Empty,
                    "The cafe cannot reuse the ambient pedestrian runtime.");
                Assert.That(
                    result.NpcRoot.GetComponentsInChildren<
                        CityPedestrianAssetRegistry>(true),
                    Is.Empty,
                    "Generic pedestrian assets must not leak into the " +
                    "authored tableau.");
                Assert.That(
                    result.NpcRoot.GetComponentsInChildren<Transform>(true)
                        .Any(transform => transform.name.IndexOf(
                            "fallback",
                            StringComparison.OrdinalIgnoreCase) >= 0),
                    Is.False);

                MountainRoadCafeCastController[] controllers =
                    result.NpcRoot.GetComponentsInChildren<
                        MountainRoadCafeCastController>(true);
                Assert.That(controllers, Has.Length.EqualTo(1));
                Assert.That(controllers[0].IsInitialized, Is.True);
                Assert.That(
                    controllers[0].ActiveEpisode,
                    Is.EqualTo(MountainRoadCafeCastEpisode.None));
                Assert.That(
                    controllers[0].NextEpisodeSeconds,
                    Is.InRange(
                        MountainRoadCafeCastController
                            .MinimumEpisodeDelaySeconds,
                        MountainRoadCafeCastController
                            .MaximumEpisodeDelaySeconds));
                Assert.That(result.Model, Is.Not.Null);
                Assert.That(
                    result.Collision.ColliderCount,
                    Is.EqualTo(
                        MountainRoadCafeCollisionWorldBuilder
                            .ExpectedColliderCount));
                Assert.That(result.Service, Is.Not.Null);
                Assert.That(result.Service.IsConfigured, Is.True);
                Assert.That(result.Service.IncludesHeroCup, Is.False);
                Assert.That(
                    controllers[0].ServicePresentation,
                    Is.SameAs(result.Service));
                Assert.That(
                    controllers[0].TryGetCup(
                        MountainRoadCafeCastRole.LonePatron,
                        out _),
                    Is.False,
                    "The sleeping door-side patron must own no cup.");
                Assert.That(
                    controllers[0].TryGetCup(
                        MountainRoadCafeCastRole.PairMan,
                        out _),
                    Is.True);
                Assert.That(
                    controllers[0].TryGetCup(
                        MountainRoadCafeCastRole.PairWoman,
                        out _),
                    Is.True);

                for (int index = 0; index < StableIds.Length; index++)
                {
                    string stableId = StableIds[index];
                    Assert.That(
                        result.SemanticAnchors.TryGetValue(
                            stableId,
                            out Transform anchor),
                        Is.True,
                        $"Cafe semantic anchor '{stableId}' is missing.");
                    Assert.That(anchor, Is.Not.Null);
                    Assert.That(
                        anchor.GetComponentsInChildren<
                            MountainRoadCafeCastPresentation>(true),
                        Has.Length.EqualTo(1),
                        $"Cafe semantic anchor '{stableId}' does not own " +
                        "exactly one cafe figure.");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        [Category("MountainRoad")]
        public void LoneInterjection_RequiresPairReservationAndReturnsToIdle()
        {
            var parent = new GameObject("Cafe Lone Interjection Test");
            try
            {
                MountainRoadCafeWorldResult result =
                    MountainRoadCafeWorldBuilder.Build(
                        parent.transform,
                        CreateCafePlan());
                MountainRoadCafeCastController controller = result.Cast;
                MountainRoadCafeCastPresentation lone = result.NpcRoot
                    .GetComponentsInChildren<
                        MountainRoadCafeCastPresentation>(true)
                    .Single(presentation =>
                        presentation.Role ==
                        MountainRoadCafeCastRole.LonePatron);

                Assert.That(
                    controller.TryBeginLonePatronInterjection(),
                    Is.False,
                    "The lone beat cannot pre-empt an unreserved pair.");
                Assert.That(controller.TryReservePairConversation(), Is.True);
                Assert.That(
                    controller.TryBeginLonePatronInterjection(),
                    Is.True);
                Assert.That(
                    controller.TryBeginLonePatronInterjection(),
                    Is.False,
                    "The one-shot cannot be started twice.");
                Assert.That(controller.IsLonePatronInterjecting, Is.True);
                Assert.That(
                    lone.CurrentClipKind,
                    Is.EqualTo(MountainRoadCafeCastClipKind.Interject));

                float duration =
                    controller.LonePatronInterjectionDurationSeconds;
                Assert.That(duration, Is.GreaterThan(0f));
                float firstStep = duration * 0.5f;
                Assert.That(
                    controller.AdvanceLonePatronInterjection(firstStep),
                    Is.True);
                Assert.That(
                    controller.LonePatronInterjectionElapsedSeconds,
                    Is.EqualTo(firstStep).Within(0.0001f));
                Assert.That(
                    lone.CurrentClipKind,
                    Is.EqualTo(MountainRoadCafeCastClipKind.Interject));
                Assert.That(
                    lone.CurrentClipTimeSeconds,
                    Is.EqualTo(firstStep).Within(0.0001f));

                Assert.That(
                    controller.AdvanceLonePatronInterjection(
                        duration - firstStep),
                    Is.False,
                    "The completed one-shot reports no active remainder.");
                Assert.That(controller.IsLonePatronInterjecting, Is.False);
                Assert.That(
                    lone.CurrentClipKind,
                    Is.EqualTo(MountainRoadCafeCastClipKind.Idle));
                Assert.That(
                    controller.IsPairConversationReserved,
                    Is.True,
                    "The conversation owns its reservation until its caller " +
                    "finishes the interruption.");
                Assert.That(controller.ReleasePairConversation(), Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [Test]
        [Category("MountainRoad")]
        public void BuiltCafe_ArmsNearPlayerAndPresentsDrinkDrainAndRefill()
        {
            var parent = new GameObject("Cafe Service Regression Parent");
            var observer = new GameObject("Cafe Service Observer");
            observer.transform.SetParent(parent.transform, false);
            try
            {
                MountainRoadCafeWorldResult result =
                    MountainRoadCafeWorldBuilder.Build(
                        parent.transform,
                        CreateCafePlan());
                MountainRoadCafeCastController controller = result.Cast;
                observer.transform.position = result.Entrance.position +
                    Vector3.right *
                    (MountainRoadCafeCastController.ActivationRadius + 1f);

                Assert.That(controller.IsTimelineArmed, Is.False);
                Assert.That(
                    controller.BindActivationObserver(
                        observer.transform,
                        result.Entrance.position),
                    Is.True);
                controller.Advance(
                    MountainRoadCafeCastController
                        .MaximumEpisodeDelaySeconds + 1f);
                Assert.That(controller.IsTimelineArmed, Is.False);
                Assert.That(controller.ElapsedSeconds, Is.Zero);

                observer.transform.position = result.Entrance.position;
                controller.Advance(0f);
                Assert.That(controller.IsTimelineArmed, Is.True);
                Assert.That(controller.ElapsedSeconds, Is.Zero);
                Assert.That(
                    controller.TryGetCup(
                        MountainRoadCafeCastRole.PairMan,
                        out MountainRoadCafeCupView pairManCup),
                    Is.True);
                Assert.That(
                    controller.TryGetCup(
                        MountainRoadCafeCastRole.PairWoman,
                        out MountainRoadCafeCupView pairWomanCup),
                    Is.True);
                Assert.That(
                    pairManCup.Fill01,
                    Is.EqualTo(
                        MountainRoadCafeServiceTimeline.InitialPairManFill));
                Assert.That(
                    pairWomanCup.Fill01,
                    Is.EqualTo(
                        MountainRoadCafeServiceTimeline
                            .InitialPairWomanFill));
                Assert.That(pairManCup.LiquidRenderer.enabled, Is.True);
                Assert.That(pairWomanCup.LiquidRenderer.enabled, Is.True);

                Assert.That(
                    controller.TryRequestEpisode(
                        MountainRoadCafeCastEpisode.Couple),
                    Is.True);
                controller.Advance(
                    MountainRoadCafeServiceTimeline.PairPatronDrinkSeconds *
                    0.55f);
                Assert.That(
                    controller.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.CoupleDrink));
                Assert.That(pairManCup.Fill01,
                    Is.EqualTo(0.36f).Within(0.0001f));
                Assert.That(pairWomanCup.Fill01,
                    Is.EqualTo(
                        MountainRoadCafeServiceTimeline.InitialPairWomanFill)
                        .Within(0.0001f));

                MountainRoadCafeCastPresentation[] presentations =
                    result.NpcRoot.GetComponentsInChildren<
                        MountainRoadCafeCastPresentation>(true);
                Assert.That(
                    presentations.Single(presentation =>
                            presentation.Role ==
                            MountainRoadCafeCastRole.PairMan)
                        .CurrentClipKind,
                    Is.EqualTo(MountainRoadCafeCastClipKind.Drink));
                Assert.That(
                    presentations.Single(presentation =>
                            presentation.Role ==
                            MountainRoadCafeCastRole.PairWoman)
                        .CurrentClipKind,
                    Is.EqualTo(MountainRoadCafeCastClipKind.Idle));
                Assert.That(
                    Vector3.Distance(
                        pairManCup.GripAnchor.position,
                        controller.GetCupHandSocket(
                            MountainRoadCafeCastRole.PairMan).position),
                    Is.LessThanOrEqualTo(0.005f));
                float untilWomanMidSip =
                    MountainRoadCafeServiceTimeline
                        .PairWomanDrinkStartSeconds +
                    MountainRoadCafeServiceTimeline
                        .PairPatronDrinkSeconds * 0.55f -
                    controller.ServiceFrame.PhaseElapsedSeconds;
                controller.Advance(untilWomanMidSip);
                Assert.That(pairManCup.Fill01,
                    Is.EqualTo(0.28f).Within(0.0001f));
                Assert.That(pairWomanCup.Fill01,
                    Is.EqualTo(0.47f).Within(0.0001f));
                Assert.That(
                    presentations.Single(presentation =>
                            presentation.Role ==
                            MountainRoadCafeCastRole.PairMan)
                        .CurrentClipKind,
                    Is.EqualTo(MountainRoadCafeCastClipKind.Idle));
                Assert.That(
                    presentations.Single(presentation =>
                            presentation.Role ==
                            MountainRoadCafeCastRole.PairWoman)
                        .CurrentClipKind,
                    Is.EqualTo(MountainRoadCafeCastClipKind.Drink));
                Assert.That(
                    Vector3.Distance(
                        pairWomanCup.GripAnchor.position,
                        controller.GetCupHandSocket(
                            MountainRoadCafeCastRole.PairWoman).position),
                    Is.LessThanOrEqualTo(0.005f));

                controller.Advance(
                    controller.ServiceFrame.PhaseDurationSeconds -
                    controller.ServiceFrame.PhaseElapsedSeconds);
                Assert.That(
                    controller.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.Notice));
                Assert.That(
                    controller.ServiceFrame.ServiceTarget,
                    Is.EqualTo(MountainRoadCafeCastRole.PairMan));
                controller.Advance(
                    MountainRoadCafeServiceTimeline.NoticeSeconds +
                    MountainRoadCafeServiceTimeline.WalkSeconds);
                Assert.That(
                    controller.ServiceFrame.Phase,
                    Is.EqualTo(MountainRoadCafeServicePhase.Pour));
                float fillBeforePour = pairManCup.Fill01;
                controller.Advance(
                    MountainRoadCafeServiceTimeline.PourSeconds * 0.55f);
                Assert.That(pairManCup.Fill01, Is.GreaterThan(fillBeforePour));
                Assert.That(pairManCup.Fill01, Is.LessThan(0.90f));
                Assert.That(
                    presentations.Single(presentation =>
                            presentation.Role ==
                            MountainRoadCafeCastRole.Attendant)
                        .CurrentClipKind,
                    Is.EqualTo(MountainRoadCafeCastClipKind.Pour));
                Assert.That(
                    result.Model.TryGetProp(
                        "PourStream",
                        out MountainRoadCafeDynamicPropBinding stream),
                    Is.True);
                Assert.That(stream.Renderers.Single().enabled, Is.True);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(parent);
            }
        }

        [TestCase(-0.01f, 2f, 0f)]
        [TestCase(float.NaN, 2f, 0f)]
        [TestCase(0f, 0f, 0f)]
        [TestCase(0f, 2f, 0f)]
        [TestCase(0.09f, 2f, 0.5f)]
        [TestCase(0.18f, 2f, 1f)]
        [TestCase(2f, 2f, 1f)]
        [TestCase(2.16f, 2f, 0.5f)]
        [TestCase(2.32f, 2f, 0f)]
        [TestCase(2.40f, 2f, 0f)]
        [Category("MountainRoad")]
        public void ResolveBeatWeight_UsesBoundedBlendEnvelope(
            float elapsedSeconds,
            float clipLengthSeconds,
            float expectedWeight)
        {
            Assert.That(
                MountainRoadCafeCastPresentation.ResolveBeatWeight(
                    elapsedSeconds,
                    clipLengthSeconds),
                Is.EqualTo(expectedWeight).Within(0.0001f));
        }

        [Test]
        [Category("MountainRoad")]
        public void ServiceTimeline_PairUsesStaggeredClocksAndIndependentFill()
        {
            var timeline = new MountainRoadCafeServiceTimeline(42);

            Assert.That(
                timeline.TryRequestDrink(
                    MountainRoadCafeCastRole.PairMan),
                Is.True);
            timeline.Advance(
                MountainRoadCafeServiceTimeline.PairPatronDrinkSeconds *
                0.55f);
            MountainRoadCafeServiceFrame frame = timeline.Frame;
            Assert.That(
                frame.IsDrinking(MountainRoadCafeCastRole.PairMan),
                Is.True);
            Assert.That(
                frame.IsDrinking(MountainRoadCafeCastRole.PairWoman),
                Is.False);
            Assert.That(frame.PairManFill,
                Is.EqualTo(0.36f).Within(0.0001f));
            Assert.That(frame.PairWomanFill,
                Is.EqualTo(
                    MountainRoadCafeServiceTimeline.InitialPairWomanFill)
                    .Within(0.0001f));

            float untilWomanMidSip =
                MountainRoadCafeServiceTimeline.PairWomanDrinkStartSeconds +
                MountainRoadCafeServiceTimeline.PairPatronDrinkSeconds *
                0.55f -
                frame.PhaseElapsedSeconds;
            timeline.Advance(
                untilWomanMidSip);

            frame = timeline.Frame;
            Assert.That(
                frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.CoupleDrink));
            Assert.That(
                frame.IsDrinking(MountainRoadCafeCastRole.PairMan),
                Is.False);
            Assert.That(
                frame.IsDrinking(MountainRoadCafeCastRole.PairWoman),
                Is.True);
            Assert.That(frame.PairManFill, Is.EqualTo(0.28f).Within(0.0001f));
            Assert.That(frame.PairWomanFill,
                Is.EqualTo(0.47f).Within(0.0001f));
            timeline.Advance(
                frame.PhaseDurationSeconds - frame.PhaseElapsedSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Notice));
            Assert.That(
                timeline.Frame.ServiceTarget,
                Is.EqualTo(MountainRoadCafeCastRole.PairMan),
                "Only the cup below the threshold should enter service.");
            Assert.That(timeline.Frame.PairWomanFill,
                Is.EqualTo(0.38f).Within(0.0001f));

            timeline.Advance(
                MountainRoadCafeServiceTimeline.NoticeSeconds +
                MountainRoadCafeServiceTimeline.WalkSeconds +
                MountainRoadCafeServiceTimeline.PourSeconds +
                MountainRoadCafeServiceTimeline.WalkSeconds);
            Assert.That(
                timeline.TryRequestDrink(
                    MountainRoadCafeCastRole.PairWoman),
                Is.True);
            timeline.Advance(
                MountainRoadCafeServiceTimeline.CoupleDrinkSeconds);
            Assert.That(
                timeline.Frame.ServiceTarget,
                Is.EqualTo(MountainRoadCafeCastRole.PairWoman),
                "The next independently emptied cup should be serviced " +
                "without refilling the man's cup again.");
            Assert.That(timeline.Frame.PairManFill,
                Is.EqualTo(0.74f).Within(0.0001f));
            Assert.That(timeline.Frame.PairWomanFill,
                Is.EqualTo(0.20f).Within(0.0001f));
        }

        [Test]
        [Category("MountainRoad")]
        public void ServiceTimeline_SleepingLonePatronNeverOwnsACupOrDrink()
        {
            var timeline = new MountainRoadCafeServiceTimeline(42);

            Assert.That(
                MountainRoadCafeServiceTimeline.IsPatronWithCup(
                    MountainRoadCafeCastRole.LonePatron),
                Is.False);
            Assert.That(
                timeline.TryRequestDrink(
                    MountainRoadCafeCastRole.LonePatron),
                Is.False);
            Assert.That(
                timeline.Frame.IsDrinking(
                    MountainRoadCafeCastRole.LonePatron),
                Is.False);
            Assert.That(
                () => timeline.Frame.GetFill(
                    MountainRoadCafeCastRole.LonePatron),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        [Category("MountainRoad")]
        public void ServiceTimeline_LargeStepMatchesFineSteps()
        {
            var hitched = new MountainRoadCafeServiceTimeline(117);
            var stepped = new MountainRoadCafeServiceTimeline(117);
            const float duration = 240f;

            hitched.Advance(duration);
            for (int index = 0; index < 960; index++)
            {
                stepped.Advance(0.25f);
            }

            MountainRoadCafeServiceFrame expected = stepped.Frame;
            MountainRoadCafeServiceFrame actual = hitched.Frame;
            Assert.That(actual.Phase, Is.EqualTo(expected.Phase));
            Assert.That(actual.Sequence, Is.EqualTo(expected.Sequence));
            Assert.That(
                actual.PhaseElapsedSeconds,
                Is.EqualTo(expected.PhaseElapsedSeconds).Within(0.002f));
            Assert.That(actual.PairManFill, Is.EqualTo(expected.PairManFill).Within(0.0001f));
            Assert.That(actual.PairWomanFill, Is.EqualTo(expected.PairWomanFill).Within(0.0001f));
        }

        [Test]
        [Category("MountainRoad")]
        public void ServiceTimeline_HeroNoticeNeverCreatesAnotherServiceTarget()
        {
            var timeline = new MountainRoadCafeServiceTimeline(91);

            Assert.That(MountainRoadCafeServiceTimeline.ServesHero, Is.False);
            Assert.That(timeline.TryRequestHeroNotice(), Is.True);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Notice));
            Assert.That(timeline.Frame.HasServiceTarget, Is.False);
            Assert.That(
                MountainRoadCafeServiceTimeline.IsPatronWithCup(
                    MountainRoadCafeCastRole.Attendant),
                Is.False);

            timeline.Advance(MountainRoadCafeServiceTimeline.NoticeSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Wiping));
            Assert.That(
                timeline.Frame.PairManFill,
                Is.EqualTo(
                    MountainRoadCafeServiceTimeline.InitialPairManFill));
            Assert.That(
                timeline.Frame.PairWomanFill,
                Is.EqualTo(
                    MountainRoadCafeServiceTimeline.InitialPairWomanFill));
        }

        [Test]
        [Category("MountainRoad")]
        public void
            ServiceTimeline_HeroMenuQueuesBehindPourAndCompletesHandoff()
        {
            var timeline = new MountainRoadCafeServiceTimeline(91);

            Assert.That(MountainRoadCafeServiceTimeline.OffersHeroMenu,
                Is.True);
            Assert.That(
                timeline.TryRequestDrink(
                    MountainRoadCafeCastRole.PairMan),
                Is.True);
            timeline.Advance(
                MountainRoadCafeServiceTimeline.CoupleDrinkSeconds +
                MountainRoadCafeServiceTimeline.NoticeSeconds +
                MountainRoadCafeServiceTimeline.WalkSeconds +
                MountainRoadCafeServiceTimeline.PourSeconds * 0.5f);

            MountainRoadCafeServiceFrame beforeRequest = timeline.Frame;
            Assert.That(
                beforeRequest.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Pour));
            Assert.That(beforeRequest.HasServiceTarget, Is.True);
            Assert.That(
                beforeRequest.ServiceTarget,
                Is.EqualTo(MountainRoadCafeCastRole.PairMan));

            Assert.That(timeline.TryRequestHeroMenu(), Is.True);
            MountainRoadCafeServiceFrame queued = timeline.Frame;
            Assert.That(queued.Phase, Is.EqualTo(beforeRequest.Phase));
            Assert.That(
                queued.HasServiceTarget,
                Is.EqualTo(beforeRequest.HasServiceTarget));
            Assert.That(
                queued.PhaseElapsedSeconds,
                Is.EqualTo(beforeRequest.PhaseElapsedSeconds)
                    .Within(0.0001f));
            Assert.That(
                queued.ServiceTarget,
                Is.EqualTo(beforeRequest.ServiceTarget));
            Assert.That(
                queued.PairManFill,
                Is.EqualTo(beforeRequest.PairManFill).Within(0.0001f));
            Assert.That(
                queued.PairWomanFill,
                Is.EqualTo(beforeRequest.PairWomanFill).Within(0.0001f));
            Assert.That(queued.HeroMenuRequested, Is.True);
            Assert.That(queued.HeroMenuPlaced, Is.False);
            Assert.That(timeline.TryRequestHeroMenu(), Is.False);

            timeline.Advance(
                timeline.RemainingPhaseSeconds +
                MountainRoadCafeServiceTimeline.WalkSeconds);
            MountainRoadCafeServiceFrame menuNotice = timeline.Frame;
            Assert.That(
                menuNotice.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.MenuNotice));
            Assert.That(menuNotice.HasServiceTarget, Is.False);
            Assert.That(menuNotice.HeroMenuRequested, Is.True);
            Assert.That(menuNotice.HeroMenuPlaced, Is.False);
            Assert.That(
                menuNotice.PairManFill,
                Is.EqualTo(MountainRoadCafeServiceTimeline.RefilledLevel)
                    .Within(0.0001f));
            Assert.That(
                menuNotice.PairWomanFill,
                Is.EqualTo(beforeRequest.PairWomanFill).Within(0.0001f));

            float menuManFill = menuNotice.PairManFill;
            float menuWomanFill = menuNotice.PairWomanFill;
            timeline.Advance(MountainRoadCafeServiceTimeline.NoticeSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.WalkToHero));
            Assert.That(timeline.Frame.HasServiceTarget, Is.False);
            Assert.That(
                timeline.Frame.PairManFill,
                Is.EqualTo(menuManFill).Within(0.0001f));
            Assert.That(
                timeline.Frame.PairWomanFill,
                Is.EqualTo(menuWomanFill).Within(0.0001f));
            timeline.Advance(MountainRoadCafeServiceTimeline.WalkSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.PlaceMenu));
            Assert.That(timeline.Frame.HasServiceTarget, Is.False);
            Assert.That(timeline.Frame.HeroMenuPlaced, Is.False);
            Assert.That(
                timeline.Frame.PairManFill,
                Is.EqualTo(menuManFill).Within(0.0001f));
            Assert.That(
                timeline.Frame.PairWomanFill,
                Is.EqualTo(menuWomanFill).Within(0.0001f));
            timeline.Advance(MountainRoadCafeServiceTimeline.NoticeSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.MenuWalkBack));
            Assert.That(timeline.Frame.HeroMenuRequested, Is.False);
            Assert.That(timeline.Frame.HeroMenuPlaced, Is.True);
            Assert.That(
                timeline.Frame.PairManFill,
                Is.EqualTo(menuManFill).Within(0.0001f));
            Assert.That(
                timeline.Frame.PairWomanFill,
                Is.EqualTo(menuWomanFill).Within(0.0001f));

            timeline.Advance(MountainRoadCafeServiceTimeline.WalkSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Wiping));
            Assert.That(timeline.Frame.HasServiceTarget, Is.False);
            Assert.That(timeline.Frame.HeroMenuPlaced, Is.True);
            Assert.That(
                timeline.Frame.PairManFill,
                Is.EqualTo(menuManFill).Within(0.0001f));
            Assert.That(
                timeline.Frame.PairWomanFill,
                Is.EqualTo(menuWomanFill).Within(0.0001f));
        }

        [Test]
        [Category("MountainRoad")]
        public void
            ServiceTimeline_MenuRetrievalQueuesAndPreservesPatronCups()
        {
            var timeline = new MountainRoadCafeServiceTimeline(109);
            Assert.That(timeline.TryRequestHeroMenu(), Is.True);
            timeline.Advance(
                MountainRoadCafeServiceTimeline.NoticeSeconds +
                MountainRoadCafeServiceTimeline.WalkSeconds +
                MountainRoadCafeServiceTimeline.NoticeSeconds);

            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.MenuWalkBack));
            Assert.That(timeline.Frame.HeroMenuPlaced, Is.True);
            timeline.Advance(
                MountainRoadCafeServiceTimeline.WalkSeconds * 0.4f);
            MountainRoadCafeServiceFrame beforeRequest = timeline.Frame;

            Assert.That(
                timeline.TryRequestHeroMenuRetrieval(),
                Is.True);
            Assert.That(
                timeline.TryRequestHeroMenuRetrieval(),
                Is.False);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.MenuWalkBack));
            Assert.That(
                timeline.Frame.PhaseElapsedSeconds,
                Is.EqualTo(beforeRequest.PhaseElapsedSeconds)
                    .Within(0.0001f));
            Assert.That(timeline.Frame.HeroMenuRetrievalRequested, Is.True);
            Assert.That(timeline.Frame.HeroMenuRetrieved, Is.False);

            float pairManFill = timeline.Frame.PairManFill;
            float pairWomanFill = timeline.Frame.PairWomanFill;
            timeline.Advance(timeline.RemainingPhaseSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.WalkToMenu));
            Assert.That(timeline.Frame.HeroMenuPlaced, Is.True);

            timeline.Advance(MountainRoadCafeServiceTimeline.WalkSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.TakeMenu));
            Assert.That(timeline.Frame.HeroMenuPlaced, Is.True);

            timeline.Advance(MountainRoadCafeServiceTimeline.NoticeSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.CarryMenuBack));
            Assert.That(timeline.Frame.HeroMenuPlaced, Is.False);
            Assert.That(timeline.Frame.HeroMenuRetrievalRequested, Is.True);

            timeline.Advance(MountainRoadCafeServiceTimeline.WalkSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Wiping));
            Assert.That(timeline.Frame.HeroMenuRetrievalRequested, Is.False);
            Assert.That(timeline.Frame.HeroMenuRetrieved, Is.True);
            Assert.That(
                timeline.Frame.PairManFill,
                Is.EqualTo(pairManFill).Within(0.0001f));
            Assert.That(
                timeline.Frame.PairWomanFill,
                Is.EqualTo(pairWomanFill).Within(0.0001f));
            Assert.That(timeline.TryRequestHeroMenu(), Is.False);
        }

        [Test]
        [Category("MountainRoad")]
        public void CafeActivationRadius_DoesNotReachTheClimbingRoute()
        {
            MountainRoadPlan plan = MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed);
            Vector3 entrance = MountainRoadTerminalPlanner.CreateCafe(plan.Plateau).DoorCenter;
            float nearest = float.PositiveInfinity;
            foreach (MountainRoadRouteSample sample in plan.Route.Samples)
            {
                if (sample.Section == MountainRoadRouteSection.UpperApproach)
                {
                    continue;
                }

                Vector3 offset = sample.Position - entrance;
                offset.y = 0f;
                nearest = Mathf.Min(nearest, offset.magnitude);
            }

            Assert.That(
                nearest,
                Is.GreaterThan(
                    MountainRoadCafeCastController.ActivationRadius + 2f),
                "The cafe clock can arm from a lower hairpin.");
        }

        [TestCase(42)]
        [TestCase(91)]
        [Category("MountainRoad")]
        public void ServiceTimeline_FirstAutonomousDrinkStartsPourWithinOneMinute(
            int seed)
        {
            var timeline = new MountainRoadCafeServiceTimeline(seed);
            float elapsed = 0f;
            while ((timeline.Frame.Phase !=
                        MountainRoadCafeServicePhase.Pour ||
                    !MountainRoadCafeServiceTimeline.IsPourFlowActive(
                        timeline.Frame.PhaseNormalized)) &&
                   elapsed < 60f)
            {
                timeline.Advance(0.1f);
                elapsed += 0.1f;
            }

            Assert.That(elapsed, Is.LessThan(60f));
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Pour));
            Assert.That(
                MountainRoadCafeServiceTimeline.IsPourFlowActive(
                    timeline.Frame.PhaseNormalized),
                Is.True);
        }

        [Test]
        [Category("MountainRoad")]
        public void CupView_ReparentPreservesScaleAndRestoresExactDockPose()
        {
            var dock = new GameObject("Cup Dock");
            var socket = new GameObject("Imported Socket 100x");
            var lift = new GameObject("Cup Lift");
            var grip = new GameObject("Grip");
            var liquid = new GameObject("Liquid");
            try
            {
                dock.transform.localScale = Vector3.one * 0.01f;
                lift.transform.SetParent(dock.transform, false);
                lift.transform.localPosition = new Vector3(0.2f, 0.3f, 0.4f);
                lift.transform.localRotation = Quaternion.Euler(0f, 23f, 0f);
                lift.transform.localScale = Vector3.one * 100f;
                grip.transform.SetParent(lift.transform, false);
                grip.transform.localPosition = new Vector3(0.092f, 0.062f, 0f);
                socket.transform.localScale = Vector3.one * 100f;
                liquid.transform.SetParent(lift.transform, false);
                Renderer renderer = liquid.AddComponent<MeshRenderer>();
                var view = dock.AddComponent<MountainRoadCafeCupView>();
                Vector3 restPosition = lift.transform.localPosition;
                Quaternion restRotation = lift.transform.localRotation;
                Vector3 restScale = lift.transform.localScale;
                Vector3 worldScale = lift.transform.lossyScale;
                view.Configure(
                    MountainRoadCafeCastRole.PairMan,
                    liquid.transform,
                    renderer,
                    new Vector3(0f, 0.01f, 0f),
                    new Vector3(0f, 0.08f, 0f),
                    liquid.transform,
                    lift.transform,
                    grip.transform);

                view.SetDrinkPose(true, 0.5f, socket.transform);
                Assert.That(lift.transform.parent, Is.SameAs(socket.transform));
                AssertVector(lift.transform.lossyScale, worldScale, 0.0001f);
                AssertVector(grip.transform.position, socket.transform.position, 0.0001f);

                view.SetDrinkPose(false, 1f, socket.transform);
                Assert.That(lift.transform.parent, Is.SameAs(dock.transform));
                AssertVector(lift.transform.localPosition, restPosition, 0.0001f);
                Assert.That(
                    Quaternion.Angle(
                        lift.transform.localRotation,
                        restRotation),
                    Is.LessThan(0.0001f));
                AssertVector(lift.transform.localScale, restScale, 0.0001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(dock);
                UnityEngine.Object.DestroyImmediate(socket);
            }
        }

        private static void AssertVector(
            Vector3 actual,
            Vector3 expected,
            float tolerance)
        {
            Assert.That(actual.x, Is.EqualTo(expected.x).Within(tolerance));
            Assert.That(actual.y, Is.EqualTo(expected.y).Within(tolerance));
            Assert.That(actual.z, Is.EqualTo(expected.z).Within(tolerance));
        }

        [Test]
        [Category("MountainRoad")]
        public void Attendant_TurnsHisHeadAfterTheHeroAtTheCounterButNotDuringABeat()
        {
            // The attendant looks up from his counter under the hero's own
            // notice rule: right for a hero on his right, left for one on
            // his left, never behind him, and never while an authored
            // service beat owns his body.
            var root = new GameObject("Cafe Attendant Attention Root");
            var player = new GameObject("Cafe Attendant Test Player");
            player.transform.SetParent(root.transform, false);
            try
            {
                MountainRoadCafePlan cafePlan = CreateCafePlan();
                MountainRoadCafeWorldResult cafe =
                    MountainRoadCafeWorldBuilder.Build(
                        root.transform,
                        cafePlan);
                Transform attendantRoot = cafe.Cast.GetPresentationRoot(
                    MountainRoadCafeCastRole.Attendant);
                Assert.That(
                    attendantRoot.GetComponent<NpcHeroAttentionLook>(),
                    Is.Null,
                    "Nobody looks at a hero who has not been bound.");

                Assert.That(
                    cafe.Cast.BindHeroAttention(player.transform),
                    Is.True);
                Assert.That(
                    cafe.Cast.BindHeroAttention(player.transform),
                    Is.False,
                    "One glance per attendant.");
                NpcHeroAttentionLook look =
                    attendantRoot.GetComponent<NpcHeroAttentionLook>();
                Assert.That(look, Is.Not.Null);
                MountainRoadCafeCastPresentation attendant = attendantRoot
                    .GetComponent<MountainRoadCafeCastPresentation>();
                MountainRoadCafeCastAssetRegistry registry =
                    attendant.Registry;
                Vector3 forward = attendantRoot.forward;
                Vector3 right = attendantRoot.right;

                player.transform.position = attendantRoot.position -
                                            (forward * 2f);
                Step(look, 40);
                Assert.That(look.IsAttending, Is.False);
                Assert.That(look.AttentionWeight, Is.EqualTo(0f));

                player.transform.position = attendantRoot.position +
                                            (forward * 1.5f) +
                                            (right * 2f);
                Step(look, 40);
                Assert.That(look.IsAttending, Is.True);
                Assert.That(
                    look.AttentionWeight,
                    Is.EqualTo(1f).Within(0.0001f));
                float rightYaw = FaceYaw(registry, forward);
                Assert.That(
                    rightYaw,
                    Is.GreaterThan(15f),
                    "A hero on his right turns the face right.");

                player.transform.position = attendantRoot.position +
                                            (forward * 1.5f) -
                                            (right * 2f);
                Step(look, 60);
                float leftYaw = FaceYaw(registry, forward);
                Assert.That(
                    leftYaw,
                    Is.LessThan(-15f),
                    "A hero on his left turns the face left.");

                Assert.That(
                    attendant.ApplyClip(MountainRoadCafeCastClipKind.Walk, 0f),
                    Is.True);
                Assert.That(attendant.IsBeatPlaying, Is.True);
                Step(look, 40);
                Assert.That(
                    look.IsAttending,
                    Is.False,
                    "A service beat owns the body; the glance stands down.");
                Assert.That(look.AttentionWeight, Is.EqualTo(0f));
                Assert.That(
                    Mathf.Abs(FaceYaw(registry, forward)),
                    Is.LessThan(4f),
                    "...and the face is back on the authored pose.");

                Assert.That(
                    attendant.ApplyClip(registry.DefaultClipKind, 0f),
                    Is.True);
                Step(look, 40);
                Assert.That(
                    look.IsAttending,
                    Is.True,
                    "Back at the counter he notices the hero again.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void Step(NpcHeroAttentionLook look, int frames)
        {
            for (int frame = 0; frame < frames; frame++)
            {
                look.Advance(0.02f);
            }
        }

        /// <summary>
        /// Where the face points off the body facing, in the plane: head
        /// bone to the midpoint of the eye bones, which ride the head.
        /// </summary>
        private static float FaceYaw(
            MountainRoadCafeCastAssetRegistry registry,
            Vector3 facing)
        {
            Transform head = registry.FindModelTransform("head");
            Transform leftEye = registry.FindModelTransform("face.eye.L");
            Transform rightEye = registry.FindModelTransform("face.eye.R");
            Assert.That(head, Is.Not.Null);
            Assert.That(leftEye, Is.Not.Null);
            Assert.That(rightEye, Is.Not.Null);
            Vector3 eyes = (leftEye.position + rightEye.position) * 0.5f;
            Vector3 face = eyes - head.position;
            face.y = 0f;
            facing.y = 0f;
            return Vector3.SignedAngle(facing, face, Vector3.up);
        }

        private static MountainRoadCafePlan CreateCafePlan()
        {
            return MountainRoadTerminalPlanner.CreateCafe(MountainRoadPlanner.Create(GameSessionState.DefaultCitySeed).Plateau);
        }

        private static MountainRoadCafeCastMemberPlan Find(
            MountainRoadCafeCastPlan plan,
            MountainRoadCafeCastRole role)
        {
            return plan.Members.Single(member => member.Role == role);
        }

        private static float LocalRight(
            MountainRoadCafePlan cafe,
            MountainRoadCafeCastMemberPlan member)
        {
            return Vector3.Dot(
                member.Position - cafe.Center,
                cafe.Right);
        }

    }
}
