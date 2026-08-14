using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class PipebackRollerContentProbeTests
    {
        [Test]
        public void Probe_ReportsWhatTheStagedPrefabActuallyContains()
        {
            YardWheelchairProvider provider =
                YardWheelchairProvider.Load();
            Assert.That(provider, Is.Not.Null);
            Assert.That(provider.StagedPrefab, Is.Not.Null);

            GameObject instance =
                Object.Instantiate(provider.StagedPrefab);
            try
            {
                Renderer[] renderers =
                    instance.GetComponentsInChildren<Renderer>(true);
                Debug.Log(
                    $"[pipeback] renderer count={renderers.Length}");

                string[] chairWords =
                {
                    "Wheel", "Caster", "Chair", "Frame", "Armrest",
                    "Backrest", "Seat", "Bellows", "Pipe", "Footplate",
                    "Rim", "Axle"
                };
                Renderer[] chairParts = renderers
                    .Where(item => chairWords.Any(word =>
                        item.name.Contains(word)))
                    .ToArray();
                Debug.Log(
                    $"[pipeback] chair-like renderers={chairParts.Length}");
                foreach (Renderer part in chairParts.Take(20))
                {
                    Debug.Log(
                        $"[pipeback] chair part '{part.name}' " +
                        $"enabled={part.enabled} " +
                        $"active={part.gameObject.activeInHierarchy} " +
                        $"bounds={part.bounds.size}");
                }

                foreach (Renderer part in renderers.Take(60))
                {
                    Debug.Log($"[pipeback] renderer '{part.name}'");
                }

                var registry = instance
                    .GetComponentInChildren<
                        CityWheelchairNpcAssetRegistry>(true);
                Debug.Log(
                    $"[pipeback] pivots wheelL={registry.LeftWheelPivot?.name} " +
                    $"wheelR={registry.RightWheelPivot?.name} " +
                    $"casterL={registry.LeftCasterPivot?.name} " +
                    $"bellows={registry.BellowsPivot?.name} " +
                    $"pipeBank={registry.PipeBankPivot?.name}");

                Bounds total = renderers[0].bounds;
                foreach (Renderer part in renderers)
                {
                    total.Encapsulate(part.bounds);
                }

                Debug.Log(
                    $"[pipeback] total bounds center={total.center} " +
                    $"size={total.size}");
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }

            Assert.Pass();
        }
    }
}
