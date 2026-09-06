using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal readonly struct AlpineVillageRockPlacement
    {
        public AlpineVillageRockPlacement(int variant, Vector3 position, Vector3 outward, float tint)
        {
            Variant = variant;
            Position = position;
            Rotation = Quaternion.LookRotation(outward, Vector3.up);
            TintVariation = tint;
        }

        public int Variant { get; }
        public Vector3 Position { get; }
        public Quaternion Rotation { get; }
        public float TintVariation { get; }
    }

    /// <summary>Places the authored wedge faces along the straight parts of
    /// the existing physical bowl. It never puts a decoration on open ground
    /// or in the cable cut, and never changes a gameplay height or collider.</summary>
    internal static class AlpineVillageRockPlanner
    {
        private const float ToeInset = 2.4f;
        private const float BuriedFoot = 2.5f;
        private const float Interval = 20f;
        private const float CableMargin = 2f;

        public static IReadOnlyList<AlpineVillageRockPlacement> Create(AlpineVillagePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!Mathf.Approximately(AlpineVillageTerrainSampler.RidgeRisePerMeter,
                    VillageRockAssetProvider.AuthoredRidgeRise))
            {
                throw new InvalidOperationException(
                    "The authored village strata no longer match the physical ridge slope.");
            }

            var result = new List<AlpineVillageRockPlacement>();
            Rect bounds = plan.TerrainBounds;
            AddSide(plan, result, new Vector2(bounds.xMin, bounds.yMin),
                Vector2.up, Vector2.left, bounds.height, 0);
            AddSide(plan, result, new Vector2(bounds.xMax, bounds.yMax),
                Vector2.down, Vector2.right, bounds.height, 1);
            AddSide(plan, result, new Vector2(bounds.xMax, bounds.yMin),
                Vector2.left, Vector2.down, bounds.width, 2);
            AddSide(plan, result, new Vector2(bounds.xMin, bounds.yMax),
                Vector2.right, Vector2.up, bounds.width, 3);
            return result.AsReadOnly();
        }

        private static void AddSide(
            AlpineVillagePlan plan,
            List<AlpineVillageRockPlacement> result,
            Vector2 start,
            Vector2 tangent,
            Vector2 outward,
            float length,
            int side)
        {
            float endMargin = VillageRockAssetProvider.HalfWidth + 1f;
            float usable = length - endMargin * 2f;
            if (usable <= 0f)
            {
                return;
            }

            int count = Mathf.Max(1, Mathf.FloorToInt(usable / Interval) + 1);
            for (int index = 0; index < count; index++)
            {
                uint hash = Mix((uint)plan.Seed ^ (uint)(side * 73856093 + index * 19349663));
                float along = count == 1 ? length * 0.5f :
                    endMargin + index * usable / (count - 1);
                Vector2 point = start + tangent * along + outward *
                    (AlpineVillageTerrainSampler.RidgeStandoff + ToeInset);
                Vector2 right = new Vector2(outward.y, -outward.x);
                if (TouchesCableCut(plan, point, right, outward))
                {
                    continue;
                }

                // The lowest actual foot height across the whole panel is
                // used, so even the downhill end is buried on the village's
                // gentle macro-slope. The high side simply exposes less rock.
                float lowestFoot = float.PositiveInfinity;
                for (int sample = 0; sample <= 4; sample++)
                {
                    Vector2 foot = point + right * Mathf.Lerp(
                        -VillageRockAssetProvider.HalfWidth,
                        VillageRockAssetProvider.HalfWidth, sample / 4f);
                    lowestFoot = Mathf.Min(lowestFoot,
                        AlpineVillageTerrainSampler.SampleHeight(plan, foot));
                }

                Vector3 position = new Vector3(point.x, lowestFoot - BuriedFoot, point.y);
                result.Add(new AlpineVillageRockPlacement(
                    (int)(hash % VillageRockAssetProvider.VariantCount), position,
                    new Vector3(outward.x, 0f, outward.y),
                    Mathf.Lerp(-0.045f, 0.045f, ((hash >> 8) & 255) / 255f)));
            }
        }

        private static bool TouchesCableCut(
            AlpineVillagePlan plan, Vector2 origin, Vector2 right, Vector2 outward)
        {
            MountainRoadCablewayPlan cableway = plan.Station.Cableway;
            Vector2 cableOrigin = new Vector2(cableway.StationArea.Center.x,
                cableway.StationArea.Center.z);
            Vector2 cableRight = new Vector2(cableway.LineRight.x, cableway.LineRight.z).normalized;
            Vector2 cableForward = new Vector2(cableway.LineForward.x, cableway.LineForward.z).normalized;
            float minAcross = float.PositiveInfinity;
            float maxAcross = float.NegativeInfinity;
            float maxAlong = float.NegativeInfinity;
            for (int depth = 0; depth < 2; depth++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    Vector2 corner = origin + right * (side * VillageRockAssetProvider.HalfWidth) +
                                     outward * (depth * VillageRockAssetProvider.Depth);
                    Vector2 delta = corner - cableOrigin;
                    float across = Vector2.Dot(delta, cableRight);
                    minAcross = Mathf.Min(minAcross, across);
                    maxAcross = Mathf.Max(maxAcross, across);
                    maxAlong = Mathf.Max(maxAlong, Vector2.Dot(delta, cableForward));
                }
            }

            float halfWidth = AlpineVillageTerrainSampler.CablewayCutOuterHalfWidth + CableMargin;
            return maxAlong > 0f && minAcross <= halfWidth && maxAcross >= -halfWidth;
        }

        private static uint Mix(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                return value ^ (value >> 16);
            }
        }
    }
}
