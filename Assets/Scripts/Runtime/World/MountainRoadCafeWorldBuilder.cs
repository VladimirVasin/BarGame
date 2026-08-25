using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using static BarPromenade.MountainRoadCafeGeometry;

namespace BarPromenade
{
    /// <summary>
    /// A same-scene, physically enterable mountain cafe tableau inspired by
    /// Nighthawks. Its glass, counter and furniture are solid, while the
    /// plateau remains the authoritative floor collider through the open
    /// 1.6 metre side entrance.
    /// </summary>
    public static class MountainRoadCafeWorldBuilder
    {
        public const string EntranceAnchorId =
            "terminal-cafe-entrance";
        public const string CounterAnchorId =
            "terminal-cafe-counter";
        public const string GlassAnchorId =
            "terminal-cafe-glass";
        public const string LonePatronAnchorId =
            "terminal-cafe-npc-lone";
        public const string PairFirstAnchorId =
            "terminal-cafe-npc-pair-a";
        public const string PairSecondAnchorId =
            "terminal-cafe-npc-pair-b";
        public const string AttendantAnchorId =
            "terminal-cafe-npc-attendant";

        public const int MaximumRealtimeLights = 2;
        public const int TableauNpcCount = 4;

        private const float WallThickness = 0.24f;
        private const float PlinthHeight = 0.62f;
        private const float FasciaHeight = 0.62f;
        private const float GlassThickness = 0.028f;
        private const float FrameWidth = 0.10f;
        private const float DoorHeight = 2.28f;
        private const float FloorVisualOffset = 0.036f;
        private const float RoofThickness = 0.22f;

        private static readonly Color Facade =
            new Color(0.035f, 0.075f, 0.068f, 1f);
        private static readonly Color FacadeTrim =
            new Color(0.045f, 0.125f, 0.105f, 1f);
        private static readonly Color Brick =
            new Color(0.29f, 0.105f, 0.065f, 1f);
        private static readonly Color InteriorCream =
            new Color(0.72f, 0.62f, 0.37f, 1f);
        private static readonly Color FloorLinoleum =
            new Color(0.16f, 0.32f, 0.27f, 1f);
        private static readonly Color Glass =
            new Color(0.25f, 0.54f, 0.47f, 0.28f);
        private static readonly Color CounterWood =
            new Color(0.34f, 0.105f, 0.045f, 1f);
        private static readonly Color CounterTop =
            new Color(0.46f, 0.16f, 0.060f, 1f);
        private static readonly Color StoolMetal =
            new Color(0.14f, 0.16f, 0.145f, 1f);
        private static readonly Color StoolSeat =
            new Color(0.41f, 0.12f, 0.055f, 1f);
        private static readonly Color Appliance =
            new Color(0.51f, 0.53f, 0.43f, 1f);
        private static readonly Color ApplianceDark =
            new Color(0.20f, 0.235f, 0.215f, 1f);
        private static readonly Color WarmGlow =
            new Color(3.20f, 2.15f, 0.72f, 1f);
        private static readonly Color ColdGlow =
            new Color(0.72f, 1.60f, 1.42f, 1f);

        public static MountainRoadCafeWorldResult Build(
            Transform parent,
            MountainRoadCafePlan plan)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            ValidatePlan(plan);

            var root = new GameObject("Nighthawks Mountain Cafe");
            root.transform.SetParent(parent, false);
            var physicalRoot = new GameObject("Physical Cafe");
            physicalRoot.transform.SetParent(root.transform, false);
            var dressingRoot = new GameObject("Cafe Dressing");
            dressingRoot.transform.SetParent(root.transform, false);
            var npcRoot = new GameObject("Silent Cafe Tableau");
            npcRoot.transform.SetParent(root.transform, false);
            var lightingRoot = new GameObject("Always-On Cafe Lighting");
            lightingRoot.transform.SetParent(root.transform, false);

            var semanticAnchors = new Dictionary<string, Transform>(
                StringComparer.Ordinal)
            {
                { plan.StableId, root.transform }
            };
            BuildShell(
                plan,
                physicalRoot.transform,
                dressingRoot.transform,
                semanticAnchors);
            BuildInterior(
                plan,
                physicalRoot.transform,
                dressingRoot.transform,
                semanticAnchors);
            BuildTableau(
                plan,
                npcRoot.transform,
                semanticAnchors);
            List<Light> lights = BuildLights(
                plan,
                lightingRoot.transform);
            MountainRoadCafeSoundscape soundscape =
                MountainRoadCafeSoundscape.Create(
                    root.transform,
                    semanticAnchors,
                    StableSeed(plan.StableId));

