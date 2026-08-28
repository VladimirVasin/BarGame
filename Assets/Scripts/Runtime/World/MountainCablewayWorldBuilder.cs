using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using UnityEngine.Rendering;

namespace BarPromenade
{
    public sealed class MountainCablewayWorldResult
    {
        internal MountainCablewayWorldResult(
            GameObject root,
            GameObject stationRoot,
            MountainCablewayController controller,
            Light stationLight,
            Transform bullwheel,
            IReadOnlyList<Transform> supports,
            IReadOnlyList<Transform> cabins,
            IDictionary<string, Transform> semanticObjects)
        {
            Root = root ?? throw new ArgumentNullException(nameof(root));
            StationRoot = stationRoot ??
                throw new ArgumentNullException(nameof(stationRoot));
            Controller = controller ??
                throw new ArgumentNullException(nameof(controller));
            StationLight = stationLight ??
                throw new ArgumentNullException(nameof(stationLight));
            Bullwheel = bullwheel ??
                throw new ArgumentNullException(nameof(bullwheel));
            Supports = new ReadOnlyCollection<Transform>(
                new List<Transform>(supports));
            Cabins = new ReadOnlyCollection<Transform>(
                new List<Transform>(cabins));
            SemanticObjects = new ReadOnlyDictionary<string, Transform>(
                new Dictionary<string, Transform>(
                    semanticObjects,
                    StringComparer.Ordinal));
        }

        public GameObject Root { get; }
        public GameObject StationRoot { get; }
        public MountainCablewayController Controller { get; }
        public Light StationLight { get; }
        public Transform Bullwheel { get; }
        public IReadOnlyList<Transform> Supports { get; }
        public IReadOnlyList<Transform> Cabins { get; }
        public IReadOnlyDictionary<string, Transform> SemanticObjects { get; }
    }

    /// <summary>
    /// Which end of the line a station is.
    ///
    /// One rope, two terminals, and they are not the same building. The drive
    /// has the motor, the reducer and the shaft; the return has a tension
    /// weight and nothing that turns under power. Building the second as a
    /// copy of the first would put a second engine on a line that has one.
    /// </summary>
    public enum MountainCablewayStationKind
    {
        Drive = 0,
        Return = 1
    }

    /// <summary>
    /// Broad-strokes PS1 cableway presentation. Only the station this scene
    /// builds is physical; remote towers, cables and cabins cannot create
    /// invisible airborne blockers.
    /// </summary>
    public static class MountainCablewayWorldBuilder
    {
        /// <summary>Seat height off the cabin floor - a plank, not a chair.
        /// </summary>
        public const float CabinBenchHeight = 0.44f;

        public const float CabinBenchDepth = 0.42f;

        /// <summary>Name of the pelvis anchor a ride binds its passenger to.
        /// </summary>
        public const string CabinSeatAnchorName = "CabinSeatAnchor";

        private static readonly Color Concrete =
            new Color(0.23f, 0.245f, 0.225f, 1f);
        private static readonly Color DarkSteel =
            new Color(0.105f, 0.145f, 0.135f, 1f);
        private static readonly Color GreenSteel =
            new Color(0.16f, 0.225f, 0.19f, 1f);
        private static readonly Color Rust =
            new Color(0.37f, 0.245f, 0.15f, 1f);
        private static readonly Color Cable =
            new Color(0.045f, 0.055f, 0.052f, 1f);
        private static readonly Color ClosedMark =
            new Color(0.48f, 0.14f, 0.105f, 1f);
        private static readonly Color LampLens =
            new Color(0.58f, 0.78f, 0.65f, 1f);
        private static readonly Color FadedSign =
            new Color(0.56f, 0.52f, 0.39f, 1f);
        private static readonly Color CabinWarm =
            new Color(0.31f, 0.14f, 0.11f, 1f);
        private static readonly Color CabinCool =
            new Color(0.105f, 0.23f, 0.20f, 1f);
        private static readonly Color CabinWindow =
            new Color(0.20f, 0.34f, 0.28f, 1f);

        public static MountainCablewayWorldResult Build(
            Transform parent,
            MountainRoadCablewayPlan plan)
        {
            return Build(parent, plan, MountainCablewayStationKind.Drive);
        }

        public static MountainCablewayWorldResult Build(
            Transform parent,
            MountainRoadCablewayPlan plan,
            MountainCablewayStationKind stationKind)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (plan.Nodes.Count < 2 || plan.Cabins.Count == 0)
            {
                throw new ArgumentException(
                    "Cableway world requires nodes and cabins.",
                    nameof(plan));
            }

            var root = new GameObject("Operating Mountain Cableway");
            root.transform.SetParent(parent, false);
            var semanticObjects = new Dictionary<string, Transform>(
                StringComparer.Ordinal)
            {
                [plan.StableId] = root.transform
            };

            BuildContinuousCable(root.transform, plan);
            StationPresentation station = BuildLowerStation(
                root.transform,
                plan,
                stationKind);
            semanticObjects[plan.Nodes[0].StableId] =
                station.Root.transform;
            var supports = new List<Transform>();
            var supportRollerAnchors = new List<Transform>();
            BuildSupports(
                root.transform,
                plan,
                supports,
                supportRollerAnchors,
                semanticObjects);
            Transform upperTurn = BuildOccludedUpperTurn(
                root.transform,
                plan);
            semanticObjects[plan.Nodes[plan.Nodes.Count - 1].StableId] =
                upperTurn;
            List<Transform> cabins = BuildCabins(
                root.transform,
                plan,
                semanticObjects);

            MountainCablewayController controller =
                root.AddComponent<MountainCablewayController>();
            controller.Initialize(
                plan,
                cabins,
                station.Bullwheel,
                station.Reducer,
                supportRollerAnchors);
            return new MountainCablewayWorldResult(
                root,
                station.Root,
                controller,
                station.Light,
                station.Bullwheel,
                supports,
                cabins,
                semanticObjects);
        }

