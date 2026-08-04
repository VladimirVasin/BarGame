using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;
using UnityEngine.TestTools.Utils;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class Player3DOrdinaryPresentationPlayModeTests
    {
        private GameObject cameraObject;
        private GameObject playerObject;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (playerObject != null)
            {
                Object.Destroy(playerObject);
            }

            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator FactoryCreatesModular3DPlayerAndDrivesLocomotion()
        {
            cameraObject = new GameObject("Player3D Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;

            PlayerRuntime player = PlayerFactory.Create(
                null,
                Vector3.zero,
                camera,
                null,
                null);
            playerObject = player.GameObject;
            player.Motor.enabled = false;
            yield return null;

            Assert.That(player.GameObject.name, Is.EqualTo("Player"));
            Assert.That(
                player.Visual,
                Is.TypeOf<Player3DCharacterPresentation>());
            var presentation =
                (Player3DCharacterPresentation)player.Visual;
            Assert.That(presentation.Registry, Is.Not.Null);
            Assert.That(
                presentation.Registry.transform.parent,
                Is.EqualTo(player.GameObject.transform));
            Assert.That(
                presentation.Registry.Animator.applyRootMotion,
                Is.False);
            SpriteRenderer[] spriteRenderers =
                player.GameObject.GetComponentsInChildren<
                    SpriteRenderer>(true);
            Assert.That(
                spriteRenderers,
                Is.Empty,
                "The production player hierarchy must not retain a sprite bridge.");

            IReadOnlyList<Player3DAnatomicalPartBinding> parts =
                presentation.Registry.AnatomicalParts;
            Assert.That(parts.Count, Is.EqualTo(16));
            var partRenderers = new HashSet<Renderer>();
            var partObjects = new HashSet<GameObject>();
            for (int index = 0; index < parts.Count; index++)
            {
                Player3DAnatomicalPartBinding part = parts[index];
                Assert.That(part, Is.Not.Null);
                Assert.That(part.Renderer, Is.Not.Null);
                Assert.That(part.Bone, Is.Not.Null);
                Assert.That(partRenderers.Add(part.Renderer), Is.True);
                Assert.That(
                    partObjects.Add(part.Renderer.gameObject),
                    Is.True,
                    "Every core anatomical part must remain an independent object.");
            }

            Assert.That(
                presentation.Renderers.Count,
                Is.GreaterThanOrEqualTo(16));
            for (int index = 0; index < presentation.Renderers.Count; index++)
            {
                Renderer renderer = presentation.Renderers[index];
                Assert.That(renderer, Is.Not.Null);
                Assert.That(renderer.enabled, Is.True);
                Assert.That(
                    renderer.shadowCastingMode,
                    Is.EqualTo(ShadowCastingMode.On));
            }

            presentation.SetMotion(
                Vector3.forward *
                Player3DCharacterPresentation.FullWalkSpeed);
            float deadline = Time.realtimeSinceStartup + 1f;
            while (presentation.LocomotionBlend < 0.95f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                presentation.CurrentLocomotionState,
                Is.EqualTo(Player3DLocomotionState.Walk));
            Assert.That(presentation.LocomotionBlend, Is.GreaterThan(0.9f));

            presentation.SetMotion(Vector3.zero);
            deadline = Time.realtimeSinceStartup + 1f;
            while (presentation.LocomotionBlend > 0.05f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                presentation.CurrentLocomotionState,
                Is.EqualTo(Player3DLocomotionState.Idle));
            Assert.That(presentation.LocomotionBlend, Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator StatusFaceFallsAndContactShadowDrive3DBonesAndCleanUp()
        {
            cameraObject = new GameObject("Player3D Status Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;

            PlayerRuntime player = PlayerFactory.Create(
                null,
                Vector3.zero,
                camera,
                null,
                null);
            playerObject = player.GameObject;
            player.Motor.enabled = false;
            player.GameObject.transform.rotation =
                Quaternion.Euler(0f, 31f, 0f);
            yield return null;

            var presentation =
                (Player3DCharacterPresentation)player.Visual;
            Player3DAssetRegistry registry = presentation.Registry;
            Transform pelvis = registry.Anchors.Pelvis;
            Transform leftEye = FindBone(registry, "face.eye.L");
            Transform leftBrow = FindBone(registry, "face.brow.L");
            Transform leftUpperArm = GetPartBone(
                registry,
                Player3DAnatomicalPart.LeftUpperArm);
            Transform rightUpperArm = GetPartBone(
                registry,
                Player3DAnatomicalPart.RightUpperArm);
            Assert.That(pelvis, Is.Not.Null);
            Assert.That(leftEye, Is.Not.Null);
            Assert.That(leftBrow, Is.Not.Null);
            Assert.That(leftUpperArm, Is.Not.Null);
            Assert.That(rightUpperArm, Is.Not.Null);

            Vector3 neutralEyeScale = leftEye.localScale;
            Quaternion neutralBrowRotation = leftBrow.localRotation;
            Quaternion neutralPelvisRotation = pelvis.localRotation;
            Quaternion neutralLeftArmRotation =
                leftUpperArm.localRotation;
            Quaternion neutralRightArmRotation =
                rightUpperArm.localRotation;
            Quaternion actorHeading =
                player.GameObject.transform.rotation;

            float faceDeadline =
                Time.realtimeSinceStartup +
                PlayerFacialAnimationState.InitialWatchfulDelaySeconds +
                0.65f;
            while (presentation.CurrentFacialExpression !=
                       PlayerFacialExpression.Watchful &&
                   Time.realtimeSinceStartup < faceDeadline)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
            }

            Assert.That(
                presentation.CurrentFacialExpression,
                Is.EqualTo(PlayerFacialExpression.Watchful));
            Assert.That(
                leftEye.localScale.y,
                Is.GreaterThan(neutralEyeScale.y * 1.1f));
            Assert.That(
                Quaternion.Angle(
                    neutralBrowRotation,
                    leftBrow.localRotation),
                Is.GreaterThan(2f));

            presentation.SetIntoxication(1f);
            presentation.SetBalancePose(0.65f);
            float statusDeadline = Time.realtimeSinceStartup + 1f;
            float maximumPelvisAngle = 0f;
            float maximumLeftArmAngle = 0f;
            float maximumRightArmAngle = 0f;
            while ((presentation.IntoxicationAmount < 0.99f ||
                    presentation.BalanceLean < 0.64f) &&
                   Time.realtimeSinceStartup < statusDeadline)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
                maximumPelvisAngle = Mathf.Max(
                    maximumPelvisAngle,
                    Quaternion.Angle(
                        neutralPelvisRotation,
                        pelvis.localRotation));
                maximumLeftArmAngle = Mathf.Max(
                    maximumLeftArmAngle,
                    Quaternion.Angle(
                        neutralLeftArmRotation,
                        leftUpperArm.localRotation));
                maximumRightArmAngle = Mathf.Max(
                    maximumRightArmAngle,
                    Quaternion.Angle(
                        neutralRightArmRotation,
                        rightUpperArm.localRotation));
                Assert.That(
                    Quaternion.Angle(
                        actorHeading,
                        player.GameObject.transform.rotation),
                    Is.LessThan(0.001f));
            }

            Assert.That(
                presentation.IntoxicationAmount,
                Is.GreaterThan(0.95f));
            Assert.That(presentation.BalanceLean, Is.GreaterThan(0.6f));
            Assert.That(maximumPelvisAngle, Is.GreaterThan(4f));
            Assert.That(maximumLeftArmAngle, Is.GreaterThan(4f));
            Assert.That(maximumRightArmAngle, Is.GreaterThan(4f));

            presentation.SetIntoxication(0f);
            presentation.SetBalancePose(0f);
            float neutralDeadline = Time.realtimeSinceStartup + 1f;
            while ((presentation.IntoxicationAmount > 0.01f ||
                    Mathf.Abs(presentation.BalanceLean) > 0.01f) &&
                   Time.realtimeSinceStartup < neutralDeadline)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
            }

            yield return null;
            presentation.ReapplyLatePresentationPose();
            Assert.That(
                Quaternion.Angle(
                    neutralPelvisRotation,
                    pelvis.localRotation),
                Is.LessThan(0.5f));
            Assert.That(
                Quaternion.Angle(
                    neutralLeftArmRotation,
                    leftUpperArm.localRotation),
                Is.LessThan(0.5f));
            Assert.That(
                Quaternion.Angle(
                    neutralRightArmRotation,
                    rightUpperArm.localRotation),
                Is.LessThan(0.5f));

            var sides = new[]
            {
                new FallSideCase(-1f, "Left"),
                new FallSideCase(1f, "Right")
            };
            var phases = new[]
            {
                new FallPhaseCase(
                    PlayerFallAnimationPhase.Falling,
                    "Fall"),
                new FallPhaseCase(
                    PlayerFallAnimationPhase.Down,
                    "Down"),
                new FallPhaseCase(
                    PlayerFallAnimationPhase.Rising,
                    "Rise")
            };

            for (int sideIndex = 0;
                 sideIndex < sides.Length;
                 sideIndex++)
            {
                FallSideCase side = sides[sideIndex];
                presentation.SetFallPose(side.Direction, 1f);
                for (int phaseIndex = 0;
                     phaseIndex < phases.Length;
                     phaseIndex++)
                {
                    FallPhaseCase phase = phases[phaseIndex];
                    presentation.SetFallAnimation(
                        phase.Phase,
                        0.55f);
                    yield return null;

                    Assert.That(presentation.IsClipActive, Is.True);
                    Assert.That(
                        presentation.ActiveClipName,
                        Is.EqualTo(phase.ClipPrefix + side.Suffix));
                    Assert.That(
                        Quaternion.Angle(
                            neutralPelvisRotation,
                            pelvis.localRotation),
                        Is.GreaterThan(2f));

                    Vector3 shadowOffset =
                        player.ContactShadow.ShadowRoot.position -
                        player.GameObject.transform.position;
                    float signedOffset = Vector3.Dot(
                        shadowOffset,
                        player.GameObject.transform.right);
                    Assert.That(
                        signedOffset * side.Direction,
                        Is.GreaterThan(0.2f));
                    Assert.That(
                        player.ContactShadow.ShadowRoot.localScale.x,
                        Is.GreaterThan(PlayerContactShadow.BaseWidth));
                }
            }

            presentation.SetFallPose(0f, 0f);
            presentation.SetFallAnimation(
                PlayerFallAnimationPhase.None,
                0f);
            yield return null;
            // The contact patch samples presentation metrics in LateUpdate.
            // Allow that phase to consume the restored ordinary foot plant
            // before asserting its final dimensions in batch mode.
            yield return null;

            Assert.That(presentation.IsClipActive, Is.False);
            Assert.That(presentation.FallAmount, Is.Zero);
            Assert.That(
                player.ContactShadow.ShadowRoot.localPosition,
                Is.EqualTo(new Vector3(
                    0f,
                    PlayerContactShadow.GroundOffset,
                    0f)).Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                player.ContactShadow.ShadowRoot.localScale.x,
                Is.EqualTo(PlayerContactShadow.BaseWidth).Within(0.001f));
            Assert.That(
                player.ContactShadow.ShadowRoot.localScale.z,
                Is.EqualTo(PlayerContactShadow.BaseDepth).Within(0.001f));

            presentation.SetIntoxication(1f);
            presentation.SetBalancePose(-1f);
            presentation.SetFallPose(-1f, 1f);
            presentation.SetFallAnimation(
                PlayerFallAnimationPhase.Down,
                0.75f);
            presentation.enabled = false;
            yield return null;

            Assert.That(presentation.IsClipActive, Is.False);
            Assert.That(presentation.IntoxicationAmount, Is.Zero);
            Assert.That(presentation.BalanceLean, Is.Zero);
            Assert.That(presentation.FallAmount, Is.Zero);
            Assert.That(
                presentation.CurrentFacialExpression,
                Is.EqualTo(PlayerFacialExpression.Neutral));
            Assert.That(
                leftEye.localScale,
                Is.EqualTo(neutralEyeScale)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                Quaternion.Angle(
                    neutralPelvisRotation,
                    pelvis.localRotation),
                Is.LessThan(0.5f));
            Assert.That(
                Quaternion.Angle(
                    actorHeading,
                    player.GameObject.transform.rotation),
                Is.LessThan(0.001f));
        }

        private static Transform FindBone(
            Player3DAssetRegistry registry,
            string boneName)
        {
            for (int index = 0;
                 index < registry.MeshBindings.Count;
                 index++)
            {
                Player3DMeshBinding binding =
                    registry.MeshBindings[index];
                if (binding != null &&
                    binding.BoneName == boneName &&
                    binding.Bone != null)
                {
                    return binding.Bone;
                }
            }

            return null;
        }

        private static Transform GetPartBone(
            Player3DAssetRegistry registry,
            Player3DAnatomicalPart part)
        {
            return registry.TryGetPart(part, out var binding) &&
                   binding != null
                ? binding.Bone
                : null;
        }

        private readonly struct FallSideCase
        {
            public FallSideCase(float direction, string suffix)
            {
                Direction = direction;
                Suffix = suffix;
            }

            public float Direction { get; }
            public string Suffix { get; }
        }

        private readonly struct FallPhaseCase
        {
            public FallPhaseCase(
                PlayerFallAnimationPhase phase,
                string clipPrefix)
            {
                Phase = phase;
                ClipPrefix = clipPrefix;
            }

            public PlayerFallAnimationPhase Phase { get; }
            public string ClipPrefix { get; }
        }
    }
}
