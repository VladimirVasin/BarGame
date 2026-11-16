using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The menu warm-up derives its path list from the same catalog the
    /// roots load from, with the hero and Home models ahead of City actors.
    /// </summary>
    public sealed class AreaAssetWarmupTests
    {
        [Test]
        public void MenuWarmupPaths_CoverHomeAndCityAssetsOnce()
        {
            IReadOnlyList<string> paths =
                AreaAssetWarmup.CollectMenuWarmupPaths();

            Assert.That(paths, Is.Not.Empty);
            Assert.That(paths, Is.Unique);
            Assert.That(
                paths[0],
                Is.EqualTo(Player3DResources.PrefabResourcePath),
                "The hero prefab is warmed first.");
            Assert.That(paths[1], Is.EqualTo(HomeInteriorModelLibrary.ResourcePath));
            Assert.That(paths[paths.Count - 1], Is.EqualTo(CityBusResources.PrefabResourcePath));

            IEnumerable<string> pooled = CityPedestrianResources
                .CreatePoolComposition(
                    CityPedestrianPopulationProfile.City.PoolSize)
                .Select(archetype => archetype.PrefabResourcePath)
                .Distinct();
            Assert.That(
                paths.Skip(2).Take(paths.Count - 3),
                Is.EquivalentTo(pooled),
                "Every pooled archetype prefab is warmed exactly once.");

            foreach (string path in paths)
            {
                string extension = path == HomeInteriorModelLibrary.ResourcePath ? ".asset" : ".prefab";
                string assetPath = Path.Combine(
                    Application.dataPath,
                    "Resources",
                    path + extension);
                Assert.That(
                    File.Exists(assetPath),
                    Is.True,
                    $"Resources/{path}{extension} must exist for the warm-up to " +
                    "load what the roots load.");
            }
        }
    }
}
