using UnityEngine;

namespace BarPromenade
{
    public static class StairwellDressingBuilder
    {
        private static readonly Color Rust =
            new Color(0.27f, 0.105f, 0.050f);
        private static readonly Color DarkMetal =
            new Color(0.065f, 0.082f, 0.074f);
        private static readonly Color Pipe =
            new Color(0.16f, 0.19f, 0.17f);
        private static readonly Color Damp =
            new Color(0.055f, 0.072f, 0.062f);
        private static readonly Color Paper =
            new Color(0.24f, 0.22f, 0.16f);

        public static void Build(
            Transform root,
            StairwellLayoutPlan plan)
        {
            Transform dressing =
                new GameObject("Stairwell Dressing").transform;
            dressing.SetParent(root, false);
            BuildPipes(dressing, plan);
            BuildVentilation(dressing, plan);
            BuildElectricalCabinets(dressing, plan);
            BuildRadiator(dressing);
            BuildWallDamage(dressing, plan);
            BuildLooseTrash(dressing, plan);
            BuildUpperDebris(dressing, plan);
            BuildPracticalFixtures(dressing);
        }

        private static void BuildPipes(
            Transform root,
            StairwellLayoutPlan plan)
        {
            float leftWall = -plan.RoomSize.x * 0.5f + 0.19f;
            float rightWall = plan.RoomSize.x * 0.5f - 0.19f;
            CreatePipe(
                "Left Waste Stack",
                root,
                new Vector3(leftWall, 3.0f, 1.72f),
                new Vector3(0.13f, 3.0f, 0.13f),
                Quaternion.identity,
                Pipe);
            CreatePipe(
                "Left Water Riser",
                root,
                new Vector3(leftWall + 0.22f, 3.0f, 1.72f),
                new Vector3(0.055f, 3.0f, 0.055f),
                Quaternion.identity,
                Rust);
            CreatePipe(
                "Right Waste Stack",
                root,
                new Vector3(rightWall, 3.0f, 2.35f),
                new Vector3(0.10f, 3.0f, 0.10f),
                Quaternion.identity,
                Pipe);
            CreatePipe(
                "Ceiling Main Pipe",
                root,
                new Vector3(0f, 5.73f, 4.52f),
                new Vector3(0.085f, 4.0f, 0.085f),
                Quaternion.Euler(0f, 0f, 90f),
                Pipe);
            CreatePipe(
                "Apartment Feed Pipe",
                root,
                new Vector3(4.02f, 4.62f, -0.20f),
                new Vector3(0.065f, 2.05f, 0.065f),
                Quaternion.Euler(90f, 0f, 0f),
                Rust);

            for (int index = 0; index < 5; index++)
            {
                CreateSurfaceCylinder(
                    $"Pipe Clamp {index + 1}",
                    root,
                    new Vector3(
                        leftWall,
                        0.65f + index * 1.08f,
                        1.72f),
                    new Vector3(0.17f, 0.025f, 0.17f),
                    DarkMetal,
                    StairwellSurfaceKind.CorrodedMetal,
                    StairwellSurfaceProjection.CylinderCapXZ,
                    false);
            }

            CreateSurfaceCylinder(
                "Pipe Shutoff Valve",
                root,
                new Vector3(
                    leftWall + 0.22f,
                    1.26f,
                    1.72f),
                new Vector3(0.22f, 0.035f, 0.22f),
                Rust,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.CylinderCapXZ,
                false).transform.localRotation =
                Quaternion.Euler(90f, 0f, 0f);
        }

        private static void BuildVentilation(
            Transform root,
            StairwellLayoutPlan plan)
        {
            float backZ = plan.RoomSize.y * 0.5f - 0.145f;
            CreateSurfaceBox(
                "Large Vent Recess",
                root,
                new Vector3(0.72f, 3.92f, backZ),
                new Vector3(1.90f, 1.12f, 0.08f),
                DarkMetal,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXY,
                false);
            for (int index = 0; index < 8; index++)
            {
                CreateSurfaceBox(
                    $"Large Vent Slat {index + 1}",
                    root,
                    new Vector3(
                        0.72f,
                        3.48f + index * 0.125f,
                        backZ - 0.055f),
                    new Vector3(1.70f, 0.045f, 0.045f),
                    Rust,
                    StairwellSurfaceKind.CorrodedMetal,
                    StairwellSurfaceProjection.BoxXY,
                    false);
            }

            CreateSurfaceBox(
                "Ground Vent Recess",
                root,
                new Vector3(-3.10f, 0.82f, backZ),
                new Vector3(1.12f, 0.78f, 0.08f),
                DarkMetal,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXY,
                false);
            for (int index = 0; index < 5; index++)
            {
                CreateSurfaceBox(
                    $"Ground Vent Slat {index + 1}",
                    root,
                    new Vector3(
                        -3.10f,
                        0.54f + index * 0.14f,
                        backZ - 0.055f),
                    new Vector3(0.94f, 0.04f, 0.045f),
                    Rust,
                    StairwellSurfaceKind.CorrodedMetal,
                    StairwellSurfaceProjection.BoxXY,
                    false);
            }
        }

