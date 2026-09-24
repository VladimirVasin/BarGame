using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused deep-snow movement and real village gait captures.")]
        [PrebuildSetup(typeof(ColdHeroAssetsSetup))]
        public IEnumerator AlpineVillageSnowGait()
        {
            float previousCapture = Time.captureDeltaTime;
            var input = new InputTestFixture();
            input.Setup();
            Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
            AlpineVillageRoot village = null;
            try
            {
                GameSessionState.BeginNewGame();
                GameSessionState.TryStartGameTimeFromWake();
                Time.captureDeltaTime = 1f / 30f;
                var loading = SceneManager.LoadSceneAsync(SceneIds.AlpineVillage, LoadSceneMode.Single);
                float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
                while (Time.realtimeSinceStartup < deadline)
                {
                    village = Object.FindAnyObjectByType<AlpineVillageRoot>();
                    if (loading.isDone && village != null && village.IsInitialized) break;
                    yield return null;
                }
                Assert.That(village != null && village.IsInitialized, Is.True);
                var motor = village.Player.Motor;
                var hero = (Player3DCharacterPresentation)village.Player.Visual;
                var snow = village.World.SnowTreading;
                Assert.That(hero.HasClip("SnowWalk") && hero.HasClip("SnowWalkBackward"), Is.True);
                FindSnowGaitApproach(village, out Vector3 start, out Vector3 direction);
                motor.Teleport(start);
                motor.transform.rotation = Quaternion.LookRotation(direction);
                Physics.SyncTransforms();
                for (int frame = 0; frame < 8; frame++) yield return null;

                int contacts = 0;
                hero.SnowFootContact += left => contacts++;
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                bool entered = false, leftShot = false, rightShot = false;
                float maximumLift = 0f;
                int deepFrames = 0;
                for (int frame = 0; frame < 330; frame++)
                {
                    yield return null;
                    if (!motor.InDeepSnow) continue;
                    entered = true;
                    if (motor.SnowBlend < .99f) continue;
                    deepFrames++;
                    if (deepFrames > 12)
                    {
                        Assert.That(motor.PlanarVelocity.magnitude, Is.LessThan(1.12f),
                            "Held Shift cannot carry running momentum through deep snow after braking.");
                    }
                    if (deepFrames > 24) Assert.That(hero.RunBlend, Is.LessThan(.01f));
                    float lift = Mathf.Max(SnowFootLift(village, hero.Metrics.LeftFootWorldPosition),
                        SnowFootLift(village, hero.Metrics.RightFootWorldPosition));
                    maximumLift = Mathf.Max(maximumLift, lift);
                    if (hero.SnowBlend < .95f) continue;
                    float phase = hero.SnowGaitCycle;
                    if (!leftShot && phase > .68f && phase < .79f)
                    {
                        CaptureColdHero(Camera.main, hero, "snow-00-game-camera");
                        village.CameraFollow.enabled = false;
                        AimAtColdHero(Camera.main, motor.transform, new Vector3(3.2f, 1.45f, .4f));
                        CaptureColdHero(Camera.main, hero, "snow-01-left-extraction");
                        village.CameraFollow.enabled = true;
                        leftShot = true;
                    }
                    if (!rightShot && phase > .18f && phase < .29f)
                    {
                        village.CameraFollow.enabled = false;
                        AimAtColdHero(Camera.main, motor.transform, new Vector3(-2.8f, 1.45f, 1.3f));
                        CaptureColdHero(Camera.main, hero, "snow-02-right-extraction");
                        village.CameraFollow.enabled = true;
                        rightShot = true;
                    }
                    if (leftShot && rightShot && deepFrames > 75) break;
                }
                Assert.That(entered && leftShot && rightShot, Is.True, "The real deep-snow path was not traversed.");
                Assert.That(deepFrames, Is.GreaterThan(60), "Own treading must not repeatedly cancel the gait.");
                Assert.That(contacts, Is.GreaterThanOrEqualTo(3), "Snow sound must follow alternating foot contacts.");
                Assert.That(maximumLift, Is.GreaterThan(.43f), "At least one boot must clear knee-deep snow.");
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                for (int frame = 0; frame < 26; frame++) yield return null;
                Assert.That(motor.PlanarVelocity.magnitude, Is.LessThan(.02f));
                Assert.That(hero.SnowBlend, Is.LessThan(.02f), "Standing must settle, not keep marching.");

                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.S, Key.LeftShift));
                for (int frame = 0; frame < 18; frame++) yield return null;
                Assert.That(Vector3.Dot(motor.PlanarVelocity, motor.transform.forward), Is.LessThan(0f));
                Assert.That(hero.RunBlend, Is.LessThan(.01f));
                village.CameraFollow.enabled = false;
                AimAtColdHero(Camera.main, motor.transform, new Vector3(3.1f, 1.5f, .6f));
                CaptureColdHero(Camera.main, hero, "snow-03-backstep");
                village.CameraFollow.enabled = true;

                // Return along the actually pressed track with Shift still held.
                motor.transform.rotation = Quaternion.LookRotation(-direction);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W, Key.LeftShift));
                bool runningAgain = false;
                for (int frame = 0; frame < 210; frame++)
                {
                    yield return null;
                    if (motor.SnowBlend <= 0f && motor.PlanarVelocity.magnitude > 4f)
                    { runningAgain = true; break; }
                }
                Assert.That(runningAgain, Is.True, "Leaving deep snow must restore held sprint.");
                CaptureColdHero(Camera.main, hero, "snow-04-return-to-path");
            }
            finally
            {
                if (keyboard != null && keyboard.added) InputSystem.RemoveDevice(keyboard);
                input.TearDown();
                if (village != null) village.CameraFollow.enabled = true;
                Time.captureDeltaTime = previousCapture;
            }
        }

        private static float SnowFootLift(AlpineVillageRoot village, Vector3 foot) =>
            foot.y - AlpineVillageTerrainSampler.SampleHeight(village.Plan, new Vector2(foot.x, foot.z));

        private static void FindSnowGaitApproach(AlpineVillageRoot village, out Vector3 start, out Vector3 direction)
        {
            foreach (var lane in village.Plan.Lane.Samples)
            {
                if (lane.Distance < 14f) continue;
                foreach (float side in new[] { -1f, 1f })
                {
                    direction = lane.Right * side;
                    start = lane.Position;
                    bool clear = true;
                    for (float distance = 0f; distance < 9f; distance += .3f)
                        clear &= village.World.WalkableArea.Contains(start + direction * distance, .4f);
                    if (!clear || village.World.SnowTreading.SampleVisibleDepth(start + direction * 6f) < .4f ||
                        village.World.SnowTreading.SampleMovementSnowDepth(start, direction) >= .12f) continue;
                    start.y = AlpineVillageTerrainSampler.SampleHeight(village.Plan, new Vector2(start.x, start.z)) +
                        PlayerFactory.GroundedRootOffset;
                    return;
                }
            }
            start = direction = Vector3.zero;
            Assert.Fail("No clear lane-to-field crossing exists for the snow gait capture.");
        }
    }
}
