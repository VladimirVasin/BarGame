using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

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
                    Assert.That(root.Hero.TryStep(directions[i]), Is.True);
                    root.Tick(.075f);
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, "tactics-" + names[i] + "-lead");
                    root.Tick(.15f);
                    yield return null;
                    CaptureCurrentCamera(camera, SceneIds.CombatTest, "tactics-" + names[i] + "-close");
                    root.Tick(.3f);
                    yield return null;
                    Assert.That(root.Hero.State.Phase, Is.EqualTo(MeleePhase.Ready));
                }
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
            }
            finally { Time.captureDeltaTime = captureDelta; }
        }
    }
}
