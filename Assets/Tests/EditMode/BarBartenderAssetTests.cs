using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class BarBartenderAssetTests
    {
        [Test]
        public void DefaultPopulation_OwnsBartenderAppearanceAndServiceRig()
        {
            GameObject instance = CreateBartender();
            try
            {
                var registry = instance.GetComponent<BarBartenderAssetRegistry>();
                var assignment = DefaultNpcPopulation.GetAssignment(DefaultNpcPopulation.BarBartender);
                Assert.That(registry.DesignId, Is.EqualTo(DefaultNpcCatalog.OrdinaryWorker));
                Assert.That(registry.Actor.GetComponent<DefaultNpcAppearance>().AppearanceKey,
                    Is.EqualTo(DefaultNpcPopulation.BarBartender));
                Assert.That(assignment.ItemIds, Does.Contain("apron.work"));
                Assert.That(registry.Actor.IsInitialized, Is.False,
                    "The population sampler must release the Animator to the bar sampler.");
                Assert.That(registry.ServiceTowel, Is.Not.Null);
                Assert.That(registry.ServiceTowel.Id, Is.EqualTo(CityPedestrianHandPropId.ServiceTowel));
                foreach (BarBartenderClipKind kind in System.Enum.GetValues(typeof(BarBartenderClipKind)))
                    Assert.That(registry.TryGetClip(kind, out _, out _), Is.True);
                Assert.That(registry.Actor.GetComponent<NpcWardrobe>().IsModular, Is.True);
            }
            finally { Object.DestroyImmediate(instance); }
        }

        [Test]
        public void OrdinaryPresentation_ReadsServiceTimelineIntoServiceClips()
        {
            var root = new GameObject("Test bartender service");
            try
            {
                GameObject instance = CreateBartender(root.transform);
                GameObject roomPrefab = BarModelResources.LoadInteriorPrefab();
                Assert.That(roomPrefab, Is.Not.Null);
                GameObject room = Object.Instantiate(roomPrefab, root.transform);
                Renderer counter = room.GetComponent<BarAssetRegistry>().Parts
                    .Single(part => part.SourceName == "Counter Top").Renderer;
                BarNpcAnchor placement = BarInteriorLayoutPlanner.Generate(
                    31, "Test bartender service", BarActivityKind.BeerPong,
                    CityDistrictKind.Nightlife).NpcAnchors.Single(anchor => anchor.Role == BarNpcRole.Bartender);
                instance.transform.localPosition = placement.Position;
                instance.transform.localRotation = Quaternion.Euler(0f, placement.YawDegrees, 0f);
                BarBartenderAssetRegistry registry =
                    instance.GetComponent<BarBartenderAssetRegistry>();
                registry.ConfigureWipeSurface(counter);
                BarBartenderPresentation presentation =
                    instance.AddComponent<BarBartenderPresentation>();
                presentation.Initialize(registry);
                // PlayMode creates this same manual graph on Initialize.
                // EditMode steps it explicitly, with no player-loop timing.
                MethodInfo createGraph = typeof(BarBartenderPresentation).GetMethod(
                    "CreateOrdinaryGraph", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(createGraph, Is.Not.Null);
                createGraph.Invoke(presentation, null);
                presentation.Advance(0f);

                Renderer serviceTowel = registry.ServiceTowel.Renderers[0];
                CityPedestrianHandPropRegistry originalTowel = registry.ServiceTowel;

                Assert.That(presentation.UsesOrdinaryRig, Is.True);
                Assert.That(serviceTowel, Is.Not.Null);
                Assert.That(serviceTowel.enabled, Is.True);
                Assert.That(presentation.TowelPresentation.WipeContactError, Is.LessThanOrEqualTo(.003f));
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
                Assert.That(presentation.TowelPresentation.State, Is.EqualTo(BarBartenderTowelState.Stowing));
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Wipe),
                    "The notice action must wait for the real towel movement.");
                Vector3 wipeHand = registry.LeftHand.position;
                presentation.Advance(.18f);
                Assert.That(serviceTowel.enabled, Is.True, "The towel must stay visible while lifted from the counter.");
                Assert.That(Vector3.Distance(wipeHand, registry.LeftHand.position), Is.GreaterThan(.03f));
                AdvanceUntilTowelStored(presentation, originalTowel);
                presentation.Advance(0f);
                Assert.That(presentation.CurrentClipKind, Is.EqualTo(BarBartenderClipKind.Notice));
                Assert.That(serviceTowel.enabled, Is.False);

                timeline.Advance(
                    BarDrinkServiceTimeline
                        .CameraApproachDurationSeconds);
                presentation.ApplyServiceFrame(timeline.CurrentFrame);
                presentation.ResetServicePose();
                Assert.That(presentation.TowelPresentation.State, Is.EqualTo(BarBartenderTowelState.Drawing));
                Assert.That(serviceTowel.enabled, Is.False, "The empty hand must reach behind the back before drawing the towel.");
                AdvanceUntilTowelDrawn(presentation, originalTowel);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Wipe));
                Assert.That(serviceTowel.enabled, Is.True,
                    "Placed-menu browsing must restore the idle towel.");
                Assert.That(timeline.Confirm(), Is.True);
                presentation.ApplyServiceFrame(
                    timeline.CurrentFrame,
                    leftHandCarriesMenu: true);
                Assert.That(presentation.IsTowelServiceReady, Is.False);
                Assert.That(serviceTowel.enabled, Is.True, "Retrieval first carries the visible towel behind the back.");
                AdvanceUntilTowelStored(presentation, originalTowel);
                presentation.Advance(0f);
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
                presentation.SetChainTarget(BarBartenderPresentation.OrdinaryBottleHandIndex, Vector3.zero, 0f);
                Assert.That(presentation.IsTowelWipeReady, Is.False,
                    "Reset must draw the towel before returning to wiping.");
                AdvanceUntilTowelDrawn(presentation, originalTowel);
                Assert.That(
                    presentation.CurrentClipKind,
                    Is.EqualTo(BarBartenderClipKind.Wipe));
                Assert.That(serviceTowel.enabled, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void AdvanceUntilTowelStored(
            BarBartenderPresentation presentation, CityPedestrianHandPropRegistry original)
        {
            bool reachedBack = false;
            for (int step = 0; step < 90 && !presentation.IsTowelServiceReady; step++)
            {
                presentation.Advance(.02f);
                BarBartenderTowelPresentation towel = presentation.TowelPresentation;
                Assert.That(towel.Towel, Is.SameAs(original));
                if (towel.State == BarBartenderTowelState.Stowing)
                    Assert.That(towel.IsTowelVisible, Is.True);
                else if (towel.State == BarBartenderTowelState.Stowed)
                {
                    if (!reachedBack)
                    {
                        Assert.That(towel.HandBehindBack, Is.True);
                        Assert.That(towel.LastGripError, Is.LessThanOrEqualTo(.003f));
                        reachedBack = true;
                    }
                    Assert.That(towel.IsTowelVisible, Is.False);
                }
            }
            Assert.That(reachedBack && presentation.IsTowelServiceReady, Is.True,
                $"The towel never completed stow: {presentation.TowelPresentation.State}, " +
                $"grip={presentation.TowelPresentation.LastGripError:F4}.");
        }

        private static void AdvanceUntilTowelDrawn(
            BarBartenderPresentation presentation, CityPedestrianHandPropRegistry original)
        {
            bool sawCloth = false;
            for (int step = 0; step < 90 && !presentation.IsTowelWipeReady; step++)
            {
                presentation.Advance(.02f);
                BarBartenderTowelPresentation towel = presentation.TowelPresentation;
                Assert.That(towel.Towel, Is.SameAs(original));
                if (!sawCloth && towel.IsTowelVisible)
                {
                    Assert.That(towel.HandBehindBack, Is.True,
                        "The same towel must reappear in the hand behind the back.");
                    Assert.That(towel.LastGripError, Is.LessThanOrEqualTo(.003f));
                    sawCloth = true;
                }
            }
            Assert.That(sawCloth && presentation.IsTowelWipeReady, Is.True,
                $"The towel never completed draw: {presentation.TowelPresentation.State}, " +
                $"grip={presentation.TowelPresentation.LastGripError:F4}.");
            Assert.That(presentation.TowelPresentation.WipeContactError, Is.LessThanOrEqualTo(.003f));
        }

        [Test]
        public void OrdinaryWalkClip_ContainsVisibleFootTravel()
        {
            GameObject instance = CreateBartender();
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
                instanceRegistry.TryGetClip(BarBartenderClipKind.Walk, out AnimationClip walk, out _);
                walk.SampleAnimation(
                    instanceRegistry.Animator.gameObject,
                    0f);
                Vector3 leftStart = leftFoot.position;
                Vector3 rightStart = rightFoot.position;
                walk.SampleAnimation(
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
            var root = new GameObject("Bartender Attention Root");
            try
            {
                GameObject glancingObject = CreateBartender(root.transform);
                GameObject twinObject = CreateBartender(root.transform);
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

        private static GameObject CreateBartender(Transform parent = null)
        {
            var actor = DefaultNpcFactory.CreateForCharacter(parent, DefaultNpcPopulation.BarBartender);
            CityPortCrew.AlignWorkerModelWithPlacement(actor);
            actor.ReleaseAnimation();
            actor.gameObject.AddComponent<BarBartenderAssetRegistry>().Configure(actor);
            return actor.gameObject;
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