        private static void BuildElectricalCabinets(
            Transform root,
            StairwellLayoutPlan plan)
        {
            float rightX = plan.RoomSize.x * 0.5f - 0.145f;
            CreateSurfaceBox(
                "Ground Electrical Cabinet",
                root,
                new Vector3(rightX, 1.16f, -0.38f),
                new Vector3(0.10f, 1.48f, 1.12f),
                DarkMetal,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxZY,
                false);
            CreateSurfaceBox(
                "Ground Electrical Cabinet Door",
                root,
                new Vector3(rightX - 0.065f, 1.16f, -0.38f),
                new Vector3(0.05f, 1.34f, 0.98f),
                new Color(0.12f, 0.135f, 0.115f),
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxZY,
                false);
            CreateSurfaceBox(
                "Apartment Fuse Box",
                root,
                new Vector3(
                    rightX,
                    plan.ApartmentElevation + 1.30f,
                    -0.55f),
                new Vector3(0.10f, 0.82f, 0.68f),
                DarkMetal,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxZY,
                false);
            for (int index = 0; index < 4; index++)
            {
                CreateSurfaceBox(
                    $"Loose Cable {index + 1}",
                    root,
                    new Vector3(
                        rightX - 0.08f,
                        2.02f + index * 0.16f,
                        -0.82f + index * 0.18f),
                    new Vector3(0.035f, 0.035f, 0.72f),
                    new Color(0.025f, 0.027f, 0.023f),
                    StairwellSurfaceKind.CorrodedMetal,
                    StairwellSurfaceProjection.BoxZY,
                    false);
            }
        }

        private static void BuildRadiator(Transform root)
        {
            Transform radiator =
                new GameObject("Rusted Radiator").transform;
            radiator.SetParent(root, false);
            for (int index = 0; index < 7; index++)
            {
                CreateSurfaceBox(
                    $"Radiator Rib {index + 1}",
                    radiator,
                    new Vector3(
                        -3.98f,
                        0.64f,
                        -3.82f + index * 0.13f),
                    new Vector3(0.18f, 0.92f, 0.075f),
                    Rust,
                    StairwellSurfaceKind.CorrodedMetal,
                    StairwellSurfaceProjection.BoxZY,
                    false);
            }

            CreatePipe(
                "Radiator Feed",
                radiator,
                new Vector3(-3.98f, 0.28f, -2.82f),
                new Vector3(0.045f, 0.46f, 0.045f),
                Quaternion.Euler(90f, 0f, 0f),
                Rust);
        }

        private static void BuildWallDamage(
            Transform root,
            StairwellLayoutPlan plan)
        {
            float backZ = plan.RoomSize.y * 0.5f - 0.16f;
            Vector3[] dampPatches =
            {
                new Vector3(-2.80f, 2.60f, backZ),
                new Vector3(2.85f, 4.80f, backZ),
                new Vector3(0.10f, 1.05f, backZ - 0.01f),
                new Vector3(-3.35f, 4.45f, backZ)
            };
            Vector3[] dampSizes =
            {
                new Vector3(1.25f, 1.85f, 0.025f),
                new Vector3(0.92f, 1.35f, 0.025f),
                new Vector3(1.72f, 0.65f, 0.025f),
                new Vector3(0.52f, 1.05f, 0.025f)
            };
            for (int index = 0; index < dampPatches.Length; index++)
            {
                CreateSurfaceBox(
                    $"Damp Wall Patch {index + 1}",
                    root,
                    dampPatches[index],
                    dampSizes[index],
                    Damp,
                    StairwellSurfaceKind.Damage,
                    StairwellSurfaceProjection.BoxXY,
                    false);
            }

            for (int index = 0; index < 6; index++)
            {
                CreateSurfaceBox(
                    $"Peeling Paint Chip {index + 1}",
                    root,
                    new Vector3(
                        -3.30f + index * 1.12f,
                        1.55f + (index % 3) * 0.76f,
                        backZ - 0.025f),
                    new Vector3(
                        0.30f + (index % 2) * 0.16f,
                        0.18f,
                        0.018f),
                    new Color(0.28f, 0.29f, 0.23f),
                    StairwellSurfaceKind.Damage,
                    StairwellSurfaceProjection.BoxXY,
                    false);
            }

            CreateSurfaceCylinder(
                "Ground Damp Puddle",
                root,
                new Vector3(-2.80f, 0.012f, -3.05f),
                new Vector3(0.82f, 0.008f, 0.48f),
                new Color(0.025f, 0.055f, 0.050f),
                StairwellSurfaceKind.Damage,
                StairwellSurfaceProjection.CylinderCapXZ,
                false);
        }

