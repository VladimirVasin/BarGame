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
        [Explicit("Grounded views and real keyboard contacts through the west/south talus transition. Run alone.")]
        [PrebuildSetup(typeof(CityRockfaceAssetsSetup))]
        public IEnumerator CityMountainTalusTransitions()
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
                yield return CaptureCityLocalAcceptance(city => ReviewMountainTalus(city, keyboard));
#if UNITY_EDITOR
                System.Type.GetType("BarPromenade.Editor.CityMapArtReferenceExport, BarPromenade.Editor", true)
                    .GetMethod("ExportCurrent", System.Type.EmptyTypes).Invoke(null, null);
#endif
            }
            finally
            {
                input.TearDown();
                GameInput.HandleApplicationFocus(focus);
            }
        }

        private static IEnumerator ReviewMountainTalus(CityGameRoot city, Keyboard keyboard)
        {
            var ground = CityHillsideGroundPlan.ForLayout(city.Layout);
            var talus = CityMountainTalusPlan.ForLayout(city.Layout);
            var summit = city.World.MountainBoundaryPlan.Ridges.Single(r =>
                r.StableId == "mountain-south-west-before-tunnel");
            float rimZ = summit.Stations.Min(s => s.WorldXZ.y);
            float minInfluence = 1f, maxInfluence = 0f, maxRise = 0f;
            int physicalSamples = 0;
            foreach (float x in new[] { -36.2f, -39.5f, -44f, -50f, -58f, -65f })
            foreach (float setback in new[] { .45f, .9f, 1.6f, 2.4f, 3.4f, 4.5f })
            {
                Vector2 point = new Vector2(x, rimZ + setback);
                Assert.That(ground.TrySampleTop(point, out float expected), Is.True,
                    "Summit talus lost its ground at " + point);
                float influence = talus.Influence(point);
                minInfluence = Mathf.Min(minInfluence, influence);
                maxInfluence = Mathf.Max(maxInfluence, influence);
                maxRise = Mathf.Max(maxRise, talus.ApplyHeight(point, 0f));
                var hits = Physics.RaycastAll(new Vector3(point.x, expected + 2f, point.y),
                    Vector3.down, 4f, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
                bool matched = hits.Any(hit => hit.collider is MeshCollider &&
                    hit.collider.name == "Terraced ground and courts" &&
                    Mathf.Abs(hit.point.y - expected) < .035f);
                Assert.That(matched, Is.True, "Talus sampling and physical terrain disagree at " + point +
                    " expectedY=" + expected + " hits=" + string.Join(", ", hits.Select(hit =>
                        hit.collider.name + "@" + hit.point.y.ToString("F3"))));
                physicalSamples++;
            }
            Assert.That(maxRise, Is.GreaterThan(.15f), "The summit must have a raised talus toe.");
            Assert.That(maxInfluence, Is.GreaterThan(.4f), "The toe must carry mountain material.");
            Assert.That(maxInfluence - minInfluence, Is.GreaterThan(.3f),
                "Material coverage must fade into the existing soil.");
            Debug.Log($"MOUNTAIN TALUS physical ground samples={physicalSamples}, rise={maxRise:F3}, " +
                $"materialRange={minInfluence:F3}..{maxInfluence:F3}.");

            var reported = new Vector2(-39.5f, -370.8f);
            Assert.That(ground.TrySampleTop(reported, out float reportY), Is.True);
            Vector3 eye = new Vector3(reported.x, reportY + EyeHeight, reported.y);
            city.Player.Motor.Teleport(eye - Vector3.up * (EyeHeight - PlayerFactory.GroundedRootOffset));
            yield return null;
            yield return LocalAcceptanceFrame(city, "talus-reported-along-wall", eye,
                new Vector3(reported.x + 18f, reportY + 1.9f, rimZ - 1f));
            yield return LocalAcceptanceFrame(city, "talus-reported-ground", eye,
                new Vector3(reported.x - 3f, reportY + .25f, rimZ + .7f));
            yield return LocalAcceptanceFrame(city, "talus-reported-reverse", eye,
                new Vector3(reported.x - 20f, reportY + 1.1f, rimZ - .6f));

            var west = city.World.FringeYardPlan.Yards.Single(y =>
                y.Kind == CityFringeYardKind.WestStoneTerraces);
            Vector3 westToe = west.Access.Center;
            westToe.x = west.AreaBounds.xMin;
            Vector3 westEye = SupportedRockfaceEye(city, westToe, Vector3.left, 4f);
            westToe.y = westEye.y - EyeHeight;
            yield return LocalAcceptanceFrame(city, "talus-west-toe", westEye,
                westToe + Vector3.forward * 8f + Vector3.up * .7f);

            var east = city.World.MountainBoundaryPlan.Ridges.Single(r =>
                r.StableId == "mountain-south-east-outer-rim").Stations
                .Where(s => s.OutwardNormal.z < -.97f)
                .OrderBy(s => Mathf.Abs(s.WorldXZ.x - 59f) + Mathf.Abs(s.WorldXZ.y - rimZ)).First();
            Vector3 eastEye = SupportedRockfaceEye(city, east.Toe, east.OutwardNormal, 4f);
            yield return LocalAcceptanceFrame(city, "talus-east-upper-rim", eastEye,
                east.Toe + Vector3.left * 9f + Vector3.up * .8f);

            yield return ReviewRockfaceContacts(city, keyboard);
            yield return WalkTalusBand(city, keyboard, reported, Vector3.left, "along summit wall", true);
            yield return WalkTalusBand(city, keyboard, reported, new Vector3(-.45f, 0f, -1f).normalized,
                "diagonal summit approach", false);
        }

        private static IEnumerator WalkTalusBand(CityGameRoot city, Keyboard keyboard, Vector2 origin,
            Vector3 direction, string label, bool requireTravel)
        {
            float previousStep = Time.captureDeltaTime;
            var motor = city.Player.Motor;
            var ground = CityHillsideGroundPlan.ForLayout(city.Layout);
            try
            {
                Time.captureDeltaTime = .02f;
                Assert.That(ground.TrySampleTop(origin, out float y), Is.True, label);
                Vector3 start = new Vector3(origin.x, y + PlayerFactory.GroundedRootOffset, origin.y);
                motor.Teleport(start);
                motor.transform.rotation = Quaternion.LookRotation(direction);
                motor.SetInputEnabled(true);
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                GameInput.ReadMovement();
                Physics.SyncTransforms();
                InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.W));
                InputSystem.Update();
                for (int frame = 0; frame < 120; frame++)
                {
                    Vector3 before = motor.transform.position;
                    yield return null;
                    Vector3 after = motor.transform.position;
                    Assert.That(Vector3.ProjectOnPlane(after - before, Vector3.up).magnitude,
                        Is.LessThan(4.2f * Time.deltaTime + .2f), label + " jumped horizontally at " + after);
                    Assert.That(motor.PlanarVelocity.magnitude, Is.LessThanOrEqualTo(6f), label + " gained contact momentum.");
                    Assert.That(Mathf.Abs(after.y - before.y), Is.LessThan(.35f), label + " jumped vertically at " + after);
                }
                if (requireTravel)
                    Assert.That(Vector3.ProjectOnPlane(motor.transform.position - start, Vector3.up).magnitude,
                        Is.GreaterThan(1f), label + " has no usable passage.");
                Debug.Log($"MOUNTAIN TALUS WALK {label}, start={start:F3}, finish={motor.transform.position:F3}.");
            }
            finally
            {
                InputSystem.QueueStateEvent(keyboard, new KeyboardState());
                InputSystem.Update();
                motor.SetInputEnabled(false);
                Time.captureDeltaTime = previousStep;
            }
        }
    }
}
