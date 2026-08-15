using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class SupermarketInteriorAtmosphereTests
    {
        [Test]
        public void Install_KeepsTheShadowlessSixLightBudget()
        {
            SupermarketInteriorLayoutPlan plan =
                SupermarketInteriorLayoutPlanner.Generate(20260815);
            var parent = new GameObject("Atmosphere Test Root");
            try
            {
                SupermarketInteriorAtmosphere atmosphere =
                    SupermarketInteriorAtmosphere.Install(
                        parent.transform,
                        plan);

                Light[] lights =
                    parent.GetComponentsInChildren<Light>(true);
                Assert.That(
                    lights,
                    Has.Length.EqualTo(
                        SupermarketInteriorAtmosphere
                            .PracticalLightCount));
                for (int index = 0; index < lights.Length; index++)
                {
                    Assert.That(
                        lights[index].type,
                        Is.Not.EqualTo(LightType.Directional),
                        "Practicals must never add a second sun.");
                    Assert.That(
                        lights[index].shadows,
                        Is.EqualTo(LightShadows.None),
                        "The scene owns exactly one shadow caster: " +
                        "the directional key.");
                    Assert.That(
                        lights[index].transform.position.y,
                        Is.LessThan(plan.RoomHeight));
                }

                Assert.That(atmosphere.Flicker, Is.Not.Null);
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void Flicker_DipsAndRecoversDeterministically()
        {
            var holder = new GameObject("Flicker Test Light");
            try
            {
                Light light = holder.AddComponent<Light>();
                light.type = LightType.Point;
                light.intensity = 1f;
                var flicker = holder
                    .AddComponent<SupermarketFluorescentFlicker>();
                flicker.Initialize(light, null, Color.white);

                float minimum = float.MaxValue;
                float maximum = float.MinValue;
                float step =
                    SupermarketFluorescentFlicker.StepSeconds;
                for (int index = 0; index < 40; index++)
                {
                    flicker.Advance(step);
                    minimum = Mathf.Min(
                        minimum,
                        flicker.CurrentMultiplier);
                    maximum = Mathf.Max(
                        maximum,
                        flicker.CurrentMultiplier);
                    Assert.That(
                        flicker.CurrentMultiplier,
                        Is.InRange(
                            SupermarketFluorescentFlicker
                                .MinimumMultiplier,
                            1f));
                }

                Assert.That(
                    minimum,
                    Is.LessThan(0.9f),
                    "The tired ballast must visibly dip.");
                Assert.That(
                    maximum,
                    Is.EqualTo(1f),
                    "The row must spend most of its time fully lit.");
            }
            finally
            {
                Object.DestroyImmediate(holder);
            }
        }
    }
}
