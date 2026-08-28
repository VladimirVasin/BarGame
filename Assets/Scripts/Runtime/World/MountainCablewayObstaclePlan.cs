using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// What part of the station a blocking box is. The world builder reads it
    /// to pick a surface; nothing about walking depends on it.
    /// </summary>
    public enum MountainCablewayObstacleKind
    {
        Pad = 0,
        Column = 1,
        ServiceHut = 2,
        FencePost = 3,
        FenceRail = 4,
        BoardingApron = 5,
        BoardingPlatform = 6,
        PlatformTread = 7,
        BullwheelPedestal = 8,
        BullwheelPedestalFoot = 9,
        MachineDeckProp = 10,
        TensionCarriage = 11
    }

    /// <summary>
    /// One solid box of a cableway station, in world space, with the station's
    /// own axes.
    /// </summary>
    public readonly struct MountainCablewayObstacle
    {
        internal MountainCablewayObstacle(
            MountainCablewayObstacleKind kind,
            string name,
            Vector3 localCenter,
            Vector3 size,
            Vector3 center,
            Vector3 right,
            Vector3 forward)
        {
            Kind = kind;
            Name = name ?? string.Empty;
            LocalCenter = localCenter;
            Size = size;
            Center = center;
            Right = right;
            Forward = forward;
        }

        public MountainCablewayObstacleKind Kind { get; }

        /// <summary>The name the world builder gives the object, and the one a
        /// test looks it up by.</summary>
        public string Name { get; }

        /// <summary>Centre in the STATION frame, which is what the builder
        /// places it at.</summary>
        public Vector3 LocalCenter { get; }

        public Vector3 Size { get; }

        /// <summary>Centre in world space, which is what the validator floods
        /// with.</summary>
        public Vector3 Center { get; }

        public Vector3 Right { get; }
        public Vector3 Forward { get; }

        /// <summary>The surface a person would end up standing on.</summary>
        public float TopY => Center.y + Size.y * 0.5f;

        /// <summary>
        /// Whether this is something a person stands ON rather than something
        /// they have to get past.
        ///
        /// It decides how the box is rasterized into a walk fill, and the
        /// distinction is not cosmetic. An obstruction has to be widened by
        /// the body that must pass it, or a `0.25 m` grid of POINTS walks
        /// through the `0.20 m` slot between the drive hut and the edge of
        /// the pad - which it did, and which kept the test that should have
        /// caught this bug green while the bug was in front of it. Widening a
        /// SURFACE the same way would be the opposite error: the strip would
        /// swallow the treads that climb it and wall off its own steps.
        /// </summary>
        public bool IsWalkableSurface =>
            Kind == MountainCablewayObstacleKind.Pad ||
            Kind == MountainCablewayObstacleKind.BoardingApron ||
            Kind == MountainCablewayObstacleKind.BoardingPlatform ||
            Kind == MountainCablewayObstacleKind.PlatformTread ||
            Kind == MountainCablewayObstacleKind.BullwheelPedestalFoot;

        /// <summary>The four XZ corners, in the same order and with the same
        /// meaning as a site part's, so one fill can rasterize both.</summary>
        public void GetFootprintCorners(Vector2[] corners)
        {
            GetFootprintCorners(corners, 0f);
        }

        /// <summary>The same four corners, pushed out by
        /// <paramref name="inflate"/> on every side.</summary>
        public void GetFootprintCorners(Vector2[] corners, float inflate)
        {
            if (corners == null)
            {
                throw new ArgumentNullException(nameof(corners));
            }

            if (corners.Length < 4)
            {
                throw new ArgumentException(
                    "Four corners are required.",
                    nameof(corners));
            }

            var right = new Vector2(Right.x, Right.z);
            var forward = new Vector2(Forward.x, Forward.z);
            var center = new Vector2(Center.x, Center.z);
            float halfX = Size.x * 0.5f + inflate;
            float halfZ = Size.z * 0.5f + inflate;
            corners[0] = center - right * halfX - forward * halfZ;
            corners[1] = center + right * halfX - forward * halfZ;
            corners[2] = center + right * halfX + forward * halfZ;
            corners[3] = center - right * halfX + forward * halfZ;
        }
    }

    /// <summary>
    /// Every solid box a cableway station puts on the ground, as a pure
    /// function of its plan.
    ///
    /// It exists because the station was invisible to every check the terminal
    /// has. `MountainRoadTerminalSiteValidator` already floods the summit with
    /// the player's own capsule and step offset, and its own comment says that
    /// neither the walkable mask nor any other validator would notice a piece
    /// of furniture cutting the yard in two - but the fill only ever walked
    /// `site.Parts`, and `MountainRoadTerminalSitePlanner` does not know the
    /// cableway exists. So the pad, the columns, the drive hut, the fence and
    /// the boarding strip were all holes in the map: the drive hut stood
    /// squarely across the only lane to the platform, the hero could not board
    /// at all, and the suite stayed green.
    ///
    /// One list, two readers. The world builder places these and gives them
    /// their colliders; the validator floods with them. Neither may hold a
    /// number the other does not.
    /// </summary>
    public static class MountainCablewayObstaclePlan
    {
        /// <summary>
        /// Where the drive's service hut stands, on the MACHINE side of the
        /// line.
        ///
        /// It was on `+3.25` - the boarding side - and its `2.1 m` body ran
        /// from `2.20` to `4.30` across exactly the lane the platform steps
        /// rise out of, leaving `0.20 m` to the pad's edge on one hand and the
        /// fence's end post on the other. A drive hut belongs with the drive:
        /// the reducer, the machine deck and the headframe are all on the
        /// other side, and the deck clears its roof by half a metre.
        /// </summary>
        public const float ServiceHutRightOffset = -3.25f;

        public const float ServiceHutForwardOffset = -1.62f;
        public static readonly Vector3 ServiceHutSize =
            new Vector3(2.1f, 2.48f, 2.0f);

        public const float ServiceHutFloorToRoof = 2.48f;

        public static IReadOnlyList<MountainCablewayObstacle> Create(
            MountainRoadCablewayPlan plan,
            MountainCablewayStationKind stationKind)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Vector3 center = plan.StationArea.Center;
            Vector3 right = plan.LineRight;
            Vector3 forward = plan.LineForward;
            Vector2 pad = plan.StationArea.Size;
            bool drive = stationKind == MountainCablewayStationKind.Drive;
            var boxes = new List<MountainCablewayObstacle>(24);

            void Add(
                MountainCablewayObstacleKind kind,
                string name,
                Vector3 localCenter,
                Vector3 size)
            {
                boxes.Add(new MountainCablewayObstacle(
                    kind,
                    name,
                    localCenter,
                    size,
                    center +
                    right * localCenter.x +
                    Vector3.up * localCenter.y +
                    forward * localCenter.z,
                    right,
                    forward));
            }

            Add(
                MountainCablewayObstacleKind.Pad,
                "Physical Concrete Station Pad",
                new Vector3(
                    0f,
                    MountainRoadCablewayPlan.StationPadTopY * 0.5f,
                    0f),
                new Vector3(
                    pad.x,
                    MountainRoadCablewayPlan.StationPadTopY,
                    pad.y));

            float columnRight = plan.StationColumnRightOffset;
            float columnForward = plan.StationColumnForwardOffset;
            for (int side = -1; side <= 1; side += 2)
            {
                for (int end = -1; end <= 1; end += 2)
                {
                    Add(
                        MountainCablewayObstacleKind.Column,
                        "Physical Station Column",
                        new Vector3(
                            side * columnRight,
                            MountainRoadCablewayPlan.StationColumnHeight *
                            0.5f,
                            end * columnForward),
                        new Vector3(
                            MountainRoadCablewayPlan.StationColumnThickness,
                            MountainRoadCablewayPlan.StationColumnHeight,
                            MountainRoadCablewayPlan.StationColumnThickness));
                }
            }

            AppendBoardingSide(plan, Add);

            Vector3 bullwheel = LocalBullwheel(plan);
            if (drive)
            {
                Add(
                    MountainCablewayObstacleKind.ServiceHut,
                    "Physical Drive Service Hut",
                    new Vector3(
                        ServiceHutRightOffset,
                        ServiceHutFloorToRoof * 0.5f +
                        MountainRoadCablewayPlan.StationPadTopY - 0.08f,
                        ServiceHutForwardOffset),
                    ServiceHutSize);

                float pedestalTop = bullwheel.y - 0.34f;
                Add(
                    MountainCablewayObstacleKind.BullwheelPedestal,
                    "Physical Bullwheel Pedestal",
                    new Vector3(bullwheel.x, pedestalTop * 0.5f, bullwheel.z),
                    new Vector3(0.48f, pedestalTop, 0.48f));

                // The foot is `0.22 m` proud and a person steps over it. It is
                // in the list so the world and the fill agree about a thing
                // that is there, not because it stops anybody.
                Add(
                    MountainCablewayObstacleKind.BullwheelPedestalFoot,
                    "Bullwheel Pedestal Foot",
                    new Vector3(bullwheel.x, 0.11f, bullwheel.z),
                    new Vector3(1.15f, 0.22f, 1.15f));

                float deckTop = bullwheel.y - 0.91f;
                Add(
                    MountainCablewayObstacleKind.MachineDeckProp,
                    "Physical Machine Deck Prop",
                    new Vector3(-0.9f, (deckTop - 0.16f) * 0.5f, 1.9f),
                    new Vector3(0.2f, deckTop - 0.16f, 0.2f));
            }
            else
            {
                Add(
                    MountainCablewayObstacleKind.TensionCarriage,
                    "Physical Tension Carriage",
                    new Vector3(
                        bullwheel.x,
                        bullwheel.y - 0.62f,
                        -columnForward * 0.72f),
                    new Vector3(1.35f, 0.34f, 1.05f));
            }

            return boxes;
        }

        /// <summary>The bullwheel's own place in the station frame - which is
        /// `4.5 m` FORWARD of the pad's centre, and the reason the boarding
        /// strip needs an apron under it.</summary>
        public static Vector3 LocalBullwheel(MountainRoadCablewayPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            Vector3 delta = plan.LowerCableCenter - plan.StationArea.Center;
            return new Vector3(
                Vector3.Dot(delta, plan.LineRight),
                delta.y,
                Vector3.Dot(delta, plan.LineForward));
        }

        /// <summary>
        /// The apron, the fence, the steps and the strip - everything the
        /// boarding side is made of, at both terminals.
        /// </summary>
        private static void AppendBoardingSide(
            MountainRoadCablewayPlan plan,
            Action<MountainCablewayObstacleKind, string, Vector3, Vector3> add)
        {
            float apronTop = MountainRoadCablewayPlan.StationPadTopY;
            float apronBottom = -0.1f;
            float apronInner = plan.BoardingApronInnerOffset;
            float apronOuter = plan.BoardingApronOuterOffset;
            float apronNear = plan.BoardingApronNearForward;
            float apronFar = plan.BoardingApronFarForward;
            add(
                MountainCablewayObstacleKind.BoardingApron,
                "Physical Boarding Apron",
                new Vector3(
                    (apronInner + apronOuter) * 0.5f,
                    (apronBottom + apronTop) * 0.5f,
                    (apronNear + apronFar) * 0.5f),
                new Vector3(
                    apronOuter - apronInner,
                    apronTop - apronBottom,
                    apronFar - apronNear));

            float jamb = plan.BoardingGateJambOffset;
            float[] posts =
            {
                MountainRoadCablewayPlan.BoardingFenceLeftEndOffset,
                -0.8f,
                0.8f,
                jamb
            };
            for (int index = 0; index < posts.Length; index++)
            {
                add(
                    MountainCablewayObstacleKind.FencePost,
                    "Physical Boarding Post",
                    new Vector3(
                        posts[index],
                        MountainRoadCablewayPlan.BoardingFencePostHeight *
                        0.5f + 0.045f,
                        plan.BoardingFenceForward),
                    new Vector3(
                        MountainRoadCablewayPlan.BoardingFencePostThickness,
                        MountainRoadCablewayPlan.BoardingFencePostHeight,
                        MountainRoadCablewayPlan
                            .BoardingFencePostThickness));
            }

            // Three bays now, not two: the old opening was on the CENTRE line
            // and the boarding strip is nowhere near it, so leaving it there
            // would be a second way through the fence leading to the drive.
            for (int bay = 0; bay + 1 < posts.Length; bay++)
            {
                float from = posts[bay];
                float to = posts[bay + 1];
                for (int rail = 0; rail < 2; rail++)
                {
                    add(
                        MountainCablewayObstacleKind.FenceRail,
                        "Physical Boarding Rail",
                        new Vector3(
                            (from + to) * 0.5f,
                            0.66f + rail * 0.58f,
                            plan.BoardingFenceForward),
                        new Vector3(
                            to - from,
                            MountainRoadCablewayPlan.BoardingFenceRailThickness,
                            MountainRoadCablewayPlan
                                .BoardingFenceRailThickness));
                }
            }

            float top = plan.BoardingPlatformLocalTop;
            if (top <= 0.18f)
            {
                // The pad already stands at the right height for this line; a
                // strip would be a lip to trip over rather than a step.
                return;
            }

            float centerX = plan.BoardingPlatformCenterOffset;
            float width = plan.BoardingPlatformWidth;
            add(
                MountainCablewayObstacleKind.BoardingPlatform,
                "Physical Boarding Platform",
                new Vector3(
                    centerX,
                    top * 0.5f,
                    (plan.BoardingPlatformNearForward +
                     plan.BoardingPlatformFarForward) * 0.5f),
                new Vector3(width, top, plan.BoardingPlatformLength));

            int treadCount = MountainRoadCablewayPlan.BoardingTreadCount;
            float treadDepth = MountainRoadCablewayPlan.BoardingTreadDepth;
            for (int tread = 0; tread < treadCount; tread++)
            {
                float height = top * (tread + 1) / (treadCount + 1);
                add(
                    MountainCablewayObstacleKind.PlatformTread,
                    "Physical Platform Tread",
                    new Vector3(
                        centerX,
                        height * 0.5f,
                        plan.BoardingStepsNearForward +
                        treadDepth * (tread + 0.5f)),
                    new Vector3(width, height, treadDepth));
            }
        }
    }
}