        private static void BuildLooseTrash(
            Transform root,
            StairwellLayoutPlan plan)
        {
            Vector3[] paperPositions =
            {
                new Vector3(2.82f, 0.025f, -2.82f),
                new Vector3(-3.20f, 0.025f, -2.52f),
                new Vector3(2.60f, plan.ApartmentElevation + 0.025f, -2.52f),
                new Vector3(-0.20f, plan.MiddleElevation + 0.025f, 1.72f)
            };
            for (int index = 0;
                 index < paperPositions.Length;
                 index++)
            {
                GameObject paper = CreateSurfaceBox(
                    $"Discarded Paper {index + 1}",
                    root,
                    paperPositions[index],
                    new Vector3(0.38f, 0.012f, 0.26f),
                    Paper,
                    StairwellSurfaceKind.Debris,
                    StairwellSurfaceProjection.BoxXZ,
                    false);
                paper.transform.localRotation =
                    Quaternion.Euler(
                        0f,
                        17f + index * 31f,
                        (index % 2 == 0) ? 3f : -4f);
            }

            CreateSurfaceCylinder(
                "Empty Bottle",
                root,
                new Vector3(3.48f, 3.29f, -2.48f),
                new Vector3(0.075f, 0.22f, 0.075f),
                new Color(0.095f, 0.18f, 0.11f),
                StairwellSurfaceKind.Debris,
                StairwellSurfaceProjection.CylinderSide,
                false).transform.localRotation =
                Quaternion.Euler(0f, 0f, 78f);
        }

        private static void BuildUpperDebris(
            Transform root,
            StairwellLayoutPlan plan)
        {
            Bounds blocker = plan.UpperBlockerBounds;
            Transform debris =
                new GameObject("Impassable Upper Debris").transform;
            debris.SetParent(root, false);

            GameObject wardrobe = CreateSurfaceBox(
                "Collapsed Wardrobe",
                debris,
                blocker.center +
                new Vector3(-0.30f, 0.06f, -0.06f),
                new Vector3(1.18f, 1.72f, 0.42f),
                new Color(0.16f, 0.075f, 0.038f),
                StairwellSurfaceKind.DirtyWood,
                StairwellSurfaceProjection.BoxXY,
                false);
            wardrobe.transform.localRotation =
                Quaternion.Euler(8f, 18f, 63f);

            GameObject mattress = CreateSurfaceBox(
                "Mouldy Mattress",
                debris,
                blocker.center +
                new Vector3(0.28f, -0.08f, 0.10f),
                new Vector3(1.42f, 0.26f, 1.82f),
                new Color(0.24f, 0.22f, 0.16f),
                StairwellSurfaceKind.Debris,
                StairwellSurfaceProjection.BoxXZ,
                false);
            mattress.transform.localRotation =
                Quaternion.Euler(72f, -10f, 13f);

            CreateSurfaceBox(
                "Broken Metal Cabinet",
                debris,
                blocker.center +
                new Vector3(0.38f, -0.28f, -0.15f),
                new Vector3(0.72f, 1.05f, 0.56f),
                DarkMetal,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXY,
                false).transform.localRotation =
                Quaternion.Euler(6f, -21f, -18f);
            CreateSurfaceBox(
                "Wire Mesh Sheet",
                debris,
                blocker.center +
                new Vector3(0f, 0.18f, -0.42f),
                new Vector3(1.66f, 1.28f, 0.055f),
                Rust,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXY,
                false).transform.localRotation =
                Quaternion.Euler(0f, 8f, 11f);

            for (int index = 0; index < 5; index++)
            {
                GameObject plank = CreateSurfaceBox(
                    $"Debris Plank {index + 1}",
                    debris,
                    blocker.center +
                    new Vector3(
                        -0.50f + index * 0.25f,
                        -0.58f + (index % 2) * 0.28f,
                        -0.42f + (index % 3) * 0.20f),
                    new Vector3(0.16f, 1.48f, 0.10f),
                    new Color(0.19f, 0.105f, 0.052f),
                    StairwellSurfaceKind.DirtyWood,
                    StairwellSurfaceProjection.BoxXY,
                    false);
                plank.transform.localRotation =
                    Quaternion.Euler(
                        22f,
                        index * 19f,
                        -48f + index * 17f);
            }

            for (int index = 0; index < 3; index++)
            {
                CreateSurfaceBox(
                    $"Debris Sack {index + 1}",
                    debris,
                    blocker.center +
                    new Vector3(
                        -0.44f + index * 0.42f,
                        -0.78f,
                        -0.14f + index * 0.16f),
                    new Vector3(0.55f, 0.42f, 0.58f),
                    new Color(0.105f, 0.11f, 0.085f),
                    StairwellSurfaceKind.Debris,
                    StairwellSurfaceProjection.BoxXZ,
                    false);
            }
        }

