using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class CityOpenAreaWorldBuilder
    {
        public const string RootName = "Open Area Landmarks";
        private const float SpatialChunkSize = 48f;

        private static readonly Color LakeStone =
            new Color(0.24f, 0.28f, 0.25f);
        private static readonly Color Reeds =
            new Color(0.25f, 0.34f, 0.16f);
        private static readonly Color WeatheredWood =
            new Color(0.27f, 0.20f, 0.13f);
        private static readonly Color CemeteryIron =
            new Color(0.08f, 0.10f, 0.10f);
        private static readonly Color CemeteryPath =
            new Color(0.27f, 0.26f, 0.21f);
        private static readonly Color GraveStone =
            new Color(0.31f, 0.32f, 0.29f);
        private static readonly Color TreeTrunk =
            new Color(0.13f, 0.10f, 0.07f);
        private static readonly Color DarkFoliage =
            new Color(0.08f, 0.16f, 0.10f);
        // Bare earth beaten a shade darker than the yard ground it cuts.
        private static readonly Color YardWornTrack =
            new Color(0.22f, 0.19f, 0.14f);
        private static readonly Color YardTimber =
            new Color(0.24f, 0.19f, 0.14f);
        private static readonly Color YardPipe =
            new Color(0.19f, 0.20f, 0.19f);
        // The single saturated note in the yard, on one dropped toy.
        private static readonly Color YardPaint =
            new Color(0.46f, 0.23f, 0.16f);

        public static GameObject Build(
            Transform parent,
            CityOpenAreaDecorationPlan plan)
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
            var batches = new Dictionary<BatchKey, List<Bounds>>();
            for (int index = 0; index < plan.Descriptors.Count; index++)
            {
                CityOpenAreaDecorationDescriptor descriptor =
                    plan.Descriptors[index];
                var key = new BatchKey(
                    Mathf.FloorToInt(
                        descriptor.Bounds.center.x / SpatialChunkSize),
                    Mathf.FloorToInt(
                        descriptor.Bounds.center.z / SpatialChunkSize),
                    descriptor.Style);
                if (!batches.TryGetValue(key, out List<Bounds> boxes))
                {
                    boxes = new List<Bounds>();
                    batches.Add(key, boxes);
                }

                boxes.Add(descriptor.Bounds);
            }

            var keys = new List<BatchKey>(batches.Keys);
            keys.Sort(BatchKey.Compare);
            for (int index = 0; index < keys.Count; index++)
            {
                BatchKey key = keys[index];
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    $"Open Area Chunk {key.X} {key.Z} {key.Style}",
                    root,
                    batches[key],
                    ResolveColor(key.Style),
                    CityOpenAreaDecorationRules.BlocksMovement(key.Style));
            }

            return root.gameObject;
        }

        private static Color ResolveColor(
            CityOpenAreaDecorationStyle style)
        {
            switch (style)
            {
                case CityOpenAreaDecorationStyle.LakeStone:
                    return LakeStone;
                case CityOpenAreaDecorationStyle.Reeds:
                    return Reeds;
                case CityOpenAreaDecorationStyle.WeatheredWood:
                    return WeatheredWood;
                case CityOpenAreaDecorationStyle.CemeteryIron:
                    return CemeteryIron;
                case CityOpenAreaDecorationStyle.CemeteryPath:
                    return CemeteryPath;
                case CityOpenAreaDecorationStyle.GraveStone:
                    return GraveStone;
                case CityOpenAreaDecorationStyle.TreeTrunk:
                    return TreeTrunk;
                case CityOpenAreaDecorationStyle.YardWornTrack:
                    return YardWornTrack;
                case CityOpenAreaDecorationStyle.YardTimber:
                    return YardTimber;
                case CityOpenAreaDecorationStyle.YardPipe:
                    return YardPipe;
                case CityOpenAreaDecorationStyle.YardPaint:
                    return YardPaint;
                case CityOpenAreaDecorationStyle.DarkFoliage:
                    return DarkFoliage;
                default:
                    throw new ArgumentOutOfRangeException(nameof(style));
            }
        }

        private readonly struct BatchKey : IEquatable<BatchKey>
        {
            public BatchKey(
                int x,
                int z,
                CityOpenAreaDecorationStyle style)
            {
                X = x;
                Z = z;
                Style = style;
            }

            public int X { get; }
            public int Z { get; }
            public CityOpenAreaDecorationStyle Style { get; }

            public bool Equals(BatchKey other)
            {
                return X == other.X &&
                       Z == other.Z &&
                       Style == other.Style;
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
                    return (hash * 397) ^ (int)Style;
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
                return z != 0
                    ? z
                    : left.Style.CompareTo(right.Style);
            }
        }
    }
}