            return new MountainRoadCafeWorldResult(
                plan,
                root,
                physicalRoot,
                dressingRoot,
                npcRoot,
                lightingRoot,
                soundscape,
                lights,
                semanticAnchors);
        }

        private static void ValidatePlan(MountainRoadCafePlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (string.IsNullOrWhiteSpace(plan.StableId) ||
                plan.FootprintXZ == null ||
                plan.FootprintXZ.Count != 5 ||
                plan.Height < 4f ||
                plan.DoorWidth < 1.59f)
            {
                throw new ArgumentException(
                    "The mountain cafe requires its stable five-sided, " +
                    "4 metre high, 1.6 metre entrance plan.",
                    nameof(plan));
            }
        }

        private static void BuildShell(
            MountainRoadCafePlan plan,
            Transform physicalRoot,
            Transform dressingRoot,
            IDictionary<string, Transform> semanticAnchors)
        {
            Vector3[] corners = GetFootprint(plan);
            Vector3 southStart = corners[0];
            Vector3 southEnd = corners[1];
            Vector3 southDirection =
                (southEnd - southStart).normalized;
            Vector3 doorCenter = new Vector3(
                plan.DoorCenter.x,
                plan.FloorY,
                plan.DoorCenter.z);
            Vector3 doorStart =
                doorCenter - southDirection * (plan.DoorWidth * 0.5f);
            Vector3 doorEnd =
                doorCenter + southDirection * (plan.DoorWidth * 0.5f);

            var facadeRootObject = new GameObject(
                "Five-Sided Cafe Shell");
            facadeRootObject.transform.SetParent(physicalRoot, false);
            Transform facadeRoot = facadeRootObject.transform;

            CreateOpaqueSegment(
                "South Entrance Pier",
                facadeRoot,
                southStart,
                doorStart,
                plan,
                Facade);
            CreateGlazedSegment(
                "South Window Wall",
                facadeRoot,
                doorEnd,
                southEnd,
                plan);
            CreateGlazedSegment(
                "Chamfered Corner Window",
                facadeRoot,
                corners[1],
                corners[2],
                plan);
            CreateGlazedSegment(
                "East Window Wall",
                facadeRoot,
                corners[2],
                corners[3],
                plan);
            CreateOpaqueSegment(
                "North Service Wall",
                facadeRoot,
                corners[3],
                corners[4],
                plan,
                Brick);
            CreateOpaqueSegment(
                "West Blind Wall",
                facadeRoot,
                corners[4],
                corners[0],
                plan,
                Facade);

            GameObject glassAnchor = new GameObject("Glazed Facade Anchor");
            glassAnchor.transform.SetParent(facadeRoot, false);
            glassAnchor.transform.position = corners[2] +
                Vector3.up * (PlinthHeight +
                              (plan.Height - PlinthHeight -
                               FasciaHeight) * 0.5f);
            semanticAnchors.Add(GlassAnchorId, glassAnchor.transform);

            BuildDoor(
                plan,
                dressingRoot,
                doorStart,
                doorEnd,
                southDirection,
                semanticAnchors);
            BuildRoof(plan, physicalRoot);
            BuildInteriorWallLining(plan, dressingRoot);
        }

        private static void CreateOpaqueSegment(
            string name,
            Transform parent,
            Vector3 first,
            Vector3 second,
            MountainRoadCafePlan plan,
            Color color)
        {
            CreateSegmentBox(
                name,
                parent,
                first,
                second,
                plan.FloorY + plan.Height * 0.5f,
                plan.Height,
                WallThickness,
                color,
                null,
                true);
        }