        private static void BuildPracticalFixtures(
            Transform root)
        {
            BuildFluorescentFixture(
                root,
                "Ground",
                StairwellInteriorAtmosphere.GroundEmitterPosition,
                3.12f,
                new Color(2.20f, 3.20f, 2.20f));
            BuildFluorescentFixture(
                root,
                "Middle",
                StairwellInteriorAtmosphere.MiddleEmitterPosition,
                6.12f,
                new Color(1.65f, 2.85f, 2.35f));
            BuildFluorescentFixture(
                root,
                "Apartment",
                StairwellInteriorAtmosphere.ApartmentEmitterPosition,
                6.12f,
                new Color(3.40f, 2.15f, 0.72f));
        }

        private static void BuildFluorescentFixture(
            Transform root,
            string label,
            Vector3 position,
            float supportY,
            Color color)
        {
            CreateSurfaceBox(
                $"{label} Fluorescent Housing",
                root,
                position + Vector3.up * 0.13f,
                new Vector3(1.34f, 0.10f, 0.30f),
                DarkMetal,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.BoxXZ,
                false);
            GameObject tube = RuntimePrimitiveFactory.CreateBox(
                $"{label} Fluorescent Tube",
                root,
                position,
                new Vector3(1.18f, 0.11f, 0.15f),
                color,
                CityNightResources.EmissiveMaterial,
                false);
            for (int side = -1; side <= 1; side += 2)
            {
                CreateSurfaceBox(
                    $"{label} Fluorescent End Cap " +
                    (side < 0 ? "Left" : "Right"),
                    root,
                    position +
                    new Vector3(side * 0.64f, 0.015f, 0f),
                    new Vector3(0.10f, 0.16f, 0.22f),
                    Rust,
                    StairwellSurfaceKind.CorrodedMetal,
                    StairwellSurfaceProjection.BoxZY,
                    false);
                float suspensionHeight =
                    supportY - position.y - 0.18f;
                CreateSurfaceCylinder(
                    $"{label} Fluorescent Suspension " +
                    (side < 0 ? "Left" : "Right"),
                    root,
                    new Vector3(
                        position.x + side * 0.43f,
                        position.y + 0.18f +
                        suspensionHeight * 0.5f,
                        position.z),
                    new Vector3(
                        0.035f,
                        suspensionHeight * 0.5f,
                        0.035f),
                    Rust,
                    StairwellSurfaceKind.CorrodedMetal,
                    StairwellSurfaceProjection.CylinderSide,
                    false);
            }

            GameObject haloObject =
                new GameObject($"{label} Fluorescent Halo");
            haloObject.transform.SetParent(root, false);
            haloObject.transform.localPosition =
                tube.transform.localPosition;
            CityLightHalo halo =
                haloObject.AddComponent<CityLightHalo>();
            halo.Initialize(
                CityNightResources.AtmosphereMaterial,
                0.42f,
                1.00f,
                new Color(
                    Mathf.Min(color.r, 1f),
                    Mathf.Min(color.g, 1f),
                    Mathf.Min(color.b, 1f),
                    0.12f),
                new Color(
                    Mathf.Min(color.r, 1f),
                    Mathf.Min(color.g, 1f),
                    Mathf.Min(color.b, 1f),
                    0.03f));
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
            GameObject box = RuntimePrimitiveFactory.CreateBox(
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

        private static GameObject CreateSurfaceCylinder(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Color color,
            StairwellSurfaceKind surfaceKind,
            StairwellSurfaceProjection projection,
            bool collider = true)
        {
            GameObject cylinder = RuntimePrimitiveFactory.CreateCylinder(
                name,
                parent,
                position,
                size,
                color,
                collider);
            StairwellSurfaceAppearance.Apply(
                cylinder.GetComponent<Renderer>(),
                surfaceKind,
                projection,
                color);
            return cylinder;
        }

        private static void CreatePipe(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 size,
            Quaternion rotation,
            Color color)
        {
            GameObject pipe = CreateSurfaceCylinder(
                name,
                parent,
                position,
                size,
                color,
                StairwellSurfaceKind.CorrodedMetal,
                StairwellSurfaceProjection.CylinderSide,
                false);
            pipe.transform.localRotation = rotation;
        }
    }
}
