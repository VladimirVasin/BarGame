using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public static class BarInteriorWorldBuilder
    {
        private static readonly Color FloorColor =
            new Color(0.095f, 0.035f, 0.024f);
        private static readonly Color WallColor =
            new Color(0.29f, 0.075f, 0.075f);
        private static readonly Color WallPanelColor =
            new Color(0.13f, 0.042f, 0.032f);
        private static readonly Color DarkWoodColor =
            new Color(0.075f, 0.024f, 0.017f);
        private static readonly Color WoodColor =
            new Color(0.16f, 0.055f, 0.028f);
        private static readonly Color LeatherColor =
            new Color(0.30f, 0.035f, 0.045f);
        private static readonly Color BrassColor =
            new Color(0.86f, 0.46f, 0.14f);
        private static readonly Color TealGlassColor =
            new Color(0.055f, 0.18f, 0.19f);

        public static Transform Build(
            Transform parent,
            BarInteriorLayoutPlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Transform room = new GameObject(
                $"Interior {plan.BarId}").transform;
            room.SetParent(parent, false);

            BuildShell(room, plan);
            BuildWallPanels(room, plan);
            BuildCounter(room, plan);
            BuildBackbar(room, plan);
            BuildBooths(room, plan);
            BuildStage(room, plan);
            BuildActivityBay(room);
            BuildSocialTables(room, plan);
            BuildEntranceDress(room, plan);
            BuildWallDress(room);
            BuildCeilingFan(room);
            BuildActivityDress(room, plan);
            BuildPracticalFixtures(room, plan);
            return room;
        }

        private static void BuildShell(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            float width = plan.RoomSize.x;
            float depth = plan.RoomSize.y;
            float height = plan.RoomHeight;
            float wall = plan.WallThickness;
            float doorWidth = 3.2f;
            float frontSegmentWidth = (width - doorWidth) * 0.5f;
            float frontSegmentOffset =
                doorWidth * 0.5f + frontSegmentWidth * 0.5f;

            RuntimePrimitiveFactory.CreateBox(
                "Floor",
                room,
                new Vector3(0f, -0.12f, 0f),
                new Vector3(width, 0.24f, depth),
                FloorColor);
            RuntimePrimitiveFactory.CreateBox(
                "Ceiling",
                room,
                new Vector3(0f, height + 0.10f, 0f),
                new Vector3(width, 0.20f, depth),
                new Color(0.045f, 0.021f, 0.020f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Back Wall",
                room,
                new Vector3(0f, height * 0.5f, depth * 0.5f),
                new Vector3(width, height, wall),
                WallColor);
            RuntimePrimitiveFactory.CreateBox(
                "Left Wall",
                room,
                new Vector3(-width * 0.5f, height * 0.5f, 0f),
                new Vector3(wall, height, depth),
                WallColor);
            RuntimePrimitiveFactory.CreateBox(
                "Right Wall",
                room,
                new Vector3(width * 0.5f, height * 0.5f, 0f),
                new Vector3(wall, height, depth),
                WallColor);
            RuntimePrimitiveFactory.CreateBox(
                "Front Wall Left",
                room,
                new Vector3(
                    -frontSegmentOffset,
                    height * 0.5f,
                    -depth * 0.5f),
                new Vector3(frontSegmentWidth, height, wall),
                WallColor);
            RuntimePrimitiveFactory.CreateBox(
                "Front Wall Right",
                room,
                new Vector3(
                    frontSegmentOffset,
                    height * 0.5f,
                    -depth * 0.5f),
                new Vector3(frontSegmentWidth, height, wall),
                WallColor);

            RuntimePrimitiveFactory.CreateBox(
                "Entrance Left Post",
                room,
                new Vector3(-1.72f, 2.25f, -depth * 0.5f + 0.04f),
                new Vector3(0.28f, 4.5f, 0.42f),
                BrassColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Entrance Right Post",
                room,
                new Vector3(1.72f, 2.25f, -depth * 0.5f + 0.04f),
                new Vector3(0.28f, 4.5f, 0.42f),
                BrassColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Entrance Lintel",
                room,
                new Vector3(0f, 4.20f, -depth * 0.5f + 0.04f),
                new Vector3(3.7f, 0.30f, 0.42f),
                BrassColor,
                false);

            var crossBeams = new List<Bounds>();
            for (float x = -9f; x <= 9.01f; x += 3f)
            {
                crossBeams.Add(new Bounds(
                    new Vector3(x, height - 0.18f, 0f),
                    new Vector3(0.22f, 0.34f, depth - 0.35f)));
            }

            SetNoShadows(RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Ceiling Cross Beams",
                room,
                crossBeams,
                DarkWoodColor));

            var longBeams = new List<Bounds>
            {
                new Bounds(
                    new Vector3(-5.4f, height - 0.28f, 0f),
                    new Vector3(0.32f, 0.50f, depth - 0.30f)),
                new Bounds(
                    new Vector3(5.4f, height - 0.28f, 0f),
                    new Vector3(0.32f, 0.50f, depth - 0.30f))
            };
            SetNoShadows(RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Ceiling Long Beams",
                room,
                longBeams,
                DarkWoodColor));
        }

        private static void BuildWallPanels(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            float width = plan.RoomSize.x;
            float depth = plan.RoomSize.y;
            var panels = new List<Bounds>
            {
                new Bounds(
                    new Vector3(0f, 0.82f, depth * 0.5f - 0.19f),
                    new Vector3(width - 0.55f, 1.58f, 0.10f)),
                new Bounds(
                    new Vector3(-width * 0.5f + 0.19f, 0.82f, 0f),
                    new Vector3(0.10f, 1.58f, depth - 0.55f)),
                new Bounds(
                    new Vector3(width * 0.5f - 0.19f, 0.82f, 0f),
                    new Vector3(0.10f, 1.58f, depth - 0.55f))
            };
            SetNoShadows(RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Wall Wainscot",
                room,
                panels,
                WallPanelColor));

            var rails = new List<Bounds>
            {
                new Bounds(
                    new Vector3(0f, 1.64f, depth * 0.5f - 0.26f),
                    new Vector3(width - 0.40f, 0.10f, 0.12f)),
                new Bounds(
                    new Vector3(-width * 0.5f + 0.26f, 1.64f, 0f),
                    new Vector3(0.12f, 0.10f, depth - 0.40f)),
                new Bounds(
                    new Vector3(width * 0.5f - 0.26f, 1.64f, 0f),
                    new Vector3(0.12f, 0.10f, depth - 0.40f))
            };
            SetNoShadows(RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Wall Brass Rails",
                room,
                rails,
                BrassColor));
        }

        private static void BuildCounter(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            Vector3 counter = plan.CounterPosition;
            Vector3 size = plan.CounterSize;
            RuntimePrimitiveFactory.CreateBox(
                "Bar Counter",
                room,
                counter,
                size,
                DarkWoodColor);
            RuntimePrimitiveFactory.CreateBox(
                "Counter Top",
                room,
                counter + Vector3.up * (size.y * 0.5f + 0.08f),
                new Vector3(size.x + 0.45f, 0.16f, size.z + 0.32f),
                BrassColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Counter Foot Rail",
                room,
                counter + new Vector3(
                    0f,
                    -size.y * 0.29f,
                    -size.z * 0.62f),
                new Vector3(size.x - 0.45f, 0.10f, 0.10f),
                BrassColor,
                false);

            var panels = new List<Bounds>();
            float panelWidth = (size.x - 0.65f) / 7f;
            for (int index = 0; index < 7; index++)
            {
                float x =
                    -size.x * 0.5f +
                    0.33f +
                    panelWidth * (index + 0.5f);
                panels.Add(new Bounds(
                    counter + new Vector3(
                        x,
                        0f,
                        -size.z * 0.51f),
                    new Vector3(panelWidth - 0.11f, size.y - 0.20f, 0.08f)));
            }

            SetNoShadows(RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Counter Front Panels",
                room,
                panels,
                WoodColor));

            float[] stoolXs = { -4.25f, -2.55f, -0.85f, 0.85f, 2.55f, 4.25f };
            for (int index = 0; index < stoolXs.Length; index++)
            {
                Vector3 stoolPosition = new Vector3(
                    stoolXs[index],
                    0f,
                    counter.z - size.z * 0.5f - 0.72f);
                Vector2 stationDelta = new Vector2(
                    stoolPosition.x - plan.CounterStationPosition.x,
                    stoolPosition.z - plan.CounterStationPosition.z);
                if (stationDelta.sqrMagnitude < 1.35f * 1.35f)
                {
                    continue;
                }

                BuildStool(
                    room,
                    $"Bar Stool {index + 1}",
                    stoolPosition);
            }

            for (int index = 0; index < 5; index++)
            {
                float x = -2.2f + index * 1.1f;
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Beer Tap Stem {index + 1}",
                    room,
                    new Vector3(
                        x,
                        counter.y + size.y * 0.5f + 0.34f,
                        counter.z),
                    new Vector3(0.08f, 0.26f, 0.08f),
                    BrassColor,
                    false);
                RuntimePrimitiveFactory.CreateBox(
                    $"Beer Tap Handle {index + 1}",
                    room,
                    new Vector3(
                        x,
                        counter.y + size.y * 0.5f + 0.63f,
                        counter.z),
                    new Vector3(0.13f, 0.30f, 0.13f),
                    index % 2 == 0 ? LeatherColor : TealGlassColor,
                    false);
            }
        }

        private static void BuildBackbar(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            BarInteriorFurnitureFootprint footprint =
                RequireFurniture(
                    plan,
                    BarInteriorFurnitureKind.BackBar);
            float backZ = footprint.Bounds.yMax - 0.025f;
            RuntimePrimitiveFactory.CreateBox(
                "Backbar Cabinet",
                room,
                new Vector3(
                    footprint.Bounds.center.x,
                    footprint.Height * 0.5f,
                    footprint.Bounds.center.y),
                new Vector3(
                    footprint.Bounds.width,
                    footprint.Height,
                    footprint.Bounds.height),
                DarkWoodColor);

            var mirrorPanels = new List<Bounds>();
            for (int index = 0; index < 5; index++)
            {
                mirrorPanels.Add(new Bounds(
                    new Vector3(
                        -4.25f + index * 2.125f,
                        2.72f,
                        backZ - 0.04f),
                    new Vector3(1.82f, 2.55f, 0.055f)));
            }

            SetNoShadows(RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Backbar Mirror Panels",
                room,
                mirrorPanels,
                TealGlassColor));

            var shelfBoxes = new List<Bounds>();
            for (int row = 0; row < 3; row++)
            {
                shelfBoxes.Add(new Bounds(
                    new Vector3(
                        0f,
                        1.62f + row * 0.72f,
                        backZ - 0.12f),
                    new Vector3(10.7f, 0.10f, 0.36f)));
            }

            for (int column = 0; column < 6; column++)
            {
                shelfBoxes.Add(new Bounds(
                    new Vector3(
                        -5.15f + column * 2.06f,
                        2.52f,
                        backZ - 0.13f),
                    new Vector3(0.09f, 2.58f, 0.34f)));
            }

            SetNoShadows(RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Backbar Shelves",
                room,
                shelfBoxes,
                BrassColor));
            BuildBottleSilhouettes(room, backZ - 0.28f);

            RuntimePrimitiveFactory.CreateBox(
                "Backbar Crown",
                room,
                new Vector3(0f, 4.14f, backZ - 0.12f),
                new Vector3(11.35f, 0.34f, 0.42f),
                DarkWoodColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Backbar Amber Sign",
                room,
                new Vector3(0f, 4.12f, backZ - 0.35f),
                new Vector3(4.6f, 0.18f, 0.08f),
                new Color(2.6f, 1.10f, 0.24f),
                CityNightResources.EmissiveMaterial,
                false);
        }

        private static void BuildBottleSilhouettes(
            Transform room,
            float z)
        {
            Color[] colors =
            {
                new Color(0.72f, 0.22f, 0.07f),
                new Color(0.12f, 0.38f, 0.25f),
                new Color(0.46f, 0.15f, 0.40f),
                new Color(0.72f, 0.62f, 0.32f)
            };
            var boxesByColor = new List<Bounds>[colors.Length];
            for (int index = 0; index < boxesByColor.Length; index++)
            {
                boxesByColor[index] = new List<Bounds>();
            }

            for (int row = 0; row < 3; row++)
            {
                for (int column = 0; column < 18; column++)
                {
                    float x = -4.85f + column * 0.57f;
                    // The lower central shelf is reserved for the nine
                    // individually selectable retail bottles built by the
                    // drink-service presentation. Keep the remaining backbar
                    // dressing combined so the physical menu does not turn
                    // every decorative silhouette into a draw call.
                    if (row == 0 && x >= -4.35f && x <= 2.0f)
                    {
                        continue;
                    }

                    float height =
                        0.25f + ((column * 7 + row * 3) % 4) * 0.045f;
                    boxesByColor[(column + row * 2) % colors.Length].Add(
                        new Bounds(
                            new Vector3(
                                x,
                                1.83f + row * 0.72f,
                                z),
                            new Vector3(0.15f, height, 0.14f)));
                }
            }

            for (int index = 0; index < boxesByColor.Length; index++)
            {
                SetNoShadows(RuntimePrimitiveFactory.CreateCombinedBoxes(
                    $"Bottle Silhouettes {index + 1}",
                    room,
                    boxesByColor[index],
                    colors[index]));
            }
        }

        private static void BuildBooths(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            int boothIndex = 0;
            for (int index = 0;
                 index < plan.FurnitureFootprints.Count;
                 index++)
            {
                BarInteriorFurnitureFootprint footprint =
                    plan.FurnitureFootprints[index];
                if (footprint.Kind !=
                    BarInteriorFurnitureKind.Booth)
                {
                    continue;
                }

                boothIndex++;
                Rect bounds = footprint.Bounds;
                float z = bounds.center.y;
                float baseX = bounds.xMin + 0.88f;
                float tableX = bounds.xMax - 0.59f;
                float backX = bounds.xMin + 0.12f;
                float depth = bounds.height - 0.02f;
                RuntimePrimitiveFactory.CreateBox(
                    $"Booth Base {boothIndex}",
                    room,
                    new Vector3(baseX, 0.31f, z),
                    new Vector3(1.72f, 0.62f, depth),
                    DarkWoodColor);
                RuntimePrimitiveFactory.CreateBox(
                    $"Booth Cushion {boothIndex}",
                    room,
                    new Vector3(baseX + 0.10f, 0.68f, z),
                    new Vector3(1.70f, 0.18f, depth - 0.10f),
                    LeatherColor,
                    false);
                RuntimePrimitiveFactory.CreateBox(
                    $"Booth Back {boothIndex}",
                    room,
                    new Vector3(backX, 1.32f, z),
                    new Vector3(0.24f, 1.55f, bounds.height),
                    LeatherColor);
                RuntimePrimitiveFactory.CreateBox(
                    $"Booth Table Top {boothIndex}",
                    room,
                    new Vector3(tableX, 0.88f, z),
                    new Vector3(1.18f, 0.12f, 1.48f),
                    BrassColor);
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Booth Table Leg {boothIndex}",
                    room,
                    new Vector3(tableX, 0.43f, z),
                    new Vector3(0.18f, 0.43f, 0.18f),
                    DarkWoodColor);
            }
        }

        private static void BuildStage(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            BarInteriorFurnitureFootprint footprint =
                RequireFurniture(
                    plan,
                    BarInteriorFurnitureKind.Stage);
            Rect bounds = footprint.Bounds;
            float centerX = bounds.center.x;
            float centerZ = bounds.center.y;
            float curtainZ = bounds.yMax + 0.24f;
            RuntimePrimitiveFactory.CreateBox(
                "Small Stage",
                room,
                new Vector3(
                    centerX,
                    footprint.Height * 0.5f,
                    centerZ),
                new Vector3(
                    bounds.width,
                    footprint.Height,
                    bounds.height),
                DarkWoodColor);
            RuntimePrimitiveFactory.CreateBox(
                "Stage Left Curtain",
                room,
                new Vector3(bounds.xMin + 0.16f, 2.55f, curtainZ),
                new Vector3(0.42f, 4.25f, 0.35f),
                LeatherColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Stage Right Curtain",
                room,
                new Vector3(bounds.xMax - 0.16f, 2.55f, curtainZ),
                new Vector3(0.42f, 4.25f, 0.35f),
                LeatherColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Stage Valance",
                room,
                new Vector3(centerX, 4.34f, curtainZ),
                new Vector3(bounds.width + 0.12f, 0.62f, 0.35f),
                LeatherColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Stage Left Speaker",
                room,
                new Vector3(
                    bounds.xMin + 0.55f,
                    0.92f,
                    centerZ + 0.42f),
                new Vector3(0.72f, 1.45f, 0.62f),
                new Color(0.035f, 0.035f, 0.04f));
            RuntimePrimitiveFactory.CreateBox(
                "Stage Right Speaker",
                room,
                new Vector3(
                    bounds.xMax - 0.55f,
                    0.92f,
                    centerZ + 0.42f),
                new Vector3(0.72f, 1.45f, 0.62f),
                new Color(0.035f, 0.035f, 0.04f));
            RuntimePrimitiveFactory.CreateCylinder(
                "Stage Microphone Stand",
                room,
                new Vector3(centerX, 0.95f, centerZ - 0.55f),
                new Vector3(0.055f, 0.80f, 0.055f),
                BrassColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Stage Microphone",
                room,
                new Vector3(centerX, 1.78f, centerZ - 0.60f),
                new Vector3(0.12f, 0.22f, 0.12f),
                new Color(0.06f, 0.06f, 0.065f),
                false);
        }

        private static void BuildActivityBay(Transform room)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Activity Bay Rug",
                room,
                new Vector3(7.00f, 0.018f, 0.55f),
                new Vector3(6.15f, 0.035f, 5.65f),
                new Color(0.12f, 0.11f, 0.20f),
                false);

            var border = new List<Bounds>
            {
                new Bounds(
                    new Vector3(7f, 0.045f, -2.28f),
                    new Vector3(6.22f, 0.06f, 0.08f)),
                new Bounds(
                    new Vector3(7f, 0.045f, 3.38f),
                    new Vector3(6.22f, 0.06f, 0.08f)),
                new Bounds(
                    new Vector3(3.92f, 0.045f, 0.55f),
                    new Vector3(0.08f, 0.06f, 5.58f)),
                new Bounds(
                    new Vector3(10.08f, 0.045f, 0.55f),
                    new Vector3(0.08f, 0.06f, 5.58f))
            };
            SetNoShadows(RuntimePrimitiveFactory.CreateCombinedBoxes(
                "Activity Bay Border",
                room,
                border,
                BrassColor));
        }

        private static void BuildSocialTables(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            int tableIndex = 0;
            for (int index = 0;
                 index < plan.FurnitureFootprints.Count;
                 index++)
            {
                BarInteriorFurnitureFootprint footprint =
                    plan.FurnitureFootprints[index];
                if (footprint.Kind !=
                    BarInteriorFurnitureKind.HighTopTable)
                {
                    continue;
                }

                tableIndex++;
                BuildHighTable(
                    room,
                    $"Social High Table {tableIndex}",
                    new Vector3(
                        footprint.Bounds.center.x,
                        0f,
                        footprint.Bounds.center.y));
            }
        }

        private static void BuildEntranceDress(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            RuntimePrimitiveFactory.CreateBox(
                "Entrance Rug",
                room,
                new Vector3(0f, 0.016f, -6.45f),
                new Vector3(3.65f, 0.03f, 2.25f),
                new Color(0.21f, 0.03f, 0.045f),
                false);
            BarInteriorFurnitureFootprint coatRack =
                RequireFurniture(
                    plan,
                    BarInteriorFurnitureKind.CoatRack);
            Vector3 coatRackPosition = new Vector3(
                coatRack.Bounds.center.x,
                0f,
                coatRack.Bounds.center.y);
            RuntimePrimitiveFactory.CreateCylinder(
                "Coat Rack",
                room,
                coatRackPosition + Vector3.up * 0.92f,
                new Vector3(0.12f, 0.92f, 0.12f),
                BrassColor);
            for (int index = 0; index < 4; index++)
            {
                GameObject hook = RuntimePrimitiveFactory.CreateBox(
                    $"Coat Rack Hook {index + 1}",
                    room,
                    coatRackPosition + Vector3.up * 1.70f,
                    new Vector3(0.52f, 0.08f, 0.08f),
                    BrassColor,
                    false);
                hook.transform.localRotation =
                    Quaternion.Euler(0f, index * 45f, 18f);
            }

            RuntimePrimitiveFactory.CreateBox(
                "Service Door",
                room,
                new Vector3(9.65f, 1.25f, 7.76f),
                new Vector3(1.65f, 2.50f, 0.12f),
                TealGlassColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Service Door Frame",
                room,
                new Vector3(9.65f, 2.57f, 7.70f),
                new Vector3(1.92f, 0.14f, 0.20f),
                BrassColor,
                false);
        }

        private static void BuildWallDress(Transform room)
        {
            BuildPoster(
                room,
                "Burgundy Poster",
                new Vector3(10.78f, 2.55f, -4.30f),
                new Color(0.56f, 0.08f, 0.10f));
            BuildPoster(
                room,
                "Teal Poster",
                new Vector3(10.78f, 2.55f, 4.25f),
                new Color(0.08f, 0.38f, 0.42f));
            BuildPoster(
                room,
                "Entrance Notice",
                new Vector3(-10.78f, 2.45f, -6.20f),
                new Color(0.62f, 0.38f, 0.12f));
        }

        private static void BuildCeilingFan(Transform room)
        {
            GameObject fan = new GameObject("Slow Ceiling Fan");
            fan.transform.SetParent(room, false);
            fan.transform.localPosition = new Vector3(0f, 4.35f, 0.75f);
            fan.AddComponent<BarCeilingFan>();

            RuntimePrimitiveFactory.CreateCylinder(
                "Fan Hub",
                fan.transform,
                Vector3.zero,
                new Vector3(0.28f, 0.10f, 0.28f),
                BrassColor,
                false);
            for (int index = 0; index < 4; index++)
            {
                GameObject blade = RuntimePrimitiveFactory.CreateBox(
                    $"Fan Blade {index + 1}",
                    fan.transform,
                    new Vector3(1.10f, -0.05f, 0f),
                    new Vector3(1.75f, 0.08f, 0.34f),
                    DarkWoodColor,
                    false);
                blade.transform.localRotation =
                    Quaternion.Euler(0f, index * 90f, 0f);
                blade.transform.localPosition =
                    blade.transform.localRotation * new Vector3(1.10f, -0.05f, 0f);
            }
        }

        private static void BuildActivityDress(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            switch (plan.Activity)
            {
                case BarActivityKind.BeerPong:
                    BuildBeerPongTable(room, plan);
                    break;
                case BarActivityKind.SplitTheG:
                    BuildSplitTheGDisplay(room, plan);
                    break;
                case BarActivityKind.TinctureMatch:
                    BuildTinctureMatchDisplay(room, plan);
                    break;
                default:
                    BuildCocktailDisplay(room, plan);
                    break;
            }
        }

        private static void BuildBeerPongTable(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            BarInteriorFurnitureFootprint footprint =
                RequireFurniture(
                    plan,
                    BarInteriorFurnitureKind.ActivityFixture);
            Vector3 center = new Vector3(
                footprint.Bounds.center.x,
                0.92f,
                footprint.Bounds.center.y);
            RuntimePrimitiveFactory.CreateBox(
                "Beer Pong Table",
                room,
                center,
                new Vector3(
                    footprint.Bounds.width,
                    0.14f,
                    footprint.Bounds.height),
                new Color(0.055f, 0.26f, 0.29f));

            float legX =
                footprint.Bounds.width * 0.5f - 0.275f;
            float legZ =
                footprint.Bounds.height * 0.5f - 0.445f;
            Vector3[] legs =
            {
                center + new Vector3(-legX, -0.49f, -legZ),
                center + new Vector3(legX, -0.49f, -legZ),
                center + new Vector3(-legX, -0.49f, legZ),
                center + new Vector3(legX, -0.49f, legZ)
            };
            for (int index = 0; index < legs.Length; index++)
            {
                RuntimePrimitiveFactory.CreateBox(
                    $"Beer Pong Table Leg {index + 1}",
                    room,
                    legs[index],
                    new Vector3(0.16f, 0.86f, 0.16f),
                    DarkWoodColor);
            }

            RuntimePrimitiveFactory.CreateBox(
                "Beer Pong Center Line",
                room,
                center + Vector3.up * 0.08f,
                new Vector3(
                    footprint.Bounds.width - 0.25f,
                    0.025f,
                    0.06f),
                BrassColor,
                false);

            Color cupColor = new Color(0.82f, 0.12f, 0.10f);
            Vector2[] cupOffsets =
            {
                new Vector2(0f, 1.02f),
                new Vector2(-0.27f, 1.32f),
                new Vector2(0.27f, 1.32f),
                new Vector2(-0.54f, 1.62f),
                new Vector2(0f, 1.62f),
                new Vector2(0.54f, 1.62f)
            };
            for (int index = 0; index < cupOffsets.Length; index++)
            {
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Beer Pong Cup {index + 1}",
                    room,
                    center + new Vector3(
                        cupOffsets[index].x,
                        0.23f,
                        cupOffsets[index].y),
                    new Vector3(0.22f, 0.16f, 0.22f),
                    cupColor,
                    false);
            }
        }

        private static void BuildCocktailDisplay(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            Vector3 basePosition = BuildActivityConsole(
                room,
                plan,
                "Cocktail Service Cart",
                new Color(0.10f, 0.24f, 0.22f));
            RuntimePrimitiveFactory.CreateCylinder(
                "Cocktail Shaker",
                room,
                basePosition,
                new Vector3(0.20f, 0.31f, 0.20f),
                new Color(0.64f, 0.68f, 0.66f),
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Cocktail Glass",
                room,
                basePosition + new Vector3(0.55f, -0.04f, 0f),
                new Vector3(0.24f, 0.25f, 0.24f),
                new Color(0.24f, 0.58f, 0.62f),
                false);
        }

        private static void BuildSplitTheGDisplay(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            Vector3 display = BuildActivityConsole(
                room,
                plan,
                "Split the G Tap Cart",
                new Color(0.10f, 0.18f, 0.13f));
            RuntimePrimitiveFactory.CreateCylinder(
                "Split the G Coaster",
                room,
                display,
                new Vector3(0.38f, 0.035f, 0.38f),
                DarkWoodColor,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Split the G Pint",
                room,
                display + Vector3.up * 0.30f,
                new Vector3(0.25f, 0.30f, 0.25f),
                new Color(0.36f, 0.16f, 0.055f),
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Split the G Foam",
                room,
                display + Vector3.up * 0.61f,
                new Vector3(0.26f, 0.045f, 0.26f),
                new Color(0.94f, 0.83f, 0.61f),
                false);
            RuntimePrimitiveFactory.CreateBox(
                "Split the G Target",
                room,
                display + new Vector3(0f, 0.32f, -0.26f),
                new Vector3(0.31f, 0.045f, 0.025f),
                BrassColor,
                false);
        }

        private static void BuildTinctureMatchDisplay(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            Vector3 tray = BuildActivityConsole(
                room,
                plan,
                "Tincture Apothecary Cart",
                new Color(0.16f, 0.08f, 0.22f));
            RuntimePrimitiveFactory.CreateBox(
                "Tincture Match Tray",
                room,
                tray,
                new Vector3(2.15f, 0.08f, 0.62f),
                DarkWoodColor,
                false);

            Color[] colors =
            {
                new Color(0.66f, 0.08f, 0.10f),
                new Color(0.94f, 0.44f, 0.08f),
                new Color(0.20f, 0.12f, 0.48f),
                new Color(0.13f, 0.48f, 0.24f),
                new Color(0.74f, 0.57f, 0.20f)
            };
            for (int index = 0; index < colors.Length; index++)
            {
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Tincture Shot {index + 1}",
                    room,
                    tray + new Vector3(-0.76f + index * 0.38f, 0.18f, 0f),
                    new Vector3(0.22f, 0.16f, 0.22f),
                    colors[index],
                    false);
            }

            Vector3 bottle = tray + new Vector3(1.55f, 0.29f, 0f);
            RuntimePrimitiveFactory.CreateCylinder(
                "Tincture XXX Bottle",
                room,
                bottle,
                new Vector3(0.34f, 0.34f, 0.34f),
                new Color(0.70f, 0.82f, 0.78f),
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                "Tincture XXX Bottle Neck",
                room,
                bottle + Vector3.up * 0.42f,
                new Vector3(0.16f, 0.12f, 0.16f),
                new Color(0.70f, 0.82f, 0.78f),
                false);

            Vector3 sign = bottle + new Vector3(0f, 0.02f, -0.22f);
            RuntimePrimitiveFactory.CreateBox(
                "Tincture XXX Sign",
                room,
                sign,
                new Vector3(0.74f, 0.38f, 0.035f),
                BrassColor,
                false);
            Color ink = new Color(0.16f, 0.08f, 0.04f);
            for (int xIndex = 0; xIndex < 3; xIndex++)
            {
                float x = sign.x - 0.22f + xIndex * 0.22f;
                for (int stroke = 0; stroke < 2; stroke++)
                {
                    GameObject mark = RuntimePrimitiveFactory.CreateBox(
                        $"Tincture XXX Mark {xIndex + 1}-{stroke + 1}",
                        room,
                        new Vector3(x, sign.y, sign.z - 0.03f),
                        new Vector3(0.055f, 0.29f, 0.025f),
                        ink,
                        false);
                    mark.transform.localRotation = Quaternion.Euler(
                        0f,
                        0f,
                        stroke == 0 ? 38f : -38f);
                }
            }
        }

        private static Vector3 BuildActivityConsole(
            Transform room,
            BarInteriorLayoutPlan plan,
            string name,
            Color accent)
        {
            BarInteriorFurnitureFootprint footprint =
                RequireFurniture(
                    plan,
                    BarInteriorFurnitureKind.ActivityFixture);
            Vector3 center = new Vector3(
                footprint.Bounds.center.x,
                0.64f,
                footprint.Bounds.center.y);
            RuntimePrimitiveFactory.CreateBox(
                name,
                room,
                center,
                new Vector3(
                    footprint.Bounds.width,
                    1.20f,
                    footprint.Bounds.height),
                DarkWoodColor);
            RuntimePrimitiveFactory.CreateBox(
                name + " Top",
                room,
                center + Vector3.up * 0.66f,
                new Vector3(2.85f, 0.12f, 1.22f),
                BrassColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                name + " Accent",
                room,
                center + new Vector3(0f, 0f, -0.56f),
                new Vector3(2.20f, 0.52f, 0.06f),
                accent,
                CityNightResources.EmissiveMaterial,
                false);
            return center + Vector3.up * 0.78f;
        }

        private static void BuildPracticalFixtures(
            Transform room,
            BarInteriorLayoutPlan plan)
        {
            for (int index = 0; index < plan.LightAnchors.Count; index++)
            {
                BarInteriorLightAnchor anchor =
                    plan.LightAnchors[index];
                float cableHeight =
                    Mathf.Max(0.2f, plan.RoomHeight - anchor.Position.y);
                RuntimePrimitiveFactory.CreateBox(
                    $"Practical Cable {index + 1}",
                    room,
                    new Vector3(
                        anchor.Position.x,
                        anchor.Position.y + cableHeight * 0.5f,
                        anchor.Position.z),
                    new Vector3(0.035f, cableHeight, 0.035f),
                    DarkWoodColor,
                    false);
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Practical Shade {index + 1}",
                    room,
                    anchor.Position + Vector3.up * 0.10f,
                    new Vector3(0.58f, 0.14f, 0.58f),
                    index % 2 == 0 ? BrassColor : DarkWoodColor,
                    false);
                RuntimePrimitiveFactory.CreateCylinder(
                    $"Practical Bulb {index + 1}",
                    room,
                    anchor.Position - Vector3.up * 0.10f,
                    new Vector3(0.19f, 0.18f, 0.19f),
                    anchor.Color * 2.2f,
                    CityNightResources.EmissiveMaterial,
                    false);
            }
        }

        private static void BuildStool(
            Transform room,
            string name,
            Vector3 position)
        {
            RuntimePrimitiveFactory.CreateCylinder(
                name + " Leg",
                room,
                position + Vector3.up * 0.42f,
                new Vector3(0.12f, 0.42f, 0.12f),
                DarkWoodColor,
                false);
            RuntimePrimitiveFactory.CreateCylinder(
                name,
                room,
                position + Vector3.up * 0.87f,
                new Vector3(0.48f, 0.09f, 0.48f),
                LeatherColor,
                false);
        }

        private static void BuildHighTable(
            Transform room,
            string name,
            Vector3 position)
        {
            RuntimePrimitiveFactory.CreateCylinder(
                name + " Leg",
                room,
                position + Vector3.up * 0.47f,
                new Vector3(0.17f, 0.47f, 0.17f),
                DarkWoodColor);
            RuntimePrimitiveFactory.CreateCylinder(
                name,
                room,
                position + Vector3.up * 0.98f,
                new Vector3(0.90f, 0.08f, 0.90f),
                BrassColor);
        }

        private static void BuildPoster(
            Transform room,
            string name,
            Vector3 position,
            Color posterColor)
        {
            RuntimePrimitiveFactory.CreateBox(
                name + " Frame",
                room,
                position,
                new Vector3(0.08f, 1.72f, 1.20f),
                BrassColor,
                false);
            RuntimePrimitiveFactory.CreateBox(
                name,
                room,
                position + Vector3.left * 0.055f,
                new Vector3(0.06f, 1.50f, 0.98f),
                posterColor,
                false);
        }

        private static BarInteriorFurnitureFootprint RequireFurniture(
            BarInteriorLayoutPlan plan,
            BarInteriorFurnitureKind kind)
        {
            if (plan.TryGetFurniture(kind, out BarInteriorFurnitureFootprint
                    footprint))
            {
                return footprint;
            }

            throw new InvalidOperationException(
                $"The bar layout is missing furniture kind '{kind}'.");
        }

        private static GameObject SetNoShadows(GameObject gameObject)
        {
            if (gameObject == null)
            {
                return null;
            }

            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer != null)
            {
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }

            return gameObject;
        }
    }

    [DisallowMultipleComponent]
    public sealed class BarCeilingFan : MonoBehaviour
    {
        [SerializeField] private float degreesPerSecond = 14f;

        private void Update()
        {
            transform.Rotate(
                Vector3.up,
                degreesPerSecond * Time.deltaTime,
                Space.Self);
        }
    }
}