        private static void CreateGlazedSegment(
            string name,
            Transform parent,
            Vector3 first,
            Vector3 second,
            MountainRoadCafePlan plan)
        {
            float glassHeight =
                plan.Height - PlinthHeight - FasciaHeight;
            CreateSegmentBox(
                name + " Plinth",
                parent,
                first,
                second,
                plan.FloorY + PlinthHeight * 0.5f,
                PlinthHeight,
                WallThickness,
                Facade,
                null,
                true);
            CreateSegmentBox(
                name + " Glass",
                parent,
                first,
                second,
                plan.FloorY + PlinthHeight + glassHeight * 0.5f,
                glassHeight,
                GlassThickness,
                Glass,
                HomeBalconyResources.GlassMaterial,
                true);
            CreateSegmentBox(
                name + " Fascia",
                parent,
                first,
                second,
                plan.FloorY + plan.Height - FasciaHeight * 0.5f,
                FasciaHeight,
                WallThickness,
                Facade,
                null,
                true);

            float length = Vector3.Distance(first, second);
            int divisions = Mathf.Max(1, Mathf.CeilToInt(length / 2.25f));
            Vector3 direction = (second - first).normalized;
            for (int index = 0; index <= divisions; index++)
            {
                Vector3 point = Vector3.Lerp(
                    first,
                    second,
                    index / (float)divisions);
                CreateSegmentBox(
                    name + $" Mullion {index:00}",
                    parent,
                    point - direction * (FrameWidth * 0.5f),
                    point + direction * (FrameWidth * 0.5f),
                    plan.FloorY + PlinthHeight + glassHeight * 0.5f,
                    glassHeight,
                    WallThickness + 0.025f,
                    FacadeTrim,
                    null,
                    true);
            }
        }

        private static void BuildDoor(
            MountainRoadCafePlan plan,
            Transform parent,
            Vector3 doorStart,
            Vector3 doorEnd,
            Vector3 closedDirection,
            IDictionary<string, Transform> semanticAnchors)
        {
            CreateSegmentBox(
                "Entrance Header",
                parent,
                doorStart,
                doorEnd,
                plan.FloorY + DoorHeight +
                (plan.Height - DoorHeight) * 0.5f,
                plan.Height - DoorHeight,
                WallThickness,
                Facade,
                null,
                true);

            CreateDoorJamb(
                "Entrance West Jamb",
                parent,
                doorStart - closedDirection * (FrameWidth * 0.5f),
                plan);
            CreateDoorJamb(
                "Entrance East Jamb",
                parent,
                doorEnd + closedDirection * (FrameWidth * 0.5f),
                plan);

            Vector3 openDirection =
                Quaternion.AngleAxis(-68f, Vector3.up) * closedDirection;
            GameObject leaf = CreateSegmentBox(
                "Open Glass Door",
                parent,
                doorStart,
                doorStart + openDirection * (plan.DoorWidth - 0.12f),
                plan.FloorY + DoorHeight * 0.5f,
                DoorHeight,
                GlassThickness,
                Glass,
                HomeBalconyResources.GlassMaterial,
                false);
            leaf.name = "Open Glass Door - Non Blocking";
            RuntimePrimitiveFactory.CreateCylinder(
                "Open Door Brass Handle",
                leaf.transform,
                new Vector3(
                    (plan.DoorWidth - 0.12f) * 0.40f,
                    0f,
                    -0.045f),
                new Vector3(0.045f, 0.12f, 0.045f),
                new Color(0.55f, 0.38f, 0.12f, 1f),
                false).transform.localRotation =
                    Quaternion.Euler(0f, 0f, 90f);

            var entrance = new GameObject("Open Entrance Anchor");
            entrance.transform.SetParent(parent, false);
            entrance.transform.position = new Vector3(
                plan.DoorCenter.x,
                plan.FloorY + 0.04f,
                plan.DoorCenter.z);
            entrance.transform.rotation = Quaternion.LookRotation(
                plan.DoorForward,
                Vector3.up);
            semanticAnchors.Add(EntranceAnchorId, entrance.transform);
        }

