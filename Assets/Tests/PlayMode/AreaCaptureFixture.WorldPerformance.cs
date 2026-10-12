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
        [Explicit("Focused City and Mountain Road frames for shared litter, Windows surfaces, hair detail and forest chunks.")]
        public IEnumerator WorldPerformance()
        {
            GameSessionState.BeginNewGame();
            GameSessionState.TryStartGameTimeFromWake();
            GameSessionState.AdvanceGameTime((float)((12d * 60d - GameSessionState.GameTimeOfDayMinutes) /
                GameTimeState.GameMinutesPerRealSecond));
            CityGameRoot city = null;
            yield return Capture(SceneIds.City, () =>
            {
                city = Object.FindAnyObjectByType<CityGameRoot>();
                return city != null && city.IsInitialized ? city : null;
            }, () =>
            {
                AssertWorldPerformanceLitter();
                AssertWorldPerformancePortTextures();
                CityLitterPart? litter = First(LitterPlan(city), CityLitterZone.Sidewalk,
                    CityDistrictKind.Residential);
                Assert.That(litter.HasValue, Is.True);
                CityPortController port = city.World.Root.GetComponentInChildren<CityPortController>();
                Assert.That(port, Is.Not.Null);
                city.Cannery.AutoAdvance = false;
                city.Cannery.ForcePresentation = true;
                port.AutoAdvance = false;
                port.ForcePresentation = true;
                city.Player.Motor.SetInputEnabled(false);
                return new[]
                {
                    Shot.At("Test litter close", Eye(city, litter.Value, 2.1f, 1.3f),
                        litter.Value.Position + Vector3.up * .08f, 44f),
                    Shot.At("Test port concrete",
                        port.Plan.World(new Vector3(-10f, CityPortPlan.DeckHeight + 1.4f, -7.5f)),
                        port.Plan.World(new Vector3(-10f, CityPortPlan.DeckHeight, -5.8f)), 48f),
                    Shot.At("Test warehouse surfaces",
                        port.Plan.World(new Vector3(-9.2f, CityPortPlan.DeckHeight + 1.7f, -10f)),
                        port.Plan.World(new Vector3(-3f, CityPortPlan.DeckHeight + 1.3f, -12f)), 54f)
                };
            });
            yield return CaptureWorldPerformanceHair(city);

            MountainRoadRoot mountain = null;
            yield return Capture(SceneIds.MountainRoad, () =>
            {
                mountain = Object.FindAnyObjectByType<MountainRoadRoot>();
                return mountain != null && mountain.IsInitialized ? mountain : null;
            }, () =>
            {
                AssertWorldPerformanceForest(mountain);
                Shot[] existing = MountainRoadShots(mountain);
                return new[]
                {
                    Shot.At("Test mountain lower forest", existing[1].Position,
                        existing[1].Target, existing[1].FieldOfView),
                    Shot.At("Test mountain upper forest", existing[5].Position,
                        existing[5].Target, existing[5].FieldOfView)
                };
            });
        }

        private static void AssertWorldPerformanceLitter()
        {
            GameObject root = GameObject.Find(CityLitterWorldBuilder.RootName);
            Assert.That(root, Is.Not.Null);
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(13));
            var roles = new Dictionary<string, Material>(StringComparer.Ordinal);
            foreach (Renderer renderer in renderers)
            {
                Assert.That(renderer.HasPropertyBlock(), Is.False, renderer.name);
                Material material = renderer.sharedMaterial;
                Assert.That(material, Is.Not.Null, renderer.name);
                Assert.That(material.name, Does.StartWith("Litter "), renderer.name);
                if (roles.TryGetValue(material.name, out Material shared))
                    Assert.That(material, Is.SameAs(shared), renderer.name);
                else roles.Add(material.name, material);
            }
            Assert.That(roles.Count, Is.InRange(1, 13));
        }

        private static void AssertWorldPerformancePortTextures()
        {
            foreach (string role in new[] { "Concrete", "Timber", "Plaster" })
            {
                Texture2D texture = CityPortAssetProvider.GetSurfaceMaterial(role)
                    .GetTexture("_BaseMap") as Texture2D;
                Assert.That(texture, Is.Not.Null, role);
                Assert.That(texture.width, Is.EqualTo(512), role);
                Assert.That(texture.height, Is.EqualTo(512), role);
                Assert.That(texture.mipmapCount, Is.EqualTo(10), role);
#if UNITY_EDITOR
                Assert.That(texture.format, Is.EqualTo(TextureFormat.BC7), role);
#endif
            }
        }

        private static IEnumerator CaptureWorldPerformanceHair(CityGameRoot city)
        {
            CityCanneryController cannery = city.Cannery;
            CanneryWomanPresentation woman = cannery.Woman;
            Assert.That(woman, Is.Not.Null);
            Assert.That(woman.Hair, Is.Not.Null);
            Camera camera = Camera.main;
            PlayerCameraFollow follow = camera.GetComponent<PlayerCameraFollow>();
            bool savedFollow = follow != null && follow.enabled;
            bool savedForce = cannery.ForcePresentation;
            Vector3 savedHero = city.Player.GameObject.transform.position;
            Pose savedCamera = new Pose(camera.transform.position, camera.transform.rotation);
            float savedField = camera.fieldOfView;
            var hidden = new List<Renderer>();
            foreach (Renderer renderer in city.Player.GameObject.GetComponentsInChildren<Renderer>(true))
                if (renderer.enabled) { hidden.Add(renderer); renderer.enabled = false; }
            try
            {
                if (follow != null) follow.enabled = false;
                cannery.ForcePresentation = false;
                city.Player.Motor.Teleport(woman.Motion.transform.position + woman.Motion.transform.forward * 4f);
                cannery.ApplyAt(0d);
                cannery.ApplyLifeAt(10d);
                Physics.SyncTransforms();
                Vector3 direction = WorldPerformanceHairDirection(cannery);
                double life = cannery.LifeSeconds;
                yield return Frame("Test hair near", 4f, 8);
                int resets = woman.Hair.ResetCount;
                yield return Frame("Test hair distant", 32f, 2);
                Assert.That(woman.Hair.ResetCount, Is.EqualTo(resets));
                yield return Frame("Test hair near again", 4f, 8);
                Assert.That(woman.Hair.ResetCount, Is.EqualTo(resets));

                IEnumerator Frame(string name, float distance, int iterations)
                {
                    Vector3 target = woman.Motion.Head.position - Vector3.up * .35f;
                    Vector3 eye = target + direction * distance + Vector3.up * .35f;
                    city.Player.Motor.Teleport(eye - Vector3.up * EyeHeight);
                    camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                    camera.fieldOfView = 60f;
                    Physics.SyncTransforms();
                    for (int frame = 0; frame < 30; frame++)
                    {
                        cannery.ApplyLifeAt(life += 1d / 60d);
                        yield return null;
                    }
                    Assert.That(woman.gameObject.activeInHierarchy, Is.True);
                    Assert.That(woman.Hair.SurfaceContactIterationLimit, Is.EqualTo(iterations), name);
                    Assert.That(WorldPerformanceHairLineIsClear(cannery,
                        woman.Motion.Head.position - Vector3.up * .35f, eye, out string blocker),
                        Is.True, name + " is obscured by " + blocker);
                    Assert.That(Vector3.Dot(direction, woman.Motion.transform.forward), Is.LessThan(-.4f),
                        "The close view must show the long hair from behind at three quarters.");
                    CaptureCurrentCamera(camera, SceneIds.City, name);
                }
            }
            finally
            {
                foreach (Renderer renderer in hidden) if (renderer != null) renderer.enabled = true;
                cannery.ForcePresentation = savedForce;
                city.Player.Motor.Teleport(savedHero);
                camera.transform.SetPositionAndRotation(savedCamera.position, savedCamera.rotation);
                camera.fieldOfView = savedField;
                if (follow != null) follow.enabled = savedFollow;
            }
        }

        private static Vector3 WorldPerformanceHairDirection(CityCanneryController cannery)
        {
            VillageResidentPresentation actor = cannery.Woman.Motion;
            Vector3 target = actor.Head.position - Vector3.up * .35f;
            // Prefer either rear three-quarter view: a clear physics ray can
            // still pass through a coworker's visible body without a collider.
            foreach (float angle in new[] { 145f, 215f, 135f, 225f, 155f, 205f, 125f, 235f,
                165f, 195f, 115f, 245f })
            {
                Vector3 direction = Quaternion.Euler(0f, angle, 0f) * actor.transform.forward;
                Vector3 eye = target + direction * 32f + Vector3.up * .35f;
                if (WorldPerformanceHairLineIsClear(cannery, target, eye, out _)) return direction;
            }
            Assert.Fail("No unobstructed rear three-quarter camera line for the worker frames.");
            return Vector3.back;
        }

        private static bool WorldPerformanceHairLineIsClear(CityCanneryController cannery,
            Vector3 target, Vector3 eye, out string blocker)
        {
            blocker = null;
            Vector3 offset = eye - target;
            float distance = offset.magnitude;
            var ray = new Ray(target, offset.normalized);
            Transform actor = cannery.Woman.Motion.transform;
            foreach (RaycastHit hit in Physics.RaycastAll(ray, distance, ~0, QueryTriggerInteraction.Ignore))
                if (!hit.transform.IsChildOf(actor) && hit.transform.GetComponentInParent<PlayerMotor>() == null)
                { blocker = hit.transform.name; return false; }
            for (int role = 0; role < 4; role++)
            {
                if (role == CanneryWomanPresentation.WorkerSlot) continue;
                VillageResidentPresentation worker = cannery.GetFactoryWorker(role);
                if (worker == null) continue;
                foreach (Renderer renderer in worker.GetComponentsInChildren<Renderer>(true))
                {
                    if (!renderer.enabled || renderer.forceRenderingOff || !renderer.gameObject.activeInHierarchy) continue;
                    Bounds bounds = renderer.bounds;
                    // Reserve the hair's width and length around the centre
                    // ray, rather than allowing a neighbouring shoulder to
                    // cover the locks while missing one narrow physics ray.
                    bounds.Expand(new Vector3(.5f, .7f, .5f));
                    if (!bounds.IntersectRay(ray, out float hitDistance) || hitDistance >= distance) continue;
                    blocker = worker.name + "/" + renderer.name;
                    return false;
                }
            }
            return true;
        }

        private static void AssertWorldPerformanceForest(MountainRoadRoot mountain)
        {
            Transform forest = mountain.World.PhysicalRoot.transform.Find("Batched Melancholic Forest");
            Assert.That(forest, Is.Not.Null);
            int crowns = 0;
            foreach (Renderer renderer in forest.GetComponentsInChildren<Renderer>(true))
                if (renderer.name.EndsWith("Conifer Crowns", StringComparison.Ordinal))
                {
                    crowns++;
                    Assert.That(renderer.transform.parent.name, Does.StartWith("Forest "));
                    Assert.That(renderer.enabled && renderer.receiveShadows, Is.True);
                    Assert.That(renderer.shadowCastingMode,
                        Is.EqualTo(UnityEngine.Rendering.ShadowCastingMode.On));
                }
            Assert.That(crowns, Is.GreaterThan(3), "Forest rendering is divided into spatial chunks.");
            MeshCollider[] collisions = forest.GetComponentsInChildren<MeshCollider>(true);
            Assert.That(collisions, Has.Length.EqualTo(1));
            Assert.That(collisions[0].name, Is.EqualTo("Physical Conifer Trunk Collision"));
            Assert.That(collisions[0].enabled && !collisions[0].isTrigger, Is.True);
            int physicalTrees = 0;
            foreach (MountainRoadForestDescriptor tree in mountain.Plan.Forest)
                if (tree.Layer == MountainRoadForestLayer.Physical) physicalTrees++;
            Assert.That(collisions[0].sharedMesh.GetIndexCount(0), Is.EqualTo((uint)(physicalTrees * 36)));
        }
    }
}
