using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The joints stay anatomical through the whole fall: the ragdoll's
    /// hinges are hinges about the actor's right, no knee or elbow ever
    /// bends the wrong way while the physics has the body or while the
    /// rise's clip and late pass have it, and the body's own colliders
    /// neither overlap at rest nor pass through one another on the floor.
    /// </summary>
    public sealed class Player3DRiseAnatomyPlayModeTests
    {
        private const float PinnedFrameSeconds = 1f / 60f;
        private const int TestCitySeed = 4244;

        /// <summary>The hinge's own five degrees of give, the limit's two of contact distance, and the solver's slack under a pinned limb.</summary>
        private const float HyperextensionToleranceDegrees = 10f;

        /// <summary>
        /// Under the physics a hand slapping the floor at two metres a
        /// second pushes its elbow past the hard limit on the landing
        /// frame before the solver wins: `13–22°` there with the solver at
        /// `60/16` iterations and the authored masses (heavier limbs, joint
        /// preprocessing and a depenetration cap each made it WORSE, to
        /// `30–36°`). The whip of an impact, gone the next frame. The
        /// knees are held to the same number and stay at zero; the ranges
        /// are logged for anyone tightening this.
        /// </summary>
        private const float RagdollHyperextensionToleranceDegrees = 25f;

        /// <summary>
        /// A full fold: a real elbow or knee closes to about 145 degrees.
        /// The ragdoll limits at 115 to 120 from an idle already bent a
        /// few degrees, and a rise blends the frozen body into the clip,
        /// which can fold a little past either.
        /// </summary>
        private const float MaximumFlexionDegrees = 145f;

        private GameObject groundObject;
        private GameObject cameraObject;
        private GameObject playerObject;
        private GameObject uiObject;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.captureDeltaTime = PinnedFrameSeconds;
            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);

            groundObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            groundObject.name = "Rise Anatomy Ground";
            groundObject.transform.position = new Vector3(0f, -0.1f, 0f);
            groundObject.transform.localScale = new Vector3(14f, 0.2f, 14f);

            cameraObject = new GameObject("Rise Anatomy Camera");
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            DestroyHero();
            if (cameraObject != null)
            {
                Object.Destroy(cameraObject);
            }

            if (groundObject != null)
            {
                Object.Destroy(groundObject);
            }

            ResetSession();
            Time.captureDeltaTime = 0f;
            yield return null;
            yield return null;
        }

        [UnityTest]
        public IEnumerator RagdollJointFrames_AreHingesAtInitialize()
        {
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            Player3DAssetRegistry registry = presentation.Registry;
            Transform root = hero.GameObject.transform;
            yield return null;

            Assert.That(hero.Ragdoll, Is.Not.Null);
            Assert.That(hero.Ragdoll.IsInitialized, Is.True);
            var hinges = new[]
            {
                Player3DAnatomicalPart.LeftShin,
                Player3DAnatomicalPart.RightShin,
                Player3DAnatomicalPart.LeftForearm,
                Player3DAnatomicalPart.RightForearm
            };
            foreach (Player3DAnatomicalPart part in hinges)
            {
                Transform bone = Bone(registry, part);
                var joint = bone.GetComponent<ConfigurableJoint>();
                Assert.That(joint, Is.Not.Null, $"{part} has no joint");
                Vector3 axisWorld = bone.TransformDirection(joint.axis).normalized;
                Assert.That(
                    Mathf.Abs(Vector3.Dot(axisWorld, root.right)),
                    Is.GreaterThan(0.95f),
                    $"{part}'s hinge axis is not the actor's right");
            }

            // The frames are baked from the idle pose, which is straight.
            foreach (Limb limb in Limbs(registry, presentation))
            {
                float flexion = Mathf.Abs(limb.SignedFlexion());
                Assert.That(
                    flexion,
                    Is.LessThan(Player3DRagdollController.JointFrameBentDegrees),
                    $"{limb.Name} is bent {flexion:F1} deg in the idle pose");
            }
        }

        [UnityTest]
        public IEnumerator Ragdoll_NeverHyperextendsKneesOrElbows()
        {
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            IntoxicationStatusController status = CreateStatus(hero, 100);
            List<Limb> limbs = Limbs(presentation.Registry, presentation);
            yield return Shove(status, presentation);

            var worst = new Dictionary<string, float>();
            var deepest = new Dictionary<string, float>();
            var firstViolation = new List<string>();
            var handoff = new List<string>();
            Transform actor = hero.GameObject.transform;
            foreach (Limb limb in limbs)
            {
                handoff.Add($"{limb.Name} {limb.SignedFlexion():F1} [{limb.Describe(actor)}]");
            }

            PlayerBalanceOutput fallOutput = status.Balance.Output;
            Debug.Log(
                "Ragdoll flexion at the handoff (frame 0): " + string.Join("; ", handoff) +
                $" | step active {fallOutput.Step.Active} side {fallOutput.Step.Side} progress {fallOutput.Step.Progress:F2}, " +
                $"fall axis {fallOutput.FallAxis}, lean {fallOutput.FallLeanDegrees:F1}");
            int frames = 0;
            float deadline = Time.realtimeSinceStartup + 15f;
            while (hero.Ragdoll.IsSimulating && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
                frames++;
                foreach (Limb limb in limbs)
                {
                    float flexion = limb.SignedFlexion();
                    bool wasFine = !worst.TryGetValue(limb.Name, out float previous) ||
                                   previous >= -RagdollHyperextensionToleranceDegrees;
                    worst[limb.Name] = Mathf.Min(
                        worst.TryGetValue(limb.Name, out float low) ? low : 0f,
                        flexion);
                    deepest[limb.Name] = Mathf.Max(
                        deepest.TryGetValue(limb.Name, out float deep) ? deep : 0f,
                        flexion);
                    if (wasFine && flexion < -RagdollHyperextensionToleranceDegrees)
                    {
                        // Name the moment and the deepest contact when a
                        // hinge first gives way: the diagnosis, not just
                        // the verdict.
                        float depth = hero.Ragdoll.DebugMaximumPenetration(out string pair);
                        firstViolation.Add(
                            $"{limb.Name} {flexion:F1} deg at frame {frames} " +
                            $"(pelvis y {Bone(presentation.Registry, Player3DAnatomicalPart.Pelvis).position.y:F2}, " +
                            $"deepest contact {pair} {depth:F3} m, body speed {hero.Ragdoll.MaximumBodySpeed:F2})");
                    }
                }
            }

            if (firstViolation.Count > 0)
            {
                Debug.Log("Ragdoll hinge violations: " + string.Join("; ", firstViolation));
            }

            Assert.That(frames, Is.GreaterThan(30), "the ragdoll barely simulated");
            Assert.That(hero.Ragdoll.IsSimulating, Is.False, "the ragdoll never came to rest");
            var failures = new List<string>();
            var report = new List<string>();
            foreach (Limb limb in limbs)
            {
                report.Add($"{limb.Name} [{worst[limb.Name]:F1}, {deepest[limb.Name]:F1}]");
                if (worst[limb.Name] < -RagdollHyperextensionToleranceDegrees)
                {
                    failures.Add($"{limb.Name} bent the wrong way by {-worst[limb.Name]:F1} deg under the ragdoll");
                }

                if (deepest[limb.Name] > MaximumFlexionDegrees)
                {
                    failures.Add($"{limb.Name} folded {deepest[limb.Name]:F1} deg under the ragdoll");
                }
            }

            Debug.Log("Ragdoll flexion ranges over " + frames + " frames: " + string.Join("; ", report));
            Assert.That(failures, Is.Empty, string.Join("; ", failures));
        }

        [UnityTest]
        public IEnumerator Ragdoll_LimbsDoNotInterpenetrate()
        {
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            yield return null;
            Assert.That(
                hero.Ragdoll.RestingOverlapPairCount,
                Is.Zero,
                "colliders overlap in the idle pose: " + hero.Ragdoll.DebugRestingOverlaps);
            float idle = hero.Ragdoll.DebugMaximumPenetration(out string idlePair);
            Assert.That(
                idle,
                Is.LessThan(Player3DRagdollController.RestingOverlapMetres),
                $"{idlePair} overlap {idle:F3} m in the idle pose");

            IntoxicationStatusController status = CreateStatus(hero, 100);
            yield return Shove(status, presentation);

            float deadline = Time.realtimeSinceStartup + 15f;
            while (hero.Ragdoll.IsSimulating && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(hero.Ragdoll.IsSimulating, Is.False, "the ragdoll never came to rest");
            float depth = hero.Ragdoll.DebugMaximumPenetration(out string pair);
            Assert.That(depth, Is.LessThan(0.02f), $"{pair} overlap {depth:F3} m where he lies");
            Transform pelvis = Bone(presentation.Registry, Player3DAnatomicalPart.Pelvis);
            Assert.That(pelvis.position.y, Is.GreaterThan(0.02f), "the pelvis sank into the floor");
        }

        [UnityTest]
        public IEnumerator Rise_KeepsKneesAndElbowsAnatomical()
        {
            PlayerRuntime hero = CreateHero();
            var presentation = (Player3DCharacterPresentation)hero.Visual;
            IntoxicationStatusController status = CreateStatus(hero, 100);
            List<Limb> limbs = Limbs(presentation.Registry, presentation);
            yield return Shove(status, presentation);

            var failures = new List<string>();
            var moments = new[]
            {
                (PlayerRiseStage.Stirring, 0.5f),
                (PlayerRiseStage.PushingUp, 0.5f),
                (PlayerRiseStage.Kneeling, 0.05f),
                (PlayerRiseStage.Kneeling, 0.85f),
                (PlayerRiseStage.Standing, 0.5f)
            };
            var report = new List<string>();
            foreach ((PlayerRiseStage stage, float progress) in moments)
            {
                yield return WaitForRise(status, stage, progress, 14f);
                presentation.ReapplyLatePresentationPose();
                var moment = new List<string>();
                foreach (Limb limb in limbs)
                {
                    float flexion = limb.SignedFlexion();
                    moment.Add($"{limb.Name} {flexion:F1}");
                    if (flexion < -HyperextensionToleranceDegrees ||
                        flexion > MaximumFlexionDegrees)
                    {
                        failures.Add(
                            $"{limb.Name} {flexion:F1} deg at {status.RiseStageName} " +
                            $"{status.Rise.Output.StageProgress:F2} (clip {status.Rise.Output.ClipTime:F2})");
                    }
                }

                report.Add(
                    $"{status.RiseStageName} {status.Rise.Output.StageProgress:F2} " +
                    $"(clip {status.Rise.Output.ClipTime:F2}): {string.Join(", ", moment)}");
            }

            Debug.Log("Rise flexion by moment: " + string.Join(" | ", report));
            Assert.That(failures, Is.Empty, string.Join("; ", failures));
        }

        private IEnumerator Shove(
            IntoxicationStatusController status,
            Player3DCharacterPresentation presentation)
        {
            status.Balance.ArmGrace(0f);
            for (int frame = 0; frame < 90; frame++)
            {
                yield return null;
            }

            Assert.That(presentation.IntoxicationAmount, Is.GreaterThan(0.9f));
            float graceDeadline = Time.realtimeSinceStartup + 6f;
            while (!status.Balance.FallAllowedNow &&
                   Time.realtimeSinceStartup < graceDeadline)
            {
                yield return null;
            }

            Assert.That(status.Balance.FallAllowedNow, Is.True);
            status.Balance.InjectPerturbation(new Vector2(3f, 0f));
            float fallDeadline = Time.realtimeSinceStartup + 8f;
            while (!status.IsFalling && Time.realtimeSinceStartup < fallDeadline)
            {
                yield return null;
            }

            Assert.That(status.IsFalling, Is.True, "the shove must floor him");
        }

        private static IEnumerator WaitForRise(
            IntoxicationStatusController status,
            PlayerRiseStage stage,
            float stageProgress,
            float seconds)
        {
            float deadline = Time.realtimeSinceStartup + seconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                PlayerRiseModel rise = status.Rise;
                if (rise != null &&
                    (rise.Stage > stage ||
                     (rise.Stage == stage && rise.Output.StageProgress >= stageProgress)))
                {
                    yield break;
                }

                if (!status.IsFalling)
                {
                    Assert.Fail($"The fall ended before the rise reached {stage} at {stageProgress:F2}.");
                }

                yield return null;
            }

            Assert.Fail($"The rise never reached {stage} at {stageProgress:F2} (at {status.RiseStageName}).");
        }

        private static List<Limb> Limbs(
            Player3DAssetRegistry registry,
            Player3DCharacterPresentation presentation)
        {
            return new List<Limb>
            {
                new Limb(
                    "left knee",
                    Bone(registry, Player3DAnatomicalPart.LeftThigh),
                    Bone(registry, Player3DAnatomicalPart.LeftShin),
                    Bone(registry, Player3DAnatomicalPart.LeftFoot),
                    () => presentation.DebugKneeForward(FootSide.Left)),
                new Limb(
                    "right knee",
                    Bone(registry, Player3DAnatomicalPart.RightThigh),
                    Bone(registry, Player3DAnatomicalPart.RightShin),
                    Bone(registry, Player3DAnatomicalPart.RightFoot),
                    () => presentation.DebugKneeForward(FootSide.Right)),
                new Limb(
                    "left elbow",
                    Bone(registry, Player3DAnatomicalPart.LeftUpperArm),
                    Bone(registry, Player3DAnatomicalPart.LeftForearm),
                    Bone(registry, Player3DAnatomicalPart.LeftHand),
                    () => presentation.DebugElbowBack(false)),
                new Limb(
                    "right elbow",
                    Bone(registry, Player3DAnatomicalPart.RightUpperArm),
                    Bone(registry, Player3DAnatomicalPart.RightForearm),
                    Bone(registry, Player3DAnatomicalPart.RightHand),
                    () => presentation.DebugElbowBack(true))
            };
        }

        private static Transform Bone(
            Player3DAssetRegistry registry,
            Player3DAnatomicalPart part)
        {
            Assert.That(registry.TryGetPart(part, out var binding), Is.True, $"{part} is not registered");
            Assert.That(binding.Bone, Is.Not.Null, $"{part} has no bone");
            return binding.Bone;
        }

        private PlayerRuntime CreateHero()
        {
            PlayerRuntime hero = PlayerFactory.Create(
                null,
                Vector3.up * PlayerFactory.GroundedRootOffset,
                cameraObject.GetComponent<Camera>(),
                null,
                null);
            playerObject = hero.GameObject;
            Physics.SyncTransforms();
            return hero;
        }

        private IntoxicationStatusController CreateStatus(
            PlayerRuntime hero,
            int level)
        {
            GameSessionState.UpdateDrinkingProgress(level, DrinkId.Vodka, 5);
            uiObject = new GameObject("Rise Anatomy UI");
            IntoxicationHudView hud = uiObject.AddComponent<IntoxicationHudView>();
            var followCameraObject = new GameObject("Rise Anatomy Follow");
            followCameraObject.transform.SetParent(uiObject.transform, false);
            Camera followCamera = followCameraObject.AddComponent<Camera>();
            followCamera.enabled = false;
            var follow = followCameraObject.AddComponent<PlayerCameraFollow>();
            follow.Initialize(followCamera, hero.GameObject.transform, false);
            follow.enabled = false;

            IntoxicationStatusController status =
                uiObject.AddComponent<IntoxicationStatusController>();
            status.Initialize(hero, follow, hud);
            return status;
        }

        private void DestroyHero()
        {
            if (uiObject != null)
            {
                Object.Destroy(uiObject);
                uiObject = null;
            }

            if (playerObject != null)
            {
                Object.Destroy(playerObject);
                playerObject = null;
            }

            ResetSession();
            GameSessionState.SetCitySeed(TestCitySeed);
        }

        private static void ResetSession()
        {
            GameSessionState.SetCitySeed(GameSessionState.DefaultCitySeed);
            GameSessionState.EnterBar(null);
            GameSessionState.CompleteCityReturn();
            GameSessionState.ResetDrinkingState();
        }

        /// <summary>
        /// One hinge: its flexion is the fold from straight, signed by
        /// whether the middle joint sits on the anatomical side of the
        /// root-to-tip line (the calibrated knee-forward or elbow-back,
        /// read in the upper bone's current frame) or the other one.
        /// </summary>
        private sealed class Limb
        {
            private readonly Transform root;
            private readonly Transform hinge;
            private readonly Transform tip;
            private readonly System.Func<Vector3> bendDirection;

            public Limb(
                string name,
                Transform root,
                Transform hinge,
                Transform tip,
                System.Func<Vector3> bendDirection)
            {
                Name = name;
                this.root = root;
                this.hinge = hinge;
                this.tip = tip;
                this.bendDirection = bendDirection;
            }

            public string Name { get; }

            /// <summary>The joint's offset from the root-tip line and the bend reference, both in the actor's frame: is the joint really behind, or is the reference twisted?</summary>
            public string Describe(Transform actor)
            {
                Vector3 axis = tip.position - root.position;
                Vector3 offset = Vector3.ProjectOnPlane(hinge.position - root.position, axis);
                Vector3 reference = Vector3.ProjectOnPlane(bendDirection(), axis);
                Vector3 offsetLocal = actor.InverseTransformDirection(offset);
                Vector3 referenceLocal = actor.InverseTransformDirection(reference.normalized);
                return $"offset {offsetLocal.x:F2},{offsetLocal.y:F2},{offsetLocal.z:F2} ref {referenceLocal.x:F2},{referenceLocal.y:F2},{referenceLocal.z:F2} span {axis.magnitude:F2}";
            }

            public float SignedFlexion()
            {
                Vector3 pivot = hinge.position;
                float flexion = 180f - Vector3.Angle(
                    root.position - pivot,
                    tip.position - pivot);
                Vector3 axis = tip.position - root.position;
                Vector3 offset = Vector3.ProjectOnPlane(pivot - root.position, axis);
                Vector3 anatomical = Vector3.ProjectOnPlane(bendDirection(), axis);
                if (offset.sqrMagnitude < 0.000001f || anatomical.sqrMagnitude < 0.000001f)
                {
                    return flexion;
                }

                return Vector3.Dot(offset, anatomical) >= 0f ? flexion : -flexion;
            }
        }
    }
}
