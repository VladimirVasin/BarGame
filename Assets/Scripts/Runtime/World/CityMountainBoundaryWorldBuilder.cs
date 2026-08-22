using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the close-range western and southern mountain boundary plus the
    /// deliberately sealed southern tunnel stub. The natural south-west
    /// corner ground closes the view but remains behind the road fence; the
    /// tunnel creates no navigation continuation, interaction or transition.
    /// </summary>
    internal static class CityMountainBoundaryWorldBuilder
    {
        internal const float ThroatPortalOffset = 0.45f;
        internal const float ThroatJointOverlap = 0.04f;
        internal const float ThroatFloorGroundOverlap = 0.25f;
        internal const float ThroatFloorSurfaceLift = 0.03f;
        internal const float ThroatFloorThickness = 0.36f;

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

            if (plan.HasSouthWestCornerClosure)
            {
                CityMountainBoundaryMeshFactory.CreateCornerClosure(
                    root.transform,
                    plan.SouthWestCornerClosure);
            }

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
                BuildTunnel(root.transform, plan.Tunnel);
            }

            if (plan.HasRiverCave)
            {
                BuildRiverCave(root.transform, plan.RiverCave);
            }

            return root;
        }

        private static void BuildRiverCave(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            var root = new GameObject("South River Cave");
            root.transform.SetParent(parent, false);
            BuildRiverCaveForefield(root.transform, cave);
            BuildRiverCaveRockStop(root.transform, cave);
            BuildRiverCaveLining(root.transform, cave);
        }

        private static void BuildRiverCaveForefield(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            const float thickness = 0.22f;
            var shoulders = new List<RuntimeOrientedBox>(2);
            AddSlopedGround(
                shoulders,
                Rect.MinMaxRect(
                    cave.ApproachBounds.xMin,
                    cave.ApproachBounds.yMin,
                    cave.WestBankBounds.xMin,
                    cave.ApproachBounds.yMax),
                cave.WestMouthBankY,
                cave.WestCityBankY,
                thickness);
            AddSlopedGround(
                shoulders,
                Rect.MinMaxRect(
                    cave.EastBankBounds.xMax,
                    cave.ApproachBounds.yMin,
                    cave.ApproachBounds.xMax,
                    cave.ApproachBounds.yMax),
                cave.EastMouthBankY,
                cave.EastCityBankY,
                thickness);
            if (shoulders.Count == 0)
            {
                return;
            }

            HomeSurfaceRecipe recipe =
                CityFringeYardSurfaceAppearance.GetRecipe(
                    CityFringeYardSurfaceKind.ForefieldGround);
            GameObject forefield =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "River Cave Forefield",
                    parent,
                    shoulders,
                    CityExteriorAppearance.YardGround,
                    true,
                    recipe.MetersPerTile,
                    RuntimeWorldUvMode.XZPlanar);
            CityFringeYardSurfaceAppearance.ApplyCombined(
                forefield.GetComponent<Renderer>(),
                CityFringeYardSurfaceKind.ForefieldGround,
                CityExteriorAppearance.YardGround);
        }

        private static void AddSlopedGround(
            ICollection<RuntimeOrientedBox> target,
            Rect bounds,
            float southTopY,
            float northTopY,
            float thickness)
        {
            if (bounds.width <= 0.01f || bounds.height <= 0.01f)
            {
                return;
            }

            Vector3 start = new Vector3(
                bounds.center.x,
                southTopY - thickness * 0.5f,
                bounds.yMin);
            Vector3 end = new Vector3(
                bounds.center.x,
                northTopY - thickness * 0.5f,
                bounds.yMax);
            Vector3 delta = end - start;
            target.Add(new RuntimeOrientedBox(
                (start + end) * 0.5f,
                Quaternion.LookRotation(delta.normalized, Vector3.up),
                new Vector3(bounds.width, thickness, delta.magnitude)));
        }

        private static void BuildRiverCaveRockStop(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            const float ringThickness = 1.15f;
            const float facadeDepth = 4f;
            const float overlap = 0.12f;
            float portalBaseY = cave.BaseY +
                                CitySurfaceDescriptor.WaterTopOffset -
                                CityRiverWorldBuilder.RiverBedDepth;
            float mouthZ = cave.ApproachBounds.yMin;
            var portalDescriptor = new CityMountainTunnelDescriptor(
                "mountain-south-river-cave-portal",
                string.Empty,
                string.Empty,
                new Vector3(
                    cave.WaterApproachBounds.center.x,
                    portalBaseY,
                    mouthZ),
                cave.Axis,
                cave.MouthBounds,
                cave.ApproachBounds,
                cave.WaterApproachBounds.width,
                cave.OpeningHeight,
                cave.ThroatDepth,
                0f,
                false);
            GameObject portal =
                CityMountainBoundaryMeshFactory.CreatePortalFrame(
                    parent,
                    portalDescriptor);
            portal.name = "River Cave Portal";

            float leftInner =
                cave.WaterApproachBounds.xMin - ringThickness;
            float rightInner =
                cave.WaterApproachBounds.xMax + ringThickness;
            float crownBottomY = portalBaseY + cave.OpeningHeight - overlap;
            // One flat-topped massif at the HIGHER of the two adjoining
            // ridge peaks. The notch splits the ridge line, so there is
            // no rock behind the facade: with the crown at the LOWER
            // peak, the taller side stood 4+ m above it and the gap
            // between them read as a bright hole in the mountain.
            float facadeTopY = Mathf.Max(
                crownBottomY + 4f,
                Mathf.Max(cave.WestPeakY, cave.EastPeakY));
            float facadeBottomY = portalBaseY - 0.45f;
            Quaternion rotation = Quaternion.LookRotation(
                Flatten(cave.Axis),
                Vector3.up);
            Vector3 depthOffset = Flatten(cave.Axis) *
                                  (facadeDepth * 0.5f);
            var rock = new List<RuntimeOrientedBox>(3);
            AddFacadeBox(
                rock,
                cave.ApproachBounds.xMin,
                leftInner + overlap,
                facadeBottomY,
                facadeTopY,
                mouthZ,
                depthOffset,
                rotation,
                facadeDepth);
            AddFacadeBox(
                rock,
                rightInner - overlap,
                cave.ApproachBounds.xMax,
                facadeBottomY,
                facadeTopY,
                mouthZ,
                depthOffset,
                rotation,
                facadeDepth);
            AddFacadeBox(
                rock,
                leftInner,
                rightInner,
                crownBottomY,
                facadeTopY,
                mouthZ,
                depthOffset,
                rotation,
                facadeDepth);
            GameObject stop =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "River Cave Rock Stop",
                    parent,
                    rock,
                    MidRock,
                    true,
                    CityMountainSurfaceAppearance.MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            // The same fog-safe dither the physical ridges wear: the
            // facade fills the notch the ridges leave, and a facade that
            // stays opaque while the rock beside it dissolves into the
            // night fog splits the massif's silhouette at the seam - a
            // bright hole in the mountain right over the cave mouth.
            CityMountainSurfaceAppearance.ApplyPhysicalRidge(
                stop.GetComponent<Renderer>(),
                MidRock);
        }

        private static void AddFacadeBox(
            ICollection<RuntimeOrientedBox> target,
            float xMin,
            float xMax,
            float bottomY,
            float topY,
            float mouthZ,
            Vector3 depthOffset,
            Quaternion rotation,
            float depth)
        {
            float width = xMax - xMin;
            float height = topY - bottomY;
            if (width <= 0.01f || height <= 0.01f)
            {
                return;
            }

            target.Add(new RuntimeOrientedBox(
                new Vector3(
                    (xMin + xMax) * 0.5f,
                    (bottomY + topY) * 0.5f,
                    mouthZ) + depthOffset,
                rotation,
                new Vector3(width, height, depth)));
        }

        private static void BuildRiverCaveLining(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            const float wallThickness = 0.90f;
            const float portalOverlap = 0.45f;
            float portalBaseY = cave.BaseY +
                                CitySurfaceDescriptor.WaterTopOffset -
                                CityRiverWorldBuilder.RiverBedDepth;
            Vector3 axis = Flatten(cave.Axis);
            Quaternion rotation = Quaternion.LookRotation(axis, Vector3.up);
            float depth = cave.ThroatDepth + portalOverlap;
            float centreDistance = depth * 0.5f;
            float centreX = cave.WaterApproachBounds.center.x;
            float halfWidth = cave.WaterApproachBounds.width * 0.5f;
            Vector3 origin = new Vector3(
                centreX,
                portalBaseY,
                cave.ApproachBounds.yMin);
            var lining = new List<RuntimeOrientedBox>(4)
            {
                new RuntimeOrientedBox(
                    origin + axis * centreDistance +
                    Vector3.left * (halfWidth + wallThickness * 0.5f) +
                    Vector3.up * (cave.OpeningHeight * 0.5f),
                    rotation,
                    new Vector3(
                        wallThickness,
                        cave.OpeningHeight,
                        depth)),
                new RuntimeOrientedBox(
                    origin + axis * centreDistance +
                    Vector3.right * (halfWidth + wallThickness * 0.5f) +
                    Vector3.up * (cave.OpeningHeight * 0.5f),
                    rotation,
                    new Vector3(
                        wallThickness,
                        cave.OpeningHeight,
                        depth)),
                new RuntimeOrientedBox(
                    origin + axis * centreDistance +
                    Vector3.up * (cave.OpeningHeight + 0.35f),
                    rotation,
                    new Vector3(
                        cave.WaterApproachBounds.width +
                        wallThickness * 2f,
                        0.70f,
                        depth)),
                new RuntimeOrientedBox(
                    origin + axis * (cave.ThroatDepth + 0.45f) +
                    Vector3.up * (cave.OpeningHeight * 0.5f),
                    rotation,
                    new Vector3(
                        cave.WaterApproachBounds.width +
                        wallThickness * 2f,
                        cave.OpeningHeight,
                        0.90f))
            };
            GameObject liningObject =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "River Cave Dark Lining",
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

        private static void BuildTunnel(
            Transform parent,
            CityMountainTunnelDescriptor tunnel)
        {
            var root = new GameObject("Sealed South Tunnel");
            root.transform.SetParent(parent, false);

            CityMountainBoundaryMeshFactory.CreatePortalFrame(
                root.transform,
                tunnel);
            BuildThroat(root.transform, tunnel);
            BuildSealedGate(root.transform, tunnel);
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
            float centreDistance = depth * 0.5f + ThroatPortalOffset;
            float wallHeight = tunnel.OpeningHeight;
            Vector3 centre = tunnel.PortalGroundCenter +
                             axis * centreDistance;
            var lining = new List<RuntimeOrientedBox>(3);
            for (int side = -1; side <= 1; side += 2)
            {
                lining.Add(new RuntimeOrientedBox(
                    centre +
                    right * (((tunnel.OpeningWidth + wallThickness) *
                              0.5f + ThroatJointOverlap) * side) +
                    Vector3.up *
                    (wallHeight * 0.5f - ThroatJointOverlap),
                    rotation,
                    new Vector3(wallThickness, wallHeight, depth)));
            }

            lining.Add(new RuntimeOrientedBox(
                centre + Vector3.up * (tunnel.OpeningHeight + 0.20f),
                rotation,
                new Vector3(
                    tunnel.OpeningWidth + wallThickness * 2f +
                    ThroatJointOverlap * 4f,
                    0.55f,
                    depth)));

            float floorEndDistance = depth + ThroatPortalOffset;
            float floorDepth = floorEndDistance +
                               ThroatFloorGroundOverlap;
            float floorCentreDistance =
                (floorEndDistance - ThroatFloorGroundOverlap) * 0.5f;
            var floor = new List<RuntimeOrientedBox>(1)
            {
                new RuntimeOrientedBox(
                    tunnel.PortalGroundCenter +
                    axis * floorCentreDistance +
                    Vector3.up *
                    (ThroatFloorSurfaceLift -
                     ThroatFloorThickness * 0.5f),
                    rotation,
                    new Vector3(
                        tunnel.OpeningWidth + wallThickness,
                        ThroatFloorThickness,
                        floorDepth))
            };

            GameObject floorObject =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Tunnel Floor",
                    parent,
                    floor,
                    ThroatRock,
                    false,
                    CityMountainSurfaceAppearance.MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            Renderer floorRenderer = floorObject.GetComponent<Renderer>();
            CityMountainSurfaceAppearance.ApplyCombined(
                floorRenderer,
                ThroatRock);
            floorRenderer.shadowCastingMode = ShadowCastingMode.Off;

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
