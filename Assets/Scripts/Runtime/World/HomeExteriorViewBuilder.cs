using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Rebuilds a bounded, collider-free view of the player's real seeded
    /// street in Home coordinates. It deliberately creates no city gameplay
    /// root, player, camera, entrance or realtime street-light pool.
    /// </summary>
    public static class HomeExteriorViewBuilder
    {
        public const float ExteriorMinimumX =
            PlayerHomeBalconyGeometry.HomeFacadeX +
            PlayerHomeBalconyGeometry.WallThickness *
            0.5f +
            0.01f;
        private const float StreetLampExteriorClearance =
            0.90f;
        private const float TrafficSignalExteriorClearance =
            0.65f;

        private static readonly Color TerminalHaze =
            new Color(0.050f, 0.073f, 0.071f);
        private static readonly Color TerminalHazeSide =
            new Color(0.042f, 0.061f, 0.060f);

        public static Transform Build(
            Transform parent,
            HomeBalconyLayoutPlan balcony,
            HomeExteriorContextPlan context)
        {
            return Build(
                parent,
                balcony,
                context,
                out _);
        }

        public static Transform Build(
            Transform parent,
            HomeBalconyLayoutPlan balcony,
            HomeExteriorContextPlan context,
            out CityNightWorldResult night)
        {
            night = null;
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (balcony == null)
            {
                throw new ArgumentNullException(nameof(balcony));
            }

            if (context == null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            Transform root =
                new GameObject("Home Exterior View").transform;
            root.SetParent(parent, false);

            BuildTerminalEnvironment(root, balcony);
            BuildRoads(root, context);
            BuildBuildings(root, context);
            CityDistrictPointOfInterestWorldBuilder
                .BuildHomeExterior(root, context);
            CityDecorationWorldBuilder.BuildHomeExterior(
                root,
                context,
                context.NearbyDecorations);
            BuildHomeBusStop(root, context);
            night = BuildNightFixtures(root, context);
            return root;
        }

        private static void BuildHomeBusStop(
            Transform parent,
            HomeExteriorContextPlan context)
        {
            CityBusStopDescriptor stop = context.HomeBusStop;
            if (stop == null)
            {
                return;
            }

            Vector3 localShelter = PlayerHomeBalconyGeometry.ToHomeLocal(
                context.PlayerHome,
                stop.ShelterPosition);

            localShelter = ClipStopToExterior(localShelter);

            Transform root = new GameObject("Home Bus Stops").transform;
            root.SetParent(parent, false);
            CityBusStopWorldBuilder.BuildLocalStop(
                root,
                stop,
                localShelter,
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    context.PlayerHome,
                    stop.Forward),
                PlayerHomeBalconyGeometry.ToHomeLocalDirection(
                    context.PlayerHome,
                    stop.RoadsideForward),
                false);
        }

        private static void BuildTerminalEnvironment(
            Transform parent,
            HomeBalconyLayoutPlan balcony)
        {
            float radius =
                HomeExteriorContextPlanner.ViewRadius;
            float facadeX =
                PlayerHomeBalconyGeometry.HomeFacadeX;
            float groundWidth = radius * 2.5f;
            CreateExteriorGroundBox(
                "Home Exterior Ground",
                parent,
                new Bounds(
                    new Vector3(
                        facadeX + radius * 0.18f,
                        balcony.StreetGroundY - 0.24f,
                        0f),
                    new Vector3(
                        groundWidth,
                        0.32f,
                        groundWidth)));

            float horizonX = facadeX + radius + 22f;
            float horizonHeight = 25f;
            float horizonY =
                balcony.StreetGroundY +
                horizonHeight * 0.5f;
            CreateExteriorBox(
                "Home Exterior Terminal Haze",
                parent,
                new Bounds(
                    new Vector3(
                        horizonX,
                        horizonY,
                        0f),
                    new Vector3(
                        0.75f,
                        horizonHeight,
                        groundWidth + 10f)),
                TerminalHaze,
                CityNightResources.EmissiveMaterial);
            CreateExteriorBox(
                "Home Exterior Terminal Haze South",
                parent,
                new Bounds(
                    new Vector3(
                        facadeX + radius * 0.45f,
                        horizonY,
                        -groundWidth * 0.5f),
                    new Vector3(
                        radius * 1.55f,
                        horizonHeight,
                        0.75f)),
                TerminalHazeSide,
                CityNightResources.EmissiveMaterial);
            CreateExteriorBox(
                "Home Exterior Terminal Haze North",
                parent,
                new Bounds(
                    new Vector3(
                        facadeX + radius * 0.45f,
                        horizonY,
                        groundWidth * 0.5f),
                    new Vector3(
                        radius * 1.55f,
                        horizonHeight,
                        0.75f)),
                TerminalHazeSide,
                CityNightResources.EmissiveMaterial);
        }

        private static void BuildRoads(
            Transform parent,
            HomeExteriorContextPlan context)
        {
            CityStreetSurfacePlan plan =
                CityStreetSurfacePlanner.Create(context.Layout);
            var nearbyRoadRectangles = new List<Rect>(
                context.NearbyRoads.Count);
            for (int index = 0;
                 index < context.NearbyRoads.Count;
                 index++)
            {
                nearbyRoadRectangles.Add(
                    context.Layout.GetRoadRect(
                        context.NearbyRoads[index]));
            }

            var streets = new List<Bounds>();
            var parkPaths = new List<Bounds>();
            var sidewalks = new List<Bounds>();
            var centerMarkings = new List<Bounds>();
            var crosswalkMarkings = new List<Bounds>();
            AddHomeLocalStreetGeometry(
                plan.StreetSurfaces,
                nearbyRoadRectangles,
                context,
                streets);
            AddHomeLocalStreetGeometry(
                plan.ParkPaths,
                nearbyRoadRectangles,
                context,
                parkPaths);
            AddHomeLocalStreetGeometry(
                plan.Sidewalks,
                nearbyRoadRectangles,
                context,
                sidewalks);
            AddHomeLocalStreetGeometry(
                plan.CenterMarkings,
                nearbyRoadRectangles,
                context,
                centerMarkings);
            AddHomeLocalStreetGeometry(
                plan.CrosswalkMarkings,
                nearbyRoadRectangles,
                context,
                crosswalkMarkings);

            BuildRoadSurfaceBoxesIfAny(
                "Home Exterior Street Surfaces",
                parent,
                streets);
            BuildParkPathBoxesIfAny(
                "Home Exterior Park Paths",
                parent,
                parkPaths);
            BuildSidewalkSurfaceBoxesIfAny(
                "Home Exterior Sidewalk Surfaces",
                parent,
                sidewalks);
            BuildRoadMarkingBoxesIfAny(
                "Home Exterior Road Center Markings",
                parent,
                centerMarkings);
            BuildRoadMarkingBoxesIfAny(
                "Home Exterior Pedestrian Crossings",
                parent,
                crosswalkMarkings);
        }

        private static void AddHomeLocalStreetGeometry(
            IReadOnlyList<Bounds> source,
            IReadOnlyList<Rect> nearbyRoadRectangles,
            HomeExteriorContextPlan context,
            ICollection<Bounds> target)
        {
            for (int index = 0; index < source.Count; index++)
            {
                Bounds cityBounds = source[index];
                if (!TouchesAnyRoad(
                        cityBounds,
                        nearbyRoadRectangles))
                {
                    continue;
                }

                var localBounds = new Bounds(
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        cityBounds.center),
                    PlayerHomeBalconyGeometry.ToHomeLocalSize(
                        context.PlayerHome,
                        cityBounds.size));
                if (TryClipToExteriorHalfSpace(
                        localBounds,
                        out Bounds exteriorBounds))
                {
                    target.Add(exteriorBounds);
                }
            }
        }

        private static bool TouchesAnyRoad(
            Bounds bounds,
            IReadOnlyList<Rect> roadRectangles)
        {
            for (int index = 0;
                 index < roadRectangles.Count;
                 index++)
            {
                Rect road = roadRectangles[index];
                if (bounds.max.x >= road.xMin &&
                    bounds.min.x <= road.xMax &&
                    bounds.max.z >= road.yMin &&
                    bounds.min.z <= road.yMax)
                {
                    return true;
                }
            }

            return false;
        }

        private static void BuildBuildings(
            Transform parent,
            HomeExteriorContextPlan context)
        {
            Transform buildings =
                new GameObject(
                    "Home Exterior Building Silhouettes")
                    .transform;
            buildings.SetParent(parent, false);
            for (int index = 0;
                 index < context.NearbyLots.Count;
                 index++)
            {
                BuildingLot lot =
                    context.NearbyLots[index];
                if (lot.IsPlayerHome)
                {
                    continue;
                }

                if (lot.IsBar || lot.IsSupermarket)
                {
                    float foundationDepth = CityWorldBuilder
                        .ResolveBuildingFoundationDepth(
                            context.Layout,
                            lot);
                    CityBuildingExteriorFit specialFit =
                        CitySpecialBuildingWorldBuilder
                            .ClassifyHomeExterior(
                                context,
                                lot,
                                foundationDepth);
                    if (specialFit == CityBuildingExteriorFit.Hidden)
                    {
                        continue;
                    }

                    if (specialFit == CityBuildingExteriorFit.Full)
                    {
                        Transform specialBuilding = new GameObject(
                            lot.IsBar
                                ? $"Exterior Bar {lot.BarId}"
                                : "Exterior Supermarket")
                            .transform;
                        specialBuilding.SetParent(buildings, false);
                        if (lot.IsBar)
                        {
                            CitySpecialBuildingWorldBuilder
                                .BuildBarHomeInfrastructure(
                                    specialBuilding,
                                    context,
                                    lot,
                                    foundationDepth);
                            CityBarFacadeWorldBuilder.BuildHomeExterior(
                                specialBuilding,
                                context,
                                lot);
                        }
                        else
                        {
                            CitySpecialBuildingWorldBuilder.BuildHomeExterior(
                                specialBuilding,
                                context,
                                lot,
                                foundationDepth);
                            BuildWindowBands(
                                specialBuilding,
                                context,
                                lot);
                            CitySupermarketFacadeWorldBuilder
                                .BuildHomeExterior(
                                    specialBuilding,
                                    context,
                                    lot);
                        }

                        continue;
                    }

                    // Crossing special shells retain the clipped legacy
                    // silhouette below. A non-readable imported mesh must
                    // never be sheared at the apartment facade.
                }

                if (lot.IsOrdinaryBuilding)
                {
                    CityBuildingExteriorFit fit =
                        CityBuildingPrototypeWorldBuilder
                            .ClassifyHomeExterior(context, lot);
                    if (fit == CityBuildingExteriorFit.Hidden)
                    {
                        continue;
                    }

                    if (fit == CityBuildingExteriorFit.Full)
                    {
                        Transform prototypeBuilding = new GameObject(
                            $"Exterior Building {lot.Cell.x}-{lot.Cell.y}")
                            .transform;
                        prototypeBuilding.SetParent(buildings, false);
                        CityBuildingPrototypeWorldBuilder
                            .BuildHomeExterior(
                                prototypeBuilding,
                                context,
                                lot,
                                CityWorldBuilder
                                    .ResolveBuildingFoundationDepth(
                                        context.Layout,
                                        lot));
                        continue;
                    }

                    // A fixed-metre, non-readable Blender mesh is never
                    // sheared at the apartment wall. Only this rare crossing
                    // case retains the existing bounds-clipped silhouette.
                }

                Vector3 cityCenter =
                    lot.Center +
                    Vector3.up *
                    (lot.Height * 0.5f +
                     CityFacadeGrid.MassBaseElevation);
                Vector3 localCenter =
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        cityCenter);
                Vector3 localSize =
                    PlayerHomeBalconyGeometry.ToHomeLocalSize(
                        context.PlayerHome,
                        new Vector3(
                            lot.Size.x,
                            lot.Height,
                            lot.Size.y));
                Color facade =
                    CityExteriorAppearance
                        .CreateNightFacadeColor(lot);
                if (!TryClipToExteriorHalfSpace(
                        new Bounds(
                            localCenter,
                            localSize),
                        out Bounds exteriorMass))
                {
                    continue;
                }

                Transform building =
                    new GameObject(
                        lot.IsBar
                            ? $"Exterior Bar {lot.BarId}"
                            : lot.IsSupermarket
                                ? "Exterior Supermarket"
                                : $"Exterior Building {lot.Cell.x}-{lot.Cell.y}")
                        .transform;
                building.SetParent(buildings, false);
                GameObject mass =
                    RuntimePrimitiveFactory.CreateBox(
                        "Exterior Building Mass",
                        building,
                        exteriorMass.center,
                        exteriorMass.size,
                        facade,
                        RuntimePrimitiveFactory.DefaultMaterial,
                        false);
                CityFacadeAppearance.Apply(
                    mass.GetComponent<Renderer>(),
                    lot,
                    context.Layout.Seed,
                    facade,
                    CreateExteriorPlacement(
                        context,
                        lot,
                        localCenter,
                        exteriorMass));

                Vector3 roofCenter =
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        lot.Center +
                        Vector3.up *
                        (lot.Height + 0.22f));
                Vector3 roofSize =
                    PlayerHomeBalconyGeometry.ToHomeLocalSize(
                        context.PlayerHome,
                        new Vector3(
                            lot.Size.x + 0.35f,
                            0.28f,
                            lot.Size.y + 0.35f));
                if (TryClipToExteriorHalfSpace(
                        new Bounds(
                            roofCenter,
                            roofSize),
                        out Bounds exteriorRoof))
                {
                    Color roofColor =
                        CityExteriorAppearance.Darken(
                            facade,
                            0.055f);
                    GameObject roof =
                        RuntimePrimitiveFactory.CreateBox(
                            "Exterior Roof",
                            building,
                            exteriorRoof.center,
                            exteriorRoof.size,
                            roofColor,
                            RuntimePrimitiveFactory.DefaultMaterial,
                            false);
                    CityFacadeAppearance.ApplyRoof(
                        roof.GetComponent<Renderer>(),
                        roofColor);
                }
                BuildWindowBands(
                    building,
                    context,
                    lot);
                if (lot.IsSupermarket)
                {
                    CitySupermarketFacadeWorldBuilder
                        .BuildHomeExterior(
                            building,
                            context,
                            lot);
                }
                // A crossing pub keeps this clipped legacy silhouette only.
                // Stretch-clipping a pitched authored roof or chimney would
                // visibly deform it at the apartment facade.
            }
        }

        /// <summary>
        /// Restates a City lot's facade placement in Home-local terms.
        /// <para>
        /// Two things move between the frames and both have to be undone here.
        /// <see cref="PlayerHomeBalconyGeometry.ToHomeLocal"/> rebuilds the
        /// world on the home's own frontage axis, so a lot whose windows face
        /// along X may end up with its bay run along local Z or local X
        /// depending on which way the home itself faces. And
        /// <see cref="TryClipToExteriorHalfSpace"/> trims the mass on one
        /// side, which shifts where the bay grid starts without changing its
        /// pitch. Height is untouched by both, so the floor phase carries over
        /// unchanged.
        /// </para>
        /// </summary>
        internal static CityFacadePlacement CreateExteriorPlacement(
            HomeExteriorContextPlan context,
            BuildingLot lot,
            Vector3 unclippedLocalCenter,
            Bounds clippedLocalBounds)
        {
            bool homeFrontageAlongX = Mathf.Abs(
                PlayerHomeBalconyGeometry
                    .GetFrontageDirection(context.PlayerHome)
                    .x) > 0.5f;
            bool localUsesZ =
                CityFacadeGrid.FrontageRunsAlongX(lot) ==
                homeFrontageAlongX;
            float uCenterOffset = localUsesZ
                ? clippedLocalBounds.center.z - unclippedLocalCenter.z
                : clippedLocalBounds.center.x - unclippedLocalCenter.x;
            return new CityFacadePlacement(
                localUsesZ
                    ? CityFacadeProjection.BoxZY
                    : CityFacadeProjection.BoxXY,
                uCenterOffset,
                CityFacadeGrid.MassBaseElevation);
        }

        private static void BuildWindowBands(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot)
        {
            int floorCount =
                CityFacadeGrid.ResolveFloorCount(lot.Height);
            for (int floor = 0;
                 floor < floorCount;
                 floor++)
            {
                float y =
                    CityFacadeGrid.ResolveFloorCenterY(floor);
                if (!CityFacadeGrid.IsFloorWithinHeight(
                        floor,
                        lot.Height))
                {
                    break;
                }

                Vector3 frontPosition;
                Vector3 backPosition;
                Vector3 rowSize;
                if (lot.HasRoadFrontage)
                {
                    Vector3 frontage = new Vector3(
                        lot.FrontageDirection.x,
                        0f,
                        lot.FrontageDirection.y);
                    bool frontageIsX =
                        Mathf.Abs(frontage.x) > 0.5f;
                    float facadeDistance =
                        frontageIsX
                            ? lot.Size.x * 0.5f +
                              CityFacadeGrid.FacadeProudOffset
                            : lot.Size.y * 0.5f +
                              CityFacadeGrid.FacadeProudOffset;
                    Vector3 offset =
                        frontage * facadeDistance;
                    frontPosition =
                        lot.Center +
                        offset +
                        Vector3.up * y;
                    backPosition =
                        lot.Center -
                        offset +
                        Vector3.up * y;
                    rowSize =
                        frontageIsX
                            ? new Vector3(
                                CityFacadeGrid.PaneThickness,
                                0.7f,
                                CityFacadeGrid.ResolveRowLength(
                                    lot.Size.y))
                            : new Vector3(
                                CityFacadeGrid.ResolveRowLength(
                                    lot.Size.x),
                                0.7f,
                                CityFacadeGrid.PaneThickness);
                }
                else
                {
                    frontPosition =
                        lot.Center +
                        new Vector3(
                            0f,
                            y,
                            -(lot.Size.y * 0.5f +
                              CityFacadeGrid.FacadeProudOffset));
                    backPosition =
                        lot.Center +
                        new Vector3(
                            0f,
                            y,
                            lot.Size.y * 0.5f +
                            CityFacadeGrid.FacadeProudOffset);
                    rowSize = new Vector3(
                        CityFacadeGrid.ResolveRowLength(lot.Size.x),
                        0.7f,
                        CityFacadeGrid.PaneThickness);
                }

                if (!lot.IsSupermarket || floor > 0)
                {
                    BuildWindowRow(
                        parent,
                        context,
                        lot,
                        frontPosition,
                        rowSize,
                        floor,
                        0);
                }
                BuildWindowRow(
                    parent,
                    context,
                    lot,
                    backPosition,
                    rowSize,
                    floor,
                    1);
            }
        }

        private static void BuildWindowRow(
            Transform parent,
            HomeExteriorContextPlan context,
            BuildingLot lot,
            Vector3 cityPosition,
            Vector3 cityRowSize,
            int floor,
            int side)
        {
            bool runsAlongX =
                cityRowSize.x > cityRowSize.z;
            float rowLength =
                runsAlongX
                    ? cityRowSize.x
                    : cityRowSize.z;
            int paneCount =
                CityFacadeGrid.ResolvePaneCount(rowLength);
            float paneLength =
                CityFacadeGrid.ResolvePaneLength(
                    rowLength,
                    paneCount);
            float paneHeight =
                CityFacadeGrid.ResolvePaneHeight(lot);

            for (int pane = 0;
                 pane < paneCount;
                 pane++)
            {
                float offset =
                    CityFacadeGrid.ResolvePaneOffset(
                        rowLength,
                        paneCount,
                        pane);
                Vector3 panePosition =
                    cityPosition +
                    (runsAlongX
                        ? new Vector3(offset, 0f, 0f)
                        : new Vector3(0f, 0f, offset));
                Vector3 paneSize =
                    runsAlongX
                        ? new Vector3(
                            paneLength,
                            paneHeight,
                            cityRowSize.z)
                        : new Vector3(
                            cityRowSize.x,
                            paneHeight,
                            paneLength);
                Vector3 localPosition =
                    PlayerHomeBalconyGeometry.ToHomeLocal(
                        context.PlayerHome,
                        panePosition);
                Vector3 localSize =
                    PlayerHomeBalconyGeometry.ToHomeLocalSize(
                        context.PlayerHome,
                        paneSize);
                if (!TryClipToExteriorHalfSpace(
                        new Bounds(
                            localPosition,
                            localSize),
                        out Bounds exteriorPane))
                {
                    continue;
                }

                CityWindowFamily family =
                    CityExteriorAppearance.ResolveWindowFamily(
                        lot,
                        context.Layout.Seed,
                        floor,
                        pane,
                        side,
                        out uint paneHash);

                GameObject paneObject;
                if (family == CityWindowFamily.Off)
                {
                    paneObject = RuntimePrimitiveFactory.CreateBox(
                        $"Exterior Window {floor}-{side}-{pane}",
                        parent,
                        exteriorPane.center,
                        exteriorPane.size,
                        CityExteriorAppearance.WindowOff,
                        false);
                    CityWindowAppearance.ApplyDarkPane(
                        paneObject.GetComponent<Renderer>(),
                        paneHash);
                }
                else
                {
                    paneObject =
                        RuntimePrimitiveFactory.CreateMaterialBox(
                            $"Exterior Window {floor}-{side}-{pane}",
                            parent,
                            exteriorPane.center,
                            exteriorPane.size,
                            CityWindowAppearance.ResolveLitMaterial(
                                family),
                            false);
                    CityWindowAppearance.ApplyLitPane(
                        paneObject.GetComponent<Renderer>(),
                        paneHash);
                }
            }
        }

        private static CityNightWorldResult BuildNightFixtures(
            Transform parent,
            HomeExteriorContextPlan context)
        {
            var lamps =
                new List<StreetLampDescriptor>(
                    context.NearbyStreetLamps.Count);
            for (int index = 0;
                 index <
                 context.NearbyStreetLamps.Count;
                 index++)
            {
                StreetLampDescriptor source =
                    context.NearbyStreetLamps[index];
                Vector3 localPosition =
                    PlayerHomeBalconyGeometry
                        .ToHomeLocal(
                            context.PlayerHome,
                            source.Position);
                if (localPosition.x -
                    StreetLampExteriorClearance <
                    ExteriorMinimumX)
                {
                    continue;
                }

                lamps.Add(new StreetLampDescriptor(
                    source.Edge,
                    source.EdgeT,
                    source.Side,
                    localPosition,
                    PlayerHomeBalconyGeometry
                        .ToHomeLocalDirection(
                            context.PlayerHome,
                            source.Forward)));
            }

            var signals =
                new List<TrafficSignalDescriptor>(
                    context.NearbyTrafficSignals.Count);
            for (int index = 0;
                 index <
                 context.NearbyTrafficSignals.Count;
                 index++)
            {
                TrafficSignalDescriptor source =
                    context.NearbyTrafficSignals[index];
                Vector3 localPosition =
                    PlayerHomeBalconyGeometry
                        .ToHomeLocal(
                            context.PlayerHome,
                            source.Position);
                if (localPosition.x -
                    TrafficSignalExteriorClearance <
                    ExteriorMinimumX)
                {
                    continue;
                }

                signals.Add(new TrafficSignalDescriptor(
                    source.IntersectionNode,
                    source.PairIndex,
                    localPosition,
                    PlayerHomeBalconyGeometry
                        .ToHomeLocalDirection(
                            context.PlayerHome,
                            source.Forward),
                    source.BlinkPhase01));
            }

            var fixturePlan =
                new CityNightFixturePlan(
                    lamps,
                    signals);
            CityNightWorldResult result =
                CityNightWorldBuilder.Build(
                    parent,
                    fixturePlan,
                    Array.Empty<BarEntrance>(),
                    // No collision out here. Everything in this view is on
                    // the far side of the facade, so a lamp's blocking box
                    // can only ever cost physics for nothing - or, worse,
                    // fence off part of the balcony the player IS standing
                    // on.
                    buildCollision: false);
            result.Root.name =
                "Home Exterior Night Fixtures";
            return result;
        }

        /// <summary>
        /// How far past the facade the composed stop's anchor is held. Its
        /// shelter is `4.65 m` across and turns with the lane, so a little
        /// over half that clears the wall whichever way it faces.
        /// </summary>
        public const float BusStopFacadeClearance = 2.70f;

        /// <summary>
        /// Holds a composed bus stop in the exterior half-space.
        ///
        /// The plan picks the stop that BELONGS to this home, by distance
        /// to its door, and that is the right question for it to answer -
        /// but the stop that belongs to the home can sit against the
        /// block's own footprint, and converted into the flat's local
        /// frame it then lands behind the facade. Drawn there it is a bus
        /// shelter inside the bedroom. Everything else in this diorama is
        /// clipped to the half-space past the wall already; the stop is
        /// clipped the same way rather than dropped, because a balcony
        /// with no stop in sight loses something the view is for.
        /// </summary>
        public static Vector3 ClipStopToExterior(Vector3 localShelter)
        {
            localShelter.x = Mathf.Max(
                localShelter.x,
                ExteriorMinimumX + BusStopFacadeClearance);
            return localShelter;
        }

        internal static bool TryClipToExteriorHalfSpace(
            Bounds source,
            out Bounds clipped)
        {
            Vector3 maximum = source.max;
            if (maximum.x <= ExteriorMinimumX)
            {
                clipped = default;
                return false;
            }

            Vector3 minimum = source.min;
            minimum.x =
                Mathf.Max(
                    minimum.x,
                    ExteriorMinimumX);
            clipped = new Bounds();
            clipped.SetMinMax(
                minimum,
                maximum);
            return true;
        }

        private static void CreateExteriorGroundBox(
            string name,
            Transform parent,
            Bounds bounds)
        {
            if (!TryClipToExteriorHalfSpace(
                    bounds,
                    out Bounds exteriorBounds))
            {
                return;
            }

            Bounds[] boxes = { exteriorBounds };
            GameObject surface =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    parent,
                    boxes,
                    Color.white,
                    false,
                    CityExteriorAppearance.GroundTextureTileSize);
            CityExteriorAppearance.ApplyGroundSurface(
                surface.GetComponent<Renderer>());
        }

        private static void CreateExteriorBox(
            string name,
            Transform parent,
            Bounds bounds,
            Color color,
            Material material)
        {
            if (!TryClipToExteriorHalfSpace(
                    bounds,
                    out Bounds exteriorBounds))
            {
                return;
            }

            RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                exteriorBounds.center,
                exteriorBounds.size,
                color,
                material,
                false);
        }

        /// <summary>
        /// The reconstructed view repeats City's park walk on the same
        /// sheet at the same metre pitch, so a path seen from the
        /// balcony is the surface the player walks on down there.
        /// </summary>
        private static void BuildParkPathBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            GameObject surface =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    parent,
                    boxes,
                    CityExteriorAppearance.ParkPath,
                    false,
                    CityParkSurfaceAppearance
                        .GetRecipe(CityParkSurfaceKind.Path)
                        .MetersPerTile);
            CityParkSurfaceAppearance.ApplyCombined(
                surface.GetComponent<Renderer>(),
                CityParkSurfaceKind.Path,
                CityExteriorAppearance.ParkPath);
        }

        private static void BuildRoadSurfaceBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            GameObject surface =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    parent,
                    boxes,
                    CityExteriorAppearance.Asphalt,
                    false,
                    CityExteriorAppearance.RoadTextureTileSize);
            CityExteriorAppearance.ApplyRoadSurface(
                surface.GetComponent<Renderer>());
        }

        private static void BuildSidewalkSurfaceBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            GameObject surface =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    parent,
                    boxes,
                    Color.white,
                    false,
                    CityExteriorAppearance.SidewalkTextureTileSize);
            CityExteriorAppearance.ApplySidewalkSurface(
                surface.GetComponent<Renderer>());
        }

        private static void BuildRoadMarkingBoxesIfAny(
            string name,
            Transform parent,
            IReadOnlyList<Bounds> boxes)
        {
            if (boxes.Count == 0)
            {
                return;
            }

            GameObject markings =
                RuntimePrimitiveFactory.CreateCombinedBoxes(
                    name,
                    parent,
                    boxes,
                    Color.white,
                    false,
                    CityExteriorAppearance.RoadMarkingTextureTileSize);
            CityExteriorAppearance.ApplyRoadMarkingSurface(
                markings.GetComponent<Renderer>());
        }
    }
}
