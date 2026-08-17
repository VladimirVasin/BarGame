using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    /// <summary>
    /// The swing under real physics: it has to answer a walker, and it
    /// has to keep going once he is gone.
    /// </summary>
    public sealed class CityPlaygroundSwingPlayModeTests
    {
        private const float WalkSpeed = 2.4f;
        private const int PushSteps = 60;
        private const int ReturnSteps = 300;

        [UnityTest]
        public IEnumerator Swing_TakesAWalkersPushAndSwingsBack()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityDecorationPlan plan = CityDecorationPlanner.CreatePlan(
                layout,
                RoadFencePlanner.CreatePlan(layout),
                CityNightFixturePlanner.CreatePlan(layout));
            var parent = new GameObject("Playground Swing Physics Test");
            try
            {
                GameObject root = CityPlaygroundSwingBuilder.Build(
                    parent.transform,
                    layout,
                    plan,
                    Color.grey,
                    Color.grey);
                Assert.That(root, Is.Not.Null);

                CityPlaygroundSwing swing =
                    root.GetComponentInChildren<CityPlaygroundSwing>();
                Assert.That(swing, Is.Not.Null);

                Vector3 axis = swing.PushAxis;
                Vector3 rest = swing.SeatCenter;

                // A bare walker: the hero's own controller dimensions,
                // stepping straight into the plank.
                var walker = new GameObject("Walker");
                walker.transform.SetParent(parent.transform, false);

                // Placed before the controller exists: a live
                // CharacterController owns its own pose and would drag
                // the transform back, exactly as PlayerMotor.Teleport
                // has to work around.
                walker.transform.position =
                    rest -
                    (axis * 0.95f) -
                    (Vector3.up * CityPlaygroundGeometry.SeatCenterY);
                CharacterController controller =
                    walker.AddComponent<CharacterController>();
                controller.height = 1.7f;
                controller.radius = 0.32f;
                controller.center = new Vector3(0f, 0.85f, 0f);
                controller.minMoveDistance = 0f;

                for (int step = 0; step < PushSteps; step++)
                {
                    controller.Move(axis * (WalkSpeed * Time.fixedDeltaTime));
                    yield return new WaitForFixedUpdate();
                }

                float pushed = Vector3.Dot(swing.SeatCenter - rest, axis);
                Assert.That(
                    swing.ContactCount,
                    Is.GreaterThan(0),
                    "The push volume must see the walker.");
                Assert.That(
                    swing.PushCount,
                    Is.GreaterThan(0),
                    "Walking into the plank must be read as a push.");
                Assert.That(
                    pushed,
                    Is.GreaterThan(0.35f),
                    "Walking into a swing must move it.");
                Assert.That(
                    swing.SeatCenter.y,
                    Is.GreaterThan(rest.y + 0.02f),
                    "A pushed seat rises along its arc.");

                // He steps out of the way; the swing owes the rest to
                // gravity alone.
                controller.enabled = false;
                walker.transform.position =
                    rest - (axis * 8f) - (Vector3.up * 4f);

                bool returned = false;
                for (int step = 0; step < ReturnSteps && !returned; step++)
                {
                    yield return new WaitForFixedUpdate();
                    returned =
                        Vector3.Dot(swing.SeatCenter - rest, axis) <
                        -0.1f;
                }

                Assert.That(
                    returned,
                    Is.True,
                    "A released swing must come back past its rest.");
                Assert.That(
                    float.IsNaN(swing.SeatCenter.x) ||
                    float.IsNaN(swing.SeatCenter.y) ||
                    float.IsNaN(swing.SeatCenter.z),
                    Is.False);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
