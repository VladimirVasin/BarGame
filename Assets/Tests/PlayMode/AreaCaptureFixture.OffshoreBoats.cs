using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Focused hero-local offshore lifetime, routes, imported art, sound and day/night captures.")]
        public IEnumerator CityOffshoreBoats()
        {
            Type assetSetup = Type.GetType("BarPromenade.Editor.CityOffshoreBoatAssetSetup, BarPromenade.Editor");
            Assert.That(assetSetup, Is.Not.Null);
            assetSetup.GetMethod("ValidateOrThrow").Invoke(null, null);
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime(360f);
            CityGameRoot city = null;
            CityOffshoreBoatController fleet = null;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                fleet = Object.FindAnyObjectByType<CityOffshoreBoatController>();
                Assert.That(fleet, Is.Not.Null, "The default coastline needs a local fishing-vessel controller.");
                Assert.That(fleet.IsSpawned, Is.False, "Building the city must not create a global fleet.");
                Assert.That(fleet.Plan, Is.Null);
                Assert.That(fleet.Sound, Is.Null);
                Assert.That(fleet.Boats, Is.Empty);
                Assert.That(fleet.GetComponentsInChildren<Renderer>(true), Is.Empty);
                Assert.That(fleet.GetComponentsInChildren<AudioSource>(true), Is.Empty);

                CitySeacoastFrame frame = city.World.SeacoastPlan.Frame;
                Assert.That(city.World.SeacoastPlan.TryGetPart(CitySeacoastPlanner.PierDeckHeadId,
                    out CitySeacoastPartDescriptor pier), Is.True);
                Vector3 shore = new Vector3(pier.Center.x, frame.BeachEdgeTopY, frame.WaterlineZ - 1f);
                city.Player.Motor.SetInputEnabled(false);
                city.Player.Motor.Teleport(shore + Vector3.back * 80f);
                Camera.main.transform.position = shore + Vector3.up * EyeHeight;
                fleet.Advance(CityOffshoreBoatController.PresenceFadeSeconds + 1f);
                Assert.That(fleet.IsSpawned, Is.False,
                    "A camera at the sea cannot spawn boats while the hero stays in the city.");
                city.Player.Motor.Teleport(shore);
                fleet.Advance(CityOffshoreBoatController.PresenceFadeSeconds + 1f);
                Assert.That(fleet.IsSpawned, Is.True, "The hero approaching the shore creates a local pass.");
                Assert.That(fleet.SpawnAnchor.x, Is.EqualTo(shore.x));
                ValidateOffshoreCourses(city, fleet);
                var route = fleet.Plan.Routes[0];
                fleet.ApplyAt(route.DurationSeconds * 0.5 - route.PhaseSeconds, Time.timeSinceLevelLoad);
                Vector3 point = fleet.Boats[0].position;
                return new[] { Shot.At("offshore-00-shore-day",
                    new Vector3(point.x, fleet.Plan.SeaTopY + EyeHeight, fleet.Plan.WaterlineZ - 1f),
                    point + Vector3.up, 62f) };
            });

            Camera camera = Camera.main;
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>())
                renderer.enabled = false;
            city.Player.Motor.SetInputEnabled(false);

            CitySeacoastPlan coast = city.World.SeacoastPlan;
            Vector3 originalHero = city.Player.GameObject.transform.position;
            Vector3 originalAnchor = fleet.SpawnAnchor;
            CityOffshoreBoatPlan originalPlan = fleet.Plan;
            camera.transform.position = originalHero + new Vector3(90f, EyeHeight, -90f);
            fleet.Advance(0.2f);
            Assert.That(fleet.Plan, Is.SameAs(originalPlan), "Camera motion must not move the boat courses.");
            Assert.That(fleet.SpawnAnchor, Is.EqualTo(originalAnchor));
            Assert.That(fleet.IsSpawned, Is.True, "Moving only the camera inland must not remove the hero's fleet.");

            float shiftedX = Mathf.Clamp(originalHero.x + 48f,
                coast.Frame.BeachRowBounds.xMin + 10f, coast.Frame.BeachRowBounds.xMax - 10f);
            if (Mathf.Abs(shiftedX - originalAnchor.x) <= CityOffshoreBoatController.RelocationDistance)
                shiftedX = originalHero.x - 48f;
            Vector3 shiftedHero = new Vector3(shiftedX, coast.Frame.BeachEdgeTopY, coast.Frame.WaterlineZ - 1f);
            Assert.That(Mathf.Abs(shiftedHero.x - originalAnchor.x),
                Is.GreaterThan(CityOffshoreBoatController.RelocationDistance));
            city.Player.Motor.Teleport(shiftedHero);
            fleet.Advance(0.1f);
            Assert.That(fleet.Plan, Is.SameAs(originalPlan), "Existing hulls fade before a course is relocated.");
            fleet.Advance(CityOffshoreBoatController.PresenceFadeSeconds + 1f);
            Assert.That(fleet.IsSpawned, Is.False);
            fleet.Advance(CityOffshoreBoatController.PresenceFadeSeconds + 1f);
            Assert.That(fleet.IsSpawned, Is.True);
            Assert.That(fleet.Plan, Is.Not.SameAs(originalPlan));
            Assert.That(fleet.SpawnAnchor.x, Is.EqualTo(shiftedHero.x));
            ValidateOffshoreCourses(city, fleet);

            // Departure and return are driven by the physical hero, even while the
            // borrowed capture camera keeps looking at the water.
            camera.transform.position = shiftedHero + Vector3.up * EyeHeight;
            city.Player.Motor.Teleport(shiftedHero + Vector3.back * 80f);
            CityOffshoreBoatPlan beforeDeparture = fleet.Plan;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                fleet.Advance(CityOffshoreBoatController.PresenceFadeSeconds + 1f);
                Assert.That(fleet.Plan, Is.SameAs(beforeDeparture), "Pause freezes the local fleet lifetime.");
            }
            fleet.Advance(CityOffshoreBoatController.PresenceFadeSeconds + 1f);
            Assert.That(fleet.IsSpawned, Is.False);
            Assert.That(fleet.Plan, Is.Null);
            Assert.That(fleet.Sound, Is.Null);
            Assert.That(fleet.Boats, Is.Empty);
            Assert.That(CitySeaResources.WaterMaterial.GetVector("_OffshoreLamp0").w, Is.Zero);
            Assert.That(CitySeaResources.WaterMaterial.GetVector("_OffshoreLamp1").w, Is.Zero);
            Assert.That(CitySeaResources.WaterMaterial.GetFloat("_LanternGlint"), Is.GreaterThan(0f));
            yield return null;
            Assert.That(fleet.GetComponentsInChildren<Renderer>(true), Is.Empty);
            Assert.That(fleet.GetComponentsInChildren<AudioSource>(true), Is.Empty);
            city.Player.Motor.Teleport(shiftedHero);
            fleet.Advance(CityOffshoreBoatController.PresenceFadeSeconds + 1f);
            Assert.That(fleet.IsSpawned, Is.True);
            Assert.That(fleet.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(3));
            Assert.That(fleet.Sound.HornsPlayed, Is.Zero, "Returning must not replay horns missed inland.");

            for (int variant = 0; variant < fleet.Plan.Routes.Count; variant++)
            {
                var route = fleet.Plan.Routes[variant];
                double middle = route.DurationSeconds * 0.5 - route.PhaseSeconds;
                fleet.ApplyAt(middle, 15f);
                Vector3 center = fleet.Boats[variant].position;
                CaptureOffshore(camera, city, fleet, variant, 0d,
                    new Vector3(center.x - 5f, fleet.Plan.SeaTopY + EyeHeight, fleet.Plan.WaterlineZ - 1f),
                    $"offshore-0{variant + 1}-variant-{variant}-day");
                Assert.That(city.World.SeacoastPlan.TryGetPart(CitySeacoastPlanner.PierDeckHeadId,
                    out CitySeacoastPartDescriptor pier), Is.True);
                CaptureOffshore(camera, city, fleet, variant, 0d,
                    pier.Center + Vector3.up * (pier.Size.y * 0.5f + EyeHeight),
                    $"offshore-0{variant + 3}-pier-variant-{variant}");
            }

            Assert.That(city.World.SeacoastPlan.TryGetPart(CitySeacoastPlanner.MolDeckHeadId,
                out CitySeacoastPartDescriptor mol), Is.True);
            CaptureOffshore(camera, city, fleet, 0, 0d,
                mol.Center + Vector3.up * (mol.Size.y * 0.5f + EyeHeight),
                "offshore-09-mol-day");
            var first = fleet.Plan.Routes[0];
            double passTime = first.DurationSeconds * 0.5 - first.PhaseSeconds;
            fleet.ApplyAt(passTime, 15f);
            Vector3 before = fleet.Boats[0].position;
            fleet.ApplyAt(passTime + 12d, 27f);
            Assert.That(Vector3.Distance(before, fleet.Boats[0].position), Is.InRange(4.9f, 5.7f));
            Vector3 beforePause = fleet.Boats[0].position;
            double pauseTime = fleet.ElapsedSeconds;
            using (GameTimeScaleRuntime.AcquirePause())
            {
                yield return null;
                yield return null;
                Assert.That(fleet.ElapsedSeconds, Is.EqualTo(pauseTime));
                Assert.That(fleet.Boats[0].position, Is.EqualTo(beforePause));
            }

            // On the mol, sound stays attached to actual imported anchors.
            camera.transform.position = city.Player.GameObject.transform.position + Vector3.up * EyeHeight;
            fleet.ApplyAt(passTime, 15f);
            CityOffshoreBoatSound sound = fleet.Sound;
            Assert.That(sound.GetComponentsInChildren<AudioSource>(true).Length, Is.EqualTo(3));
            Assert.That(sound.HornsPlayed, Is.Zero, "Loading a coast must not greet the player with a horn.");
            sound.Advance(2f, true);
            Assert.That(sound.EngineSources[0].volume, Is.GreaterThan(0.01f),
                "An in-view working boat must have an audible motor at the shore.");
            for (int i = 0; i < fleet.Boats.Count; i++)
            {
                var source = sound.EngineSources[i];
                Assert.That(source.spatialBlend, Is.EqualTo(1f));
                Assert.That(source.dopplerLevel, Is.Zero);
                Assert.That(source.outputAudioMixerGroup, Is.Not.Null);
                Assert.That(source.volume, Is.LessThanOrEqualTo(CityOffshoreBoatSound.MaximumEngineVolume));
                Assert.That(source.transform.position, Is.EqualTo(
                    CityOffshoreBoatAssetProvider.FindPart(fleet.Boats[i].gameObject, "ANCHOR_Engine").position));
            }
            float due = sound.SecondsUntilHorn;
            sound.Advance(500f, false);
            Assert.That(sound.SecondsUntilHorn, Is.EqualTo(due));
            sound.Advance(due + 0.01f, true);
            Assert.That(sound.HornsPlayed, Is.EqualTo(1));
            Assert.That(sound.HornSource.spatialBlend, Is.EqualTo(1f));
            Assert.That(sound.HornSource.transform.position, Is.EqualTo(
                CityOffshoreBoatAssetProvider.FindPart(fleet.Boats[sound.HornBoatIndex].gameObject, "ANCHOR_Horn").position));
            sound.Advance(4000f, true);
            Assert.That(sound.HornsPlayed, Is.LessThanOrEqualTo(2), "Never replay missed horn windows.");

            GameSessionState.AdvanceGameTime((float)(21d * 60d - GameSessionState.GameTimeOfDayMinutes));
            city.DayNight.ApplyCurrentTime(true);
            yield return null;
            for (int i = 0; i < fleet.Plan.Routes.Count; i++)
            {
                var route = fleet.Plan.Routes[i];
                double time = route.DurationSeconds * 0.5 - route.PhaseSeconds;
                fleet.ApplyAt(time, 15f);
                Vector3 center = fleet.Boats[i].position;
                CaptureOffshore(camera, city, fleet, i, 0d,
                    new Vector3(center.x - 4f, fleet.Plan.SeaTopY + EyeHeight, fleet.Plan.WaterlineZ - 1f),
                    $"offshore-0{i + 5}-shore-variant-{i}-night");
                CaptureOffshore(camera, city, fleet, i, 12d,
                    new Vector3(center.x - 4f, fleet.Plan.SeaTopY + EyeHeight, fleet.Plan.WaterlineZ - 1f),
                    $"offshore-0{i + 7}-motion-variant-{i}-night");
            }
            Assert.That(camera.farClipPlane, Is.EqualTo(48f));
            Assert.That(fleet.GetComponentsInChildren<Collider>(true), Is.Empty);
            Assert.That(fleet.GetComponentsInChildren<Light>(true), Is.Empty);
            Assert.That(fleet.GetComponentsInChildren<Rigidbody>(true), Is.Empty);
            sound = fleet.Sound;
            fleet.enabled = false;
            foreach (var engine in sound.EngineSources) Assert.That(engine.volume, Is.Zero);
            Assert.That(CitySeaResources.WaterMaterial.GetVector("_OffshoreLamp0").w, Is.Zero);
            Assert.That(CitySeaResources.WaterMaterial.GetFloat("_LanternGlint"), Is.GreaterThan(0f));
            Debug.Log("OFFSHORE BOATS ACCEPTANCE OK: hero-local lazy spawn, camera independence, fade/reanchor, departure cleanup, return without horn catchup, imported metres, safe courses, motion, pause, 3 spatial voices, separate sea lamps, day/night frames.");
        }

        private static void CaptureOffshore(Camera camera, CityGameRoot city, CityOffshoreBoatController fleet,
            int variant, double passOffset, Vector3 from, string name)
        {
            city.Player.Motor.Teleport(from - Vector3.up * EyeHeight);
            fleet.Advance(CityOffshoreBoatController.PresenceFadeSeconds + 1f);
            fleet.Advance(CityOffshoreBoatController.PresenceFadeSeconds + 1f);
            Assert.That(fleet.IsSpawned, Is.True, name + " must depict a pass local to the hero.");
            if (variant >= fleet.Plan.Routes.Count) return;
            var route = fleet.Plan.Routes[variant];
            double seconds = route.DurationSeconds * 0.5 - route.PhaseSeconds + passOffset;
            fleet.ApplyAt(seconds, Time.timeSinceLevelLoad);
            Vector3 target = fleet.Boats[variant].position + Vector3.up;
            camera.transform.SetPositionAndRotation(from, Quaternion.LookRotation(target - from));
            camera.fieldOfView = 62f;
            CaptureCurrentCamera(camera, SceneIds.City, name);
        }

        private static void ValidateOffshoreCourses(CityGameRoot city, CityOffshoreBoatController fleet)
        {
            Assert.That(fleet.Plan.Routes.Count, Is.InRange(1, CityOffshoreBoatPlan.MaximumBoatCount));
            Assert.That(CityOffshoreBoatPlanner.Create(city.Layout.Seed, null, null), Is.Null);
            CitySeacoastPlan coast = city.World.SeacoastPlan;
            Vector3 shore = new Vector3(fleet.SpawnAnchor.x, 0f, coast.Frame.WaterlineZ);
            Assert.That(CityOffshoreBoatPlanner.ShorePresence(coast, shore), Is.EqualTo(1f));
            Assert.That(CityOffshoreBoatPlanner.ShorePresence(coast, shore + Vector3.back * 8f), Is.EqualTo(1f));
            Assert.That(CityOffshoreBoatPlanner.ShorePresence(coast, shore + Vector3.back * 18f), Is.EqualTo(0.5f).Within(0.001f));
            Assert.That(CityOffshoreBoatPlanner.ShorePresence(coast, shore + Vector3.back * 28f), Is.Zero);
            foreach (float edgeX in new[] { coast.Frame.BeachRowBounds.xMin - 29f, coast.Frame.BeachRowBounds.xMax + 29f })
                Assert.That(CityOffshoreBoatPlanner.ShorePresence(coast,
                    new Vector3(edgeX, 0f, shore.z)), Is.Zero, "The shoreline cannot extend infinitely sideways.");
            foreach (string deckId in new[] { CitySeacoastPlanner.PierDeckHeadId, CitySeacoastPlanner.MolDeckHeadId })
            {
                Assert.That(coast.TryGetPart(deckId, out CitySeacoastPartDescriptor deck), Is.True);
                Assert.That(CityOffshoreBoatPlanner.ShorePresence(coast, deck.Center), Is.EqualTo(1f));
            }
            var island = CityLighthouseIslandPlanner.Create(city.Layout.Seed, coast);
            var duplicate = CityOffshoreBoatPlanner.Create(city.Layout.Seed, coast, island, fleet.SpawnAnchor.x);
            var obstacles = new List<Rect>();
            foreach (var part in island.Parts)
                obstacles.Add(CityOffshoreBoatPlanner.ProjectedBounds(part.Center, part.Rotation, part.Size));
            foreach (var part in coast.Parts)
                obstacles.Add(CityOffshoreBoatPlanner.ProjectedBounds(part.Center, part.Rotation, part.Size));
            for (int i = 0; i < fleet.Plan.Routes.Count; i++)
            {
                var route = fleet.Plan.Routes[i];
                Assert.That(Mathf.Abs((route.Start.x + route.End.x) * 0.5f - fleet.SpawnAnchor.x),
                    Is.LessThanOrEqualTo(CityOffshoreBoatPlanner.MaximumLocalCourseOffset + 0.001f));
                Assert.That(route.Start, Is.EqualTo(duplicate.Routes[i].Start));
                Assert.That(route.PhaseSeconds, Is.EqualTo(duplicate.Routes[i].PhaseSeconds));
                double start = -route.PhaseSeconds;
                Assert.That(route.Sample(start).Presence, Is.Zero);
                Assert.That(route.Sample(start + route.CycleSeconds - 0.01d).Presence, Is.Zero);
                Assert.That(route.Sample(start + route.CycleSeconds + 0.01d).Presence, Is.LessThan(0.001f));
                for (int step = 0; step <= 20; step++)
                {
                    Vector3 point = route.Sample(start + route.DurationSeconds * step / 20d).Position;
                    Rect sweep = Rect.MinMaxRect(point.x - 8.5f, point.z - 2f, point.x + 8.5f, point.z + 2f);
                    foreach (Rect obstacle in obstacles)
                        Assert.That(sweep.Overlaps(obstacle), Is.False, route.StableId + " crosses existing coast scenery.");
                }
            }
        }
    }
}
