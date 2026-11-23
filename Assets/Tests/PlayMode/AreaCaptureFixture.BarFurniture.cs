using System;
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
        [UnityTest, Explicit("The rebuilt pub at the counter, behind the taps and in the booths.")]
        [PrebuildSetup(typeof(CompactBarAssetsSetup))]
        public IEnumerator BarFurnitureFinishes()
        {
            float previousStep = Time.captureDeltaTime;
            GameSessionState.BeginNewGame("pub-furniture");
            BarTestTime.StartFullAttendance("pub-furniture");
            GameSessionState.EnterBar("pub-furniture", BarActivityKind.SplitTheG, CityDistrictKind.Residential);
            try
            {
                Time.captureDeltaTime = 1f / 30f;
                yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior, LoadSceneMode.Single);
                var bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                IEnumerator WaitFor(Func<bool> condition, string description)
                {
                    for (int frame = 0; frame < 1600 && !condition(); frame++) yield return null;
                    Assert.That(condition(), Is.True, description);
                }
                yield return WaitFor(() => bar != null && bar.IsInitialized, "arrival");
                bar.ArrivalPresentation.Skip();
                Camera camera = Camera.main;
                void Photo(string name, Vector3 eye, Vector3 target, float fov)
                {
                    var original = new Pose(camera.transform.position, camera.transform.rotation);
                    float lens = camera.fieldOfView;
                    try
                    {
                        camera.transform.position = bar.Room.TransformPoint(eye);
                        camera.transform.LookAt(bar.Room.TransformPoint(target), bar.Room.up);
                        camera.fieldOfView = fov;
                        CaptureCurrentCamera(camera, SceneIds.BarInterior, "fittings-" + name);
                    }
                    finally
                    {
                        camera.transform.SetPositionAndRotation(original.position, original.rotation);
                        camera.fieldOfView = lens;
                    }
                }
                var colour = Resources.Load<Texture2D>(BarInteriorSurfaceAppearance.AlbedoResourcePath);
                var response = Resources.Load<Texture2D>(BarInteriorSurfaceAppearance.ResponseResourcePath);
                var properties = new MaterialPropertyBlock();
                foreach (string name in new[] { "Counter Top", "Counter Return Top", "Counter Foot Rail",
                    "Bar Drink Service Stool", "Tap Slotted Grate", "Beer Tap Spout 2", "Backbar Shelf Brackets",
                    "Booth Back 1 Near", "Booth Cushion 2 Far", "Social Pub Table 1" })
                {
                    var part = bar.Room.Find(name);
                    Assert.That(part, Is.Not.Null, name);
                    part.GetComponent<Renderer>().GetPropertyBlock(properties);
                    Assert.That(properties.GetTexture("_BaseMap"), Is.SameAs(colour), name);
                    Assert.That(properties.GetTexture("_SceneMaterialResponse"), Is.SameAs(response), name);
                    Assert.That(part.GetComponent<MeshFilter>().sharedMesh.uv2, Is.Not.Empty, name);
                }
                var bottleColour = Resources.Load<Texture2D>(BarInteriorSurfaceAppearance.BottleAlbedoPath);
                var bottleResponse = Resources.Load<Texture2D>(BarInteriorSurfaceAppearance.BottleResponsePath);
                Assert.That(bottleColour != null && bottleResponse != null, Is.True);
                foreach (string finish in new[] { "BeerLongneck", "WineBottle", "VodkaBottle", "CognacBottle", "Caps", "Labels" })
                {
                    var stock = bar.Room.Find("Shelf Bottle " + finish);
                    Assert.That(stock, Is.Not.Null, finish);
                    stock.GetComponent<Renderer>().GetPropertyBlock(properties);
                    Assert.That(properties.GetTexture("_BaseMap"), Is.SameAs(bottleColour), finish);
                    Assert.That(properties.GetTexture("_SceneMaterialResponse"), Is.SameAs(bottleResponse), finish);
                }
                Photo("bottles", new Vector3(1.71f,2.16f,2.69f), new Vector3(2.01f,2.26f,3.42f), 54f);
                Photo("counter", new Vector3(-2.82f,1.52f,-.58f), new Vector3(-.52f,.86f,1.82f), 66f);
                Photo("seat", new Vector3(-1.30f,1.04f,.12f), new Vector3(-.70f,.61f,1.20f), 66f);
                Photo("taps", new Vector3(2.52f,1.82f,2.89f), new Vector3(3.57f,1.42f,2.02f), 60f);
                Photo("shelves", new Vector3(-.55f,1.78f,1.79f), new Vector3(.76f,1.93f,3.55f), 75f);
                Photo("booth", new Vector3(-2.73f,1.39f,-1.81f), new Vector3(-4.51f,.78f,-2.46f), 66f);
                foreach (var boothZ in new[] { -2.15f, .65f })
                    Photo("stage-" + (boothZ < 0f ? "near" : "far"), new Vector3(-4.02f,1.22f,boothZ),
                        new Vector3(4.1f,1.13f,-2.7f), 68f);
                var table = bar.Room.Find("Social Pub Table 1").GetComponent<Renderer>().bounds.center;
                var tableLocal = bar.Room.InverseTransformPoint(table);
                Photo("table", tableLocal + new Vector3(-.67f,.37f,-.81f), tableLocal + Vector3.down*.12f, 59f);

                foreach (BarBartenderClipKind kind in Enum.GetValues(typeof(BarBartenderClipKind)))
                {
                    Assert.That(bar.Bartender.Registry.TryGetClip(kind, out var clip, out _), Is.True, kind.ToString());
                    Assert.That(clip.length, Is.GreaterThan(0f), kind.ToString());
                }
                Assert.That(bar.Patrons.Count, Is.EqualTo(BarBusinessHours.MaximumGuests));
                yield return VerifyAllBarDrinkCycles(bar);
                var poseProbe = bar.gameObject.AddComponent<WarehouseRefrigeratorPresentationProbe>();
                yield return AtWarehouseFridgePresentation(poseProbe, AssertBarPatronPlacement);
                Object.Destroy(poseProbe);
                var station = bar.CounterStation;
                var shop = bar.DrinkShop;
                var plan = station.Seat.Plan;
                bar.Player.Motor.Teleport(plan.EntryPose.RootPosition);
                bar.Player.GameObject.transform.rotation = plan.EntryPose.RootRotation;
                Physics.SyncTransforms();
                station.Interact(bar.Player.Interactor);
                yield return WaitFor(() => station.Seat.IsSeated && shop.IsBrowsing, "sit and open the menu");
                int offer = Array.FindIndex(shop.Offers.ToArray(), item => item.DrinkId == DrinkId.Vodka);
                Assert.That(shop.Select(offer) && shop.ConfirmSelection(), Is.True);
                yield return WaitFor(() => shop.CanDrinkServedVessel, "served shot");
                var vessel = shop.HeroServiceVessel;
                var top = bar.Room.Find("Counter Top").GetComponent<Renderer>().bounds;
                Assert.That(vessel.GlassRenderer.bounds.min.y, Is.EqualTo(top.max.y).Within(.012f));
                CaptureCurrentCamera(camera, SceneIds.BarInterior, "fittings-served");
                int cleared = shop.ClearedEmptyVesselCount;
                // Leaving a paid drink behind uses the bartender's regular carry-to-storage sequence.
                for (int frame = 0; frame < 600 && !shop.RequestClosingDeparture(); frame++) yield return null;
                Assert.That(station.Seat.RequestExit(), Is.True);
                yield return WaitFor(() => shop.ClearedEmptyVesselCount == cleared + 1 && !station.Seat.IsSeated, "collect glass and stand");
                yield return VerifyBarKaraokeStanding(bar);
            }
            finally
            {
                Time.captureDeltaTime = previousStep;
            }
        }
    }
}
