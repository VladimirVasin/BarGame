using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One park table the hero can actually sit down and play at: the
    /// free plank, the game laid out on it, the lattice frame the men
    /// stand in and the pose his eyes take once he is on the timber.
    ///
    /// Everything here is derived from the drawn set rather than
    /// authored beside it. The seat comes out of the decoration recipe,
    /// the lattice out of `CityChessBoardGeometry`, and the eye out of
    /// the seat — so a table that moves takes its game with it and a
    /// board that is redrawn cannot leave the camera pointing at grass.
    /// </summary>
    public readonly struct CityBoardGameTable
    {
        public CityBoardGameTable(
            string seatId,
            CityBoardGameKind game,
            int table,
            Vector3 origin,
            Vector3 tangent,
            Vector3 forward,
            Vector3 seatTopCenter,
            Vector3 seatFacing)
        {
            SeatId = seatId;
            Game = game;
            Table = table;
            Origin = origin;
            Tangent = tangent;
            Forward = forward;
            SeatTopCenter = seatTopCenter;
            SeatFacing = seatFacing;
            IsPresent = !string.IsNullOrEmpty(seatId);
        }

        /// <summary>The plank the hero takes, in the shared bench
        /// registry's own id space.</summary>
        public string SeatId { get; }

        public CityBoardGameKind Game { get; }

        /// <summary>`-1` for the chess table, `+1` for the draughts
        /// one, matching the recipe's own two halves.</summary>
        public int Table { get; }

        public Vector3 Origin { get; }
        public Vector3 Tangent { get; }
        public Vector3 Forward { get; }
        public Vector3 SeatTopCenter { get; }
        public Vector3 SeatFacing { get; }
        public bool IsPresent { get; }

        /// <summary>
        /// The hero is dark at both boards, because at both boards the
        /// side nearest the free plank is the dark one. That is not a
        /// balance decision: it is what the set already had on it when
        /// he walked up, and it means the old man opposite always moves
        /// first.
        /// </summary>
        public BoardGameSide HeroSide => BoardGameSide.Dark;

        /// <summary>The middle of one square, at the surface a man on
        /// it stands on.</summary>
        public Vector3 SquareCenter(int file, int rank)
        {
            return CityChessBoardGeometry.SquareCenterWorld(
                Origin,
                Tangent,
                Forward,
                Table,
                file,
                rank);
        }

        /// <summary>The middle of the whole board, at its surface.</summary>
        public Vector3 BoardCenter =>
            Origin +
            Tangent * (Table * CityChessBoardGeometry.TableOffsetMeters) +
            Vector3.up * CityChessBoardGeometry.LightTopY;
    }

    /// <summary>
    /// Both playable tables of the park chess set, and the seated
    /// first-person pose each one hands the hero.
    ///
    /// The eye numbers are geometry rather than taste, and the geometry
    /// is tight: the board is `1.2 m` across, the plank sits `1.10 m`
    /// from its middle and a seated man's eyes are less than half a
    /// metre above the stone. The whole playing field has to fit inside
    /// a `16:9` frame from there, which is what the field of view and
    /// the forward step below are solving.
    ///
    /// The eye began exactly on the head bone, which put the lens
    /// inside the skull and the hero's own chest across the bottom
    /// third of the frame. It now stands over and in front of that
    /// bone, leaning in over the stone the way somebody studying a
    /// board does. Only the head is ever hidden — the body is never
    /// touched, it simply falls below the frame at the resting angle
    /// and comes back the moment he looks down. See
    /// <see cref="Player3DHeadVisibility"/>.
    ///
    /// The pitch is not authored. It is the angular bisector of the
    /// board's near and far edges from wherever the eye ends up, so
    /// the field is centred in the frame by construction and moving
    /// the eye can never leave it hanging off one end.
    /// </summary>
    public static class CityBoardGamePlan
    {
        /// <summary>The chess table's far plank. The old chess player
        /// holds `-seat-a1` and this is the one across from him.</summary>
        public const string ChessSeatIdSuffix = "-seat-a2";

        /// <summary>And the draughts table's near plank, across from
        /// the man on `-seat-b2`.</summary>
        public const string DraughtsSeatIdSuffix = "-seat-b1";

        /// <summary>
        /// How far the hero's view stands over the plank he is sitting
        /// on. The head bone of the seated rig is at `0.76 m`, so this
        /// is well above it: a lens at the middle of a skull is a lens
        /// inside a skull, and a board `1.2 m` across seen from `0.3 m`
        /// above it is a board nobody can read. This is a man leaning
        /// right over the stone at his position, and these two
        /// constants are the only ones to touch if that lean wants to
        /// be more or less — the pitch follows them.
        ///
        /// There is a ceiling on it, found by rendering: past roughly
        /// `1.12 m` and `0.38 m` the shot stops being a park and starts
        /// being a chess application. The pieces flatten, the man
        /// opposite thins to a sliver at the top edge and the perspective
        /// that made it a board on a table in a city goes with him.
        /// </summary>
        public const float EyeHeightAboveSeat = 1.06f;

        /// <summary>
        /// And how far in front of him, along the plank's own facing.
        /// It moves with the height rather than independently: leaning
        /// up without leaning in would look down at the near rank from
        /// a ladder. It also puts the lens clear of his own chest,
        /// which at `0.06 m` was a flat wall across the bottom third of
        /// the frame.
        /// </summary>
        public const float EyeForwardMeters = 0.34f;

        /// <summary>Where the board's surface sits below the eye, and
        /// how far the near and far edges of the field are from it
        /// along the ground. Everything the framing needs.</summary>
        public static float EyeAboveBoardMeters =>
            EyeHeightAboveSeat +
            CityChessBoardGeometry.BenchSeatTopY -
            CityChessBoardGeometry.LightTopY;

        public static float NearEdgeDistanceMeters =>
            CityChessBoardGeometry.BenchCenterZMeters -
            EyeForwardMeters -
            CityChessBoardGeometry.FieldSizeMeters * 0.5f;

        public static float FarEdgeDistanceMeters =>
            CityChessBoardGeometry.BenchCenterZMeters -
            EyeForwardMeters +
            CityChessBoardGeometry.FieldSizeMeters * 0.5f;

        /// <summary>
        /// How far down he looks before he moves his head: exactly the
        /// angle that bisects the board. Derived rather than authored,
        /// so the field stays centred in the frame however the eye is
        /// moved — the near edge of a board this close subtends far
        /// more angle than the far one, and an eye-level guess at the
        /// midpoint is wrong by a dozen degrees every time.
        /// </summary>
        public static float BasePitchDegrees =>
            0.5f *
            (Mathf.Atan2(EyeAboveBoardMeters, NearEdgeDistanceMeters) +
             Mathf.Atan2(EyeAboveBoardMeters, FarEdgeDistanceMeters)) *
            Mathf.Rad2Deg;

        /// <summary>
        /// Wide, and it has to be. From `0.40 m` behind the near edge
        /// of a `1.2 m` board the near corners sit `50` degrees off the
        /// axis; at `68` they were `2%` inside a `16:9` frame, which is
        /// not a margin, it is a coincidence. At `72` they are `10%`
        /// inside, and the extra angle is spent on the hero's own arms
        /// at the bottom of the frame rather than wasted.
        /// </summary>
        public const float FieldOfView = 72f;

        /// <summary>
        /// How far he may turn his head on the plank, and how far up
        /// and down.
        ///
        /// The up limit is small on purpose. The man opposite is only
        /// `2.2 m` away with his head slightly below the hero's own eye
        /// line, so six degrees is what it takes to put his face in the
        /// middle of the frame. A wider limit was tried and it looked
        /// at the tops of the park trees. Down reaches past the near
        /// edge of the board to his own arms on the stone.
        /// </summary>
        public const float MaximumYawOffsetDegrees = 55f;
        public const float MinimumPitchDegrees = -6f;
        public const float MaximumPitchDegrees = 75f;

        /// <summary>
        /// Reads both playable tables out of the generated city, or
        /// returns an empty list when the blueprint grew no park or the
        /// decoration planner could not fit the set.
        /// </summary>
        public static IReadOnlyList<CityBoardGameTable> Create(
            CityLayout layout,
            CityDecorationPlan decorations)
        {
            var tables = new List<CityBoardGameTable>(2);
            if (layout == null || decorations == null)
            {
                return tables;
            }

            var seats = new List<CityBenchSeat>(4);
            for (int index = 0;
                 index < decorations.Descriptors.Count;
                 index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations.Descriptors[index];
                if (descriptor.Kind !=
                    CityDecorationKind.ParkChessTables)
                {
                    continue;
                }

                if (!CityChessSetWorldBuilder.TryDescribeBoardBasis(
                        layout,
                        descriptor,
                        out Vector3 origin,
                        out Vector3 forward,
                        out Vector3 tangent))
                {
                    continue;
                }

                seats.Clear();
                CityDecorationWorldBuilder.AppendBenchSeats(
                    layout,
                    descriptor,
                    seats);
                AppendTable(
                    tables,
                    seats,
                    descriptor.StableId + ChessSeatIdSuffix,
                    CityBoardGameKind.Chess,
                    CityChessSetPlan.ChessTable,
                    origin,
                    tangent,
                    forward);
                AppendTable(
                    tables,
                    seats,
                    descriptor.StableId + DraughtsSeatIdSuffix,
                    CityBoardGameKind.Draughts,
                    CityChessSetPlan.DraughtsTable,
                    origin,
                    tangent,
                    forward);
            }

            return tables;
        }

        /// <summary>
        /// Where the hero's eyes are and what they look at, given how
        /// far he has turned his head off the board. The position never
        /// moves: he is sitting on a plank, not leaning around it.
        /// </summary>
        public static void EvaluateCamera(
            CityBoardGameTable table,
            float yawOffsetDegrees,
            float pitchDegrees,
            out Vector3 position,
            out Quaternion rotation)
        {
            if (!table.IsPresent)
            {
                throw new ArgumentException(
                    "The camera pose needs a present table.",
                    nameof(table));
            }

            position = table.SeatTopCenter +
                Vector3.up * EyeHeightAboveSeat +
                table.SeatFacing * EyeForwardMeters;
            float yaw = Mathf.Clamp(
                yawOffsetDegrees,
                -MaximumYawOffsetDegrees,
                MaximumYawOffsetDegrees);
            float pitch = Mathf.Clamp(
                pitchDegrees,
                MinimumPitchDegrees,
                MaximumPitchDegrees);
            Quaternion facing = Quaternion.LookRotation(
                table.SeatFacing,
                Vector3.up);
            rotation = facing * Quaternion.Euler(pitch, yaw, 0f);
        }

        /// <summary>
        /// Which square a ray lands on, or false when it misses the
        /// board entirely. The board is a plane rather than sixty-four
        /// colliders: the men standing on it would shadow half of them
        /// anyway, and a pointer that reads the plane hits the square
        /// under a piece as readily as an empty one.
        /// </summary>
        public static bool TryPickSquare(
            CityBoardGameTable table,
            Ray ray,
            out int file,
            out int rank)
        {
            file = -1;
            rank = -1;
            if (!table.IsPresent ||
                Mathf.Abs(ray.direction.y) < 0.0001f)
            {
                return false;
            }

            float planeY = CityChessBoardGeometry.LightTopY +
                table.Origin.y;
            float distance = (planeY - ray.origin.y) / ray.direction.y;
            if (distance <= 0f)
            {
                return false;
            }

            Vector3 hit = ray.origin + ray.direction * distance;
            Vector3 local = hit -
                table.Origin -
                table.Tangent *
                    (table.Table *
                     CityChessBoardGeometry.TableOffsetMeters);
            float alongTangent = Vector3.Dot(local, table.Tangent);
            float alongForward = Vector3.Dot(local, table.Forward);
            const float edge =
                (CityChessBoardGeometry.SquaresPerSide - 1) * 0.5f;
            float fileValue =
                alongTangent / CityChessBoardGeometry.SquareSizeMeters +
                edge;
            float rankValue =
                alongForward / CityChessBoardGeometry.SquareSizeMeters +
                edge;
            int candidateFile = Mathf.RoundToInt(fileValue);
            int candidateRank = Mathf.RoundToInt(rankValue);
            if (!CityChessBoardGeometry.IsOnBoard(
                    candidateFile,
                    candidateRank))
            {
                return false;
            }

            file = candidateFile;
            rank = candidateRank;
            return true;
        }

        private static void AppendTable(
            List<CityBoardGameTable> tables,
            IReadOnlyList<CityBenchSeat> seats,
            string seatId,
            CityBoardGameKind game,
            int table,
            Vector3 origin,
            Vector3 tangent,
            Vector3 forward)
        {
            for (int index = 0; index < seats.Count; index++)
            {
                CityBenchSeat seat = seats[index];
                if (!seat.IsPresent ||
                    !string.Equals(
                        seat.Id,
                        seatId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                tables.Add(new CityBoardGameTable(
                    seatId,
                    game,
                    table,
                    origin,
                    tangent,
                    forward,
                    seat.SeatTopCenter,
                    seat.FaceDirection));
                return;
            }
        }
    }
}
