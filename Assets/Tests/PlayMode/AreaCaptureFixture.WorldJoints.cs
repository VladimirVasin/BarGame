using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Construction profiles and ground transitions in the shipped locations outside City.")]
        public IEnumerator WorldJointSceneMigration()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)(360f / GameTimeState.GameMinutesPerRealSecond));
            try
            {
                yield return CaptureJointScene<MountainRoadRoot>(SceneIds.MountainRoad, root => root.IsInitialized, root =>
                {
                    Renderer road = FindJointRenderer(root.World.PhysicalRoot.transform, "Continuous Narrow Road");
                    AssertRoadShoulder(road);
                    MountainRoadRoutePlan route = root.Plan.Route;
                    MountainRoadRouteSample sample = route.Sample(route.Hairpins[0].StartDistance - 6f);
                    Vector3 edge = sample.Position + sample.Right * (sample.Width * .5f);
                    return JointPair("mountain-road", edge + sample.Forward * -2f + Vector3.up * 1.35f,
                        edge + sample.Forward * 2f);
                });
                AssertJointPixels(SceneIds.MountainRoad, "mountain-road");
                yield return CaptureJointScene<AlpineVillageRoot>(SceneIds.AlpineVillage, root => root.IsInitialized, root =>
                {
                    Renderer terrain = root.World.TerrainRoot.GetComponent<Renderer>();
                    AssertRoadShoulder(terrain, AlpineVillageWorldBuilder.TerrainAsphaltMaterialIndex);
                    Renderer pathSurface = root.World.Root.GetComponentsInChildren<MeshRenderer>().First(renderer =>
                        renderer.name.StartsWith("Visible Path - ", StringComparison.Ordinal));
                    var pathProperties = new MaterialPropertyBlock();
                    pathSurface.GetPropertyBlock(pathProperties);
                    Assert.That(pathProperties.GetVector(GroundSurfaceAppearance.ShoulderMixId).w, Is.EqualTo(1f));
                    AlpineVillagePathDescriptor road = root.Plan.Expansion.Paths.Where(path => path.Kind == AlpineVillagePathKind.AbandonedRoad)
                        .OrderByDescending(path => (path.End - path.Start).sqrMagnitude).First();
                    Vector3 axis = (road.End - road.Start).normalized;
                    Vector3 edge = (road.Start + road.End) * .5f +
                                   Vector3.Cross(Vector3.up, axis).normalized * road.SurfaceHalfWidth;
                    edge.y = AlpineVillageTerrainSampler.SampleMeshHeight(root.Plan, new Vector2(edge.x, edge.z));
                    var house = root.Plan.MothersHouse;
                    Vector3 doorstep = house.GroundCenter + house.Facing * (house.FootprintSize.y * .5f);
                    return JointPair("village-road", edge - axis * 2f + Vector3.up * 1.35f, edge + axis * 2f)
                        .Concat(JointPair("village-threshold", doorstep + house.Facing * 2.8f + Vector3.up * 1.25f,
                            doorstep + Vector3.up * .2f)).ToArray();
                });
                AssertJointPixels(SceneIds.AlpineVillage, "village-road");
                AssertJointPixels(SceneIds.AlpineVillage, "village-threshold");
                yield return CaptureJointScene<HomeInteriorRoot>(SceneIds.HomeInterior, root => root.IsInitialized, root =>
                {
                    Renderer threshold = FindJointRenderer(root.Room, "Home Balcony Threshold");
                    return JointPartShot("home-threshold", threshold, new Vector3(-1.75f, .92f, .2f));
                });
                yield return CaptureJointScene<StairwellInteriorRoot>(SceneIds.StairwellInterior, root => root.IsInitialized, root =>
                {
                    FindJointRenderer(root.World.Root, "Lower Stair 01");
                    return NamedJointShots("stairwell", StairwellAuthoredShots(root).Take(2));
                });
                yield return CaptureJointScene<DoorTransitionRoot>(SceneIds.DoorTransition, _ => true, root =>
                {
                    root.Initialize(DoorTransitionDirection.EnterApartment);
                    Vector3 threshold = FindJointRenderer(root.transform, "Door Threshold").bounds.center;
                    root.StartCoroutine(root.Play());
                    return new[] { Shot.At("world-joints-transition-threshold", threshold + new Vector3(.2f, .75f, -2.2f),
                        threshold, 62f, readyWhen: () => root.CurrentPose.DoorOpen > .45f) };
                });
                GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.ApartmentDoor);
                GameSessionState.CommitHomeSleepAppearance();
                yield return CaptureJointScene<StairwellInteriorRoot>(SceneIds.StairwellInterior, root => root.IsInitialized, root =>
                {
                    Assert.That(root.Layout.IsCorrupted, Is.True);
                    return NamedJointShots("labyrinth", CorruptedStairwellShots(root).Take(2));
                });

                GameSessionState.BeginNewGame();
                BarTestTime.StartFullAttendance("joint-capture");
                GameSessionState.EnterBar("joint-capture", BarActivityKind.SplitTheG, CityDistrictKind.Residential);
                yield return CaptureJointScene<BarInteriorRoot>(SceneIds.BarInterior, root => root.IsInitialized, root =>
                {
                    Renderer frame = FindJointRenderer(root.Room, "Entrance Door Frame");
                    return JointPartShot("bar-door", frame, new Vector3(.55f, .05f, 2.6f));
                });
                GameSessionState.BeginNewGame();
                yield return CaptureJointScene<SupermarketInteriorRoot>(SceneIds.SupermarketInterior, root => root.IsInitialized, root =>
                {
                    Renderer threshold = FindJointRenderer(root.Room, "Entrance Threshold");
                    return JointPartShot("supermarket-threshold", threshold, new Vector3(.3f, 1.3f, 2.25f));
                });
                yield return CaptureJointScene<ChurchInteriorRoot>(SceneIds.ChurchInterior, root => root.IsInitialized, root =>
                {
                    Renderer rail = FindJointRenderer(root.World.Root, "INT_CommunionRail");
                    return JointPartShot("church-rail", rail, new Vector3(.5f, 1.05f, -2.7f));
                });
                yield return CaptureJointScene<MothersHouseInteriorRoot>(SceneIds.MothersHouseInterior, root => root.IsInitialized, root =>
                {
                    FindJointRenderer(root.Room, "FIX_Skirting");
                    return new[] { MothersHouseShot(root, HomeCameraShotKind.MainRoom, "world-joints-mothers-house") };
                });
                yield return CaptureJointScene<PawnshopInteriorRoot>(SceneIds.PawnshopInterior, root => root.IsInitialized, root =>
                {
                    Renderer counter = FindJointRenderer(root.Model, "PART_CounterTop");
                    return JointPartShot("pawnshop-counter", counter, new Vector3(.45f, .60f, -2.25f));
                });

                CombatTestRoot.UseLegacyArenaForTests = false;
                CombatTestRoot.WarehouseSeedForTests = 0;
                yield return CaptureJointScene<CombatTestRoot>(SceneIds.CombatTest, root => root.IsInitialized, root =>
                {
                    root.AutomaticSimulation = false;
                    WarehouseArenaStair stair = root.WarehousePlan.Stairs.Single(part => part.IsBroken);
                    Assert.That(root.GetComponentsInChildren<Transform>().Any(part => part.name == "Warehouse Broken Stair"), Is.True);
                    return new[] { Shot.At("world-joints-warehouse-stair", stair.Foot + new Vector3(-.9f, 1.6f, -1.8f),
                        stair.Foot + new Vector3(0f, .65f, 1.3f), 63f) };
                });
            }
            finally
            {
                CombatTestRoot.WarehouseSeedForTests = null;
                Shader.SetGlobalFloat("_CityJointPreviewOff", 0f);
                GameSessionState.BeginNewGame();
            }
        }

        private static IEnumerator CaptureJointScene<T>(string scene, Func<T, bool> ready, Func<T, Shot[]> shots)
            where T : Component
        {
            T root = null;
            return Capture(scene, () =>
            {
                root = Object.FindAnyObjectByType<T>();
                return root != null && ready(root) ? root : null;
            }, () => shots(root));
        }

        private static Renderer FindJointRenderer(Transform parent, string name)
        {
            Renderer match = parent.GetComponentsInChildren<MeshRenderer>(true).FirstOrDefault(renderer =>
                renderer.name == name && renderer.enabled && renderer.gameObject.activeInHierarchy);
            Assert.That(match, Is.Not.Null, "The live model must contain " + name);
            Assert.That(match.GetComponent<MeshFilter>().sharedMesh.vertexCount, Is.GreaterThan(24));
            return match;
        }

        private static void AssertRoadShoulder(Renderer renderer, int material = -1)
        {
            var properties = new MaterialPropertyBlock();
            GroundSurfaceAppearance.ReadProperties(renderer, properties, material);
            Assert.That(properties.GetVector(GroundSurfaceAppearance.ShoulderMixId).y, Is.GreaterThan(.3f));
            Assert.That(properties.GetTexture("_GroundSubstrateMap"), Is.Not.Null);
            Assert.That(properties.GetTexture("_GroundSubstrateResponse"), Is.Not.Null);
        }

        private static Shot[] JointPair(string name, Vector3 eye, Vector3 target)
        {
            return new[] { JointShot(true), JointShot(false) };

            Shot JointShot(bool bypass) => Shot.At("world-joints-" + name + (bypass ? "-baseline" : "-enabled"),
                eye, target, 64f, readyWhen: () =>
                {
                    Shader.SetGlobalFloat("_CityJointPreviewOff", bypass ? 1f : 0f);
                    return true;
                });
        }

        private static Shot[] JointPartShot(string name, Renderer renderer, Vector3 eyeOffset)
        {
            Vector3 target = renderer.bounds.center;
            return new[] { Shot.At("world-joints-" + name, target + eyeOffset, target, 62f) };
        }

        private static void AssertJointPixels(string scene, string name)
        {
            string stem = "world-joints-" + name;
            Assert.That(PolishDifference(scene, stem + "-baseline", stem + "-enabled"), Is.GreaterThan(24));
        }

        private static Shot[] NamedJointShots(string name, IEnumerable<Shot> shots) =>
            shots.Select((shot, index) => Shot.At("world-joints-" + name + "-" + index, shot.Position,
                shot.Target, shot.FieldOfView, delayFrames: shot.DelayFrames)).ToArray();
    }
}
