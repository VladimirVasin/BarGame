using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Proves the dressed summit is still a place a car can park on and a
    /// person can cross.
    ///
    /// It collects every violation and reports them together rather than
    /// throwing on the first. A site is authored as one composition, and a
    /// validator that names one corner at a time turns that into a queue.
    ///
    /// The connectivity pass is the one that earns its keep. The retaining
    /// wall is the first thing in this world able to cut the terminal in
    /// two, and neither the walkable mask — which is a polygon and knows
    /// nothing about furniture — nor any existing validator would notice.
    /// It walks heights rather than a blocking flag, so a wall stops the
    /// fill and the three risers through it do not, which is exactly the
    /// distinction the player's own step offset makes.
    /// </summary>
    public static class MountainRoadTerminalSiteValidator
    {
        private const float CapsuleRadius = 0.32f;
        private const float StepOffset = 0.28f;
        private const float FillCell = 0.25f;
        private const float CableHeadroom = 2f;

        /// <summary>
        /// How much room an approach keeps on each side of its own
        /// width, and how far out it holds. A door the hero has to
        /// aim at is a door the dressing has spoiled.
        /// </summary>
        private const float ApproachSideMargin = 0.8f;

        /// <summary>
        /// A door is walked at; a seat is only walked TO. Its offer docks
        /// less than a metre in front of it, and what lies beyond that is
        /// usually the thing the seat was put there to face - the parapet
        /// in front of the brink bench is not in the way of it, it is the
        /// point of it.
        /// </summary>
        private const float DoorApproachDepth = 3f;

        private const float SeatApproachDepth = 1f;
        private const float ApproachClearance = 0.4f;

        /// <summary>
        /// How far a dock check may look for a cell it will accept. One cell
        /// each way - `0.25 m` - because the boarding strip is only `1.37 m`
        /// wide and the generous four-cell window the yard checks use would
        /// let ground OFF the strip answer for it.
        /// </summary>
        private const int DockSearchCells = 1;

        public static void ValidateOrThrow(MountainRoadPlan plan)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadTerminalSitePlan site = plan.Terminal.Site;
            if (site == null)
            {
                throw new InvalidOperationException(
                    "The terminal needs its site plan.");
            }

            MountainRoadPlateauDescriptor plateau = plan.Plateau;
            MountainRoadTerminalPlan terminal = plan.Terminal;
            var problems = new List<string>();
            if (site.Parts.Count == 0 ||
                site.Parts.Count > MountainRoadTerminalSitePlan
                    .MaximumPartCount)
            {
                problems.Add(
                    $"the site has {site.Parts.Count} parts against a " +
                    $"{MountainRoadTerminalSitePlan.MaximumPartCount} " +
                    "ceiling.");
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            var corners = new Vector2[4];
            float protectedRadius =
                terminal.VehicleApron.TurningRadius + 0.55f;
            var apronCenter = new Vector2(
                terminal.VehicleApron.Center.x,
                terminal.VehicleApron.Center.z);

            for (int index = 0; index < site.Parts.Count; index++)
            {
                MountainRoadSitePartDescriptor part = site.Parts[index];
                if (string.IsNullOrEmpty(part.StableId) ||
                    !ids.Add(part.StableId))
                {
                    problems.Add(
                        $"'{part.StableId}': ID is empty or repeated.");
                }

                if (part.Size.x <= 0f ||
                    part.Size.y <= 0f ||
                    part.Size.z <= 0f)
                {
                    problems.Add($"'{part.StableId}': has no size.");
                }

                part.GetFootprintCorners(corners);
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector2 point = corners[corner];

                    // The cut face is the mountain, not the yard: it is the
                    // one group allowed to stand outside the polygon.
                    if (part.Group != MountainRoadSiteGroup.RockCut &&
                        !plateau.Contains(point))
                    {
                        float over = DistanceToPolygonEdge(
                            plateau.VerticesXZ,
                            point);
                        problems.Add(
                            $"'{part.StableId}': corner {corner} is " +
                            $"{over:0.00} m outside the plateau polygon.");
                    }

                    float toApron = Vector2.Distance(point, apronCenter);
                    if (toApron < protectedRadius)
                    {
                        problems.Add(
                            $"'{part.StableId}': corner {corner} is " +
                            $"{toApron:0.00} m from the apron centre, " +
                            $"inside the {protectedRadius:0.00} m the car " +
                            "turns in.");
                    }

                    if (terminal.Cafe.ContainsInterior(
                            new Vector3(
                                point.x,
                                terminal.Cafe.FloorY + 0.5f,
                                point.y),
                            0f))
                    {
                        problems.Add(
                            $"'{part.StableId}': corner {corner} stands " +
                            "inside the cafe.");
                    }

                    if (terminal.Cableway.StationArea.ContainsXZ(
                            new Vector3(point.x, 0f, point.y),
                            0f))
                    {
                        problems.Add(
                            $"'{part.StableId}': corner {corner} stands " +
                            "inside the cable station.");
                    }
                }

                CheckCableHeadroom(terminal.Cableway, part, problems);
            }

            CheckParapetInsideTheRim(plateau, site, problems);
            CheckApproachesStayClear(terminal, site, problems);
            CheckTheYardStaysOnePlace(plateau, terminal, site, problems);

            if (problems.Count == 0)
            {
                return;
            }

            throw new InvalidOperationException(
                $"The terminal site has {problems.Count} problems:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, problems));
        }

        /// <summary>
        /// Nothing on the summit may reach the haul line. The rock cut is
        /// the piece that could: it runs the rim the cable leaves over.
        /// </summary>
        private static void CheckCableHeadroom(
            MountainRoadCablewayPlan cableway,
            MountainRoadSitePartDescriptor part,
            ICollection<string> problems)
        {
            Vector3 lower = cableway.Nodes[0].CableCenter;
            Vector3 forward = cableway.LineForward;
            Vector3 delta = part.Center - lower;
            float along = Vector3.Dot(delta, forward);
            if (along < 0f || along > cableway.LineLength)
            {
                return;
            }

            float lateral = Mathf.Abs(
                Vector3.Dot(delta, cableway.LineRight));
            float halfSpan = Mathf.Max(part.Size.x, part.Size.z) * 0.5f +
                             cableway.TrackSeparation * 0.5f +
                             cableway.CabinSize.x * 0.5f;
            if (lateral > halfSpan)
            {
                return;
            }

            float cableY = SampleCableHeight(cableway, along);
            float partTop = part.Center.y + part.Size.y * 0.5f;
            if (partTop > cableY - CableHeadroom)
            {
                problems.Add(
                    $"'{part.StableId}': reaches {partTop:0.00} m under a " +
                    $"haul cable at {cableY:0.00} m.");
            }
        }

        private static float SampleCableHeight(
            MountainRoadCablewayPlan cableway,
            float distance)
        {
            IReadOnlyList<MountainCablewayNodeDescriptor> nodes =
                cableway.Nodes;
            for (int index = 1; index < nodes.Count; index++)
            {
                if (distance > nodes[index].Distance)
                {
                    continue;
                }

                float span = nodes[index].Distance -
                             nodes[index - 1].Distance;
                float t = span <= 0.0001f
                    ? 0f
                    : (distance - nodes[index - 1].Distance) / span;
                return Mathf.Lerp(
                    nodes[index - 1].CableCenter.y,
                    nodes[index].CableCenter.y,
                    t);
            }

            return nodes[nodes.Count - 1].CableCenter.y;
        }

        /// <summary>
        /// The parapet has to be reachable and it has to be the thing that
        /// stops the hero. If its inner face sat closer to the rim than the
        /// mask's own clamp, the clamp would stop him first and the wall
        /// would be scenery in front of an invisible one.
        /// </summary>
        private static void CheckParapetInsideTheRim(
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalSitePlan site,
            ICollection<string> problems)
        {
            bool found = false;
            var corners = new Vector2[4];
            for (int index = 0; index < site.Parts.Count; index++)
            {
                MountainRoadSitePartDescriptor part = site.Parts[index];
                if (!part.StableId.StartsWith(
                        "site-parapet-wall-",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                found = true;
                part.GetFootprintCorners(corners);
                for (int corner = 0; corner < 4; corner++)
                {
                    float distance = DistanceToPolygonEdge(
                        plateau.VerticesXZ,
                        corners[corner]);
                    if (distance < CapsuleRadius)
                    {
                        problems.Add(
                            $"'{part.StableId}': corner {corner} stands " +
                            $"{distance:0.00} m from the rim, inside the " +
                            $"{CapsuleRadius:0.00} m the mask already " +
                            "clamps.");
                    }
                }
            }

            if (!found)
            {
                problems.Add("the brink has no parapet.");
            }
        }

        /// <summary>
        /// Keeps the ways in clear of the dressing.
        ///
        /// The flood fill below proves the yard is CONNECTED, and that is a
        /// different question: it walks a `0.25 m` grid with no capsule
        /// inflation, so a two-cell slot between a bin and a snow bank
        /// reads as passable when nobody could walk it. This measures the
        /// approaches themselves - the cafe door and both seat docks -
        /// against the width a body actually needs.
        /// </summary>
        private static void CheckApproachesStayClear(
            MountainRoadTerminalPlan terminal,
            MountainRoadTerminalSitePlan site,
            ICollection<string> problems)
        {
            CheckOneApproach(
                site,
                terminal.Cafe.DoorCenter,
                terminal.Cafe.DoorForward,
                terminal.Cafe.DoorWidth,
                -0.1f,
                DoorApproachDepth,
                "the cafe door",
                problems);
            CheckSeatApproach(
                site,
                site.BrinkSeat,
                "the brink bench",
                problems);
            CheckSeatApproach(
                site,
                site.CounterSeat,
                "the counter stool",
                problems);
        }

        /// <summary>
        /// A seat's approach starts past its own timber. Measured from the
        /// seat centre it would report the bench's own legs and planks as
        /// blocking the way to the bench, which is true and useless.
        /// </summary>
        private static void CheckSeatApproach(
            MountainRoadTerminalSitePlan site,
            MountainRoadSiteSeatDescriptor seat,
            string label,
            ICollection<string> problems)
        {
            float near = seat.SeatDepth * 0.5f + 0.12f;
            CheckOneApproach(
                site,
                seat.SeatTopCenter,
                seat.ApproachDirection,
                seat.SeatWidth,
                near,
                near + SeatApproachDepth,
                label,
                problems);
        }

        private static void CheckOneApproach(
            MountainRoadTerminalSitePlan site,
            Vector3 mouth,
            Vector3 outward,
            float width,
            float nearOffset,
            float farOffset,
            string label,
            ICollection<string> problems)
        {
            Vector3 forward = outward;
            forward.y = 0f;
            forward = forward.normalized;
            var lateral = new Vector3(forward.z, 0f, -forward.x);
            float halfWidth = width * 0.5f + ApproachSideMargin;
            var corners = new Vector2[4];

            for (int index = 0; index < site.Parts.Count; index++)
            {
                MountainRoadSitePartDescriptor part = site.Parts[index];
                if (!part.BlocksMovement)
                {
                    continue;
                }

                part.GetFootprintCorners(corners);
                for (int corner = 0; corner < 4; corner++)
                {
                    Vector3 point = new Vector3(
                        corners[corner].x,
                        mouth.y,
                        corners[corner].y) - mouth;
                    float along = Vector3.Dot(point, forward);
                    float across = Mathf.Abs(Vector3.Dot(point, lateral));
                    if (along < nearOffset ||
                        along > farOffset ||
                        across > halfWidth + ApproachClearance)
                    {
                        continue;
                    }

                    problems.Add(
                        $"'{part.StableId}': corner {corner} stands " +
                        $"{across:0.00} m off the centreline and " +
                        $"{along:0.00} m out from {label}, inside the " +
                        $"{halfWidth:0.00} m half-width it keeps from " +
                        $"{nearOffset:0.00} m to {farOffset:0.00} m out.");
                }
            }
        }

        /// <summary>
        /// One flood fill from the mouth of the road. Heights, not flags:
        /// a cell is reachable from its neighbour when the step between
        /// them is one the player's own controller would take.
        /// </summary>
        private static void CheckTheYardStaysOnePlace(
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalPlan terminal,
            MountainRoadTerminalSitePlan site,
            ICollection<string> problems)
        {
            bool[,] reached = BuildReachability(
                plateau,
                terminal,
                site,
                out Rect bounds,
                out bool[,] open,
                out float[,] height);
            if (reached == null)
            {
                problems.Add("the road mouth is not open ground.");
                return;
            }

            CheckReached(
                bounds,
                reached,
                open,
                terminal.Cafe.DoorCenter +
                terminal.Cafe.DoorForward * 1.2f,
                "the cafe doorstep",
                problems);
            CheckReached(
                bounds,
                reached,
                open,
                terminal.Cableway.StationArea.Center -
                terminal.Cableway.LineForward * 5.4f,
                "the cable station",
                problems);
            CheckReached(
                bounds,
                reached,
                open,
                MountainRoadTerminalPlanner.LocalToWorld(
                    plateau,
                    MountainRoadTerminalSitePlanner.TerraceLeftRight + 4f,
                    0f,
                    MountainRoadTerminalSitePlanner.TerraceRimForward -
                    1.4f),
                "the terrace",
                problems);

            // The yard in front of the station is not the platform. That check
            // above aims `5.4 m` SHORT of the station centre and passed
            // happily while the drive hut stood across the only lane to the
            // boarding strip and the cabin could not be entered at all. This
            // one lands on the dock itself and demands the cell be at the
            // strip's own height, because a reachable cell a metre away at pad
            // level proves nothing about a platform you cannot climb onto.
            if (!IsStandableAt(
                    bounds,
                    reached,
                    open,
                    height,
                    terminal.Cableway.BoardingDockPosition,
                    DockSearchCells))
            {
                problems.Add(
                    "the site cut the cableway boarding platform off from " +
                    "the arrival.");
            }
        }

        /// <summary>
        /// Whether a person who walked in off the road can stand at
        /// <paramref name="target"/> - at the height the target itself is at.
        ///
        /// The same fill the site validation runs, exposed so a test can ask
        /// the question directly rather than reading it out of an exception
        /// message.
        /// </summary>
        public static bool CanWalkTo(MountainRoadPlan plan, Vector3 target)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            MountainRoadTerminalSitePlan site = plan.Terminal.Site;
            if (site == null)
            {
                throw new InvalidOperationException(
                    "The terminal needs its site plan.");
            }

            bool[,] reached = BuildReachability(
                plan.Plateau,
                plan.Terminal,
                site,
                out Rect bounds,
                out bool[,] open,
                out float[,] height);
            return reached != null &&
                   IsStandableAt(
                       bounds,
                       reached,
                       open,
                       height,
                       target,
                       DockSearchCells);
        }

        /// <summary>
        /// One flood fill from the mouth of the road. Heights, not flags:
        /// a cell is reachable from its neighbour when the step between
        /// them is one the player's own controller would take.
        ///
        /// It walks the site's parts AND the cableway station's, which for a
        /// long time it did not: `MountainRoadTerminalSitePlanner` has no idea
        /// the cableway exists, so the pad, the columns, the drive hut, the
        /// fence and the whole boarding strip were holes in this map.
        /// </summary>
        private static bool[,] BuildReachability(
            MountainRoadPlateauDescriptor plateau,
            MountainRoadTerminalPlan terminal,
            MountainRoadTerminalSitePlan site,
            out Rect bounds,
            out bool[,] open,
            out float[,] height)
        {
            bounds = plateau.BoundsXZ;
            int columns = Mathf.CeilToInt(bounds.width / FillCell) + 1;
            int rows = Mathf.CeilToInt(bounds.height / FillCell) + 1;
            open = new bool[columns, rows];
            height = new float[columns, rows];
            var corners = new Vector2[4];

            for (int column = 0; column < columns; column++)
            {
                for (int row = 0; row < rows; row++)
                {
                    Vector2 point = CellPoint(bounds, column, row);
                    open[column, row] =
                        plateau.Contains(point) &&
                        DistanceToPolygonEdge(plateau.VerticesXZ, point) >=
                        CapsuleRadius &&
                        !terminal.Cafe.ContainsInterior(
                            new Vector3(
                                point.x,
                                terminal.Cafe.FloorY + 0.5f,
                                point.y),
                            0f);
                    height[column, row] = site.YardTopY;
                }
            }

            for (int index = 0; index < site.Parts.Count; index++)
            {
                MountainRoadSitePartDescriptor part = site.Parts[index];
                if (!part.BlocksMovement)
                {
                    continue;
                }

                part.GetFootprintCorners(corners);
                RaiseFootprint(
                    bounds,
                    height,
                    corners,
                    part.Center.y + part.Size.y * 0.5f);
            }

            IReadOnlyList<MountainCablewayObstacle> station =
                MountainCablewayObstaclePlan.Create(
                    terminal.Cableway,
                    MountainCablewayStationKind.Drive);
            for (int index = 0; index < station.Count; index++)
            {
                // An obstruction is widened by the capsule that has to get
                // past it; a surface is rasterized exactly. Without that, the
                // fill is a POINT and it walks the `0.20 m` slot between the
                // drive hut and the edge of the pad - which is not a gap
                // anybody fits through, and which kept this very check green
                // while the boarding lane was blocked.
                station[index].GetFootprintCorners(
                    corners,
                    station[index].IsWalkableSurface ? 0f : CapsuleRadius);
                RaiseFootprint(
                    bounds,
                    height,
                    corners,
                    station[index].TopY);
            }

            // A stride inside the mouth, because the entry sample sits ON
            // the polygon edge and the mask holds a capsule off it.
            Vector3 inside = terminal.VehicleApron.EntryCenter +
                             terminal.VehicleApron.Forward * 1.5f;
            var mouth = new Vector2(inside.x, inside.z);
            var start = new Vector2Int(
                Mathf.Clamp(
                    Mathf.RoundToInt((mouth.x - bounds.xMin) / FillCell),
                    0,
                    columns - 1),
                Mathf.Clamp(
                    Mathf.RoundToInt((mouth.y - bounds.yMin) / FillCell),
                    0,
                    rows - 1));
            if (!open[start.x, start.y])
            {
                return null;
            }

            var reached = new bool[columns, rows];
            var queue = new Queue<Vector2Int>();
            reached[start.x, start.y] = true;
            queue.Enqueue(start);
            Vector2Int[] steps =
            {
                new Vector2Int(1, 0),
                new Vector2Int(-1, 0),
                new Vector2Int(0, 1),
                new Vector2Int(0, -1)
            };
            while (queue.Count > 0)
            {
                Vector2Int cell = queue.Dequeue();
                for (int index = 0; index < steps.Length; index++)
                {
                    Vector2Int next = cell + steps[index];
                    if (next.x < 0 ||
                        next.y < 0 ||
                        next.x >= columns ||
                        next.y >= rows ||
                        reached[next.x, next.y] ||
                        !open[next.x, next.y])
                    {
                        continue;
                    }

                    if (Mathf.Abs(
                            height[next.x, next.y] -
                            height[cell.x, cell.y]) > StepOffset)
                    {
                        continue;
                    }

                    reached[next.x, next.y] = true;
                    queue.Enqueue(next);
                }
            }

            return reached;
        }

        private static void RaiseFootprint(
            Rect bounds,
            float[,] height,
            Vector2[] corners,
            float top)
        {
            int columns = height.GetLength(0);
            int rows = height.GetLength(1);
            Rect footprint = Enclose(corners);
            int minColumn = Mathf.Max(
                0,
                Mathf.FloorToInt((footprint.xMin - bounds.xMin) / FillCell));
            int maxColumn = Mathf.Min(
                columns - 1,
                Mathf.CeilToInt((footprint.xMax - bounds.xMin) / FillCell));
            int minRow = Mathf.Max(
                0,
                Mathf.FloorToInt((footprint.yMin - bounds.yMin) / FillCell));
            int maxRow = Mathf.Min(
                rows - 1,
                Mathf.CeilToInt((footprint.yMax - bounds.yMin) / FillCell));
            for (int column = minColumn; column <= maxColumn; column++)
            {
                for (int row = minRow; row <= maxRow; row++)
                {
                    Vector2 point = CellPoint(bounds, column, row);
                    if (!ContainsXZ(corners, point))
                    {
                        continue;
                    }

                    height[column, row] = Mathf.Max(height[column, row], top);
                }
            }
        }

        /// <summary>
        /// A reachable cell close to the target AND at the target's own
        /// standing height. The height half is what stops a cell on the yard
        /// beside a platform from vouching for the platform.
        /// </summary>
        private static bool IsStandableAt(
            Rect bounds,
            bool[,] reached,
            bool[,] open,
            float[,] height,
            Vector3 target,
            int searchCells)
        {
            int columns = reached.GetLength(0);
            int rows = reached.GetLength(1);
            int centreColumn = Mathf.RoundToInt(
                (target.x - bounds.xMin) / FillCell);
            int centreRow = Mathf.RoundToInt(
                (target.z - bounds.yMin) / FillCell);
            for (int column = centreColumn - searchCells;
                 column <= centreColumn + searchCells;
                 column++)
            {
                for (int row = centreRow - searchCells;
                     row <= centreRow + searchCells;
                     row++)
                {
                    if (column < 0 ||
                        row < 0 ||
                        column >= columns ||
                        row >= rows)
                    {
                        continue;
                    }

                    if (open[column, row] &&
                        reached[column, row] &&
                        Mathf.Abs(height[column, row] - target.y) <=
                        StepOffset)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void CheckReached(
            Rect bounds,
            bool[,] reached,
            bool[,] open,
            Vector3 target,
            string label,
            ICollection<string> problems)
        {
            int columns = reached.GetLength(0);
            int rows = reached.GetLength(1);
            int centreColumn = Mathf.RoundToInt(
                (target.x - bounds.xMin) / FillCell);
            int centreRow = Mathf.RoundToInt(
                (target.z - bounds.yMin) / FillCell);
            for (int column = centreColumn - 4;
                 column <= centreColumn + 4;
                 column++)
            {
                for (int row = centreRow - 4; row <= centreRow + 4; row++)
                {
                    if (column < 0 ||
                        row < 0 ||
                        column >= columns ||
                        row >= rows)
                    {
                        continue;
                    }

                    if (open[column, row] && reached[column, row])
                    {
                        return;
                    }
                }
            }

            problems.Add($"the site cut {label} off from the arrival.");
        }

        private static Vector2 CellPoint(Rect bounds, int column, int row)
        {
            return new Vector2(
                bounds.xMin + column * FillCell,
                bounds.yMin + row * FillCell);
        }

        private static Rect Enclose(Vector2[] corners)
        {
            float xMin = corners[0].x;
            float xMax = xMin;
            float yMin = corners[0].y;
            float yMax = yMin;
            for (int index = 1; index < corners.Length; index++)
            {
                xMin = Mathf.Min(xMin, corners[index].x);
                xMax = Mathf.Max(xMax, corners[index].x);
                yMin = Mathf.Min(yMin, corners[index].y);
                yMax = Mathf.Max(yMax, corners[index].y);
            }

            return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
        }

        private static bool ContainsXZ(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int first = 0, second = polygon.Length - 1;
                 first < polygon.Length;
                 second = first++)
            {
                Vector2 a = polygon[first];
                Vector2 b = polygon[second];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) *
                    (point.y - a.y) /
                    ((b.y - a.y) + Mathf.Epsilon) + a.x;
                if (crosses)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static float DistanceToPolygonEdge(
            IReadOnlyList<Vector2> polygon,
            Vector2 point)
        {
            float best = float.PositiveInfinity;
            for (int index = 0; index < polygon.Count; index++)
            {
                Vector2 first = polygon[index];
                Vector2 second = polygon[(index + 1) % polygon.Count];
                Vector2 segment = second - first;
                float denominator = segment.sqrMagnitude;
                float t = denominator <= 0.0001f
                    ? 0f
                    : Mathf.Clamp01(
                        Vector2.Dot(point - first, segment) / denominator);
                best = Mathf.Min(
                    best,
                    Vector2.Distance(point, first + segment * t));
            }

            return best;
        }
    }
}
