using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>The authored seven-day household dressing. Never owns inventory or collision.</summary>
    [DisallowMultipleComponent]
    public sealed class HomeApartmentDressing : MonoBehaviour
    {
        private readonly List<(HomeAuthoredPart part, GameObject root)> staged =
            new List<(HomeAuthoredPart, GameObject)>();
        private readonly List<(HomeAuthoredPart part, GameObject root)> decoration =
            new List<(HomeAuthoredPart, GameObject)>();
        private readonly Dictionary<Renderer, (HomeSurfaceKind kind, Color tint)> surfaces =
            new Dictionary<Renderer, (HomeSurfaceKind, Color)>();
        public int AppliedDayNumber { get; private set; } = 1;
        public int VisiblePartCount
        {
            get
            {
                int count = 0;
                foreach (var item in decoration) if (item.root.activeInHierarchy) count++;
                return count;
            }
        }

        private void Awake() => AppliedDayNumber = HomeApartmentDayRules.ResolveDay(GameSessionState.GameDayNumber);

        internal void Register(HomeAuthoredPart part, GameObject root)
        {
            if (part.min_day <= 1 && part.max_day >= 7) return;
            staged.Add((part, root));
            root.SetActive(AppliedDayNumber >= part.min_day && AppliedDayNumber <= part.max_day);
        }

        internal void RegisterSurface(Renderer renderer, HomeSurfaceKind kind)
        {
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            surfaces[renderer] = (kind, properties.GetColor("_BaseColor"));
            ApplySurfaceDay(renderer, kind, surfaces[renderer].tint);
        }

        internal void Build(HomeInteriorLayoutPlan plan, HomeBalconyLayoutPlan balcony,
            HomeOcclusionRegistry occlusion)
        {
            Transform bedClutter = new GameObject(HomeBedInteraction.SurfaceClutterName).transform;
            bedClutter.SetParent(transform, false);
            foreach (HomeAuthoredPart part in HomeInteriorModelLibrary.Load().Parts)
            {
                if (part.role != "decor") continue;
                bool onBed = part.group == "home.bed.surface-clutter";
                GameObject item = HomeAuthoredVisualFactory.Place(part,
                    string.IsNullOrEmpty(part.semantic_name) ? part.name : part.semantic_name,
                    onBed ? bedClutter : transform, part.Position, Vector3.one);
                item.transform.localRotation = Quaternion.Euler(part.Rotation);
                decoration.Add((part, item));
                // The closed-room shell remains opaque even when the hero stands before its door.
                string group = onBed ? HomeInteriorWorldBuilder.BedOccluderId : part.group;
                if (occlusion != null && !string.IsNullOrEmpty(group) &&
                    occlusion.TryGetGroup(group, out _)) occlusion.AddRenderers(group, item);
            }
            HomeLockedRoomPlan.BuildCollision(transform, plan);
            ValidateOrThrow(plan, balcony);
        }

        public void ApplyDay(int dayNumber)
        {
            AppliedDayNumber = HomeApartmentDayRules.ResolveDay(dayNumber);
            foreach (var item in staged)
                if (item.root != null) item.root.SetActive(
                    AppliedDayNumber >= item.part.min_day && AppliedDayNumber <= item.part.max_day);
            foreach (var entry in surfaces)
                if (entry.Key != null) ApplySurfaceDay(entry.Key, entry.Value.kind, entry.Value.tint);
        }

        private void ApplySurfaceDay(Renderer renderer, HomeSurfaceKind kind, Color original)
        {
            float amount = (AppliedDayNumber - 1) / 6f;
            Color lastDay;
            switch (kind)
            {
                case HomeSurfaceKind.BedLinen: lastDay = new Color(0.60f, 0.51f, 0.39f); break;
                case HomeSurfaceKind.Upholstery: lastDay = new Color(0.67f, 0.60f, 0.47f); break;
                case HomeSurfaceKind.BathroomTile:
                case HomeSurfaceKind.Enamel: lastDay = new Color(0.76f, 0.72f, 0.57f); break;
                case HomeSurfaceKind.PlankFloor:
                case HomeSurfaceKind.Rug: lastDay = new Color(0.71f, 0.65f, 0.55f); break;
                case HomeSurfaceKind.WornLaminate: lastDay = new Color(0.75f, 0.69f, 0.57f); break;
                default: lastDay = new Color(0.90f, 0.87f, 0.81f); break;
            }
            Color tint = original * Color.Lerp(Color.white, lastDay, amount);
            tint.a = original.a;
            var properties = new MaterialPropertyBlock();
            renderer.GetPropertyBlock(properties);
            properties.SetColor("_BaseColor", tint);
            properties.SetColor("_Color", tint);
            renderer.SetPropertyBlock(properties);
        }

        public void ValidateOrThrow(HomeInteriorLayoutPlan plan, HomeBalconyLayoutPlan balcony)
        {
            HomeLockedRoomPlan.ValidateOrThrow(plan);
            HomeBedInteractionPlan bed = HomeBedInteractionPlan.Create(plan);
            HomeRefrigeratorPlan fridge = HomeRefrigeratorPlan.Create(plan);
            const float radius = HomeInteriorLayoutValidator.PlayerClearanceRadius;
            Rect bedApproach = Rect.MinMaxRect(bed.EntryRootPosition.x - radius,
                bed.EntryRootPosition.z - radius, -0.80f + radius, bed.EntryRootPosition.z + radius);
            foreach (var item in decoration)
            {
                HomeAuthoredPart part = item.part;
                if (part.min_day < 1 || part.max_day > 7 || part.min_day > part.max_day)
                    throw new InvalidOperationException($"Invalid day range on '{part.name}'.");
                Bounds source = part.mesh.bounds;
                Matrix4x4 toRoom = transform.worldToLocalMatrix * item.root.transform.localToWorldMatrix;
                Bounds bounds = new Bounds(toRoom.MultiplyPoint3x4(source.min), Vector3.zero);
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                        for (int z = -1; z <= 1; z += 2)
                            bounds.Encapsulate(toRoom.MultiplyPoint3x4(source.center +
                                Vector3.Scale(source.extents, new Vector3(x, y, z))));
                // Paper-thin stains, wall details and supported tabletop props do not obstruct feet.
                if (bounds.max.y <= 0.045f || bounds.min.y >= 0.20f || part.collider) continue;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                Rect footprint = Rect.MinMaxRect(min.x, min.z, max.x, max.z);
                foreach (HomeInteriorPath path in plan.Paths)
                    if (Overlaps(footprint, path.Bounds))
                        throw new InvalidOperationException($"Home clutter '{part.name}' blocks '{path.Id}'.");
                if (balcony != null && Overlaps(footprint, balcony.InteriorAccessPath))
                    throw new InvalidOperationException($"Home clutter '{part.name}' blocks balcony access.");
                if (Overlaps(footprint, bedApproach) || Overlaps(footprint, fridge.ApproachBounds))
                    throw new InvalidOperationException($"Home clutter '{part.name}' blocks a household interaction dock.");
                for (int index = 1; index < fridge.ApproachWaypoints.Count; index++)
                {
                    Vector3 a = fridge.ApproachWaypoints[index - 1];
                    Vector3 b = fridge.ApproachWaypoints[index];
                    Rect corridor = Rect.MinMaxRect(Mathf.Min(a.x, b.x) - radius,
                        Mathf.Min(a.z, b.z) - radius, Mathf.Max(a.x, b.x) + radius,
                        Mathf.Max(a.z, b.z) + radius);
                    if (Overlaps(footprint, corridor))
                        throw new InvalidOperationException($"Home clutter '{part.name}' blocks the refrigerator approach.");
                }
            }
        }

        private static bool Overlaps(Rect a, Rect b) =>
            Mathf.Min(a.xMax, b.xMax) - Mathf.Max(a.xMin, b.xMin) > 0.001f &&
            Mathf.Min(a.yMax, b.yMax) - Mathf.Max(a.yMin, b.yMin) > 0.001f;
    }
}
