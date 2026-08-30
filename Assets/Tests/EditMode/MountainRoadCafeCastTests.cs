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
        private const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        private const string StagedPrefabRoot =
            "Assets/Pedestrians/Staged/Prefabs/";

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
        public void Provider_LoadsFourDistinctPassiveStagedPrefabs()
        {
            MountainRoadCafeCastProvider provider =
                MountainRoadCafeCastProvider.Load();
            Assert.That(provider, Is.Not.Null);
            Assert.That(provider.HasCompleteCast, Is.True);

            GameObject[] prefabs = GetProviderPrefabs(provider);
            MountainRoadCafeCastRole[] roles =
            {
                MountainRoadCafeCastRole.LonePatron,
                MountainRoadCafeCastRole.PairMan,
                MountainRoadCafeCastRole.PairWoman,
                MountainRoadCafeCastRole.Attendant
            };
            Assert.That(prefabs, Has.Length.EqualTo(4));
            Assert.That(prefabs.Distinct().Count(), Is.EqualTo(4));

            Player3DAssetRegistry playerRegistry =
                Player3DResources.LoadPrefab(Player3DVariant.ProductionV2)
                    .GetComponent<Player3DAssetRegistry>();
            Assert.That(playerRegistry, Is.Not.Null);
            Assert.That(playerRegistry.Animator.avatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(playerRegistry.Animator.avatar),
                Is.EqualTo(PlayerModelPath));

            var ambientPrefabs = new HashSet<GameObject>(
                CityPedestrianResources.Archetypes
                    .Select(archetype => Resources.Load<GameObject>(
                        archetype.PrefabResourcePath))
                    .Where(prefab => prefab != null));

            for (int index = 0; index < prefabs.Length; index++)
            {
                GameObject prefab = prefabs[index];
                Assert.That(prefab, Is.Not.Null);
                Assert.That(provider.GetPrefab(roles[index]), Is.SameAs(prefab));
                string assetPath = AssetDatabase.GetAssetPath(prefab);
                Assert.That(assetPath, Does.StartWith(StagedPrefabRoot));
                Assert.That(assetPath, Does.Not.Contain("/Resources/"));
                Assert.That(ambientPrefabs.Contains(prefab), Is.False);
                Assert.That(
                    prefab.GetComponentsInChildren<
                        CityPedestrianAssetRegistry>(true),
                    Is.Empty);

                AssertPrefabContract(
                    prefab,
                    playerRegistry.Animator.avatar,
                    roles[index]);
            }

            Assert.That(
                prefabs.Sum(prefab => prefab
                    .GetComponent<MountainRoadCafeCastAssetRegistry>()
                    .ClipBindings.Count),
                Is.EqualTo(10),
                "The isolated cafe library is a ten-clip contract.");
        }

        [Test]
        [Category("MountainRoad")]
        public void BuiltCafe_ContainsOnlyFourBespokeInitializedFigures()
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
                    Is.True);
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
                        "exactly one bespoke figure.");
                }
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
        public void ServiceTimeline_PairDrinkSharesOneClockAndFillDelta()
        {
            var timeline = new MountainRoadCafeServiceTimeline(42);

            Assert.That(
                timeline.TryRequestDrink(
                    MountainRoadCafeCastRole.PairMan),
                Is.True);
            timeline.Advance(
                MountainRoadCafeServiceTimeline.CoupleDrinkSeconds * 0.40f);
            Assert.That(
                timeline.Frame.PairManFill,
                Is.EqualTo(0.78f).Within(0.0001f),
                "Coffee cannot vanish while the cups are still lifting.");
            timeline.Advance(
                MountainRoadCafeServiceTimeline.CoupleDrinkSeconds * 0.15f);

            MountainRoadCafeServiceFrame frame = timeline.Frame;
            Assert.That(
                frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.CoupleDrink));
            Assert.That(frame.PhaseNormalized, Is.EqualTo(0.55f).Within(0.0001f));
            Assert.That(frame.PairManFill, Is.EqualTo(0.67f).Within(0.0001f));
            Assert.That(frame.PairWomanFill, Is.EqualTo(0.67f).Within(0.0001f));
            Assert.That(frame.LoneFill, Is.EqualTo(0.62f).Within(0.0001f));
        }

        [Test]
        [Category("MountainRoad")]
        public void ServiceTimeline_RequestsRefillOnlyAtThreshold()
        {
            var timeline = new MountainRoadCafeServiceTimeline(42);

            Assert.That(
                timeline.TryRequestDrink(
                    MountainRoadCafeCastRole.LonePatron),
                Is.True);
            timeline.Advance(MountainRoadCafeServiceTimeline.LoneDrinkSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Wiping));
            Assert.That(
                timeline.Frame.LoneFill,
                Is.EqualTo(0.40f).Within(0.0001f));

            Assert.That(
                timeline.TryRequestDrink(
                    MountainRoadCafeCastRole.LonePatron),
                Is.True);
            timeline.Advance(MountainRoadCafeServiceTimeline.LoneDrinkSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Notice));
            Assert.That(timeline.Frame.HasServiceTarget, Is.True);
            Assert.That(
                timeline.Frame.ServiceTarget,
                Is.EqualTo(MountainRoadCafeCastRole.LonePatron));
            Assert.That(
                timeline.Frame.LoneFill,
                Is.EqualTo(0.18f).Within(0.0001f));

            timeline.Advance(
                MountainRoadCafeServiceTimeline.NoticeSeconds +
                MountainRoadCafeServiceTimeline.WalkSeconds);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.Pour));

            timeline.Advance(
                MountainRoadCafeServiceTimeline.PourSeconds * 0.30f);
            Assert.That(
                timeline.Frame.LoneFill,
                Is.EqualTo(0.18f).Within(0.0001f),
                "Coffee cannot rise while the pot is still lifting.");

            timeline.Advance(
                MountainRoadCafeServiceTimeline.PourSeconds * 0.25f);
            Assert.That(
                timeline.Frame.LoneFill,
                Is.EqualTo(0.54f).Within(0.0001f),
                "The fill should be halfway through its authored flow " +
                "window at normalized clip time 0.55.");

            timeline.Advance(
                MountainRoadCafeServiceTimeline.PourSeconds * 0.45f);
            Assert.That(
                timeline.Frame.Phase,
                Is.EqualTo(MountainRoadCafeServicePhase.WalkBack));
            Assert.That(
                timeline.Frame.LoneFill,
                Is.EqualTo(0.90f).Within(0.0001f));
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
            Assert.That(actual.LoneFill, Is.EqualTo(expected.LoneFill).Within(0.0001f));
            Assert.That(actual.PairManFill, Is.EqualTo(expected.PairManFill).Within(0.0001f));
            Assert.That(actual.PairWomanFill, Is.EqualTo(expected.PairWomanFill).Within(0.0001f));
        }

        [Test]
        [Category("MountainRoad")]
        public void ServiceTimeline_HeroNoticeNeverCreatesFourthServiceTarget()
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
            Assert.That(timeline.Frame.LoneFill, Is.EqualTo(0.62f));
            Assert.That(timeline.Frame.PairManFill, Is.EqualTo(0.78f));
            Assert.That(timeline.Frame.PairWomanFill, Is.EqualTo(0.78f));
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
                    MountainRoadCafeCastRole.LonePatron,
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

        private static void AssertPrefabContract(
            GameObject prefab,
            Avatar expectedAvatar,
            MountainRoadCafeCastRole expectedRole)
        {
            MountainRoadCafeCastAssetRegistry[] registries =
                prefab.GetComponentsInChildren<
                    MountainRoadCafeCastAssetRegistry>(true);
            Assert.That(registries, Has.Length.EqualTo(1));
            MountainRoadCafeCastAssetRegistry registry = registries[0];
            Assert.That(registry.Animator, Is.Not.Null);
            Assert.That(registry.ModelRoot, Is.Not.Null);
            Assert.That(registry.Role, Is.EqualTo(expectedRole));
            Assert.That(registry.IdleClip, Is.Not.Null);
            Assert.That(registry.BeatClip, Is.Not.Null);
            int expectedClipCount =
                expectedRole == MountainRoadCafeCastRole.Attendant ? 4 : 2;
            Assert.That(
                registry.ClipBindings.Count,
                Is.EqualTo(expectedClipCount));
            Assert.That(
                registry.ClipBindings.Select(binding => binding.Kind)
                    .Distinct().Count(),
                Is.EqualTo(expectedClipCount));
            foreach (MountainRoadCafeCastClipBinding clip in
                     registry.ClipBindings)
            {
                Assert.That(clip.Clip, Is.Not.Null);
                bool expectedLoop =
                    clip.Kind == MountainRoadCafeCastClipKind.Idle ||
                    clip.Kind == MountainRoadCafeCastClipKind.Wipe ||
                    clip.Kind == MountainRoadCafeCastClipKind.Walk;
                Assert.That(clip.Loop, Is.EqualTo(expectedLoop));
            }
            Assert.That(
                registry.FindModelTransform(
                    "SOCKET_CafePotSpout") != null,
                Is.EqualTo(
                    expectedRole == MountainRoadCafeCastRole.Attendant));

            if (expectedRole != MountainRoadCafeCastRole.Attendant)
            {
                string cupSocket = expectedRole ==
                                   MountainRoadCafeCastRole.PairWoman
                    ? "SOCKET_Vessel.L"
                    : "SOCKET_Bottle.R";
                Assert.That(
                    registry.FindModelTransform(cupSocket),
                    Is.Not.Null,
                    $"{expectedRole} requires its animated cup-hand socket.");
            }
            Assert.That(registry.Animator.avatar, Is.SameAs(expectedAvatar));
            Assert.That(registry.Animator.applyRootMotion, Is.False);
            Assert.That(
                registry.Animator.runtimeAnimatorController,
                Is.Null,
                "Cafe figures are Playables-driven and must stay " +
                "controller-free.");
            Assert.That(registry.RendererBindings, Is.Not.Empty);
            Assert.That(
                registry.RendererBindings
                    .Select(binding => binding.Renderer)
                    .All(renderer => renderer != null),
                Is.True);
            Assert.That(
                registry.RendererBindings
                    .Select(binding => binding.Renderer)
                    .Distinct().Count(),
                Is.EqualTo(registry.RendererBindings.Count));

            Assert.That(
                prefab.GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Collider2D>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Rigidbody2D>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<AudioSource>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                prefab.GetComponentsInChildren<Camera>(true),
                Is.Empty);

            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.SetActive(false);
                instance.SetActive(true);
                MountainRoadCafeCastAssetRegistry instanceRegistry =
                    instance.GetComponentInChildren<
                        MountainRoadCafeCastAssetRegistry>(true);
                instanceRegistry.ApplyBaseColors();
                var properties = new MaterialPropertyBlock();
                for (int index = 0;
                     index < instanceRegistry.RendererBindings.Count;
                     index++)
                {
                    MountainRoadCafeCastRendererBinding binding =
                        instanceRegistry.RendererBindings[index];
                    binding.Renderer.GetPropertyBlock(properties);
                    AssertColor(
                        properties.GetColor("_BaseColor"),
                        binding.Color);
                    AssertColor(
                        properties.GetColor("_Color"),
                        binding.Color);
                    properties.Clear();
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void AssertColor(Color actual, Color expected)
        {
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
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

        private static MountainRoadCafePlan CreateCafePlan()
        {
            return MountainRoadPlanner.Create(
                GameSessionState.DefaultCitySeed).Terminal.Cafe;
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

        private static GameObject[] GetProviderPrefabs(
            MountainRoadCafeCastProvider provider)
        {
            return new[]
            {
                provider.LonePatronPrefab,
                provider.PairManPrefab,
                provider.PairWomanPrefab,
                provider.AttendantPrefab
            };
        }
    }
}
