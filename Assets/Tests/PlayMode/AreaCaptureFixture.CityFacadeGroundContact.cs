using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(300000)]
        [Explicit("Ground can meet the edge of a facade block while missing its window. Inspect the resulting west Ridge facade in daylight and at night.")]
        public IEnumerator CityFacadeGroundContactAtBlockEdge()
        {
            Vector3 start = Vector3.zero, end = Vector3.right * 3f;
            Vector3 a = new(.02f, .05f, -.1f), b = new(.02f, .05f, .1f), c = new(.05f, .05f, 0f);
            Assert.That(CityFacadeGroundContact.ReachesBlock(start, end, a, b, c), Is.True);
            Assert.That(CityFacadeGroundContact.ReachesBlock(start + Vector3.right, end - Vector3.right, a, b, c),
                Is.False, "The triangle must miss the frame while reaching the block.");
            Assert.That(CityFacadeGroundContact.ReachesBlock(start, end, a - Vector3.up, b - Vector3.up, c - Vector3.up),
                Is.False, "A surface below the block must leave its window intact.");

            GameSessionState.BeginNewGame("Facade block ground contact");
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
            CityGameRoot city = null;
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (Time.realtimeSinceStartup < deadline)
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                if (city != null && city.IsInitialized && city.World.RidgeRoot != null) break;
                yield return null;
            }
            Assert.That(city != null && city.IsInitialized && city.World.RidgeRoot != null, Is.True);
            city.Player.Motor.SetInputEnabled(false);

            Vector3 reported = new(-128.1f, 35.4f, -315.4f);
            var buildings = city.World.RidgeRoot.GetComponentsInChildren<CityBuildingAssetRegistry>(true);
            var nearby = buildings.Where(building =>
                (new Vector2(building.transform.position.x, building.transform.position.z) -
                 new Vector2(reported.x, reported.z)).sqrMagnitude < 24f * 24f).ToArray();
            Assert.That(nearby, Is.Not.Empty);
            var covered = nearby.SelectMany(building => building.WindowSlots
                .Where(slot => (building.GroundedWindowMask & (1UL << slot.Uv2SlotId)) != 0)
                .Select(slot => (building, slot, center: building.transform.TransformPoint(slot.LocalCenter))))
                .OrderBy(window => (window.center - reported).sqrMagnitude).ToArray();
            Assert.That(covered, Is.Not.Empty, "The reported stretch must contain a ground-contact facade block.");
            foreach (var building in buildings.Where(building => building.GroundedWindowMask != 0))
            {
                Assert.That((building.GuaranteedWindowMask | building.OptionalWindowMask | building.LitWindowMask) &
                    building.GroundedWindowMask, Is.Zero, "Solid wall must have no window light on any clock phase.");
                Assert.That(building.GroundedWindowMask & building.WindowSlots
                    .Where(slot => slot.OpeningKind == CityBuildingOpeningKind.BalconyDoor)
                    .Aggregate(0UL, (mask, slot) => mask | (1UL << slot.Uv2SlotId)), Is.Zero);
                foreach (var part in building.Parts.Where(part =>
                    part.Role is CityBuildingMeshRole.WindowGlass or CityBuildingMeshRole.WindowFrame))
                {
                    if (!part.Renderer.enabled) continue;
                    var mesh = part.Renderer.GetComponent<MeshFilter>().sharedMesh;
                    Vector2[] slots = mesh.uv2;
                    Assert.That(mesh.triangles.Any(i =>
                        (building.GroundedWindowMask & (1UL << Mathf.FloorToInt(
                            slots[i].x * CityBuildingAssetRegistry.WindowSlotUv2Divisor))) != 0), Is.False);
                }
            }

            var shot = covered[0];
            Assert.That(shot.building.WindowSlots.Any(slot => slot.Side == shot.slot.Side &&
                (shot.building.GroundedWindowMask & (1UL << slot.Uv2SlotId)) == 0), Is.True,
                "The checked side must also retain an above-ground opening.");
            Vector3 outward = shot.building.transform.TransformDirection(
                global::BarPromenade.CityWindowLighting.LocalOutward(shot.slot.Side));
            Vector3 target = shot.center + Vector3.up * 1.6f;
            Vector3 eye = reported + Vector3.up * 1.25f + outward * 1.4f;
            if (Vector3.Dot(eye - shot.center, outward) < 1f)
                eye = target + outward * 4f + Vector3.Cross(Vector3.up, outward) * 3f;
            city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                renderer.forceRenderingOff = true;
            SetWindowLightCamera(city, eye, target);
            city.Visibility.Refresh(true);
            city.DayNight.ApplyCurrentTime(true);
            CaptureCurrentCamera(city.Camera, "CityFacadeGroundContact", "west-ridge-day", 1280, 720);
            double minutes = (23 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
            GameSessionState.AdvanceGameTime((float)(minutes / GameTimeState.GameMinutesPerRealSecond));
            city.DayNight.ApplyCurrentTime(true);
            city.Lighting.WindowLights.RefreshImmediate();
            CaptureCurrentCamera(city.Camera, "CityFacadeGroundContact", "west-ridge-night", 1280, 720);
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            GameSessionState.BeginNewGame("Facade block ground contact cleanup");
        }
    }
}
