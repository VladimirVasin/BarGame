using System.Collections;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class CityTerracePilotPlayModeTests
    {
        [UnityTest]
        public IEnumerator LiveCity_PhoneBoothPanelsLightEntriesAndForecourts()
        {
            Assert.That(GameSessionState.TryStartGameTimeAt(12 * 60), Is.True);
            yield return LoadProductionCity();
            Object.FindAnyObjectByType<GameTimeRuntime>().enabled = false;
            city.Weather.enabled = false;
            city.Player.Motor.SetInputEnabled(false);
            city.Player.Interactor.SetInputEnabled(false);
            var follow = city.Camera.GetComponent<PlayerCameraFollow>();
            follow.SetOrbitInputEnabled(false);
            var atmosphere = city.Night.Atmosphere;
            var docks = CityStreetUtilityDock.CreateAll(city.Layout, city.World.DecorationPlan)
                .Where(dock => dock.Kind == CityStreetUtilityKind.PhoneBooth).ToArray();
            var anchors = atmosphere.LampAnchors.Where(anchor => anchor != null &&
                anchor.name == "IndustrialTaskLight_PhoneBooth").ToArray();
            Assert.That(anchors.Length, Is.EqualTo(docks.Length), "Every City and Ridge booth needs one panel light anchor.");
            Assert.That(anchors.Length, Is.GreaterThan(3));
            var selected = new List<CityStreetUtilityDock> { docks.Where(dock => dock.Id.StartsWith("ridge-"))
                .OrderBy(dock => Vector3.Distance(dock.StandPosition, city.Layout.SummitHome.ThresholdWorld)).First() };
            foreach (var dock in docks.Where(dock => !dock.Id.StartsWith("ridge-")))
                if (selected.Count < 3 && selected.All(prior => Vector3.Dot(prior.Facing, dock.Facing) < .95f)) selected.Add(dock);
            Assert.That(selected.Count, Is.EqualTo(3), "Exercise the Ridge and differently rotated old-city booths.");
            var dayIntensity = new Dictionary<string, float>();
            bool capture = System.Environment.GetEnvironmentVariable("BAR_PROMENADE_CAPTURE_PHONE_BOOTH_LIGHTING") == "1" &&
                SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            int priorInterval = OnDemandRendering.renderFrameInterval;
            Light priorLease = null;
            Vector3 priorAnchor = default;
            try
            {
                OnDemandRendering.renderFrameInterval = 24;
                foreach (int minute in new[] { 12 * 60, 0 })
                {
                    AdvanceRidgeClock(minute);
                    foreach (var dock in selected)
                    {
                        Vector3 forward = -dock.Facing, right = Vector3.Cross(Vector3.up, forward);
                        Transform anchor = anchors.OrderBy(candidate => Vector3.Distance(candidate.position,
                            dock.StandPosition + Vector3.up * 2.65f)).First();
                        Assert.That(anchor.position.y - dock.StandPosition.y, Is.EqualTo(2.65f).Within(.085f));
                        Assert.That(Vector3.Distance(anchor.position, dock.StandPosition + Vector3.up * 2.65f), Is.LessThan(1f));
                        city.Player.Motor.Teleport(dock.StandPosition + forward * 2.8f + right * 1.6f +
                            Vector3.up * PlayerFactory.GroundedRootOffset);
                        Physics.SyncTransforms();
                        atmosphere.RefreshImmediate();
                        yield return null;
                        yield return null;
                        Light source = atmosphere.StreetLightPool.Single(light => light.isActiveAndEnabled &&
                            Vector3.Distance(light.transform.position, anchor.position) < .01f);
                        Assert.That(atmosphere.RealtimeLightCount, Is.LessThanOrEqualTo(CityNightAtmosphere.MaximumRealtimeLights));
                        Assert.That(source.type, Is.EqualTo(LightType.Spot));
                        Assert.That(source.shadows, Is.EqualTo(LightShadows.Soft));
                        Assert.That(source.range, Is.EqualTo(6.5f).Within(.01f));
                        Assert.That(source.spotAngle, Is.EqualTo(130f).Within(.1f));
                        Assert.That(source.color.g, Is.GreaterThan(source.color.r), "Retain the existing pale fluorescent panel colour.");
                        if (minute == 720) dayIntensity[dock.Id] = source.intensity;
                        else Assert.That(dayIntensity[dock.Id], Is.GreaterThanOrEqualTo(source.intensity * (2f / 3f) - .001f));
                        Vector3 door = dock.StandPosition + dock.Facing * .58f + Vector3.up * 1.2f;
                        Vector3 threshold = PhoneBoothGround(dock.StandPosition + dock.Facing * .37f) + Vector3.up * .1f;
                        Vector3 forecourt = PhoneBoothGround(dock.StandPosition + forward * 1.4f) + Vector3.up * .1f;
                        foreach (Vector3 point in new[] { door, threshold, forecourt })
                        {
                            Vector3 delta = point - source.transform.position;
                            Assert.That(delta.magnitude, Is.LessThan(source.range * .95f));
                            Assert.That(Vector3.Angle(delta, source.transform.forward), Is.LessThan(source.spotAngle * .5f - 2f));
                        }
                        // A coarse utility proxy can cover the actual door face. The
                        // rendered A/B below proves that the real visible face receives light.
                        Assert.That(RidgeLightHasClearPath(source, threshold) && RidgeLightHasClearPath(source, forecourt), Is.True,
                            dock.Id + " must cast its real beam onto the entry and forecourt.");
                        Debug.Log($"Phone booth {dock.Id}, minute {minute}: source={source.transform.position:F2}, intensity={source.intensity:F3}.");
                        if (capture)
                        {
                            Vector3 eye = dock.StandPosition + forward * 4.8f + right * 2.1f + Vector3.up * 1.7f;
                            follow.SetFixedPose(eye, Quaternion.LookRotation(dock.StandPosition - forward * .4f + Vector3.up * 1.15f - eye), 59f);
                            yield return null;
                            yield return null;
                            PhotographPhone(source, minute + "-phone-" + selected.IndexOf(dock),
                                new[] { ("entry-face", door), ("threshold", threshold), ("forecourt", forecourt) }, minute == 0);
                            follow.ClearFixedPose();
                        }
                        priorLease = source;
                        priorAnchor = anchor.position;
                    }
                }
                // A normal mast must restore the shared slot after the booth lease.
                var taskAnchors = atmosphere.LampAnchors.Where(anchor => anchor != null && anchor.name.StartsWith("IndustrialTaskLight_")).ToArray();
                Transform farMast = city.Night.LampAnchors.OrderByDescending(mast =>
                    taskAnchors.Min(anchor => (anchor.position - mast.position).sqrMagnitude)).First();
                city.Player.Motor.Teleport(farMast.position - Vector3.up * 4.7f + Vector3.up * PlayerFactory.GroundedRootOffset);
                Physics.SyncTransforms();
                atmosphere.RefreshImmediate();
                Assert.That(Vector3.Distance(priorLease.transform.position, priorAnchor), Is.GreaterThan(1f));
                Assert.That(priorLease.shadows, Is.EqualTo(LightShadows.None), "A retasked normal street light must clear the booth shadow profile.");
                var data = priorLease.GetUniversalAdditionalLightData();
                Assert.That(data.usePipelineSettings && !data.customShadowLayers && (uint)data.renderingLayers == 1u, Is.True);
            }
            finally { follow.ClearFixedPose(); OnDemandRendering.renderFrameInterval = priorInterval; }

            Vector3 PhoneBoothGround(Vector3 point)
            {
                var xz = new Vector2(point.x, point.z);
                bool supported = CityStreetSupportPlan.ForLayout(city.Layout).TrySampleTop(xz, out float top, out _) ||
                    CityHillsideGroundPlan.ForLayout(city.Layout).TrySampleTop(xz, out top) ||
                    CityTerrainSurfacePlan.TrySampleGroundTop(city.Layout, xz, out top, out _);
                Assert.That(supported, Is.True, "The booth forecourt needs a real receiving surface: " + point);
                return new Vector3(point.x, top, point.z);
            }

            void PhotographPhone(Light source, string shot, (string name, Vector3 point)[] patches, bool night)
            {
                var renderers = city.Player.GameObject.GetComponentsInChildren<Renderer>();
                var states = renderers.Select(renderer => renderer.enabled).ToArray();
                bool enabled = source.enabled;
                float intensity = source.intensity, aspect = city.Camera.aspect;
                try
                {
                    foreach (Renderer renderer in renderers) renderer.enabled = false;
                    city.Camera.aspect = 1280f / 720f;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting", shot + "-on", 1280, 720);
                    source.enabled = false;
                    source.intensity = 0f;
                    AreaCaptureFixture.CaptureCurrentCamera(city.Camera, "RidgeLighting", shot + "-off", 1280, 720);
                    foreach (var patch in patches)
                    {
                        Vector3 viewport = city.Camera.WorldToViewportPoint(patch.point);
                        Assert.That(viewport.z > 0f && viewport.x > .03f && viewport.x < .97f && viewport.y > .03f && viewport.y < .97f,
                            Is.True, shot + "/" + patch.name + " must remain inside the observational frame.");
                        float lit = RidgeKioskStreetPatchLuminance(shot + "-on", viewport);
                        float unlit = RidgeKioskStreetPatchLuminance(shot + "-off", viewport);
                        Debug.Log($"Phone rendered {shot}/{patch.name}: on={lit:F5}, off={unlit:F5}.");
                        Assert.That(lit, Is.GreaterThan(unlit * (night ? 1.10f : 1.025f) + (night ? .0005f : .0002f)),
                            shot + "/" + patch.name + ": the real panel source must brighten this visible surface.");
                    }
                }
                finally
                {
                    source.intensity = intensity; source.enabled = enabled; city.Camera.aspect = aspect;
                    for (int i = 0; i < renderers.Length; i++) if (renderers[i] != null) renderers[i].enabled = states[i];
                }
            }
        }
    }
}
