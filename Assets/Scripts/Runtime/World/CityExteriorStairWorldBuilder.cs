using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    public sealed class CityExteriorStairWorldResult
    {
        internal CityExteriorStairWorldResult(
            GameObject root,
            IList<Collider> rampColliders)
        {
            Root = root != null
                ? root
                : throw new ArgumentNullException(nameof(root));
            RampColliders = new ReadOnlyCollection<Collider>(
                new List<Collider>(
                    rampColliders ??
                    throw new ArgumentNullException(
                        nameof(rampColliders))));
        }

        public GameObject Root { get; }
        public IReadOnlyList<Collider> RampColliders { get; }
    }

    /// <summary>
    /// Builds visible exterior steps around one continuous, seam-free ramp
    /// collider per flight. CityWorldBuilder composes these from the shared
    /// deterministic elevation plan.
    /// </summary>
    public static class CityExteriorStairWorldBuilder
    {
        private const float StepTreadOverlap = 0.012f;
        private const float RampThickness = 0.12f;
        private const float DegenerateRailLength = 0.001f;

        private static readonly Color StairColor =
            new Color(0.43f, 0.43f, 0.39f);
        private static readonly Color RailColor =
            new Color(0.16f, 0.19f, 0.18f);
        private static readonly Color RetainingWallColor =
            new Color(0.28f, 0.28f, 0.25f);

        public static CityExteriorStairWorldResult Build(
            Transform parent,
            CityExteriorStairPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            CityExteriorStairValidator.ValidateOrThrow(plan);

            Transform root = new GameObject(
                $"Exterior Stair {plan.Id}").transform;
            root.SetParent(parent, false);
            // On the root: the tread triggers and the hidden ramp under
            // them are one flight of the sidewalk's flags.
            FootstepGround.Stamp(root, FootstepGroundKind.Stone);
            var rampColliders = new List<Collider>(plan.Flights.Count);

            BuildFlights(root, plan.Flights, rampColliders);
            BuildLandings(root, plan.Landings);
            BuildRails(root, plan.Rails, "Guard Rails");
            BuildRetainingWalls(root, plan.RetainingWalls);

            return new CityExteriorStairWorldResult(
                root.gameObject,
                rampColliders);
        }

        internal static void BuildGroundJoints(Transform parent, CityLayout layout,
            CityElevationStairDescriptor stair, CityElevationStairPlacement placement)
        {
            CityExteriorStairFlightDescriptor flight = placement.ExteriorPlan.Flights[0];
            Vector3 soilSide = placement.SideDirection * (stair.Width * .5f);
            Vector3 first = placement.LowerApproachStart + soilSide;
            Vector3 last = placement.UpperApproachEnd + soilSide;
            float run = Vector3.Dot(last - first, flight.Direction);
            int stations = Mathf.Max(1, Mathf.CeilToInt(run / flight.TreadDepth));
            float bottom = flight.Start.y - .6f;
            var cut = new CityHillsideStairCut();
            bool forward = Vector3.Dot(Vector3.Cross(flight.Direction, Vector3.down), soilSide) < 0f;
            bool previousHasSoil = false;
            Vector3 previous = default;
            Vector3 previousBacking = default;
            for (int station = 0; station <= stations; station++)
            {
                Vector3 rim = Vector3.Lerp(first, last, station / (float)stations);
                Vector3 probe = rim + placement.SideDirection * .002f;
                bool hasSoil = CityTerrainSurfacePlan.TrySampleGroundTop(layout, new Vector2(probe.x, probe.z),
                    out rim.y, out CitySurfaceDescriptor surface);
                Vector3 backing = rim + placement.SideDirection * .14f;
                if (hasSoil)
                {
                    bottom = Mathf.Min(bottom, rim.y - .25f);
                    // Independently tessellated street and terrain need a
                    // short buried lap across the cut, even at grazing views.
                    backing.y = CityTerrainSurfacePlan.SampleTop(layout, surface, new Vector2(backing.x, backing.z)) - .035f;
                    rim.y += .045f;
                    if (previousHasSoil) cut.Add(forward ? previous : rim, forward ? rim : previous, bottom,
                        forward ? previousBacking : backing, forward ? backing : previousBacking);
                }
                previous = rim;
                previousBacking = backing;
                previousHasSoil = hasSoil;
            }
            cut.Build(parent, false, CityExteriorAppearance.ResolveDistrictGroundTint(
                CityDistrictPresentationPlanner.GetProfile(stair.District).Wear));

            var support = Resources.Load<GameObject>(CityTerracePilotWorld.ResourceFolder + "TerraceSupport");
            Footing(flight.Start, flight.End, flight.Start.y + .02f, "Flight footing");
            foreach (var landing in placement.ExteriorPlan.Landings)
                Footing(landing.StartEdgeCenter, landing.EndEdgeCenter, landing.Elevation - landing.Thickness + .02f, "Landing footing");
            Footing(placement.LowerApproachStart, placement.LowerApproachEnd, placement.LowerApproachStart.y - .04f, "Lower approach footing");
            Footing(placement.UpperApproachStart, placement.UpperApproachEnd, placement.UpperApproachEnd.y - .04f, "Upper approach footing");

            void Footing(Vector3 start, Vector3 end, float top, string name)
            {
                float length = Vector3.Dot(end - start, flight.Direction);
                if (length < .01f) return;
                Vector3 center = (start + end) * .5f;
                center.y = top;
                Vector2 size = Mathf.Abs(flight.Direction.x) > .5f
                    ? new Vector2(length, stair.Width) : new Vector2(stair.Width, length);
                CityTerracePilotWorld.Support(support, parent, stair.Id + " " + name, center, size, bottom);
            }
        }

        private static void BuildFlights(
            Transform root,
            IReadOnlyList<CityExteriorStairFlightDescriptor> flights,
            ICollection<Collider> rampColliders)
        {
            Transform flightGroup = CreateGroup("Flights", root);
            for (int index = 0; index < flights.Count; index++)
            {
                CityExteriorStairFlightDescriptor flight = flights[index];
                Transform flightRoot = CreateGroup(flight.Id, flightGroup);
                Transform steps = CreateGroup("Visible Steps", flightRoot);
                float yaw = DirectionYaw(flight.Direction);
                for (int stepIndex = 0;
                     stepIndex < flight.StepCount;
                     stepIndex++)
                {
                    GameObject step = RuntimePrimitiveFactory.CreateBox(
                        $"Step {stepIndex + 1:00}",
                        steps,
                        flight.GetStepCenter(stepIndex),
                        flight.GetStepSize(
                            stepIndex,
                            StepTreadOverlap),
                        StairColor,
                        false);
                    step.transform.localRotation =
                        Quaternion.Euler(0f, yaw, 0f);
                    RuntimePrimitiveFactory.ProfileStructuralBox(step, .012f, CityExteriorAppearance.SidewalkTextureTileSize);
                    CityExteriorAppearance.ApplySidewalkSurface(
                        step.GetComponent<Renderer>());
                    // Walking bodies use the invisible ramp below; the
                    // tread is a surface only foot probes and the contact
                    // shadow can see.
                    FootProbeSurface.AddTreadCollider(step);
                }

                rampColliders.Add(BuildFlightRamp(flightRoot, flight));
            }
        }

        private static Collider BuildFlightRamp(
            Transform parent,
            CityExteriorStairFlightDescriptor flight)
        {
            Vector3 slopeDirection = flight.End - flight.Start;
            Quaternion rotation = Quaternion.LookRotation(
                slopeDirection.normalized,
                Vector3.up);
            Vector3 upperNormal = rotation * Vector3.up;
            Vector3 surfaceCenter = (flight.Start + flight.End) * 0.5f;
            GameObject ramp = RuntimePrimitiveFactory.CreateBox(
                "Continuous Invisible Ramp Collider",
                parent,
                surfaceCenter - (upperNormal * (RampThickness * 0.5f)),
                new Vector3(
                    flight.Width,
                    RampThickness,
                    slopeDirection.magnitude),
                StairColor,
                true);
            ramp.transform.localRotation = rotation;
            Renderer renderer = ramp.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            Collider collider = ramp.GetComponent<Collider>();
            StairWalkableSurface.Stamp(collider);
            return collider;
        }

        private static void BuildLandings(
            Transform root,
            IReadOnlyList<CityExteriorStairLandingDescriptor> landings)
        {
            Transform landingGroup = CreateGroup("Landings", root);
            for (int index = 0; index < landings.Count; index++)
            {
                CityExteriorStairLandingDescriptor landing =
                    landings[index];
                Vector3 center = landing.Center;
                center.y = landing.Elevation - (landing.Thickness * 0.5f);
                GameObject surface = RuntimePrimitiveFactory.CreateBox(
                    landing.Id,
                    landingGroup,
                    center,
                    new Vector3(
                        landing.Width,
                        landing.Thickness,
                        landing.Length),
                    StairColor,
                    true);
                surface.transform.localRotation = Quaternion.Euler(
                    0f,
                    DirectionYaw(landing.Direction),
                    0f);
                RuntimePrimitiveFactory.ProfileStructuralBox(surface, .015f, CityExteriorAppearance.SidewalkTextureTileSize);
                CityExteriorAppearance.ApplySidewalkSurface(
                    surface.GetComponent<Renderer>());
            }
        }

        internal static void BuildRails(
            Transform root,
            IReadOnlyList<CityExteriorStairRailDescriptor> rails,
            string groupName)
        {
            Transform railGroup = CreateGroup(groupName, root);
            for (int index = 0; index < rails.Count; index++)
            {
                CityExteriorStairRailDescriptor rail = rails[index];
                if (Vector3.Distance(rail.SurfaceStart, rail.SurfaceEnd) <=
                    DegenerateRailLength)
                {
                    continue;
                }

                Transform railRoot = CreateGroup(rail.Id, railGroup);
                CreateRailPost(
                    "Start Post",
                    railRoot,
                    rail.SurfaceStart,
                    rail.Height,
                    rail.Thickness);
                CreateRailPost(
                    "End Post",
                    railRoot,
                    rail.SurfaceEnd,
                    rail.Height,
                    rail.Thickness);
                CreateBeamBetween(
                    "Rail Beam",
                    railRoot,
                    rail.SurfaceStart + (Vector3.up * rail.Height),
                    rail.SurfaceEnd + (Vector3.up * rail.Height),
                    rail.Thickness,
                    RailColor);
            }
        }

        private static void CreateRailPost(
            string name,
            Transform parent,
            Vector3 surfacePoint,
            float height,
            float thickness)
        {
            GameObject post = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                surfacePoint + (Vector3.up * (height * 0.5f)),
                new Vector3(thickness, height, thickness),
                RailColor,
                true);
            RuntimePrimitiveFactory.ProfileStructuralBox(post, .007f);
        }

        private static void BuildRetainingWalls(
            Transform root,
            IReadOnlyList<CityExteriorStairRetainingWallDescriptor> walls)
        {
            Transform wallGroup = CreateGroup("Retaining Walls", root);
            for (int index = 0; index < walls.Count; index++)
            {
                CityExteriorStairRetainingWallDescriptor wall = walls[index];
                Vector3 run = wall.TopEnd - wall.TopStart;
                Vector3 center = (wall.TopStart + wall.TopEnd) * 0.5f;
                center.y = wall.BottomElevation + (wall.Height * 0.5f);
                GameObject wallObject = RuntimePrimitiveFactory.CreateBox(
                    wall.Id,
                    wallGroup,
                    center,
                    new Vector3(
                        wall.Thickness,
                        wall.Height,
                        new Vector2(run.x, run.z).magnitude),
                    RetainingWallColor,
                    true);
                wallObject.transform.localRotation = Quaternion.Euler(
                    0f,
                    DirectionYaw(run),
                    0f);
                RuntimePrimitiveFactory.ProfileStructuralBox(wallObject, .025f);
            }
        }

        private static void CreateBeamBetween(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float thickness,
            Color color)
        {
            Vector3 delta = end - start;
            GameObject beam = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                (start + end) * 0.5f,
                new Vector3(thickness, thickness, delta.magnitude),
                color,
                true);
            beam.transform.localRotation = Quaternion.LookRotation(
                delta.normalized,
                Vector3.up);
            RuntimePrimitiveFactory.ProfileStructuralBox(beam, .007f);
        }

        private static Transform CreateGroup(string name, Transform parent)
        {
            Transform group = new GameObject(name).transform;
            group.SetParent(parent, false);
            return group;
        }

        private static float DirectionYaw(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg;
        }
    }
}
