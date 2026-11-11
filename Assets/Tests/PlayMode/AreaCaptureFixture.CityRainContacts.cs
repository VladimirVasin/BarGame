using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private const string RainCaptureFolder = "CityRainContacts";

        [UnityTest]
        [Timeout(180000)]
        [Explicit("First rain contact and visible precipitation on the live City and Home cameras.")]
        public IEnumerator CityRainContacts()
        {
            float previousCaptureDelta = Time.captureDeltaTime;
            try
            {
                Time.captureDeltaTime = 1f / 30f;
                GameSessionState.BeginNewGame("Test City rain contacts");
                Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
                yield return SceneManager.LoadSceneAsync(SceneIds.City, LoadSceneMode.Single);
                CityGameRoot city = null;
                yield return WaitFor(() => (city = Object.FindAnyObjectByType<CityGameRoot>()) != null && city.IsInitialized,
                    "The City did not compose.");
                CityRainContactField contacts = city.Rain.Contacts;
                Assert.That(contacts, Is.Not.Null);
                city.Weather.enabled = false;
                city.Rain.SetWindDrift(new Vector2(.8f, .65f));
                city.Rain.SetIntensity(1f);
                CityWetSurfaceRegistry.SetImmediate(1f);
                CityWaterResources.SetRainIntensity(1f);

                CityRainDripEdge eave = city.BusStops.GetComponentInChildren<CityRainDripEdge>();
                Assert.That(eave, Is.Not.Null);
                Vector3 lip = eave.Sample(.5f);
                Vector3 forward = eave.transform.forward;
                var canopyRay = new Ray(lip - .45f * forward + Vector3.up * 6f, Vector3.down);
                yield return WaitFor(() => contacts.ProbeSurface(canopyRay, 10f, out var roof) &&
                    Mathf.Abs(roof.Point.y - lip.y - .16f) < .055f,
                    "The visible bus canopy must stop a drop before the bench or pavement.");

                RuntimeOrientedBox puddle = city.World.Root.GetComponentsInChildren<CityRainWaterReceiver>()
                    .Where(receiver => receiver.Puddles != null).SelectMany(receiver => receiver.Puddles)
                    .First(patch => contacts.ProbeSurface(new Ray(patch.Center + 8f * Vector3.up, Vector3.down),
                        10f, out var hit) && hit.Kind == CityRainSurfaceKind.Puddle);
                Assert.That(contacts.ProbeSurface(new Ray(puddle.Center + 3f * Vector3.up, Vector3.down),
                    6f, out var standingWater), Is.True);
                Assert.That(standingWater.Kind, Is.EqualTo(CityRainSurfaceKind.Puddle));
                CityWetSurfaceRegistry.SetImmediate(0f);
                Assert.That(contacts.ProbeSurface(new Ray(puddle.Center + 3f * Vector3.up, Vector3.down),
                    6f, out var dryRoad), Is.True);
                Assert.That(dryRoad.Kind, Is.Not.EqualTo(CityRainSurfaceKind.Puddle));
                CityWetSurfaceRegistry.SetImmediate(1f);

                Camera camera = city.Camera;
                city.Player.Motor.SetInputEnabled(false);
                city.Player.Interactor.SetInputEnabled(false);
                camera.GetComponent<PlayerCameraFollow>().enabled = false;
                Vector3 pavement = lip - Vector3.up * 2.45f;
                city.Player.Motor.Teleport(pavement + forward * 1.65f);
                camera.transform.position = pavement + forward * 3.5f + eave.transform.right * 2.55f + Vector3.up * 1.44f;
                camera.transform.LookAt(pavement + Vector3.up * .89f);
                yield return WaitFor(() => contacts.VisibleQuadCount > 12, "Rain contacts did not reach the gameplay camera.");
                for (int frame = 0; frame < 12; frame++) yield return null;
                CaptureCurrentCamera(camera, RainCaptureFolder, "01-street-and-canopy-day");

                Vector3 up = Vector3.up;
                Vector3 cross = puddle.Rotation * Vector3.forward;
                city.Player.Motor.Teleport(puddle.Center + 1.9f * cross + 2.8f * (puddle.Rotation * Vector3.right) + .07f * up);
                camera.transform.position = puddle.Center + cross * 2.65f + up * .91f;
                camera.transform.LookAt(puddle.Center + up * .11f);
                double toNight = (21 * 60 - GameSessionState.GameTimeOfDayMinutes + 1440) % 1440;
                GameSessionState.AdvanceGameTime((float)(toNight / GameTimeState.GameMinutesPerRealSecond));
                city.DayNight.ApplyCurrentTime(true);
                yield return WaitFor(() => contacts.WaterRingCount > 0, "A falling drop never reached the selected puddle.");
                CaptureCurrentCamera(camera, RainCaptureFolder, "02-puddle-at-night");

                int beforePause = contacts.ActiveDropCount;
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    yield return null;
                    yield return null;
                    Assert.That(contacts.ActiveDropCount, Is.EqualTo(beforePause));
                }
                Assert.That(contacts.ActiveDropCount, Is.LessThanOrEqualTo(CityRainContactField.MaximumDrops));
                AssertBusRainAttachment(city.Bus.GetOrCreatePresentation());
                contacts.enabled = false;
                Assert.That(contacts.ActiveDropCount, Is.Zero);
                Assert.That(contacts.WaterRingCount, Is.Zero);

                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
                HomeInteriorRoot home = null;
                yield return WaitFor(() => (home = Object.FindAnyObjectByType<HomeInteriorRoot>()) != null && home.IsInitialized,
                    "Home did not compose.");
                CityRainField balconyRain = home.ExteriorAtmosphere.RainField;
                Assert.That(balconyRain.Contacts.ActiveDropCount, Is.Zero);
                Rect balcony = home.BalconyLayout.BalconyCameraActivationBounds;
                home.Player.Motor.Teleport(new Vector3(balcony.center.x, home.Layout.PlayerSpawn.y, balcony.center.y));
                yield return WaitFor(() => balconyRain.Contacts.VisibleQuadCount > 0,
                    "The visible passive balcony exterior did not receive rain.");
                Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.Balcony));
                CaptureCurrentCamera(Camera.main, RainCaptureFolder, "03-balcony-exterior");
                home.Player.Motor.Teleport(home.Layout.PlayerSpawn);
                yield return WaitFor(() => !balconyRain.RainRenderer.enabled && balconyRain.Contacts.ActiveDropCount == 0,
                    "The apartment must release the balcony's live drops.");
            }
            finally
            {
                Time.captureDeltaTime = previousCaptureDelta;
                GameSessionState.BeginNewGame("Test City rain contacts cleanup");
            }
        }

        private static IEnumerator WaitFor(System.Func<bool> condition, string message)
        {
            float deadline = Time.realtimeSinceStartup + TimeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.That(condition(), Is.True, message);
        }

        private static void AssertBusRainAttachment(CityBusPresentation bus)
        {
            Assert.That(bus, Is.Not.Null);
            Renderer pane = bus.Registry.RendererBindings.First(binding => binding.MaterialSlot == CityBusMaterialSlot.Glass).Renderer;
            Renderer overlay = bus.GlassRainOverlays.First(renderer => renderer.transform.parent == pane.transform);
            Vector3 local = pane.GetComponent<MeshFilter>().sharedMesh.bounds.center;
            bus.RecordRainHit(pane, pane.transform.TransformPoint(local));
            var properties = new MaterialPropertyBlock();
            overlay.GetPropertyBlock(properties);
            Assert.That(properties.GetVectorArray("_BusRainContacts").Any(hit => hit.w > 0f &&
                (new Vector3(hit.x, hit.y, hit.z) - local).sqrMagnitude < .000001f), Is.True);
        }
    }
}
