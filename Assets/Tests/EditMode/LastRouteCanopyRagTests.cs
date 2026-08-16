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
                int canopyRagCount = 0;
                int carpetCount = 0;
                for (int index = 0; index < cloths.Length; index++)
                {
                    Assert.That(
                        cloths[index]
                            .GetComponent<SkinnedMeshRenderer>(),
                        Is.Not.Null);
                    // The city hangs two cloth families: the island's
                    // torn canopy rags and the drying yard's pinned
                    // laundry.
                    bool isCanopyRag =
                        cloths[index].name.Contains(
                            "Broken Canopy Segment") &&
                        cloths[index].name.Contains("Rag");
                    if (!isCanopyRag)
                    {
                        Assert.That(
                            cloths[index].transform.parent.name,
                            Is.EqualTo("Residential Drying Yard"),
                            "Unexpected cloth outside the canopy and " +
                            "the drying yard.");
                        if (cloths[index].name.StartsWith(
                                "Beaten Carpet"))
                        {
                            carpetCount++;
                        }

                        continue;
                    }

                    canopyRagCount++;
                    Assert.That(
                        cloths[index].transform.localPosition.y,
                        Is.GreaterThan(2f),
                        "Rags hang from the canopy underside.");
                }

                Assert.That(
                    canopyRagCount,
                    Is.GreaterThanOrEqualTo(1),
                    "The broken canopy must hang cloth rags.");

                // The heavy hung carpets are simulated but deliberately
                // outside the weather wind; everything else registers.
                Assert.That(
                    CityClothWindRegistry.Count - registeredBefore,
                    Is.EqualTo(cloths.Length - carpetCount),
                    "Every rag and laundry piece registers for the " +
                    "weather wind.");
                Assert.That(carpetCount, Is.EqualTo(2));
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
