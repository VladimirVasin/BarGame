using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Materializes the planned fringe as a small set of 48-metre batches.
    /// Tracks, drains and cables stay visual; walls, large stock, sheds and
    /// berms carry the only new collision. Four emissive lenses expose poses
    /// to the existing night pool; no Light component or interaction is built.
    /// </summary>
    internal static class CityFringeYardWorldBuilder
    {
        private const float SpatialChunkSize = 48f;
        internal const float PracticalLensForwardOffset = 0.24f;

        private static readonly Color ServiceGround =
            new Color(0.30f, 0.275f, 0.215f, 1f);
        private static readonly Color Drainage =
            new Color(0.115f, 0.14f, 0.13f, 1f);
        private static readonly Color OldMasonry =
            new Color(0.335f, 0.35f, 0.325f, 1f);
        private static readonly Color Concrete =
            new Color(0.285f, 0.315f, 0.305f, 1f);
        private static readonly Color Gabion =
            new Color(0.30f, 0.325f, 0.295f, 1f);
        private static readonly Color Iron =
            new Color(0.135f, 0.17f, 0.165f, 1f);
        private static readonly Color UtilityPaint =
            new Color(0.22f, 0.305f, 0.31f, 1f);
        private static readonly Color Rock =
            new Color(0.235f, 0.26f, 0.235f, 1f);

        internal static CityFringeYardWorldResult Build(
            Transform parent,
            CityFringeYardPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsEnabled)
            {
                return null;
            }

            var root = new GameObject("Authored Fringe Yards");
            root.transform.SetParent(parent, false);
            var batches =
                new Dictionary<BatchKey, List<RuntimeOrientedBox>>();
            for (int yardIndex = 0;
                 yardIndex < plan.Yards.Count;
                 yardIndex++)
            {
                CityFringeYardDescriptor yard = plan.Yards[yardIndex];
                for (int partIndex = 0;
                     partIndex < yard.Parts.Count;
                     partIndex++)
                {
                    CityFringeYardPartDescriptor part =
                        yard.Parts[partIndex];
                    var key = new BatchKey(
                        Mathf.FloorToInt(part.Center.x / SpatialChunkSize),
                        Mathf.FloorToInt(part.Center.z / SpatialChunkSize),
                        part.Style,
                        part.BlocksMovement);
                    if (!batches.TryGetValue(
                            key,
                            out List<RuntimeOrientedBox> boxes))
                    {
                        boxes = new List<RuntimeOrientedBox>();
                        batches.Add(key, boxes);
                    }

                    boxes.Add(new RuntimeOrientedBox(
                        part.Center,
                        part.Rotation,
                        part.Size));
                }
            }

            var keys = new List<BatchKey>(batches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                Color color = ResolveColor(key.Style);
                GameObject chunk =
                    RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                        $"Fringe Chunk {key.X} {key.Z} {key.Style} " +
                        $"{(key.BlocksMovement ? "Solid" : "Visual")}",
                        root.transform,
                        batches[key],
                        color,
                        key.BlocksMovement,
                        ResolveTileSize(key.Style),
                        ResolveUvMode(key.Style));
                Renderer renderer = chunk.GetComponent<Renderer>();
                ApplyAppearance(renderer, key.Style, color);
                if (!key.BlocksMovement &&
                    (key.Style == CityFringeYardStyle.ServiceGround ||
                     key.Style == CityFringeYardStyle.ServiceTrack ||
                     key.Style == CityFringeYardStyle.Drainage ||
                     key.Style == CityFringeYardStyle.Iron))
                {
                    renderer.shadowCastingMode = ShadowCastingMode.Off;
                }
            }

            IList<CityFringePracticalAnchor> practicalAnchors =
                BuildPracticalAnchors(root.transform, plan.Practicals);
            return new CityFringeYardWorldResult(root, practicalAnchors);
        }

        private static IList<CityFringePracticalAnchor>
            BuildPracticalAnchors(
                Transform parent,
                IReadOnlyList<CityFringeYardPracticalDescriptor> practicals)
        {
            var result = new List<CityFringePracticalAnchor>(
                practicals.Count);
            for (int index = 0; index < practicals.Count; index++)
            {
                CityFringeYardPracticalDescriptor descriptor =
                    practicals[index];
                Transform anchor = new GameObject(
                    $"Fringe Practical {descriptor.Kind}").transform;
                anchor.SetParent(parent, false);
                anchor.localPosition = descriptor.Position;
                anchor.localRotation = Quaternion.LookRotation(
                    descriptor.Forward,
                    Vector3.up);
                GameObject lens = RuntimePrimitiveFactory.CreateBox(
                    "Practical Emissive Lens",
                    anchor,
                    Vector3.forward * PracticalLensForwardOffset,
                    descriptor.LensSize,
                    descriptor.LitColor,
                    CityNightResources.EmissiveMaterial,
                    false);
                CityNightGlowRegistry.Register(
                    lens.GetComponent<Renderer>(),
                    descriptor.LitColor);
                result.Add(new CityFringePracticalAnchor(
                    descriptor.YardKind,
                    anchor));
            }

            return result;
        }

        private static void ApplyAppearance(
            Renderer renderer,
            CityFringeYardStyle style,
            Color color)
        {
            switch (style)
            {
                case CityFringeYardStyle.ServiceGround:
                    CityExteriorAppearance.ApplyGroundSurface(
                        renderer,
                        color);
                    break;
                case CityFringeYardStyle.ServiceTrack:
                    CityFringeYardSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityFringeYardSurfaceKind.ServiceTrack,
                        color);
                    break;
                case CityFringeYardStyle.Drainage:
                    CityRiverSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityRiverSurfaceKind.Bed,
                        color);
                    break;
                case CityFringeYardStyle.OldMasonry:
                    CityFringeYardSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityFringeYardSurfaceKind.Masonry,
                        color);
                    break;
                case CityFringeYardStyle.Concrete:
                    CityFringeYardSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityFringeYardSurfaceKind.Concrete,
                        color);
                    break;
                case CityFringeYardStyle.Gabion:
                    CityMountainSurfaceAppearance.ApplyCombined(
                        renderer,
                        color);
                    break;
                case CityFringeYardStyle.Iron:
                case CityFringeYardStyle.UtilityPaint:
                    CityRiverSurfaceAppearance.ApplyCombined(
                        renderer,
                        CityRiverSurfaceKind.Iron,
                        color);
                    break;
                case CityFringeYardStyle.Rock:
                    CityMountainSurfaceAppearance.ApplyCombined(
                        renderer,
                        color);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        null);
            }
        }

        private static float ResolveTileSize(CityFringeYardStyle style)
        {
            switch (style)
            {
                case CityFringeYardStyle.ServiceGround:
                    return CityExteriorAppearance.GroundTextureTileSize;
                case CityFringeYardStyle.ServiceTrack:
                    return CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.ServiceTrack).MetersPerTile;
                case CityFringeYardStyle.Drainage:
                    return CityRiverSurfaceAppearance
                        .GetRecipe(CityRiverSurfaceKind.Bed).MetersPerTile;
                case CityFringeYardStyle.OldMasonry:
                    return CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.Masonry).MetersPerTile;
                case CityFringeYardStyle.Concrete:
                    return CityFringeYardSurfaceAppearance.GetRecipe(
                        CityFringeYardSurfaceKind.Concrete).MetersPerTile;
                case CityFringeYardStyle.Gabion:
                    return CityMountainSurfaceAppearance.MetersPerTile;
                case CityFringeYardStyle.Iron:
                case CityFringeYardStyle.UtilityPaint:
                    return CityRiverSurfaceAppearance
                        .GetRecipe(CityRiverSurfaceKind.Iron).MetersPerTile;
                case CityFringeYardStyle.Rock:
                    return CityMountainSurfaceAppearance.MetersPerTile;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        null);
            }
        }

        private static RuntimeWorldUvMode ResolveUvMode(
            CityFringeYardStyle style)
        {
            return style == CityFringeYardStyle.ServiceGround ||
                   style == CityFringeYardStyle.ServiceTrack ||
                   style == CityFringeYardStyle.Drainage
                ? RuntimeWorldUvMode.XZPlanar
                : RuntimeWorldUvMode.BoxProjected;
        }

        private static Color ResolveColor(CityFringeYardStyle style)
        {
            switch (style)
            {
                case CityFringeYardStyle.ServiceGround:
                case CityFringeYardStyle.ServiceTrack:
                    return ServiceGround;
                case CityFringeYardStyle.Drainage:
                    return Drainage;
                case CityFringeYardStyle.OldMasonry:
                    return OldMasonry;
                case CityFringeYardStyle.Concrete:
                    return Concrete;
                case CityFringeYardStyle.Gabion:
                    return Gabion;
                case CityFringeYardStyle.Iron:
                    return Iron;
                case CityFringeYardStyle.UtilityPaint:
                    return UtilityPaint;
                case CityFringeYardStyle.Rock:
                    return Rock;
                default:
                    throw new ArgumentOutOfRangeException(
                        nameof(style),
                        style,
                        null);
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public BatchKey(
                int x,
                int z,
                CityFringeYardStyle style,
                bool blocksMovement)
            {
                X = x;
                Z = z;
                Style = style;
                BlocksMovement = blocksMovement;
            }

            public int X { get; }
            public int Z { get; }
            public CityFringeYardStyle Style { get; }
            public bool BlocksMovement { get; }

            public bool Equals(BatchKey other)
            {
                return X == other.X &&
                       Z == other.Z &&
                       Style == other.Style &&
                       BlocksMovement == other.BlocksMovement;
            }

            public override bool Equals(object obj)
            {
                return obj is BatchKey other && Equals(other);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = X;
                    hash = (hash * 397) ^ Z;
                    hash = (hash * 397) ^ (int)Style;
                    return (hash * 397) ^ BlocksMovement.GetHashCode();
                }
            }

            public static int Compare(BatchKey left, BatchKey right)
            {
                int x = left.X.CompareTo(right.X);
                if (x != 0)
                {
                    return x;
                }

                int z = left.Z.CompareTo(right.Z);
                if (z != 0)
                {
                    return z;
                }

                int style = left.Style.CompareTo(right.Style);
                return style != 0
                    ? style
                    : left.BlocksMovement.CompareTo(right.BlocksMovement);
            }
        }
    }
}
