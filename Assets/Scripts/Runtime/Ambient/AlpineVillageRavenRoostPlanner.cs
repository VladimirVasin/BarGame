using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Chooses the alpine village's raven roosts: the spoil-heap edge
    /// before the adit mouth, the firewood mine-cart behind it, and a
    /// fence-line spot at one ordinary house's cable gate. The chapel
    /// over the source takes NO roost and pushes everything away by
    /// the birds' whole audible radius: story §5 poured the poison
    /// there, so a held corvid pair over the spring would be the
    /// game's clearest false omen — the chapel gets exactly the
    /// waterworks-court treatment, place plus silence.
    ///
    /// The planner is pure and seedless in its arithmetic (the
    /// village plan already carries the seed), and it deliberately
    /// reads the dog and firewood positions out of the soundscape
    /// planner rather than re-deriving them — one authority for where
    /// the cart stands and where the dog lives, so audio and birds
    /// can never disagree about either.
    /// </summary>
    public static class AlpineVillageRavenRoostPlanner
    {
        /// <summary>
        /// Minimum planar distance between accepted roost anchors.
        /// The whole lane is around eighty metres, so the city's 70 m
        /// figure would permit exactly one roost; 24 m keeps pairs a
        /// couple of house-plots apart, sparse at village scale.
        /// </summary>
        public const float MinimumRoostSpacingMeters = 24f;

        /// <summary>
        /// How far the chapel plot pushes roosts away. Equal to the
        /// raven voice's audible radius: the crime scene must get
        /// neither a bird nor a single note of one, the same geometry
        /// the city gives the pipe's lower end at the waterworks
        /// court.
        /// </summary>
        public const float ChapelClearanceMeters =
            CemeteryRavenVoice.AudibleRadiusMeters;

        /// <summary>
        /// Clearance from the dog's yard anchor. The dog is the
        /// village's one animal voice and its scene is composed —
        /// birds at its fence would read as a staged encounter, and a
        /// caw inside the bark's audible circle would tangle the two
        /// voices.
        /// </summary>
        public const float DogClearanceMeters = 15f;

        /// <summary>
        /// A terrain-grounded anchor resolves its height through the
        /// teleport ground, whose mask may clamp the point onto the
        /// nearest trodden path; within this drift the perch still
        /// reads as standing at its anchor, beyond it the authored
        /// spot is not standable and the roost drops.
        /// </summary>
        public const float AuthoredAnchorDriftToleranceMeters = 1.5f;

        private const float AditMouthStandOffMeters = 1.2f;
        private const float FirewoodStandOffMeters = 1.2f;

        /// <summary>
        /// Plans the roosts for one generated village. The seed takes
        /// no part in the arithmetic — the plan already is that
        /// seed's product and per-bird entropy derives later from
        /// (area seed, roost id) — it stands in the signature so the
        /// three scene planners share one calling shape.
        /// </summary>
        public static IReadOnlyList<RavenRoostDescriptor> Create(
            AlpineVillagePlan plan,
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

            // The soundscape planner is exactly as strict as the
            // village validator about the plots it needs, so a plan
            // that builds a village at all also answers for the dog
            // and the firewood cart.
            AlpineVillageSoundscapePlan soundscape =
                AlpineVillageSoundscapePlanner.Create(plan);
            AlpineVillageSoundAnchorDescriptor dog =
                soundscape.GetRequiredAnchor(
                    AlpineVillageSoundKind.DogBehindFence);
            AlpineVillageSoundAnchorDescriptor firewood =
                soundscape.GetRequiredAnchor(
                    AlpineVillageSoundKind.FirewoodInMineCart);
            AlpineVillagePlotDescriptor dogHouse =
                FindDogHouse(plan);
            Func<Vector2, bool> excluded =
                BuildExclusion(plan, dog);

            var roosts = new List<RavenRoostDescriptor>(3);
            var acceptedAnchors = new List<Vector2>(3);
            AlpineVillagePlotDescriptor adit =
                FindPlot(plan, AlpineVillagePlotKind.Adit);
            if (adit != null)
            {
                TryAddAditRoost(
                    adit, ground, excluded,
                    roosts, acceptedAnchors);
                TryAddWoodpileRoost(
                    adit, firewood, ground, excluded,
                    roosts, acceptedAnchors);
            }

            TryAddLaneFenceRoost(
                plan, dogHouse, dog, ground, excluded,
                roosts, acceptedAnchors);
            return new ReadOnlyCollection<RavenRoostDescriptor>(roosts);
        }

        /// <summary>
        /// A pair on the spoil-heap edge before the adit mouth —
        /// bare worked ground with the dark opening behind it, gravel
        /// and stone for a backdrop exactly as the art rule wants.
        /// </summary>
        private static void TryAddAditRoost(
            AlpineVillagePlotDescriptor adit,
            ICityMapTeleportGround ground,
            Func<Vector2, bool> excluded,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            Vector3 anchor = adit.GroundCenter +
                adit.Facing *
                (adit.FootprintSize.y * 0.5f +
                 AditMouthStandOffMeters);
            TryAddTerrainRoost(
                "village-roost-adit",
                new Vector2(anchor.x, anchor.z),
                adit.GroundCenter,
                ground,
                excluded,
                roosts,
                acceptedAnchors);
        }

        /// <summary>
        /// A pair beside the firewood mine-cart — the village's
        /// deliberately unremarkable site. The cart stands behind the
        /// adit plot, so on the default village this anchor lands
        /// well inside the adit roost's spacing circle and the greedy
        /// pass drops it, leaving two roosts; the row is still
        /// authored because a reseeded village that pushes the two
        /// apart fields it with no code change, and the priority
        /// order (mouth first, woodpile second) is the deliberate
        /// tie-break.
        /// </summary>
        private static void TryAddWoodpileRoost(
            AlpineVillagePlotDescriptor adit,
            AlpineVillageSoundAnchorDescriptor firewood,
            ICityMapTeleportGround ground,
            Func<Vector2, bool> excluded,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            // OwnerPosition is the cart itself (the sound sits a
            // hand's width above it); the bird stands a step toward
            // the lane side of the cart.
            Vector3 anchor = firewood.OwnerPosition +
                             adit.Facing * FirewoodStandOffMeters;
            TryAddTerrainRoost(
                "village-roost-woodpile",
                new Vector2(anchor.x, anchor.z),
                firewood.OwnerPosition,
                ground,
                excluded,
                roosts,
                acceptedAnchors);
        }

        /// <summary>
        /// A pair at one house's cable-gate post on the lane fence,
        /// hunched against the gale. The owner house is never the dog
        /// house, and the gate must clear the dog's anchor by
        /// <see cref="DogClearanceMeters"/> — the dressing planner
        /// computes a gate position for any ordinary house, so the
        /// candidates walk the houses in stable-id order and the
        /// first legal fence wins. The gate position samples its own
        /// terrain height, so perch A takes it verbatim.
        /// </summary>
        private static void TryAddLaneFenceRoost(
            AlpineVillagePlan plan,
            AlpineVillagePlotDescriptor dogHouse,
            AlpineVillageSoundAnchorDescriptor dog,
            ICityMapTeleportGround ground,
            Func<Vector2, bool> excluded,
            List<RavenRoostDescriptor> roosts,
            List<Vector2> acceptedAnchors)
        {
            const string id = "village-roost-lane-fence";
            var candidates = new List<AlpineVillagePlotDescriptor>(
                plan.Plots.Count);
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor plot = plan.Plots[index];
                if (plot.Kind == AlpineVillagePlotKind.House &&
                    !ReferenceEquals(plot, dogHouse))
                {
                    candidates.Add(plot);
                }
            }

            candidates.Sort((left, right) =>
                string.CompareOrdinal(left.StableId, right.StableId));
            var dogXZ = new Vector2(
                dog.OwnerPosition.x,
                dog.OwnerPosition.z);
            for (int index = 0; index < candidates.Count; index++)
            {
                AlpineVillagePlotDescriptor house = candidates[index];
                Vector3 gate = AlpineVillageDressingPlanner
                    .GetCableGatePosition(plan, house);
                var gateXZ = new Vector2(gate.x, gate.z);
                if (Vector2.Distance(gateXZ, dogXZ) <
                    DogClearanceMeters ||
                    excluded(gateXZ) ||
                    !IsSpaced(acceptedAnchors, gateXZ))
                {
                    continue;
                }

                // The bird faces the lane the way the house does —
                // an ordinary fence bird, not a sentry watching a
                // door.
                var perchA = new CemeteryRavenPerch(
                    true,
                    id,
                    gate,
                    RavenRoostPlan.ComputeYawToward(
                        gate,
                        gate + house.Facing));
                if (!RavenRoostPlan.TrySelectGroundPerch(
                        id,
                        gate,
                        ground,
                        excluded,
                        out CemeteryRavenPerch perchB))
                {
                    continue;
                }

                roosts.Add(new RavenRoostDescriptor(
                    id,
                    perchA,
                    perchB));
                acceptedAnchors.Add(gateXZ);
                return;
            }
        }

        private static bool TryAddTerrainRoost(
            string stableId,
            Vector2 anchorXZ,
            Vector3 lookTarget,
            ICityMapTeleportGround ground,
            Func<Vector2, bool> excluded,
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
                excluded(resolvedXZ) ||
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
                    excluded,
                    out CemeteryRavenPerch perchB))
            {
                return false;
            }

            roosts.Add(new RavenRoostDescriptor(
                stableId,
                perchA,
                perchB));
            acceptedAnchors.Add(resolvedXZ);
            return true;
        }

        /// <summary>
        /// Everything the village closes to the species, applied to
        /// BOTH perches: the chapel plot with the birds' whole
        /// audible radius (the crime scene), the burial ground, the
        /// mother's house, the station mechanism's pad, and the
        /// dog's circle. Plain plot rectangles carry the last four —
        /// the rule there is "not on it", not "not near it".
        /// </summary>
        private static Func<Vector2, bool> BuildExclusion(
            AlpineVillagePlan plan,
            AlpineVillageSoundAnchorDescriptor dog)
        {
            var rects = new List<Rect>(4);
            AlpineVillagePlotDescriptor chapel = FindPlot(
                plan,
                AlpineVillagePlotKind.Chapel);
            if (chapel != null)
            {
                rects.Add(Inflate(
                    chapel.BoundsXZ,
                    ChapelClearanceMeters));
            }

            AlpineVillagePlotDescriptor cemetery = FindPlot(
                plan,
                AlpineVillagePlotKind.Cemetery);
            if (cemetery != null)
            {
                rects.Add(cemetery.BoundsXZ);
            }

            rects.Add(plan.MothersHouse.BoundsXZ);
            MountainRoadTerminalRect pad = plan.Station.PadArea;
            var dogXZ = new Vector2(
                dog.OwnerPosition.x,
                dog.OwnerPosition.z);
            return point =>
            {
                for (int index = 0; index < rects.Count; index++)
                {
                    if (ContainsInclusive(rects[index], point))
                    {
                        return true;
                    }
                }

                // Probed at the pad's own height: the containment
                // test projects onto the pad's axes, and a probe at
                // y = 0 would fold the pad's full elevation into any
                // stray vertical component of them.
                if (pad.ContainsXZ(
                        new Vector3(point.x, pad.Center.y, point.y)))
                {
                    return true;
                }

                return Vector2.Distance(point, dogXZ) <
                       DogClearanceMeters;
            };
        }

        /// <summary>
        /// The house the dog lives behind — restated from the
        /// soundscape planner's private selection so the fence roost
        /// can avoid it: the closest ordinary house to the authored
        /// lane fraction on the right-hand side, first-found winning
        /// ties, exactly as the audio side picks it.
        /// </summary>
        private static AlpineVillagePlotDescriptor FindDogHouse(
            AlpineVillagePlan plan)
        {
            float target = plan.Lane.Length *
                AlpineVillageSoundscapePlanner.DogHouseLaneFraction;
            AlpineVillagePlotDescriptor best = null;
            float bestDistance = float.PositiveInfinity;
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                AlpineVillagePlotDescriptor candidate =
                    plan.Plots[index];
                if (candidate.Kind != AlpineVillagePlotKind.House ||
                    candidate.Side != 1)
                {
                    continue;
                }

                float distance = Mathf.Abs(
                    candidate.LaneDistance - target);
                if (distance >= bestDistance)
                {
                    continue;
                }

                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        private static AlpineVillagePlotDescriptor FindPlot(
            AlpineVillagePlan plan,
            AlpineVillagePlotKind kind)
        {
            for (int index = 0; index < plan.Plots.Count; index++)
            {
                if (plan.Plots[index].Kind == kind)
                {
                    return plan.Plots[index];
                }
            }

            return null;
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
    }
}
