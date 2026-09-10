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
    /// Nothing here carries a collider. The forest's trunks stop the hero, but
    /// they stop him through the walkable mask - see
    /// <c>AlpineVillageWalkableArea.BuildForestTrunks</c> for why physics would
    /// be the wrong instrument.
    /// </summary>
    internal static class AlpineVillageTreeBuilder
    {
        public const string RootName = "Village Conifers";
        public const string CrownsName = "Village Conifer Crowns";
        public const string TrunksName = "Village Conifer Trunks";
        public const string StumpsName = "Village Cut Stumps";
        public const string BranchesName = "Village Fallen Branches";

        /// <summary>The mountain road's own bark, copied so the two areas cannot drift.</summary>
        private static readonly Color TrunkColor =
            new Color(0.19f, 0.165f, 0.135f, 1f);

        /// <summary>
        /// The road's `Mid` crown tint. The village sits above that layer, so
        /// it borrows the middle value rather than the near one.
        /// </summary>
        private static readonly Color CrownColor =
            new Color(0.095f, 0.14f, 0.115f, 1f);

        /// <summary>The road's dead wood, for the limbs that came down.</summary>
        private static readonly Color DeadWoodColor =
            new Color(0.27f, 0.25f, 0.21f, 1f);

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
            BuildBranches(root.transform, trees.Branches);
        }

        /// <summary>
        /// Deadfall: a shaft with one or two forks still on it, lying on the
        /// snow. All of it in one batch, no collider - art §13 keeps small
        /// detail non-physical, and a limb the hero stops dead against would
        /// read worse than one he walks over.
        ///
        /// The fork angles are derived from the descriptor's own numbers
        /// rather than rolled here: the plan owns every transform in this
        /// area, and a builder that invents its own randomness is a second
        /// source of truth for where things are.
        /// </summary>
        private static void BuildBranches(
            Transform parent,
            IReadOnlyList<AlpineVillageBranchDescriptor> branches)
        {
            if (branches.Count == 0)
            {
                return;
            }

            var boxes = new List<RuntimeOrientedBox>(branches.Count * 3);
            for (int index = 0; index < branches.Count; index++)
            {
                AlpineVillageBranchDescriptor branch = branches[index];
                Quaternion rotation = Quaternion.Euler(
                    branch.TiltDegrees, branch.YawDegrees, 0f);
                Vector3 centre = branch.Position +
                    rotation * Vector3.up * (branch.Thickness * 0.5f);
                boxes.Add(new RuntimeOrientedBox(
                    centre,
                    rotation,
                    new Vector3(
                        branch.Thickness, branch.Thickness, branch.Length)));

                for (int fork = 0; fork < branch.ForkCount; fork++)
                {
                    float side = fork == 0 ? 1f : -1f;
                    float angle = side *
                        (32f + Mathf.Repeat(branch.YawDegrees, 26f));
                    float at = Mathf.Repeat(branch.Length, 0.34f) - 0.17f +
                        (fork == 0 ? 0.12f : -0.2f);
                    float forkLength = branch.Length *
                        (0.32f + Mathf.Repeat(branch.Thickness * 7f, 0.2f));
                    Quaternion forkRotation =
                        rotation * Quaternion.Euler(0f, angle, 0f);
                    Vector3 root = centre +
                        rotation * Vector3.forward * (branch.Length * at);
                    boxes.Add(new RuntimeOrientedBox(
                        root + forkRotation * Vector3.forward * (forkLength * 0.5f),
                        forkRotation,
                        new Vector3(
                            branch.Thickness * 0.7f,
                            branch.Thickness * 0.7f,
                            forkLength)));
                }
            }

            float metersPerTile = MountainRoadSurfaceAppearance.GetRecipe(
                MountainRoadSurfaceKind.BarkAndDeadwood).MetersPerTile;
            GameObject batch = RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                BranchesName,
                parent,
                boxes,
                DeadWoodColor,
                false,
                metersPerTile,
                RuntimeWorldUvMode.BoxProjected);
            var renderer = batch.GetComponent<Renderer>();
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            MountainRoadSurfaceAppearance.ApplyCombined(
                renderer, MountainRoadSurfaceKind.BarkAndDeadwood, DeadWoodColor);
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
