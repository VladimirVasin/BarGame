using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityNightGlowRegistryTests
    {
        [TearDown]
        public void RestoreNight()
        {
            CityNightGlowRegistry.SetNightFactor(1f);
        }

        [Test]
        public void RegisteredGlow_DiesByDayAndPrunesDeadRenderers()
        {
            var lit = new Color(1.40f, 0.24f, 0.12f, 1f);
            GameObject box = GameObject.CreatePrimitive(
                PrimitiveType.Cube);
            try
            {
                Renderer renderer = box.GetComponent<Renderer>();
                CityNightGlowRegistry.SetNightFactor(1f);
                CityNightGlowRegistry.Register(renderer, lit);
                AssertBlockColor(renderer, lit);

                CityNightGlowRegistry.SetNightFactor(0f);
                AssertBlockColor(
                    renderer,
                    new Color(
                        lit.r * CityNightGlowRegistry.DeadGlowFraction,
                        lit.g * CityNightGlowRegistry.DeadGlowFraction,
                        lit.b * CityNightGlowRegistry.DeadGlowFraction,
                        lit.a));

                CityNightGlowRegistry.SetNightFactor(0.5f);
                AssertBlockColor(
                    renderer,
                    Color.Lerp(
                        new Color(
                            lit.r *
                            CityNightGlowRegistry.DeadGlowFraction,
                            lit.g *
                            CityNightGlowRegistry.DeadGlowFraction,
                            lit.b *
                            CityNightGlowRegistry.DeadGlowFraction,
                            lit.a),
                        lit,
                        0.5f));

                int registered = CityNightGlowRegistry.Count;
                Object.DestroyImmediate(box);
                box = null;
                CityNightGlowRegistry.SetNightFactor(0.25f);
                Assert.That(
                    CityNightGlowRegistry.Count,
                    Is.EqualTo(registered - 1),
                    "A destroyed renderer must leave the registry.");
            }
            finally
            {
                if (box != null)
                {
                    Object.DestroyImmediate(box);
                }
            }
        }

        private static void AssertBlockColor(
            Renderer renderer,
            Color expected)
        {
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            Color actual = properties.GetColor("_BaseColor");
            Assert.That(actual.r, Is.EqualTo(expected.r).Within(0.0001f));
            Assert.That(actual.g, Is.EqualTo(expected.g).Within(0.0001f));
            Assert.That(actual.b, Is.EqualTo(expected.b).Within(0.0001f));
            Assert.That(actual.a, Is.EqualTo(expected.a).Within(0.0001f));
        }
    }
}
