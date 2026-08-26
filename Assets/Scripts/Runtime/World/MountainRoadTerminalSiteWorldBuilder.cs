using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public sealed class MountainRoadTerminalSiteWorldResult
    {
        internal MountainRoadTerminalSiteWorldResult(
            GameObject root,
            IDictionary<string, Transform> semanticObjects)
        {
            Root = root;
            SemanticObjects = new Dictionary<string, Transform>(
                semanticObjects,
                StringComparer.Ordinal);
        }

        public GameObject Root { get; }

        /// <summary>
        /// The handful of site pieces something else has to find again:
        /// what the sounds hang on and where the yard lamp burns.
        /// </summary>
        public IReadOnlyDictionary<string, Transform> SemanticObjects { get; }
    }

    /// <summary>
    /// Materialises the dressed summit.
    ///
    /// Ten styles, ten batches, and not one new sheet: every style resolves
    /// to one of the fifteen surfaces the mountain already prints or
    /// borrows AND to a tint that surface's manifest already carries. A
    /// borrowed sheet's albedo compensation is fitted to the tints that
    /// multiply it, so inventing a new colour here would have meant
    /// re-solving that fit and re-measuring the sheet; reusing the measured
    /// ones costs nothing and cannot drift.
    ///
    /// The site is `42 x 27 m`, so it is keyed by style alone rather than
    /// by the `48 m` spatial chunk the city uses — chunking a site smaller
    /// than one chunk only adds a name.
    /// </summary>
    public static class MountainRoadTerminalSiteWorldBuilder
    {
        public const string RootName = "Mountain Terminal Site";
        public const string AnchorRootName = "Site Anchors";
        public const string YardLampAnchorId = "site-yard-lamp-shade";
        public const string LooseRailAnchorId = "site-parapet-gap-post-00";

        private static readonly Color PloughedSnow =
            new Color(0.47f, 0.52f, 0.525f);
        private static readonly Color Concrete =
            new Color(0.23f, 0.245f, 0.225f);
        private static readonly Color DressedStone =
            new Color(0.245f, 0.265f, 0.245f);
        private static readonly Color RawStone =
            new Color(0.19f, 0.215f, 0.205f);
        private static readonly Color Rust =
            new Color(0.33f, 0.245f, 0.17f);
        private static readonly Color PaintedSteel =
            new Color(0.2f, 0.265f, 0.24f);
        private static readonly Color PoleEnamel =
            new Color(0.62f, 0.22f, 0.18f);
        private static readonly Color FadedSign =
            new Color(0.56f, 0.52f, 0.39f);
        private static readonly Color Timber =
            new Color(0.19f, 0.165f, 0.135f);
        private static readonly Color DeadTimber =
            new Color(0.27f, 0.25f, 0.21f);
        private static readonly Color Canvas =
            new Color(0.31f, 0.29f, 0.24f);

        /// <summary>The apartment's own porcelain tint, borrowed with the
        /// shape.</summary>
        private static readonly Color Porcelain =
            new Color(0.51f, 0.53f, 0.43f);

        private static readonly Color PanSeat =
            new Color(0.27f, 0.25f, 0.21f);

        public static MountainRoadTerminalSiteWorldResult Build(
            Transform parent,
            MountainRoadTerminalSitePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            var root = new GameObject(RootName);
            root.transform.SetParent(parent, false);
            var semanticObjects = new Dictionary<string, Transform>(
                StringComparer.Ordinal);

            BuildBatches(root.transform, plan);
            BuildChains(root.transform, plan);
            BuildCloth(root.transform, plan, semanticObjects);
            BuildPrivyPan(root.transform, plan);
            BuildAnchors(root.transform, plan, semanticObjects);

            return new MountainRoadTerminalSiteWorldResult(
                root,
                semanticObjects);
        }

        private static void BuildBatches(
            Transform root,
            MountainRoadTerminalSitePlan plan)
        {
            var physical = new List<RuntimeOrientedBox>(64);
            var visual = new List<RuntimeOrientedBox>(64);
            foreach (MountainRoadSiteStyle style in
                     Enum.GetValues(typeof(MountainRoadSiteStyle)))
            {
                physical.Clear();
                visual.Clear();
                for (int index = 0; index < plan.Parts.Count; index++)
                {
                    MountainRoadSitePartDescriptor part = plan.Parts[index];
                    if (part.Style != style)
                    {
                        continue;
                    }

                    var box = new RuntimeOrientedBox(
                        part.Center,
                        Quaternion.Euler(0f, part.YawDegrees, 0f),
                        part.Size);
                    if (part.BlocksMovement)
                    {
                        physical.Add(box);
                    }
                    else
                    {
                        visual.Add(box);
                    }
                }

                EmitBatch(root, style, physical, true);
                EmitBatch(root, style, visual, false);
            }
        }

        private static void EmitBatch(
            Transform root,
            MountainRoadSiteStyle style,
            IReadOnlyList<RuntimeOrientedBox> boxes,
            bool physical)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            MountainRoadSurfaceKind kind = ResolveSurface(style);
            HomeSurfaceRecipe recipe =
                MountainRoadSurfaceAppearance.GetRecipe(kind);
            Color tint = ResolveColor(style);
            GameObject batch =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    physical
                        ? $"Site {DisplayName(style)}"
                        : $"Site {DisplayName(style)} (Visual)",
                    root,
                    new List<RuntimeOrientedBox>(boxes),
                    tint,
                    physical,
                    recipe.MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(
                batch.GetComponent<Renderer>(),
                kind,
                tint);
        }

        /// <summary>
        /// Chains sag, so they are their own batch rather than part of the
        /// iron: a run of chords laid on the curve, and no collider on any
        /// of them. The gap in the parapet is closed by a chain precisely
        /// because a chain does not close anything.
        /// </summary>
        private static void BuildChains(
            Transform root,
            MountainRoadTerminalSitePlan plan)
        {
            if (plan.Chains.Count == 0)
            {
                return;
            }

            var chords = new List<RuntimeOrientedBox>(
                plan.Chains.Count * 8);
            for (int index = 0; index < plan.Chains.Count; index++)
            {
                MountainRoadSiteChainDescriptor chain = plan.Chains[index];
                CityRopeSpanGeometry.AppendChordBoxes(
                    chords,
                    chain.Start,
                    chain.End,
                    chain.Sag,
                    chain.Thickness);
            }

            MountainRoadSurfaceKind kind =
                MountainRoadSurfaceKind.RustedIron;
            GameObject batch =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Site Slack Chains",
                    root,
                    chords,
                    Rust,
                    false,
                    MountainRoadSurfaceAppearance.GetRecipe(kind)
                        .MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            MountainRoadSurfaceAppearance.ApplyCombined(
                batch.GetComponent<Renderer>(),
                kind,
                Rust);
        }

        /// <summary>
        /// The windsock and the tarp over the freight. They keep the shared
        /// two-sided cloth material rather than a mountain sheet because
        /// they are skinned and the wind moves them, which is also why
        /// they can never join a static batch.
        /// </summary>
        private static void BuildCloth(
            Transform root,
            MountainRoadTerminalSitePlan plan,
            IDictionary<string, Transform> semanticObjects)
        {
            for (int index = 0; index < plan.Cloth.Count; index++)
            {
                MountainRoadSiteClothDescriptor cloth = plan.Cloth[index];
                GameObject rag = ClothPanelFactory.CreateHangingRag(
                    ClothName(cloth.StableId),
                    root,
                    cloth.Anchor,
                    cloth.YawDegrees,
                    cloth.Width,
                    cloth.Height,
                    Canvas,
                    cloth.Torn ? 1 : 0);
                semanticObjects[cloth.StableId] = rag.transform;
            }
        }

        /// <summary>
        /// Empty transforms for the pieces something else has to find. The
        /// parts themselves live inside combined meshes and have no
        /// transform of their own, and giving them one just to be found
        /// would cost the batch they belong to.
        /// </summary>
        /// <summary>
        /// The apartment's porcelain, carried up the mountain and set into
        /// a plank bench. It is two turned shapes and the site batch does
        /// only boxes, so it is built here beside the cloth and the
        /// chains - and set INTO the bench, rim flush, because that is
        /// what a pan bolted through a board looks like rather than a
        /// suite standing on one.
        /// </summary>
        private static void BuildPrivyPan(
            Transform root,
            MountainRoadTerminalSitePlan plan)
        {
            if (!plan.TryGetPart(
                    "site-privy-pan-seat",
                    out MountainRoadSitePartDescriptor seat))
            {
                return;
            }

            var pan = new GameObject("Privy Pan");
            pan.transform.SetParent(root, false);

            float rim = seat.Center.y + seat.Size.y * 0.5f;
            MountainRoadSurfaceKind enamel =
                MountainRoadSurfaceKind.PaleEnamel;
            float tile = MountainRoadSurfaceAppearance
                .GetRecipe(enamel).MetersPerTile;

            GameObject bowl = RuntimePrimitiveFactory.CreateCylinder(
                "Privy Porcelain Pan",
                pan.transform,
                new Vector3(seat.Center.x, rim - 0.11f, seat.Center.z),
                new Vector3(0.4f, 0.24f, 0.4f),
                Porcelain,
                false);
            MountainRoadSurfaceAppearance.Apply(
                bowl.GetComponent<Renderer>(),
                enamel,
                SurfaceProjection.CylinderSide,
                Porcelain);

            GameObject lid = RuntimePrimitiveFactory.CreateCylinder(
                "Privy Pan Seat Ring",
                pan.transform,
                new Vector3(seat.Center.x, rim + 0.02f, seat.Center.z),
                new Vector3(0.36f, 0.03f, 0.36f),
                PanSeat,
                false);
            MountainRoadSurfaceAppearance.Apply(
                lid.GetComponent<Renderer>(),
                MountainRoadSurfaceKind.BarkAndDeadwood,
                SurfaceProjection.CylinderCapXZ,
                PanSeat);
        }

        private static void BuildAnchors(
            Transform root,
            MountainRoadTerminalSitePlan plan,
            IDictionary<string, Transform> semanticObjects)
        {
            var anchorRoot = new GameObject(AnchorRootName);
            anchorRoot.transform.SetParent(root, false);

            AddAnchor(
                anchorRoot.transform,
                semanticObjects,
                plan.YardLamp.StableId,
                plan.YardLamp.Position);

            if (plan.TryGetPart(
                    LooseRailAnchorId,
                    out MountainRoadSitePartDescriptor rail))
            {
                AddAnchor(
                    anchorRoot.transform,
                    semanticObjects,
                    rail.StableId,
                    rail.Center + Vector3.up * (rail.Size.y * 0.5f));
            }
        }

        private static void AddAnchor(
            Transform parent,
            IDictionary<string, Transform> semanticObjects,
            string stableId,
            Vector3 position)
        {
            var anchor = new GameObject(stableId);
            anchor.transform.SetParent(parent, false);
            anchor.transform.position = position;
            semanticObjects[stableId] = anchor.transform;
        }

        public const string WindsockName = "Windsock";
        public const string LoadTarpName = "Load Tarp";

        private static string ClothName(string stableId)
        {
            return string.Equals(
                stableId,
                "site-windsock",
                StringComparison.Ordinal)
                ? WindsockName
                : LoadTarpName;
        }

        private static MountainRoadSurfaceKind ResolveSurface(
            MountainRoadSiteStyle style)
        {
            switch (style)
            {
                case MountainRoadSiteStyle.DirtySnow:
                    return MountainRoadSurfaceKind.WindSnow;
                case MountainRoadSiteStyle.Concrete:
                    return MountainRoadSurfaceKind.Concrete;
                case MountainRoadSiteStyle.DressedStone:
                case MountainRoadSiteStyle.RawStone:
                    return MountainRoadSurfaceKind.LayeredStone;
                case MountainRoadSiteStyle.RustedIron:
                    return MountainRoadSurfaceKind.RustedIron;
                case MountainRoadSiteStyle.PaintedSteel:
                    return MountainRoadSurfaceKind.PaintedMetal;
                case MountainRoadSiteStyle.PaleEnamel:
                case MountainRoadSiteStyle.FadedSign:
                    return MountainRoadSurfaceKind.PaleEnamel;
                case MountainRoadSiteStyle.Timber:
                case MountainRoadSiteStyle.DeadTimber:
                    return MountainRoadSurfaceKind.BarkAndDeadwood;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        null);
            }
        }

        private static Color ResolveColor(MountainRoadSiteStyle style)
        {
            switch (style)
            {
                case MountainRoadSiteStyle.DirtySnow:
                    return PloughedSnow;
                case MountainRoadSiteStyle.Concrete:
                    return Concrete;
                case MountainRoadSiteStyle.DressedStone:
                    return DressedStone;
                case MountainRoadSiteStyle.RawStone:
                    return RawStone;
                case MountainRoadSiteStyle.RustedIron:
                    return Rust;
                case MountainRoadSiteStyle.PaintedSteel:
                    return PaintedSteel;
                case MountainRoadSiteStyle.PaleEnamel:
                    return PoleEnamel;
                case MountainRoadSiteStyle.FadedSign:
                    return FadedSign;
                case MountainRoadSiteStyle.Timber:
                    return Timber;
                case MountainRoadSiteStyle.DeadTimber:
                    return DeadTimber;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        null);
            }
        }

        private static string DisplayName(MountainRoadSiteStyle style)
        {
            switch (style)
            {
                case MountainRoadSiteStyle.DirtySnow:
                    return "Ploughed Snow";
                case MountainRoadSiteStyle.Concrete:
                    return "Concrete Work";
                case MountainRoadSiteStyle.DressedStone:
                    return "Dressed Stone";
                case MountainRoadSiteStyle.RawStone:
                    return "Cut Rock";
                case MountainRoadSiteStyle.RustedIron:
                    return "Rusted Iron";
                case MountainRoadSiteStyle.PaintedSteel:
                    return "Painted Steel";
                case MountainRoadSiteStyle.PaleEnamel:
                    return "Pole Enamel";
                case MountainRoadSiteStyle.FadedSign:
                    return "Faded Enamel";
                case MountainRoadSiteStyle.Timber:
                    return "Pole Timber";
                case MountainRoadSiteStyle.DeadTimber:
                    return "Weathered Timber";
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        null);
            }
        }
    }
}
