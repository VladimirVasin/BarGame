using System.Collections;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        [UnityTest]
        [Explicit("Ordinary keyboard approaches, stops and retreats at the reported rock face. Run alone.")]
        public IEnumerator CityRockfaceContactsStayLocal()
        {
            var input = new InputTestFixture();
            input.Setup();
            bool focus = GameInput.MovementFocused;
            try
            {
                var keyboard = InputSystem.AddDevice<Keyboard>();
                keyboard.MakeCurrent();
                GameInput.HandleApplicationFocus(true);
                GameInput.ReadMovement();
                yield return CaptureCityLocalAcceptance(city => ReviewRockfaceContacts(city, keyboard));
            }
            finally
            {
                input.TearDown();
                GameInput.HandleApplicationFocus(focus);
            }
        }

        private static IEnumerator ReviewRockfaceContacts(CityGameRoot city, Keyboard keyboard)
        {
            float previousStep = Time.captureDeltaTime;
            Time.captureDeltaTime = .02f;
            PlayerMotor motor = city.Player.Motor;
            motor.SetInputEnabled(true);
            try
            {
                var summit = city.World.MountainBoundaryPlan.Ridges.Single(r => r.StableId == "mountain-south-west-before-tunnel");
                const float reportedX = -36.2f;
                float rimZ = summit.Stations.Min(s => s.WorldXZ.y);
                Assert.That(Mathf.Abs(rimZ + 373.7f), Is.LessThan(.5f), "Wrong summit boundary.");
                var ground = CityHillsideGroundPlan.ForLayout(city.Layout);
                int measured = 0;
                foreach (float x in new[] { reportedX, -38f, -42.7f, -47.4f, -51f, -54f, -59.5f, -64f, -69f })
                {
                    Vector2 point = new Vector2(x, rimZ + 1.65f);
                    bool supported = ground.TrySampleTop(point, out float y) &&
                        city.World.WalkableArea.Contains(new Vector3(x, y, point.y), motor.GetComponent<CharacterController>().radius);
                    if (x == reportedX) Assert.That(supported, Is.True, "Missing the reported normal approach.");
                    if (!supported) continue;
                    var start = new Vector3(x, y, point.y);
                    yield return WalkIntoRockface(city, keyboard, start, Vector3.back, "summit-x" + x, measured % 2 == 1);
                    measured++;
                }
                Assert.That(measured, Is.GreaterThanOrEqualTo(7), "The reported summit segment requires multiple real approaches.");

                foreach (var yard in city.World.FringeYardPlan.Yards.Where(y =>
                             y.Kind == CityFringeYardKind.WestStoneTerraces || y.Kind == CityFringeYardKind.WestIndustrialBelt))
                {
                    Vector3 start = yard.Access.Center;
                    start.x = yard.AreaBounds.xMin + 1.65f;
                    start = LocalAcceptanceFloor(city, start, yard.StableId);
                    yield return WalkIntoRockface(city, keyboard, start, Vector3.left, yard.StableId, false);
                    if (yard.Kind == CityFringeYardKind.WestStoneTerraces)
                    {
                        Vector3 edge = LocalAcceptanceFloor(city, start + Vector3.forward * 1.2f, yard.StableId + " near retaining opening");
                        yield return WalkIntoRockface(city, keyboard, edge, Vector3.left, yard.StableId + "-opening-side", true);
                    }
                }

                // Positions rounding to the HUD report can already be a few centimetres
                // inside the radius margin. A previously displaced capsule may be deeper.
                foreach (float clearance in new[] { .30f, .15f })
                {
                    Vector2 point = new Vector2(reportedX, rimZ + clearance);
                    Assert.That(ground.TrySampleTop(point, out float y), Is.True);
                    yield return WalkIntoRockface(city, keyboard, new Vector3(point.x, y, point.y),
                        Vector3.back, "reported-recovery-" + clearance, false, true);
                }
            }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                motor.SetInputEnabled(false);
                Time.captureDeltaTime = previousStep;
            }
        }

        private static IEnumerator WalkIntoRockface(CityGameRoot city, Keyboard keyboard, Vector3 ground,
            Vector3 outward, string site, bool sprint, bool recovery = false)
        {
            var motor = city.Player.Motor;
            var body = motor.GetComponent<CharacterController>();
            motor.Teleport(ground + Vector3.up * PlayerFactory.GroundedRootOffset);
            motor.transform.rotation = Quaternion.LookRotation(outward);
            Physics.SyncTransforms();
            InputSystem.QueueStateEvent(keyboard, new KeyboardState());
            InputSystem.Update();
            GameInput.ReadMovement();
            Vector3 start = motor.transform.position;
            if (recovery)
                Debug.Log($"ROCKFACE RECOVERY BEFORE FIRST UPDATE {site} root={start:F3} radius={body.radius:F3} " +
                    $"allowed={city.World.WalkableArea.Contains(start, body.radius)} " +
                    $"stationaryConstraint={city.World.WalkableArea.Constrain(start, start, body.radius):F3}");
            float furthest = 0f, maxStep = 0f, maxSpeed = 0f, maxRetreat = 0f;
            bool reachedBoundary = false, readForward = false, readReverse = false;
            float beforeReverse = 0f;
            const int release = 80, reverse = 93, finish = 126;
            for (int frame = 0; frame < finish; frame++)
            {
                if (frame == 0 || frame == release || frame == reverse)
                {
                    KeyboardState keys = frame == reverse ? new KeyboardState(Key.S) :
                        frame == release ? new KeyboardState() :
                        sprint ? new KeyboardState(Key.W, Key.LeftShift) : new KeyboardState(Key.W);
                    InputSystem.QueueStateEvent(keyboard, keys);
                    InputSystem.Update();
                }
                Vector3 before = motor.transform.position;
                if (frame == reverse) beforeReverse = Vector3.Dot(before - start, outward);
                bool wasAllowed = city.World.WalkableArea.Contains(before, body.radius);
                yield return null;
                Vector3 after = motor.transform.position;
                Vector3 requested = motor.RequestedPlanarVelocity;
                float signedRequest = Vector3.Dot(requested, outward);
                readForward |= frame < release && signedRequest > 1f;
                readReverse |= frame >= reverse && signedRequest < -.5f;
                var contact = motor.LastContact;
                reachedBoundary |= frame < release && contact.HasWall;
                float progress = Vector3.Dot(after - start, outward);
                float step = Vector3.ProjectOnPlane(after - before, Vector3.up).magnitude;
                maxStep = Mathf.Max(maxStep, step);
                maxSpeed = Mathf.Max(maxSpeed, motor.PlanarVelocity.magnitude);
                if (frame < release)
                {
                    furthest = Mathf.Max(furthest, progress);
                    maxRetreat = Mathf.Max(maxRetreat, furthest - progress);
                }
                if (step > 4.2f * Time.deltaTime + .20f ||
                    (!motor.ExternalPushActive && motor.PlanarVelocity.magnitude > 6f) || maxRetreat > .75f)
                {
                    Vector3 preview = city.World.WalkableArea.Constrain(before, before + requested * Time.deltaTime, body.radius);
                    Assert.Fail($"Rockface contact {site}, frame={frame}, dt={Time.deltaTime:F4}, groundY={ground.y:F3}, " +
                        $"start={start:F3}, before={before:F3}, after={after:F3}, radius={body.radius:F3}, beforeAllowed={wasAllowed}, " +
                        $"step={step:F3}, maxStep={maxStep:F3}, requested={requested:F3}, preview={preview:F3}, " +
                        $"velocity={motor.PlanarVelocity:F3}, push={motor.ExternalPushActive}, retreat={maxRetreat:F3}, " +
                        $"flags={contact.Flags}, normal={contact.Normal:F3}, refused={contact.ConstraintPush:F3}.");
                }
            }
            float escaped = beforeReverse - Vector3.Dot(motor.transform.position - start, outward);
            string message = $"{site} start={start:F3} groundY={ground.y:F3} radius={body.radius:F3} " +
                $"advance={furthest:F3} step={maxStep:F3} speed={maxSpeed:F3} retreat={maxRetreat:F3} escape={escaped:F3}";
            Assert.That(readForward && readReverse, Is.True, "Keyboard movement was not sampled. " + message);
            Assert.That(reachedBoundary, Is.True, "Neither physical contact nor boundary refusal was seen. " + message);
            if (!recovery) Assert.That(furthest, Is.GreaterThan(.35f), "Approach never reached the rock. " + message);
            Assert.That(escaped, Is.GreaterThan(.22f), "Backward input must leave the wall. " + message);
            Debug.Log("ROCKFACE CONTACT " + message);
        }
    }
}
