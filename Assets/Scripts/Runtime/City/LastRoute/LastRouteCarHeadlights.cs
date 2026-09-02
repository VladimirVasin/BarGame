using System;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BarPromenade
{
    /// <summary>
    /// The car's burning headlights: two shadow-casting beams and one wide
    /// unshadowed spill, raised only where they are meant to be the thing
    /// the world is lit by.
    ///
    /// Until now the car's lamps were halo billboards and nothing else, on
    /// the stated ground that the night light budget belongs to the street
    /// masts. That holds in the city, where the masts exist. On the mountain
    /// road there are no masts, no windows and — during the climb — no sun
    /// either, and the halos are a bloom around a lamp that is not lighting
    /// anything. So the halos stay exactly as they were, and these are added
    /// on top: the glow was never the problem, the absence of light was.
    ///
    /// They hang off the SPRUNG body rather than the runtime root, so the
    /// beam dips when the car brakes and lifts when it pulls away without a
    /// line of code to say so. The root deliberately does not rock — it
    /// carries the obstacle collider — which is the one reason the halos
    /// are still on it.
    ///
    /// They light a road that is lit ANYWAY. There was a build in which the
    /// mountain's sun, ambient, reflection and fog were all taken out while
    /// the car drove, so that these were the only light in the world; it
    /// came up as a black frame with two blown-white pools in it and was
    /// pulled on sight. The area keeps its own ordinary grade at every hour,
    /// and these burn on top of it — which is what a car's headlights
    /// actually are.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class LastRouteCarHeadlights : MonoBehaviour
    {
        /// <summary>
        /// Do not reason about this number. Arithmetic against the street
        /// masts (`31` over `16.5 m`) said `110`, and that was more than an
        /// order of magnitude short, because a mast lights a pavement eight
        /// metres below it and a headlight has to throw twenty. The way to
        /// move it is a PlayMode capture of the real road — the main camera
        /// to a RenderTexture, read back as sRGB (a Linear readback encoded
        /// to PNG reads about ten times too dark) with the camera anywhere
        /// but `0.6 m` behind the car, which is inside the cabin.
        ///
        /// `2600` was measured, but against a world with its sun, ambient,
        /// reflection and fog all taken out — and that world is gone. On the
        /// area's ordinary grade the beams have a lit sky to be seen
        /// against, so they burn harder: this is the "сильно усиль свет от
        /// фар" of 2026-08-26, set by ratio to the measured value rather
        /// than re-measured, because the editor held the project lock. If it
        /// wants tuning, tune it here and nowhere else.
        /// </summary>
        public const float BeamIntensity = 6000f;

        /// <summary>Past the URP asset's `50 m` shadow distance on purpose:
        /// the far end of the throw is fog and silhouette, and nothing out
        /// there needs to cast.</summary>
        public const float BeamRange = 58f;

        public const float BeamSpotAngle = 52f;
        public const float BeamInnerSpotAngle = 22f;

        /// <summary>
        /// A `52°` cone leaves a hard black wedge either side of the car,
        /// which reads as a bug the first time a hairpin swings the road
        /// out of the beam. One wide, dim, unshadowed light fills it. In
        /// Forward+ the per-object light limit does not bind, so it is very
        /// nearly free.
        /// </summary>
        public const float SpillIntensity = 300f;

        public const float SpillRange = 20f;
        public const float SpillSpotAngle = 120f;
        public const float SpillInnerSpotAngle = 40f;

        /// <summary>
        /// The dipped beam of a car STANDING on the mountain apron, which is
        /// a different fixture from the one above even though it is the same
        /// three lamps.
        ///
        /// <see cref="BeamIntensity"/> is sized to throw twenty metres down a
        /// moving road. A parked car has to mark the middle of a yard eight
        /// metres in front of its own bumper, and this is that number by
        /// throw: the lamps sit about `0.75 m` up raked `5.5°` down, so the
        /// pool centres near `8 m` and `16 / 8² = 0.25` arrives - against the
        /// `17.25 / 5.5² = 0.57` the mercury yard lamp lays under itself.
        /// Half the yard fixture is a mark; six thousand would be the blown
        /// white pool that got a whole feature pulled.
        ///
        /// It also agrees with this project's own precedent for a vehicle
        /// lamp seen in a scene rather than driven behind - the bus runs its
        /// headlights at `14` over `22 m` - and it sits inside the mountain's
        /// documented `1.65`-`16` fixture band, which the city's `31`-`240`
        /// scale does not.
        /// </summary>
        public const float StandingBeamIntensity = 16f;

        /// <summary>The wide filler, at the same ratio to the standing beam
        /// that <see cref="SpillIntensity"/> holds to the full one.</summary>
        public const float StandingSpillIntensity =
            SpillIntensity * (StandingBeamIntensity / BeamIntensity);

        /// <summary>
        /// How far PROUD of the lamp's own front face each emitter sits.
        /// Forward, and small: a headlight is a thing on the outside of a
        /// car and its light starts at the glass.
        ///
        /// This used to be `1.8 m` the other way — BEHIND the lens — on the
        /// reasoning that inverse-square makes the four metres ahead of the
        /// bumper eleven times brighter than the pool at fourteen, and that
        /// setting the source back flattens it to about four. The arithmetic
        /// was right and the fix was wrong: `1.8 m` back from the lamps of
        /// this car is the WINDSCREEN, so both beams were emitting from
        /// inside the cabin. Their cones then opened across the bonnet, the
        /// pillars and the door card on the way out, and the "blown white
        /// pools" in the frame were never the road at all — they were the
        /// car lighting itself.
        ///
        /// The near-field hot zone the setback was hiding does come back,
        /// and it does not matter: from the seat it falls behind the bonnet,
        /// which is exactly where a real car puts it.
        /// </summary>
        public const float LensStandoffMeters = 0.12f;

        /// <summary>Rake, in degrees below the horizon.</summary>
        public const float BeamPitchDegrees = 5.5f;

        /// <summary>Toe-out, so the pair reads as a pair.</summary>
        public const float BeamSpreadDegrees = 6f;

        private static readonly Color BeamColor =
            new Color(1.00f, 0.955f, 0.865f);

        /// <summary>Seconds for the beams to come up, and to die again as
        /// the car stops on the terrace. A headlight that snaps reads as a
        /// bug in the fade.</summary>
        public const float SwitchOnSeconds = 1.2f;

        public const float SwitchOffSeconds = 2.5f;

        /// <summary>
        /// <see cref="Power"/> is a fraction of the FULL beam, so a dipped
        /// lamp sits at `16 / 6000`, and the old "is it burning" test of
        /// `power > 0.004` would have switched it off as rounding error. The
        /// question is about delivered light, so ask it in intensity.
        /// </summary>
        private const float MinimumBurningIntensity = 0.01f;

        /// <summary>What a dipped lamp is as a fraction of full beam.</summary>
        public const float StandingPower =
            StandingBeamIntensity / BeamIntensity;

        private Light leftBeam;
        private Light rightBeam;
        private Light spill;
        private float appliedPower = -1f;
        private LastRouteCarLamps lamps = LastRouteCarLamps.RideOnly;
        private LastRouteRideController ride;

        public bool IsInitialized { get; private set; }
        public Light LeftBeam => leftBeam;
        public Light RightBeam => rightBeam;
        public Light Spill => spill;
        public float Power => appliedPower < 0f ? 0f : appliedPower;

        /// <summary>
        /// What these lamps fall back to with no journey running: dipped on
        /// the mountain apron, dark on a car that only lights its own trip.
        /// </summary>
        public float RestingPower =>
            lamps == LastRouteCarLamps.AlwaysDipped ? StandingPower : 0f;

        /// <summary>
        /// Builds the three lamps under <paramref name="carrier"/>, aimed
        /// along the axes of <paramref name="root"/>. They start dark;
        /// <see cref="SetPower"/> is what turns them on.
        ///
        /// THE TWO TRANSFORMS ARE NOT INTERCHANGEABLE, AND THIS COST A BUILD.
        /// The carrier is the sprung body, which is where the lamps have to
        /// hang so the beam dips under braking — but
        /// <c>LastRouteCarSuspension.TryCreateSprungBody</c> copies its
        /// `localRotation` straight off the IMPORTED body node, and this
        /// car's imported forward is very nearly vertical. Aiming with a
        /// local Euler in that space threw both beams at the sky, so the
        /// blackout landed on a road nothing was lighting and the scene came
        /// up black. Every axis therefore comes from the runtime root, which
        /// is the one transform this project sets itself
        /// (`LookRotation(plan.Facing, up)`), and the world pose is written
        /// AFTER parenting so the spring still carries it. Same rule as
        /// everywhere else here: resolve against the runtime root, never
        /// against an imported node.
        /// </summary>
        public void Initialize(
            Transform root,
            Transform carrier,
            Vector3 lensCenterWorld,
            float lensHalfWidth,
            float lensHalfDepth,
            LastRouteCarLamps lamps = LastRouteCarLamps.RideOnly)
        {
            if (IsInitialized)
            {
                throw new InvalidOperationException(
                    "The car's headlights are already initialized.");
            }

            if (root == null)
            {
                throw new ArgumentNullException(nameof(root));
            }

            if (carrier == null)
            {
                throw new ArgumentNullException(nameof(carrier));
            }

            Vector3 forward = root.forward;
            Vector3 up = root.up;
            Vector3 right = root.right;
            Quaternion facing = Quaternion.LookRotation(forward, up);

            // Out through the glass and clear of it. The `up` nudge that used
            // to be here is gone with the setback: it lifted the source
            // toward the bonnet line, which is one of the things the beams
            // were washing.
            Vector3 emitter = lensCenterWorld +
                              forward *
                              (Mathf.Max(0f, lensHalfDepth) +
                               LensStandoffMeters);

            leftBeam = CreateBeam(
                carrier,
                "Headlight Beam Left",
                emitter - right * (lensHalfWidth * 0.72f),
                facing * Quaternion.Euler(
                    BeamPitchDegrees,
                    -BeamSpreadDegrees,
                    0f));
            rightBeam = CreateBeam(
                carrier,
                "Headlight Beam Right",
                emitter + right * (lensHalfWidth * 0.72f),
                facing * Quaternion.Euler(
                    BeamPitchDegrees,
                    BeamSpreadDegrees,
                    0f));
            spill = CreateSpill(
                carrier,
                emitter,
                facing * Quaternion.Euler(
                    BeamPitchDegrees * 1.6f,
                    0f,
                    0f));
            this.lamps = lamps;
            IsInitialized = true;

            // Straight to the resting level, not a ramp up to it. A parked
            // car's lamps are already on when you walk round the corner; a
            // fade-in on scene load would read as the lights coming on
            // because the hero arrived.
            SetPower(RestingPower);
        }

        /// <summary>
        /// Hands the lamps the journey. They burn while it is running and go
        /// out when the car stops, and they own that themselves rather than
        /// borrowing it from whatever is grading the sky - the sky is not
        /// graded any more, and a headlight is a switch on a car either way.
        ///
        /// The controller's own flags are POLLED rather than subscribed to,
        /// because the mountain leg has several ways to decline to start (a
        /// path that will not build, a seat that will not resume) and a
        /// polled flag is right on every one of them where a "began" event
        /// would leave the beams burning on a car that never moved.
        /// </summary>
        public void Follow(LastRouteRideController rideController)
        {
            ride = rideController;

            // The mountain leg is armed under a screen that is already fully
            // black, so coming up at once costs nothing and starting dark
            // would mean fading up onto an unlit road for a frame.
            if (ride != null && (ride.IsRiding || ride.IsAwaitingStart))
            {
                SetPower(1f);
            }
        }

        /// <summary>
        /// `0` is a dead lamp, `1` is full beam. It is a continuous power
        /// rather than a switch because the beams come up and die on a ramp,
        /// and a headlight that snaps reads as a bug in the fade.
        /// </summary>
        public void SetPower(float power)
        {
            if (!IsInitialized)
            {
                return;
            }

            float clamped = Mathf.Clamp01(power);
            if (clamped.Equals(appliedPower))
            {
                return;
            }

            appliedPower = clamped;
            bool burning =
                clamped * BeamIntensity > MinimumBurningIntensity;
            ApplyPower(leftBeam, BeamIntensity, clamped, burning);
            ApplyPower(rightBeam, BeamIntensity, clamped, burning);
            ApplyPower(spill, SpillIntensity, clamped, burning);
        }

        private void Update()
        {
            if (!IsInitialized || ride == null)
            {
                return;
            }

            bool driving = ride.IsRiding || ride.IsAwaitingStart;

            // Not `0f` any more: a car that has finished its journey on the
            // mountain apron is a PARKED car there, and parked is dipped
            // rather than dark. The arriving leg therefore needs no separate
            // mode - it comes up full under the black screen, drives, and
            // settles onto the same standing beam the chart-arrival car
            // already burns.
            float target = driving ? 1f : RestingPower;
            if (Mathf.Approximately(Power, target))
            {
                return;
            }

            // Unscaled: the pause menu freezes `timeScale`, and beams that
            // kept ramping through a paused game would come up on a car that
            // is standing still.
            float seconds = driving ? SwitchOnSeconds : SwitchOffSeconds;
            SetPower(
                Mathf.MoveTowards(
                    Power,
                    target,
                    Time.unscaledDeltaTime / Mathf.Max(seconds, 0.001f)));
        }

        private static void ApplyPower(
            Light light,
            float fullIntensity,
            float power,
            bool burning)
        {
            if (light == null)
            {
                return;
            }

            light.intensity = fullIntensity * power;
            light.enabled = burning;
        }

        private static Light CreateBeam(
            Transform carrier,
            string name,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            Light light = CreateLight(
                carrier,
                name,
                worldPosition,
                worldRotation);
            light.range = BeamRange;
            light.spotAngle = BeamSpotAngle;
            light.innerSpotAngle = BeamInnerSpotAngle;
            light.shadows = LightShadows.Hard;
            light.shadowStrength = 0.88f;
            light.shadowBias = 0.05f;

            // Deliberately above the project's usual 0.25. These beams sweep
            // a forest of seven-sided cones whose normals are their own, and
            // on a cone peter-panning is invisible where acne is not. This
            // is the dial for that, never shadowBias.
            light.shadowNormalBias = 0.30f;
            light.shadowNearPlane = 0.30f;

            // URP refuses this outside play mode ("Cannot modify
            // additionalLightsShadowResolutionTier outside of play mode"),
            // and it throws rather than declining — which takes the whole
            // car build down with it in any edit-mode path. The tier is a
            // shadow-quality nicety; the beam is correct without it.
            if (Application.isPlaying)
            {
                UniversalAdditionalLightData data =
                    light.GetUniversalAdditionalLightData();
                data.usePipelineSettings = false;
                data.additionalLightsShadowResolutionTier =
                    UniversalAdditionalLightData
                        .AdditionalLightsShadowResolutionTierHigh;
            }

            return light;
        }

        private static Light CreateSpill(
            Transform carrier,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            Light light = CreateLight(
                carrier,
                "Headlight Spill",
                worldPosition,
                worldRotation);
            light.range = SpillRange;
            light.spotAngle = SpillSpotAngle;
            light.innerSpotAngle = SpillInnerSpotAngle;
            light.shadows = LightShadows.None;
            return light;
        }

        private static Light CreateLight(
            Transform carrier,
            string name,
            Vector3 worldPosition,
            Quaternion worldRotation)
        {
            var host = new GameObject(name);
            host.transform.SetParent(carrier, false);

            // The world pose is written AFTER parenting on purpose: the
            // carrier's own basis is the imported model's, and it must not
            // be allowed anywhere near the aim.
            host.transform.SetPositionAndRotation(
                worldPosition,
                worldRotation);
            Light light = host.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = BeamColor;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;
            light.enabled = false;
            return light;
        }
    }
}
