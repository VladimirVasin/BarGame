using System;
using UnityEngine;

namespace BarPromenade
{
    public static class InteriorSoundscapeAnchorPlanner
    {
        public static StairwellSoundscapeAnchors
            CreateStairwellLocal(StairwellLayoutPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            float backWall =
                plan.RoomSize.y * 0.5f - 0.145f;
            float leftWall =
                plan.RoomSize.x * -0.5f + 0.19f;
            return new StairwellSoundscapeAnchors(
                new Vector3(0.72f, 3.92f, backWall),
                StairwellInteriorAtmosphere.MiddleEmitterPosition,
                new Vector3(leftWall, 3.0f, 1.72f),
                plan.UpperBlockerBounds.center,
                new Vector3(-2.80f, 0.12f, -3.05f),
                plan.ApartmentEntrancePosition);
        }

        public static StairwellSoundscapeAnchors
            CreateStairwellWorld(
                StairwellLayoutPlan plan,
                Transform sceneRoot)
        {
            return ToWorld(
                CreateStairwellLocal(plan),
                sceneRoot);
        }

        public static HomeSoundscapeAnchors
            CreateHomeLocal(
                HomeInteriorLayoutPlan plan,
                HomeBalconyLayoutPlan balcony)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (balcony == null)
            {
                throw new ArgumentNullException(nameof(balcony));
            }

            HomeFurnitureFootprint bed =
                GetRequiredFurniture(
                    plan,
                    HomeFurnitureKind.Bed);
            HomeFurnitureFootprint bookcase =
                GetRequiredFurniture(
                    plan,
                    HomeFurnitureKind.Bookcase);

            Vector3 refrigerator =
                HomeRefrigeratorPlan.Create(plan).SoundAnchor;
            Vector3 balconyNightAir =
                balcony.WindowCenter + Vector3.right * 0.18f;
            Vector3 softWood =
                new Vector3(
                    bed.Bounds.center.x,
                    0.28f,
                    bed.Bounds.center.y);
            Vector3 radiator =
                new Vector3(
                    plan.RoomBounds.xMax - 0.18f,
                    0.72f,
                    plan.BathroomBounds.center.y);
            Vector3 radio =
                new Vector3(
                    bookcase.Bounds.center.x,
                    HomeInteriorWorldBuilder
                        .BookcaseDressingSurfaceHeight +
                    0.15f,
                    bookcase.Bounds.center.y);
            Vector3 bathroom =
                new Vector3(
                    plan.BathroomBounds.center.x,
                    0.10f,
                    plan.BathroomBounds.center.y);

            return new HomeSoundscapeAnchors(
                refrigerator,
                balconyNightAir,
                softWood,
                radiator,
                radio,
                bathroom);
        }

        public static HomeSoundscapeAnchors CreateHomeWorld(
            HomeInteriorLayoutPlan plan,
            HomeBalconyLayoutPlan balcony,
            Transform sceneRoot)
        {
            return ToWorld(
                CreateHomeLocal(plan, balcony),
                sceneRoot);
        }

        private static HomeFurnitureFootprint
            GetRequiredFurniture(
                HomeInteriorLayoutPlan plan,
                HomeFurnitureKind kind)
        {
            if (!plan.TryGetFurniture(
                    kind,
                    out HomeFurnitureFootprint furniture))
            {
                throw new InvalidOperationException(
                    $"Home soundscape requires {kind}.");
            }

            return furniture;
        }

        private static StairwellSoundscapeAnchors ToWorld(
            StairwellSoundscapeAnchors local,
            Transform sceneRoot)
        {
            if (sceneRoot == null)
            {
                throw new ArgumentNullException(nameof(sceneRoot));
            }

            return new StairwellSoundscapeAnchors(
                sceneRoot.TransformPoint(local.Ventilation),
                sceneRoot.TransformPoint(local.Electrical),
                sceneRoot.TransformPoint(local.PipeKnock),
                sceneRoot.TransformPoint(local.MetalStress),
                sceneRoot.TransformPoint(local.DistantWater),
                sceneRoot.TransformPoint(local.DistantMovement));
        }

        private static HomeSoundscapeAnchors ToWorld(
            HomeSoundscapeAnchors local,
            Transform sceneRoot)
        {
            if (sceneRoot == null)
            {
                throw new ArgumentNullException(nameof(sceneRoot));
            }

            return new HomeSoundscapeAnchors(
                sceneRoot.TransformPoint(local.Refrigerator),
                sceneRoot.TransformPoint(local.BalconyNightAir),
                sceneRoot.TransformPoint(local.SoftWood),
                sceneRoot.TransformPoint(local.Radiator),
                sceneRoot.TransformPoint(local.Radio),
                sceneRoot.TransformPoint(local.Bathroom));
        }
    }
}
