using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    /// <summary>
    /// The menu warm-up derives its path list from the same catalog the
    /// roots load from: the hero first, then every distinct archetype the
    /// City pedestrian pool resolves, each naming a prefab under Resources.
    /// </summary>
    public sealed class AreaAssetWarmupTests
    {
        [Test]
        public void MenuWarmupPaths_CoverTheHeroAndEveryPooledArchetypeOnce()
        {
            IReadOnlyList<string> paths =
                AreaAssetWarmup.CollectMenuWarmupPaths();

            Assert.That(paths, Is.Not.Empty);
            Assert.That(paths, Is.Unique);
            Assert.That(
                paths[0],
                Is.EqualTo(Player3DResources.PrefabResourcePath),
                "The hero prefab is warmed first.");

            IEnumerable<string> pooled = CityPedestrianResources
                .CreatePoolComposition(
                    CityPedestrianPopulationProfile.City.PoolSize)
                .Select(archetype => archetype.PrefabResourcePath)
                .Distinct();
            Assert.That(
                paths.Skip(1),
                Is.EquivalentTo(pooled),
                "Every pooled archetype prefab is warmed exactly once.");

            foreach (string path in paths)
            {
                string assetPath = Path.Combine(
                    Application.dataPath,
                    "Resources",
                    path + ".prefab");
                Assert.That(
                    File.Exists(assetPath),
                    Is.True,
                    $"Resources/{path}.prefab must exist for the warm-up to " +
                    "load what the roots load.");
            }
        }
    }
}
