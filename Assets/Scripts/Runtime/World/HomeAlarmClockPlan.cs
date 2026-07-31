using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Bed-relative placement for collider-free clock dressing.
    /// It deliberately stays outside the validated furniture catalogue.
    /// </summary>
    public readonly struct HomeAlarmClockPlan
    {
        public const float NightstandHeight = 0.72f;
        public const float NightstandTopThickness = 0.05f;
        public static readonly Vector3 ClockBodySize =
            new Vector3(0.46f, 0.27f, 0.24f);

        private HomeAlarmClockPlan(
            Rect bedBounds,
            Rect nightstandFootprint,
            Vector3 clockPosition)
        {
            BedBounds = bedBounds;
            NightstandFootprint = nightstandFootprint;
            ClockPosition = clockPosition;
        }

        public Rect BedBounds { get; }
        public Rect NightstandFootprint { get; }
        public Vector3 NightstandCenter =>
            new Vector3(
                NightstandFootprint.center.x,
                NightstandHeight * 0.5f,
                NightstandFootprint.center.y);
        public Vector3 NightstandSize =>
            new Vector3(
                NightstandFootprint.width,
                NightstandHeight,
                NightstandFootprint.height);
        public Vector3 ClockPosition { get; }

        public static HomeAlarmClockPlan Create(
            HomeInteriorLayoutPlan layout)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (!layout.TryGetFurniture(
                    HomeFurnitureKind.Bed,
                    out HomeFurnitureFootprint bed))
            {
                throw new InvalidOperationException(
                    "The home alarm clock requires a bed.");
            }

            const float nightstandWidth = 0.68f;
            const float nightstandDepth = 0.46f;
            Vector2 center = new Vector2(
                bed.Bounds.xMin + 0.38f,
                bed.Bounds.yMax + 0.30f);
            Rect nightstand = new Rect(
                center.x - nightstandWidth * 0.5f,
                center.y - nightstandDepth * 0.5f,
                nightstandWidth,
                nightstandDepth);
            Vector3 clockPosition = new Vector3(
                center.x,
                NightstandHeight +
                NightstandTopThickness +
                ClockBodySize.y * 0.5f +
                0.015f,
                center.y);
            var plan = new HomeAlarmClockPlan(
                bed.Bounds,
                nightstand,
                clockPosition);
            ValidateOrThrow(layout, plan);
            return plan;
        }

        private static void ValidateOrThrow(
            HomeInteriorLayoutPlan layout,
            HomeAlarmClockPlan plan)
        {
            if (!Contains(
                    layout.RoomBounds,
                    plan.NightstandFootprint) ||
                plan.NightstandFootprint.Overlaps(
                    plan.BedBounds,
                    true))
            {
                throw new InvalidOperationException(
                    "The alarm-clock nightstand must stay inside the room " +
                    "and outside the bed.");
            }

            for (int index = 0;
                 index < layout.Paths.Count;
                 index++)
            {
                if (plan.NightstandFootprint.Overlaps(
                        layout.Paths[index].Bounds,
                        true))
                {
                    throw new InvalidOperationException(
                        "The alarm-clock nightstand must not block a " +
                        "protected home path.");
                }
            }

            for (int index = 0;
                 index < layout.Furniture.Count;
                 index++)
            {
                HomeFurnitureFootprint furniture =
                    layout.Furniture[index];
                if (furniture.Kind == HomeFurnitureKind.Bed)
                {
                    continue;
                }

                if (plan.NightstandFootprint.Overlaps(
                        furniture.Bounds,
                        true))
                {
                    throw new InvalidOperationException(
                        "The alarm-clock nightstand must not overlap " +
                        $"home furniture '{furniture.Id}'.");
                }
            }
        }

        private static bool Contains(Rect outer, Rect inner)
        {
            return inner.xMin >= outer.xMin &&
                   inner.xMax <= outer.xMax &&
                   inner.yMin >= outer.yMin &&
                   inner.yMax <= outer.yMax;
        }
    }
}
