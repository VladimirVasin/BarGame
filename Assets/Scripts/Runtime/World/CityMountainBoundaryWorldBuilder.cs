using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the close-range western and southern mountain boundary plus the
    /// deliberately sealed southern tunnel stub. It does not extend the
    /// walkable mask and it creates no interaction or scene transition.
    /// </summary>
    internal static class CityMountainBoundaryWorldBuilder
    {
        private const float ApproachSegmentLength = 2.5f;
        private const float ApproachLift = 0.025f;
        private const float ApproachThickness = 0.035f;
        private const float TrackWidth = 0.52f;
        private const float TrackSeparation = 2.2f;

        internal static readonly Color ForeRock =
            new Color(0.21f, 0.235f, 0.215f, 1f);
        internal static readonly Color MidRock =
            new Color(0.255f, 0.28f, 0.255f, 1f);
        internal static readonly Color HighRock =
            new Color(0.295f, 0.32f, 0.30f, 1f);

        private static readonly Color ThroatRock =
            new Color(0.105f, 0.120f, 0.112f, 1f);
        private static readonly Color GateMetal =
            new Color(0.155f, 0.185f, 0.175f, 1f);
        private static readonly Color GateBrace =
            new Color(0.105f, 0.125f, 0.120f, 1f);
        private static readonly Color ApproachSoil =
            new Color(0.36f, 0.31f, 0.23f, 1f);
        private static readonly Color TrackSoil =
            new Color(0.19f, 0.17f, 0.13f, 1f);

        internal static GameObject Build(
            Transform parent,
            CityLayout layout,
            CityMountainBoundaryPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (!plan.IsEnabled)
            {
                return null;
            }

            var root = new GameObject("Mountain Boundary");
            root.transform.SetParent(parent, false);

            Transform ridges = new GameObject("Physical Ridges").transform;
            ridges.SetParent(root.transform, false);
            for (int index = 0; index < plan.Ridges.Count; index++)
            {
                CityMountainBoundaryMeshFactory.CreateRidge(
                    ridges,
                    plan.Ridges[index]);
            }

            if (plan.HasTunnel)
            {
                BuildTunnel(root.transform, layout, plan.Tunnel);
            }

            return root;
        }

        private static void BuildTunnel(
            Transform parent,
            CityLayout layout,
            CityMountainTunnelDescriptor tunnel)
        {
            var root = new GameObject("Sealed South Tunnel");
            root.transform.SetParent(parent, false);

            if (TryFindAccess(layout, tunnel, out CityOpenAreaAccessDescriptor
                    access))
            {
                BuildApproach(root.transform, layout, access, tunnel);
            }

            CityMountainBoundaryMeshFactory.CreatePortalFrame(
                root.transform,
                tunnel);
            BuildThroat(root.transform, tunnel);
            BuildSealedGate(root.transform, tunnel);
        }

        private static void BuildApproach(
            Transform parent,
            CityLayout layout,
            CityOpenAreaAccessDescriptor access,
            CityMountainTunnelDescriptor tunnel)
        {
            Vector3 axis = Flatten(tunnel.Axis);
            Vector3 start = access.Center + axis * 0.8f;
            Vector3 end = tunnel.PortalGroundCenter - axis * 0.75f;
            float length = Vector3.Dot(end - start, axis);
            if (length <= 0.25f)
            {
                return;
            }

            float width = Mathf.Min(
                access.Width - 0.8f,
                tunnel.OpeningWidth - 1.1f);
            int segmentCount = Mathf.Max(
                1,
                Mathf.CeilToInt(length / ApproachSegmentLength));
            float segmentLength = length / segmentCount;
            Quaternion rotation = Quaternion.LookRotation(axis, Vector3.up);
            var road = new List<RuntimeOrientedBox>(segmentCount);
            var tracks = new List<RuntimeOrientedBox>(segmentCount * 2);
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
            for (int index = 0; index < segmentCount; index++)
            {
                float distance = (index + 0.5f) * segmentLength;
                Vector3 center = start + axis * distance;
                center.y = SampleGroundTop(
                    layout,
                    center,
                    tunnel.PortalGroundCenter.y) +
                    ApproachLift;
                road.Add(new RuntimeOrientedBox(
                    center,
                    rotation,
                    new Vector3(
                        width,
                        ApproachThickness,
                        segmentLength + 0.06f)));

                for (int side = -1; side <= 1; side += 2)
                {
                    Vector3 trackCenter = center +
                        right * (TrackSeparation * 0.5f * side) +
                        Vector3.up * 0.012f;
                    tracks.Add(new RuntimeOrientedBox(
                        trackCenter,
                        rotation,
                        new Vector3(
                            TrackWidth,
                            ApproachThickness * 0.62f,
                            segmentLength + 0.08f)));
                }
            }

            GameObject roadObject =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Worn Tunnel Approach",
                    parent,
                    road,
                    ApproachSoil,
                    false,
                    CityExteriorAppearance.GroundTextureTileSize,
                    RuntimeWorldUvMode.XZPlanar);
            Renderer roadRenderer = roadObject.GetComponent<Renderer>();
            CityExteriorAppearance.ApplyGroundSurface(roadRenderer);
            RuntimePrimitiveFactory.SetColor(roadRenderer, ApproachSoil);
            roadRenderer.shadowCastingMode = ShadowCastingMode.Off;

            GameObject trackObject =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Tunnel Approach Wheel Ruts",
                    parent,
                    tracks,
                    TrackSoil,
                    false,
                    CityExteriorAppearance.GroundTextureTileSize,
                    RuntimeWorldUvMode.XZPlanar);
            Renderer trackRenderer = trackObject.GetComponent<Renderer>();
            CityExteriorAppearance.ApplyGroundSurface(trackRenderer);
            RuntimePrimitiveFactory.SetColor(trackRenderer, TrackSoil);
            trackRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void BuildThroat(
            Transform parent,
            CityMountainTunnelDescriptor tunnel)
        {
            Vector3 axis = Flatten(tunnel.Axis);
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
            Quaternion rotation = Quaternion.LookRotation(axis, Vector3.up);
            float depth = Mathf.Max(1.5f, tunnel.ThroatDepth);
            float wallThickness = 0.55f;
            float centreDistance = depth * 0.5f + 0.45f;
            float wallHeight = tunnel.OpeningHeight - 0.35f;
            Vector3 centre = tunnel.PortalGroundCenter +
                             axis * centreDistance;
            var lining = new List<RuntimeOrientedBox>(4);
            for (int side = -1; side <= 1; side += 2)
            {
                lining.Add(new RuntimeOrientedBox(
                    centre +
                    right * ((tunnel.OpeningWidth + wallThickness) *
                             0.5f * side) +
                    Vector3.up * (wallHeight * 0.5f),
                    rotation,
                    new Vector3(wallThickness, wallHeight, depth)));
            }

            lining.Add(new RuntimeOrientedBox(
                centre + Vector3.up * (tunnel.OpeningHeight + 0.20f),
                rotation,
                new Vector3(
                    tunnel.OpeningWidth + wallThickness * 2f,
                    0.55f,
                    depth)));
            lining.Add(new RuntimeOrientedBox(
                centre + Vector3.down * 0.18f,
                rotation,
                new Vector3(
                    tunnel.OpeningWidth + wallThickness,
                    0.36f,
                    depth)));

            GameObject liningObject =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Dark Rock Throat",
                    parent,
                    lining,
                    ThroatRock,
                    false,
                    CityMountainSurfaceAppearance.MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            Renderer renderer = liningObject.GetComponent<Renderer>();
            CityMountainSurfaceAppearance.ApplyCombined(
                renderer,
                ThroatRock);
            renderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        private static void BuildSealedGate(
            Transform parent,
            CityMountainTunnelDescriptor tunnel)
        {
            Vector3 axis = Flatten(tunnel.Axis);
            Vector3 right = Vector3.Cross(Vector3.up, axis).normalized;
            Quaternion rotation = Quaternion.LookRotation(axis, Vector3.up);
            float gateWidth = tunnel.OpeningWidth - 0.38f;
            float gateHeight = tunnel.OpeningHeight;
            Vector3 gateCenter = tunnel.PortalGroundCenter +
                axis * tunnel.GateInset +
                Vector3.up * (gateHeight * 0.5f);
            var gate = new List<RuntimeOrientedBox>(1)
            {
                new RuntimeOrientedBox(
                    gateCenter,
                    rotation,
                    new Vector3(gateWidth, gateHeight, 0.30f))
            };
            float pitch = CityRiverSurfaceAppearance
                .GetRecipe(CityRiverSurfaceKind.Iron)
                .MetersPerTile;
            GameObject gateObject =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Sealed Mountain Gate",
                    parent,
                    gate,
                    GateMetal,
                    true,
                    pitch,
                    RuntimeWorldUvMode.BoxProjected);
            CityRiverSurfaceAppearance.ApplyCombined(
                gateObject.GetComponent<Renderer>(),
                CityRiverSurfaceKind.Iron,
                GateMetal);

            var braces = new List<RuntimeOrientedBox>
            {
                new RuntimeOrientedBox(
                    gateCenter - axis * 0.18f,
                    rotation,
                    new Vector3(0.16f, gateHeight, 0.10f)),
                new RuntimeOrientedBox(
                    gateCenter - axis * 0.19f +
                    Vector3.up * (gateHeight * 0.16f),
                    rotation,
                    new Vector3(gateWidth, 0.18f, 0.10f)),
                new RuntimeOrientedBox(
                    gateCenter - axis * 0.19f -
                    Vector3.up * (gateHeight * 0.19f),
                    rotation,
                    new Vector3(gateWidth, 0.18f, 0.10f))
            };
            GameObject braceObject =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Sealed Gate Braces",
                    parent,
                    braces,
                    GateBrace,
                    false,
                    pitch,
                    RuntimeWorldUvMode.BoxProjected);
            CityRiverSurfaceAppearance.ApplyCombined(
                braceObject.GetComponent<Renderer>(),
                CityRiverSurfaceKind.Iron,
                GateBrace);

            GameObject warningPlate = RuntimePrimitiveFactory.CreateBox(
                "Closed Gate Warning Plate",
                parent,
                gateCenter - axis * 0.245f +
                right * (gateWidth * 0.22f) +
                Vector3.up * 0.35f,
                new Vector3(1.18f, 0.58f, 0.06f),
                new Color(0.58f, 0.48f, 0.20f),
                false);
            warningPlate.transform.rotation = rotation;
            warningPlate.GetComponent<Renderer>().shadowCastingMode =
                ShadowCastingMode.Off;
        }

        private static bool TryFindAccess(
            CityLayout layout,
            CityMountainTunnelDescriptor tunnel,
            out CityOpenAreaAccessDescriptor access)
        {
            for (int index = 0;
                 index < layout.OpenAreaAccesses.Count;
                 index++)
            {
                CityOpenAreaAccessDescriptor candidate =
                    layout.OpenAreaAccesses[index];
                if (string.Equals(
                        candidate.Id,
                        tunnel.TargetAccessId,
                        StringComparison.Ordinal) &&
                    string.Equals(
                        candidate.AreaId,
                        tunnel.AreaId,
                        StringComparison.Ordinal))
                {
                    access = candidate;
                    return true;
                }
            }

            access = default;
            return false;
        }

        private static float SampleGroundTop(
            CityLayout layout,
            Vector3 position,
            float fallback)
        {
            return CityTerrainSurfacePlan.TrySampleGroundTop(
                layout,
                new Vector2(position.x, position.z),
                out float topY,
                out _)
                ? topY
                : fallback;
        }

        private static Vector3 Flatten(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                throw new ArgumentException(
                    "A tunnel axis must have an XZ component.",
                    nameof(direction));
            }

            return direction.normalized;
        }
    }
}
