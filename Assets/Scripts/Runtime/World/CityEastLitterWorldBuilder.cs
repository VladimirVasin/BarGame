using UnityEngine;

namespace BarPromenade
{
    /// <summary>Rigid shared Blender meshes settle on the existing ground, without per-instance mesh copies.</summary>
    public static class CityEastLitterWorldBuilder
    {
        public const string RootName = "Eastern Roadside Litter";

        internal static Transform Build(Transform parent, CityEastExitPlan exit, CityLitterGroundSample sample)
        {
            CityEastLitterPlan plan = CityEastLitterPlan.Create(exit);
            var instancer = new CityLitterInstancer(CityLitterCatalog.Load());
            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            int triangles = 0;
            foreach (CityEastLitterPart part in plan.Parts)
            {
                instancer.Place(root, part.Id, part.Item, part.Position, part.Rotation, part.Scale, sample);
                triangles += part.Item.TriangleCount;
            }
            GameLog.Debug("city", "east_litter_built", GameLog.Field("parts", plan.Parts.Count),
                GameLog.Field("variants", instancer.VariantCount), GameLog.Field("triangles", triangles));
            return root;
        }
    }
}
