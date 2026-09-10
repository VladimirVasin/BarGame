using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the village's conifers out of the mountain road's own geometry:
    /// the same two-cone crown generator, the same trunk boxes, the same
    /// foliage material and wind shader. Three renderers for the whole village.
    ///
    /// Nothing here carries a collider. The copse's trunks stop the hero, but
    /// they stop him through the walkable mask - see
    /// <c>AlpineVillageWalkableArea.BuildCopseTrunks</c> for why physics would
    /// be the wrong instrument.
    /// </summary>
    internal static class AlpineVillageTreeBuilder
    {
        public const string RootName = "Village Conifers";
        public const string CrownsName = "Village Conifer Crowns";
        public const string TrunksName = "Village Conifer Trunks";
        public const string StumpsName = "Village Cut Stumps";

        /// <summary>The mountain road's own bark, copied so the two areas cannot drift.</summary>
        private static readonly Color TrunkColor =
            new Color(0.19f, 0.165f, 0.135f, 1f);

        /// <summary>
        /// The road's `Mid` crown tint. The village sits above that layer, so
        /// it borrows the middle value rather than the near one.
        /// </summary>
        private static readonly Color CrownColor =
            new Color(0.095f, 0.14f, 0.115f, 1f);

        public static void Build(Transform parent, AlpineVillagePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            AlpineVillageTreePlan trees = plan.Trees;
            if (trees == null)
            {
                return;
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);

            var crowned = new List<MountainRoadForestDescriptor>();
            foreach (MountainRoadForestDescriptor tree in trees.CrownedTrees)
            {
                crowned.Add(tree);
            }

            BuildCrowns(root.transform, crowned);
            BuildTrunks(root.transform, TrunksName, crowned, 0.32f);
            BuildTrunks(root.transform, StumpsName, trees.Stumps, 1f);
        }

        private static void BuildCrowns(
            Transform parent, IReadOnlyList<MountainRoadForestDescriptor> trees)
        {
            if (trees.Count == 0)
            {
                return;
            }

            Mesh mesh = MountainRoadSceneryMeshFactory.CreateConiferCrowns(
                CrownsName, trees);
            var host = new GameObject(CrownsName);
            host.transform.SetParent(parent, false);
            host.AddComponent<MeshFilter>().sharedMesh = mesh;

            // The factory hands back a bare mesh - on the road the owner comes
            // from a private helper - so the village attaches it itself, the
            // way it already does for its ground, lane and snow meshes.
            host.AddComponent<RuntimeGeneratedMeshOwner>().Initialize(mesh);
            MeshRenderer renderer = host.AddComponent<MeshRenderer>();

            // Shadows off. The precedent is not the road, whose crowns cast
            // because the car's headlights sweep them, but the rock panels
            // standing on this same wall: the rise itself has no ShadowCaster
            // pass, and a tree throwing a long shadow across a village a
            // sixty-metre wall cannot shade would read as a mistake. It also
            // means the shader's fourth, shadow-side wind pass never runs, so
            // crown and shadow cannot disagree about the gust.
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            MountainRoadSurfaceAppearance.ApplyCombined(
                renderer,
                MountainRoadSurfaceKind.ConiferNeedles,
                CrownColor,
                MountainRoadSurfaceAppearance.FoliageMaterial);
        }

        private static void BuildTrunks(
            Transform parent,
            string name,
            IReadOnlyList<MountainRoadForestDescriptor> trees,
            float heightFraction)
        {
            if (trees.Count == 0)
            {
                // The batching factory throws on an empty list rather than
                // returning an empty object, so an absent group is skipped.
                return;
            }

            var boxes = new List<RuntimeOrientedBox>(trees.Count);
            for (int index = 0; index < trees.Count; index++)
            {
                MountainRoadForestDescriptor tree = trees[index];
                float height = tree.Height * heightFraction;
                float radius = heightFraction < 1f
                    ? tree.TrunkRadius
                    : tree.CrownRadius;
                boxes.Add(new RuntimeOrientedBox(
                    tree.Position + Vector3.up * (height * 0.5f),
                    Quaternion.Euler(0f, tree.YawDegrees, 0f),
                    new Vector3(radius * 2f, height, radius * 2f)));
            }

            float metersPerTile = MountainRoadSurfaceAppearance.GetRecipe(
                MountainRoadSurfaceKind.BarkAndDeadwood).MetersPerTile;
            GameObject batch = RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                name,
                parent,
                boxes,
                TrunkColor,
                false,
                metersPerTile,
                RuntimeWorldUvMode.BoxProjected);
            var renderer = batch.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            MountainRoadSurfaceAppearance.ApplyCombined(
                renderer, MountainRoadSurfaceKind.BarkAndDeadwood, TrunkColor);
        }
    }
}
