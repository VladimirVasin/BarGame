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
        [UnityTest, Explicit("Apartment furniture, its contact points and the balcony threshold.")]
        [PrebuildSetup(typeof(HomeInteriorCaptureAssetsSetup))]
        public IEnumerator HomeFurnitureFinishes()
        {
            float previousStep = Time.captureDeltaTime;
            GameSessionState.BeginNewGame();
            GameSessionState.PrepareHomeArrival(HomeArrivalKind.NewGameSleep);
            try
            {
                Time.captureDeltaTime = 1f / 30f;
                yield return SceneManager.LoadSceneAsync(SceneIds.HomeInterior, LoadSceneMode.Single);
                var home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                Assert.That(home, Is.Not.Null);
                yield return WaitHomeBalcony(() => home.IsInitialized && home.Opening.Phase == HomeOpeningPhase.AwaitingWake, "sleeping");
                void Photo(string name, Vector3 eye, Vector3 aim, float lens)
                {
                    Camera camera = Camera.main;
                    var pose = new Pose(camera.transform.position, camera.transform.rotation);
                    float fov = camera.fieldOfView;
                    try
                    {
                        camera.transform.position = home.Room.TransformPoint(eye);
                        camera.transform.LookAt(home.Room.TransformPoint(aim), home.Room.up);
                        camera.fieldOfView = lens;
                        CaptureCurrentCamera(camera, SceneIds.HomeInterior, "furniture-" + name);
                    }
                    finally
                    {
                        camera.transform.SetPositionAndRotation(pose.position, pose.rotation);
                        camera.fieldOfView = fov;
                    }
                }
                var atlas = Resources.Load<Texture2D>("Home/Textures/HomeFurnitureFinishes");
                Assert.That(atlas, Is.Not.Null);
                var renderers = home.Room.GetComponentsInChildren<MeshRenderer>(true);
                void CheckFinishes()
                {
                    var block = new MaterialPropertyBlock();
                    foreach (string label in new[] { "Home Pillow", "Home Bed Mattress", "Home Sleeping Sofa Back",
                        "Home Kitchen Cabinet Pull 0", "Home Sink", "Home Refrigerator Shelf 1",
                        "Home Refrigerator Door Gasket Left", "Home Balcony Threshold",
                        "Home Balcony Folding Wood", "Home Balcony Outer Post", "Home Balcony Outer Rail Cap" })
                    {
                        var part = renderers.First(r => r.name == label);
                        part.GetPropertyBlock(block);
                        Assert.That(block.GetTexture("_BaseMap"), Is.SameAs(atlas), label);
                        Assert.That(block.GetVector("_BaseMap_ST"), Is.EqualTo(new Vector4(1, 1, 0, 0)), label);
                    }
                }
                CheckFinishes();
                Photo("head", new Vector3(-5.18f, 1.46f, -1.82f), new Vector3(-6.30f, .68f, -.91f), 61f);
                Assert.That(home.Opening.TryWake(), Is.True);
                yield return WaitHomeBalcony(() => home.Opening.Phase == HomeOpeningPhase.AwaitingStand, "seated after waking");
                Assert.That(home.Opening.TryStand(), Is.True);
                yield return WaitHomeBalcony(() => home.Opening.Phase == HomeOpeningPhase.Complete && home.Player.Motor.InputEnabled, "stand");
                double minutes = 13 * 60d - GameSessionState.GameTimeOfDayMinutes;
                if (minutes > 0) GameSessionState.AdvanceGameTime((float)(minutes / GameTimeState.GameMinutesPerRealSecond));
                home.DayNight.RefreshImmediate();
                yield return null;
                home.Player.Motor.Teleport(home.Room.TransformPoint(new Vector3(-3.60f, 0f, -.85f)));
                Photo("sofa", new Vector3(-4.66f, 1.52f, -1.79f), new Vector3(-6.28f, .62f, -.22f), 68f);
                home.Player.Motor.Teleport(home.Room.TransformPoint(home.RefrigeratorPlan.ApproachPosition));
                Physics.SyncTransforms();
                yield return null;
                Photo("sink", new Vector3(-5.48f, 1.70f, 1.38f), new Vector3(-6.00f, .97f, 2.88f), 65f);
                Assert.That(home.RefrigeratorInteraction.BeginInteraction(), Is.True);
                yield return WaitHomeBalcony(() => home.RefrigeratorInteraction.Phase == HomeRefrigeratorInteractionPhase.Inspecting, "inspect shelves");
                Photo("fridge", new Vector3(-4.48f, 1.51f, .87f), new Vector3(-4.66f, 1.13f, 2.94f), 66f);
                Assert.That(home.RefrigeratorInteraction.RequestClose(), Is.True);
                yield return WaitHomeBalcony(() => !home.RefrigeratorInteraction.OwnsInteraction, "fridge closed");
                Assert.That(home.BalconyDoor.IsOpen, Is.True);
                Photo("balcony", new Vector3(3.70f, 1.52f, -1.19f), new Vector3(5.63f, .93f, -.43f), 72f);
                Photo("rail", new Vector3(5.45f, 1.23f, -.28f), new Vector3(7.21f, .62f, -1.47f), 74f);
                AssertBalconyCapsuleCrossing(home, false);
                var player = home.Player.GameObject.transform;
                home.Player.Motor.Teleport(home.Room.TransformPoint(new Vector3(5.75f, .04f, -.50f)));
                Physics.SyncTransforms();
                player.GetComponent<CharacterController>().Move(home.Room.TransformDirection(Vector3.left * 1.4f));
                Assert.That(home.Room.InverseTransformPoint(player.position).x, Is.LessThan(4.75f));
                home.Player.Motor.Teleport(home.Layout.PlayerSpawn);
                var calendarAtlas = Resources.Load<Texture2D>("Home/Textures/HomeHouseholdFinishes");
                string[] witnesses = { "Home Day1 Folded Towel", "Day2 Table Bottle 0", "Day3 Sofa Foot Linen",
                    "Day4 Kitchen Spill", "Day5 Sofa Abandoned Duvet", "Day6 Sofa Dirty Sheet", "Day7 Sofa Crumpled Duvet" };
                var seat = renderers.First(r => r.name == "Home Bed Mattress");
                var state = new MaterialPropertyBlock();
                seat.GetPropertyBlock(state);
                Color clean = state.GetColor("_BaseColor");
                float previousWear = -1f;
                foreach (int level in new[] { 1, 2, 3, 4, 5, 6, 7, 4, 1 })
                {
                    Assert.That(home.DebugWindow.Open(), Is.True);
                    if (home.ApartmentDays.AppliedDayNumber != level)
                        Assert.That(home.DebugWindow.TrySetGameDayNumber(level), Is.True);
                    Assert.That(home.DebugWindow.Close(), Is.True);
                    yield return null;
                    Assert.That(home.ApartmentDays.AppliedDayNumber, Is.EqualTo(level));
                    CheckFinishes();
                    var witness = renderers.First(r => r.name == witnesses[level - 1]);
                    Assert.That(witness.gameObject.activeInHierarchy, Is.True, witness.name);
                    witness.GetPropertyBlock(state);
                    Assert.That(state.GetTexture("_BaseMap"), Is.SameAs(calendarAtlas), witness.name);
                    seat.GetPropertyBlock(state);
                    float wear = state.GetVector("_SurfaceDamageMode").x;
                    float t = (level - 1) / 6f;
                    Assert.That(wear, Is.EqualTo(t * (.45f + .55f * t)).Within(.001f));
                    if (level == 1) Assert.That(state.GetColor("_BaseColor"), Is.EqualTo(clean));
                    Assert.That(home.BalconyDoor.IsSealed, Is.EqualTo(level == 7));
                    AssertBalconyCapsuleCrossing(home, level == 7);
                    if (wear > previousWear)
                    {
                        Photo("level-" + level, new Vector3(-2.05f,2.29f,-3.28f), new Vector3(-5.21f,.74f,.80f), 73f);
                        Photo("kitchen-" + level, new Vector3(-4.91f,1.78f,1.12f), new Vector3(-5.94f,.97f,2.88f), 66f);
                    }
                    if (level == 7)
                        Photo("sealed", new Vector3(3.70f, 1.52f, -1.19f), new Vector3(5.63f, .93f, -.43f), 72f);
                    previousWear = wear;
                }
                // A return from sleep can select the last appearance while the calendar is still on day one.
                GameSessionState.CommitHomeSleepAppearance();
                Assert.That(GameSessionState.GameDayNumber, Is.EqualTo(1));
                Assert.That(home.ApartmentDays.RefreshImmediate(), Is.True);
                Assert.That(home.ApartmentDays.AppliedDayNumber, Is.EqualTo(7));
                Assert.That(home.BalconyDoor.IsSealed, Is.True);
                CheckFinishes();
                yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu, LoadSceneMode.Single);
            }
            finally
            {
                Time.captureDeltaTime = previousStep;
                GameSessionState.BeginNewGame();
            }
        }
    }
}
