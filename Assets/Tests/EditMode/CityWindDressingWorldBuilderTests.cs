using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class CityWindDressingWorldBuilderTests
    {
        [Test]
        public void Build_DrawsEveryPlannedClothAndRegistersWind()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityBlueprintCatalog.Default,
                CityGenerationSettings.Default,
                GameSessionState.DefaultCitySeed);
            CityMountainBoundaryPlan mountainPlan =
                CityMountainBoundaryPlanner.Create(layout);
            CityWindDressingPlan plan = CityWindDressingPlanner.Create(
                layout,
                CityDecorationPlanner.CreatePlan(
                    layout,
                    RoadFencePlanner.CreatePlan(layout),
                    CityNightFixturePlanner.CreatePlan(layout)),
                CitySeacoastPlanner.Create(layout),
                CityCemeteryPlanner.Create(layout),
                CityFringeYardPlanner.Create(layout, mountainPlan));

            int bodiedPlanned = 0;
            for (int index = 0; index < plan.ClothCount; index++)
            {
                if (plan.Cloths[index].RegisterBody)
                {
                    bodiedPlanned++;
                }
            }

            var parent = new GameObject("Wind Dressing Build Test");
            try
            {
                int windBefore = CityClothWindRegistry.Count;
                int bodyBefore = CityClothBodyRegistry.ClothCount;
                GameObject root = CityWindDressingWorldBuilder.Build(
                    parent.transform,
                    plan);

                Assert.That(
                    root.name,
                    Is.EqualTo(CityWindDressingWorldBuilder.RootName));

                Cloth[] cloths =
                    root.GetComponentsInChildren<Cloth>(true);
                Assert.That(
                    cloths.Length,
                    Is.EqualTo(plan.ClothCount));
                for (int index = 0; index < cloths.Length; index++)
                {
                    Assert.That(
                        cloths[index]
                            .GetComponent<SkinnedMeshRenderer>(),
                        Is.Not.Null);
                    // Simulated cloth is walked through; only the
                    // supports it hangs from may collide.
                    Assert.That(
                        cloths[index].GetComponent<Collider>(),
                        Is.Null);
                }

                Assert.That(
                    CityClothWindRegistry.Count - windBefore,
                    Is.EqualTo(plan.ClothCount),
                    "Every wind-dressing piece rides the weather " +
                    "wind.");
                Assert.That(
                    CityClothBodyRegistry.ClothCount - bodyBefore,
                    Is.EqualTo(bodiedPlanned),
                    "Only body-height wash parts around the hero.");

                if (plan.Supports.Count > 0)
                {
                    Assert.That(
                        root.GetComponentsInChildren<MeshRenderer>(true)
                            .Length,
                        Is.GreaterThan(0),
                        "Planned supports must be drawn.");
                }

                // A planned line pole is a blocking support and its
                // batch carries the collider.
                bool plansPole = false;
                for (int index = 0;
                     index < plan.Supports.Count;
                     index++)
                {
                    if (plan.Supports[index].Kind ==
                        CityWindDressingSupportKind.LinePole)
                    {
                        plansPole = true;
                    }
                }

                if (plansPole)
                {
                    Transform poleBatch = root.transform.Find(
                        "Wind Dressing LinePole");
                    Assert.That(poleBatch, Is.Not.Null);
                    Assert.That(
                        poleBatch.GetComponent<Collider>(),
                        Is.Not.Null,
                        "Line poles block movement.");
                }
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
