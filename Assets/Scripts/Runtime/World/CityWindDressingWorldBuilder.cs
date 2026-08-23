using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Draws the city-wide wind dressing: static rope supports into
    /// the shared batches, and every simulated cloth piece outside
    /// them. Cloth is a SkinnedMeshRenderer and can never join a
    /// combined batch, so this builder lives with the swing seats and
    /// the fountain water in the build order — after every static
    /// dresser, before the world result is handed back.
    /// </summary>
    public static class CityWindDressingWorldBuilder
    {
        public const string RootName = "City Wind Dressing";

        private static readonly Color PoleColor =
            new Color(0.160f, 0.140f, 0.112f, 1f);
        private static readonly Color RopeColor =
            new Color(0.310f, 0.272f, 0.212f, 1f);
        private static readonly Color BattenColor =
            new Color(0.205f, 0.172f, 0.128f, 1f);

        public static GameObject Build(
            Transform parent,
            CityWindDressingPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform root = new GameObject(RootName).transform;
            root.SetParent(parent, false);
            // Descriptors carry world positions; pin the root to the
            // world origin so every local offset passes through.
            root.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            root.localScale = Vector3.one;

            BuildSupports(root, plan);
            BuildCloths(root, plan);
            return root.gameObject;
        }

        /// <summary>
        /// The static poles, rope chords and pin battens. A handful of
        /// boxes city-wide, so one combined batch per support kind is
        /// enough — the 48-metre chunking of the big dressers would
        /// only mint single-box meshes here.
        /// </summary>
        private static void BuildSupports(
            Transform root,
            CityWindDressingPlan plan)
        {
            foreach (CityWindDressingSupportKind kind in
                     (CityWindDressingSupportKind[])Enum.GetValues(
                         typeof(CityWindDressingSupportKind)))
            {
                var boxes = new List<RuntimeOrientedBox>();
                for (int index = 0;
                     index < plan.Supports.Count;
                     index++)
                {
                    if (plan.Supports[index].Kind == kind)
                    {
                        boxes.Add(plan.Supports[index].Box);
                    }
                }

                if (boxes.Count == 0)
                {
                    continue;
                }

                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    $"Wind Dressing {kind}",
                    root,
                    boxes,
                    ResolveSupportColor(kind),
                    CityWindDressingRules.BlocksMovement(kind));
            }
        }

        private static Color ResolveSupportColor(
            CityWindDressingSupportKind kind)
        {
            switch (kind)
            {
                case CityWindDressingSupportKind.LinePole:
                    return PoleColor;
                case CityWindDressingSupportKind.RopeChord:
                    return RopeColor;
                default:
                    return BattenColor;
            }
        }

        private static void BuildCloths(
            Transform root,
            CityWindDressingPlan plan)
        {
            for (int index = 0; index < plan.Cloths.Count; index++)
            {
                CityWindDressingClothDescriptor descriptor =
                    plan.Cloths[index];
                GameObject rag = ClothPanelFactory.CreateHangingRag(
                    $"Wind Dressing {descriptor.StableId}",
                    root,
                    descriptor.Position,
                    descriptor.YawDegrees,
                    descriptor.Width,
                    descriptor.Height,
                    descriptor.Color,
                    descriptor.TornVariant,
                    descriptor.Columns,
                    descriptor.Rows);

                // Rope-width strips stay on the factory's flat colour:
                // at that width the weave is sub-pixel and the sheet
                // would only shimmer.
                if (!descriptor.IsRopeStrip)
                {
                    CityPointOfInterestSurfaceAppearance.ApplyClothPanel(
                        rag.GetComponent<SkinnedMeshRenderer>(),
                        descriptor.Color,
                        descriptor.Width,
                        descriptor.Height);
                }

                Cloth cloth = rag.GetComponent<Cloth>();
                CityClothWindRegistry.Register(cloth);
                if (descriptor.RegisterBody)
                {
                    CityClothBodyRegistry.RegisterCloth(cloth);
                }
            }
        }
    }
}
