using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The winner's taunt over the settled body: the walk-in with the bar in the left hand, the eye-level stream on the body, the marks, the return and R.")]
        [PrebuildSetup(typeof(CombatTestAssetsSetup))]
        public IEnumerator CombatTaunt()
        {
            float previousCaptureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            var input = new InputTestFixture();
            Keyboard keyboard = null;
            try
            {
                input.Setup();
                keyboard = InputSystem.AddDevice<Keyboard>();
                yield return CaptureCombatTaunt(input, keyboard);
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                Time.captureDeltaTime = previousCaptureDelta;
            }
        }

        private IEnumerator CaptureCombatTaunt(InputTestFixture input, Keyboard keyboard)
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
            var root = Object.FindAnyObjectByType<CombatTestRoot>();
            Assert.That(root, Is.Not.Null);
            root.AutomaticSimulation = false;
            for (int frame = 0; frame < 12; frame++) yield return null;
            Camera camera = root.CameraFollow.Camera;
            root.SetSparring(false);
            root.Hero.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, 0f), Vector3.forward);
            root.Opponent.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, 1.1f), Vector3.back);
            Physics.SyncTransforms();
            // The live round lands the authored contacts the way the PlayMode fixture does:
            // the target must be Ready again before each strike.
            root.AutomaticSimulation = true;
            for (int contact = 0; contact < HitsToDefeat && !root.Opponent.State.IsDefeated; contact++)
            {
                yield return WaitForTaunt(() => root.Opponent.State.Phase == MeleePhase.Ready, "The target never recovered.");
                root.Hero.ResetActor(root.Opponent.transform.position - Vector3.forward * 1.1f, Vector3.forward);
                Physics.SyncTransforms();
                float health = root.Opponent.State.Health;
                Assert.That(root.Hero.TryAttack(), Is.True);
                yield return WaitForTaunt(() => root.Opponent.State.Health < health, "The strike never reached the target rig.");
            }
            Assert.That(root.Opponent.State.IsDefeated, Is.True);
            yield return WaitForTaunt(() => root.Opponent.Ragdoll.IsSettled && root.RoundCameraReleased, "The body never settled.");
            CombatTauntInteraction taunt = root.Taunt;
            PlayerInteractor interactor = root.Player.Interactor;
            for (int frame = 0; frame < 30 && !ReferenceEquals(interactor.ActiveInteractable, taunt); frame++) yield return null;
            Assert.That(interactor.ActiveInteractable, Is.SameAs(taunt), "The settled body must offer the taunt.");
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-01-prompt");

            input.Press(keyboard.eKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.eKey, queueEventOnly: true);
            yield return null;
            Assert.That(taunt.IsActive, Is.True);
            for (int frame = 0; frame < 12 && taunt.CurrentPhase == CombatTauntInteraction.Phase.Approaching; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-02-walk-in");
            CaptureLeftHandGrip(camera, root, "taunt-05-grip-left");
            for (int frame = 0; frame < 600 && taunt.CurrentPhase == CombatTauntInteraction.Phase.Approaching; frame++) yield return null;
            Assert.That(taunt.CurrentPhase, Is.EqualTo(CombatTauntInteraction.Phase.Using), "The walk never reached the dock.");
            while (taunt.Timeline.CameraBlend < .5f) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-03-enter-blend");
            while (taunt.Urine.SurfaceHitCount < 12) yield return null;
            for (int frame = 0; frame < 20; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-04-stream-body");
            taunt.FirstPerson.ApplyAimDelta(60f, 10f);
            for (int frame = 0; frame < 40; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-06-aim-floor");
            taunt.FirstPerson.ApplyAimDelta(-60f, -10f);
            while (taunt.Timeline.Phase != HomeToiletScenePhase.Shaking) yield return null;
            for (int frame = 0; frame < 30; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-07-shake");
            while (taunt.Timeline.Phase != HomeToiletScenePhase.Exiting) yield return null;
            while (taunt.IsActive && taunt.Timeline.CameraBlend > .5f) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-08-exit-blend");
            while (taunt.IsActive) yield return null;
            for (int frame = 0; frame < 30; frame++) yield return null;
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-09-restored");
            // A witness beside the body: the marks on the bones and the floor, the bar back in the right hand.
            Vector3 body = taunt.AimPoint;
            Vector3 eye = body + new Vector3(1.6f, 1.5f, -1.4f);
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(body - eye));
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-09-witness");
            root.CameraFollow.Snap();
            yield return null;

            input.Press(keyboard.rKey, queueEventOnly: true);
            yield return null;
            input.Release(keyboard.rKey, queueEventOnly: true);
            for (int frame = 0; frame < 30; frame++) yield return null;
            Assert.That(root.RoundFinished, Is.False);
            CaptureCurrentCamera(camera, SceneIds.CombatTest, "taunt-10-after-reset");
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
        }

        private static IEnumerator WaitForTaunt(System.Func<bool> condition, string failure)
        {
            float deadline = Time.realtimeSinceStartup + 30f;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, failure);
        }

        private static void CaptureLeftHandGrip(Camera camera, CombatTestRoot root, string name)
        {
            NpcHandPose pose = root.Player.GameObject.GetComponentInChildren<NpcHandPose>();
            Assert.That(pose, Is.Not.Null);
            Assert.That(root.Hero.IsWeaponInLeftHand, Is.True);
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fov = camera.fieldOfView, near = camera.nearClipPlane;
            try
            {
                Vector3 axis = pose.CylinderAxis(true), normal = pose.PalmNormal(true);
                Vector3 target = pose.CylinderCentre(true) + axis * .045f;
                Vector3 eye = target + normal * .48f + Vector3.Cross(axis, normal) * .20f + axis * .10f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye, axis));
                camera.fieldOfView = 42f;
                camera.nearClipPlane = .02f;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, name);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fov;
                camera.nearClipPlane = near;
            }
        }
    }
}
