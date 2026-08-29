using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Pure composition rules for the climb's negative space and roadside
    /// rhythm. Geometry remains in <see cref="MountainRoadPlanner"/>; this
    /// class says where tall mass and natural debris must pause so the road's
    /// own structure can become the subject of a frame.
    /// </summary>
    public static class MountainRoadCompositionRules
    {
        internal const float NaturalMiscClearance = 0.35f;

        // Imported dead trees are normalized by HEIGHT, unlike every other
        // misc mesh. The widest shipped source variant reaches 0.178 of that
        // height in XZ, and its collidered branches remain inside 0.13. Keep
        // the shared plan/validator envelope slightly outside both.
        internal const float DeadTreeFootprintRadiusPerHeight = 0.19f;

        private const int NaturalMiscPlacementAttempts = 192;

        /// <summary>
        /// Three spaced inner bends break the climb into readable chapters.
        /// All three crown layers yield at the centre, but the surrounding
        /// far stand and both ridge rings still close the horizon: the reveal
        /// is another piece of ROAD, never a second vista.
        /// </summary>
        public const float HairpinForestRevealRadius = 5.4f;

        /// <summary>
        /// The bridge is the exposed middle beat of the drive. Its near and
        /// middle trees stand back far enough for the deck and the opposite
        /// abutment to read before the car is already on them.
        /// </summary>
        public const float BridgeForestRevealLead = 18f;
        public const float BridgeForestRevealTail = 12f;
        public const float BridgeForestRevealHalfWidth = 13.5f;

        /// <summary>
        /// The last straight is a municipal arrival apron, not one more
        /// forest tunnel. Only the near and middle stands yield; the far
        /// layer still closes the mountain behind the terminal.
        /// </summary>
        public const float TerminalForestRevealLength = 22f;
        public const float TerminalForestRevealHalfWidth = 10.5f;

        /// <summary>
        /// The negative space of the climb. Both seeded placement and
        /// validation query this function, so a different seed cannot quietly
        /// refill an authored opening.
        /// </summary>
        internal static bool IsReservedForestOpening(
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            MountainRoadForestLayer layer,
            Vector2 point,
            float crownRadius)
        {
            for (int index = 0; index < route.Hairpins.Count; index++)
            {
                MountainRoadHairpinDescriptor hairpin =
                    route.Hairpins[index];
                if (!IsForestRevealHairpin(hairpin.Index))
                {
                    continue;
                }

                float clearance = HairpinForestRevealRadius + crownRadius;
                if ((point - hairpin.CenterXZ).sqrMagnitude <
                    clearance * clearance)
                {
                    return true;
                }
            }

            // A far-layer anchor can still land in a hairpin centre when it
            // was sampled from the neighbouring shelf. That puts a huge,
            // colliderless foreground crown in the very road window this
            // rule authors. Hairpin centres therefore clear all layers;
            // bridge and terminal windows retain Far as their closing wall.
            if (layer == MountainRoadForestLayer.Far)
            {
                return false;
            }

            MountainRoadBridgeDescriptor bridge = route.Bridge;
            if (InsideRoadAlignedOpening(
                    point,
                    bridge.Start,
                    bridge.End,
                    BridgeForestRevealLead,
                    BridgeForestRevealTail,
                    BridgeForestRevealHalfWidth + crownRadius))
            {
                return true;
            }

            MountainRoadRouteSample terminalStart = route.Sample(
                plateau.EntryDistance - TerminalForestRevealLength);
            MountainRoadRouteSample terminalEnd = route.Sample(
                plateau.EntryDistance);
            return InsideRoadAlignedOpening(
                point,
                terminalStart.Position,
                terminalEnd.Position,
                crownRadius,
                crownRadius,
                TerminalForestRevealHalfWidth + crownRadius);
        }

        internal static bool IsForestRevealHairpin(int hairpinIndex)
        {
            return hairpinIndex >= 1 &&
                   hairpinIndex < MountainRoadPlanner.HairpinCount - 1 &&
                   (hairpinIndex - 1) % 3 == 0;
        }

        /// <summary>
        /// Natural debris is grouped into five unequal roadside chapters.
        /// The gaps align with the three spaced hairpin reveals, the bridge and
        /// the terminal approach instead of placing another log every fixed
        /// number of metres.
        /// </summary>
        internal static MountainRoadMiscDescriptor PlaceNaturalMisc(
            string stableId,
            MountainRoadMiscKind kind,
            MountainRoadRoutePlan route,
            MountainRoadPlateauDescriptor plateau,
            IReadOnlyList<MountainRoadMiscDescriptor> occupied,
            int seed,
            int index,
            uint salt,
            float minimumLateral,
            float maximumLateral,
            Vector3 size,
            bool blocksMovement)
        {
            for (int attempt = 0;
                 attempt < NaturalMiscPlacementAttempts;
                 attempt++)
            {
                uint candidateSalt = AttemptSalt(salt, attempt);
                MountainRoadMiscDescriptor candidate =
                    MountainRoadPlanner.PlaceMisc(
                        stableId,
                        kind,
                        route,
                        plateau,
                        NaturalMiscDistance(
                            seed, index, candidateSalt),
                        NaturalMiscSide(
                            seed, index, candidateSalt),
                        Mathf.Lerp(
                            minimumLateral,
                            maximumLateral,
                            Unit(seed, index,
                                candidateSalt ^ 0x4C415445u)),
                        size,
                        blocksMovement,
                        Unit(seed, index,
                            candidateSalt ^ 0x59415721u) * 360f);
                if (HasClearance(candidate, occupied))
                {
                    return candidate;
                }
            }

            throw new InvalidOperationException(
                $"Could not place {stableId} without overlapping roadside " +
                "composition.");
        }

        internal static bool IsNaturalMiscKind(MountainRoadMiscKind kind)
        {
            return kind == MountainRoadMiscKind.Boulder ||
                   kind == MountainRoadMiscKind.FallenLog ||
                   kind == MountainRoadMiscKind.Stump ||
                   kind == MountainRoadMiscKind.DeadTree;
        }

        internal static bool HaveMiscFootprintClearance(
            MountainRoadMiscDescriptor first,
            MountainRoadMiscDescriptor second)
        {
            DescribeFootprint(
                first,
                out Vector2 firstRight,
                out Vector2 firstForward,
                out Vector2 firstHalf);
            DescribeFootprint(
                second,
                out Vector2 secondRight,
                out Vector2 secondForward,
                out Vector2 secondHalf);
            Vector2 delta = new Vector2(
                second.Position.x - first.Position.x,
                second.Position.z - first.Position.z);
            return HasSeparatingAxis(
                       delta,
                       firstRight,
                       firstRight,
                       firstForward,
                       firstHalf,
                       secondRight,
                       secondForward,
                       secondHalf) ||
                   HasSeparatingAxis(
                       delta,
                       firstForward,
                       firstRight,
                       firstForward,
                       firstHalf,
                       secondRight,
                       secondForward,
                       secondHalf) ||
                   HasSeparatingAxis(
                       delta,
                       secondRight,
                       firstRight,
                       firstForward,
                       firstHalf,
                       secondRight,
                       secondForward,
                       secondHalf) ||
                   HasSeparatingAxis(
                       delta,
                       secondForward,
                       firstRight,
                       firstForward,
                       firstHalf,
                       secondRight,
                       secondForward,
                       secondHalf);
        }

        internal static float AbandonedChairDistance(
            MountainRoadRoutePlan route)
        {
            return route.Hairpins[8].EndDistance + 17.25f;
        }

        private static float NaturalMiscDistance(
            int seed,
            int index,
            uint salt)
        {
            float chapter = Unit(seed, index, salt);
            float local = Unit(seed, index, salt ^ 0x4C4F4341u);
            if (chapter < 0.18f)
            {
                return Mathf.Lerp(6f, 60f, local);
            }

            if (chapter < 0.46f)
            {
                return Mathf.Lerp(112f, 210f, local);
            }

            if (chapter < 0.68f)
            {
                return Mathf.Lerp(330f, 414f, local);
            }

            if (chapter < 0.86f)
            {
                return Mathf.Lerp(467f, 521f, local);
            }

            return Mathf.Lerp(548f, 584f, local);
        }

        private static float NaturalMiscSide(
            int seed,
            int index,
            uint salt)
        {
            return (Hash(seed, index, salt ^ 0x53494445u) & 1u) == 0u
                ? -1f
                : 1f;
        }

        private static bool HasClearance(
            MountainRoadMiscDescriptor candidate,
            IReadOnlyList<MountainRoadMiscDescriptor> occupied)
        {
            for (int index = 0; index < occupied.Count; index++)
            {
                if (!HaveMiscFootprintClearance(
                        candidate,
                        occupied[index]))
                {
                    return false;
                }
            }

            return true;
        }

        internal static float FootprintRadius(
            MountainRoadMiscDescriptor item)
        {
            float descriptorRadius = Mathf.Sqrt(
                item.Size.x * item.Size.x +
                item.Size.z * item.Size.z) * 0.5f;
            return item.Kind == MountainRoadMiscKind.DeadTree
                ? Mathf.Max(
                    descriptorRadius,
                    item.Size.y * DeadTreeFootprintRadiusPerHeight)
                : descriptorRadius;
        }

        private static void DescribeFootprint(
            MountainRoadMiscDescriptor item,
            out Vector2 right,
            out Vector2 forward,
            out Vector2 half)
        {
            Vector3 right3 = item.Rotation * Vector3.right;
            Vector3 forward3 = item.Rotation * Vector3.forward;
            right = new Vector2(right3.x, right3.z).normalized;
            forward = new Vector2(forward3.x, forward3.z).normalized;
            if (item.Kind == MountainRoadMiscKind.DeadTree)
            {
                float radius = FootprintRadius(item);
                half = new Vector2(radius, radius);
                return;
            }

            half = new Vector2(item.Size.x, item.Size.z) * 0.5f;
        }

        private static bool HasSeparatingAxis(
            Vector2 delta,
            Vector2 axis,
            Vector2 firstRight,
            Vector2 firstForward,
            Vector2 firstHalf,
            Vector2 secondRight,
            Vector2 secondForward,
            Vector2 secondHalf)
        {
            float firstProjection =
                Mathf.Abs(Vector2.Dot(firstRight, axis)) * firstHalf.x +
                Mathf.Abs(Vector2.Dot(firstForward, axis)) * firstHalf.y;
            float secondProjection =
                Mathf.Abs(Vector2.Dot(secondRight, axis)) * secondHalf.x +
                Mathf.Abs(Vector2.Dot(secondForward, axis)) * secondHalf.y;
            return Mathf.Abs(Vector2.Dot(delta, axis)) >=
                   firstProjection + secondProjection +
                   NaturalMiscClearance;
        }

        private static uint AttemptSalt(uint salt, int attempt)
        {
            if (attempt == 0)
            {
                return salt;
            }

            return salt ^ unchecked(
                (uint)attempt * 0x9E3779B9u);
        }

        private static bool InsideRoadAlignedOpening(
            Vector2 point,
            Vector3 start,
            Vector3 end,
            float lead,
            float tail,
            float halfWidth)
        {
            var startXZ = new Vector2(start.x, start.z);
            var endXZ = new Vector2(end.x, end.z);
            Vector2 span = endXZ - startXZ;
            float length = span.magnitude;
            if (length <= 0.001f)
            {
                return false;
            }

            Vector2 forward = span / length;
            Vector2 delta = point - startXZ;
            float along = Vector2.Dot(delta, forward);
            if (along < -lead || along > length + tail)
            {
                return false;
            }

            float lateral = Mathf.Abs(
                delta.x * -forward.y + delta.y * forward.x);
            return lateral < halfWidth;
        }

        private static uint Hash(int seed, int index, uint salt)
        {
            uint hash = CitySoundStableHash.Combine(
                unchecked((uint)seed),
                unchecked((uint)index));
            return CitySoundStableHash.Combine(hash, salt);
        }

        private static float Unit(int seed, int index, uint salt)
        {
            return CitySoundStableHash.ToUnitFloat(Hash(seed, index, salt));
        }
    }
}
