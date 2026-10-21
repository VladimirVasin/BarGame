using System.Collections;
using BarPromenade.Rendering;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static IEnumerator CapturePostProcessingRooms()
        {
            bool previousLens = GraphicsEffectsSettings.IntoxicationLensFxEnabled;
            Random.State previousRandom = Random.state;
            BarInteriorRoot bar = null;
            HomeInteriorRoot home = null;
            StairwellInteriorRoot stair = null;
            try
            {
                Assert.That(BarInteriorAtmosphere.FilmGrainIntensity, Is.EqualTo(.04f));
                Assert.That(HomeInteriorAtmosphere.FilmGrainIntensity, Is.EqualTo(.04f));
                Assert.That(StairwellInteriorAtmosphere.FilmGrainIntensity, Is.EqualTo(.05f));
                Assert.That(StairwellInteriorAtmosphere.VignetteIntensity, Is.EqualTo(.23f));
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = false;
                IntoxicationRenderState.Clear();
                GameSessionState.EnterBar("Test bar post processing", BarActivityKind.SplitTheG, CityDistrictKind.Residential);
                yield return Capture(SceneIds.BarInterior, () =>
                {
                    bar = Object.FindAnyObjectByType<BarInteriorRoot>();
                    return bar != null && bar.IsInitialized ? bar : null;
                }, () =>
                {
                    FreezePostProcessingRoom(bar.Player.Motor);
                    return PostProcessingGrainPair(bar.Atmosphere.PostProcessVolume.profile,
                        "Test bar", .12f, BarInteriorAtmosphere.FilmGrainIntensity, FilmGrainLookup.Thin1, .72f,
                        new Vector3(-1.6f, 1.65f, 1.4f), new Vector3(-.6f, 1.35f, 6.2f), 55f);
                });
                Assert.That(PolishDifference(SceneIds.BarInterior, "Test bar - Previous grain", "Test bar - Calibrated grain"),
                    Is.GreaterThan(24));

                // Release the UI owner while its camera-local renderers still exist.
                var arms = bar.DrinkShop.FirstPersonArms;
                Object.Destroy(bar.DrinkShop);
                yield return null;
                if (arms != null) Object.Destroy(arms);
                yield return null;

                GameSessionState.EnterHome();
                GameSessionState.PrepareHomeArrival(HomeArrivalKind.Normal);
                yield return Capture(SceneIds.HomeInterior, () =>
                {
                    home = Object.FindAnyObjectByType<HomeInteriorRoot>();
                    return home != null && home.IsInitialized ? home : null;
                }, () =>
                {
                    FreezePostProcessingRoom(home.Player.Motor);
                    Assert.That(home.FixedCamera.ActiveShotKind, Is.EqualTo(HomeCameraShotKind.MainRoom));
                    HomeCameraShot shot = home.FixedCamera.ActiveShot;
                    return PostProcessingGrainPair(home.Atmosphere.RuntimeProfile,
                        "Test home", .14f, HomeInteriorAtmosphere.FilmGrainIntensity, FilmGrainLookup.Thin1, .74f,
                        shot.Position, shot.Position + shot.Rotation * Vector3.forward, shot.FieldOfView);
                });
                Assert.That(PolishDifference(SceneIds.HomeInterior, "Test home - Previous grain", "Test home - Calibrated grain"),
                    Is.GreaterThan(24));

                GameSessionState.PrepareStairwellArrival(StairwellArrivalKind.StreetDoor);
                yield return Capture(SceneIds.StairwellInterior, () =>
                {
                    stair = Object.FindAnyObjectByType<StairwellInteriorRoot>();
                    return stair != null && stair.IsInitialized ? stair : null;
                }, () =>
                {
                    FreezePostProcessingRoom(stair.Player.Motor);
                    VolumeProfile profile = stair.Atmosphere.RuntimeProfile;
                    Assert.That(profile.TryGet(out Vignette vignette), Is.True);
                    Assert.That(vignette.active, Is.True);
                    Assert.That(vignette.intensity.value, Is.EqualTo(StairwellInteriorAtmosphere.VignetteIntensity));
                    Assert.That(vignette.smoothness.value, Is.EqualTo(.56f));
                    StairwellCameraShot pose = StairwellFixedCameraController.CreateDefaultShots(stair.Layout)[1];
                    Vector3 aim = pose.Position + pose.Rotation * Vector3.forward;
                    Shot[] grain = PostProcessingGrainPair(profile, "Test stairwell", .19f,
                        StairwellInteriorAtmosphere.FilmGrainIntensity, FilmGrainLookup.Thin2, .78f,
                        pose.Position, aim, pose.FieldOfView);
                    Random.State noise = Random.state;
                    return new[]
                    {
                        grain[0], grain[1],
                        VignetteShot("Sober previous vignette", .31f, false),
                        VignetteShot("Sober calibrated vignette", StairwellInteriorAtmosphere.VignetteIntensity, false),
                        VignetteShot("Maximum lens previous vignette", .31f, true),
                        VignetteShot("Maximum lens calibrated vignette", StairwellInteriorAtmosphere.VignetteIntensity, true)
                    };

                    Shot VignetteShot(string label, float strength, bool intoxicated) =>
                        Shot.At("Test stairwell - " + label, pose.Position, aim, pose.FieldOfView, readyWhen: () =>
                        {
                            Random.state = noise;
                            vignette.intensity.Override(strength);
                            if (intoxicated)
                            {
                                GraphicsEffectsSettings.IntoxicationLensFxEnabled = true;
                                // Fixed presentation phase, without advancing balance/fall simulation.
                                IntoxicationRenderState.Set(IntoxicationStageRules.Evaluate(100),
                                    1.25f, 0f, Vector2.zero, Vector3.zero);
                                Assert.That(IntoxicationRenderState.Current.VignetteStrength, Is.EqualTo(.28f));
                            }
                            else IntoxicationRenderState.Clear();
                            return true;
                        });
                });
                Assert.That(PolishDifference(SceneIds.StairwellInterior,
                    "Test stairwell - Previous grain", "Test stairwell - Calibrated grain"), Is.GreaterThan(24));
                foreach (string condition in new[] { "Sober", "Maximum lens" })
                    Assert.That(PolishDifference(SceneIds.StairwellInterior,
                        "Test stairwell - " + condition + " previous vignette",
                        "Test stairwell - " + condition + " calibrated vignette"), Is.GreaterThan(24));
            }
            finally
            {
                if (bar != null && bar.Atmosphere != null && bar.Atmosphere.PostProcessVolume != null)
                    RestorePostProcessingGrain(bar.Atmosphere.PostProcessVolume.profile, BarInteriorAtmosphere.FilmGrainIntensity);
                if (home != null && home.Atmosphere != null)
                    RestorePostProcessingGrain(home.Atmosphere.RuntimeProfile, HomeInteriorAtmosphere.FilmGrainIntensity);
                if (stair != null && stair.Atmosphere != null)
                {
                    RestorePostProcessingGrain(stair.Atmosphere.RuntimeProfile, StairwellInteriorAtmosphere.FilmGrainIntensity);
                    if (stair.Atmosphere.RuntimeProfile != null && stair.Atmosphere.RuntimeProfile.TryGet(out Vignette vignette))
                        vignette.intensity.Override(StairwellInteriorAtmosphere.VignetteIntensity);
                }
                // Also preserve teardown order when a bar assertion aborts before its scene handoff.
                if (bar != null && bar.DrinkShop != null)
                {
                    var arms = bar.DrinkShop.FirstPersonArms;
                    Object.DestroyImmediate(bar.DrinkShop);
                    if (arms != null) Object.DestroyImmediate(arms);
                }
                IntoxicationRenderState.Clear();
                GraphicsEffectsSettings.IntoxicationLensFxEnabled = previousLens;
                Random.state = previousRandom;
            }
        }

        private static Shot[] PostProcessingGrainPair(VolumeProfile profile, string label,
            float previous, float calibrated, FilmGrainLookup preset, float response,
            Vector3 eye, Vector3 aim, float fieldOfView)
        {
            Assert.That(profile.TryGet(out FilmGrain grain), Is.True);
            Assert.That(grain.active, Is.True);
            Assert.That(grain.type.value, Is.EqualTo(preset));
            Assert.That(grain.intensity.overrideState, Is.True);
            Assert.That(grain.intensity.value, Is.EqualTo(calibrated));
            Assert.That(grain.response.value, Is.EqualTo(response));
            Random.State noise = Random.state;
            int captureFrame = -1;
            return new[] { GrainShot("Previous grain", previous), GrainShot("Calibrated grain", calibrated) };

            Shot GrainShot(string suffix, float strength) =>
                Shot.At(label + " - " + suffix, eye, aim, fieldOfView, readyWhen: () =>
                {
                    // Adjacent shots render in one engine frame, using the same grain phase/offset.
                    if (captureFrame < 0) captureFrame = Time.frameCount;
                    Assert.That(Time.frameCount, Is.EqualTo(captureFrame));
                    Random.state = noise;
                    grain.intensity.Override(strength);
                    Assert.That(grain.active, Is.True);
                    return true;
                });
        }

        private static void RestorePostProcessingGrain(VolumeProfile profile, float intensity)
        {
            if (profile != null && profile.TryGet(out FilmGrain grain)) grain.intensity.Override(intensity);
        }

        private static void FreezePostProcessingRoom(PlayerMotor motor)
        {
            motor.SetInputEnabled(false);
            var clock = Object.FindAnyObjectByType<GameTimeRuntime>();
            if (clock != null) clock.enabled = false;
            var follow = Camera.main.GetComponent<PlayerCameraFollow>();
            if (follow != null) follow.enabled = false;
        }
    }
}
