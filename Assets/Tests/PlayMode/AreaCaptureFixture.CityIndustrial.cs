using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Industrial functions: placed geometry, material receivers, task lights and day/night street views.")]
        [PrebuildSetup(typeof(CompactCityAssetsSetup))]
        public IEnumerator CityIndustrialFunctions()
        {
            GameSessionState.BeginNewGame();
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            CityGameRoot city = null;
            GameTimeRuntime clock = null;
            PlayerCameraFollow follow = null;
            bool clockEnabled = false, followEnabled = false;
            try
            {
                yield return Capture(SceneIds.City, () =>
                {
                    city = Object.FindAnyObjectByType<CityGameRoot>();
                    return city != null && city.IsInitialized ? city : null;
                }, () =>
                {
                    clock = Object.FindAnyObjectByType<GameTimeRuntime>();
                    clockEnabled = clock != null && clock.enabled;
                    if (clock != null) clock.enabled = false;
                    follow = city.Camera.GetComponent<PlayerCameraFollow>();
                    followEnabled = follow != null && follow.enabled;
                    if (follow != null) follow.enabled = false;
                    city.Player.Motor.SetInputEnabled(false);
                    city.DayNight.enabled = false;
                    city.Weather.enabled = false;
                    return IndustrialFunctionShots(city);
                });
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
                AssertCityLightingGlobalsCleared();
            }
            finally
            {
                if (clock != null) clock.enabled = clockEnabled;
                if (follow != null) follow.enabled = followEnabled;
                if (city != null)
                {
                    city.DayNight.enabled = true;
                    city.Weather.enabled = true;
                    city.Player.Motor.SetInputEnabled(true);
                }
                GameSessionState.BeginNewGame();
            }
        }

        private static Shot[] IndustrialFunctionShots(CityGameRoot city)
        {
            CityBuildingAssetProvider provider = CityBuildingAssetProvider.LoadOrThrow();
            Assert.That(provider.Entries.Count, Is.EqualTo(CityBuildingAssetProvider.ExpectedPrototypeCount));
            var lots = city.Layout.BuildingLots.Where(lot => lot.IsOrdinaryBuilding &&
                lot.District == CityDistrictKind.Industrial && lot.HasRoadFrontage)
                .GroupBy(lot => lot.BuildingVariant).OrderBy(group => group.Key)
                .Select(group => group.First()).ToArray();
            Assert.That(lots.Select(lot => lot.BuildingVariant), Is.EquivalentTo(Enumerable.Range(0, 8)),
                "The production streets must actually show every industrial function, including the small packaging shop.");
            Assert.That(city.Night.Atmosphere.IndustrialTaskLightCount, Is.GreaterThanOrEqualTo(lots.Length + 3),
                "The shared pool must discover authored building and coastal-service fixtures.");
            var poses = new List<Shot>();
            int baseMap = Shader.PropertyToID("_BaseMap");
            int responseMap = Shader.PropertyToID("_SceneMaterialResponse");
            foreach (BuildingLot lot in lots)
            {
                CityBuildingAssetRegistry registry = FindBuiltPrototype(lot);
                Bounds actual = registry.Parts[0].Renderer.bounds;
                foreach (CityBuildingPartBinding part in registry.Parts)
                {
                    actual.Encapsulate(part.Renderer.bounds);
                    if (part.Role == CityBuildingMeshRole.WindowGlass) continue;
                    if (part.Role == CityBuildingMeshRole.TaskLightLens)
                    {
                        Assert.That(part.Renderer.sharedMaterial, Is.SameAs(CityNightResources.EmissiveMaterial));
                        continue;
                    }
                    var block = new MaterialPropertyBlock();
                    part.Renderer.GetPropertyBlock(block);
                    Texture pigment = block.GetTexture(baseMap) ?? part.Renderer.sharedMaterial.GetTexture(baseMap);
                    Assert.That(pigment, Is.Not.Null.And.Not.SameAs(Texture2D.whiteTexture), part.SourceName);
                    Assert.That(part.Renderer.sharedMaterial.shader.name, Is.EqualTo(CitySurfaceLibrary.ShaderName), part.SourceName);
                    Assert.That(block.GetTexture(responseMap) ?? part.Renderer.sharedMaterial.GetTexture(responseMap),
                        Is.Not.Null, part.SourceName + " needs the existing physical response map.");
                    Assert.That(part.Renderer.sharedMaterial.shader.isSupported, Is.True, part.SourceName);
                }
                CityBuildingPrototypePose placement = CityBuildingPrototypePlacement.ResolveCityPose(lot, registry);
                Bounds expected = CityBuildingPrototypePlacement.TransformBounds(registry.LocalBounds, placement);
                Assert.That(Vector3.Distance(actual.center, expected.center), Is.LessThan(.035f), registry.StableId);
                Assert.That(Vector3.Distance(actual.size, expected.size), Is.LessThan(.06f), registry.StableId);
                Transform taskLight = registry.GetComponentsInChildren<Transform>(true)
                    .SingleOrDefault(item => item.name.StartsWith("IndustrialTaskLight_", System.StringComparison.Ordinal));
                Assert.That(taskLight, Is.Not.Null, registry.StableId + " needs its authored working light anchor.");

                Vector3 facade = registry.FrontAnchor.position;
                Vector3 right = Vector3.Cross(Vector3.up, lot.FacadeForward);
                poses.Add(Shot.At("industrial-" + registry.StableId,
                    facade + lot.FacadeForward * 10f + right * 3.5f + Vector3.up * EyeHeight,
                    facade + Vector3.up * (registry.Height * .4f), 76f));
            }
            var shots = new List<Shot>();
            foreach (Shot pose in poses) Add(pose, false);
            foreach (CityCoastalFacility facility in CityCoastalInfrastructurePlan.ForLayout(city.Layout).Facilities)
            {
                Vector3 forward = facility.Rotation * Vector3.forward;
                Vector3 right = facility.Rotation * Vector3.right;
                Add(Shot.At("industrial-service-" + facility.Id,
                    facility.Position + forward * 8f + right * 3f + Vector3.up * EyeHeight,
                    facility.Position + Vector3.up * 1.7f, 68f), false);
            }
            foreach (Shot pose in poses) Add(pose, true);
            BuildingLot packaging = lots.Single(lot => lot.BuildingVariant == 4);
            Transform packagingModel = FindBuiltPrototype(packaging).transform;
            float packagingFront = CityBuildingAssetProvider.GetExpectedEnvelope(CityDistrictKind.Industrial, 4).z * .5f;
            Add(Shot.At("industrial-near-equipment",
                packagingModel.TransformPoint(new Vector3(2.3f, EyeHeight, packagingFront + 3.5f)),
                packagingModel.TransformPoint(new Vector3(2.3f, 1.3f, packagingFront - .62f)), 65f), true);
            Transform repairModel = FindBuiltPrototype(lots.Single(lot => lot.BuildingVariant == 2)).transform;
            Add(Shot.At("industrial-repair-court", repairModel.TransformPoint(new Vector3(3.8f, EyeHeight, -4.8f)),
                repairModel.TransformPoint(new Vector3(2.2f, 1.3f, -1f)), 68f), true);
            Transform coldModel = FindBuiltPrototype(lots.Single(lot => lot.BuildingVariant == 1)).transform;
            Add(Shot.At("industrial-cold-roof", coldModel.TransformPoint(new Vector3(8f, 10.4f, 17f)),
                coldModel.TransformPoint(new Vector3(0f, 7.1f, -2f)), 65f), true);
            Shot close = poses[0];
            Add(Shot.At("industrial-intoxication", close.Position, close.Target, close.FieldOfView), true, true);
            return shots.ToArray();

            void Add(Shot pose, bool night, bool intoxicated = false)
            {
                int frames = 0;
                shots.Add(Shot.At((night ? "night-" : "day-") + pose.Name,
                    pose.Position, pose.Target, pose.FieldOfView, readyWhen: () =>
                    {
                        if (frames++ == 0)
                        {
                            if (night && GameSessionState.GameTimeOfDayMinutes < 21 * 60)
                            {
                                GameSessionState.AdvanceGameTime((float)((21 * 60 - GameSessionState.GameTimeOfDayMinutes) /
                                    GameTimeState.GameMinutesPerRealSecond));
                                city.DayNight.ApplyCurrentTime(true);
                            }
                            if (intoxicated) GameSessionState.UpdateDrinkingProgress(100, DrinkId.LightBeer, 1);
                            MoveCityLightingObserver(city, pose);
                        }
                        Assert.That(city.Night.Atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(12));
                        Assert.That(city.Lighting.HalationSourceCount, Is.LessThanOrEqualTo(24));
                        Assert.That(city.Lighting.VolumeSourceCount, Is.LessThanOrEqualTo(4));
                        Assert.That(city.Lighting.BounceRegionCount, Is.LessThanOrEqualTo(4));
                        return frames > (intoxicated ? 90 : SettleFrames);
                    }));
            }
        }
    }
}
