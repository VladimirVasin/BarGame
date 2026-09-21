using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;
using static BarPromenade.Tests.PlayMode.CombatTuning;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("The grounded defensive steps through the playable shoulder camera.")]
        [PrebuildSetup(typeof(CombatTestAssetsSetup))]
        public IEnumerator CombatTactics()
        {
            float captureDelta = Time.captureDeltaTime;
            Time.captureDeltaTime = 1f / 60f;
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest);
                var root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root, Is.Not.Null);
                root.AutomaticSimulation = false;
                Camera camera = root.CameraFollow.Camera;
                var directions = new[] { Vector2.up, Vector2.down, Vector2.left, Vector2.right };
                var names = new[] { "forward", "back", "left", "right" };
                for (int i = 0; i < directions.Length; i++)
                {
                    root.SetSparring(false);
                    root.Hero.ResetActor(Vector3.up * PlayerFactory.GroundedRootOffset, Vector3.forward);
                    root.Opponent.ResetActor(new Vector3(0f, PlayerFactory.GroundedRootOffset, 3.5f), Vector3.back);
                    Physics.SyncTransforms();
                    root.CameraFollow.Snap();
                    for (int frame = 0; frame < 6; frame++) yield return null;
                    Vector3 start = root.Hero.transform.position;
                    Assert.That(root.Hero.TryStep(directions[i]), Is.True);
                    root.Tick(S.StepTravelSeconds * .25f);
                    Assert.That(Vector3.Distance(start, root.Hero.transform.position),
                        Is.InRange(S.StepDistance * .10f, S.StepDistance * .25f),
                        "The first quarter must read as an accelerating step, not a blink.");
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, "tactics-" + names[i] + "-lead");
                    CaptureStepSupport(camera, root.Hero, names[i] + "-lead");
                    root.Tick(S.StepTravelSeconds * .25f);
                    Assert.That(Vector3.Distance(start, root.Hero.transform.position),
                        Is.InRange(S.StepDistance * .45f, S.StepDistance * .55f),
                        "Travel must remain distributed across both halves of the step.");
                    root.Tick(S.StepTravelSeconds * .25f);
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, "tactics-" + names[i] + "-close");
                    CaptureStepSupport(camera, root.Hero, names[i] + "-close");
                    root.Tick(.38f - S.StepTravelSeconds * .75f);
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Step),
                        "The longer step must still be settling at the former completion time.");
                    root.Tick(S.StepDurationSeconds - .38f + .01f);
                    yield return null;
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                    Assert.That(Vector3.Distance(start, root.Hero.transform.position),
                        Is.EqualTo(S.StepDistance).Within(.025f), "Retiming may not change the committed distance.");
                }
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            }
            finally { Time.captureDeltaTime = captureDelta; }
        }

        private static void CaptureStepSupport(Camera camera, CombatActor actor, string shot)
        {
            Vector3 position = camera.transform.position;
            Quaternion rotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            try
            {
                Vector3 target = actor.transform.position + Vector3.up * .8f;
                Vector3 eye = target + actor.transform.right * 2.7f - actor.transform.forward * 1.4f + Vector3.up * .3f;
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.fieldOfView = 50f;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "tactics-support-" + shot);
            }
            finally
            {
                camera.transform.SetPositionAndRotation(position, rotation);
                camera.fieldOfView = fieldOfView;
            }
        }
    }
}
