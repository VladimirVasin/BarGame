using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest, Timeout(360000), Explicit("Review tonal separation along a walk under real stair, counter and street lights.")]
        public IEnumerator WorldTonalReadability()
        {
            const string barId = "tonal-readability";
            GameSessionState.BeginNewGame(barId);
            try
            {
                GameSessionState.TryStartGameTimeAt(22 * 60);
                yield return SceneManager.LoadSceneAsync(SceneIds.StairwellInterior);
                StairwellInteriorRoot stair = null;
                yield return WaitFor(() => (stair = Object.FindAnyObjectByType<StairwellInteriorRoot>()) && stair.IsInitialized, "Stairwell");
                var flight = stair.Layout.LowerFlight;
                Vector3 forward = new Vector3(flight.Direction.x, 0f, flight.Direction.y);
                Vector3 foot = new Vector3(flight.Start.x, flight.BaseElevation, flight.Start.y);
                Vector3 rail = stair.World.Root.TransformPoint(foot + forward * 2.7f +
                    Vector3.Cross(Vector3.up, forward) * (flight.Width * .5f) + Vector3.up * 1.7f);
                Vector3 eye = stair.World.Root.TransformPoint(foot - forward * .75f + Vector3.up * EyeHeight);
                CompareTonalWalk(stair.Atmosphere.PostProcessVolume.profile, "stair",
                    eye, rail - Vector3.up * .32f, stair.World.Root.TransformDirection(forward) * .33f);

                GameSessionState.EnterBar(barId, BarActivityKind.SplitTheG, CityDistrictKind.Residential);
                yield return SceneManager.LoadSceneAsync(SceneIds.BarInterior);
                BarInteriorRoot bar = null;
                yield return WaitFor(() => (bar = Object.FindAnyObjectByType<BarInteriorRoot>()) && bar.IsInitialized, "Bar");
                bar.ArrivalPresentation?.Skip();
                Vector3 face = bar.Bartender.Registry.Head.position;
                Vector3 pendant = bar.Room.TransformPoint(bar.Layout.LightAnchors.Single(a => a.Id == "counter-pendant-center").Position);
                CompareTonalWalk(bar.Atmosphere.PostProcessVolume.profile, "bar",
                    bar.Room.TransformPoint(new Vector3(-.32f, EyeHeight, .26f)),
                    Vector3.Lerp(face, pendant, .36f), bar.Room.right * .30f);

                yield return SceneManager.LoadSceneAsync(SceneIds.City);
                CityGameRoot city = null;
                yield return WaitFor(() => (city = Object.FindAnyObjectByType<CityGameRoot>()) && city.IsInitialized, "City");
                var volume = Object.FindObjectsByType<Volume>().Single(v => v.gameObject.scene == city.gameObject.scene &&
                    v.sharedProfile && v.sharedProfile.name == "CityNoirVolumeProfile");
                Assert.That(volume.sharedProfile.TryGet(out ShadowsMidtonesHighlights _), Is.False,
                    "The night grade belongs to the live scene clone.");
                var player = city.Player.GameObject.transform;
                var lamp = city.Night.LampAnchors.Where(a => Mathf.Abs(a.forward.y) < .05f)
                    .OrderBy(a => (a.position - player.position).sqrMagnitude).First();
                Vector3 street = lamp.position + lamp.forward * 1.7f;
                Assert.That(Physics.Raycast(street, Vector3.down, out RaycastHit ground, 10f, Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore), Is.True, "Ground in the real street light pool");
                city.Player.Motor.Teleport(ground.point + Vector3.up * PlayerFactory.GroundedRootOffset);
                city.Night.Atmosphere.RefreshImmediate();
                city.Visibility.Refresh(true);
                yield return null;
                Vector3 alongStreet = Vector3.Cross(Vector3.up, lamp.forward).normalized;
                eye = ground.point + Vector3.up * EyeHeight + alongStreet * 5.8f + lamp.forward * .6f;
                CompareTonalWalk(volume.profile, "street", eye,
                    ground.point + Vector3.up * 1.95f, -alongStreet * .45f);

                volume.profile.TryGet(out ShadowsMidtonesHighlights night);
                Assert.That(night.shadows.value.w, Is.Zero, "Unlit gaps retain unit shadow gain.");
                Assert.That(night.midtones.value.w, Is.GreaterThan(0f));
                AdvanceNightGradeClock(12 * 60);
                city.DayNight.ApplyCurrentTime(true);
                Assert.That(night.midtones.value.w, Is.Zero);
                Assert.That(night.highlights.value.w, Is.Zero);
            }
            finally { GameSessionState.BeginNewGame(); }
            yield return SceneManager.LoadSceneAsync(SceneIds.MainMenu);
        }

        private static void CompareTonalWalk(VolumeProfile profile, string scene, Vector3 eye, Vector3 target, Vector3 stride)
        {
            Assert.That(profile.TryGet(out ShadowsMidtonesHighlights tones), Is.True);
            Assert.That(tones.active && tones.IsActive(), Is.True);
            Camera camera = Camera.main;
            Assert.That(camera.GetUniversalAdditionalCameraData().renderPostProcessing, Is.True);
            var saved = new DefaultNpcCameraState(camera);
            try
            {
                camera.fieldOfView = 60f;
                for (int step = 0; step < 3; step++)
                {
                    Vector3 position = eye + stride * step;
                    camera.transform.SetPositionAndRotation(position, Quaternion.LookRotation(target - position));
                    // Render both states in the same simulation tick: same pose, lamps and weather.
                    tones.active = false;
                    CaptureCurrentCamera(camera, "WorldTonalReadability", scene + "-" + step + "-before");
                    tones.active = true;
                    CaptureCurrentCamera(camera, "WorldTonalReadability", scene + "-" + step + "-after");
                }
            }
            finally
            {
                tones.active = true;
                saved.Restore(camera);
            }
        }
    }
}
