using System;
using UnityEngine;

namespace BarPromenade
{
    internal static class StairwellRailFinish
    {
        private static Material shared;
        private static Texture2D wood, steel;
        private static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();

        internal static void Apply(Renderer renderer, bool timber, bool alongZ)
        {
            if (shared == null) shared = Resources.Load<Material>("WorldFinishes/RailContact");
            ref Texture2D chart = ref (timber ? ref wood : ref steel);
            if (chart == null) chart = Resources.Load<Texture2D>(timber ? "WorldFinishes/RailWood" : "WorldFinishes/RailSupports");
            if (chart == null || shared == null) throw new InvalidOperationException("Missing stair rail finish.");
            Bounds bounds = renderer.localBounds;
            int axis = alongZ ? 2 : 0;
            var along = Vector4.zero;
            along[axis] = 1f / bounds.size[axis];
            along.w = -bounds.min[axis] * along[axis];
            renderer.GetPropertyBlock(Block);
            Block.SetVector("_ContactFinishU", along);
            Block.SetVector("_ContactFinishV", new Vector4(0f, 1f / bounds.size.y, 0f, -bounds.min.y / bounds.size.y));
            Block.SetTexture("_ContactFinishMap", chart);
            Block.SetColor("_ContactSubstrate", timber ? new Color(.33f,.23f,.14f) : new Color(.44f,.43f,.40f));
            renderer.sharedMaterial = shared;
            renderer.SetPropertyBlock(Block);
            Block.Clear();
        }
    }
}
