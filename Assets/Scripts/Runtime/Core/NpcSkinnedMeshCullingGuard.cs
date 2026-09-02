using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Keeps modular humanoid meshes visible outside their imported bind pose.
    /// NPC models and their animation banks live in separate FBX files, so the
    /// model importer cannot bake the animated poses into each part's bounds.
    /// </summary>
    public static class NpcSkinnedMeshCullingGuard
    {
        public static int EnableDynamicBounds(Transform modelRoot)
        {
            if (modelRoot == null)
            {
                return 0;
            }

            SkinnedMeshRenderer[] renderers = modelRoot
                .GetComponentsInChildren<SkinnedMeshRenderer>(true);
            int configuredCount = 0;
            for (int index = 0; index < renderers.Length; index++)
            {
                SkinnedMeshRenderer renderer = renderers[index];
                if (renderer == null)
                {
                    continue;
                }

                // Unity recalculates the current skinned bounds every frame
                // only under this contract. Otherwise a hand, head or whole
                // modular figure can be culled by its bind-pose box.
                renderer.updateWhenOffscreen = true;
                configuredCount++;
            }

            return configuredCount;
        }
    }
}
