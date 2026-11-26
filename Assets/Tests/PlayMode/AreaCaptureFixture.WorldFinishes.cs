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
        [UnityTest, Explicit("Compare contact finishes from close gameplay views under the existing lights.")]
        public IEnumerator WorldContactFinishes()
        {
            GameSessionState.BeginNewGame("contact-finishes");
            try
            {
                GameSessionState.TryStartGameTimeAt(720);
                yield return SceneManager.LoadSceneAsync(SceneIds.StairwellInterior);
                StairwellInteriorRoot stair = null;
                yield return WaitFor(() => (stair = Object.FindAnyObjectByType<StairwellInteriorRoot>()) && stair.IsInitialized, "Stairwell");
                var rail = stair.World.Root.GetComponentsInChildren<MeshRenderer>().First(r => r.name == "Lower Handrail A");
                Assert.That(rail.sharedMaterial.IsKeywordEnabled("_WORLD_CONTACT_FINISH"), Is.True);
                var properties = new MaterialPropertyBlock();
                rail.GetPropertyBlock(properties);
                Assert.That(properties.GetTexture("_ContactFinishMap").name, Is.EqualTo("RailWood"));
                var supports = stair.World.Root.GetComponentsInChildren<MeshRenderer>().First(r => r.name == rail.name + " Supports");
                supports.GetPropertyBlock(properties);
                Assert.That(properties.GetTexture("_ContactFinishMap").name, Is.EqualTo("RailSupports"));
                Vector3 target = rail.bounds.center - Vector3.up * .09f;
                foreach (int step in new[] { -1, 0, 1 })
                    FinishView("stair-" + step, target + rail.transform.forward * (.68f + step * .27f) +
                        (stair.World.Root.position.x > target.x ? Vector3.right : Vector3.left) * .68f + Vector3.up * .33f, target, 48f);

                GameSessionState.EnterBar("contact-finishes", BarActivityKind.SplitTheG, CityDistrictKind.Residential);
                yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior);
                BarInteriorRoot bar = null;
                yield return WaitFor(() => (bar = Object.FindAnyObjectByType<BarInteriorRoot>()) && bar.IsInitialized, "Bar");
                bar.ArrivalPresentation?.Skip();
                FinishView("counter", bar.Room.TransformPoint(new Vector3(-1.4f,1.43f,.15f)),
                    bar.Room.TransformPoint(new Vector3(-.72f,.84f,1.58f)), 52f);

                yield return SceneManager.LoadSceneAsync(SceneIds.City);
                CityGameRoot city = null;
                yield return WaitFor(() => (city = Object.FindAnyObjectByType<CityGameRoot>()) && city.IsInitialized, "City");
                var model = Object.Instantiate(Resources.Load<GameObject>(CityBusResources.PrefabResourcePath),
                    city.Player.GameObject.transform.position + Vector3.forward * 4f, Quaternion.identity);
                var bus = model.GetComponent<CityBusPresentation>() ?? model.AddComponent<CityBusPresentation>();
                bus.Initialize(model.GetComponent<CityBusAssetRegistry>());
                try
                {
                    bus.SetDoors(1f);
                    foreach (var part in bus.Registry.RendererBindings.Where(part => part.MaterialSlot == CityBusMaterialSlot.Rail ||
                        part.MaterialSlot == CityBusMaterialSlot.Floor))
                    {
                        var material = part.Renderer.sharedMaterial;
                        Assert.That(material.IsKeywordEnabled("_WORLD_CONTACT_FINISH"), Is.True);
                        Assert.That(material.GetTexture("_ContactFinishMap").name,
                            Is.EqualTo(part.MaterialSlot == CityBusMaterialSlot.Rail ? "BusGrip" : "BusStep"));
                    }
                    var entry = bus.Registry.FrontDoorEntryAnchor.position;
                    Vector3 outward = Vector3.ProjectOnPlane(entry - bus.Registry.ModelRoot.position, Vector3.up);
                    outward = Vector3.Project(outward, bus.transform.right).normalized;
                    var along = bus.transform.forward;
                    foreach (int step in new[] { -1, 0, 1 })
                        FinishView("bus-" + step, entry + outward * 1.28f + along * (.32f * step) + Vector3.up * 1.22f,
                            entry - outward * .24f + Vector3.up * .74f, 58f);
                    foreach (var door in new[] { bus.Registry.FrontDoorEntryAnchor, bus.Registry.RearDoorEntryAnchor })
                    {
                        Vector3 threshold = door.position + outward * .40f - Vector3.up * .17f;
                        FinishView(door.name + "-step", threshold + outward * 1.03f + along * .17f + Vector3.up * .72f,
                            threshold, 48f);
                    }
                }
                finally { Object.DestroyImmediate(model); }
            }
            finally { GameSessionState.BeginNewGame(); }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
        }

        private static void FinishView(string shot, Vector3 eye, Vector3 target, float fov)
        {
            Camera camera = Camera.main;
            var saved = new DefaultNpcCameraState(camera);
            try
            {
                camera.transform.SetPositionAndRotation(eye, Quaternion.LookRotation(target - eye));
                camera.fieldOfView = fov;
                CaptureCurrentCamera(camera, "WorldContactFinishes", shot);
            }
            finally { saved.Restore(camera); }
        }
    }
}
