using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        [UnityTest]
        public IEnumerator Range_CombatBodyInertiaKeepsBothRigsContinuousAndClockBound()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            yield return EnterRange();
            foreach (bool heroActs in new[] { true, false })
            foreach (MeleeSwing swing in new[] { MeleeSwing.Forehand, MeleeSwing.Backhand })
            {
                PlacePair(3f);
                CombatActor actor = heroActs ? root.Hero : root.Opponent;
                string subject = (heroActs ? "hero" : "opponent") +
                    (swing == MeleeSwing.Backhand ? "-backhand" : "");
                yield return null;
                PresentInertiaPose();
                AssertCombatReadySupport(actor, subject);
                var readyPose = new CombatInertiaPose(actor);
                CaptureInertiaFrame(actor, subject, 0);

                actor.State.ObserveLateralCue(swing == MeleeSwing.Backhand ? 1 : 0);
                Assert.That(actor.RequestCharge(), Is.True);
                Assert.That(actor.State.Swing, Is.EqualTo(swing));
                for (int frame = 1; frame <= 20; frame++)
                {
                    root.Tick(1f / 30f);
                    yield return null;
                    PresentInertiaPose();
                    CaptureInertiaFrame(actor, subject, frame);
                }
                var chargedPose = new CombatInertiaPose(actor);
                Assert.That(actor.CombatFearAmount, heroActs ? Is.GreaterThan(.8f) : Is.EqualTo(0f),
                    subject + ": only the hero carries the frightened novice presentation.");
                Assert.That(chargedPose.AngleFrom(readyPose, "spine"), Is.GreaterThan(1f),
                    subject + ": the spine must join the preparation, not leave the arms swinging alone.");
                Assert.That(chargedPose.AngleFrom(readyPose, "chest"), Is.GreaterThan(3f));
                AssertInertiaSamplingStable(actor, subject + " loaded charge");
                float power = actor.State.Charge01;
                Assert.That(actor.ReleaseCharge(), Is.True);
                PresentInertiaPose();
                chargedPose.AssertMatches(actor, .008f, .75f, subject + " charge release");
                Assert.That(actor.State.AttackPower, Is.EqualTo(power).Within(.0001f));
                float pelvisSwing = 0f;
                for (int frame = 21; frame <= 78; frame++)
                {
                    root.Tick(1f / 30f);
                    yield return null;
                    PresentInertiaPose();
                    pelvisSwing = Mathf.Max(pelvisSwing, new CombatInertiaPose(actor).AngleFrom(readyPose, "pelvis"));
                    CaptureInertiaFrame(actor, subject, frame);
                }
                Assert.That(pelvisSwing, Is.GreaterThan(1f),
                    subject + ": the hips must turn through the blow while the loaded charge preserves its shared lower-body dock.");
                Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready));

                // Reverse the guard while it is still moving. Every new action
                // starts at the displayed pose; re-reading it cannot advance it.
                for (int change = 0; change < 4; change++)
                {
                    var before = new CombatInertiaPose(actor);
                    actor.SetBlock(change % 2 == 0);
                    PresentInertiaPose();
                    before.AssertMatches(actor, .008f, .75f, subject + " interrupted guard");
                    for (int frame = 0; frame < 2; frame++)
                    {
                        root.Tick(1f / 30f);
                        yield return null;
                        PresentInertiaPose();
                        CaptureInertiaFrame(actor, subject, 79 + change * 2 + frame);
                    }
                    AssertInertiaSamplingStable(actor, subject + " guard transition");
                }
                actor.SetBlock(true);
                PresentInertiaPose();
                root.Tick(.06f);
                PresentInertiaPose();
                var beforeVelocity = new CombatInertiaPose(actor);
                root.Tick(CombatTestRoot.SimulationStep);
                PresentInertiaPose();
                var movingGuard = new CombatInertiaPose(actor);
                Vector3 enteringVelocity = movingGuard.AngularDeltaFrom(beforeVelocity, "chest");
                Assert.That(enteringVelocity.magnitude, Is.GreaterThan(.005f));
                actor.SetBlock(false);
                PresentInertiaPose();
                root.Tick(CombatTestRoot.SimulationStep);
                PresentInertiaPose();
                Vector3 continuedVelocity = new CombatInertiaPose(actor).AngularDeltaFrom(movingGuard, "chest");
                Assert.That(Vector3.Dot(enteringVelocity, continuedVelocity), Is.GreaterThan(0f),
                    subject + ": an interrupted guard must first carry its existing angular velocity into the new transition; " +
                    "entering=" + enteringVelocity.ToString("F6") + ", continued=" + continuedVelocity.ToString("F6"));
                root.Tick(.3f);
                yield return null;
                PresentInertiaPose();
                Vector3 stepStart = actor.transform.position;
                Assert.That(actor.TryStep(Vector2.left), Is.True);
                for (int frame = 0; frame < 16; frame++)
                {
                    root.Tick(1f / 30f);
                    yield return null;
                    PresentInertiaPose();
                    CaptureInertiaFrame(actor, subject, 87 + frame);
                }
                Vector3 stepTravel = actor.transform.position - stepStart;
                stepTravel.y = 0f;
                Assert.That(stepTravel.magnitude, Is.EqualTo(S.StepDistance).Within(.035f),
                    "Body inertia may not add drift to the committed defensive step.");

                Assert.That(actor.RequestCharge(), Is.True);
                root.Tick(.12f);
                yield return null;
                PresentInertiaPose();
                var pausedPose = new CombatInertiaPose(actor);
                float pausedCharge = actor.State.Charge01;
                Assert.That(root.PauseMenu.Open(), Is.True);
                root.Tick(1f);
                actor.Step(1f);
                yield return null;
                PresentInertiaPose();
                pausedPose.AssertMatches(actor, .002f, .1f, subject + " paused inertia");
                Assert.That(actor.State.Charge01, Is.EqualTo(pausedCharge));
                Assert.That(root.PauseMenu.Cancel(), Is.True);
                yield return WaitFor(() => GameInput.CanRead(GameInputContext.Gameplay), "Pause kept the combat input lock.");
                PlacePair(3f);
                yield return null;
                PresentInertiaPose();
                readyPose.AssertMatches(actor, .008f, .75f, subject + " reset inertia");
                Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready));

                // Contact collection and final presentation must use the same
                // body pose. Save the actual blade at the event, not a proxy ray.
                PlacePair(1.1f);
                yield return null;
                PresentInertiaPose();
                CombatActor target = heroActs ? root.Opponent : root.Hero;
                Transform strikeBase = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeBase");
                Transform strikeTip = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip");
                Vector3 contactBase = Vector3.zero, contactTip = Vector3.zero;
                bool received = false;
                void RememberContact(CombatImpact impact)
                {
                    received = true;
                    contactBase = strikeBase.position;
                    contactTip = strikeTip.position;
                }
                target.ImpactReceived += RememberContact;
                try
                {
                    actor.State.ObserveLateralCue(swing == MeleeSwing.Backhand ? 1 : 0);
                    Assert.That(actor.TryAttack(), Is.True);
                    Assert.That(actor.State.Swing, Is.EqualTo(swing));
                    for (int tick = 0; tick < ContactTicks(120f) + 12 && !received; tick++)
                    {
                        root.Tick(CombatTestRoot.SimulationStep);
                        PresentInertiaPose();
                    }
                    Assert.That(received, Is.True, subject + ": the authored blade never made contact.");
                    Assert.That(target.ReceivedImpactCount, Is.EqualTo(1));
                    Assert.That(actor.State.AttackElapsed, Is.InRange(actor.State.AttackWindupSeconds,
                        actor.State.AttackActiveEnd + CombatTestRoot.SimulationStep));
                    Assert.That(Vector3.Distance(strikeBase.position, contactBase), Is.LessThan(.012f),
                        "The visible base must agree with the pose used to collect the hit.");
                    Assert.That(Vector3.Distance(strikeTip.position, contactTip), Is.LessThan(.012f),
                        "The visible tip must agree with the pose used to collect the hit.");
                    AssertInertiaSamplingStable(actor, subject + " contact freeze");
                    CaptureInertiaFrame(actor, subject + "-contact", 0);
                }
                finally { target.ImpactReceived -= RememberContact; }
            }
            yield return VerifyCombatFootwork();
            yield return VerifyCombatTravelInertia();
            LogAssert.NoUnexpectedReceived();
        }

        private IEnumerator VerifyCombatFootwork()
        {
            // Feed achieved displacement directly, identically for the two rigs.
            // The motor's acceleration remains covered below; these cases isolate
            // foot contacts, direction changes and entering a blow mid-stride.
            Vector3[] directions = { Vector3.forward, Vector3.back, Vector3.left, Vector3.right,
                new Vector3(-1f, 0f, 1f).normalized, new Vector3(1f, 0f, -1f).normalized,
                new Vector3(1f, 0f, 1f).normalized, new Vector3(-1f, 0f, -1f).normalized };
            string[] names = { "advance", "retreat", "left", "right", "advance-left", "retreat-right", "advance-right", "retreat-left" };
            const float delta = 1f / 60f;
            foreach (bool heroActs in new[] { true, false })
            for (int directionIndex = 0; directionIndex < directions.Length; directionIndex++)
            {
                PlacePair(4f);
                CombatActor actor = heroActs ? root.Hero : root.Opponent;
                string subject = (heroActs ? "hero" : "opponent") + "-footwork-" + names[directionIndex];
                yield return null;
                yield return null; // Reset's one-frame handoff must finish before testing the live transition.
                PresentInertiaPose();
                Transform left = CombatBone(actor, "foot.L"), right = CombatBone(actor, "foot.R");
                Vector3 readyLeft = left.position, readyRight = right.position;
                Vector3 previousLeft = left.position, previousRight = right.position;
                Vector3 direction = actor.transform.TransformDirection(directions[directionIndex]);
                bool firstHalf = directionIndex % 2 == 0;
                int travelFrames = firstHalf ? 13 : 39;
                float maximumLift = 0f;
                for (int frame = 0; frame < travelFrames; frame++)
                {
                    AdvanceAchievedCombatTravel(actor, direction * .6f, delta);
                    AssertFootworkSupport(actor, left, right, readyLeft.y, readyRight.y,
                        previousLeft, previousRight, subject + " travelling");
                    maximumLift = Mathf.Max(maximumLift, left.position.y - readyLeft.y, right.position.y - readyRight.y);
                    previousLeft = left.position; previousRight = right.position;
                }
                Assert.That(maximumLift, Is.GreaterThan(.015f), subject + ": a moving foot must leave the ground.");
                AssertInertiaSamplingStable(actor, subject + " walking");
                // The isolated simulation can advance many poses in one Unity
                // frame. Let GPU skinning consume this pose before photographing
                // it alongside the rigid weapon, which updates immediately.
                yield return null;
                PresentInertiaPose();
                CaptureInertiaFrame(actor, subject, 0);

                var enteringPose = new CombatInertiaPose(actor);
                actor.State.ObserveLateralCue(firstHalf ? 0 : 1);
                Assert.That(actor.TryAttack(), Is.True);
                PresentInertiaPose();
                enteringPose.AssertMatches(actor, .008f, .75f, subject + " immediate attack entry");
                Assert.That(actor.State.AttackElapsed, Is.EqualTo(0f),
                    "A moving attack must begin immediately, without waiting for the next gait boundary.");
                bool inspectedContact = false;
                for (int frame = 0; frame < 90 && actor.State.IsAttacking; frame++)
                {
                    AdvanceAchievedCombatTravel(actor, direction * (.6f * actor.MovementScale), delta);
                    AssertFootworkSupport(actor, left, right, readyLeft.y, readyRight.y,
                        previousLeft, previousRight, subject + " moving attack");
                    previousLeft = left.position; previousRight = right.position;
                    if (!inspectedContact && actor.State.Phase == MeleePhase.Active)
                    {
                        inspectedContact = true;
                        Assert.That(left.position.y, Is.EqualTo(readyLeft.y).Within(.018f), subject + ": left foot at impact");
                        Assert.That(right.position.y, Is.EqualTo(readyRight.y).Within(.018f), subject + ": right foot at impact");
                        AssertInertiaSamplingStable(actor, subject + " planted impact");
                        yield return null;
                        PresentInertiaPose();
                        CaptureInertiaFrame(actor, subject, 1);
                    }
                }
                Assert.That(inspectedContact, Is.True);
                Assert.That(actor.State.Phase, Is.EqualTo(MeleePhase.Ready));

                // A held direction with no achieved travel models a wall stop:
                // intent alone cannot keep shuffling the feet beneath the body.
                actor.SetLocomotion(direction * .6f);
                for (int frame = 0; frame < 24; frame++) { actor.Step(delta); PresentInertiaPose(); }
                Vector3 stoppedLeft = left.position, stoppedRight = right.position;
                for (int frame = 0; frame < 12; frame++) { actor.Step(delta); PresentInertiaPose(); }
                Assert.That(Vector3.Distance(stoppedLeft, left.position), Is.LessThan(.006f), subject + ": blocked left support");
                Assert.That(Vector3.Distance(stoppedRight, right.position), Is.LessThan(.006f), subject + ": blocked right support");
                AssertInertiaSamplingStable(actor, subject + " stopped");
                yield return null;
                PresentInertiaPose();
                CaptureInertiaFrame(actor, subject, 2);
            }
        }

        private void AdvanceAchievedCombatTravel(CombatActor actor, Vector3 requestedVelocity, float seconds)
        {
            Vector3 before = actor.transform.position;
            actor.Body.Move(requestedVelocity * seconds);
            actor.SetLocomotion((actor.transform.position - before) / seconds);
            actor.Step(seconds);
            PresentInertiaPose();
        }

        private static Transform CombatBone(CombatActor actor, string name)
        {
            foreach (Transform bone in actor.DamageRigRoot.GetComponentsInChildren<Transform>(true))
                if (bone.name == name) return bone;
            Assert.Fail("The combat rig is missing its " + name + " bone.");
            return null;
        }

        private static void AssertCombatReadySupport(CombatActor actor, string context)
        {
            Vector3 left = actor.transform.InverseTransformPoint(CombatBone(actor, "foot.L").position);
            Vector3 right = actor.transform.InverseTransformPoint(CombatBone(actor, "foot.R").position);
            Assert.That(Mathf.Abs(left.x - right.x), Is.InRange(.39f, .46f), context + ": ready stance width");
            Assert.That(Mathf.Abs(left.z - right.z), Is.InRange(.24f, .31f), context + ": ready stagger");
        }

        private static void AssertFootworkSupport(CombatActor actor, Transform left, Transform right,
            float leftGround, float rightGround, Vector3 previousLeft, Vector3 previousRight, string context)
        {
            Assert.That(left.position.y - leftGround, Is.InRange(-.012f, .12f), context + ": left sole height");
            Assert.That(right.position.y - rightGround, Is.InRange(-.012f, .12f), context + ": right sole height");
            Vector3 span = actor.transform.InverseTransformDirection(right.position - left.position);
            Assert.That(span.x, Is.GreaterThan(.28f), context + ": feet must retain their sides without crossing");
            float leftTravel = Vector3.Distance(previousLeft, left.position);
            float rightTravel = Vector3.Distance(previousRight, right.position);
            Assert.That(Mathf.Max(leftTravel, rightTravel), Is.LessThan(.075f), context + ": no frame-sized foot teleport");
            Assert.That(Mathf.Min(leftTravel, rightTravel), Is.LessThan(.008f), context + ": at least one loaded support stays planted");
        }

        private IEnumerator VerifyCombatTravelInertia()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                PlacePair(3f);
                input.Press(keyboard.dKey, queueEventOnly: true);
                yield return null;
                yield return null;
                float initialSpeed = root.Player.Motor.PlanarVelocity.magnitude;
                for (int frame = 0; frame < 18; frame++)
                {
                    root.Tick(1f / 60f);
                    yield return null;
                }
                float movingSpeed = root.Player.Motor.PlanarVelocity.magnitude;
                Assert.That(initialSpeed, Is.LessThan(movingSpeed * .6f), "A combat strafe must accelerate.");
                Assert.That(movingSpeed, Is.GreaterThan(.5f));
                Vector3 movingDirection = root.Player.Motor.PlanarVelocity.normalized;
                Vector3 releasePoint = root.Hero.transform.position;
                input.Release(keyboard.dKey, queueEventOnly: true);
                for (int frame = 0; frame < 24; frame++)
                {
                    root.Tick(1f / 60f);
                    yield return null;
                }
                Assert.That(Vector3.Dot(root.Hero.transform.position - releasePoint, movingDirection), Is.GreaterThan(.025f),
                    "Releasing the key must brake the existing velocity over time.");
                Assert.That(root.Player.Motor.PlanarVelocity.magnitude, Is.LessThan(.025f));

                root.SetSparring(true);
                root.Hero.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                root.Opponent.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, 3f), Vector3.back);
                Physics.SyncTransforms();
                Vector3 firstTravel = Vector3.zero;
                // The decision clock may wait before requesting pursuit. Inspect
                // the first achieved stride, independently of that decision delay.
                for (int tick = 0; tick < 30 && firstTravel.sqrMagnitude < .0000000001f; tick++)
                {
                    Vector3 opponentStart = root.Opponent.transform.position;
                    root.Tick(CombatTestRoot.SimulationStep);
                    firstTravel = root.Opponent.transform.position - opponentStart;
                    firstTravel.y = 0f;
                }
                Assert.That(firstTravel.magnitude, Is.InRange(.00001f, .005f),
                    "The opponent must start by accelerating instead of taking an immediate full-speed stride.");
                root.SetSparring(false);
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        private void PresentInertiaPose()
        {
            root.Hero.Present();
            root.Opponent.Present();
            ((Player3DCharacterPresentation)root.Player.Visual).ReapplyLatePresentationPose();
        }

        private void AssertInertiaSamplingStable(CombatActor actor, string context)
        {
            var before = new CombatInertiaPose(actor);
            for (int sample = 0; sample < 5; sample++)
            {
                actor.Step(0f);
                PresentInertiaPose();
            }
            before.AssertMatches(actor, .0002f, .05f, context + ": repeated presentation has no time budget");
        }

        private void CaptureInertiaFrame(CombatActor actor, string subject, int frame)
        {
            Camera camera = root.CameraFollow.Camera;
            Vector3 previousPosition = camera.transform.position;
            Quaternion previousRotation = camera.transform.rotation;
            float previousFov = camera.fieldOfView;
            try
            {
                // Borrow the production camera and post-processing for a fixed
                // three-quarter diagnostic view, with both soles in frame.
                Vector3 focus = actor.transform.position + Vector3.up * .95f;
                Vector3 eye = actor.transform.position + actor.transform.TransformDirection(new Vector3(2.4f, 1.7f, 3.2f));
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                camera.fieldOfView = 44f;
                LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("^Area capture wrote "));
                AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest + "/inertia/" + subject,
                    "frame-" + frame.ToString("D4"));
                // A few complementary full-body stills keep fear/effort and
                // fore-aft support reviewable without tripling every sequence.
                if (frame == 0 || frame == 1 || frame == 35 || frame == 50)
                {
                    foreach (bool side in new[] { false, true })
                    {
                        eye = actor.transform.position + actor.transform.TransformDirection(
                            side ? new Vector3(2.2f, 1.25f, .15f) : new Vector3(.15f, 1.25f, 2.2f));
                        camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(focus - eye));
                        camera.fieldOfView = 50f;
                        LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("^Area capture wrote "));
                        AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest + "/inertia/" + subject,
                            (side ? "side-" : "front-") + frame.ToString("D4"));
                    }
                    camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                    camera.fieldOfView = previousFov;
                    LogAssert.Expect(LogType.Log, new System.Text.RegularExpressions.Regex("^Area capture wrote "));
                    AreaCaptureFixture.CaptureCurrentCamera(camera, SceneIds.CombatTest + "/inertia/" + subject,
                        "gameplay-" + frame.ToString("D4"));
                }
            }
            finally
            {
                camera.transform.SetPositionAndRotation(previousPosition, previousRotation);
                camera.fieldOfView = previousFov;
            }
        }

        private sealed class CombatInertiaPose
        {
            private readonly Transform[] bones;
            private readonly Vector3[] positions;
            private readonly Quaternion[] rotations;
            private readonly Vector3 basePoint, tipPoint;

            public CombatInertiaPose(CombatActor actor)
            {
                var sampled = new List<Transform>();
                foreach (Transform bone in actor.DamageRigRoot.GetComponentsInChildren<Transform>(true))
                    if (bone.name == "pelvis" || bone.name == "spine" || bone.name == "chest" ||
                        bone.name == "neck" || bone.name == "head" || bone.name.StartsWith("thigh.") ||
                        bone.name.StartsWith("shin.") || bone.name.StartsWith("foot.")) sampled.Add(bone);
                bones = sampled.ToArray();
                Assert.That(bones.Length, Is.GreaterThanOrEqualTo(3));
                positions = new Vector3[bones.Length];
                rotations = new Quaternion[bones.Length];
                for (int i = 0; i < bones.Length; i++)
                {
                    positions[i] = bones[i].localPosition;
                    rotations[i] = bones[i].localRotation;
                }
                basePoint = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeBase").position;
                tipPoint = CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip").position;
            }

            public float AngleFrom(CombatInertiaPose previous, string name)
            {
                for (int i = 0; i < bones.Length; i++)
                    if (bones[i].name == name) return Quaternion.Angle(previous.rotations[i], rotations[i]);
                Assert.Fail("The combat rig is missing its " + name + " bone.");
                return 0f;
            }

            public Vector3 AngularDeltaFrom(CombatInertiaPose previous, string name)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    if (bones[i].name != name) continue;
                    Quaternion delta = rotations[i] * Quaternion.Inverse(previous.rotations[i]);
                    if (delta.w < 0f) delta = new Quaternion(-delta.x, -delta.y, -delta.z, -delta.w);
                    // acos(w) loses sub-.04-degree motion when float w rounds to
                    // one. The vector part still resolves the per-tick rotation.
                    var vector = new Vector3(delta.x, delta.y, delta.z);
                    float sine = vector.magnitude;
                    return sine < .00000001f ? Vector3.zero :
                        vector * (2f * Mathf.Atan2(sine, delta.w) * Mathf.Rad2Deg / sine);
                }
                Assert.Fail("The combat rig is missing its " + name + " bone.");
                return Vector3.zero;
            }

            public void AssertMatches(CombatActor actor, float distanceTolerance, float angleTolerance, string context)
            {
                for (int i = 0; i < bones.Length; i++)
                {
                    Assert.That(Vector3.Distance(positions[i], bones[i].localPosition), Is.LessThan(distanceTolerance),
                        context + ": " + bones[i].name + " position");
                    Assert.That(Quaternion.Angle(rotations[i], bones[i].localRotation), Is.LessThan(angleTolerance),
                        context + ": " + bones[i].name + " rotation");
                }
                Assert.That(Vector3.Distance(basePoint, CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeBase").position),
                    Is.LessThan(distanceTolerance), context + ": crowbar base");
                Assert.That(Vector3.Distance(tipPoint, CombatAssetProvider.FindAnchor(actor.Weapon, "StrikeTip").position),
                    Is.LessThan(distanceTolerance), context + ": crowbar tip");
            }
        }
    }
}
