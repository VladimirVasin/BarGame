using System.Collections;
using System.Collections.Generic;
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
        [Timeout(240000)]
        [Explicit("City resident life: one scene, evening/noon/night density, stable identities, pause and lifecycle; inspect the frames.")]
        public IEnumerator CityResidentLife()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(18 * 60), Is.True);
            AsyncOperation load = SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            Assert.That(load, Is.Not.Null);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds * 2f;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (load.isDone && city != null && city.IsInitialized && !CompositionDriver.IsComposing) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized && !CompositionDriver.IsComposing, Is.True);
            CityPedestrianDirector pedestrians = city.Pedestrians;
            Assert.That(pedestrians, Is.Not.Null);
            Assert.That(pedestrians.HasResidentLife, Is.True);
            Assert.That(pedestrians.LogicalResidentCount, Is.EqualTo(192));
            Assert.That(pedestrians.CurrentActiveLimit, Is.EqualTo(36));

            Camera camera = Camera.main;
            Assert.That(camera, Is.Not.Null);
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool wasFollowing = follow != null && follow.enabled;
            Vector3 cameraPosition = camera.transform.position;
            Quaternion cameraRotation = camera.transform.rotation;
            float fieldOfView = camera.fieldOfView;
            var hiddenPlayer = new List<Renderer>();
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled) { renderer.enabled = false; hiddenPlayer.Add(renderer); }
            if (follow != null) follow.enabled = false;
            try
            {
                // Setup is paused while choosing a representative street. It
                // uses only genuine scheduled positions on the existing graph.
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    PlaceResidentLifeStreet(city);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    AssertResidentLifePool(pedestrians);
                    int eveningCount = pedestrians.ActiveCount;
                    Debug.Log("CityResidentLife: evening active=" + eveningCount + " target=" + pedestrians.CurrentActiveLimit +
                              " pooled=" + pedestrians.PoolCapacity);
                    Assert.That(eveningCount, Is.GreaterThan(8),
                        "The busy street must represent substantially more than the former eight-slot population.");
                    Vector3 center = ResidentLifeCrowdCenter(pedestrians);
                    Vector3 direction = ResidentLifeStreetDirection(pedestrians);
                    yield return ResidentLifeFrame(camera, Shot.At("01-evening-street",
                        center - direction * 10f + Vector3.up * EyeHeight,
                        center + direction * 13f + Vector3.up * 1.1f, 70f));
                    yield return ResidentLifeFrame(camera, Shot.At("02-evening-overview",
                        center - direction * 11f + Vector3.up * 11f,
                        center + direction * 9f + Vector3.up * .8f, 72f));

                    var positions = new Dictionary<CityPedestrianActor, Vector3>();
                    foreach (CityPedestrianActor actor in pedestrians.Actors)
                        if (actor.IsSpawned) positions.Add(actor, actor.Position);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    foreach (KeyValuePair<CityPedestrianActor, Vector3> pair in positions)
                        Assert.That(Vector3.Distance(pair.Key.Position, pair.Value), Is.LessThan(.0001f),
                            "A paused city must not move residents or age their routes.");
                    Assert.That(pedestrians.ActiveCount, Is.EqualTo(eveningCount));

                    // Advancing the shared calendar jumps directly; it does
                    // not wait through the game's complete day in real time.
                    AdvanceResidentLifeClockTo(2 * 60);
                    Assert.That(pedestrians.CurrentActiveLimit, Is.EqualTo(5));
                    PlaceResidentLifeStreet(city);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    AssertResidentLifePool(pedestrians);
                    Debug.Log("CityResidentLife: night active=" + pedestrians.ActiveCount + " target=" + pedestrians.CurrentActiveLimit);
                    Assert.That(pedestrians.ActiveCount, Is.InRange(1, 5));
                    center = ResidentLifeCrowdCenter(pedestrians);
                    direction = ResidentLifeStreetDirection(pedestrians);
                    yield return ResidentLifeFrame(camera, Shot.At("03-night-overview",
                        center - direction * 9f + Vector3.up * 7f,
                        center + direction * 9f + Vector3.up * .8f, 68f));

                    AdvanceResidentLifeClockTo(12 * 60);
                    Assert.That(pedestrians.CurrentActiveLimit, Is.EqualTo(22));
                    PlaceResidentLifeStreet(city);
                    for (int frame = 0; frame < 3; frame++) yield return null;
                    AssertResidentLifePool(pedestrians);
                    Debug.Log("CityResidentLife: noon active=" + pedestrians.ActiveCount + " target=" + pedestrians.CurrentActiveLimit);
                    Assert.That(pedestrians.ActiveCount, Is.InRange(1, 22));
                    center = ResidentLifeCrowdCenter(pedestrians);
                    direction = ResidentLifeStreetDirection(pedestrians);
                    yield return ResidentLifeFrame(camera, Shot.At("04-noon-street",
                        center - direction * 9f + Vector3.up * EyeHeight,
                        center + direction * 12f + Vector3.up * 1.1f, 70f));

                    pedestrians.enabled = false;
                    Assert.That(pedestrians.ActiveCount, Is.Zero);
                    foreach (CityPedestrianActor actor in pedestrians.Actors)
                        Assert.That(actor.CollisionEnabled, Is.False,
                            "Disabling the director releases every resident capsule.");
                    pedestrians.enabled = true;
                    Assert.That(pedestrians.WarmResidentPopulation(), Is.True,
                        "Re-enabling can represent the same resident world again.");
                    yield return null;
                    AssertResidentLifePool(pedestrians);
                }
                Debug.Log("CityResidentLife: evening, night and noon density; stable IDs, bounded pool, pause and disable/re-enable verified.");
            }
            finally
            {
                foreach (Renderer renderer in hiddenPlayer) if (renderer != null) renderer.enabled = true;
                if (camera != null)
                {
                    camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
                    camera.fieldOfView = fieldOfView;
                }
                if (follow != null) follow.enabled = wasFollowing;
                if (pedestrians != null) pedestrians.enabled = true;
            }
        }

        private static void AdvanceResidentLifeClockTo(int minute)
        {
            double difference = minute - GameSessionState.GameTimeOfDayMinutes;
            if (difference <= 0d) difference += 1440d;
            GameSessionState.AdvanceGameTime((float)(difference / GameTimeState.GameMinutesPerRealSecond));
        }

        private static void PlaceResidentLifeStreet(CityGameRoot city)
        {
            CityPedestrianDirector pedestrians = city.Pedestrians;
            pedestrians.enabled = false;
            var positions = new List<Vector3>();
            for (int id = 0; id < CityResidentLifeRules.ResidentCount; id++)
                if (pedestrians.Residents.TrySample(id, GameSessionState.GameDayIndex,
                        GameSessionState.GameTimeOfDayMinutes, out CityResidentWorld.StreetSample sample))
                    positions.Add(sample.Position);
            Assert.That(positions, Is.Not.Empty, "The selected time has a scheduled outdoor population.");
            int best = 0, bestCount = -1;
            for (int index = 0; index < positions.Count; index++)
            {
                int count = 0;
                foreach (Vector3 point in positions)
                    if ((point - positions[index]).sqrMagnitude <= 70f * 70f) count++;
                if (count > bestCount) { best = index; bestCount = count; }
            }
            city.Player.Motor.Teleport(positions[best] + Vector3.up * PlayerFactory.GroundedRootOffset);
            Physics.SyncTransforms();
            pedestrians.enabled = true;
            for (int attempt = 0; attempt < CityResidentLifeRules.ResidentCount &&
                 pedestrians.ActiveCount < pedestrians.CurrentActiveLimit; attempt++)
                if (!pedestrians.WarmResidentPopulation()) break;
        }

        private static void AssertResidentLifePool(CityPedestrianDirector pedestrians)
        {
            Assert.That(pedestrians.PoolCapacity, Is.LessThanOrEqualTo(pedestrians.Profile.PoolSize));
            Assert.That(pedestrians.ActiveCount, Is.LessThanOrEqualTo(pedestrians.Profile.PoolSize));
            var active = new HashSet<int>();
            foreach (CityPedestrianActor actor in pedestrians.Actors)
            {
                if (!actor.IsSpawned) continue;
                int id = pedestrians.ResidentIndex(actor);
                Assert.That(id, Is.InRange(0, CityResidentLifeRules.ResidentCount - 1));
                Assert.That(active.Add(id), Is.True, "One resident cannot appear twice.");
                CityPedestrianDefaultNpcBody body = actor.Presentation.GetComponent<CityPedestrianDefaultNpcBody>();
                Assert.That(body, Is.Not.Null);
                Assert.That(body.CharacterId, Is.EqualTo(DefaultNpcPopulation.PedestrianId(id)));
            }
            var cached = new HashSet<string>();
            foreach (CityPedestrianDefaultNpcBody body in pedestrians.GetComponentsInChildren<CityPedestrianDefaultNpcBody>(true))
                Assert.That(cached.Add(body.CharacterId), Is.True, "The bounded body cache keeps one body per permanent ID.");
        }

        private static Vector3 ResidentLifeCrowdCenter(CityPedestrianDirector pedestrians)
        {
            CityPedestrianActor actor = ResidentLifeFocusActor(pedestrians);
            if (actor != null) return actor.Position;
            Assert.Fail("The resident capture requires a visible street actor.");
            return default;
        }

        private static Vector3 ResidentLifeStreetDirection(CityPedestrianDirector pedestrians)
        {
            CityPedestrianActor actor = ResidentLifeFocusActor(pedestrians);
            if (actor != null && actor.TravelDirection.sqrMagnitude > .1f) return actor.TravelDirection.normalized;
            return Vector3.forward;
        }

        private static CityPedestrianActor ResidentLifeFocusActor(CityPedestrianDirector pedestrians)
        {
            CityPedestrianActor best = null;
            int bestCount = -1;
            foreach (CityPedestrianActor candidate in pedestrians.Actors)
            {
                if (!candidate.IsSpawned) continue;
                int count = 0;
                foreach (CityPedestrianActor other in pedestrians.Actors)
                    if (other.IsSpawned && (candidate.Position - other.Position).sqrMagnitude < 24f * 24f) count++;
                if (count > bestCount) { best = candidate; bestCount = count; }
            }
            return best;
        }

        private static IEnumerator ResidentLifeFrame(Camera camera, Shot shot)
        {
            camera.transform.SetPositionAndRotation(shot.Position,
                Quaternion.LookRotation(shot.Target - shot.Position));
            camera.fieldOfView = shot.FieldOfView;
            yield return null;
            CaptureCurrentCamera(camera, "CityResidentLife", shot.Name);
        }
    }
}
