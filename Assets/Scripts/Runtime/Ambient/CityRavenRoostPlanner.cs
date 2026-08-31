using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Chooses the city's outdoor raven roosts: up to fourteen
    /// authored candidate sites, taken in a fixed priority order and
    /// thinned by a greedy spacing rule, so the pairs read as a sparse
    /// species scattered over a coastal town and never as a trail of
    /// markers. Ten sites are recognizable places and four are
    /// deliberately plain dumpster kerbs — the ratio is the §19
    /// defence: a bird that also lives at a rubbish bin cannot be
    /// read as a sign.
    ///
    /// The planner is pure: positions all derive from the layout and
    /// world plans, which already carry the city seed, so re-planning
    /// the same city always seats the same birds. Everything the canon
    /// forbids is enforced here as geometry rather than convention —
    /// the cemetery keeps its unique event pair, the church keeps
    /// everything it reads over, the waterworks court keeps silence
    /// for its whole audible radius, the boat-station tableau stays
    /// closed, and the fringe mason cart stays uninhabited. A candidate
    /// that fails any rule is dropped silently; the controller that
    /// consumes the list logs what actually spawned.
    /// </summary>
    public static class CityRavenRoostPlanner
    {
        /// <summary>
        /// Minimum planar distance between accepted roost anchors.
        /// The playtest read the original 70 m step as an almost
        /// birdless city, so the band widened to ten-to-fourteen
        /// pairs and the step came down to 45 m — still nearly the
        /// city's 48 m far plane, so two pairs share a frame only at
        /// the very edge of the fog and "always far apart" keeps
        /// holding at walking pace.
        /// </summary>
        public const float MinimumRoostSpacingMeters = 45f;

        /// <summary>
        /// How far the four district points of interest push roosts
        /// away, measured from their public ground. Equal to the raven
        /// voice's audible radius on purpose: the waterworks court's
        /// standard is "ни отдельного звука", and matching the rolloff
        /// distance turns that sentence into geometry.
        /// </summary>
        public const float PointOfInterestClearanceMeters =
            CemeteryRavenVoice.AudibleRadiusMeters;

        /// <summary>
        /// Clearance from the one authored fringe vignette: the unoccupied
        /// mason cart. Near the fringe is fine; ON the cart is a new
        /// inhabitant the canon says it never gets.
        /// </summary>
        public const float FringeWorkSceneClearanceMeters = 8f;

        /// <summary>
        /// Clearance from the park chess tables. The chess set carries
        /// the park's one composed human scene and its own future
        /// session logic; keeping the birds out of that circle is
        /// cheaper and safer than teaching the roost session provider
        /// about board games.
        /// </summary>
        public const float ChessTablesClearanceMeters = 12f;

        /// <summary>
        /// A terrain-grounded perch anchor is authored in XZ and asks
        /// the teleport ground for its height; the mask may clamp the
        /// point sideways onto legal ground. Within this much drift the
        /// perch still reads as standing at its anchor — beyond it, the
        /// authored place is simply not standable and the roost drops.
        /// </summary>
        public const float AuthoredAnchorDriftToleranceMeters = 1.5f;

        private const float FountainStandOffMeters = 3.0f;
        private const float LandingStairNudgeMeters = 0.6f;
        private const float LandingCompanionOffsetMeters = 1.85f;
        private const float MolCompanionSetBackMeters = 3.2f;
        private const float BargeGunwaleInsetMeters = 0.15f;
        private const float BridgeKerbInsetMeters = 0.4f;
        private const float BridgeCompanionAlongMeters = 4.0f;
        private const float ForecourtAlongAxisMeters = 2.0f;
        private const float ForecourtLateralMeters = 3.0f;
        private const float ClockPlazaStandOffMeters = 3.5f;
        private const float KerbStandOffMeters = 1.2f;

        /// <summary>
        /// Plans the roosts for one generated city. The seed parameter
        /// takes no part in the arithmetic — the layout and world are
        /// already that seed's product, and per-bird entropy derives
        /// later from (area seed, roost id) — it stands in the
        /// signature so all three scene planners share one calling
        /// shape and a future seed-dependent rule costs no call-site
        /// change.
        /// </summary>
        public static IReadOnlyList<RavenRoostDescriptor> Create(
            CityLayout layout,
            CityWorldResult world,
            ICityMapTeleportGround ground,
            int citySeed)
        {
            if (layout == null)
            {
                throw new ArgumentNullException(nameof(layout));
            }

            if (world == null)
            {
                throw new ArgumentNullException(nameof(world));
            }

            if (ground == null)
            {
                throw new ArgumentNullException(nameof(ground));
            }

            ExclusionSet exclusion = BuildBaseExclusion(layout, world);
            Func<Vector2, bool> ringExcluded =
                BuildRingExclusion(world, exclusion);
            var roosts = new List<RavenRoostDescriptor>(14);
            var acceptedAnchors = new List<Vector2>(14);

            TryAddParkFountainRoost(
                world, ground, exclusion, ringExcluded,
                roosts, acceptedAnchors);
            bool hasLanding = TrySelectLanding(
                layout,
                out CityRiverLandingDescriptor landing);
            if (hasLanding)
            {
                TryAddRiverLandingRoost(
                    "city-roost-river-landing",
                    landing, exclusion, roosts, acceptedAnchors);
            }

            TryAddMolHeadRoost(
                world, exclusion, roosts, acceptedAnchors);
            TryAddEastBargeRoost(
                world, exclusion, roosts, acceptedAnchors);
            if (hasLanding)
            {
                // The bridge rule anchors on the chosen landing's id
                // even when the landing roost itself was dropped: the
                // "other" road bridge is a property of the river, not
                // of what the greedy pass happened to keep.
                TryAddRoadBridgeRoost(
                    layout, landing, exclusion, roosts, acceptedAnchors);
            }

            TryAddTunnelForecourtRoost(
                world, ground, exclusion, ringExcluded,
                roosts, acceptedAnchors);
            TryAddParkBandstandRoost(
                world, ground, exclusion, ringExcluded,
                roosts, acceptedAnchors);
            if (TrySelectSecondLanding(
                    layout,
                    out CityRiverLandingDescriptor secondLanding))
            {
                TryAddRiverLandingRoost(
                    "city-roost-second-landing",
                    secondLanding, exclusion, roosts,
                    acceptedAnchors);
            }

            TryAddClockPlazaRoost(
                world, ground, exclusion, ringExcluded,
                roosts, acceptedAnchors);
            AddPlainKerbRoosts(
                world, ground, exclusion, ringExcluded,
                roosts, acceptedAnchors);

            return new ReadOnlyCollection<RavenRoostDescriptor>(roosts);
        }

        /// <summary>
        /// A pair on the plaza gravel beside the park fountain — the
        /// one place whose canon already names a visible lone bird.
        /// Three metres to the side keeps the fountain itself as the
        /// backdrop rather than a pedestal.
        /// </summary>
        private static void TryAddParkFountainRoost(
            CityWorldResult world,
            ICityMapTeleportGround ground,
            ExclusionSet exclusion,
            Func<Vector2, bool> ringExcluded,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            if (!TrySelectDecoration(
                    world.DecorationPlan,
                    CityDecorationKind.ParkFountainAndStatue,
                    out CityDecorationDescriptor fountain))
            {
                return;
            }

            Vector3 lateral = Vector3.Cross(
                Vector3.up,
                fountain.Forward).normalized;
            Vector3 anchor = fountain.Position +
                             lateral * FountainStandOffMeters;
            TryAddTerrainRoost(
                "city-roost-park-fountain",
                new Vector2(anchor.x, anchor.z),
                fountain.Position,
                ground,
                exclusion,
                ringExcluded,
                roosts,
                acceptedAnchors);
        }

        /// <summary>
        /// The lower waterside platform of one river landing. Both
        /// perches are authored with the plan's own LowerY datum: the
        /// platform hangs in the river cut, where the teleport ground
        /// only knows the quay a level above, so a resolver probe here
        /// would seat the bird on the bank top. The platform is
        /// 3 x 5 m, tighter than the cemetery's preferred band — the
        /// pair simply sits closer, the way birds on a small ledge do.
        /// The ordinal-first landing takes this shape first; the
        /// ordinal-second takes it again under its own id, because
        /// the playtest wanted the promenade lived-in on both walks
        /// along it, and the four landings sit far enough apart that
        /// the spacing step decides, not this method.
        /// </summary>
        private static void TryAddRiverLandingRoost(
            string id,
            in CityRiverLandingDescriptor landing,
            ExclusionSet exclusion,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            Vector2 platformCenter = landing.PlatformBounds.center;
            Vector2 towardStair =
                landing.StairBounds.center - platformCenter;
            if (towardStair.sqrMagnitude < 0.0001f)
            {
                Vector3 descent = landing.DescentDirection;
                towardStair = new Vector2(-descent.x, -descent.z);
            }

            towardStair.Normalize();
            Vector2 anchorXZ = platformCenter +
                               towardStair * LandingStairNudgeMeters;
            var perchAPosition = new Vector3(
                anchorXZ.x,
                landing.LowerY,
                anchorXZ.y);
            // The anchor bird looks back up the stair the hero comes
            // down by — against the descent direction.
            var perchA = new CemeteryRavenPerch(
                true,
                id,
                perchAPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchAPosition,
                    perchAPosition - landing.DescentDirection));
            Vector2 companionXZ = platformCenter -
                                  towardStair * LandingCompanionOffsetMeters;
            var perchBPosition = new Vector3(
                companionXZ.x,
                landing.LowerY,
                companionXZ.y);
            var perchB = new CemeteryRavenPerch(
                true,
                id,
                perchBPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchBPosition,
                    perchAPosition));
            TryAccept(
                id, perchA, perchB,
                exclusion, roosts, acceptedAnchors);
        }

        /// <summary>
        /// The head of the mol in the dead port, west of the mouth.
        /// The anchor bird stands on the head parapet's coping (the
        /// part's own top face); the companion stands on the deck a
        /// few steps back toward land. Both heights come from the
        /// seacoast parts because the mol is deck over sea cells —
        /// the teleport ground has no answer there at all.
        /// </summary>
        private static void TryAddMolHeadRoost(
            CityWorldResult world,
            ExclusionSet exclusion,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            const string id = "city-roost-mol-head";
            CitySeacoastPlan coast = world.SeacoastPlan;
            if (coast == null)
            {
                return;
            }

            float waterlineZ = coast.Frame.WaterlineZ;
            if (!TrySelectPartFarthestFromWaterline(
                    coast,
                    CitySeacoastPartKind.MolParapet,
                    waterlineZ,
                    out CitySeacoastPartDescriptor parapet) ||
                !TrySelectPartFarthestFromWaterline(
                    coast,
                    CitySeacoastPartKind.MolDeck,
                    waterlineZ,
                    out CitySeacoastPartDescriptor headDeck) ||
                !TrySelectPartNearestToWaterline(
                    coast,
                    CitySeacoastPartKind.MolDeck,
                    waterlineZ,
                    out CitySeacoastPartDescriptor rootDeck))
            {
                return;
            }

            var perchAPosition = new Vector3(
                parapet.Center.x,
                parapet.Center.y + parapet.Size.y * 0.5f,
                parapet.Center.z);
            // The head parapet is the coping laid ACROSS the mol, so
            // its long local axis is the one to face along; the sign
            // is fixed by construction and reads as a bird watching
            // the water off one shoulder.
            Vector3 alongParapet = parapet.Size.x >= parapet.Size.z
                ? parapet.Rotation * Vector3.right
                : parapet.Rotation * Vector3.forward;
            var perchA = new CemeteryRavenPerch(
                true,
                id,
                perchAPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchAPosition,
                    perchAPosition + alongParapet));

            // Back along the mol: from the head toward the root, on
            // the deck centreline. The head deck part supplies the
            // deck top the companion stands on.
            var back = new Vector2(
                rootDeck.Center.x - parapet.Center.x,
                rootDeck.Center.z - parapet.Center.z);
            if (back.sqrMagnitude < 0.0001f)
            {
                return;
            }

            back.Normalize();
            var perchBPosition = new Vector3(
                perchAPosition.x + back.x * MolCompanionSetBackMeters,
                headDeck.Center.y + headDeck.Size.y * 0.5f,
                perchAPosition.z + back.y * MolCompanionSetBackMeters);
            var perchB = new CemeteryRavenPerch(
                true,
                id,
                perchBPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchBPosition,
                    perchAPosition));
            TryAccept(
                id, perchA, perchB,
                exclusion, roosts, acceptedAnchors);
        }

        /// <summary>
        /// The stranded barge on the wild east shore: one bird on the
        /// shoreward gunwale, one on the far gunwale, the way corvids
        /// actually take a hull. The barge is authored as several
        /// boxes sharing the Barge kind; the deck is the hull's one
        /// walkable plane and by far the widest of them, so it is
        /// selected by footprint area rather than by the coast
        /// planner's private id strings. The planner ASSERTS the hull
        /// lies in the frame's east zone — the boat-station tableau in
        /// the centre zone is a closed composition, and a barge that
        /// drifted there is a mis-authored coast, not a roost site.
        /// </summary>
        private static void TryAddEastBargeRoost(
            CityWorldResult world,
            ExclusionSet exclusion,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            const string id = "city-roost-east-barge";
            CitySeacoastPlan coast = world.SeacoastPlan;
            if (coast == null)
            {
                return;
            }

            bool found = false;
            CitySeacoastPartDescriptor deck = default;
            float bestArea = float.NegativeInfinity;
            for (int index = 0; index < coast.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = coast.Parts[index];
                if (part.Kind != CitySeacoastPartKind.Barge)
                {
                    continue;
                }

                float area = part.Size.x * part.Size.z;
                bool better = !found ||
                              area > bestArea + 0.0001f ||
                              (area > bestArea - 0.0001f &&
                               string.CompareOrdinal(
                                   part.StableId,
                                   deck.StableId) < 0);
                if (better)
                {
                    found = true;
                    deck = part;
                    bestArea = area;
                }
            }

            if (!found)
            {
                return;
            }

            if (!ContainsInclusive(
                    coast.Frame.EastZone,
                    new Vector2(deck.Center.x, deck.Center.z)))
            {
                throw new InvalidOperationException(
                    "The barge roost requires the barge hull in the " +
                    "seacoast frame's east zone; the boat-station " +
                    "tableau is closed to it.");
            }

            float gunwaleTopY = deck.Center.y + deck.Size.y * 0.5f;
            float halfBeam = deck.Size.z * 0.5f - BargeGunwaleInsetMeters;
            Vector3 nearEdge = deck.Center +
                               deck.Rotation *
                               new Vector3(0f, 0f, halfBeam);
            Vector3 farEdge = deck.Center +
                              deck.Rotation *
                              new Vector3(0f, 0f, -halfBeam);
            // The anchor bird takes the gunwale a walker on the sand
            // actually sees — the one nearer the waterline.
            float waterlineZ = coast.Frame.WaterlineZ;
            bool nearIsShoreward =
                Mathf.Abs(nearEdge.z - waterlineZ) <=
                Mathf.Abs(farEdge.z - waterlineZ);
            Vector3 anchorEdge = nearIsShoreward ? nearEdge : farEdge;
            Vector3 companionEdge = nearIsShoreward ? farEdge : nearEdge;

            var perchAPosition = new Vector3(
                anchorEdge.x,
                gunwaleTopY,
                anchorEdge.z);
            Vector3 alongHull = deck.Rotation * Vector3.right;
            var perchA = new CemeteryRavenPerch(
                true,
                id,
                perchAPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchAPosition,
                    perchAPosition + alongHull));
            var perchBPosition = new Vector3(
                companionEdge.x,
                gunwaleTopY,
                companionEdge.z);
            var perchB = new CemeteryRavenPerch(
                true,
                id,
                perchBPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchBPosition,
                    perchAPosition));
            TryAccept(
                id, perchA, perchB,
                exclusion, roosts, acceptedAnchors);
        }

        /// <summary>
        /// The deck kerb of the OTHER road bridge — the one the chosen
        /// landing does not hang off. The two road bridges close the
        /// river circuit north and south of the corridor, so this rule
        /// is structural distance from the landing roost rather than a
        /// measured one. Both perches take the plan's WestY datum: the
        /// bridge deck, like the landing, is geometry the teleport
        /// ground does not answer for.
        /// </summary>
        private static void TryAddRoadBridgeRoost(
            CityLayout layout,
            in CityRiverLandingDescriptor landing,
            ExclusionSet exclusion,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            const string id = "city-roost-road-bridge";
            bool found = false;
            CityRiverBridgeDescriptor bridge = default;
            for (int index = 0;
                 index < layout.River.Bridges.Count;
                 index++)
            {
                CityRiverBridgeDescriptor candidate =
                    layout.River.Bridges[index];
                if (!candidate.Definition.CarriesRoadTraffic ||
                    string.Equals(
                        candidate.Definition.Id,
                        landing.BridgeId,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!found ||
                    string.CompareOrdinal(
                        candidate.Definition.Id,
                        bridge.Definition.Id) < 0)
                {
                    found = true;
                    bridge = candidate;
                }
            }

            if (!found ||
                bridge.DeckBounds.width <
                BridgeCompanionAlongMeters + BridgeKerbInsetMeters)
            {
                return;
            }

            Rect deck = bridge.DeckBounds;
            var perchAPosition = new Vector3(
                deck.xMin + BridgeKerbInsetMeters,
                bridge.WestY,
                deck.yMin + BridgeKerbInsetMeters);
            // Facing east along the deck, into the town the bridge
            // serves.
            var perchA = new CemeteryRavenPerch(
                true,
                id,
                perchAPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchAPosition,
                    perchAPosition + Vector3.right));
            var perchBPosition = new Vector3(
                deck.xMin + BridgeCompanionAlongMeters,
                bridge.WestY,
                deck.yMin + BridgeKerbInsetMeters);
            var perchB = new CemeteryRavenPerch(
                true,
                id,
                perchBPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchBPosition,
                    perchAPosition));
            TryAccept(
                id, perchA, perchB,
                exclusion, roosts, acceptedAnchors);
        }

        /// <summary>
        /// Forecourt gravel at the street end of the tunnel yard, off
        /// the portal's axis so the bird is never framed against the
        /// mouth — the portal's canon wants the turn-around to be
        /// unprompted, and a composed eye-catcher on its axis would be
        /// a hint. Both perpendicular sides are legal "off axis"; they
        /// are tried in a fixed order so the choice stays a function
        /// of the layout.
        /// </summary>
        private static void TryAddTunnelForecourtRoost(
            CityWorldResult world,
            ICityMapTeleportGround ground,
            ExclusionSet exclusion,
            Func<Vector2, bool> ringExcluded,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            if (!world.FringeYardPlan.HasTunnelForecourt)
            {
                return;
            }

            CityTunnelForecourtDescriptor forecourt =
                world.FringeYardPlan.TunnelForecourt;
            Vector3 axis = forecourt.Axis;
            axis.y = 0f;
            if (axis.sqrMagnitude < 0.0001f)
            {
                return;
            }

            axis.Normalize();
            Vector3 lateral = Vector3.Cross(Vector3.up, axis);
            // The forecourt axis runs street-to-portal (the arrival
            // code walks out of the tunnel along its negation), so the
            // STREET side of the street anchor is the negative
            // direction. The plan wants the bird on street-side gravel
            // — try that side first on both laterals, and only then
            // the yard side.
            for (int along = 0; along < 2; along++)
            {
                float alongSign = along == 0 ? -1f : 1f;
                Vector3 baseAnchor =
                    forecourt.StreetAnchor +
                    axis * (alongSign * ForecourtAlongAxisMeters);
                for (int side = 0; side < 2; side++)
                {
                    float sign = side == 0 ? 1f : -1f;
                    Vector3 anchor = baseAnchor +
                                     lateral *
                                     (sign * ForecourtLateralMeters);
                    if (TryAddTerrainRoost(
                            "city-roost-tunnel-forecourt",
                            new Vector2(anchor.x, anchor.z),
                            forecourt.StreetAnchor,
                            ground,
                            exclusion,
                            ringExcluded,
                            roosts,
                            acceptedAnchors))
                    {
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// A pair on the plaza gravel beside the park bandstand — the
        /// park's other composed clearing, taken exactly the way the
        /// fountain pair is: the same three metres to the side keep
        /// the stage as a backdrop rather than a pedestal, and the
        /// terrain resolver keeps the birds off the deck itself.
        /// </summary>
        private static void TryAddParkBandstandRoost(
            CityWorldResult world,
            ICityMapTeleportGround ground,
            ExclusionSet exclusion,
            Func<Vector2, bool> ringExcluded,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            if (!TrySelectDecoration(
                    world.DecorationPlan,
                    CityDecorationKind.ParkBandstand,
                    out CityDecorationDescriptor bandstand))
            {
                return;
            }

            Vector3 lateral = Vector3.Cross(
                Vector3.up,
                bandstand.Forward).normalized;
            Vector3 anchor = bandstand.Position +
                             lateral * FountainStandOffMeters;
            TryAddTerrainRoost(
                "city-roost-park-bandstand",
                new Vector2(anchor.x, anchor.z),
                bandstand.Position,
                ground,
                exclusion,
                ringExcluded,
                roosts,
                acceptedAnchors);
        }

        /// <summary>
        /// The old-town clock tower's plaza: ground three and a half
        /// metres out along the tower's forward, so the birds stand
        /// on the open paving under the dial rather than pressed to
        /// the tower's foot. This roost stood in the plan's first
        /// draft and was cut when the 70 m step left it no room; the
        /// 45 m step brings it back, and the terrain resolver plus
        /// the exclusions still veto it on any seed where the plaza
        /// is closed ground.
        /// </summary>
        private static void TryAddClockPlazaRoost(
            CityWorldResult world,
            ICityMapTeleportGround ground,
            ExclusionSet exclusion,
            Func<Vector2, bool> ringExcluded,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            if (!TrySelectDecoration(
                    world.DecorationPlan,
                    CityDecorationKind.OldTownClockTower,
                    out CityDecorationDescriptor tower))
            {
                return;
            }

            Vector3 anchor = tower.Position +
                             tower.Forward * ClockPlazaStandOffMeters;
            TryAddTerrainRoost(
                "city-roost-clock-plaza",
                new Vector2(anchor.x, anchor.z),
                tower.Position,
                ground,
                exclusion,
                ringExcluded,
                roosts,
                acceptedAnchors);
        }

        /// <summary>
        /// The four deliberately unremarkable roosts: ground beside a
        /// roadside dumpster, taken in stable-id order. The scan keeps
        /// walking the dumpster list until a candidate survives every
        /// rule, and each later roost continues from after the one
        /// before — dumpsters repeat at a 40 m minimum across the
        /// city, so a satisfying ordinal usually exists, and a city
        /// where none does simply fields fewer plain roosts.
        /// </summary>
        private static void AddPlainKerbRoosts(
            CityWorldResult world,
            ICityMapTeleportGround ground,
            ExclusionSet exclusion,
            Func<Vector2, bool> ringExcluded,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            IReadOnlyList<CityDecorationDescriptor> descriptors =
                world.DecorationPlan.Descriptors;
            string[] ids =
            {
                "city-roost-plain-kerb-a",
                "city-roost-plain-kerb-b",
                "city-roost-plain-kerb-c",
                "city-roost-plain-kerb-d"
            };
            int nextIndex = 0;
            for (int slot = 0; slot < ids.Length; slot++)
            {
                bool placed = false;
                while (!placed && nextIndex < descriptors.Count)
                {
                    CityDecorationDescriptor descriptor =
                        descriptors[nextIndex];
                    nextIndex++;
                    if (descriptor.Kind !=
                        CityDecorationKind.RoadsideDumpsterAndUtility)
                    {
                        continue;
                    }

                    Vector3 anchor = descriptor.Position +
                                     descriptor.Forward *
                                     KerbStandOffMeters;
                    placed = TryAddTerrainRoost(
                        ids[slot],
                        new Vector2(anchor.x, anchor.z),
                        descriptor.Position,
                        ground,
                        exclusion,
                        ringExcluded,
                        roosts,
                        acceptedAnchors);
                }
            }
        }

        /// <summary>
        /// One terrain-grounded roost: perch A resolves its height
        /// through the teleport ground (drift-guarded, capsule offset
        /// removed), faces its own anchor object, and perch B comes
        /// from the shared seeded ring. Deck roosts never come here.
        /// </summary>
        private static bool TryAddTerrainRoost(
            string stableId,
            Vector2 anchorXZ,
            Vector3 lookTarget,
            ICityMapTeleportGround ground,
            ExclusionSet exclusion,
            Func<Vector2, bool> ringExcluded,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            if (!ground.TryResolveStandingPosition(
                    anchorXZ,
                    out Vector3 standing))
            {
                return false;
            }

            var resolvedXZ = new Vector2(standing.x, standing.z);
            if (Vector2.Distance(resolvedXZ, anchorXZ) >
                AuthoredAnchorDriftToleranceMeters ||
                exclusion.Excludes(resolvedXZ) ||
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
                stableId,
                perchAPosition,
                RavenRoostPlan.ComputeYawToward(
                    perchAPosition,
                    lookTarget));
            if (!RavenRoostPlan.TrySelectGroundPerch(
                    stableId,
                    perchAPosition,
                    ground,
                    ringExcluded,
                    out CemeteryRavenPerch perchB))
            {
                return false;
            }

            return TryAccept(
                stableId, perchA, perchB,
                exclusion, roosts, acceptedAnchors);
        }

        /// <summary>
        /// The one gate every candidate passes on its way into the
        /// plan: both perches clear of every forbidden ground, and the
        /// anchor far enough from every roost already accepted. The
        /// greedy order means an earlier row never yields to a later
        /// one — the priority list IS the authorship.
        /// </summary>
        private static bool TryAccept(
            string stableId,
            in CemeteryRavenPerch perchA,
            in CemeteryRavenPerch perchB,
            ExclusionSet exclusion,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            var anchorXZ = new Vector2(
                perchA.Position.x,
                perchA.Position.z);
            var companionXZ = new Vector2(
                perchB.Position.x,
                perchB.Position.z);
            if (exclusion.Excludes(anchorXZ) ||
                exclusion.Excludes(companionXZ) ||
                !IsSpaced(acceptedAnchors, anchorXZ))
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

        /// <summary>
        /// Everything the canon closes to the species, applied to BOTH
        /// perches of every candidate: the cemetery precinct (the
        /// grave pair stays unique inside its fence), everything the
        /// church reads over, all four district points of interest
        /// with the full audible radius, the boat station's closed
        /// centre zone, the fringe mason cart, and the chess tables.
        /// </summary>
        private static ExclusionSet BuildBaseExclusion(
            CityLayout layout,
            CityWorldResult world)
        {
            var exclusion = new ExclusionSet();
            if (world.CemeteryPlan != null)
            {
                exclusion.AddRect(world.CemeteryPlan.Grounds);
            }

            if (world.ChurchPlan != null)
            {
                exclusion.AddRect(world.ChurchPlan.ApproachBounds);
            }

            if (world.ChurchCourtyardPlan != null)
            {
                exclusion.AddRect(
                    world.ChurchCourtyardPlan.ForecourtBounds);
                exclusion.AddRect(
                    world.ChurchCourtyardPlan.GardenBounds);
            }

            for (int index = 0;
                 index < layout.DistrictPointsOfInterest.Count;
                 index++)
            {
                exclusion.AddRect(Inflate(
                    layout.DistrictPointsOfInterest[index].PublicBounds,
                    PointOfInterestClearanceMeters));
            }

            if (world.SeacoastPlan != null)
            {
                exclusion.AddRect(world.SeacoastPlan.Frame.CenterZone);
            }

            IReadOnlyList<CityFringeYardDescriptor> yards =
                world.FringeYardPlan.Yards;
            for (int yardIndex = 0;
                 yardIndex < yards.Count;
                 yardIndex++)
            {
                IReadOnlyList<CityFringeYardPartDescriptor> parts =
                    yards[yardIndex].Parts;
                for (int partIndex = 0;
                     partIndex < parts.Count;
                     partIndex++)
                {
                    CityFringeYardPartDescriptor part =
                        parts[partIndex];
                    if (!IsFringeWorkScene(part.Kind))
                    {
                        continue;
                    }

                    exclusion.AddCircle(
                        new Vector2(part.Center.x, part.Center.z),
                        FringeWorkSceneClearanceMeters);
                }
            }

            IReadOnlyList<CityDecorationDescriptor> decorations =
                world.DecorationPlan.Descriptors;
            for (int index = 0; index < decorations.Count; index++)
            {
                if (decorations[index].Kind ==
                    CityDecorationKind.ParkChessTables)
                {
                    Vector3 position = decorations[index].Position;
                    exclusion.AddCircle(
                        new Vector2(position.x, position.z),
                        ChessTablesClearanceMeters);
                }
            }

            return exclusion;
        }

        /// <summary>
        /// The extra veto for RING-resolved companion perches only:
        /// the teleport mask knows buildings but not props, so a probe
        /// can otherwise answer with a point inside a seacoast box or
        /// a blocking street decoration. Authored perches skip this on
        /// purpose — a mol coping or a barge gunwale IS a part, and
        /// standing on it is the roost's whole idea. Decorations carry
        /// no size in their descriptor, so each blocking ground kind
        /// is approximated by the validator's own protection radius —
        /// the same figure the city already uses to keep things out of
        /// each other.
        /// </summary>
        private static Func<Vector2, bool> BuildRingExclusion(
            CityWorldResult world,
            ExclusionSet baseExclusion)
        {
            var meshes = new ExclusionSet();
            if (world.SeacoastPlan != null)
            {
                IReadOnlyList<CitySeacoastPartDescriptor> parts =
                    world.SeacoastPlan.Parts;
                for (int index = 0; index < parts.Count; index++)
                {
                    if (parts[index].BlocksMovement)
                    {
                        meshes.AddRect(ComputeFootprint(
                            parts[index].Center,
                            parts[index].Rotation,
                            parts[index].Size));
                    }
                }
            }

            IReadOnlyList<CityDecorationDescriptor> decorations =
                world.DecorationPlan.Descriptors;
            for (int index = 0; index < decorations.Count; index++)
            {
                CityDecorationDescriptor descriptor =
                    decorations[index];
                if (descriptor.CollisionTier !=
                    CityDecorationCollisionTier.Blocking ||
                    !IsGroundAnchored(descriptor.AnchorKind))
                {
                    continue;
                }

                meshes.AddCircle(
                    new Vector2(
                        descriptor.Position.x,
                        descriptor.Position.z),
                    CityDecorationValidator.ResolveProtectionRadius(
                        descriptor.Kind));
            }

            return point =>
                baseExclusion.Excludes(point) ||
                meshes.Excludes(point);
        }

        private static bool IsGroundAnchored(
            CityDecorationAnchorKind anchorKind)
        {
            switch (anchorKind)
            {
                case CityDecorationAnchorKind.Roadside:
                case CityDecorationAnchorKind.BuildingFrontage:
                case CityDecorationAnchorKind.ParkFeature:
                case CityDecorationAnchorKind.ParkLandmark:
                case CityDecorationAnchorKind.LotGround:
                    return true;
                default:
                    // Roof, facade and urban-landmark anchors hang on
                    // or over buildings the walkable mask already
                    // subtracts; their XZ shadow is not ground a probe
                    // can reach.
                    return false;
            }
        }

        private static bool IsFringeWorkScene(
            CityFringeYardPartKind kind)
        {
            return kind == CityFringeYardPartKind.MasonCart;
        }

        private static bool TrySelectLanding(
            CityLayout layout,
            out CityRiverLandingDescriptor landing)
        {
            landing = default;
            if (!layout.River.IsEnabled)
            {
                return false;
            }

            bool found = false;
            for (int index = 0;
                 index < layout.River.Landings.Count;
                 index++)
            {
                CityRiverLandingDescriptor candidate =
                    layout.River.Landings[index];
                if (!found ||
                    string.CompareOrdinal(
                        candidate.Id,
                        landing.Id) < 0)
                {
                    found = true;
                    landing = candidate;
                }
            }

            return found;
        }

        /// <summary>
        /// The ordinal-second landing — the next id after the one
        /// TrySelectLanding names. A river with a single landing has
        /// no second, and the roost simply never enters the plan; the
        /// choice stays a function of the ids alone, so the pair
        /// survives any reseed that keeps the landings themselves.
        /// </summary>
        private static bool TrySelectSecondLanding(
            CityLayout layout,
            out CityRiverLandingDescriptor landing)
        {
            landing = default;
            if (!TrySelectLanding(
                    layout,
                    out CityRiverLandingDescriptor first))
            {
                return false;
            }

            bool found = false;
            for (int index = 0;
                 index < layout.River.Landings.Count;
                 index++)
            {
                CityRiverLandingDescriptor candidate =
                    layout.River.Landings[index];
                if (string.Equals(
                        candidate.Id,
                        first.Id,
                        StringComparison.Ordinal))
                {
                    continue;
                }

                if (!found ||
                    string.CompareOrdinal(
                        candidate.Id,
                        landing.Id) < 0)
                {
                    found = true;
                    landing = candidate;
                }
            }

            return found;
        }

        /// <summary>First descriptor of the kind in stable-id order —
        /// the plan's list is already ordinal-sorted, so the first
        /// match is the deterministic one.</summary>
        private static bool TrySelectDecoration(
            CityDecorationPlan plan,
            CityDecorationKind kind,
            out CityDecorationDescriptor descriptor)
        {
            for (int index = 0;
                 index < plan.Descriptors.Count;
                 index++)
            {
                if (plan.Descriptors[index].Kind == kind)
                {
                    descriptor = plan.Descriptors[index];
                    return true;
                }
            }

            descriptor = default;
            return false;
        }

        private static bool TrySelectPartFarthestFromWaterline(
            CitySeacoastPlan coast,
            CitySeacoastPartKind kind,
            float waterlineZ,
            out CitySeacoastPartDescriptor selected)
        {
            return TrySelectPartByWaterlineDistance(
                coast, kind, waterlineZ, true, out selected);
        }

        private static bool TrySelectPartNearestToWaterline(
            CitySeacoastPlan coast,
            CitySeacoastPartKind kind,
            float waterlineZ,
            out CitySeacoastPartDescriptor selected)
        {
            return TrySelectPartByWaterlineDistance(
                coast, kind, waterlineZ, false, out selected);
        }

        /// <summary>
        /// The mol runs from the sand out to sea, so "the head" is
        /// simply the part of a kind farthest from the waterline and
        /// "the root" the nearest — a rule that survives any reseed
        /// without naming the coast's internal part ids. Ties fall to
        /// the ordinal-smaller stable id, the codebase's usual
        /// float-noise guard.
        /// </summary>
        private static bool TrySelectPartByWaterlineDistance(
            CitySeacoastPlan coast,
            CitySeacoastPartKind kind,
            float waterlineZ,
            bool farthest,
            out CitySeacoastPartDescriptor selected)
        {
            selected = default;
            bool found = false;
            float bestDistance = 0f;
            for (int index = 0; index < coast.Parts.Count; index++)
            {
                CitySeacoastPartDescriptor part = coast.Parts[index];
                if (part.Kind != kind)
                {
                    continue;
                }

                float distance = Mathf.Abs(
                    part.Center.z - waterlineZ);
                bool strictlyBetter = farthest
                    ? distance > bestDistance + 0.0005f
                    : distance < bestDistance - 0.0005f;
                bool tied = found &&
                            Mathf.Abs(distance - bestDistance) <=
                            0.0005f &&
                            string.CompareOrdinal(
                                part.StableId,
                                selected.StableId) < 0;
                if (!found || strictlyBetter || tied)
                {
                    found = true;
                    selected = part;
                    bestDistance = distance;
                }
            }

            return found;
        }

        /// <summary>
        /// Conservative XZ envelope of one oriented seacoast box —
        /// the same projection the fringe yard descriptors use for
        /// their clearance tests.
        /// </summary>
        private static Rect ComputeFootprint(
            Vector3 center,
            Quaternion rotation,
            Vector3 size)
        {
            Vector3 half = size * 0.5f;
            Vector3 right = rotation * Vector3.right;
            Vector3 up = rotation * Vector3.up;
            Vector3 forward = rotation * Vector3.forward;
            float halfX = Mathf.Abs(right.x) * half.x +
                          Mathf.Abs(up.x) * half.y +
                          Mathf.Abs(forward.x) * half.z;
            float halfZ = Mathf.Abs(right.z) * half.x +
                          Mathf.Abs(up.z) * half.y +
                          Mathf.Abs(forward.z) * half.z;
            return Rect.MinMaxRect(
                center.x - halfX,
                center.z - halfZ,
                center.x + halfX,
                center.z + halfZ);
        }

        private static Rect Inflate(Rect rect, float amount)
        {
            return Rect.MinMaxRect(
                rect.xMin - amount,
                rect.yMin - amount,
                rect.xMax + amount,
                rect.yMax + amount);
        }

        private static bool ContainsInclusive(
            Rect rect,
            Vector2 point)
        {
            return point.x >= rect.xMin &&
                   point.x <= rect.xMax &&
                   point.y >= rect.yMin &&
                   point.y <= rect.yMax;
        }

        /// <summary>
        /// Rects and circles answering one question: is this ground
        /// closed to the species? Kept as parallel lists rather than
        /// per-shape objects because the whole set is built once per
        /// plan and probed a few thousand times at most.
        /// </summary>
        private sealed class ExclusionSet
        {
            private readonly List<Rect> rects = new List<Rect>();
            private readonly List<Vector2> circleCenters =
                new List<Vector2>();
            private readonly List<float> circleRadii =
                new List<float>();

            public void AddRect(Rect rect)
            {
                rects.Add(rect);
            }

            public void AddCircle(Vector2 center, float radius)
            {
                circleCenters.Add(center);
                circleRadii.Add(radius);
            }

            public bool Excludes(Vector2 point)
            {
                for (int index = 0; index < rects.Count; index++)
                {
                    if (ContainsInclusive(rects[index], point))
                    {
                        return true;
                    }
                }

                for (int index = 0;
                     index < circleCenters.Count;
                     index++)
                {
                    if (Vector2.Distance(
                            circleCenters[index],
                            point) < circleRadii[index])
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
