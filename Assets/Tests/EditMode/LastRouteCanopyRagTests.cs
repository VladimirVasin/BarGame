using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class LastRouteCanopyRagTests
    {
        private const int Seed = GameSessionState.DefaultCitySeed;

        [Test]
        public void Build_CityIsland_HangsClothRagsFromBrokenCanopy()
        {
            CityLayout layout = CityLayoutGenerator.Generate(
                CityGenerationSettings.Default,
                Seed);
            var parent = new GameObject("Canopy Rag Test");
            try
            {
                int registeredBefore = CityClothWindRegistry.Count;
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder.Build(
                        parent.transform,
                        layout);

                Cloth[] cloths =
                    root.GetComponentsInChildren<Cloth>(true);
                Assert.That(
                    cloths.Length,
                    Is.GreaterThanOrEqualTo(1),
                    "The broken canopy must hang cloth rags.");
                for (int index = 0; index < cloths.Length; index++)
                {
                    Assert.That(
                        cloths[index].name,
                        Does.Contain("Broken Canopy Segment")
                            .And.Contain("Rag"));
                    Assert.That(
                        cloths[index]
                            .GetComponent<SkinnedMeshRenderer>(),
                        Is.Not.Null);
                    Assert.That(
                        cloths[index].transform.localPosition.y,
                        Is.GreaterThan(2f),
                        "Rags hang from the canopy underside.");
                }

                Assert.That(
                    CityClothWindRegistry.Count - registeredBefore,
                    Is.EqualTo(cloths.Length),
                    "Every rag registers for the weather wind.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }

        [Test]
        public void BuildHomeExterior_AddsNoClothComponents()
        {
            HomeExteriorContextPlan context =
                HomeExteriorContextPlanner.Generate(Seed);
            var parent = new GameObject("Canopy Rag Vista Test");
            try
            {
                GameObject root =
                    CityDistrictPointOfInterestWorldBuilder
                        .BuildHomeExterior(parent.transform, context);

                Assert.That(
                    root.GetComponentsInChildren<Cloth>(true),
                    Is.Empty,
                    "The distant vista must stay cloth-free.");
            }
            finally
            {
                Object.DestroyImmediate(parent);
            }
        }
    }
}
