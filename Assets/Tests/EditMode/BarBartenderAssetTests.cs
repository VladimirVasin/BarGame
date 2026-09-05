using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarBartenderAssetTests
    {
        private const string PlayerModelPath =
            "Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx";
        private const string ServiceAnimationPath =
            "Assets/Pedestrians/Animations/MountainRoadCafeCast.fbx";
        private const string WalkAnimationPath =
            "Assets/Bar/Bartender/Animations/BarBartenderWalk.anim";

        [Test]
        public void Provider_SelectsOrdinaryPrefab_AndRetainsLegacy()
        {
            BarBartenderProvider provider = BarBartenderProvider.Load();
            Assert.That(
                provider,
                Is.Not.Null,
                "The bartender provider asset must be addressable.");
            Assert.That(provider.BartenderPrefab, Is.Not.Null);
            Assert.That(provider.LegacyBartenderPrefab, Is.Not.Null);
            Assert.That(
                provider.BartenderPrefab,
                Is.Not.SameAs(provider.LegacyBartenderPrefab));

            BarBartenderAssetRegistry active =
                provider.BartenderPrefab.GetComponent<
                    BarBartenderAssetRegistry>();
            Assert.That(active, Is.Not.Null);
            Assert.That(active.Animator, Is.Not.Null);
            Assert.That(active.Animator.avatar, Is.Not.Null);
            Assert.That(
                AssetDatabase.GetAssetPath(active.Animator.avatar),
                Is.EqualTo(PlayerModelPath));
            Assert.That(
                active.DesignId,
                Is.EqualTo(BarBartenderProvider.DesignId));
            Assert.That(active.BuildSignature, Has.Length.EqualTo(64));
            Assert.That(active.SourceTriangleCount, Is.InRange(900, 2600));
            Assert.That(active.Renderers.Count, Is.InRange(28, 58));
            Assert.That(
                active.RendererBindings.Count,
                Is.EqualTo(active.Renderers.Count));
            Assert.That(active.ExtraArmChains, Is.Empty);
            Assert.That(active.UsesAuthoredServiceClips, Is.True);
            Assert.That(active.ClipBindings.Count, Is.EqualTo(5));
            BarBartenderClipBinding walk = active.ClipBindings.Single(
                binding => binding.Kind == BarBartenderClipKind.Walk);
            Assert.That(
                AssetDatabase.GetAssetPath(walk.Clip),
                Is.EqualTo(WalkAnimationPath));
            Assert.That(walk.Clip.name, Is.EqualTo("BarBartenderWalk"));
            Assert.That(walk.Clip.length, Is.EqualTo(1f).Within(0.002f));
            Assert.That(walk.Loop, Is.True);
            BarBartenderClipBinding serviceStep =
                active.ClipBindings.Single(
                    binding => binding.Kind ==
                        BarBartenderClipKind.ServiceStep);
            Assert.That(
                AssetDatabase.GetAssetPath(serviceStep.Clip),
                Is.EqualTo(ServiceAnimationPath));
            Assert.That(
                serviceStep.Clip.name,
                Is.EqualTo("CafeAttendantWalk"));
            Assert.That(active.LeftGripSocket, Is.Not.Null);
            Assert.That(active.LeftVesselSocket, Is.Not.Null);
            Assert.That(active.RightGripSocket, Is.Not.Null);
            Assert.That(active.RightBottleSocket, Is.Not.Null);
            Assert.That(active.VesselGripAnchor, Is.Not.Null);
            Assert.That(active.BottleGripAnchor, Is.Not.Null);
            Assert.That(
                provider.BartenderPrefab
                    .GetComponentsInChildren<Collider>(true),
                Is.Empty);
            Assert.That(
                provider.BartenderPrefab
                    .GetComponentsInChildren<Light>(true),
                Is.Empty);
            Assert.That(
                provider.BartenderPrefab
                    .GetComponentsInChildren<Rigidbody>(true),
                Is.Empty);
            Assert.That(
                active.LocalBounds.size.y,
                Is.EqualTo(1.75f).Within(0.05f));

            BarBartenderAssetRegistry legacy =
                provider.LegacyBartenderPrefab.GetComponent<
                    BarBartenderAssetRegistry>();
            Assert.That(legacy, Is.Not.Null);
            Assert.That(
                legacy.DesignId,
                Is.EqualTo(BarBartenderProvider.LegacyDesignId));
            Assert.That(legacy.UsesAuthoredServiceClips, Is.False);
            Assert.That(
                legacy.ExtraArmChains.Count,
                Is.EqualTo(
                    BarBartenderAssetRegistry.ExtraArmChainCount));
            var expectedChainIds = new[]
            {
                "Arm2.L",
                "Arm2.R",
                "Arm3.L",
                "Arm3.R"
            };
            var seenGrips = new HashSet<Transform>();
            for (int index = 0;
                 index < legacy.ExtraArmChains.Count;
                 index++)
            {
                BarBartenderArmChain chain =
                    legacy.ExtraArmChains[index];
                Assert.That(
                    chain.ChainId,
                    Is.EqualTo(expectedChainIds[index]));
                Assert.That(chain.ShoulderPivot, Is.Not.Null);
                Assert.That(chain.ElbowPivot, Is.Not.Null);
                Assert.That(chain.WristPivot, Is.Not.Null);
                Assert.That(chain.GripPivot, Is.Not.Null);
                Assert.That(seenGrips.Add(chain.GripPivot), Is.True);
            }
        }

        [Test]
        public void OrdinaryPresentation_ReadsServiceTimelineIntoServiceClips()
        {
            BarBartenderProvider provider = BarBartenderProvider.Load();
            GameObject instance = Object.Instantiate(
                provider.BartenderPrefab);
            try
            {
                BarBartenderAssetRegistry registry =
                    instance.GetComponent<BarBartenderAssetRegistry>();
                BarBartenderPresentation presentation =
                    instance.AddComponent<BarBartenderPresentation>();
                presentation.Initialize(registry);

                Renderer serviceTowel = null;
                for (int index = 0;
                     index < registry.RendererBindings.Count;
                     index++)
                {
                    BarBartenderRendererBinding binding =
                        registry.RendererBindings[index];
                    if (binding != null &&
                        binding.RendererName == "ACC_ServiceTowel")
                    {
                        serviceTowel = binding.Renderer;
                        break;
                    }
                }

                Assert.That(presentation.UsesOrdinaryRig, Is.True);
                Assert.That(serviceTowel, Is.Not.Null);
                Assert.That(serviceTowel.enabled, Is.True);
                Assert.That(
                    presentation.ChainCount,
                    Is.EqualTo(
                        BarBartenderPresentation.OrdinaryHandCount));
                Assert.That(
                    presentation.GetChainGrip(
                        BarBartenderPresentation
                            .OrdinaryVesselHandIndex),
                    Is.SameAs(registry.VesselGripAnchor));
                Assert.That(
                    presentation.GetChainGrip(
                        BarBartenderPresentation
                            .OrdinaryBottleHandIndex),
                    Is.SameAs(registry.BottleGripAnchor));

                var timeline = new BarDrinkServiceTimeline();
                Assert.That(timeline.BeginOpen(), Is.True);
                presentation.ApplyServiceFrame(
                    timeline.CurrentFrame,
                    leftHandCarriesMenu: true);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Notice));
                Assert.That(serviceTowel.enabled, Is.False,
                    "Menu delivery must free the bartender's left hand towel.");

                timeline.Advance(
                    BarDrinkServiceTimeline
                        .CameraApproachDurationSeconds);
                presentation.ApplyServiceFrame(timeline.CurrentFrame);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Wipe));
                Assert.That(serviceTowel.enabled, Is.True,
                    "Placed-menu browsing must restore the idle towel.");
                Assert.That(timeline.Confirm(), Is.True);
                presentation.ApplyServiceFrame(
                    timeline.CurrentFrame,
                    leftHandCarriesMenu: true);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.ServiceStep));
                Assert.That(serviceTowel.enabled, Is.False,
                    "Menu retrieval must free the bartender's left hand towel.");

                presentation.ApplyCounterTravelPose(
                    0.25f,
                    leftHandCarriesMenu: true);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Walk));

                timeline.Advance(
                    BarDrinkServiceTimeline
                        .BottlePickupDurationSeconds +
                    BarDrinkServiceTimeline
                        .VesselPlacementDurationSeconds);
                presentation.ApplyServiceFrame(
                    timeline.CurrentFrame);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Pour));

                presentation.SetChainTarget(
                    BarBartenderPresentation
                        .OrdinaryBottleHandIndex,
                    instance.transform.position + Vector3.forward,
                    1f);
                presentation.Advance(
                    BarBartenderPresentation.ReachBlendSeconds);
                Assert.That(
                    presentation.GetChainWeight(
                        BarBartenderPresentation
                            .OrdinaryBottleHandIndex),
                    Is.EqualTo(1f).Within(0.001f));

                presentation.ResetServicePose();
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Wipe));
                Assert.That(serviceTowel.enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void OrdinaryWalkClip_ContainsVisibleFootTravel()
        {
            BarBartenderProvider provider = BarBartenderProvider.Load();
            BarBartenderAssetRegistry registry =
                provider.BartenderPrefab.GetComponent<
                    BarBartenderAssetRegistry>();
            BarBartenderClipBinding walk = registry.ClipBindings.Single(
                binding => binding.Kind == BarBartenderClipKind.Walk);
            GameObject instance = Object.Instantiate(
                provider.BartenderPrefab);
            try
            {
                BarBartenderAssetRegistry instanceRegistry =
                    instance.GetComponent<BarBartenderAssetRegistry>();
                Transform leftFoot = instance
                    .GetComponentsInChildren<Transform>(true)
                    .Single(transform => transform.name == "foot.L");
                Transform rightFoot = instance
                    .GetComponentsInChildren<Transform>(true)
                    .Single(transform => transform.name == "foot.R");
                walk.Clip.SampleAnimation(
                    instanceRegistry.Animator.gameObject,
                    0f);
                Vector3 leftStart = leftFoot.position;
                Vector3 rightStart = rightFoot.position;
                walk.Clip.SampleAnimation(
                    instanceRegistry.Animator.gameObject,
                    0.25f);

                float travel = Vector3.Distance(
                    leftStart,
                    leftFoot.position) + Vector3.Distance(
                    rightStart,
                    rightFoot.position);
                Assert.That(travel, Is.GreaterThan(0.08f));
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void HeroAttention_TurnsTheFaceAfterTheHeroAndReleasesToTheRig()
        {
            // Two identical bartenders stepped in lockstep: one is given the
            // hero, the other never is. A hero across the counter on the
            // right must swing the first one's face right; a hero walking
            // off behind him must leave the head and neck exactly where the
            // twin's rig holds them.
            BarBartenderProvider provider = BarBartenderProvider.Load();
            var root = new GameObject("Bartender Attention Root");
            try
            {
                GameObject glancingObject = Object.Instantiate(
                    provider.BartenderPrefab,
                    root.transform);
                GameObject twinObject = Object.Instantiate(
                    provider.BartenderPrefab,
                    root.transform);
                BarBartenderAssetRegistry registry =
                    glancingObject.GetComponent<BarBartenderAssetRegistry>();
                BarBartenderAssetRegistry twinRegistry =
                    twinObject.GetComponent<BarBartenderAssetRegistry>();
                BarBartenderPresentation glancing =
                    glancingObject.AddComponent<BarBartenderPresentation>();
                BarBartenderPresentation twin =
                    twinObject.AddComponent<BarBartenderPresentation>();
                glancing.Initialize(registry);
                twin.Initialize(twinRegistry);

                var heroObject = new GameObject("Hero Root");
                heroObject.transform.SetParent(root.transform, false);
                Transform hero = heroObject.transform;
                hero.position = glancing.transform.position +
                                (glancing.transform.forward * 2f) +
                                (glancing.transform.right * 2f);
                NpcHeroAttentionLook look =
                    BarBartenderWorldBuilder.AttachHeroAttention(
                        glancing,
                        registry,
                        hero);
                Assert.That(look, Is.Not.Null);
                Assert.That(look.IsInitialized, Is.True);

                for (int frame = 0; frame < 40; frame++)
                {
                    glancing.Advance(0.02f);
                    twin.Advance(0.02f);
                    look.Advance(0.02f);
                }

                Assert.That(look.IsAttending, Is.True);
                Assert.That(
                    look.AttentionWeight,
                    Is.EqualTo(1f).Within(0.0001f));
                float yaw = PlanarFaceYaw(twinRegistry, registry);
                Assert.That(
                    yaw,
                    Is.GreaterThan(20f).And.LessThan(
                        PlayerAttentionRules.MaxHeadYawDegrees + 1f),
                    "A hero 45 degrees to the right turns the face right, " +
                    "within what a neck can do.");

                hero.position = glancing.transform.position -
                                (glancing.transform.forward * 2f);
                for (int frame = 0; frame < 40; frame++)
                {
                    glancing.Advance(0.02f);
                    twin.Advance(0.02f);
                    look.Advance(0.02f);
                }

                Assert.That(look.IsAttending, Is.False);
                Assert.That(look.AttentionWeight, Is.EqualTo(0f));
                Assert.That(
                    Quaternion.Angle(
                        registry.Head.localRotation,
                        twinRegistry.Head.localRotation),
                    Is.LessThan(0.01f),
                    "Released, the head is exactly the rig's own.");
                if (registry.Neck != null && twinRegistry.Neck != null)
                {
                    Assert.That(
                        Quaternion.Angle(
                            registry.Neck.localRotation,
                            twinRegistry.Neck.localRotation),
                        Is.LessThan(0.01f),
                        "Released, the neck is exactly the rig's own.");
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// The glancing face's planar yaw off the twin's: head bone to the
        /// midpoint of the eye bones, which ride the head.
        /// </summary>
        private static float PlanarFaceYaw(
            BarBartenderAssetRegistry rest,
            BarBartenderAssetRegistry turned)
        {
            Vector3 restFace = FaceDirection(rest);
            Vector3 turnedFace = FaceDirection(turned);
            restFace.y = 0f;
            turnedFace.y = 0f;
            return Vector3.SignedAngle(restFace, turnedFace, Vector3.up);
        }

        private static Vector3 FaceDirection(
            BarBartenderAssetRegistry registry)
        {
            Transform bones = registry.Animator.transform;
            Transform leftEye =
                NpcAttentionHeadLayer.FindBone(bones, "face.eye.L");
            Transform rightEye =
                NpcAttentionHeadLayer.FindBone(bones, "face.eye.R");
            Assert.That(leftEye, Is.Not.Null);
            Assert.That(rightEye, Is.Not.Null);
            Vector3 eyes = (leftEye.position + rightEye.position) * 0.5f;
            return (eyes - registry.Head.position).normalized;
        }
    }
}