        private static void CreateDoorJamb(
            string name,
            Transform parent,
            Vector3 position,
            MountainRoadCafePlan plan)
        {
            RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                position + Vector3.up * (DoorHeight * 0.5f),
                new Vector3(FrameWidth, DoorHeight, WallThickness + 0.03f),
                FacadeTrim,
                true).transform.rotation = FrameRotation(plan);
        }

        private static void BuildRoof(
            MountainRoadCafePlan plan,
            Transform parent)
        {
            Vector2[] expanded = ScaleFootprint(plan, 1.012f);
            CreatePrism(
                "Slightly Overhanging Pentagonal Roof",
                parent,
                expanded,
                plan.FloorY + plan.Height,
                plan.FloorY + plan.Height + RoofThickness,
                Facade,
                true);
        }

        private static void BuildInteriorWallLining(
            MountainRoadCafePlan plan,
            Transform parent)
        {
            LocalBounds bounds = CalculateLocalBounds(plan);
            Quaternion rotation = FrameRotation(plan);
            GameObject north = RuntimePrimitiveFactory.CreateBox(
                "Warm Rear Service Wall",
                parent,
                Local(
                    plan,
                    (bounds.MinimumRight + bounds.MaximumRight) * 0.5f,
                    2.10f,
                    bounds.MaximumForward - 0.14f),
                new Vector3(
                    bounds.Width - 0.72f,
                    3.18f,
                    0.035f),
                InteriorCream,
                false);
            north.transform.rotation = rotation;

            GameObject west = RuntimePrimitiveFactory.CreateBox(
                "Rust Interior Side Wall",
                parent,
                Local(
                    plan,
                    bounds.MinimumRight + 0.14f,
                    2.12f,
                    (bounds.MinimumForward + bounds.MaximumForward) * 0.5f),
                new Vector3(
                    0.035f,
                    3.12f,
                    bounds.Depth - 0.72f),
                Brick,
                false);
            west.transform.rotation = rotation;
        }

        private static void BuildInterior(
            MountainRoadCafePlan plan,
            Transform physicalRoot,
            Transform dressingRoot,
            IDictionary<string, Transform> semanticAnchors)
        {
            CreatePolygonSurface(
                "Inset Green Linoleum",
                dressingRoot,
                ScaleFootprint(plan, 0.978f),
                plan.FloorY + FloorVisualOffset,
                FloorLinoleum);

            Transform counter = BuildCounter(plan, physicalRoot);
            semanticAnchors.Add(CounterAnchorId, counter);
            BuildStools(plan, physicalRoot);
            BuildServiceCounter(plan, physicalRoot);
            BuildRefrigerator(plan, physicalRoot, semanticAnchors);
            BuildCoffeeBoilers(plan, physicalRoot, semanticAnchors);
            BuildCeilingFixtures(plan, dressingRoot, semanticAnchors);
            BuildTableDetails(plan, dressingRoot);
        }

        private static Transform BuildCounter(
            MountainRoadCafePlan plan,
            Transform parent)
        {
            var root = new GameObject("Faceted Mahogany Counter");
            root.transform.SetParent(parent, false);
            Quaternion rotation = FrameRotation(plan);
            GameObject baseObject = RuntimePrimitiveFactory.CreateBox(
                "Long Counter Base",
                root.transform,
                Local(plan, 0.62f, 0.45f, -1.15f),
                new Vector3(6.10f, 0.90f, 0.82f),
                CounterWood,
                true);
            baseObject.transform.rotation = rotation;
            GameObject top = RuntimePrimitiveFactory.CreateBox(
                "Long Counter Top",
                root.transform,
                Local(plan, 0.62f, 0.96f, -1.15f),
                new Vector3(6.36f, 0.12f, 1.02f),
                CounterTop,
                true);
            top.transform.rotation = rotation;

            Vector3 returnDirection =
                (plan.Right * 0.62f + plan.Forward * 0.78f).normalized;
            Vector3 returnStart = Local(plan, 3.55f, 0f, -0.95f);
            Vector3 returnEnd = returnStart + returnDirection * 1.62f;
            CreateSegmentBox(
                "Angled Counter Return",
                root.transform,
                returnStart,
                returnEnd,
                plan.FloorY + 0.45f,
                0.90f,
                0.82f,
                CounterWood,
                null,
                true);
            CreateSegmentBox(
                "Angled Counter Return Top",
                root.transform,
                returnStart - returnDirection * 0.08f,
                returnEnd + returnDirection * 0.08f,
                plan.FloorY + 0.96f,
                0.12f,
                1.02f,
                CounterTop,
                null,
                true);
            return root.transform;
        }

        private static void BuildStools(
            MountainRoadCafePlan plan,
            Transform parent)
        {
            // Two unoccupied seats are structural negative space: one
            // separates the solitary patron from the couple, the other
            // finishes the row at the bend without becoming another spawn.
            float[] rightOffsets =
            {
                -1.50f,
                -0.38f,
                0.75f,
                1.80f,
                3.00f
            };
            for (int index = 0; index < rightOffsets.Length; index++)
            {
                Transform stool = new GameObject(
                    $"Counter Stool {index + 1:00}").transform;
                stool.SetParent(parent, false);
                Vector3 center = Local(
                    plan,
                    rightOffsets[index],
                    0f,
                    -2.18f);
                RuntimePrimitiveFactory.CreateCylinder(
                    "Metal Pedestal",
                    stool,
                    center + Vector3.up * 0.215f,
                    new Vector3(0.10f, 0.215f, 0.10f),
                    StoolMetal,
                    true);
                RuntimePrimitiveFactory.CreateCylinder(
                    "Round Red Seat",
                    stool,
                    center + Vector3.up * 0.44f,
                    new Vector3(0.48f, 0.055f, 0.48f),
                    StoolSeat,
                    true);
                RuntimePrimitiveFactory.CreateCylinder(
                    "Stool Foot",
                    stool,
                    center + Vector3.up * 0.035f,
                    new Vector3(0.34f, 0.035f, 0.34f),
                    StoolMetal,
                    true);
            }
        }

        private static void BuildServiceCounter(
            MountainRoadCafePlan plan,
            Transform parent)
        {
            Quaternion rotation = FrameRotation(plan);
            GameObject cabinet = RuntimePrimitiveFactory.CreateBox(
                "Rear Service Cabinet",
                parent,
                Local(plan, 2.15f, 0.43f, 3.90f),
                new Vector3(3.65f, 0.86f, 0.78f),
                ApplianceDark,
                true);
            cabinet.transform.rotation = rotation;
            GameObject worktop = RuntimePrimitiveFactory.CreateBox(
                "Rear Service Worktop",
                parent,
                Local(plan, 2.15f, 0.90f, 3.90f),
                new Vector3(3.82f, 0.10f, 0.90f),
                CounterTop,
                true);
            worktop.transform.rotation = rotation;
        }

        private static void BuildRefrigerator(
            MountainRoadCafePlan plan,
            Transform parent,
            IDictionary<string, Transform> semanticAnchors)
        {
            Quaternion rotation = FrameRotation(plan);
            Vector3 center = Local(plan, -3.82f, 0.98f, 4.72f);
            GameObject body = RuntimePrimitiveFactory.CreateBox(
                "Visible Cafe Refrigerator",
                parent,
                center,
                new Vector3(1.12f, 1.96f, 0.72f),
                Appliance,
                true);
            body.transform.rotation = rotation;
            GameObject door = RuntimePrimitiveFactory.CreateBox(
                "Refrigerator Door Face",
                parent,
                center - plan.Forward * 0.378f + Vector3.up * 0.03f,
                new Vector3(1.01f, 1.80f, 0.035f),
                new Color(0.57f, 0.58f, 0.47f, 1f),
                false);
            door.transform.rotation = rotation;
            GameObject handle = RuntimePrimitiveFactory.CreateBox(
                "Refrigerator Handle",
                parent,
                center - plan.Forward * 0.410f +
                plan.Right * 0.39f + Vector3.up * 0.12f,
                new Vector3(0.055f, 0.82f, 0.055f),
                ApplianceDark,
                false);
            handle.transform.rotation = rotation;
            semanticAnchors.Add(
                MountainRoadCafeSoundscape.RefrigeratorAnchorId,
                body.transform);
        }

        private static void BuildCoffeeBoilers(
            MountainRoadCafePlan plan,
            Transform parent,
            IDictionary<string, Transform> semanticAnchors)
        {
            for (int index = 0; index < 2; index++)
            {
                Vector3 center = Local(
                    plan,
                    1.75f + index * 0.92f,
                    1.48f,
                    3.86f);
                GameObject body = RuntimePrimitiveFactory.CreateCylinder(
                    index == 0
                        ? "Audible Coffee Boiler"
                        : "Second Coffee Boiler",
                    parent,
                    center,
                    new Vector3(0.58f, 0.52f, 0.58f),
                    Appliance,
                    true);
                RuntimePrimitiveFactory.CreateCylinder(
                    "Boiler Lid",
                    parent,
                    center + Vector3.up * 0.57f,
                    new Vector3(0.64f, 0.045f, 0.64f),
                    ApplianceDark,
                    true);
                RuntimePrimitiveFactory.CreateBox(
                    "Boiler Sight Glass",
                    parent,
                    center - plan.Forward * 0.305f,
                    new Vector3(0.10f, 0.62f, 0.035f),
                    new Color(0.27f, 0.58f, 0.51f, 0.55f),
                    HomeBalconyResources.GlassMaterial,
                    false).transform.rotation = FrameRotation(plan);
                RuntimePrimitiveFactory.CreateBox(
                    "Boiler Tap",
                    parent,
                    center - plan.Forward * 0.37f -
                    Vector3.up * 0.27f,
                    new Vector3(0.12f, 0.10f, 0.24f),
                    ApplianceDark,
                    false).transform.rotation = FrameRotation(plan);
                if (index == 0)
                {
                    semanticAnchors.Add(
                        MountainRoadCafeSoundscape.BoilerAnchorId,
                        body.transform);
                }
            }
        }

        private static void BuildCeilingFixtures(
            MountainRoadCafePlan plan,
            Transform parent,
            IDictionary<string, Transform> semanticAnchors)
        {
            Quaternion rotation = FrameRotation(plan);
            Vector3 warmCenter = Local(plan, 0.35f, 3.88f, -0.18f);
            GameObject housing = RuntimePrimitiveFactory.CreateBox(
                "Visible Warm Fluorescent Housing",
                parent,
                warmCenter + Vector3.up * 0.055f,
                new Vector3(4.90f, 0.13f, 0.48f),
                ApplianceDark,
                false);
            housing.transform.rotation = rotation;
            GameObject tube = RuntimePrimitiveFactory.CreateBox(
                "Audible Sulphur Ceiling Tube",
                parent,
                warmCenter,
                new Vector3(4.45f, 0.065f, 0.28f),
                WarmGlow,
                CityNightResources.EmissiveMaterial,
                false);
            tube.transform.rotation = rotation;
            SetNoShadows(tube);
            semanticAnchors.Add(
                MountainRoadCafeSoundscape.FixtureAnchorId,
                tube.transform);

            Vector3 coldCenter = Local(plan, 2.18f, 3.83f, 3.18f);
            GameObject coldHousing = RuntimePrimitiveFactory.CreateBox(
                "Cold Service Strip Housing",
                parent,
                coldCenter + Vector3.up * 0.045f,
                new Vector3(2.80f, 0.12f, 0.36f),
                ApplianceDark,
                false);
            coldHousing.transform.rotation = rotation;
            GameObject coldTube = RuntimePrimitiveFactory.CreateBox(
                "Cold Service Strip",
                parent,
                coldCenter,
                new Vector3(2.52f, 0.055f, 0.20f),
                ColdGlow,
                CityNightResources.EmissiveMaterial,
                false);
            coldTube.transform.rotation = rotation;
            SetNoShadows(coldTube);
        }

        private static void BuildTableDetails(
            MountainRoadCafePlan plan,
            Transform parent)
        {
            // Only occupied places own cups. Empty stools remain visibly
            // intentional instead of reading as two more missing NPCs.
            float[] offsets = { -1.50f, 0.75f, 1.80f };
            for (int index = 0; index < offsets.Length; index++)
            {
                Vector3 place = Local(
                    plan,
                    offsets[index] + 0.16f,
                    1.09f,
                    -1.50f);
                RuntimePrimitiveFactory.CreateCylinder(
                    $"White Coffee Cup {index + 1:00}",
                    parent,
                    place,
                    new Vector3(0.14f, 0.075f, 0.14f),
                    new Color(0.70f, 0.68f, 0.55f, 1f),
                    false);
            }

            RuntimePrimitiveFactory.CreateBox(
                "Counter Napkin Holder",
                parent,
                Local(plan, 1.52f, 1.13f, -1.48f),
                new Vector3(0.18f, 0.20f, 0.12f),
                Appliance,
                false).transform.rotation = FrameRotation(plan);
        }

        private static List<Light> BuildLights(
            MountainRoadCafePlan plan,
            Transform parent)
        {
            var lights = new List<Light>(MaximumRealtimeLights)
            {
                CreateSpotLight(
                    "Sulphur Counter Light",
                    parent,
                    Local(plan, 0.35f, 3.80f, -0.18f),
                    (Vector3.down + plan.Forward * 0.05f).normalized,
                    new Color(1f, 0.72f, 0.32f),
                    10.5f,
                    8.2f,
                    108f,
                    68f,
                    true),
                CreateSpotLight(
                    "Cold Service Light",
                    parent,
                    Local(plan, 2.18f, 3.76f, 3.18f),
                    (Vector3.down - plan.Forward * 0.22f).normalized,
                    new Color(0.46f, 0.77f, 0.71f),
                    6.5f,
                    5.8f,
                    78f,
                    42f,
                    false)
            };
            return lights;
        }

        private static Light CreateSpotLight(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 direction,
            Color color,
            float intensity,
            float range,
            float spotAngle,
            float innerSpotAngle,
            bool shadows)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent, false);
            lightObject.transform.position = position;
            lightObject.transform.rotation = Quaternion.LookRotation(
                direction,
                Vector3.forward);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = spotAngle;
            light.innerSpotAngle = innerSpotAngle;
            light.shadows = shadows
                ? LightShadows.Hard
                : LightShadows.None;
            light.shadowStrength = shadows ? 0.66f : 0f;
            light.shadowBias = 0.04f;
            light.shadowNormalBias = 0.25f;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = shadows ? 0.08f : 0f;
            light.enabled = true;
            return light;
        }

        private static void BuildTableau(
            MountainRoadCafePlan plan,
            Transform parent,
            IDictionary<string, Transform> semanticAnchors)
        {
            MountainRoadCafeCastFactory.Create(
                parent,
                MountainRoadCafeCastPlan.Create(plan),
                semanticAnchors,
                StableSeed(plan.StableId));
        }

        private static Vector3[] GetFootprint(MountainRoadCafePlan plan)
        {
            var result = new Vector3[plan.FootprintXZ.Count];
            for (int index = 0; index < result.Length; index++)
            {
                Vector2 point = plan.FootprintXZ[index];
                result[index] = new Vector3(
                    point.x,
                    plan.FloorY,
                    point.y);
            }

            return result;
        }

        private static Vector2[] ScaleFootprint(
            MountainRoadCafePlan plan,
            float scale)
        {
            Vector2 center = new Vector2(plan.Center.x, plan.Center.z);
            var result = new Vector2[plan.FootprintXZ.Count];
            for (int index = 0; index < result.Length; index++)
            {
                result[index] = center +
                    (plan.FootprintXZ[index] - center) * scale;
            }

            return result;
        }

        private static LocalBounds CalculateLocalBounds(
            MountainRoadCafePlan plan)
        {
            float minimumRight = float.PositiveInfinity;
            float maximumRight = float.NegativeInfinity;
            float minimumForward = float.PositiveInfinity;
            float maximumForward = float.NegativeInfinity;
            for (int index = 0; index < plan.FootprintXZ.Count; index++)
            {
                Vector2 point = plan.FootprintXZ[index];
                Vector3 offset =
                    new Vector3(point.x, plan.FloorY, point.y) -
                    plan.Center;
                float right = Vector3.Dot(offset, plan.Right);
                float forward = Vector3.Dot(offset, plan.Forward);
                minimumRight = Mathf.Min(minimumRight, right);
                maximumRight = Mathf.Max(maximumRight, right);
                minimumForward = Mathf.Min(minimumForward, forward);
                maximumForward = Mathf.Max(maximumForward, forward);
            }

            return new LocalBounds(
                minimumRight,
                maximumRight,
                minimumForward,
                maximumForward);
        }

        private static Vector3 Local(
            MountainRoadCafePlan plan,
            float right,
            float up,
            float forward)
        {
            return plan.Center +
                   plan.Right * right +
                   Vector3.up * up +
                   plan.Forward * forward;
        }

        private static Quaternion FrameRotation(
            MountainRoadCafePlan plan)
        {
            return Quaternion.LookRotation(plan.Forward, Vector3.up);
        }

        private static void SetNoShadows(GameObject target)
        {
            Renderer renderer = target.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        private static int StableSeed(string stableId)
        {
            unchecked
            {
                int hash = 17;
                string id = stableId ?? string.Empty;
                for (int index = 0; index < id.Length; index++)
                {
                    hash = hash * 31 + id[index];
                }

                return hash;
            }
        }

        private readonly struct LocalBounds
        {
            public LocalBounds(
                float minimumRight,
                float maximumRight,
                float minimumForward,
                float maximumForward)
            {
                MinimumRight = minimumRight;
                MaximumRight = maximumRight;
                MinimumForward = minimumForward;
                MaximumForward = maximumForward;
            }

            public float MinimumRight { get; }
            public float MaximumRight { get; }
            public float MinimumForward { get; }
            public float MaximumForward { get; }
            public float Width => MaximumRight - MinimumRight;
            public float Depth => MaximumForward - MinimumForward;
        }

    }

}
