using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator Cobbler_OccludedHomeAllowsDepartureWhileVisibleHomeWaits()
        {
            var layout = CityLayoutCache.GetOrGenerate(CityBlueprintCatalog.Default,
                CityGenerationSettings.Default, GameSessionState.DefaultCitySeed);
            var site = CityCobblerSitePlan.ForLayout(layout);
            var workshop = CityCobblerWorld.Build(null, site, true, out _);
            var worker = workshop.GetComponent<CityCobblerWorker>();
            worker.AutoAdvance = false;
            var cameraObject = new GameObject("Cobbler departure observer");
            var wall = new GameObject("Other building wall");
            try
            {
                var camera = cameraObject.AddComponent<Camera>();
                Vector3 target = worker.CommutePlan.InsideHome + Vector3.up * .84f;
                camera.transform.SetPositionAndRotation(target + Vector3.forward * 40f,
                    Quaternion.LookRotation(Vector3.back));
                worker.PresentationObserver = camera.transform;
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(CityCobblerWorker).GetField("observer", flags).SetValue(worker, camera);
                MethodInfo sample = typeof(CityCobblerWorker).GetMethod("Sample", flags);
                double departure = CityCobblerWorker.ShiftStartMinute / GameTimeState.GameMinutesPerRealSecond -
                    worker.CommuteSeconds;

                var blocker = wall.AddComponent<BoxCollider>();
                wall.transform.position = target + Vector3.forward * 20f;
                blocker.size = new Vector3(4f, 4f, .3f);
                Assert.That(worker.CommutePlan.House.FoundationBounds.Contains(
                    new Vector2(wall.transform.position.x, wall.transform.position.z)), Is.False,
                    "A neighbouring building must be able to hide the doorway.");
                worker.ApplyAt(departure - 1d);
                Physics.SyncTransforms();
                Assert.That((bool)sample.Invoke(worker, new object[] { departure + 1d, true, false }), Is.True);
                Assert.That(worker.IsCommuting, Is.True);
                Assert.That(Vector3.Distance(worker.Worker.transform.position, worker.CommutePlan.InsideHome),
                    Is.GreaterThan(.5f), "An occluded doorstep must let the resident start his walk.");

                blocker.enabled = false;
                worker.ApplyAt(departure - 1d);
                Physics.SyncTransforms();
                Vector3 before = worker.Worker.transform.position;
                Assert.That((bool)sample.Invoke(worker, new object[] { departure + 1d, true, false }), Is.False,
                    "An unoccluded, directly watched doorstep still waits before showing the retired actor.");
                Assert.That(worker.Worker.transform.position, Is.EqualTo(before));

                camera.transform.rotation = Quaternion.identity;
                Assert.That((bool)sample.Invoke(worker, new object[] { departure + 1d, true, false }), Is.True,
                    "Turning away must release the same waiting departure.");
            }
            finally
            {
                Object.Destroy(workshop.gameObject);
                Object.Destroy(cameraObject);
                Object.Destroy(wall);
            }
            yield return null;
        }
    }
}