        private static void BuildContinuousCable(
            Transform parent,
            MountainRoadCablewayPlan plan)
        {
            var segments = new List<RuntimeOrientedBox>(160);
            AppendTrackSegments(parent, plan, 1, segments);
            AppendUpperTurnSegments(parent, plan, segments);
            AppendTrackSegments(parent, plan, -1, segments);
            AppendLowerTurnSegments(parent, plan, segments);
            GameObject cable = RuntimePrimitiveFactory
                .CreateCombinedOrientedBoxes(
                    "One Continuous Twin-Track Haul Cable",
                    parent,
                    segments,
                    Cable,
                    false,
                    1f,
                    RuntimeWorldUvMode.BoxProjected);
            MeshRenderer renderer = cable.GetComponent<MeshRenderer>();
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = false;
        }

        private static void AppendTrackSegments(
            Transform parent,
            MountainRoadCablewayPlan plan,
            int trackSide,
            ICollection<RuntimeOrientedBox> target)
        {
            if (trackSide > 0)
            {
                for (int index = 0; index < plan.Nodes.Count - 1; index++)
                {
                    AppendTrackSpan(
                        parent,
                        plan,
                        trackSide,
                        plan.Nodes[index].Distance,
                        plan.Nodes[index + 1].Distance,
                        target);
                }

                return;
            }

            for (int index = plan.Nodes.Count - 2; index >= 0; index--)
            {
                AppendTrackSpan(
                    parent,
                    plan,
                    trackSide,
                    plan.Nodes[index + 1].Distance,
                    plan.Nodes[index].Distance,
                    target);
            }
        }

        private static void AppendTrackSpan(
            Transform parent,
            MountainRoadCablewayPlan plan,
            int trackSide,
            float startDistance,
            float endDistance,
            ICollection<RuntimeOrientedBox> target)
        {
            int count = Mathf.Max(
                1,
                Mathf.CeilToInt(
                    Mathf.Abs(endDistance - startDistance) / 1.15f));
            Vector3 previous = MountainCablewayMotion.SampleTrackPosition(
                plan,
                startDistance,
                trackSide);
            for (int step = 1; step <= count; step++)
            {
                float distance = Mathf.Lerp(
                    startDistance,
                    endDistance,
                    step / (float)count);
                Vector3 current =
                    MountainCablewayMotion.SampleTrackPosition(
                        plan,
                        distance,
                        trackSide);
                AppendCableBox(parent, previous, current, target);
                previous = current;
            }
        }

        private static void AppendUpperTurnSegments(
            Transform parent,
            MountainRoadCablewayPlan plan,
            ICollection<RuntimeOrientedBox> target)
        {
            float turnLength = Mathf.PI * plan.TurnRadius;
            AppendLoopRange(
                parent,
                plan,
                plan.LineLength,
                plan.LineLength + turnLength,
                12,
                target);
        }

        private static void AppendLowerTurnSegments(
            Transform parent,
            MountainRoadCablewayPlan plan,
            ICollection<RuntimeOrientedBox> target)
        {
            float turnLength = Mathf.PI * plan.TurnRadius;
            float start = plan.LineLength * 2f + turnLength;
            AppendLoopRange(
                parent,
                plan,
                start,
                plan.LoopLength,
                12,
                target);
        }

        private static void AppendLoopRange(
            Transform parent,
            MountainRoadCablewayPlan plan,
            float start,
            float end,
            int count,
            ICollection<RuntimeOrientedBox> target)
        {
            Vector3 previous = MountainCablewayMotion.Sample(plan, start)
                .Position;
            for (int step = 1; step <= count; step++)
            {
                float distance = Mathf.Lerp(
                    start,
                    end,
                    step / (float)count);
                Vector3 current = MountainCablewayMotion.Sample(
                    plan,
                    distance).Position;
                AppendCableBox(parent, previous, current, target);
                previous = current;
            }
        }

        private static void AppendCableBox(
            Transform parent,
            Vector3 firstWorld,
            Vector3 secondWorld,
            ICollection<RuntimeOrientedBox> target)
        {
            Vector3 first = parent.InverseTransformPoint(firstWorld);
            Vector3 second = parent.InverseTransformPoint(secondWorld);
            Vector3 delta = second - first;
            float length = delta.magnitude;
            if (length <= 0.0001f)
            {
                return;
            }

            target.Add(new RuntimeOrientedBox(
                (first + second) * 0.5f,
                Quaternion.FromToRotation(Vector3.forward, delta / length),
                new Vector3(0.055f, 0.055f, length + 0.035f)));
        }

        /// <summary>
        /// Gives one cableway primitive its sheet. The practical lens and
        /// the sixteen cabin windows carry the shared emissive material and
        /// pass no surface; so does the haul cable, whose fifty-five
        /// millimetres sit under a texel of the composite.
        /// </summary>
        private static void TextureSurface(
            GameObject instance,
            MountainRoadSurfaceKind surface,
            Color tint)
        {
            if (instance == null)
            {
                return;
            }

            MountainRoadSurfaceAppearance.Apply(
                instance.GetComponent<Renderer>(),
                surface,
                tint);
        }

        private static void TextureSurface(
            GameObject instance,
            MountainRoadSurfaceKind surface,
            SurfaceProjection projection,
            Color tint)
        {
            if (instance == null)
            {
                return;
            }

            MountainRoadSurfaceAppearance.Apply(
                instance.GetComponent<Renderer>(),
                surface,
                projection,
                tint);
        }

