using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator RidgeSound_MachineryFiltersThroughADeckAndClearsAtItsEdge()
        {
            var listener = new GameObject("Listener test");
            listener.transform.position = new Vector3(0f, 101f, 0f);
            var ears = listener.AddComponent<WorldSoundListener>();
            var deck = new GameObject("Deck test");
            deck.transform.position = new Vector3(0f, 100f, 0f);
            deck.AddComponent<BoxCollider>().size = new Vector3(6f, .4f, 6f);
            var host = new GameObject("Machine test");
            host.transform.position = new Vector3(0f, 98f, 0f);
            var source = host.AddComponent<AudioSource>();
            var tone = CityWorkAudio.Configure(source, true, 6500f, 16f);
            var path = new CityWorkSoundPath(source, tone);
            const float gain = .32f;
            Physics.SyncTransforms();

            path.Apply(gain);
            Assert.That(source.volume, Is.LessThan(gain * .5f));
            Assert.That(tone.cutoffFrequency, Is.LessThan(2000f));
            Assert.That(ears, Is.SameAs(WorldSoundListener.Active));

            // With both endpoints beyond the edge, the very same source is clear.
            listener.transform.position += Vector3.right * 4f;
            host.transform.position += Vector3.right * 4f;
            yield return new WaitForSeconds(WorldSoundListener.RefreshSeconds + .05f);
            path.Apply(gain);
            Assert.That(source.volume, Is.EqualTo(gain).Within(.001f));
            Assert.That(tone.cutoffFrequency, Is.EqualTo(6500f).Within(1f));
        }

        [UnityTest, Timeout(360000)]
        public IEnumerator RidgeSound_FollowsShelterWalkingFeetAndDistrictStreaming()
        {
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            Assert.That(NewGameStartService.TryStart(NewGameLocation.Ridge, 12 * 60), Is.True);
            float deadline = Time.realtimeSinceStartup + 120f;
            do
            {
                yield return null;
                city = Object.FindAnyObjectByType<CityGameRoot>();
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline));
            } while (!city || !city.IsInitialized || SceneTransitionService.IsTransitioning);
            pilotScene = SceneManager.GetActiveScene();
            var pool = city.Soundscape;
            Assert.That(pool && city.RidgeDistrictReady, Is.True);
            Assert.That(pool.GetLoopSources().Count + pool.GetDetailSources().Count, Is.EqualTo(CitySoundscapeDirector.OwnedSourceCount));
            var weather = city.RainSound.GetComponent<CityWeatherSoundscape>();
            var motor = city.Player.Motor;
            motor.SetInputEnabled(false);
            GameSessionState.TrySetDebugWeather(WeatherKind.HeavyRain);
            city.Weather.ApplyCurrentWeather(true);
            var court = city.World.Root.GetComponentInChildren<RidgeKioskCourtWorld>();
            Assert.That(court, Is.Not.Null);
            var umbrella = court.Plan.Table(0) + Vector3.left * .92f + Vector3.up * PlayerFactory.GroundedRootOffset;
            motor.Teleport(umbrella);
            for (int i = 0; i < 100 && (weather.Cover < .95f || weather.LastContact != RetroSfxId.RainOnCanvas); i++)
                yield return null;
            Assert.That(weather.Cover, Is.GreaterThan(.9f), "Shelter must attenuate the diffuse rain at the player's ears.");
            Assert.That(weather.LastContact, Is.EqualTo(RetroSfxId.RainOnCanvas));
            float underRoof = city.RainSound.Source.volume;

            var open = CityPawnshopDescentPlan.ForLayout(city.Layout).BenchLandingDock;
            motor.Teleport(open + Vector3.up * PlayerFactory.GroundedRootOffset);
            for (int i = 0; i < 50 && weather.Cover > .05f; i++) yield return null;
            Assert.That(city.RainSound.Source.volume, Is.GreaterThan(underRoof * 2f));

            var ambient = city.AmbientPedestrians;
            var feet = motor.GetComponent<NpcFootstepDirector>();
            var partition = new CityDistrictStreamingPlan(city.Layout);
            bool heard = false;
            for (int i = 0; i < 220 && !heard; i++)
            {
                CityPedestrianPresentation walker = null;
                float nearest = float.PositiveInfinity;
                for (int slot = 0; slot < CityAmbientPedestrians.MaximumActive; slot++)
                {
                    var candidate = ambient.GetWalkerBody(slot);
                    if (!candidate || !candidate.IsMoving || candidate.IsRestingAtRail ||
                        !partition.IsRidge(candidate.transform.position)) continue;
                    float distance = (candidate.transform.position - motor.transform.position).sqrMagnitude;
                    if (distance >= nearest) continue;
                    nearest = distance;
                    walker = candidate;
                }
                if (walker)
                {
                    Assert.That(NpcFootstepSources.Prune(), Does.Contain(walker.transform));
                    if ((motor.transform.position - walker.transform.position).sqrMagnitude > 16f)
                        motor.Teleport(walker.transform.position - walker.transform.forward * 1.9f);
                }
                yield return null;
                heard = walker && feet.LastAudibleWalker == walker.transform;
            }
            Assert.That(heard, Is.True, "A moving ambient resident must play the contact on the nearby stamped street.");
            Assert.That(feet.LastKind, Is.Not.EqualTo(FootstepGroundKind.None));
            string reviewFolder = System.Environment.GetEnvironmentVariable("BARGAME_RIDGE_SOUND_REVIEW");
            if (!string.IsNullOrEmpty(reviewFolder))
                yield return AreaCaptureFixture.RecordRidgeSound(city, reviewFolder, motor.transform.position, umbrella);

            Vector3 lower = city.BusPlans.SelectMany(line => line.Stops.Select(stop =>
                CityBusStopWaitPlanner.StandingPosition(line, stop, city.Layout))).OrderByDescending(partition.SignedDistance).First();
            motor.Teleport(lower);
            deadline = Time.realtimeSinceStartup + 90f;
            while ((!city.LowerDistrictReady || city.RidgeDistrictReady) && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(city.LowerDistrictReady && !city.RidgeDistrictReady, Is.True);
            Assert.That(city.Soundscape, Is.SameAs(pool));
            Assert.That(pool.Plan.LoopingSources.Any(source => source.Cue == CitySourceSoundId.ParkFountainLoop), Is.True);
            Assert.That(pool.Plan.Sources.Any(source => source.PhysicalOwner == CitySoundPhysicalOwnerKind.HillsideDownpipe), Is.False);
        }
    }
}
