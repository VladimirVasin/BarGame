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
        [Explicit("Warehouse dust under working lights, stopped ventilation, blackout and scene ownership.")]
        public IEnumerator CombatWarehouseAtmosphere()
        {
            float previousDelta = Time.captureDeltaTime;
            bool previousBackground = Application.runInBackground;
            CombatTestRoot root = null;
            Time.captureDeltaTime = 1f / 60f;
            Application.runInBackground = true;
            CombatTestRoot.UseLegacyArenaForTests = false;
            CombatTestRoot.WarehouseSeedForTests = 0;
            GameSessionState.BeginNewGame();
            try
            {
                yield return SceneManager.LoadSceneAsync(SceneIds.CombatTest, LoadSceneMode.Single);
                root = Object.FindAnyObjectByType<CombatTestRoot>();
                Assert.That(root != null && root.IsInitialized, Is.True);
                root.AutomaticSimulation = false;
                root.SetSparring(false);
                root.SetDuelLogging(false);
                root.SendMessage("OnApplicationFocus", true);
                GameInput.HandleApplicationFocus(true);
                for (int frame = 0; frame < SettleFrames; frame++) yield return null;

                WarehouseAtmosphere atmosphere = root.GetComponentInChildren<WarehouseAtmosphere>();
                Assert.That(atmosphere != null && atmosphere.IsInitialized, Is.True);
                WarehouseArenaMechanisms machines = atmosphere.Mechanisms;
                Assert.That(machines, Is.SameAs(root.GetComponentInChildren<WarehouseArenaMechanisms>()));
                Assert.That(machines.FanSpeedFactor, Is.EqualTo(1f));
                AssertWarehouseAtmosphereParticles(root.WarehousePlan, atmosphere);
                var pausedParticles = new ParticleSystem.Particle[WarehouseAtmosphere.MaximumDustParticles];
                int pausedParticleCount = atmosphere.Dust.GetParticles(pausedParticles);
                double pausedTime = atmosphere.ElapsedSeconds;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    atmosphere.Advance(3f);
                    yield return null;
                    Assert.That(atmosphere.ElapsedSeconds, Is.EqualTo(pausedTime));
                    AssertWarehouseDustUnchanged(atmosphere, pausedParticles, pausedParticleCount);
                }

                atmosphere.enabled = false;
                atmosphere.Advance(1f);
                yield return null;
                Assert.That(atmosphere.ElapsedSeconds, Is.EqualTo(pausedTime));
                AssertWarehouseDustUnchanged(atmosphere, pausedParticles, pausedParticleCount);
                atmosphere.enabled = true;
                root.enabled = false;
                atmosphere.Advance(1f);
                Assert.That(atmosphere.ElapsedSeconds, Is.EqualTo(pausedTime), "The disabled warehouse cannot keep simulating its dust.");
                root.enabled = true;
                atmosphere.Advance(.1f);
                Assert.That(atmosphere.ElapsedSeconds, Is.GreaterThan(pausedTime));

                Camera camera = root.CameraFollow.Camera;
                camera.aspect = (float)Width / Height;
                root.CameraFollow.enabled = false;
                root.Hero.ResetActor(new Vector3(0f, WarehouseArenaPlan.TopHeight + PlayerFactory.GroundedRootOffset, -7.5f), Vector3.forward);
                SetWarehouseAtmosphereCamera(camera, new Vector3(0f, 7.9f, -5.6f), machines.FanRotor.position, 78f);
                yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse dust - Working fan");

                // The hero and its mounted light remain real. Only the camera
                // is staged beside the upper gallery, looking along the beam.
                Vector3 heroFeet = new Vector3(-2.1f, WarehouseArenaPlan.TopHeight, 7.5f);
                Vector3 heroFacing = new Vector3(0f, 0f, -1f);
                root.Hero.ResetActor(heroFeet + Vector3.up * PlayerFactory.GroundedRootOffset, heroFacing);
                SetWarehouseAtmosphereCamera(camera, heroFeet + new Vector3(1.5f, 1.7f, 1.4f),
                    new Vector3(-2.1f, 7.4f, -.5f), 66f);
                yield return null;
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse dust - Gallery beam");

                var cans = new[] { InventoryItemId.RottenFishTailCan, InventoryItemId.RottenFishBellyCan,
                    InventoryItemId.RottenFishHeadCan };
                for (int part = 0; part < cans.Length; part++)
                    Assert.That(GameSessionState.TryCollectWorldItem(root.WarehousePlan.Refrigerators[part].SourceId,
                        cans[part]), Is.True);
                Assert.That(machines.FanShutdownStarted, Is.True);
                // Sample the existing ramp and the matching particles together;
                // this runs the actual power event without replaying the puzzle.
                for (int step = 0; step < 50; step++)
                {
                    machines.Advance(.1f);
                    atmosphere.Advance(.1f);
                }
                machines.Advance(.01f);
                Assert.That(machines.FanSpeedFactor, Is.Zero);
                int stoppedBursts = atmosphere.BurstEmissionCount;
                atmosphere.Advance(4f);
                Assert.That(atmosphere.Debris.particleCount, Is.Zero, "Finite mechanical debris must settle after ventilation stops.");
                Assert.That(atmosphere.BurstEmissionCount, Is.EqualTo(stoppedBursts), "Stopped machines cannot emit new dust bursts.");
                Assert.That(atmosphere.Dust.particleCount, Is.GreaterThan(0), "The still air retains suspended dust after shutdown.");
                AssertWarehouseAtmosphereParticles(root.WarehousePlan, atmosphere);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse dust - Stopped ventilation");

                // Observe real LateUpdate frames after the motor's clock has
                // stopped. Mere particle presence cannot detect suspended dust
                // whose dominant trajectory has frozen with the fan.
                int stoppedCount = atmosphere.Dust.GetParticles(pausedParticles);
                double stoppedMotorTime = machines.MechanicalSeconds;
                double stoppedDustTime = atmosphere.ElapsedSeconds;
                for (int frame = 0; frame < 120; frame++) yield return null;
                var settledParticles = new ParticleSystem.Particle[WarehouseAtmosphere.MaximumDustParticles];
                Assert.That(atmosphere.Dust.GetParticles(settledParticles), Is.EqualTo(stoppedCount));
                Assert.That(machines.MechanicalSeconds, Is.EqualTo(stoppedMotorTime));
                Assert.That(atmosphere.ElapsedSeconds - stoppedDustTime, Is.GreaterThanOrEqualTo(1.9d));
                int visiblySettling = 0;
                for (int particle = 0; particle < stoppedCount; particle++)
                    if (pausedParticles[particle].startColor.a > 12 && settledParticles[particle].startColor.a > 12 &&
                        pausedParticles[particle].position.y - settledParticles[particle].position.y > .035f)
                        visiblySettling++;
                Assert.That(visiblySettling, Is.GreaterThan(stoppedCount / 2),
                    "Most visible dust must keep descending after the fan stops, rather than hanging at its last motor phase.");
                AssertWarehouseAtmosphereParticles(root.WarehousePlan, atmosphere);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse dust - Settling after stop");

                WarehouseFishLamp pendant = root.GetComponentInChildren<WarehouseFishLamp>();
                Assert.That(pendant, Is.Not.Null);
                Assert.That(GameSessionState.WarehouseFishLamp.TryBegin(true, true), Is.True);
                pendant.Advance(WarehouseFishLamp.HeatingSeconds);
                Assert.That(pendant.Phase, Is.EqualTo(WarehouseFishLampPhase.Burst));
                Assert.That(pendant.Light.enabled || machines.FanLight.enabled, Is.False);
                Assert.That(root.Flashlight.SpotLight.isActiveAndEnabled, Is.True);
                atmosphere.Advance(.1f);
                CaptureCurrentCamera(camera, SceneIds.CombatTest, "Test warehouse dust - Blackout beam");

                ParticleSystem previousDust = atmosphere.Dust;
                ParticleSystem previousDebris = atmosphere.Debris;
                GameObject previousHost = atmosphere.gameObject;
                Assert.That(root.NewWarehouseLayout(1), Is.True);
                yield return null;
                Assert.That(atmosphere == null && previousHost == null, Is.True, "A replacement layout destroys its atmosphere owner.");
                Assert.That(previousDust == null && previousDebris == null, Is.True, "The old layout leaves no orphan particle system.");
                Assert.That(root.GetComponentsInChildren<WarehouseAtmosphere>().Length, Is.EqualTo(1));
                atmosphere = root.GetComponentInChildren<WarehouseAtmosphere>();
                Assert.That(atmosphere.IsInitialized, Is.True);
                Assert.That(atmosphere.Mechanisms.FanSpeedFactor, Is.Zero, "The atmosphere follows the retained stopped ventilation.");
                AssertWarehouseAtmosphereParticles(root.WarehousePlan, atmosphere);

                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                Assert.That(atmosphere == null, Is.True, "Leaving Combat Test releases the atmosphere.");
                Assert.That(Object.FindObjectsByType<WarehouseAtmosphere>().Length, Is.Zero);
            }
            finally
            {
                if (root != null) root.enabled = true;
                Time.captureDeltaTime = previousDelta;
                Application.runInBackground = previousBackground;
                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = null;
                GameSessionState.BeginNewGame();
            }
        }

        private static void SetWarehouseAtmosphereCamera(Camera camera, Vector3 eye, Vector3 target, float fieldOfView)
        {
            camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
            camera.fieldOfView = fieldOfView;
        }

        private static void AssertWarehouseDustUnchanged(WarehouseAtmosphere atmosphere,
            ParticleSystem.Particle[] previous, int previousCount)
        {
            var current = new ParticleSystem.Particle[WarehouseAtmosphere.MaximumDustParticles];
            int count = atmosphere.Dust.GetParticles(current);
            Assert.That(count, Is.EqualTo(previousCount));
            for (int index = 0; index < count; index++)
            {
                Assert.That(current[index].position, Is.EqualTo(previous[index].position));
                Assert.That(current[index].remainingLifetime, Is.EqualTo(previous[index].remainingLifetime));
            }
        }

        private static void AssertWarehouseAtmosphereParticles(WarehouseArenaPlan plan, WarehouseAtmosphere atmosphere)
        {
            Assert.That(atmosphere.Dust.main.maxParticles, Is.EqualTo(WarehouseAtmosphere.MaximumDustParticles));
            Assert.That(atmosphere.Debris.main.maxParticles, Is.EqualTo(WarehouseAtmosphere.MaximumBurstParticles));
            Assert.That(atmosphere.Dust.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
            Assert.That(atmosphere.Debris.main.simulationSpace, Is.EqualTo(ParticleSystemSimulationSpace.World));
            Assert.That(atmosphere.Dust.particleCount, Is.InRange(1, WarehouseAtmosphere.MaximumDustParticles));
            Assert.That(atmosphere.Debris.particleCount, Is.InRange(0, WarehouseAtmosphere.MaximumBurstParticles));
            var particles = new ParticleSystem.Particle[WarehouseAtmosphere.MaximumDustParticles];
            int count = atmosphere.Dust.GetParticles(particles);
            for (int index = 0; index < count; index++)
            {
                Vector3 point = particles[index].position;
                Assert.That(float.IsFinite(point.x) && float.IsFinite(point.y) && float.IsFinite(point.z), Is.True);
                Assert.That(plan.InteriorBounds.Contains(point), Is.True, "Dust must remain inside the planned warehouse.");
                foreach (WarehouseArenaSurface surface in plan.Surfaces)
                    if (surface.Height > 0f && surface.Bounds.Contains(new Vector2(point.x, point.z)))
                        Assert.That(point.y < surface.Height - WarehouseArenaPlan.DeckThickness || point.y > surface.Height,
                            Is.True, "World-space dust cannot live inside a solid gallery deck.");
                foreach (WarehouseArenaObstacle obstacle in plan.Obstacles)
                    if (obstacle.Kind == WarehouseArenaObstacleKind.Support)
                        Assert.That(obstacle.Bounds.Contains(point), Is.False, "Dust cannot live inside a solid support.");
            }
        }
    }
}
