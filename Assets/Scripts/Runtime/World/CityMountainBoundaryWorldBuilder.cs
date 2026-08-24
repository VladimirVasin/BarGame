using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    /// <summary>
    /// Builds the close-range western and southern mountain boundary plus the
    /// open, visually deep southern tunnel. The natural south-west corner
    /// ground closes the view but remains behind the road fence; travel beyond
    /// the safe entrance corridor is still owned by a separate interaction.
    /// </summary>
    internal static class CityMountainBoundaryWorldBuilder
    {
        internal const float ThroatPortalOffset = 0.45f;
        internal const float ThroatJointOverlap = 0.04f;
        internal const float ThroatFloorGroundOverlap = 0.25f;
        internal const float ThroatFloorSurfaceLift = 0.03f;
        internal const float ThroatFloorThickness = 0.36f;
        internal const float TunnelSegmentOverlap = 0.18f;

        internal static readonly Color ForeRock =
            new Color(0.21f, 0.235f, 0.215f, 1f);
        internal static readonly Color MidRock =
            new Color(0.255f, 0.28f, 0.255f, 1f);
        internal static readonly Color HighRock =
            new Color(0.295f, 0.32f, 0.30f, 1f);

        private static readonly Color ThroatRock =
            new Color(0.105f, 0.120f, 0.112f, 1f);
        private static readonly Color IronMetal =
            new Color(0.155f, 0.185f, 0.175f, 1f);
        private static readonly Color DarkIron =
            new Color(0.105f, 0.125f, 0.120f, 1f);

        // The water mouth's one lamp: hand-lamp kerosene warmth, sized
        // between the door bulbs (64-110 @ 7-8 m) and the yard
        // floodlight (150 @ 16 m) so it carries the whole 10 m mouth,
        // with the hand lamp's "still burning at noon" day floor.
        private const float CaveLampNightIntensity = 120f;
        private const float CaveLampDayIntensity = 26f;
        private const float CaveLampRange = 15f;
        private static readonly Color CaveLampColor =
            new Color(1.00f, 0.74f, 0.42f);
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
                BuildTunnel(root.transform, plan.Tunnel, plan);
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
            BuildRiverCaveLamp(root.transform, cave);
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
                0f,
                0f,
                0f,
                false,
                false,
                Array.Empty<CityMountainTunnelSegmentDescriptor>());
            GameObject portal =
                CityMountainBoundaryMeshFactory.CreatePortalFrame(
                    parent,
                    portalDescriptor);
            portal.name = "River Cave Portal";

            // One flat-topped massif at the HIGHER of the two adjoining
            // ridge peaks. The notch splits the ridge line, so there is
            // no rock behind the facade: with the crown at the LOWER
            // peak, the taller side stood 4+ m above it and the gap
            // between them read as a bright hole in the mountain.
            float facadeTopY = Mathf.Max(
                portalBaseY + cave.OpeningHeight - overlap + 4f,
                Mathf.Max(cave.WestPeakY, cave.EastPeakY));
            var rock = new List<RuntimeOrientedBox>(7);
            AddPortalBackstop(
                rock,
                cave.WaterApproachBounds.center.x,
                cave.WaterApproachBounds.width * 0.5f,
                portalBaseY,
                cave.OpeningHeight,
                cave.ApproachBounds.xMin,
                cave.ApproachBounds.xMax,
                facadeTopY,
                mouthZ,
                Flatten(cave.Axis),
                0f);
            GameObject stop =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "River Cave Rock Stop",
                    parent,
                    rock,
                    MidRock,
                    true,
                    CityMountainSurfaceAppearance.MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            // Deliberately the ordinary opaque rock, NOT the ridges'
            // fog-handoff dither: the facade has the cave void and the
            // portal behind it rather than the backdrop shell, so a
            // dithered facade dissolves in night fog into a glowing
            // hole with a floating arch (verified in play).
            CityMountainSurfaceAppearance.ApplyCombined(
                stop.GetComponent<Renderer>(),
                MidRock);
        }

        /// <summary>
        /// The rock face a portal stands in: flat flanks beside the ring,
        /// a crown above it, and the SPANDRELS - the portal ring is a
        /// semicircle inside a rectangular opening, and without stepped
        /// blocks hugging its outer arc the two upper corners of that
        /// rectangle stay open, with a sightline down the throat past the
        /// far clip: a fog-bright gap in each corner. Shared by the river
        /// cave and the southern tunnel, which have the identical hole.
        /// Depth staggers (flanks 0, crown +0.03, spandrels +0.06) keep
        /// every overlap strip off a shared front plane, so nothing
        /// z-fights beside the arch.
        /// </summary>
        private static void AddPortalBackstop(
            ICollection<RuntimeOrientedBox> target,
            float centreX,
            float innerRadius,
            float baseY,
            float openingHeight,
            float approachXMin,
            float approachXMax,
            float facadeTopY,
            float mouthZ,
            Vector3 axisFlat,
            float setback)
        {
            const float ringThickness = 1.15f;
            const float overlap = 0.12f;
            const float facadeDepth = 4f;
            float outerRadius = innerRadius + ringThickness;
            float leftInner = centreX - outerRadius;
            float rightInner = centreX + outerRadius;
            float crownBottomY = baseY + openingHeight - overlap;
            float facadeBottomY = baseY - 0.45f;
            Quaternion rotation = Quaternion.LookRotation(
                axisFlat,
                Vector3.up);
            Vector3 depthOffset = axisFlat *
                                  ((facadeDepth * 0.5f) + setback);

            AddFacadeBox(
                target,
                approachXMin,
                leftInner + overlap,
                facadeBottomY,
                facadeTopY,
                mouthZ,
                depthOffset,
                rotation,
                facadeDepth);
            AddFacadeBox(
                target,
                rightInner - overlap,
                approachXMax,
                facadeBottomY,
                facadeTopY,
                mouthZ,
                depthOffset,
                rotation,
                facadeDepth);
            AddFacadeBox(
                target,
                leftInner,
                rightInner,
                crownBottomY,
                facadeTopY,
                mouthZ,
                depthOffset + (axisFlat * 0.03f),
                rotation,
                facadeDepth);

            // The spandrel steps, derived from the ring radii so any
            // authored mouth works: the tall step's inner edge sits just
            // outside the inner radius at spring level; the short step
            // rises from where the outer arc crosses the tall step's
            // edge, its inner-bottom corner clamped outside the inner
            // arc; past the point where the outer arc reaches the crown
            // the ring band itself closes the corner.
            float springY = baseY + openingHeight - innerRadius;
            float crownRelative = innerRadius - overlap;
            float topY = crownBottomY + 0.05f;
            float tallInset = innerRadius + 0.10f;
            float dxCrown = Mathf.Sqrt(Mathf.Max(
                0.01f,
                (outerRadius * outerRadius) -
                (crownRelative * crownRelative)));
            float shortInset = dxCrown - 0.04f;
            float shortBottomRelative = Mathf.Sqrt(Mathf.Max(
                0f,
                (outerRadius * outerRadius) -
                (tallInset * tallInset))) - 0.05f;
            if (shortInset < innerRadius)
            {
                shortBottomRelative = Mathf.Max(
                    shortBottomRelative,
                    Mathf.Sqrt(
                        (innerRadius * innerRadius) -
                        (shortInset * shortInset)) + 0.05f);
            }

            float shortBottomY = springY + shortBottomRelative;
            Vector3 spandrelOffset =
                depthOffset + (axisFlat * 0.06f);
            for (int side = -1; side <= 1; side += 2)
            {
                float outerX = centreX + (side * outerRadius);
                float tallX = centreX + (side * tallInset);
                float shortX = centreX + (side * shortInset);
                AddFacadeBox(
                    target,
                    Mathf.Min(outerX, tallX),
                    Mathf.Max(outerX, tallX),
                    springY,
                    topY,
                    mouthZ,
                    spandrelOffset,
                    rotation,
                    facadeDepth);
                if (shortBottomY < topY - 0.01f &&
                    shortInset < tallInset)
                {
                    AddFacadeBox(
                        target,
                        Mathf.Min(tallX, shortX),
                        Mathf.Max(tallX, shortX),
                        shortBottomY,
                        topY,
                        mouthZ,
                        spandrelOffset,
                        rotation,
                        facadeDepth);
                }
            }
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

        /// <summary>
        /// The noticeable light the user asked the water mouth to own:
        /// an iron-hooded lamp on the arch crown, registered like the
        /// boat-station hut bulb - a glow lens, a real point light with a halo,
        /// and the site registry dimming it to a day floor so "still
        /// burning" reads at noon too.
        /// </summary>
        private static void BuildRiverCaveLamp(
            Transform parent,
            CityMountainRiverNotchDescriptor cave)
        {
            float portalBaseY = cave.BaseY +
                                CitySurfaceDescriptor.WaterTopOffset -
                                CityRiverWorldBuilder.RiverBedDepth;
            Vector3 axis = Flatten(cave.Axis);
            var assembly = new GameObject("River Cave Portal Lamp");
            assembly.transform.SetParent(parent, false);
            assembly.transform.SetPositionAndRotation(
                new Vector3(
                    cave.WaterApproachBounds.center.x,
                    portalBaseY + cave.OpeningHeight + 0.85f,
                    cave.ApproachBounds.yMin) - (axis * 0.55f),
                Quaternion.LookRotation(axis, Vector3.up));

            RuntimePrimitiveFactory.CreateBox(
                "Cave Lamp Bracket",
                assembly.transform,
                new Vector3(0f, 0.28f, 0.30f),
                new Vector3(0.16f, 0.20f, 0.75f),
                DarkIron,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Cave Lamp Hood",
                assembly.transform,
                new Vector3(0f, 0.12f, 0f),
                new Vector3(0.66f, 0.16f, 0.52f),
                IronMetal,
                false);
            Color glow = MultiplyRgb(CaveLampColor, 4.6f, 1f);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Cave Lamp Lens",
                assembly.transform,
                Vector3.zero,
                new Vector3(0.40f, 0.14f, 0.30f),
                glow,
                CityNightResources.EmissiveMaterial,
                false);
            CityNightGlowRegistry.Register(
                lens.GetComponent<Renderer>(),
                glow);

            var emitter = new GameObject("Cave Lamp Light");
            emitter.transform.SetParent(assembly.transform, false);
            emitter.transform.localPosition =
                new Vector3(0f, -0.18f, 0f);
            Light light = emitter.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = CaveLampColor;
            light.intensity = CaveLampNightIntensity;
            light.range = CaveLampRange;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.lightmapBakeType = LightmapBakeType.Realtime;

            var haloObject = new GameObject("Cave Lamp Halo");
            haloObject.transform.SetParent(emitter.transform, false);
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.55f,
                1.60f,
                MultiplyRgb(CaveLampColor, 4.2f, 0.18f),
                MultiplyRgb(CaveLampColor, 2.1f, 0.05f));
            CityNightSiteLightRegistry.Register(
                light,
                CaveLampNightIntensity,
                CaveLampDayIntensity,
                halo);
        }

        private static Color MultiplyRgb(
            Color color,
            float multiplier,
            float alpha)
        {
            return new Color(
                color.r * multiplier,
                color.g * multiplier,
                color.b * multiplier,
                alpha);
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
            CityMountainTunnelDescriptor tunnel,
            CityMountainBoundaryPlan plan)
        {
            var root = new GameObject("Open South Tunnel");
            root.transform.SetParent(parent, false);

            CityMountainBoundaryMeshFactory.CreatePortalFrame(
                root.transform,
                tunnel);
            BuildThroat(root.transform, tunnel);
            BuildTunnelPortalRockFacade(root.transform, tunnel, plan);
        }

        /// <summary>
        /// The ridge line breaks for the tunnel exactly like it does for
        /// the river notch, and nothing stood in the gap above the arch:
        /// wedges of open sky between the tapering ridge ends, plus the
        /// same spandrel corners the river mouth had. The shared rock facade
        /// closes only that mountain gap, topped at the taller station.
        /// </summary>
        private static void BuildTunnelPortalRockFacade(
            Transform parent,
            CityMountainTunnelDescriptor tunnel,
            CityMountainBoundaryPlan plan)
        {
            float top = tunnel.PortalGroundCenter.y +
                        tunnel.OpeningHeight + 4f;
            for (int ridgeIndex = 0;
                 ridgeIndex < plan.Ridges.Count;
                 ridgeIndex++)
            {
                CityMountainRidgeDescriptor ridge =
                    plan.Ridges[ridgeIndex];
                if (ridge.Side != CityMountainBoundarySide.South)
                {
                    continue;
                }

                for (int stationIndex = 0;
                     stationIndex < ridge.Stations.Count;
                     stationIndex++)
                {
                    CityMountainRidgeStation station =
                        ridge.Stations[stationIndex];
                    float x = station.WorldXZ.x;
                    if (x >= tunnel.ApproachBounds.xMin - 1.5f &&
                        x <= tunnel.ApproachBounds.xMax + 1.5f)
                    {
                        top = Mathf.Max(top, station.PeakY);
                    }
                }
            }

            var rock = new List<RuntimeOrientedBox>(7);
            AddPortalBackstop(
                rock,
                tunnel.PortalGroundCenter.x,
                tunnel.OpeningWidth * 0.5f,
                tunnel.PortalGroundCenter.y,
                tunnel.OpeningHeight,
                tunnel.ApproachBounds.xMin,
                tunnel.ApproachBounds.xMax,
                top,
                tunnel.PortalGroundCenter.z,
                Flatten(tunnel.Axis),
                // Set back behind the mouth-plane furniture (pediment,
                // portal lamp housing) so nothing shares a front plane.
                0.45f);
            GameObject stop =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    "Tunnel Portal Rock Facade",
                    parent,
                    rock,
                    MidRock,
                    true,
                    CityMountainSurfaceAppearance.MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            // Ordinary opaque rock, same reasoning as the river cave
            // facade: the throat sits behind it, not the backdrop ring.
            CityMountainSurfaceAppearance.ApplyCombined(
                stop.GetComponent<Renderer>(),
                MidRock);
        }

        private static void BuildThroat(
            Transform parent,
            CityMountainTunnelDescriptor tunnel)
        {
            const float wallThickness = 0.55f;
            var physicalFloor = new List<RuntimeOrientedBox>();
            var visualFloor = new List<RuntimeOrientedBox>();
            var physicalLining = new List<RuntimeOrientedBox>();
            var visualLining = new List<RuntimeOrientedBox>();

            for (int index = 0; index < tunnel.Segments.Count; index++)
            {
                CityMountainTunnelSegmentDescriptor segment =
                    tunnel.Segments[index];
                Vector3 forward = segment.Forward;
                Vector3 right = Vector3.Cross(
                    Vector3.up,
                    forward).normalized;
                Quaternion rotation = Quaternion.LookRotation(
                    forward,
                    Vector3.up);
                bool bendsAtStart = index > 0 &&
                    Vector3.Angle(
                        tunnel.Segments[index - 1].Forward,
                        forward) > 0.01f;
                bool bendsAtEnd = index < tunnel.Segments.Count - 1 &&
                    Vector3.Angle(
                        forward,
                        tunnel.Segments[index + 1].Forward) > 0.01f;
                float startExtension = index == 0
                    ? -ThroatPortalOffset
                    : bendsAtStart
                        ? TunnelSegmentOverlap
                        : 0f;
                float endExtension = index == tunnel.Segments.Count - 1
                    ? 0f
                    : bendsAtEnd
                        ? TunnelSegmentOverlap
                        : 0f;
                float length = segment.Length +
                               startExtension +
                               endExtension;
                Vector3 centre = segment.Center +
                    forward * ((endExtension - startExtension) * 0.5f);
                List<RuntimeOrientedBox> lining = segment.HasCollision
                    ? physicalLining
                    : visualLining;
                for (int side = -1; side <= 1; side += 2)
                {
                    lining.Add(new RuntimeOrientedBox(
                        centre +
                        right * (((tunnel.OpeningWidth + wallThickness) *
                                  0.5f + ThroatJointOverlap) * side) +
                        Vector3.up *
                        (tunnel.OpeningHeight * 0.5f -
                         ThroatJointOverlap),
                        rotation,
                        new Vector3(
                            wallThickness,
                            tunnel.OpeningHeight,
                            length)));
                }

                lining.Add(new RuntimeOrientedBox(
                    centre +
                    Vector3.up * (tunnel.OpeningHeight + 0.20f),
                    rotation,
                    new Vector3(
                        tunnel.OpeningWidth + wallThickness * 2f +
                        ThroatJointOverlap * 4f,
                        0.55f,
                        length)));

                float floorStartExtension = index == 0
                    ? ThroatFloorGroundOverlap
                    : bendsAtStart
                        ? TunnelSegmentOverlap
                        : 0f;
                float floorLength = segment.Length +
                                    floorStartExtension +
                                    endExtension;
                Vector3 floorCentre = segment.Center +
                    forward *
                    ((endExtension - floorStartExtension) * 0.5f) +
                    Vector3.up *
                    (ThroatFloorSurfaceLift -
                     ThroatFloorThickness * 0.5f);
                List<RuntimeOrientedBox> floor = segment.HasCollision
                    ? physicalFloor
                    : visualFloor;
                floor.Add(new RuntimeOrientedBox(
                    floorCentre,
                    rotation,
                    new Vector3(
                        tunnel.OpeningWidth + wallThickness,
                        ThroatFloorThickness,
                        floorLength)));
            }

            CreateTunnelRockBatch(
                "Tunnel Floor Physical",
                parent,
                physicalFloor,
                true);
            CreateTunnelRockBatch(
                "Tunnel Floor Continuation",
                parent,
                visualFloor,
                false);
            CreateTunnelRockBatch(
                "Tunnel Lining Physical",
                parent,
                physicalLining,
                true);
            CreateTunnelRockBatch(
                "Tunnel Lining Continuation",
                parent,
                visualLining,
                false);
            BuildTunnelTechnicalDetails(parent, tunnel);
        }

        private static void CreateTunnelRockBatch(
            string name,
            Transform parent,
            IReadOnlyList<RuntimeOrientedBox> boxes,
            bool collider)
        {
            GameObject built =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    name,
                    parent,
                    boxes,
                    ThroatRock,
                    collider,
                    CityMountainSurfaceAppearance.MetersPerTile,
                    RuntimeWorldUvMode.BoxProjected);
            Renderer renderer = built.GetComponent<Renderer>();
            CityMountainSurfaceAppearance.ApplyCombined(
                renderer,
                ThroatRock);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
        }

        private static void BuildTunnelTechnicalDetails(
            Transform parent,
            CityMountainTunnelDescriptor tunnel)
        {
            var seams = new List<RuntimeOrientedBox>();
            var drains = new List<RuntimeOrientedBox>();
            var cables = new List<RuntimeOrientedBox>();
            var supports = new List<RuntimeOrientedBox>();
            float halfWidth = tunnel.OpeningWidth * 0.5f;

            for (int index = 0; index < tunnel.Segments.Count; index++)
            {
                CityMountainTunnelSegmentDescriptor segment =
                    tunnel.Segments[index];
                Vector3 forward = segment.Forward;
                Vector3 right = Vector3.Cross(
                    Vector3.up,
                    forward).normalized;
                Quaternion rotation = Quaternion.LookRotation(
                    forward,
                    Vector3.up);
                bool bendsAtStart = index > 0 &&
                    Vector3.Angle(
                        tunnel.Segments[index - 1].Forward,
                        forward) > 0.01f;
                bool bendsAtEnd = index < tunnel.Segments.Count - 1 &&
                    Vector3.Angle(
                        forward,
                        tunnel.Segments[index + 1].Forward) > 0.01f;
                float detailStartExtension = bendsAtStart ? 0.08f : 0f;
                float detailEndExtension = bendsAtEnd ? 0.08f : 0f;
                float detailLength = segment.Length +
                                     detailStartExtension +
                                     detailEndExtension;
                Vector3 detailCentre = segment.Center +
                    forward *
                    ((detailEndExtension - detailStartExtension) * 0.5f);
                for (int side = -1; side <= 1; side += 2)
                {
                    drains.Add(new RuntimeOrientedBox(
                        detailCentre +
                        right * ((halfWidth - 0.24f) * side) +
                        Vector3.up *
                        (ThroatFloorSurfaceLift + 0.025f),
                        rotation,
                        new Vector3(0.22f, 0.05f, detailLength)));
                }

                cables.Add(new RuntimeOrientedBox(
                    detailCentre +
                    right * (halfWidth + 0.055f) +
                    Vector3.up * 2.85f,
                    rotation,
                    new Vector3(0.08f, 0.08f, detailLength)));
            }

            for (float distance =
                     CityMountainBoundaryDefinition.TunnelSegmentLength;
                 distance < tunnel.VisualDepth - 0.01f;
                 distance +=
                     CityMountainBoundaryDefinition.TunnelSegmentLength)
            {
                CityMountainTunnelPathSample sample =
                    tunnel.SamplePath(distance);
                Vector3 before = tunnel
                    .SamplePath(Mathf.Max(0f, distance - 0.01f))
                    .Forward;
                Vector3 after = tunnel
                    .SamplePath(Mathf.Min(
                        tunnel.VisualDepth,
                        distance + 0.01f))
                    .Forward;
                Vector3 jointForward = (before + after).normalized;
                Vector3 right = Vector3.Cross(
                    Vector3.up,
                    jointForward).normalized;
                Quaternion rotation = Quaternion.LookRotation(
                    jointForward,
                    Vector3.up);
                for (int side = -1; side <= 1; side += 2)
                {
                    seams.Add(new RuntimeOrientedBox(
                        sample.Position +
                        right * ((halfWidth + 0.02f) * side) +
                        Vector3.up * (tunnel.OpeningHeight * 0.5f),
                        rotation,
                        new Vector3(
                            0.22f,
                            tunnel.OpeningHeight,
                            0.80f)));
                }

                seams.Add(new RuntimeOrientedBox(
                    sample.Position +
                    Vector3.up * (tunnel.OpeningHeight - 0.10f),
                    rotation,
                    new Vector3(
                        tunnel.OpeningWidth + 0.32f,
                        0.20f,
                        0.80f)));
                seams.Add(new RuntimeOrientedBox(
                    sample.Position +
                    Vector3.up * (ThroatFloorSurfaceLift + 0.04f),
                    rotation,
                    new Vector3(
                        tunnel.OpeningWidth + 0.20f,
                        0.08f,
                        0.80f)));
                supports.Add(new RuntimeOrientedBox(
                    sample.Position +
                    right * (halfWidth - 0.02f) +
                    Vector3.up * 2.75f,
                    rotation,
                    new Vector3(0.42f, 0.10f, 0.12f)));
            }

            CreateTunnelMetalBatch(
                "Tunnel Technical Seams",
                parent,
                seams,
                IronMetal);
            CreateTunnelMetalBatch(
                "Tunnel Drainage Channels",
                parent,
                drains,
                DarkIron);
            CreateTunnelMetalBatch(
                "Tunnel Cable Run",
                parent,
                cables,
                DarkIron);
            CreateTunnelMetalBatch(
                "Tunnel Cable Supports",
                parent,
                supports,
                IronMetal);
        }

        private static void CreateTunnelMetalBatch(
            string name,
            Transform parent,
            IReadOnlyList<RuntimeOrientedBox> boxes,
            Color color)
        {
            float pitch = CityRiverSurfaceAppearance
                .GetRecipe(CityRiverSurfaceKind.Iron)
                .MetersPerTile;
            GameObject built =
                RuntimePrimitiveFactory.CreateCombinedOrientedBoxes(
                    name,
                    parent,
                    boxes,
                    color,
                    false,
                    pitch,
                    RuntimeWorldUvMode.BoxProjected);
            Renderer renderer = built.GetComponent<Renderer>();
            CityRiverSurfaceAppearance.ApplyCombined(
                renderer,
                CityRiverSurfaceKind.Iron,
                color);
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
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