        private static StationPresentation BuildLowerStation(
            Transform parent,
            MountainRoadCablewayPlan plan,
            MountainCablewayStationKind stationKind)
        {
            bool drive = stationKind == MountainCablewayStationKind.Drive;
            var root = new GameObject(
                drive
                    ? "Lower Cableway Station"
                    : "Upper Cableway Return Station");
            root.transform.SetParent(parent, false);
            root.transform.SetPositionAndRotation(
                plan.StationArea.Center,
                Quaternion.LookRotation(plan.LineForward, Vector3.up));
            Vector2 stationSize = plan.StationArea.Size;

            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Physical Concrete Station Pad",
                    root.transform,
                    Vector3.up * 0.08f,
                    new Vector3(stationSize.x, 0.16f, stationSize.y),
                    Concrete,
                    true),
                MountainRoadSurfaceKind.Concrete,
                Concrete);
            BuildStationFrame(root.transform, stationSize, drive);
            BuildBoardingZone(root.transform, plan);

            Transform bullwheel = BuildLowerBullwheel(root.transform, plan);
            Transform reducer = null;
            if (drive)
            {
                reducer = BuildVisibleReducer(
                    root.transform,
                    bullwheel.localPosition);
                BuildDriveHeadframe(
                    root.transform,
                    bullwheel.localPosition,
                    reducer.localPosition,
                    stationSize);
                TextureSurface(
                    CreateBetween(
                        "Visible Drive Shaft",
                        root.transform,
                        reducer.localPosition + Vector3.up * 0.10f,
                        bullwheel.localPosition + Vector3.down * 0.16f,
                        0.14f,
                        Rust,
                        false),
                    MountainRoadSurfaceKind.RustedIron,
                    SurfaceProjection.BoxZY,
                    Rust);
            }
            else
            {
                BuildTensionCarriage(
                    root.transform,
                    bullwheel.localPosition,
                    stationSize);
            }

            Light light = BuildStationPractical(root.transform);
            BuildBoardingFlood(root.transform);
            return new StationPresentation(
                root,
                bullwheel,
                reducer,
                light);
        }

        private static void BuildStationFrame(
            Transform parent,
            Vector2 stationSize,
            bool drive)
        {
            float halfRight = stationSize.x * 0.5f - 0.55f;
            float halfForward = stationSize.y * 0.5f - 0.48f;
            for (int right = -1; right <= 1; right += 2)
            {
                for (int forward = -1; forward <= 1; forward += 2)
                {
                    TextureSurface(
                        RuntimePrimitiveFactory.CreateBox(
                            "Physical Station Column",
                            parent,
                            new Vector3(
                                right * halfRight,
                                2.25f,
                                forward * halfForward),
                            new Vector3(0.28f, 4.5f, 0.28f),
                            GreenSteel,
                            true),
                        MountainRoadSurfaceKind.PaintedMetal,
                        SurfaceProjection.BoxZY,
                        GreenSteel);
                }
            }

            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Corrugated Station Canopy",
                    parent,
                    new Vector3(0f, 4.62f, 0f),
                    new Vector3(
                        stationSize.x - 0.2f,
                        0.24f,
                        stationSize.y - 0.18f),
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                DarkSteel);
            if (!drive)
            {
                // No service hut at the return end: there is no machinery in
                // it to service.
                return;
            }

            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Physical Drive Service Hut",
                    parent,
                    new Vector3(3.25f, 1.32f, -1.62f),
                    new Vector3(2.1f, 2.48f, 2.0f),
                    GreenSteel,
                    true),
                MountainRoadSurfaceKind.PaintedMetal,
                GreenSteel);
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Rusty Service Hut Door",
                    parent,
                    new Vector3(2.185f, 1.28f, -1.62f),
                    new Vector3(0.035f, 2.0f, 1.25f),
                    Rust,
                    false),
                MountainRoadSurfaceKind.RustedIron,
                SurfaceProjection.BoxZY,
                Rust);
        }

        /// <summary>
        /// What holds the rope tight at the far end. A carriage on two rails
        /// with a stack of weights hanging off it - the whole of a return
        /// station, and nothing that turns under power.
        /// </summary>
        private static void BuildTensionCarriage(
            Transform parent,
            Vector3 bullwheelLocal,
            Vector2 stationSize)
        {
            float halfForward = stationSize.y * 0.5f - 0.48f;
            for (int side = -1; side <= 1; side += 2)
            {
                TextureSurface(
                    RuntimePrimitiveFactory.CreateBox(
                        "Tension Rail",
                        parent,
                        new Vector3(
                            bullwheelLocal.x + side * 0.5f,
                            bullwheelLocal.y - 0.52f,
                            -halfForward * 0.35f),
                        new Vector3(0.14f, 0.14f, halfForward * 1.4f),
                        Rust,
                        false),
                    MountainRoadSurfaceKind.RustedIron,
                    SurfaceProjection.BoxZY,
                    Rust);
            }

            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Physical Tension Carriage",
                    parent,
                    new Vector3(
                        bullwheelLocal.x,
                        bullwheelLocal.y - 0.62f,
                        -halfForward * 0.72f),
                    new Vector3(1.35f, 0.34f, 1.05f),
                    DarkSteel,
                    true),
                MountainRoadSurfaceKind.PaintedMetal,
                DarkSteel);

            // The weights themselves, hanging into the pit. Five plates,
            // because a stack reads as a stack and a block reads as a block.
            for (int plate = 0; plate < 5; plate++)
            {
                TextureSurface(
                    RuntimePrimitiveFactory.CreateBox(
                        "Tension Weight Plate",
                        parent,
                        new Vector3(
                            bullwheelLocal.x,
                            bullwheelLocal.y - 1.05f - plate * 0.24f,
                            -halfForward * 0.72f),
                        new Vector3(0.92f, 0.2f, 0.72f),
                        Rust,
                        false),
                    MountainRoadSurfaceKind.RustedIron,
                    SurfaceProjection.BoxXY,
                    Rust);
            }

            TextureSurface(
                CreateBetween(
                    "Tension Hanger",
                    parent,
                    new Vector3(
                        bullwheelLocal.x,
                        bullwheelLocal.y - 0.62f,
                        -halfForward * 0.72f),
                    new Vector3(
                        bullwheelLocal.x,
                        bullwheelLocal.y - 1.05f,
                        -halfForward * 0.72f),
                    0.12f,
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxZY,
                DarkSteel);
        }

        /// <summary>
        /// The raised strip beside the outbound track, and the only reason
        /// boarding works at all.
        ///
        /// The cabin floor hangs `0.87 m` over a bare pad here. That is a
        /// climb, and a dock further than the motor's two-centimetre vertical
        /// tolerance from the hero's root is refused SILENTLY - the prompt
        /// shows and the key does nothing, forever. The plan derives the top
        /// of this strip from the cabin's own hang, so the step is a fixed
        /// `0.42 m` at both terminals and neither can drift.
        /// </summary>
        private static void BuildBoardingPlatform(
            Transform parent,
            MountainRoadCablewayPlan plan)
        {
            Vector3 dockLocal = parent.InverseTransformPoint(
                plan.BoardingDockPosition);
            float top = dockLocal.y;
            if (top <= 0.18f)
            {
                // The pad is already at the right height for this line; a
                // strip would be a lip to trip over rather than a step.
                return;
            }

            // The strip runs from just clear of the cabin's outboard face to
            // a metre past the dock. Derived rather than centred on the dock,
            // because a centred `2.4 m` box reaches back UNDER the cabin.
            float inner = plan.BoardingCabinOuterOffset + 0.06f;
            float outer = dockLocal.x + 1f;
            float width = Mathf.Max(1.2f, outer - inner);
            float centerX = (inner + outer) * 0.5f;
            const float length = 5.2f;

            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Physical Boarding Platform",
                    parent,
                    new Vector3(centerX, top * 0.5f, dockLocal.z),
                    new Vector3(width, top, length),
                    Concrete,
                    true),
                MountainRoadSurfaceKind.Concrete,
                Concrete);

            // A kerb edge along the track side, so the drop is read before it
            // is stepped off.
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Platform Edge Nosing",
                    parent,
                    new Vector3(
                        inner + 0.04f,
                        top - 0.03f,
                        dockLocal.z),
                    new Vector3(0.08f, 0.06f, length),
                    FadedSign,
                    false),
                MountainRoadSurfaceKind.PaleEnamel,
                SurfaceProjection.BoxZY,
                FadedSign);

            // And a way UP onto it, at the END the yard approaches from - the
            // long sides are the cabin on one hand and the edge of the pad on
            // the other. The strip stands well over the hero's `0.28 m` step
            // offset, so without these he simply could not get on and the
            // whole boarding would be unreachable while looking correct.
            const int treadCount = 3;
            float treadDepth = 0.34f;
            for (int tread = 0; tread < treadCount; tread++)
            {
                float height = top * (tread + 1) / (treadCount + 1);
                TextureSurface(
                    RuntimePrimitiveFactory.CreateBox(
                        "Physical Platform Tread",
                        parent,
                        new Vector3(
                            centerX,
                            height * 0.5f,
                            dockLocal.z - length * 0.5f - treadDepth *
                            (treadCount - tread - 0.5f)),
                        new Vector3(width, height, treadDepth),
                        Concrete,
                        true),
                    MountainRoadSurfaceKind.Concrete,
                    Concrete);
            }
        }

        /// <summary>
        /// The barrier, with a way through it, and the raised strip a person
        /// steps off.
        ///
        /// The middle bay is open now and the leaf stands swung back against
        /// its post: the line runs and people ride it. **The faded "boarding
        /// closed" sign stays exactly where it was**, because nobody took it
        /// down - which is the truer thing about this place than either the
        /// fence or the gate.
        /// </summary>
        private static void BuildBoardingZone(
            Transform parent,
            MountainRoadCablewayPlan plan)
        {
            const float fenceForward = 1.56f;

            // Two rails either side of a `1.6 m` gap, instead of one unbroken
            // `5.45 m` run.
            const float gateHalfWidth = 0.8f;
            const float railHalfSpan = 2.2f;

            // Ends and JAMBS. The middle post used to stand on the centre
            // line, which is exactly where the opening now is - leaving it
            // there put a physical bollard in the doorway and the hero could
            // not reach the dock at all, while the fence looked open.
            float[] postOffsets =
            {
                -railHalfSpan, -gateHalfWidth, gateHalfWidth, railHalfSpan
            };
            for (int index = 0; index < postOffsets.Length; index++)
            {
                TextureSurface(
                    RuntimePrimitiveFactory.CreateBox(
                        "Physical Boarding Post",
                        parent,
                        new Vector3(postOffsets[index], 0.82f, fenceForward),
                        new Vector3(0.13f, 1.55f, 0.13f),
                        Rust,
                        true),
                    MountainRoadSurfaceKind.RustedIron,
                    SurfaceProjection.BoxZY,
                    Rust);
            }

            float wingWidth = railHalfSpan - gateHalfWidth;
            for (int rail = 0; rail < 2; rail++)
            {
                for (int side = -1; side <= 1; side += 2)
                {
                    TextureSurface(
                        RuntimePrimitiveFactory.CreateBox(
                            "Physical Boarding Rail",
                            parent,
                            new Vector3(
                                side * (gateHalfWidth + wingWidth * 0.5f),
                                0.66f + rail * 0.58f,
                                fenceForward),
                            new Vector3(wingWidth, 0.10f, 0.10f),
                            DarkSteel,
                            true),
                        MountainRoadSurfaceKind.PaintedMetal,
                        SurfaceProjection.BoxXY,
                        DarkSteel);
                }
            }

            // The leaf, standing open against the left post. No collider: an
            // open gate is not a thing to walk into, and contact here is read
            // back as achieved movement, so a graze would read as a crawl.
            GameObject leaf = RuntimePrimitiveFactory.CreateBox(
                "Boarding Gate Leaf Standing Open",
                parent,
                new Vector3(-gateHalfWidth - 0.06f, 0.95f, fenceForward),
                new Vector3(0.08f, 1.12f, 1.5f),
                Rust,
                false);
            TextureSurface(
                leaf,
                MountainRoadSurfaceKind.RustedIron,
                SurfaceProjection.BoxZY,
                Rust);

            BuildBoardingPlatform(parent, plan);

            GameObject sign = RuntimePrimitiveFactory.CreateBox(
                "Faded Sign - Boarding Closed",
                parent,
                new Vector3(2.7f, 1.62f, fenceForward - 0.08f),
                new Vector3(1.55f, 0.56f, 0.06f),
                FadedSign,
                false);
            TextureSurface(
                sign,
                MountainRoadSurfaceKind.PaleEnamel,
                SurfaceProjection.BoxXY,
                FadedSign);
            for (int diagonal = -1; diagonal <= 1; diagonal += 2)
            {
                GameObject mark = RuntimePrimitiveFactory.CreateBox(
                    "Painted Closed Mark",
                    sign.transform,
                    new Vector3(0f, 0f, -0.54f),
                    new Vector3(0.92f, 0.10f, 0.06f),
                    ClosedMark,
                    false);
                mark.transform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    diagonal * 25f);

                // Four millimetres of paint on the sign's own face; it is
                // a stroke, not a material, and takes no sheet.
            }
        }

        /// <summary>
        /// What actually holds the machinery up.
        ///
        /// The bullwheel sits four metres over the pad and four and a
        /// half metres FORWARD of the station centre - outside the
        /// canopy footprint entirely - and the reducer floats at three
        /// and a half. Between them ran a drive shaft, which tied the two
        /// to each other and neither of them to the ground: from the
        /// yard the whole drive read as hanging in the air.
        ///
        /// So: a bearing pedestal up to the hub, four struts tying that
        /// outrigger back to the frame it stands proud of, and a machine
        /// deck slung between the two rear columns for the gearbox to
        /// stand on. The boarding happens under that deck, which is what
        /// a lower station looks like.
        /// </summary>
        private static void BuildDriveHeadframe(
            Transform parent,
            Vector3 bullwheelLocal,
            Vector3 reducerLocal,
            Vector2 stationSize)
        {
            float halfRight = stationSize.x * 0.5f - 0.55f;
            float halfForward = stationSize.y * 0.5f - 0.48f;
            float pedestalTop = bullwheelLocal.y - 0.34f;

            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Physical Bullwheel Pedestal",
                    parent,
                    new Vector3(
                        bullwheelLocal.x,
                        pedestalTop * 0.5f,
                        bullwheelLocal.z),
                    new Vector3(0.48f, pedestalTop, 0.48f),
                    GreenSteel,
                    true),
                MountainRoadSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxZY,
                GreenSteel);
            TextureSurface(
                RuntimePrimitiveFactory.CreateCylinder(
                    "Bullwheel Bearing Housing",
                    parent,
                    new Vector3(
                        bullwheelLocal.x,
                        pedestalTop + 0.14f,
                        bullwheelLocal.z),
                    new Vector3(0.78f, 0.34f, 0.78f),
                    Rust,
                    false),
                MountainRoadSurfaceKind.RustedIron,
                SurfaceProjection.CylinderSide,
                Rust);
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Bullwheel Pedestal Foot",
                    parent,
                    new Vector3(
                        bullwheelLocal.x,
                        0.11f,
                        bullwheelLocal.z),
                    new Vector3(1.15f, 0.22f, 1.15f),
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.Concrete,
                DarkSteel);

            for (int side = -1; side <= 1; side += 2)
            {
                TextureSurface(
                    CreateBetween(
                        "Headframe Upper Strut",
                        parent,
                        new Vector3(
                            bullwheelLocal.x,
                            pedestalTop - 0.55f,
                            bullwheelLocal.z),
                        new Vector3(
                            side * 2.4f,
                            4.42f,
                            halfForward),
                        0.13f,
                        GreenSteel,
                        false),
                    MountainRoadSurfaceKind.PaintedMetal,
                    SurfaceProjection.BoxZY,
                    GreenSteel);
                TextureSurface(
                    CreateBetween(
                        "Headframe Lower Strut",
                        parent,
                        new Vector3(
                            bullwheelLocal.x,
                            1.35f,
                            bullwheelLocal.z),
                        new Vector3(
                            side * 1.7f,
                            0.18f,
                            halfForward + 0.15f),
                        0.11f,
                        Rust,
                        false),
                    MountainRoadSurfaceKind.RustedIron,
                    SurfaceProjection.BoxZY,
                    Rust);
            }

            float deckTop = reducerLocal.y - 0.46f;
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Machine Deck",
                    parent,
                    new Vector3(
                        -(halfRight + 0.8f) * 0.5f,
                        deckTop - 0.08f,
                        0f),
                    new Vector3(
                        halfRight - 0.8f + 0.32f,
                        0.16f,
                        halfForward * 2f + 0.3f),
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                DarkSteel);
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Physical Machine Deck Prop",
                    parent,
                    new Vector3(-0.9f, (deckTop - 0.16f) * 0.5f, 1.9f),
                    new Vector3(0.2f, deckTop - 0.16f, 0.2f),
                    GreenSteel,
                    true),
                MountainRoadSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxZY,
                GreenSteel);
        }

        private static Transform BuildLowerBullwheel(
            Transform parent,
            MountainRoadCablewayPlan plan)
        {
            Transform pivot = new GameObject(
                "Visible Operating Lower Bullwheel").transform;
            pivot.SetParent(parent, false);
            pivot.position = plan.LowerCableCenter;
            pivot.rotation = parent.rotation;
            float diameter = plan.TrackSeparation * 1.08f;
            TextureSurface(
                RuntimePrimitiveFactory.CreateCylinder(
                    "Bullwheel Disc",
                    pivot,
                    Vector3.zero,
                    new Vector3(diameter, 0.11f, diameter),
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                SurfaceProjection.CylinderCapXZ,
                DarkSteel);
            TextureSurface(
                RuntimePrimitiveFactory.CreateCylinder(
                    "Bullwheel Hub",
                    pivot,
                    Vector3.up * 0.15f,
                    new Vector3(0.52f, 0.20f, 0.52f),
                    Rust,
                    false),
                MountainRoadSurfaceKind.RustedIron,
                SurfaceProjection.CylinderSide,
                Rust);
            for (int spoke = 0; spoke < 4; spoke++)
            {
                GameObject bar = RuntimePrimitiveFactory.CreateBox(
                    "Bullwheel Spoke",
                    pivot,
                    Vector3.up * 0.14f,
                    new Vector3(diameter * 0.88f, 0.07f, 0.09f),
                    Rust,
                    false);
                bar.transform.localRotation = Quaternion.Euler(
                    0f,
                    spoke * 45f,
                    0f);
                TextureSurface(
                    bar,
                    MountainRoadSurfaceKind.RustedIron,
                    SurfaceProjection.BoxXY,
                    Rust);
            }

            for (int side = -1; side <= 1; side += 2)
            {
                GameObject roller = RuntimePrimitiveFactory.CreateCylinder(
                    "Visible Lower Guide Roller",
                    parent,
                    pivot.localPosition +
                    new Vector3(
                        side * plan.TrackSeparation * 0.5f,
                        -0.16f,
                        -0.34f),
                    new Vector3(0.44f, 0.16f, 0.44f),
                    Rust,
                    false);
                roller.transform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    90f);
                TextureSurface(
                    roller,
                    MountainRoadSurfaceKind.RustedIron,
                    SurfaceProjection.CylinderSide,
                    Rust);
            }

            return pivot;
        }

        private static Transform BuildVisibleReducer(
            Transform parent,
            Vector3 bullwheelLocalPosition)
        {
            Vector3 position = new Vector3(
                -2.15f,
                bullwheelLocalPosition.y - 0.45f,
                2.62f);
            GameObject reducer = RuntimePrimitiveFactory.CreateBox(
                "Visible Cableway Motor Reducer",
                parent,
                position,
                new Vector3(1.35f, 0.92f, 1.15f),
                GreenSteel,
                false);
            TextureSurface(
                reducer,
                MountainRoadSurfaceKind.PaintedMetal,
                GreenSteel);
            GameObject cover = RuntimePrimitiveFactory.CreateCylinder(
                "Reducer Gear Cover",
                reducer.transform,
                new Vector3(0f, 0f, -0.54f),
                new Vector3(0.62f, 0.12f, 0.62f),
                Rust,
                false);
            cover.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            TextureSurface(
                cover,
                MountainRoadSurfaceKind.RustedIron,
                SurfaceProjection.CylinderCapXZ,
                Rust);
            return reducer.transform;
        }

        /// <summary>
        /// A second fixture on the outer edge of the canopy, throwing
        /// down and OUT across the freight kerb and the yard rather than
        /// onto the platform the lens already covers.
        ///
        /// One lamp under a canopy lights the thing it hangs over and
        /// nothing else, which is why the station read as a dark shape
        /// with a glow inside it. This is what makes it a place you can
        /// see from the far side of the pad.
        /// </summary>
        private static void BuildBoardingFlood(Transform parent)
        {
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Boarding Flood Housing",
                    parent,
                    new Vector3(0f, 4.34f, -2.55f),
                    new Vector3(0.72f, 0.2f, 0.42f),
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                DarkSteel);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Visible Boarding Flood Lens",
                parent,
                new Vector3(0f, 4.21f, -2.55f),
                new Vector3(0.56f, 0.07f, 0.3f),
                LampLens,
                CityNightResources.EmissiveMaterial,
                false);
            lens.GetComponent<Renderer>().shadowCastingMode =
                ShadowCastingMode.Off;

            var lightObject = new GameObject("Station Boarding Flood");
            lightObject.transform.SetParent(lens.transform, false);
            lightObject.transform.localPosition = Vector3.down * 0.05f;
            lightObject.transform.localRotation = Quaternion.LookRotation(
                (Vector3.down * 0.88f + Vector3.back * 0.47f).normalized,
                Vector3.forward);
            Light flood = lightObject.AddComponent<Light>();
            flood.type = LightType.Spot;
            flood.color = new Color(0.62f, 0.8f, 0.72f);
            flood.intensity = 6.5f;
            flood.range = 15f;
            flood.spotAngle = 100f;
            flood.innerSpotAngle = 54f;
            flood.shadows = LightShadows.None;
            flood.renderMode = LightRenderMode.ForcePixel;
            flood.bounceIntensity = 0f;
        }

        private static Light BuildStationPractical(Transform parent)
        {
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Station Practical Housing",
                    parent,
                    new Vector3(0f, 4.42f, -0.75f),
                    new Vector3(1.55f, 0.16f, 0.58f),
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                DarkSteel);
            GameObject lens = RuntimePrimitiveFactory.CreateBox(
                "Visible Station Practical Lens",
                parent,
                new Vector3(0f, 4.31f, -0.75f),
                new Vector3(1.18f, 0.07f, 0.36f),
                LampLens,
                CityNightResources.EmissiveMaterial,
                false);
            lens.GetComponent<Renderer>().shadowCastingMode =
                ShadowCastingMode.Off;
            var lightObject = new GameObject(
                "Station Light From Visible Lens");
            lightObject.transform.SetParent(lens.transform, false);
            lightObject.transform.localPosition = Vector3.down * 0.045f;
            lightObject.transform.localRotation = Quaternion.LookRotation(
                (Vector3.down + Vector3.forward * 0.17f).normalized,
                Vector3.forward);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = new Color(0.58f, 0.78f, 0.66f);

            // The other half of the summit's night. It used to burn at
            // `1.65` against a cafe counter at `10.5`, which made the
            // station a night-light beside a lit room - and the two are
            // meant to be a pair, one cold and one warm, each pulling the
            // eye across the yard to its own side.
            light.intensity = 7.2f;
            light.range = 16f;
            light.spotAngle = 78f;
            light.innerSpotAngle = 46f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.ForcePixel;
            light.bounceIntensity = 0.08f;
            return light;
        }

        private static void BuildSupports(
            Transform parent,
            MountainRoadCablewayPlan plan,
            ICollection<Transform> supports,
            ICollection<Transform> rollerAnchors,
            IDictionary<string, Transform> semanticObjects)
        {
            for (int index = 0; index < plan.Nodes.Count; index++)
            {
                MountainCablewayNodeDescriptor node = plan.Nodes[index];
                if (node.Kind != MountainCablewayNodeKind.Support)
                {
                    continue;
                }

                Transform root = new GameObject(
                    "Colliderless A-Frame - " + node.StableId).transform;
                root.SetParent(parent, false);
                root.SetPositionAndRotation(
                    node.GroundPosition,
                    Quaternion.LookRotation(plan.LineForward, Vector3.up));
                float height = Mathf.Max(
                    4.8f,
                    node.CableCenter.y - node.GroundPosition.y);
                BuildAFrame(root, height, plan.TrackSeparation);
                Transform crossbeam = BuildSupportRollers(
                    root,
                    height,
                    plan.TrackSeparation);
                supports.Add(root);
                rollerAnchors.Add(crossbeam);
                semanticObjects[node.StableId] = root;
            }
        }

        private static void BuildAFrame(
            Transform parent,
            float height,
            float trackSeparation)
        {
            float baseHalf = trackSeparation * 0.5f + 1.65f;
            float topHalf = trackSeparation * 0.5f + 0.52f;
            for (int depth = -1; depth <= 1; depth += 2)
            {
                float z = depth * 0.46f;
                TextureSurface(
                    CreateBetween(
                        "A-Frame Left Leg",
                        parent,
                        new Vector3(-baseHalf, 0.08f, z),
                        new Vector3(-topHalf, height - 0.48f, z),
                        0.24f,
                        GreenSteel,
                        false),
                    MountainRoadSurfaceKind.PaintedMetal,
                    SurfaceProjection.BoxZY,
                    GreenSteel);
                TextureSurface(
                    CreateBetween(
                        "A-Frame Right Leg",
                        parent,
                        new Vector3(baseHalf, 0.08f, z),
                        new Vector3(topHalf, height - 0.48f, z),
                        0.24f,
                        GreenSteel,
                        false),
                    MountainRoadSurfaceKind.PaintedMetal,
                    SurfaceProjection.BoxZY,
                    GreenSteel);
            }

            for (int side = -1; side <= 1; side += 2)
            {
                TextureSurface(
                    CreateBetween(
                        "Rusty Tower Cross Brace",
                        parent,
                        new Vector3(
                            side * (baseHalf - 0.22f),
                            height * 0.22f,
                            -0.46f),
                        new Vector3(
                            side * Mathf.Lerp(baseHalf, topHalf, 0.68f),
                            height * 0.68f,
                            0.46f),
                        0.10f,
                        Rust,
                        false),
                    MountainRoadSurfaceKind.RustedIron,
                    SurfaceProjection.BoxZY,
                    Rust);
            }
        }

        private static Transform BuildSupportRollers(
            Transform parent,
            float height,
            float trackSeparation)
        {
            GameObject crossbeam = RuntimePrimitiveFactory.CreateBox(
                "Visible Support Roller Crossbeam",
                parent,
                new Vector3(0f, height - 0.28f, 0f),
                new Vector3(trackSeparation + 1.55f, 0.20f, 0.32f),
                DarkSteel,
                false);
            TextureSurface(
                crossbeam,
                MountainRoadSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXY,
                DarkSteel);
            for (int side = -1; side <= 1; side += 2)
            {
                GameObject roller = RuntimePrimitiveFactory.CreateCylinder(
                    "Visible Cable Roller",
                    parent,
                    new Vector3(
                        side * trackSeparation * 0.5f,
                        height - 0.10f,
                        0f),
                    new Vector3(0.38f, 0.15f, 0.38f),
                    Rust,
                    false);
                roller.transform.localRotation = Quaternion.Euler(
                    0f,
                    0f,
                    90f);
                TextureSurface(
                    roller,
                    MountainRoadSurfaceKind.RustedIron,
                    SurfaceProjection.CylinderSide,
                    Rust);
            }

            return crossbeam.transform;
        }

        private static Transform BuildOccludedUpperTurn(
            Transform parent,
            MountainRoadCablewayPlan plan)
        {
            Transform root = new GameObject(
                "Upper Return Hidden Behind Snow Ridge").transform;
            root.SetParent(parent, false);
            root.SetPositionAndRotation(
                plan.UpperCableCenter,
                Quaternion.LookRotation(plan.LineForward, Vector3.up));
            float diameter = plan.TrackSeparation * 1.08f;
            TextureSurface(
                RuntimePrimitiveFactory.CreateCylinder(
                    "Occluded Upper Bullwheel",
                    root,
                    Vector3.zero,
                    new Vector3(diameter, 0.10f, diameter),
                    Cable,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                SurfaceProjection.CylinderCapXZ,
                Cable);
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Occluded Upper Machinery Frame",
                    root,
                    Vector3.down * 0.55f,
                    new Vector3(diameter + 0.7f, 1.15f, 0.24f),
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXY,
                DarkSteel);
            return root;
        }

        private static List<Transform> BuildCabins(
            Transform parent,
            MountainRoadCablewayPlan plan,
            IDictionary<string, Transform> semanticObjects)
        {
            var cabins = new List<Transform>(plan.Cabins.Count);
            for (int index = 0; index < plan.Cabins.Count; index++)
            {
                MountainCablewayCabinDescriptor descriptor =
                    plan.Cabins[index];
                Transform cabin = BuildCabin(
                    parent,
                    descriptor.StableId,
                    plan.CabinSize,
                    index);
                cabins.Add(cabin);
                semanticObjects[descriptor.StableId] = cabin;
            }

            return cabins;
        }

        private static Transform BuildCabin(
            Transform parent,
            string stableId,
            Vector3 size,
            int variant)
        {
            Transform root = new GameObject(
                "Colliderless Moving Cabin - " + stableId).transform;
            root.SetParent(parent, false);
            Color body = variant % 2 == 0 ? CabinWarm : CabinCool;
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Cabin Cable Grip",
                    root,
                    new Vector3(0f, -0.10f, 0f),
                    new Vector3(0.62f, 0.12f, 0.14f),
                    Rust,
                    false),
                MountainRoadSurfaceKind.RustedIron,
                SurfaceProjection.BoxXY,
                Rust);
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Cabin Hanger",
                    root,
                    new Vector3(0f, -0.55f, 0f),
                    new Vector3(0.09f, 0.98f, 0.09f),
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxZY,
                DarkSteel);

            float roofY = -MountainRoadCablewayPlan.CabinRoofDrop;
            float floorY = roofY - size.y;
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Cabin Roof",
                    root,
                    new Vector3(0f, roofY, 0f),
                    new Vector3(size.x * 1.06f, 0.18f, size.z * 1.08f),
                    body,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                body);
            const float skirt = MountainRoadCablewayPlan.CabinSkirtHeight;
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Cabin Lower Skirt",
                    root,
                    new Vector3(0f, floorY + skirt * 0.5f, 0f),
                    new Vector3(size.x, skirt, size.z),
                    body,
                    false),
                MountainRoadSurfaceKind.PaintedMetal,
                SurfaceProjection.BoxXY,
                body);
            float postY = Mathf.Lerp(roofY, floorY + skirt, 0.5f);
            float postHeight = roofY - (floorY + skirt);
            for (int right = -1; right <= 1; right += 2)
            {
                for (int forward = -1; forward <= 1; forward += 2)
                {
                    TextureSurface(
                        RuntimePrimitiveFactory.CreateBox(
                            "Cabin Corner Post",
                            root,
                            new Vector3(
                                right * (size.x * 0.5f - 0.07f),
                                postY,
                                forward * (size.z * 0.5f - 0.07f)),
                            new Vector3(0.13f, postHeight, 0.13f),
                            body,
                            false),
                        MountainRoadSurfaceKind.PaintedMetal,
                        SurfaceProjection.BoxZY,
                        body);
                }
            }

            float windowY = postY + 0.02f;
            float windowHeight = Mathf.Max(0.42f, postHeight - 0.22f);
            CreateCabinWindow(
                root,
                "Cabin Front Window",
                new Vector3(0f, windowY, size.z * 0.5f + 0.006f),
                new Vector3(size.x * 0.78f, windowHeight, 0.025f));
            CreateCabinWindow(
                root,
                "Cabin Rear Window",
                new Vector3(0f, windowY, -size.z * 0.5f - 0.006f),
                new Vector3(size.x * 0.78f, windowHeight, 0.025f));

            // Local `+X` is a DOORWAY, and it is a doorway by omission - the
            // window that used to be here is simply not built. That is the
            // bus's and the car's pattern, and it buys more than geometry:
            // with no leaf there is no door-phase contract, so the boarding
            // clips run with their stock timing instead of needing a hold
            // while a leaf swings.
            //
            // It is always this side. The cabin yaws to face its own travel,
            // so local `+X` is the OUTBOARD side at both terminals - which is
            // where the platform is, because the gap between the two tracks
            // is filled by the bullwheel's own pedestal.
            CreateCabinWindow(
                root,
                "Cabin Left Window",
                new Vector3(-size.x * 0.5f - 0.006f, windowY, 0f),
                new Vector3(0.025f, windowHeight, size.z * 0.72f));

            BuildCabinInterior(root, size, floorY + skirt, roofY);
            return root;
        }

        /// <summary>
        /// What a converted ore cabin has inside: one bench across the back,
        /// a grab rail, and a bar that drops across the doorway.
        ///
        /// The bench is at local `-Z` and faces `+Z`, which is the way the
        /// cabin is going - on the descending track the whole body yaws round,
        /// so one bench serves both directions and the passenger is never
        /// carried backwards.
        /// </summary>
        private static void BuildCabinInterior(
            Transform root,
            Vector3 size,
            float floorTop,
            float roofY)
        {
            float benchTop = floorTop + CabinBenchHeight;
            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Cabin Bench",
                    root,
                    new Vector3(
                        0f,
                        benchTop - CabinBenchHeight * 0.5f,
                        -size.z * 0.5f + CabinBenchDepth * 0.5f + 0.06f),
                    new Vector3(
                        size.x - 0.24f,
                        CabinBenchHeight,
                        CabinBenchDepth),
                    Rust,
                    false),
                MountainRoadSurfaceKind.Timber,
                SurfaceProjection.BoxXY,
                Rust);

            // The pelvis point the ride binds to. An empty, not a measurement
            // taken later: the cabin moves, and anything solved once against
            // a parked one goes stale the moment the line starts.
            var seat = new GameObject("CabinSeatAnchor").transform;
            seat.SetParent(root, false);
            seat.localPosition = new Vector3(
                0f,
                benchTop,
                -size.z * 0.5f + CabinBenchDepth * 0.5f + 0.06f);
            seat.localRotation = Quaternion.identity;

            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Cabin Grab Rail",
                    root,
                    new Vector3(
                        -size.x * 0.5f + 0.12f,
                        roofY - 0.30f,
                        0f),
                    new Vector3(0.06f, 0.06f, size.z * 0.7f),
                    DarkSteel,
                    false),
                MountainRoadSurfaceKind.RustedIron,
                SurfaceProjection.BoxZY,
                DarkSteel);

            TextureSurface(
                RuntimePrimitiveFactory.CreateBox(
                    "Cabin Safety Bar",
                    root,
                    new Vector3(
                        size.x * 0.5f - 0.05f,
                        floorTop + 0.92f,
                        0f),
                    new Vector3(0.07f, 0.07f, size.z * 0.86f),
                    Rust,
                    false),
                MountainRoadSurfaceKind.RustedIron,
                SurfaceProjection.BoxZY,
                Rust);
        }

        private static void CreateCabinWindow(
            Transform parent,
            string name,
            Vector3 localPosition,
            Vector3 size)
        {
            GameObject window = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                localPosition,
                size,
                CabinWindow,
                CityNightResources.EmissiveMaterial,
                false);
            window.GetComponent<Renderer>().shadowCastingMode =
                ShadowCastingMode.Off;
        }

        private static GameObject CreateBetween(
            string name,
            Transform parent,
            Vector3 first,
            Vector3 second,
            float thickness,
            Color color,
            bool collider)
        {
            Vector3 delta = second - first;
            float length = delta.magnitude;
            GameObject result = RuntimePrimitiveFactory.CreateBox(
                name,
                parent,
                (first + second) * 0.5f,
                new Vector3(thickness, thickness, length),
                color,
                collider);
            result.transform.localRotation = Quaternion.FromToRotation(
                Vector3.forward,
                delta / Mathf.Max(0.0001f, length));
            return result;
        }

        private readonly struct StationPresentation
        {
            public StationPresentation(
                GameObject root,
                Transform bullwheel,
                Transform reducer,
                Light light)
            {
                Root = root;
                Bullwheel = bullwheel;
                Reducer = reducer;
                Light = light;
            }

            public GameObject Root { get; }
            public Transform Bullwheel { get; }
            public Transform Reducer { get; }
            public Light Light { get; }
        }
    }
}
