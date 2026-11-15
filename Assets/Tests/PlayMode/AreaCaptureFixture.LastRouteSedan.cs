using System;
using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class LastRouteSedanCaptureSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.LastRouteCabinAssetSetup, BarPromenade.Editor", true)
                .GetMethod("BuildOrThrow", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }

    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Review the updated car in its production day and night light.")]
        [PrebuildSetup(typeof(LastRouteSedanCaptureSetup))]
        public IEnumerator LastRouteSedanAtBothStops()
        {
            try
            {
                GameSessionState.BeginNewGame("Last Route sedan capture");
                Assert.That(GameSessionState.TryStartGameTimeAt(14 * 60), Is.True);
                CityGameRoot city = null;
                string signature = null;
                yield return Capture(SceneIds.City,
                    () =>
                    {
                        city = Object.FindAnyObjectByType<CityGameRoot>();
                        return city != null && city.IsInitialized ? city : null;
                    },
                    () =>
                    {
                        signature = city.LastRouteCar.BuildSignature;
                        return LastRouteSedanShots(city.LastRouteCar, city.LastRouteFerryman, true);
                    });

                GameSessionState.BeginNewGame("Last Route sedan summit capture");
                Assert.That(GameSessionState.TryStartGameTimeAt(22 * 60), Is.True);
                MountainRoadRoot road = null;
                yield return Capture(SceneIds.MountainRoad,
                    () =>
                    {
                        road = Object.FindAnyObjectByType<MountainRoadRoot>();
                        return road != null && road.IsInitialized ? road : null;
                    },
                    () =>
                    {
                        Assert.That(road.LastRouteCar.BuildSignature, Is.EqualTo(signature));
                        return LastRouteSedanShots(road.LastRouteCar, road.LastRouteFerryman, false);
                    });
            }
            finally
            {
                GameSessionState.BeginNewGame("Last Route sedan capture cleanup");
            }
        }

        private static Shot[] LastRouteSedanShots(LastRouteCarAssetRegistry car,
            LastRouteFerrymanPresentation ferryman, bool city)
        {
            Assert.That(car, Is.Not.Null);
            Assert.That(car.IsBound, Is.True);
            Assert.That(ferryman.GetComponent<VillageResidentPresentation>(), Is.Not.Null);
            Assert.That(car.Bindings.Count(part => part.Role == "door_panel"), Is.EqualTo(2));
            var levels = car.GetComponent<LODGroup>().GetLODs();
            Assert.That(levels.Length, Is.EqualTo(2));
            Assert.That(levels[1].renderers.Length, Is.LessThan(levels[0].renderers.Length));
            Assert.That(levels[1].renderers, Does.Contain(car.Bindings.First(part => part.Role == "headlight").Renderer));
            LastRouteCarSeatPlan passenger = LastRouteCarSeatPlan.Create(car, car.transform.position.y);
            LastRouteFerrymanBoardingPlan driver = LastRouteFerrymanBoardingPlan.Create(car, car.transform.position.y);
            Assert.That(LastRouteCarDoors.MeasureSwingClearance(passenger.EntryRootPosition,
                car.PassengerDoorLeaf.position, LastRouteCarDoors.MeasureLeafReach(car.PassengerDoorLeaf)),
                Is.GreaterThan(LastRouteCarDoors.SwingBodyClearance));
            Assert.That(driver.DoorSwingClearance, Is.GreaterThan(LastRouteCarDoors.SwingBodyClearance));
            Transform placed = car.transform;
            Shot Front() => Shot.At("last-route-sedan-front",
                placed.TransformPoint(new Vector3(2.4f, 1.65f, 4.22f)),
                placed.TransformPoint(new Vector3(0f, .73f, .62f)), 56f);
            if (!city) return new[] { Front() };
            return new[]
            {
                Front(),
                Shot.At("last-route-sedan-rear-quarter", placed.TransformPoint(new Vector3(2.55f, 1.6f, -3.75f)),
                    placed.TransformPoint(new Vector3(0f, .84f, -.34f)), 55f),
                Shot.At("last-route-sedan-lower-apron", placed.TransformPoint(new Vector3(2f, .66f, 4.82f)),
                    placed.TransformPoint(new Vector3(0f, .49f, 2.15f)), 53f),
            };
        }
    }
}
