using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The dash the passenger sits in front of: the two things on it he can
    /// touch and the one thing on it that moves by itself.
    ///
    /// Everything is measured off the DRAWN geometry and the runtime root's
    /// own axes, never off the generator's numbers or an imported node - a
    /// redrawn dash moves the contract with it, and an imported node's axes
    /// are the trap this car has been caught by seven times.
    /// </summary>
    public sealed class LastRouteCarDashboardTests
    {
        /// <summary>A seated passenger's knees, above his pelvis anchor.
        /// An open lid has to hang clear of them.</summary>
        private const float KneeClearanceAboveSeat = 0.25f;

        [SetUp]
        public void ResetSession()
        {
            GameSessionState.BeginNewGame();
        }

        [TearDown]
        public void ResetSessionAgain()
        {
            GameSessionState.BeginNewGame();
        }

        private static GameObject BuildCar(
            out LastRouteCarAssetRegistry registry,
            out LastRouteCarDashboard dashboard)
        {
            var parent = new GameObject("Dashboard Test");
            registry = LastRouteCarFactory.Create(
                parent.transform,
                LastRouteCarPlan.At(Vector3.zero, Vector3.forward));
            Assert.That(registry, Is.Not.Null, "The car failed to spawn.");
            Assert.That(registry.IsBound, Is.True);
            dashboard = registry.transform.parent
                .GetComponent<LastRouteCarDashboard>();
            Assert.That(dashboard, Is.Not.Null, "The car has no dash.");
            Assert.That(dashboard.IsInitialized, Is.True);
            return parent;
        }

        private static Renderer FindRenderer(
            LastRouteCarAssetRegistry registry,
            string role)
        {
            foreach (LastRouteCarRendererBinding binding in registry.Bindings)
            {
                if (binding.Role == role)
                {
                    return binding.Renderer;
                }
            }

            return null;
        }

        private static Bounds MeasureBounds(Transform pivot)
        {
            Renderer[] renderers = pivot.GetComponentsInChildren<Renderer>(true);
            Assert.That(renderers.Length, Is.GreaterThan(0));
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++)
            {
                bounds.Encapsulate(renderers[index].bounds);
            }

            return bounds;
        }

        private static Vector3 LowestCorner(Bounds bounds)
        {
            return bounds.min;
        }

        private static Vector3 ResolveFacing(LastRouteCarAssetRegistry registry)
        {
            Vector3 facing = Vector3.ProjectOnPlane(
                registry.SteeringWheelPivot.position -
                registry.DriverSeatAnchor.position,
                Vector3.up);
            Assert.That(facing.sqrMagnitude, Is.GreaterThan(0.0001f));
            return facing.normalized;
        }

        [Test]
        public void Prefab_BindsTheDashAndKeepsTheDialArmed()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDashboard dashboard);
            try
            {
                Assert.That(registry.GloveboxLidPivot, Is.Not.Null);
                Assert.That(registry.RadioPowerKnobPivot, Is.Not.Null);
                Assert.That(registry.RadioTuningKnobPivot, Is.Not.Null);
                Assert.That(registry.RadioNeedlePivot, Is.Not.Null);
                Assert.That(registry.SpeedoNeedlePivot, Is.Not.Null);
                Assert.That(registry.RadioNeedleTravel, Is.GreaterThan(0.05f));
                Assert.That(registry.RadioDialRenderer, Is.Not.Null);

                Material dial = registry.RadioDialRenderer.sharedMaterial;
                Assert.That(
                    dial.IsKeywordEnabled("_EMISSION"),
                    Is.True,
                    "The dial's emission keyword has to stay on, or no " +
                    "property block can ever light it.");
                Assert.That(
                    dial.GetColor("_EmissionColor").maxColorComponent,
                    Is.GreaterThan(0.5f));

                var roles = new HashSet<string>();
                int knobs = 0;
                foreach (LastRouteCarRendererBinding binding in registry.Bindings)
                {
                    roles.Add(binding.Role);
                    if (binding.Role == "radio_knob")
                    {
                        knobs++;
                    }
                }

                Assert.That(roles, Does.Contain(LastRouteCarDashboard.RadioBezelRole));
                Assert.That(roles, Does.Contain("radio_dial"));
                Assert.That(roles, Does.Contain("glovebox_lid"));
                Assert.That(knobs, Is.EqualTo(2));
                Assert.That(dashboard.RadioOn, Is.False, "A new game: off.");
                Assert.That(dashboard.GloveboxOpen, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void GloveboxLid_DropsTowardsTheSitterClearOfHisKneesAndComesBackExactly()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDashboard dashboard);
            try
            {
                Transform car = dashboard.transform;
                Transform lid = registry.GloveboxLidPivot;
                Quaternion closedRotation = lid.localRotation;
                Vector3 closedPosition = lid.localPosition;
                Vector3 closedCentre = MeasureBounds(lid).center;

                dashboard.SetGloveboxOpenness(1f);
                Bounds open = MeasureBounds(lid);
                Vector3 travel = open.center - closedCentre;
                Assert.That(
                    Vector3.Dot(travel, car.up),
                    Is.LessThan(-0.04f),
                    "An open lid hangs DOWN from its hinge.");
                Assert.That(
                    Vector3.Dot(travel, -car.forward),
                    Is.GreaterThan(0.03f),
                    "An open lid drops TOWARDS the sitter, not into the dash.");
                Assert.That(
                    LowestCorner(open).y,
                    Is.GreaterThan(
                        registry.PassengerSeatAnchor.position.y +
                        KneeClearanceAboveSeat),
                    "An open lid must clear a seated passenger's knees.");

                dashboard.SetGloveboxOpenness(0f);
                Assert.That(
                    Quaternion.Angle(closedRotation, lid.localRotation),
                    Is.LessThan(0.01f),
                    "A shut lid returns to the pose it was drawn in.");
                Assert.That(
                    (lid.localPosition - closedPosition).magnitude,
                    Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RadioKnobs_TurnClockwiseForTheSitterAndWrapBackToRest()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDashboard dashboard);
            try
            {
                Transform car = dashboard.transform;
                Transform tuning = registry.RadioTuningKnobPivot;
                Transform power = registry.RadioPowerKnobPivot;
                Quaternion tuningRest = tuning.rotation;
                Quaternion powerRest = power.rotation;
                int startDetent = dashboard.TuningDetent;

                dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                Assert.That(
                    dashboard.TuningDetent,
                    Is.EqualTo(LastRouteCarRadioModel.StepDetent(startDetent)));
                Quaternion delta = tuning.rotation * Quaternion.Inverse(tuningRest);
                delta.ToAngleAxis(out float angle, out Vector3 axis);
                Assert.That(
                    angle,
                    Is.EqualTo(LastRouteCarRadioModel.KnobDegreesPerDetent)
                        .Within(0.05f));
                Assert.That(
                    Vector3.Dot(axis, -car.forward),
                    Is.GreaterThan(0.99f),
                    "A knob turns about an axis pointed at the sitter - " +
                    "clockwise as he sees it.");
                Assert.That(
                    (tuning.position - registry.RadioTuningKnobPivot.position)
                        .magnitude,
                    Is.LessThan(0.0001f));

                for (int step = 1; step < LastRouteCarRadioModel.DetentCount; step++)
                {
                    dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                }

                Assert.That(dashboard.TuningDetent, Is.EqualTo(startDetent));
                Assert.That(
                    Quaternion.Angle(tuningRest, tuning.rotation),
                    Is.LessThan(0.01f),
                    "A full turn of detents brings the knob back exactly.");

                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                Assert.That(dashboard.RadioOn, Is.True);
                Quaternion powerDelta = power.rotation * Quaternion.Inverse(powerRest);
                powerDelta.ToAngleAxis(out float powerAngle, out Vector3 powerAxis);
                Assert.That(
                    powerAngle,
                    Is.EqualTo(LastRouteCarRadioModel.PowerKnobOnDegrees)
                        .Within(0.05f));
                Assert.That(Vector3.Dot(powerAxis, -car.forward), Is.GreaterThan(0.99f));

                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                Assert.That(dashboard.RadioOn, Is.False);
                Assert.That(
                    Quaternion.Angle(powerRest, power.rotation),
                    Is.LessThan(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RadioNeedle_SlidesAlongTheDialTowardsTheDriver()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDashboard dashboard);
            try
            {
                Transform needle = registry.RadioNeedlePivot;
                Bounds dial = registry.RadioDialRenderer.bounds;
                dial.Expand(0.02f);

                // Walk it to detent zero, then to the last one.
                while (dashboard.TuningDetent != 0)
                {
                    dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                }

                Vector3 start = needle.position;
                Assert.That(dial.Contains(start), Is.True, "Needle at the start of the dial.");
                for (int step = 1; step < LastRouteCarRadioModel.DetentCount; step++)
                {
                    dashboard.Operate(LastRouteCarDashboardTarget.RadioTuning);
                    Assert.That(
                        dial.Contains(needle.position),
                        Is.True,
                        $"Needle stays on the dial at detent {step}.");
                }

                Vector3 travel = needle.position - start;
                Assert.That(
                    travel.magnitude,
                    Is.EqualTo(registry.RadioNeedleTravel).Within(0.001f));
                Assert.That(
                    Vector3.Dot(travel.normalized, dashboard.TowardsDriver),
                    Is.GreaterThan(0.999f),
                    "The needle runs from the passenger's end towards the driver's.");
                Assert.That(
                    Vector3.Dot(
                        dashboard.TowardsDriver,
                        (registry.DriverSeatAnchor.position -
                         registry.PassengerSeatAnchor.position).normalized),
                    Is.GreaterThan(0.9f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void RadioDial_LightsOnlyWhileTheRadioIsOn()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDashboard dashboard);
            try
            {
                Assert.That(
                    dashboard.ReadDialEmission().maxColorComponent,
                    Is.LessThan(0.001f),
                    "Off: the dial is dark.");
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                Assert.That(
                    dashboard.ReadDialEmission().maxColorComponent,
                    Is.GreaterThan(0.5f),
                    "On: the dial glows.");
                dashboard.Operate(LastRouteCarDashboardTarget.RadioPower);
                Assert.That(
                    dashboard.ReadDialEmission().maxColorComponent,
                    Is.LessThan(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SpeedoNeedle_SweepsClockwiseForTheDriverWithSpeed()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDashboard dashboard);
            try
            {
                Transform car = dashboard.transform;
                Transform needle = registry.SpeedoNeedlePivot;
                Quaternion rest = needle.rotation;
                float previous = 0f;
                foreach (float value in new[] { 0.25f, 0.5f, 0.75f, 1f })
                {
                    dashboard.SetSpeedometer01(value);
                    Quaternion delta = needle.rotation * Quaternion.Inverse(rest);
                    delta.ToAngleAxis(out float angle, out Vector3 axis);
                    Assert.That(
                        angle,
                        Is.EqualTo(LastRouteCarRadioModel.SpeedoSweepDegrees * value)
                            .Within(0.05f));
                    Assert.That(angle, Is.GreaterThan(previous));
                    Assert.That(Vector3.Dot(axis, -car.forward), Is.GreaterThan(0.99f));
                    previous = angle;
                }

                dashboard.SetSpeedometer01(0f);
                Assert.That(Quaternion.Angle(rest, needle.rotation), Is.LessThan(0.01f));
                Assert.That(LastRouteCarRadioModel.Speedometer01(float.NaN), Is.Zero);
                Assert.That(
                    LastRouteCarRadioModel.Speedometer01(
                        LastRouteCarRadioModel.SpeedoFullScaleSpeed * 2f),
                    Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Gaze_PicksTheTwoKnobsAndTheLidFromThePassengersEye()
        {
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDashboard dashboard);
            try
            {
                Vector3 facing = ResolveFacing(registry);
                LastRouteCarSeatViewPlan.EvaluateCamera(
                    registry.PassengerSeatAnchor.position,
                    facing,
                    0f,
                    0f,
                    out Vector3 eye,
                    out Quaternion level);

                Renderer bezel = FindRenderer(registry, LastRouteCarDashboard.RadioBezelRole);
                Assert.That(bezel, Is.Not.Null);
                Vector3 radioCentre = bezel.bounds.center;
                Vector3 towardsDriver = dashboard.TowardsDriver;
                Vector3 lidCentre = MeasureBounds(registry.GloveboxLidPivot).center;

                Assert.That(
                    Resolve(dashboard, eye, radioCentre + (towardsDriver * 0.06f)),
                    Is.EqualTo(LastRouteCarDashboardTarget.RadioPower),
                    "The driver's half of the radio is the power knob.");
                Assert.That(
                    Resolve(dashboard, eye, radioCentre - (towardsDriver * 0.06f)),
                    Is.EqualTo(LastRouteCarDashboardTarget.RadioTuning),
                    "The passenger's half is the tuning knob.");
                Assert.That(
                    Resolve(dashboard, eye, lidCentre),
                    Is.EqualTo(LastRouteCarDashboardTarget.Glovebox));
                Assert.That(
                    dashboard.TryResolveGazeTarget(
                        new Ray(eye, level * Vector3.forward),
                        out LastRouteCarDashboardTarget ahead),
                    Is.False,
                    "Looking out of the windscreen is looking at nothing.");
                Assert.That(ahead, Is.EqualTo(LastRouteCarDashboardTarget.None));

                // And the seat's own look limits reach both: the lid is the
                // lowest thing he has to look at, the radio the furthest
                // round towards the driver.
                Vector3 toLid = Quaternion.Inverse(level) * (lidCentre - eye);
                float lidPitch = -Mathf.Asin(toLid.normalized.y) * Mathf.Rad2Deg;
                Assert.That(
                    lidPitch,
                    Is.LessThan(LastRouteCarSeatViewPlan.MaximumPitchDegrees),
                    "He must be able to look down far enough to see his own glovebox.");
                Vector3 toRadio = Quaternion.Inverse(level) * (radioCentre - eye);
                float radioYaw = Mathf.Abs(
                    Mathf.Atan2(toRadio.x, toRadio.z) * Mathf.Rad2Deg);
                Assert.That(
                    radioYaw,
                    Is.LessThan(LastRouteCarSeatViewPlan.MaximumYawOffsetDegrees));

                dashboard.SetGloveboxOpenness(1f);
                Vector3 openLidCentre = MeasureBounds(registry.GloveboxLidPivot).center;
                Assert.That(
                    Resolve(dashboard, eye, openLidCentre),
                    Is.EqualTo(LastRouteCarDashboardTarget.Glovebox),
                    "An open lid is looked at where it hangs.");
                Vector3 toOpenLid = Quaternion.Inverse(level) * (openLidCentre - eye);
                Assert.That(
                    -Mathf.Asin(toOpenLid.normalized.y) * Mathf.Rad2Deg,
                    Is.LessThan(LastRouteCarSeatViewPlan.MaximumPitchDegrees + 3f),
                    "The open lid hangs within a few degrees of the look limit.");
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void PromptKeys_FollowWhatHeIsLookingAtAndWhatItAlreadyIs()
        {
            Assert.That(
                LastRouteCarDashboard.ResolvePromptKey(
                    LastRouteCarDashboardTarget.RadioPower, false, false),
                Is.EqualTo(LastRouteCarDashboard.RadioOnPromptKey));
            Assert.That(
                LastRouteCarDashboard.ResolvePromptKey(
                    LastRouteCarDashboardTarget.RadioPower, true, false),
                Is.EqualTo(LastRouteCarDashboard.RadioOffPromptKey));
            Assert.That(
                LastRouteCarDashboard.ResolvePromptKey(
                    LastRouteCarDashboardTarget.RadioTuning, true, true),
                Is.EqualTo(LastRouteCarDashboard.RadioTunePromptKey));
            Assert.That(
                LastRouteCarDashboard.ResolvePromptKey(
                    LastRouteCarDashboardTarget.Glovebox, false, false),
                Is.EqualTo(LastRouteCarDashboard.OpenGloveboxPromptKey));
            Assert.That(
                LastRouteCarDashboard.ResolvePromptKey(
                    LastRouteCarDashboardTarget.Glovebox, false, true),
                Is.EqualTo(LastRouteCarDashboard.CloseGloveboxPromptKey));
            Assert.That(
                LastRouteCarDashboard.ResolvePromptKey(
                    LastRouteCarDashboardTarget.None, true, true),
                Is.Null);
        }

        [Test]
        public void Session_RestoresTheDashOnTheNextCar()
        {
            GameSessionState.SetCarDashboard(
                new LastRouteCarDashboardState(true, 5, true));
            GameObject root = BuildCar(
                out LastRouteCarAssetRegistry registry,
                out LastRouteCarDashboard dashboard);
            try
            {
                Assert.That(dashboard.RadioOn, Is.True, "The radio stayed on through the tunnel.");
                Assert.That(dashboard.TuningDetent, Is.EqualTo(5));
                Assert.That(dashboard.GloveboxOpen, Is.True);
                Assert.That(dashboard.GloveboxOpenness, Is.EqualTo(1f));
                Assert.That(dashboard.IsGloveboxSwinging, Is.False, "Restored, not replayed.");
                Assert.That(dashboard.ReadDialEmission().maxColorComponent, Is.GreaterThan(0.5f));

                dashboard.Operate(LastRouteCarDashboardTarget.Glovebox);
                Assert.That(GameSessionState.CarDashboard.GloveboxOpen, Is.False);
                Assert.That(GameSessionState.CarDashboard.RadioOn, Is.True);
                Assert.That(dashboard.IsGloveboxSwinging, Is.True);

                GameSessionState.BeginNewGame();
                Assert.That(GameSessionState.CarDashboard.RadioOn, Is.False);
                Assert.That(
                    GameSessionState.CarDashboard.TuningDetent,
                    Is.EqualTo(LastRouteCarRadioModel.DefaultDetent));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static LastRouteCarDashboardTarget Resolve(
            LastRouteCarDashboard dashboard,
            Vector3 eye,
            Vector3 point)
        {
            dashboard.TryResolveGazeTarget(
                new Ray(eye, (point - eye).normalized),
                out LastRouteCarDashboardTarget target);
            return target;
        }
    }
}
