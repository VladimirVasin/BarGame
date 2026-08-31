using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Chooses the mountain road's raven roosts: the gorge bridge
    /// rail, the exit-portal shoulder the hero walks past on minute
    /// one, the summit parapet, and one deliberately unremarkable
    /// roadside spot at the culvert. Three named places and one plain
    /// one — the same species-not-sign ratio the city keeps.
    ///
    /// Spacing is measured PLANAR on purpose: the serpentine stacks
    /// its switchbacks vertically, so two points a kilometre apart by
    /// route can hang thirty metres apart in the air, and route
    /// distance would let both keep a pair inside one frame. The
    /// planner is pure — every position derives from the already
    /// seeded road plan — and a candidate that fails a rule drops
    /// silently, degrading the road to three roosts.
    /// </summary>
    public static class MountainRoadRavenRoostPlanner
    {
        /// <summary>Minimum planar distance between accepted roost
        /// anchors, the city's own two-block figure: the road's far
        /// plane is 120 m, and 70 m keeps neighbouring pairs from
        /// ever sharing a frame.</summary>
        public const float MinimumRoostSpacingMeters = 70f;

        /// <summary>
        /// The summit parapet part must sit at least this far from
        /// the brink bench. The flush radius is 3.5 m and the session
        /// provider only suppresses flushes while the hero is already
        /// SEATED — a part three metres from the bench would scatter
        /// the pair on every single walk to the seat, so the showcase
        /// roost would never be seen perched near it. Five metres is
        /// flush plus the hero's capsule plus approach slack.
        /// </summary>
        public const float BrinkSeatClearanceMeters = 5f;

        /// <summary>
        /// A terrain-grounded anchor resolves its height through the
        /// teleport ground, which may clamp the point sideways onto
        /// the road ribbon; within this drift the perch still reads
        /// as standing at its anchor, beyond it the spot is simply
        /// not standable and the roost drops.
        /// </summary>
        public const float AuthoredAnchorDriftToleranceMeters = 1.5f;

        private const float BridgeRailEdgeInsetMeters = 0.15f;
        private const float BridgeCompanionAlongMeters = 5f;
        private const float BridgeCompanionEdgeInsetMeters = 0.3f;
        private const float PortalShoulderOutMeters = 4f;
        private const float PortalShoulderLateralMeters = 2.6f;
        private const float RoadsidePerchLateralMeters = 1.5f;
        private const float BrinkTerraceSetBackMeters = 2.2f;

        /// <summary>
        /// Plans the roosts for one generated road. The seed takes no
        /// part in the arithmetic — the plan already is that seed's
        /// product and per-bird entropy derives later from (area
        /// seed, roost id) — it stands in the signature so the three
        /// scene planners share one calling shape.
        /// </summary>
        public static IReadOnlyList<RavenRoostDescriptor> Create(
            MountainRoadPlan plan,
            ICityMapTeleportGround ground,
            int seed)
        {
            if (plan == null)
            {
                throw new ArgumentNullException(nameof(plan));
            }

            if (ground == null)
            {
                throw new ArgumentNullException(nameof(ground));
            }

            var roosts = new List<RavenRoostDescriptor>(4);
            var acceptedAnchors = new List<Vector2>(4);
            TryAddGorgeBridgeRoost(plan, roosts, acceptedAnchors);
            TryAddExitPortalRoost(
                plan, ground, roosts, acceptedAnchors);
            TryAddSummitBrinkRoost(plan, roosts, acceptedAnchors);
            TryAddRoadsideRoost(plan, ground, roosts, acceptedAnchors);
            return new ReadOnlyCollection<RavenRoostDescriptor>(roosts);
        }

        /// <summary>
        /// The rail top at the gorge bridge's start abutment — the
        /// loose guard rail is already one of the road's authored
        /// sounds, so a corvid on it is bird logic, not invention.
        /// Both perches take plan-supplied heights: the rail is the
        /// deck plus the descriptor's own rail height, and the
        /// companion stands on the deck edge five metres in, at the
        /// deck's interpolated height. The teleport ground answers
        /// for the road surface, but the RAIL is not ground at all,
        /// so this roost never touches the resolver.
        /// </summary>
        private static void TryAddGorgeBridgeRoost(
            MountainRoadPlan plan,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            const string id = "road-roost-gorge-bridge";
            MountainRoadBridgeDescriptor bridge = plan.Bridge;
            if (bridge.Length <=
                BridgeCompanionAlongMeters + 1f)
            {
                return;
            }

            Vector3 perchAPosition = bridge.Start +
                bridge.Right *
                (bridge.DeckWidth * 0.5f - BridgeRailEdgeInsetMeters);
            perchAPosition.y = bridge.Start.y + bridge.RailHeight;
            var perchA = new CemeteryRavenPerch(
                true,
                id,
                perchAPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchAPosition,
                    perchAPosition + bridge.Forward));

            Vector3 perchBPosition = bridge.Start +
                bridge.Forward * BridgeCompanionAlongMeters +
                bridge.Right *
                (bridge.ClearWidth * 0.5f -
                 BridgeCompanionEdgeInsetMeters);
            perchBPosition.y = bridge.Start.y +
                (bridge.End.y - bridge.Start.y) *
                (BridgeCompanionAlongMeters / bridge.Length);
            var perchB = new CemeteryRavenPerch(
                true,
                id,
                perchBPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchBPosition,
                    perchAPosition));
            TryAccept(id, perchA, perchB, roosts, acceptedAnchors);
        }

        /// <summary>
        /// The ground shoulder four metres out from the tunnel mouth,
        /// off the carriageway. The hero spawns six metres inside the
        /// tunnel and walks out past this pair on minute one — the
        /// feature's first read. The anchor keeps the portal ground's
        /// own plan height (the portal floor is one flat authored
        /// slab); the lateral side is fixed rather than seeded so the
        /// first read is the same road on every machine.
        /// </summary>
        private static void TryAddExitPortalRoost(
            MountainRoadPlan plan,
            ICityMapTeleportGround ground,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            const string id = "road-roost-exit-portal";
            MountainRoadTunnelDescriptor tunnel = plan.Tunnel;
            Vector3 axis = tunnel.OutwardAxis;
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.0001f)
            {
                return;
            }

            axis.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, axis);
            Vector3 roadPoint = tunnel.PortalGroundCenter +
                                axis * PortalShoulderOutMeters;
            Vector3 perchAPosition = roadPoint +
                lateral * PortalShoulderLateralMeters;
            perchAPosition.y = tunnel.PortalGroundCenter.y;
            var perchA = new CemeteryRavenPerch(
                true,
                id,
                perchAPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchAPosition,
                    roadPoint));
            if (!IsSpaced(
                    acceptedAnchors,
                    new Vector2(perchAPosition.x, perchAPosition.z)))
            {
                return;
            }

            if (!RavenRoostPlan.TrySelectGroundPerch(
                    id,
                    perchAPosition,
                    ground,
                    null,
                    out CemeteryRavenPerch perchB))
            {
                return;
            }

            TryAccept(id, perchA, perchB, roosts, acceptedAnchors);
        }

        /// <summary>
        /// The dressed-stone coping of the terrace parapet — a
        /// parapet and a rail ARE winter-corvid habitat, which is the
        /// whole canonical argument for this roost. The part is the
        /// ordinal-smallest dressed-stone Brink piece standing at
        /// least <see cref="BrinkSeatClearanceMeters"/> from the
        /// bench, and the companion stands on the terrace floor a
        /// couple of metres INWARD from the coping — inward being the
        /// negation of the brink descriptor's own outward normal, the
        /// direction that is guaranteed to be plateau rather than
        /// void. Heights are all plan data; the resolver knows the
        /// yard, not the terrace furniture.
        /// </summary>
        private static void TryAddSummitBrinkRoost(
            MountainRoadPlan plan,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            const string id = "road-roost-summit-brink";
            MountainRoadTerminalSitePlan site = plan.Terminal.Site;
            MountainRoadBrinkDescriptor brink = plan.Plateau.Brink;
            if (site == null || brink == null)
            {
                return;
            }

            var seatXZ = new Vector2(
                site.BrinkSeat.SeatTopCenter.x,
                site.BrinkSeat.SeatTopCenter.z);
            bool found = false;
            MountainRoadSitePartDescriptor part = default;
            for (int index = 0; index < site.Parts.Count; index++)
            {
                MountainRoadSitePartDescriptor candidate =
                    site.Parts[index];
                if (candidate.Group != MountainRoadSiteGroup.Brink ||
                    candidate.Style !=
                    MountainRoadSiteStyle.DressedStone)
                {
                    continue;
                }

                var candidateXZ = new Vector2(
                    candidate.Center.x,
                    candidate.Center.z);
                if (Vector2.Distance(candidateXZ, seatXZ) <
                    BrinkSeatClearanceMeters)
                {
                    continue;
                }

                if (!found ||
                    string.CompareOrdinal(
                        candidate.StableId,
                        part.StableId) < 0)
                {
                    found = true;
                    part = candidate;
                }
            }

            if (!found)
            {
                return;
            }

            var perchAPosition = new Vector3(
                part.Center.x,
                part.Center.y + part.Size.y * 0.5f,
                part.Center.z);
            var perchA = new CemeteryRavenPerch(
                true,
                id,
                perchAPosition,
                part.YawDegrees);

            Vector3 inward = -brink.Outward;
            var perchBPosition = new Vector3(
                perchAPosition.x +
                inward.x * BrinkTerraceSetBackMeters,
                site.TerraceTopY,
                perchAPosition.z +
                inward.z * BrinkTerraceSetBackMeters);
            var perchB = new CemeteryRavenPerch(
                true,
                id,
                perchBPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchBPosition,
                    perchAPosition));
            TryAccept(id, perchA, perchB, roosts, acceptedAnchors);
        }

        /// <summary>
        /// The deliberately unremarkable roost: roadside ground a
        /// step and a half off the carriageway beside the culvert —
        /// or, when no culvert placement survives the rules, beside
        /// the convex mirror, then the abandoned chair, then nothing
        /// at all and the road fields three roosts. The chain runs
        /// plainest-first because THIS row's whole job is to be a
        /// spot nobody would mark on a map.
        /// </summary>
        private static void TryAddRoadsideRoost(
            MountainRoadPlan plan,
            ICityMapTeleportGround ground,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            const string id = "road-roost-culvert";
            MountainRoadMiscKind[] chain =
            {
                MountainRoadMiscKind.Culvert,
                MountainRoadMiscKind.ConvexMirror,
                MountainRoadMiscKind.AbandonedChair
            };
            var candidates = new List<MountainRoadMiscDescriptor>(8);
            for (int chainIndex = 0;
                 chainIndex < chain.Length;
                 chainIndex++)
            {
                candidates.Clear();
                for (int index = 0; index < plan.Misc.Count; index++)
                {
                    if (plan.Misc[index].Kind == chain[chainIndex])
                    {
                        candidates.Add(plan.Misc[index]);
                    }
                }

                candidates.Sort((left, right) =>
                    string.CompareOrdinal(
                        left.StableId,
                        right.StableId));
                for (int index = 0; index < candidates.Count; index++)
                {
                    if (TryAddRoadsidePerchAt(
                            plan,
                            ground,
                            id,
                            candidates[index],
                            roosts,
                            acceptedAnchors))
                    {
                        return;
                    }
                }
            }
        }

        private static bool TryAddRoadsidePerchAt(
            MountainRoadPlan plan,
            ICityMapTeleportGround ground,
            string id,
            in MountainRoadMiscDescriptor anchor,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            // A step and a half AWAY from the route centreline, so
            // the bird stands at the road's edge beside its prop and
            // never in a wheel track.
            Vector3 away = AwayFromRoute(plan, anchor.Position);
            Vector3 probe = anchor.Position +
                            away * RoadsidePerchLateralMeters;
            var probeXZ = new Vector2(probe.x, probe.z);
            if (!ground.TryResolveStandingPosition(
                    probeXZ,
                    out Vector3 standing))
            {
                return false;
            }

            var resolvedXZ = new Vector2(standing.x, standing.z);
            if (Vector2.Distance(resolvedXZ, probeXZ) >
                AuthoredAnchorDriftToleranceMeters ||
                !IsSpaced(acceptedAnchors, resolvedXZ))
            {
                return false;
            }

            var perchAPosition = new Vector3(
                resolvedXZ.x,
                standing.y - PlayerFactory.GroundedRootOffset,
                resolvedXZ.y);
            var perchA = new CemeteryRavenPerch(
                true,
                id,
                perchAPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchAPosition,
                    anchor.Position));
            if (!RavenRoostPlan.TrySelectGroundPerch(
                    id,
                    perchAPosition,
                    ground,
                    null,
                    out CemeteryRavenPerch perchB))
            {
                return false;
            }

            return TryAccept(
                id, perchA, perchB, roosts, acceptedAnchors);
        }

        /// <summary>
        /// Horizontal direction from the nearest route centreline
        /// point out to the query — "off the road", whichever side
        /// the prop already stands on. A degenerate query on the
        /// centreline itself falls back to the sample's own right.
        /// </summary>
        private static Vector3 AwayFromRoute(
            MountainRoadPlan plan,
            Vector3 position)
        {
            IReadOnlyList<MountainRoadRouteSample> samples =
                plan.Route.Samples;
            int bestIndex = 0;
            float bestDistance = float.PositiveInfinity;
            var positionXZ = new Vector2(position.x, position.z);
            for (int index = 0; index < samples.Count; index++)
            {
                var sampleXZ = new Vector2(
                    samples[index].Position.x,
                    samples[index].Position.z);
                float distance =
                    (sampleXZ - positionXZ).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    bestIndex = index;
                }
            }

            MountainRoadRouteSample nearest = samples[bestIndex];
            var away = new Vector3(
                position.x - nearest.Position.x,
                0f,
                position.z - nearest.Position.z);
            if (away.sqrMagnitude < 0.0001f)
            {
                return nearest.Right;
            }

            return away.normalized;
        }

        private static bool TryAccept(
            string stableId,
            in CemeteryRavenPerch perchA,
            in CemeteryRavenPerch perchB,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            var anchorXZ = new Vector2(
                perchA.Position.x,
                perchA.Position.z);
            if (!IsSpaced(acceptedAnchors, anchorXZ))
            {
                return false;
            }

            roosts.Add(new RavenRoostDescriptor(
                stableId,
                perchA,
                perchB));
            acceptedAnchors.Add(anchorXZ);
            return true;
        }

        private static bool IsSpaced(
            List<Vector2> acceptedAnchors,
            Vector2 candidate)
        {
            for (int index = 0; index < acceptedAnchors.Count; index++)
            {
                if (Vector2.Distance(acceptedAnchors[index], candidate) <
                    MinimumRoostSpacingMeters)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
