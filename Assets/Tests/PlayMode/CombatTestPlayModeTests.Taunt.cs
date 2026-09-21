using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CombatTestPlayModeTests
    {
        /// <summary>
        /// The settled, terminally defeated body offers `E` once; the winner walks
        /// to the dock, the toilet view takes his eyes and right hand while the bar
        /// waits in his left, the stream marks the body's own bones, and R clears
        /// the marks with the round.
        /// </summary>
        [UnityTest]
        public IEnumerator Range_TauntDesecratesBodyOncePerRoundAndResets()
        {
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                yield return EnterRange();
                PlacePair(1.1f);
                root.AutomaticSimulation = true;
                var presentation = (Player3DCharacterPresentation)root.Player.Visual;
                CombatTauntInteraction taunt = root.Taunt;
                Assert.That(taunt, Is.Not.Null, "The arena installs its one interactable on the opponent.");
                PlayerInteractor interactor = root.Player.Interactor;
                Assert.That(taunt.CanInteract(interactor), Is.False, "A standing opponent offers nothing.");

                yield return StrikeToDefeat(root.Hero, root.Opponent);
                yield return WaitFor(() => root.Opponent.IsRagdollActive, "The fallen opponent never reached physics.");
                Assert.That(taunt.CanInteract(interactor), Is.False,
                    "The prompt waits for the body to settle and the camera to let go.");
                yield return WaitFor(() => root.Opponent.Ragdoll.IsSettled && root.RoundCameraReleased,
                    "The body never settled.");
                yield return WaitFor(() => taunt.CanInteract(interactor), "The settled body must offer the taunt.");
                if (!ReferenceEquals(interactor.ActiveInteractable, taunt))
                {
                    input.Press(keyboard.wKey, queueEventOnly: true);
                    yield return WaitFor(() => ReferenceEquals(interactor.ActiveInteractable, taunt),
                        "Walking up to the body must raise the prompt through its bones.");
                    input.Release(keyboard.wKey, queueEventOnly: true);
                    yield return null;
                }
                Assert.That(root.Prompt.PromptKey, Is.EqualTo(CombatTauntInteraction.PromptKeyName));
                Assert.That(root.Hero.IsWeaponInLeftHand, Is.False);

                input.Press(keyboard.eKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.eKey, queueEventOnly: true);
                yield return WaitFor(() => taunt.IsActive, "E must begin the taunt.");
                Assert.That(root.Player.Motor.InputEnabled, Is.False, "The approach owns the motor.");
                Assert.That(GameInput.CanRead(GameInputContext.Gameplay), Is.False, "Combat keys are silent during the action.");
                Assert.That(root.Hero.IsWeaponInLeftHand, Is.True);
                Assert.That(root.Hero.Weapon.transform.parent, Is.EqualTo(presentation.Registry.Anchors.LeftGrip),
                    "The bar waits in the left hand while the right one is busy.");

                // The approach is a walk: no frame carries the hero further than his gait can.
                Vector3 previous = root.Hero.transform.position;
                Vector3 start = previous;
                float longestStep = 0f;
                float deadline = Time.realtimeSinceStartup + 30f;
                while (taunt.CurrentPhase == CombatTauntInteraction.Phase.Approaching && Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                    Vector3 position = root.Hero.transform.position;
                    longestStep = Mathf.Max(longestStep, Vector3.Distance(position, previous));
                    previous = position;
                }
                Assert.That(taunt.CurrentPhase, Is.EqualTo(CombatTauntInteraction.Phase.Using), "The walk never reached the dock.");
                Assert.That(longestStep, Is.LessThan(.12f), "The approach is a walk, never a teleport.");
                Assert.That(Vector3.Distance(start, root.Hero.transform.position), Is.GreaterThan(.05f),
                    "The dock stands beside the body, not where the winner happened to be.");
                Vector3 planar = root.Hero.transform.position - taunt.DockPosition;
                planar.y = 0f;
                Assert.That(planar.magnitude, Is.LessThan(PlayerMotor.InteractionPositionTolerance + .02f));

                yield return WaitFor(() => taunt.FirstPerson.IsActive && taunt.Timeline.Phase == HomeToiletScenePhase.Urinating,
                    "The eye-level view never began.");
                for (int frame = 0; frame < 3; frame++) yield return null;
                Assert.That(taunt.FirstPerson.HiddenHeadRendererCount, Is.GreaterThan(0), "The eye-level view hides the head.");
                Assert.That(root.CameraFollow.FixedPoseActive, Is.True, "The action owns the camera through the follow's fixed pose.");
                Camera camera = root.CameraFollow.Camera;
                // The rig and the lens are sampled after their presentation owners, never
                // at the top of a frame where the locomotion layer has re-posed the arm.
                yield return AtCombatPresentation(() =>
                {
                    Assert.That(Vector3.Distance(camera.transform.position,
                        presentation.Registry.Anchors.Mouth.position + Vector3.up * .068f), Is.LessThan(.08f),
                        "At full blend the lens sits in the hero's eyes.");
                    Assert.That(camera.fieldOfView, Is.EqualTo(HomeToiletFirstPersonView.FieldOfView).Within(.5f));
                    Assert.That(taunt.FirstPerson.AimPitchDegrees, Is.InRange(10f, 48f),
                        "The dock keeps the solved pitch under the angle where the scrotum stands in front of the shaft.");
                    Assert.That(taunt.FirstPerson.GripError, Is.LessThan(HomeToiletFirstPersonView.GripContactToleranceMeters + .005f),
                        "The real right hand closes on the kit's grip.");
                });

                yield return WaitFor(() => EnabledStainCount(root.Opponent.transform) > 0,
                    "The stream must leave a mark on the body's own bones.");
                Assert.That(taunt.Urine.BowlHitCount, Is.Zero, "Nothing here absorbs.");
                Assert.That(taunt.Urine.SurfaceHitCount, Is.GreaterThan(0));
                Assert.That(taunt.Urine.ScopedResidueCount, Is.GreaterThan(0));
                Assert.That(Vector3.Distance(taunt.Urine.LastHitPoint, taunt.AimPoint), Is.LessThan(.75f),
                    "The solved arc lands on the body, not across the arena.");
                Assert.That(root.Opponent.State.IsDefeated, Is.True);
                Assert.That(root.Opponent.Ragdoll.IsSettled, Is.True, "The stream does not wake the body.");
                Assert.That(root.Hero.IsWeaponInLeftHand, Is.True);

                yield return WaitFor(() => !taunt.IsActive, "The action never finished on its own.");
                yield return null;
                Assert.That(root.Player.Motor.InputEnabled, Is.True);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                Assert.That(taunt.FirstPerson.IsActive, Is.False);
                Assert.That(taunt.FirstPerson.HiddenHeadRendererCount, Is.Zero, "The head is drawn again.");
                Assert.That(root.CameraFollow.FixedPoseActive, Is.False, "The follow camera is given back.");
                Assert.That(presentation.InteractionHandoffLocked, Is.False);
                Assert.That(root.Hero.IsWeaponInLeftHand, Is.False);
                Assert.That(root.Hero.Weapon.transform.parent, Is.EqualTo(presentation.Registry.Anchors.RightGrip),
                    "The bar returns to the right hand.");
                Assert.That(taunt.Urine.ScopedResidueCount, Is.GreaterThan(0), "The marks outlive the action.");
                Assert.That(EnabledStainCount(root.Opponent.transform), Is.GreaterThan(0));
                Assert.That(taunt.CanInteract(interactor), Is.False, "Once per round.");
                Assert.That(interactor.ActiveInteractable, Is.Null);
                Assert.That(root.RoundFinished, Is.True);

                input.Press(keyboard.rKey, queueEventOnly: true);
                yield return null;
                input.Release(keyboard.rKey, queueEventOnly: true);
                yield return null;
                Assert.That(root.RoundFinished, Is.False);
                Assert.That(taunt.Urine.ScopedResidueCount, Is.Zero, "R clears the round's marks with its blood.");
                Assert.That(EnabledStainCount(root.transform), Is.Zero);
                Assert.That(root.Hero.IsWeaponInLeftHand, Is.False);
                Assert.That(root.Hero.RequestCharge(), Is.True, "R reopens attacks after the taunt.");
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                if (root != null) root.AutomaticSimulation = false;
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
            }
        }

        /// <summary>Samples after every LateUpdate of the frame, the taunt's own (order 260) included.</summary>
        private IEnumerator AtCombatPresentation(Action sample)
        {
            var probe = root.GetComponent<HomeBathroomPresentationProbe>() ??
                root.gameObject.AddComponent<HomeBathroomPresentationProbe>();
            bool completed = false;
            Exception failure = null;
            probe.Sample = () =>
            {
                try { sample(); }
                catch (Exception exception) { failure = exception; }
                finally { completed = true; }
            };
            while (!completed) yield return null;
            if (failure != null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
        }

        private static int EnabledStainCount(Transform under)
        {
            int count = 0;
            foreach (MeshRenderer renderer in under.GetComponentsInChildren<MeshRenderer>(true))
                if (renderer.enabled && renderer.name.StartsWith("Urine Stain", StringComparison.Ordinal)) count++;
            return count;
        }
    }
}
