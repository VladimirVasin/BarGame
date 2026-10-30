using System;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class StairwellAuthoredAssetsSetup : IPrebuildSetup
    {
        public void Setup()
        {
#if UNITY_EDITOR
            Type.GetType("BarPromenade.Editor.StairwellInteriorModelAssetSetup, BarPromenade.Editor", true)
                .GetMethod("EnsureAssets", Type.EmptyTypes).Invoke(null, null);
            Type.GetType("BarPromenade.Editor.StairwellLabyrinthAssetSetup, BarPromenade.Editor", true)
                .GetMethod("EnsureAssets", Type.EmptyTypes).Invoke(null, null);
#endif
        }
    }
}
