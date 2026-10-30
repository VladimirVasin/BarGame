using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public static class StairwellWorldBuilder
    {
        private static readonly Color Concrete =
            new Color(0.16f, 0.18f, 0.16f);
        private static readonly Color Wall =
            new Color(0.20f, 0.235f, 0.205f);
        private static readonly Color LowerWall =
            new Color(0.105f, 0.135f, 0.120f);
        private static readonly Color Stair =
            new Color(0.225f, 0.225f, 0.195f);
        private static readonly Color Rust =
            new Color(0.20f, 0.095f, 0.050f);
        private static readonly Color Metal =
            new Color(0.095f, 0.115f, 0.105f);
        private static readonly Color ApartmentDoor =
            new Color(0.16f, 0.055f, 0.034f);
        private static readonly Color StreetDoor =
            new Color(0.075f, 0.11f, 0.095f);

        public static StairwellWorldResult Build(
            Transform parent,
            StairwellLayoutPlan plan)
        {
            StairwellLayoutValidator.ValidateOrThrow(plan);
            Transform root =
                new GameObject("Apartment Stairwell").transform;
            root.SetParent(parent, false);
            // Floor, landings, treads and their hidden ramps are all one
            // poured concrete, so the root answers for every one of them.
            FootstepGround.Stamp(root, FootstepGroundKind.Concrete);

            BuildShell(root, plan);
            var stairColliders = new List<Collider>();
            BuildFlight(
                root,
                plan.LowerFlight,
                "Lower",
                stairColliders);
            BuildFlight(
                root,
                plan.ApartmentFlight,
                "Apartment",
                stairColliders);
            BuildFlight(
                root,
                plan.UpperFlight,
                "Upper",
                stairColliders);
            BuildLandings(root, plan);
            BuildRailings(root, plan);
            BuildUnderStairGrille(root);

            Transform streetDoor = BuildStreetDoor(root, plan);
            Transform apartmentDoor =
                BuildApartmentDoor(root, plan);
            Collider blocker = plan.IsCorrupted ? null : BuildUpperBlocker(root, plan);
            Collider descentWall = null, upperLanding = null;
            if (plan.IsCorrupted)
                BuildCorruptedRoute(root, plan, out descentWall, out upperLanding);
            StairwellDressingBuilder.Build(root, plan);
            if (plan.IsCorrupted) ApplyCorruptedSurfaces(root);
            return new StairwellWorldResult(
                root,
                stairColliders.AsReadOnly(),
                blocker,
                streetDoor,
                apartmentDoor,
                descentWall,
                upperLanding);
        }

        private static void BuildCorruptedRoute(Transform root, StairwellLayoutPlan plan,
            out Collider descentWall, out Collider upperLanding)
        {
            Bounds wall = plan.DescentWallBounds;
            descentWall = CreateSurfaceBox("Corrupted Descent Wall", root,
                wall.center, wall.size, Wall, StairwellSurfaceKind.WallPaint,
                StairwellSurfaceProjection.BoxXY).GetComponent<Collider>();
            Rect floor = plan.UpperLandingBounds;
            upperLanding = CreateSurfaceBox("Corrupted Upper Landing", root,
                new Vector3(floor.center.x, plan.UpperFlight.TopElevation - .12f, floor.center.y),
                new Vector3(floor.width, .24f, floor.height), Stair,
                StairwellSurfaceKind.StairConcrete, StairwellSurfaceProjection.BoxXZ)
                .GetComponent<Collider>();
            foreach (float x in new[] { floor.xMin - .055f, floor.xMax + .055f })
            {
                string side = x < floor.center.x ? "Left" : "Right";
                Transform rail = new GameObject("Corrupted Upper Landing " + side).transform;
                rail.SetParent(root, false);
                rail.localPosition = new Vector3(x, 5.38f, floor.center.y);
                rail.localRotation = Quaternion.Euler(0f, 90f, 0f);
                BuildRailSegment(rail, "Apartment Landing Center Rail", Vector3.zero,
                    new Vector3(1.2f, 1.16f, .10f), StairwellSurfaceProjection.BoxXY);
                rail.GetChild(0).name = "Corrupted Upper Landing " + side + " Rail";
                rail.GetChild(1).name = "Corrupted Upper Landing " + side + " Rail Top";
            }
            // A real open grille is the temporary playable boundary. The
            // perspective continues through it; no wall closes the upper view.
            GameObject grille = CreateSurfaceBox("Under Stair Grille Blocker", root,
                new Vector3(floor.center.x, 5.72f, floor.yMax), new Vector3(1.78f, 1.84f, .10f),
                Metal, StairwellSurfaceKind.CorrodedMetal, StairwellSurfaceProjection.BoxXY);
            grille.name = "Corrupted Upper Temporary Grille";
            for (int index = 0; index < 7; index++)
            {
                GameObject bar = CreateSurfaceBox("Under Stair Grille Bar " + (index + 1), root,
                    new Vector3(floor.center.x - .78f + index * .26f, 5.72f, floor.yMax - .06f),
                    new Vector3(.045f, 1.84f, .045f), Rust,
                    StairwellSurfaceKind.CorrodedMetal, StairwellSurfaceProjection.BoxXY, false);
                bar.name = "Corrupted Upper Temporary Grille Bar " + (index + 1);
            }
            Transform continuation = new GameObject("Corrupted Endless Staircase").transform;
            continuation.SetParent(root, false);
            const int modules = 12;
            for (int index = 0; index < modules; index++)
            {
                // Keep the distant run open above its rails: a low tunnel roof
                // would become a false end wall from the apartment camera.
                const string binding = "Corrupted Endless Open Flight";
                GameObject flight = CreateSurfaceBox(binding, continuation,
                    new Vector3(-1.45f, 5.6f + index * 1.6f, 4.17f + index * 3.84f),
                    new Vector3(2.03f, 4.34f, 3.84f), new Color(.18f, .23f, .19f),
                    StairwellSurfaceKind.StairConcrete, StairwellSurfaceProjection.BoxXZ, false);
                flight.name = "Distant Flight " + (index + 1);
            }
        }

        // One construction pass over this room only. Reuse the authored damage
        // and rust families; no material instances or recurring scene scans.
        private static void ApplyCorruptedSurfaces(Transform root)
        {
            var kinds = (StairwellSurfaceKind[])System.Enum.GetValues(typeof(StairwellSurfaceKind));
            var properties = new MaterialPropertyBlock();
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>())
            {
                properties.Clear();
                renderer.GetPropertyBlock(properties);
                Texture texture = properties.GetTexture("_BaseMap");
                foreach (StairwellSurfaceKind kind in kinds)
                {
                    if (texture != StairwellSurfaceAppearance.GetTexture(kind)) continue;
                    // Keep repeatable fine material detail; the large damage
                    // chart covers this complete wall/object exactly once.
                    SurfaceDamageLibrary.Entry entry = SurfaceDamageLibrary.Resolve(
                        "stairwell", null, renderer.name, kind.ToString());
                    SurfaceDamageLibrary.Apply(renderer, properties, entry, root, 1f);
                    renderer.SetPropertyBlock(properties);
                    break;
                }
            }
        }

        private static void BuildShell(
            Transform root,
            StairwellLayoutPlan plan)
        {
            float width = plan.RoomSize.x;
            float depth = plan.RoomSize.y;
            float height = plan.RoomHeight;
            const float wallThickness = 0.24f;

            CreateSurfaceBox(
                "Stairwell Ground Floor",
                root,
                new Vector3(0f, -0.12f, 0f),
                new Vector3(width, 0.24f, depth),
                Concrete,
                StairwellSurfaceKind.Concrete,
                StairwellSurfaceProjection.BoxXZ);
            CreateSurfaceBox(
                plan.IsCorrupted ? "Stairwell Corrupted Ceiling" : "Stairwell Ceiling",
                root,
                new Vector3(0f, height + 0.10f, 0f),
                new Vector3(width, 0.20f, depth),
                new Color(0.075f, 0.085f, 0.077f),
                StairwellSurfaceKind.Concrete,
                StairwellSurfaceProjection.BoxXZ, !plan.IsCorrupted);
            if (plan.IsCorrupted)
            {
                // Collision follows the authored notch instead of filling it.
                Transform ceiling = root.Find("Stairwell Corrupted Ceiling");
                ceiling.gameObject.AddComponent<MeshCollider>().sharedMesh = ceiling.GetComponent<MeshFilter>().sharedMesh;
            }
            CreateSurfaceBox(
                plan.IsCorrupted ? "Stairwell Corrupted Back Wall" : "Stairwell Back Wall",
                root,
                new Vector3(0f, height * 0.5f, depth * 0.5f),
                new Vector3(width, height, wallThickness),
                Wall,
                StairwellSurfaceKind.WallPaint,
                StairwellSurfaceProjection.BoxXY, !plan.IsCorrupted);
            if (plan.IsCorrupted)
            {
                Transform back = root.Find("Stairwell Corrupted Back Wall");
                back.gameObject.AddComponent<MeshCollider>().sharedMesh = back.GetComponent<MeshFilter>().sharedMesh;
            }
            CreateSurfaceBox(
                "Stairwell Front Wall",
                root,
                new Vector3(0f, height * 0.5f, -depth * 0.5f),
                new Vector3(width, height, wallThickness),
                Wall,
                StairwellSurfaceKind.WallPaint,
                StairwellSurfaceProjection.BoxXY);
            CreateSurfaceBox(
                "Stairwell Left Wall",
                root,
                new Vector3(-width * 0.5f, height * 0.5f, 0f),
                new Vector3(wallThickness, height, depth),
                Wall,
                StairwellSurfaceKind.WallPaint,
                StairwellSurfaceProjection.BoxZY);
            CreateSurfaceBox(
                "Stairwell Right Wall",
                root,
                new Vector3(width * 0.5f, height * 0.5f, 0f),
                new Vector3(wallThickness, height, depth),
                Wall,
                StairwellSurfaceKind.WallPaint,
                StairwellSurfaceProjection.BoxZY);

            BuildLowerWallBand(root, plan);
            BuildStructuralColumns(root, plan);
        }

        private static void BuildLowerWallBand(
            Transform root,
            StairwellLayoutPlan plan)
        {
            float halfWidth = plan.RoomSize.x * 0.5f;
            float halfDepth = plan.RoomSize.y * 0.5f;
            const float bandHeight = 1.18f;
            const float inset = 0.135f;
            CreateSurfaceBox(
                "Back Dirty Wall Band",
                root,
                new Vector3(0f, bandHeight * 0.5f, halfDepth - inset),
                new Vector3(
                    plan.RoomSize.x - 0.28f,
                    bandHeight,
                    0.025f),
                LowerWall,
                StairwellSurfaceKind.WallPaint,
                StairwellSurfaceProjection.BoxXY,
                false);
            CreateSurfaceBox(
                "Front Dirty Wall Band",
                root,
                new Vector3(0f, bandHeight * 0.5f, -halfDepth + inset),
                new Vector3(
                    plan.RoomSize.x - 0.28f,
                    bandHeight,
                    0.025f),
                LowerWall,
                StairwellSurfaceKind.WallPaint,
                StairwellSurfaceProjection.BoxXY,
                false);
            CreateSurfaceBox(
                "Left Dirty Wall Band",
                root,
                new Vector3(-halfWidth + inset, bandHeight * 0.5f, 0f),
                new Vector3(
                    0.025f,
                    bandHeight,
                    plan.RoomSize.y - 0.28f),
                LowerWall,
                StairwellSurfaceKind.WallPaint,
                StairwellSurfaceProjection.BoxZY,
                false);
            CreateSurfaceBox(
                "Right Dirty Wall Band",
                root,
                new Vector3(halfWidth - inset, bandHeight * 0.5f, 0f),
                new Vector3(
                    0.025f,
                    bandHeight,
                    plan.RoomSize.y - 0.28f),
                LowerWall,
                StairwellSurfaceKind.WallPaint,
                StairwellSurfaceProjection.BoxZY,
                false);
        }

        private static void BuildStructuralColumns(
            Transform root,
            StairwellLayoutPlan plan)
        {
            Vector3[] positions =
            {
                new Vector3(-2.47f, plan.RoomHeight * 0.5f, 1.45f),
                new Vector3(2.47f, plan.RoomHeight * 0.5f, 1.45f)
            };
            for (int index = 0; index < positions.Length; index++)
            {
                CreateSurfaceBox(
                    $"Stairwell Column {index + 1}",
                    root,
                    positions[index],
                    new Vector3(0.34f, plan.RoomHeight, 0.34f),
                    Concrete,
                    StairwellSurfaceKind.Concrete,
                    StairwellSurfaceProjection.BoxXY);
            }
        }

        private static void BuildFlight(
            Transform root,
            StairwellFlightPlan flight,
            string label,
            List<Collider> colliders)
        {
            float yaw = Mathf.Atan2(
                flight.Direction.x,
                flight.Direction.y) * Mathf.Rad2Deg;
            for (int index = 0;
                 index < flight.StepCount;
                 index++)
            {
                GameObject step = CreateSurfaceBox(
                    $"{label} Stair {index + 1:00}",
                    root,
                    flight.GetStepCenter(index),
                    flight.GetStepSize(index),
                    Stair,
                    StairwellSurfaceKind.StairConcrete,
                    StairwellSurfaceProjection.BoxXZ,
                    false);
                step.transform.localRotation =
                    Quaternion.Euler(0f, yaw, 0f);
                // The controller walks the hidden ramp below; the tread
                // itself is a surface only the hero's foot probes see.
                FootProbeSurface.AddTreadCollider(step);
            }

            colliders.Add(
                BuildFlightRamp(root, flight, label));
        }

        private static Collider BuildFlightRamp(
            Transform root,
            StairwellFlightPlan flight,
            string label)
        {
            Vector3 slopeDirection = new Vector3(
                flight.Direction.x * flight.RunLength,
                flight.TopElevation - flight.BaseElevation,
                flight.Direction.y * flight.RunLength);
            Quaternion rotation = Quaternion.LookRotation(
                slopeDirection.normalized,
                Vector3.up);
            Vector3 upperNormal = rotation * Vector3.up;
            const float thickness = 0.12f;
            Vector2 planarCenter =
                flight.Start +
                flight.Direction * (flight.RunLength * 0.5f);
            Vector3 surfaceCenter = new Vector3(
                planarCenter.x,
                (flight.BaseElevation + flight.TopElevation) * 0.5f,
                planarCenter.y);
            GameObject ramp = RuntimePrimitiveFactory.CreateBox(
                $"{label} Stair Walkable Ramp",
                root,
                surfaceCenter - upperNormal * (thickness * 0.5f),
                new Vector3(
                    flight.Width,
                    thickness,
                    slopeDirection.magnitude),
                Stair);
            ramp.transform.localRotation = rotation;
            Renderer renderer = ramp.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            return ramp.GetComponent<Collider>();
        }

        private static void BuildLandings(
            Transform root,
            StairwellLayoutPlan plan)
        {
            CreateLanding(
                "Middle Landing",
                root,
                plan.MiddleLandingBounds,
                plan.MiddleElevation);
            CreateLanding(
                "Apartment Landing",
                root,
                plan.ApartmentLandingBounds,
                plan.ApartmentElevation);
        }

        private static void CreateLanding(
            string name,
            Transform root,
            Rect bounds,
            float elevation)
        {
            CreateSurfaceBox(
                name,
                root,
                new Vector3(
                    bounds.center.x,
                    elevation - 0.12f,
                    bounds.center.y),
                new Vector3(
                    bounds.width,
                    0.24f,
                    bounds.height),
                Stair,
                StairwellSurfaceKind.StairConcrete,
                StairwellSurfaceProjection.BoxXZ);
        }

        private static void BuildRailings(
            Transform root,
            StairwellLayoutPlan plan)
        {
            BuildFlightRailings(root, plan.LowerFlight, "Lower");
            BuildFlightRailings(
                root,
                plan.ApartmentFlight,
                "Apartment");
            BuildFlightRailings(root, plan.UpperFlight, "Upper");

            BuildRailSegment(
                root,
                "Middle Landing Back Rail",
                new Vector3(0f, plan.MiddleElevation + 0.58f, 2.32f),
                new Vector3(4.75f, 1.16f, 0.10f),
                StairwellSurfaceProjection.BoxXY);
            BuildRailSegment(
                root,
                "Apartment Landing Center Rail",
                new Vector3(
                    0f,
                    plan.ApartmentElevation + 0.58f,
                    -2.96f),
                new Vector3(1.20f, 1.16f, 0.10f),
                StairwellSurfaceProjection.BoxXY);
            BuildRailSegment(
                root,
                "Apartment Landing Right Rail",
                new Vector3(
                    3.16f,
                    plan.ApartmentElevation + 0.58f,
                    -2.96f),
                new Vector3(1.65f, 1.16f, 0.10f),
                StairwellSurfaceProjection.BoxXY);
            BuildRailSegment(
                root,
                "Apartment Landing Left Edge",
                new Vector3(
                    -2.48f,
                    plan.ApartmentElevation + 0.58f,
                    -3.20f),
                new Vector3(0.10f, 1.16f, 2.35f),
                StairwellSurfaceProjection.BoxZY);
        }

        private static void BuildFlightRailings(
            Transform root,
            StairwellFlightPlan flight,
            string label)
        {
            Vector2 side =
                new Vector2(
                    -flight.Direction.y,
                    flight.Direction.x);
            for (int index = 0;
                 index < flight.StepCount;
                 index++)
            {
                Vector3 step = flight.GetStepCenter(index);
                float top =
                    flight.BaseElevation +
                    ((index + 1) * flight.StepRise);
                for (int sideIndex = -1;
                     sideIndex <= 1;
                     sideIndex += 2)
                {
                    Vector2 offset =
                        side *
                        (flight.Width * 0.5f + 0.055f) *
                        sideIndex;
                    CreateSurfaceBox(
                        $"{label} Rail Curb {index + 1:00} " +
                        (sideIndex < 0 ? "A" : "B"),
                        root,
                        new Vector3(
                            step.x + offset.x,
                            top + 0.34f,
                            step.z + offset.y),
                        new Vector3(
                            0.11f,
                            0.68f,
                            flight.StepDepth + 0.02f),
                        Metal,
                        StairwellSurfaceKind.CorrodedMetal,
                        StairwellSurfaceProjection.BoxZY);
                }
            }

            BuildRailTop(root, flight, label, side, -1);
            BuildRailTop(root, flight, label, side, 1);
        }

        private static void BuildRailTop(
            Transform root,
            StairwellFlightPlan flight,
            string label,
            Vector2 side,
            int sideIndex)
        {
            Vector2 startPlanar =
                flight.Start +
                side *
                (flight.Width * 0.5f + 0.055f) *
                sideIndex;
            Vector2 endPlanar =
                flight.Start +
                flight.Direction * flight.RunLength +
                side *
                (flight.Width * 0.5f + 0.055f) *
                sideIndex;
            Vector3 start = new Vector3(
                startPlanar.x,
                flight.BaseElevation + 1.02f,
                startPlanar.y);
            Vector3 end = new Vector3(
                endPlanar.x,
                flight.TopElevation + 1.02f,
                endPlanar.y);
            string handrailName = $"{label} Handrail " + (sideIndex < 0 ? "A" : "B");
            Transform handrail = CreateBeamBetween(
                handrailName,
                root,
                start,
                end,
                0.095f,
                Rust);
            // The Blender assembly is authored in the beam's metric frame.
            // It is a sibling so the wood's nonuniform scale cannot stretch it.
            GameObject supports = StairwellAuthoredVisualFactory.CreateAnchoredPart(
                handrailName + " Supports", root, handrail.localPosition, handrail.localRotation, Metal);
            StairwellSurfaceAppearance.Apply(supports.GetComponent<Renderer>(),
                StairwellSurfaceKind.CorrodedMetal, StairwellSurfaceProjection.BoxZY, Metal);
        }

        private static void BuildRailSegment(
            Transform root,
            string name,
            Vector3 center,
            Vector3 size,
            StairwellSurfaceProjection projection)
        {
            CreateSurfaceBox(
                name,
                root,
                center,
                size,
                Metal,
                StairwellSurfaceKind.CorrodedMetal,
                projection);
            CreateSurfaceBox(
                name + " Top",
                root,
                center + Vector3.up * (size.y * 0.5f),
                new Vector3(size.x + 0.08f, 0.10f, size.z + 0.08f),
                Rust,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXZ,
                false);
        }

        private static void BuildUnderStairGrille(Transform root)
        {
            Transform grille =
                new GameObject("Under Stair Grille").transform;
            grille.SetParent(root, false);
            CreateSurfaceBox(
                "Under Stair Grille Blocker",
                grille,
                new Vector3(1.45f, 0.92f, -2.95f),
                new Vector3(1.78f, 1.84f, 0.10f),
                Metal,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXY);
            for (int index = 0; index < 7; index++)
            {
                CreateSurfaceBox(
                    $"Under Stair Grille Bar {index + 1}",
                    grille,
                    new Vector3(
                        0.67f + index * 0.26f,
                        0.92f,
                        -3.01f),
                    new Vector3(0.045f, 1.84f, 0.045f),
                    Rust,
                    StairwellSurfaceKind.CorrodedMetal,
                    StairwellSurfaceProjection.BoxXY,
                    false);
            }
        }

        private static Transform BuildStreetDoor(
            Transform root,
            StairwellLayoutPlan plan)
        {
            Transform door = new GameObject("Street Door").transform;
            door.SetParent(root, false);
            door.localPosition =
                new Vector3(
                    plan.StreetExitPosition.x,
                    0f,
                    -plan.RoomSize.y * 0.5f + 0.13f);
            CreateSurfaceBox(
                "Street Door Leaf",
                door,
                new Vector3(0f, 1.18f, 0f),
                new Vector3(1.72f, 2.36f, 0.13f),
                StreetDoor,
                StairwellSurfaceKind.DoorPaint,
                StairwellSurfaceProjection.BoxXY,
                false);
            BuildDoorFrame(
                door,
                Vector3.zero,
                Quaternion.identity,
                1.72f,
                2.36f);
            BuildDoorHardware(door, "Street Door Hardware", plan.StreetDoorHardwareLocal, plan.StreetDoorHardwareRotation);
            return door;
        }

        private static Transform BuildApartmentDoor(
            Transform root,
            StairwellLayoutPlan plan)
        {
            Transform door =
                new GameObject("Apartment Door").transform;
            door.SetParent(root, false);
            door.localPosition =
                new Vector3(
                    plan.RoomSize.x * 0.5f - 0.13f,
                    plan.ApartmentElevation,
                    plan.ApartmentEntrancePosition.z);
            CreateSurfaceBox(
                "Apartment Door Leaf",
                door,
                new Vector3(0f, 1.16f, 0f),
                new Vector3(0.13f, 2.32f, 1.24f),
                ApartmentDoor,
                StairwellSurfaceKind.DoorPaint,
                StairwellSurfaceProjection.BoxZY,
                false);
            BuildDoorFrame(
                door,
                Vector3.zero,
                Quaternion.Euler(0f, 90f, 0f),
                1.24f,
                2.32f);
            BuildDoorHardware(door, "Apartment Door Hardware", plan.ApartmentDoorHardwareLocal, plan.ApartmentDoorHardwareRotation);
            return door;
        }

        private static void BuildDoorHardware(Transform door, string name, Vector3 position, Quaternion rotation)
        {
            GameObject hardware = CreateSurfaceBox(name, door, position, new Vector3(.240f, .240f, .075f),
                new Color(.38f, .31f, .18f), StairwellSurfaceKind.CorrodedMetal, StairwellSurfaceProjection.BoxXY, false);
            hardware.transform.localRotation = rotation;
        }

        private static void BuildDoorFrame(
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            float width,
            float height)
        {
            Transform frame = new GameObject("Door Frame").transform;
            frame.SetParent(parent, false);
            frame.localPosition = position;
            frame.localRotation = rotation;
            CreateSurfaceBox(
                "Door Frame Left",
                frame,
                new Vector3(-width * 0.5f - 0.08f, height * 0.5f, 0f),
                new Vector3(0.16f, height + 0.20f, 0.20f),
                Rust,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXY,
                false);
            CreateSurfaceBox(
                "Door Frame Right",
                frame,
                new Vector3(width * 0.5f + 0.08f, height * 0.5f, 0f),
                new Vector3(0.16f, height + 0.20f, 0.20f),
                Rust,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXY,
                false);
            CreateSurfaceBox(
                "Door Frame Lintel",
                frame,
                new Vector3(0f, height + 0.10f, 0f),
                new Vector3(width + 0.32f, 0.18f, 0.20f),
                Rust,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXY,
                false);
        }

        private static Collider BuildUpperBlocker(
            Transform root,
            StairwellLayoutPlan plan)
        {
            Bounds bounds = plan.UpperBlockerBounds;
            GameObject blocker = RuntimePrimitiveFactory.CreateBox(
                "Upper Stair Debris Safety Blocker",
                root,
                bounds.center,
                bounds.size,
                new Color(0.02f, 0.02f, 0.018f));
            Renderer renderer = blocker.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.enabled = false;
            }

            return blocker.GetComponent<Collider>();
        }

        private static GameObject CreateSurfaceBox(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Color color,
            StairwellSurfaceKind surfaceKind,
            StairwellSurfaceProjection projection,
            bool collider = true)
        {
            GameObject box = StairwellAuthoredVisualFactory.CreateBox(
                name,
                parent,
                position,
                size,
                color,
                collider);
            StairwellSurfaceAppearance.Apply(
                box.GetComponent<Renderer>(),
                surfaceKind,
                projection,
                color);
            return box;
        }

        private static Transform CreateBeamBetween(
            string name,
            Transform parent,
            Vector3 start,
            Vector3 end,
            float thickness,
            Color color)
        {
            Vector3 direction = end - start;
            GameObject beam = CreateSurfaceBox(
                name,
                parent,
                (start + end) * 0.5f,
                new Vector3(
                    thickness,
                    thickness,
                    direction.magnitude),
                color,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxZY,
                false);
            beam.transform.localRotation =
                Quaternion.LookRotation(direction.normalized, Vector3.up);
            return beam.transform;
        }
    }
}
