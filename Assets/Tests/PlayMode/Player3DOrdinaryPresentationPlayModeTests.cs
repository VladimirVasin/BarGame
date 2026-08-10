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
        private Mesh bakedFootMesh;
        private readonly List<Vector3> bakedFootVertices =
            new List<Vector3>();

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

            if (bakedFootMesh != null)
            {
                Object.Destroy(bakedFootMesh);
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
            float previousBlend = presentation.LocomotionBlend;
            bool sawStartTransition = false;
            while (presentation.LocomotionBlend < 0.95f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                float currentBlend = presentation.LocomotionBlend;
                Assert.That(
                    currentBlend,
                    Is.GreaterThanOrEqualTo(previousBlend - 0.002f),
                    "Idle-to-Walk blending must advance monotonically.");
                sawStartTransition |= currentBlend > 0.02f &&
                                      currentBlend < 0.94f;
                previousBlend = currentBlend;
            }

            Assert.That(
                presentation.CurrentLocomotionState,
                Is.EqualTo(Player3DLocomotionState.Walk));
            Assert.That(presentation.LocomotionBlend, Is.GreaterThan(0.9f));
            Assert.That(
                sawStartTransition,
                Is.True,
                "Starting movement must visibly crossfade through an " +
                "intermediate Idle/Walk weight.");

            float visibleWalkWeight = presentation.LocomotionBlend;
            presentation.SetMotion(Vector3.zero);
            Assert.That(
                presentation.LocomotionBlend,
                Is.EqualTo(visibleWalkWeight).Within(0.0001f),
                "Releasing movement must not snap the visible Walk weight.");
            deadline = Time.realtimeSinceStartup + 1f;
            previousBlend = presentation.LocomotionBlend;
            bool sawStopTransition = false;
            while (presentation.LocomotionBlend > 0.05f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                float currentBlend = presentation.LocomotionBlend;
                Assert.That(
                    currentBlend,
                    Is.LessThanOrEqualTo(previousBlend + 0.002f),
                    "Walk-to-Idle blending must recede monotonically.");
                sawStopTransition |= currentBlend < visibleWalkWeight - 0.02f &&
                                     currentBlend > 0.06f;
                previousBlend = currentBlend;
            }

            Assert.That(
                presentation.CurrentLocomotionState,
                Is.EqualTo(Player3DLocomotionState.Idle));
            Assert.That(presentation.LocomotionBlend, Is.LessThan(0.1f));
            Assert.That(
                sawStopTransition,
                Is.True,
                "Stopping movement must visibly crossfade through an " +
                "intermediate Walk/Idle weight.");

            AssertAuthoredLocomotionJointRanges(
                presentation,
                presentation.Registry);
        }

        [UnityTest]
        public IEnumerator MaximumIntoxicationWalk_KeepsVisibleRigAnchored()
        {
            cameraObject = new GameObject("Player3D Grounding Test Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;

            PlayerRuntime player = PlayerFactory.Create(
                null,
                Vector3.up * PlayerFactory.GroundedRootOffset,
                camera,
                null,
                null);
            playerObject = player.GameObject;
            player.Motor.enabled = false;
            yield return null;

            var presentation =
                (Player3DCharacterPresentation)player.Visual;
            Player3DAssetRegistry registry = presentation.Registry;
            bakedFootMesh = new Mesh
            {
                name = "Player3D Grounding Test Mesh"
            };
            Vector3 actorPosition = player.GameObject.transform.position;
            Vector3 modelLocalPosition = registry.ModelRoot.localPosition;
            float groundY = actorPosition.y -
                            PlayerFactory.GroundedRootOffset;
            presentation.ReapplyLatePresentationPose();
            float neutralMinimumY = GetLowestVisibleFootY(registry);
            Assert.That(
                neutralMinimumY,
                Is.InRange(
                    groundY - 0.005f,
                    groundY + PlayerFactory.GroundedRootOffset + 0.005f),
                "The production neutral boot geometry must begin near the " +
                "ground plane.");
            presentation.SetMotion(
                Vector3.forward *
                Player3DCharacterPresentation.FullWalkSpeed);
            float deadline = Time.realtimeSinceStartup + 1f;
            while (presentation.LocomotionBlend < 0.99f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                presentation.LocomotionBlend,
                Is.GreaterThanOrEqualTo(0.99f),
                "The grounding regression must sample the full Walk pose.");

            const float WalkCycleSampleSeconds = 1.05f;
            const float IntoxicationStatusSampleSeconds = 6.5f;
            float ordinaryMinimumY = float.PositiveInfinity;
            float ordinaryMaximumY = float.NegativeInfinity;
            float ordinaryMinimumPelvisX = float.PositiveInfinity;
            float ordinaryMaximumPelvisX = float.NegativeInfinity;
            float ordinaryMinimumPelvisZ = float.PositiveInfinity;
            float ordinaryMaximumPelvisZ = float.NegativeInfinity;
            float sampleEnd = Time.realtimeSinceStartup +
                              WalkCycleSampleSeconds;
            while (Time.realtimeSinceStartup < sampleEnd)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
                float lowestVisibleFootY =
                    GetLowestVisibleFootY(registry);
                ordinaryMinimumY = Mathf.Min(
                    ordinaryMinimumY,
                    lowestVisibleFootY);
                ordinaryMaximumY = Mathf.Max(
                    ordinaryMaximumY,
                    lowestVisibleFootY);
                Vector3 actorLocalPelvis =
                    player.GameObject.transform.InverseTransformPoint(
                        registry.Anchors.Pelvis.position);
                ordinaryMinimumPelvisX = Mathf.Min(
                    ordinaryMinimumPelvisX,
                    actorLocalPelvis.x);
                ordinaryMaximumPelvisX = Mathf.Max(
                    ordinaryMaximumPelvisX,
                    actorLocalPelvis.x);
                ordinaryMinimumPelvisZ = Mathf.Min(
                    ordinaryMinimumPelvisZ,
                    actorLocalPelvis.z);
                ordinaryMaximumPelvisZ = Mathf.Max(
                    ordinaryMaximumPelvisZ,
                    actorLocalPelvis.z);
            }

            presentation.SetIntoxication(1f);
            deadline = Time.realtimeSinceStartup + 1f;
            while (presentation.IntoxicationAmount < 0.99f &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(
                presentation.IntoxicationAmount,
                Is.GreaterThanOrEqualTo(0.99f),
                "The grounding regression must reach maximum intoxication.");

            float intoxicatedMinimumY = float.PositiveInfinity;
            float intoxicatedMaximumY = float.NegativeInfinity;
            float intoxicatedMinimumPelvisX = float.PositiveInfinity;
            float intoxicatedMaximumPelvisX = float.NegativeInfinity;
            float intoxicatedMinimumPelvisZ = float.PositiveInfinity;
            float intoxicatedMaximumPelvisZ = float.NegativeInfinity;
            sampleEnd = Time.realtimeSinceStartup +
                        IntoxicationStatusSampleSeconds;
            while (Time.realtimeSinceStartup < sampleEnd)
            {
                yield return null;
                presentation.ReapplyLatePresentationPose();
                float lowestVisibleFootY =
                    GetLowestVisibleFootY(registry);
                intoxicatedMinimumY = Mathf.Min(
                    intoxicatedMinimumY,
                    lowestVisibleFootY);
                intoxicatedMaximumY = Mathf.Max(
                    intoxicatedMaximumY,
                    lowestVisibleFootY);
                Vector3 actorLocalPelvis =
                    player.GameObject.transform.InverseTransformPoint(
                        registry.Anchors.Pelvis.position);
                intoxicatedMinimumPelvisX = Mathf.Min(
                    intoxicatedMinimumPelvisX,
                    actorLocalPelvis.x);
                intoxicatedMaximumPelvisX = Mathf.Max(
                    intoxicatedMaximumPelvisX,
                    actorLocalPelvis.x);
                intoxicatedMinimumPelvisZ = Mathf.Min(
                    intoxicatedMinimumPelvisZ,
                    actorLocalPelvis.z);
                intoxicatedMaximumPelvisZ = Mathf.Max(
                    intoxicatedMaximumPelvisZ,
                    actorLocalPelvis.z);
            }

            Vector3 pelvisBeforeReapply = registry.Anchors.Pelvis.position;
            Vector3 leftFootBeforeReapply =
                registry.Anchors.LeftFoot.position;
            Vector3 rightFootBeforeReapply =
                registry.Anchors.RightFoot.position;
            presentation.ReapplyLatePresentationPose();

            Assert.That(
                ordinaryMinimumY,
                Is.GreaterThanOrEqualTo(groundY - 0.005f),
                "The authored ordinary Walk must remain above the floor.");
            Assert.That(
                ordinaryMaximumY,
                Is.LessThanOrEqualTo(neutralMinimumY + 0.005f),
                "The ordinary Walk must keep a visible boot planted in " +
                "every sampled pose.");
            Assert.That(
                intoxicatedMinimumY,
                Is.GreaterThanOrEqualTo(ordinaryMinimumY - 0.005f),
                "The additive intoxication pose must not push either " +
                "visible boot below the authored Walk grounding.");
            Assert.That(
                intoxicatedMinimumY,
                Is.GreaterThanOrEqualTo(groundY - 0.005f),
                "Maximum intoxication must not push the visible player " +
                "through the floor.");
            Assert.That(
                intoxicatedMaximumY,
                Is.LessThanOrEqualTo(neutralMinimumY + 0.005f),
                "Maximum intoxication must keep a visible boot planted in " +
                "every sampled pose.");
            const float HorizontalDriftTolerance = 0.005f;
            Assert.That(
                intoxicatedMinimumPelvisX,
                Is.GreaterThanOrEqualTo(
                    ordinaryMinimumPelvisX - HorizontalDriftTolerance),
                "Intoxication must not slide the whole rig sideways.");
            Assert.That(
                intoxicatedMaximumPelvisX,
                Is.LessThanOrEqualTo(
                    ordinaryMaximumPelvisX + HorizontalDriftTolerance),
                "Intoxication must not slide the whole rig sideways.");
            Assert.That(
                intoxicatedMinimumPelvisZ,
                Is.GreaterThanOrEqualTo(
                    ordinaryMinimumPelvisZ - HorizontalDriftTolerance),
                "Intoxication must not slide the whole rig forward or back.");
            Assert.That(
                intoxicatedMaximumPelvisZ,
                Is.LessThanOrEqualTo(
                    ordinaryMaximumPelvisZ + HorizontalDriftTolerance),
                "Intoxication must not slide the whole rig forward or back.");
            Assert.That(
                player.GameObject.transform.position,
                Is.EqualTo(actorPosition)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                registry.ModelRoot.localPosition,
                Is.EqualTo(modelLocalPosition)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                registry.Anchors.Pelvis.position,
                Is.EqualTo(pelvisBeforeReapply)
                    .Using(Vector3ComparerWithEqualsOperator.Instance),
                "Reapplying the additive pose must not accumulate a pelvis " +
                "offset.");
            Assert.That(
                registry.Anchors.LeftFoot.position,
                Is.EqualTo(leftFootBeforeReapply)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(
                registry.Anchors.RightFoot.position,
                Is.EqualTo(rightFootBeforeReapply)
                    .Using(Vector3ComparerWithEqualsOperator.Instance));
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

        private float GetLowestVisibleFootY(
            Player3DAssetRegistry registry)
        {
            float lowestY = float.PositiveInfinity;
            for (int index = 0;
                 index < registry.MeshBindings.Count;
                 index++)
            {
                Player3DMeshBinding binding =
                    registry.MeshBindings[index];
                if (binding == null ||
                    (binding.BoneName != "foot.L" &&
                     binding.BoneName != "foot.R") ||
                    binding.Renderer == null ||
                    !binding.Renderer.enabled ||
                    !(binding.Renderer is SkinnedMeshRenderer renderer))
                {
                    continue;
                }

                bakedFootMesh.Clear(false);
                renderer.BakeMesh(bakedFootMesh, true);
                bakedFootVertices.Clear();
                bakedFootMesh.GetVertices(bakedFootVertices);
                for (int vertexIndex = 0;
                     vertexIndex < bakedFootVertices.Count;
                     vertexIndex++)
                {
                    Vector3 vertex = bakedFootVertices[vertexIndex];
                    Vector3 worldVertex =
                        renderer.transform.TransformPoint(vertex);
                    lowestY = Mathf.Min(lowestY, worldVertex.y);
                }
            }

            Assert.That(
                float.IsPositiveInfinity(lowestY),
                Is.False,
                "The production registry must expose visible foot meshes.");
            return lowestY;
        }

        private static void AssertAuthoredLocomotionJointRanges(
            Player3DCharacterPresentation presentation,
            Player3DAssetRegistry registry)
        {
            Transform chest = registry.Anchors.Chest;
            Transform head = registry.Anchors.Head;
            Transform leftForearm = GetPartBone(
                registry,
                Player3DAnatomicalPart.LeftForearm);
            Transform rightForearm = GetPartBone(
                registry,
                Player3DAnatomicalPart.RightForearm);
            Transform leftShin = GetPartBone(
                registry,
                Player3DAnatomicalPart.LeftShin);
            Transform rightShin = GetPartBone(
                registry,
                Player3DAnatomicalPart.RightShin);
            Transform leftFoot = GetPartBone(
                registry,
                Player3DAnatomicalPart.LeftFoot);
            Transform rightFoot = GetPartBone(
                registry,
                Player3DAnatomicalPart.RightFoot);
            Assert.That(chest, Is.Not.Null);
            Assert.That(head, Is.Not.Null);
            Assert.That(leftForearm, Is.Not.Null);
            Assert.That(rightForearm, Is.Not.Null);
            Assert.That(leftShin, Is.Not.Null);
            Assert.That(rightShin, Is.Not.Null);
            Assert.That(leftFoot, Is.Not.Null);
            Assert.That(rightFoot, Is.Not.Null);

            Assert.That(presentation.TryBeginClip("Relaxed"), Is.True);
            presentation.SampleActiveClip(0.5f);
            Quaternion relaxedChest = chest.localRotation;
            Quaternion relaxedHead = head.localRotation;
            Quaternion relaxedLeftForearm = leftForearm.localRotation;
            Quaternion relaxedRightForearm = rightForearm.localRotation;
            Quaternion relaxedLeftShin = leftShin.localRotation;
            Quaternion relaxedRightShin = rightShin.localRotation;
            Quaternion relaxedLeftFoot = leftFoot.localRotation;
            Quaternion relaxedRightFoot = rightFoot.localRotation;
            presentation.EndClip();

            Assert.That(presentation.TryBeginClip("Idle"), Is.True);
            presentation.SampleActiveClip(0.16f);
            Assert.That(
                Quaternion.Angle(relaxedChest, chest.localRotation),
                Is.GreaterThan(2f),
                "Idle must visibly expand and settle the upper body.");
            Assert.That(
                Quaternion.Angle(relaxedHead, head.localRotation),
                Is.GreaterThan(1f),
                "Idle must include a readable head counter-motion.");
            Assert.That(
                Quaternion.Angle(
                    relaxedLeftForearm,
                    leftForearm.localRotation),
                Is.GreaterThan(3f),
                "Idle must not leave the arms frozen below the shoulder.");
            presentation.EndClip();

            Assert.That(presentation.TryBeginClip("Walk"), Is.True);
            presentation.SampleActiveClip(0f);
            Assert.That(
                Quaternion.Angle(
                    relaxedLeftForearm,
                    leftForearm.localRotation),
                Is.GreaterThan(10f),
                "The rear-swing elbow must remain visibly bent.");
            Assert.That(
                Quaternion.Angle(
                    relaxedRightForearm,
                    rightForearm.localRotation),
                Is.GreaterThan(18f),
                "The forward-swing elbow must flex independently.");
            Assert.That(
                Quaternion.Angle(relaxedLeftFoot, leftFoot.localRotation),
                Is.GreaterThan(7f),
                "The leading boot must articulate at the ankle.");

            presentation.SampleActiveClip(0.25f);
            Assert.That(
                Quaternion.Angle(relaxedRightShin, rightShin.localRotation),
                Is.GreaterThan(35f),
                "The right swing leg must flex clearly at the knee.");

            presentation.SampleActiveClip(0.5f);
            Assert.That(
                Quaternion.Angle(relaxedRightFoot, rightFoot.localRotation),
                Is.GreaterThan(7f),
                "The opposite leading boot must articulate at the ankle.");

            presentation.SampleActiveClip(0.75f);
            Assert.That(
                Quaternion.Angle(relaxedLeftShin, leftShin.localRotation),
                Is.GreaterThan(35f),
                "The left swing leg must flex clearly at the knee.");
            presentation.EndClip();
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
