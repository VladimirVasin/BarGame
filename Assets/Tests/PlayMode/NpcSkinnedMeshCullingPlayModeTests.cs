using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class NpcSkinnedMeshCullingPlayModeTests
    {
        private readonly List<GameObject> instances =
            new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = 0; index < instances.Count; index++)
            {
                if (instances[index] != null)
                {
                    Object.DestroyImmediate(instances[index]);
                }
            }

            instances.Clear();
        }

        [UnityTest]
        public IEnumerator ProviderPrefabs_UpdateEverySkinnedPartOffscreen()
        {
            PrefabCase[] cases = LoadProviderPrefabs();
            for (int caseIndex = 0; caseIndex < cases.Length; caseIndex++)
            {
                PrefabCase current = cases[caseIndex];
                Assert.That(
                    current.Prefab,
                    Is.Not.Null,
                    current.Label + " prefab is missing.");

                GameObject instance = Object.Instantiate(current.Prefab);
                instances.Add(instance);
                instance.SetActive(true);
                yield return null;

                SkinnedMeshRenderer[] renderers = instance
                    .GetComponentsInChildren<SkinnedMeshRenderer>(true);
                Assert.That(
                    renderers.Length,
                    Is.GreaterThan(0),
                    current.Label + " contains no skinned renderers.");

                for (int rendererIndex = 0;
                     rendererIndex < renderers.Length;
                     rendererIndex++)
                {
                    Assert.That(
                        renderers[rendererIndex].updateWhenOffscreen,
                        Is.True,
                        current.Label + " can cull animated part '" +
                        renderers[rendererIndex].name +
                        "' by its bind-pose bounds.");
                }
            }
        }

        private static PrefabCase[] LoadProviderPrefabs()
        {
            MothersHouseMotherProvider mother =
                MothersHouseMotherProvider.Load();
            CityArchShelterResidentProvider shelter =
                CityArchShelterResidentProvider.Load();
            MountainRoadCafeCastProvider cafe =
                MountainRoadCafeCastProvider.Load();
            SupermarketCashierProvider cashier =
                SupermarketCashierProvider.Load();
            BarBartenderProvider bartender = BarBartenderProvider.Load();

            Assert.That(mother, Is.Not.Null, "Mother provider is missing.");
            Assert.That(shelter, Is.Not.Null, "Shelter provider is missing.");
            Assert.That(cafe, Is.Not.Null, "Cafe provider is missing.");
            Assert.That(cashier, Is.Not.Null, "Cashier provider is missing.");
            Assert.That(bartender, Is.Not.Null, "Bartender provider is missing.");

            return new[]
            {
                new PrefabCase(
                    "city pedestrian",
                    CityPedestrianResources.LoadPrefab()),
                new PrefabCase("Mother", mother.StagedPrefab),
                new PrefabCase("shelter resident", shelter.StandingPrefab),
                new PrefabCase("cafe woman", cafe.PairWomanPrefab),
                new PrefabCase("cashier", cashier.CashierPrefab),
                new PrefabCase("bartender", bartender.BartenderPrefab),
                new PrefabCase(
                    "bus driver",
                    CityBusDriverResources.LoadPrefab())
            };
        }

        private readonly struct PrefabCase
        {
            public PrefabCase(string label, GameObject prefab)
            {
                Label = label;
                Prefab = prefab;
            }

            public string Label { get; }
            public GameObject Prefab { get; }
        }
    }
}
