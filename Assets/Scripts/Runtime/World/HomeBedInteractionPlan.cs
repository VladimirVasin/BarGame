using System;
using UnityEngine;

namespace BarPromenade
{
    public readonly struct HomeBedInteractionPlan
    {
        private const float ApproachOffset = 0.48f;
        private const float TriggerDepth = 0.72f;
        private const float TriggerHeight = 1.80f;
        private const float TriggerInset = 0.12f;
        internal const float BedSurfaceClearance = 0.045f;
        internal const float ActionHipFootwardOffset = 0.135f;
        private const float UprightHipPivotPixels = 40f;
        private const float UprightVisualOffset = 0.005f;

        private HomeBedInteractionPlan(
            Rect bedBounds,
            Vector3 interactionPosition,
            Vector3 approachRootPosition,
            Vector3 standHipPosition,
            Vector3 actionHipPosition,
            Vector3 headToFootAxis,
            Vector3 triggerCenter,
            Vector3 triggerSize)
        {
            BedBounds = bedBounds;
            InteractionPosition = interactionPosition;
            ApproachRootPosition = approachRootPosition;
            StandHipPosition = standHipPosition;
            ActionHipPosition = actionHipPosition;
            HeadToFootAxis = headToFootAxis;
            TriggerCenter = triggerCenter;
            TriggerSize = triggerSize;
        }

        public Rect BedBounds { get; }
        public Vector3 InteractionPosition { get; }
        public Vector3 ApproachRootPosition { get; }
        public Vector3 StandHipPosition { get; }
        public Vector3 ActionHipPosition { get; }
        public Vector3 HeadToFootAxis { get; }
        public Vector3 TriggerCenter { get; }
        public Vector3 TriggerSize { get; }

        public static HomeBedInteractionPlan Create(
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
                    "The home layout must contain exactly one bed.");
            }

            Rect bounds = bed.Bounds;
            float accessibleEdge = bounds.xMax;
            float centerZ = bounds.center.y;
            Vector3 approachRoot = new Vector3(
                accessibleEdge + ApproachOffset,
                layout.PlayerSpawn.y,
                centerZ);
            Vector3 standHip =
                approachRoot +
                (Vector3.up *
                 ((UprightHipPivotPixels /
                   PlayerSpriteRig.PixelsPerUnit) +
                  UprightVisualOffset));
            Vector3 headToFootAxis = Vector3.right;
            Vector3 actionHip = new Vector3(
                bounds.center.x +
                (headToFootAxis.x * ActionHipFootwardOffset),
                HomeInteriorWorldBuilder.BedDressingSurfaceHeight +
                BedSurfaceClearance,
                centerZ +
                (headToFootAxis.z * ActionHipFootwardOffset));
            Vector3 triggerSize = new Vector3(
                TriggerDepth,
                TriggerHeight,
                Mathf.Max(0.10f, bounds.height - (TriggerInset * 2f)));
            Vector3 triggerCenter = new Vector3(
                accessibleEdge + (TriggerDepth * 0.5f),
                TriggerHeight * 0.5f,
                centerZ);

            return new HomeBedInteractionPlan(
                bounds,
                approachRoot,
                approachRoot,
                standHip,
                actionHip,
                headToFootAxis,
                triggerCenter,
                triggerSize);
        }
    }
}
