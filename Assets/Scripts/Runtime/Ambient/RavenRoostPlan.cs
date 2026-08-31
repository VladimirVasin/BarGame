using System;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// One outdoor raven roost: a stable id and the pair's two
    /// perches. It reuses the cemetery's scene-neutral perch carrier
    /// because a roost bird stands exactly the way a grave bird does;
    /// the perch's PlotId field simply carries the roost's stable id —
    /// out on the open streets there is no burial lattice, and the id
    /// is the only registry key a roost has. The descriptor is the
    /// whole contract between a scene's pure roost planner and the
    /// controller that spawns the birds: seeds, staggers and idle
    /// offsets all derive from the id, so a re-planned scene puts the
    /// same birds back with the same manners.
    /// </summary>
    public readonly struct RavenRoostDescriptor
    {
        /// <summary>
        /// Both perches must already be present ones —
        /// <see cref="CemeteryRavenPerch"/>'s constructor takes an
        /// explicit isPresent flag, and a descriptor built over a
        /// defaulted perch would seat a bird at the world origin. A
        /// planner whose perch resolution failed drops the roost
        /// instead of constructing this.
        /// </summary>
        public RavenRoostDescriptor(
            string stableId,
            CemeteryRavenPerch perchA,
            CemeteryRavenPerch perchB)
        {
            if (string.IsNullOrEmpty(stableId))
            {
                throw new ArgumentException(
                    "A roost is addressed by its stable id.",
                    nameof(stableId));
            }

            if (!perchA.IsPresent)
            {
                throw new ArgumentException(
                    "Perch A must be a present perch.",
                    nameof(perchA));
            }

            if (!perchB.IsPresent)
            {
                throw new ArgumentException(
                    "Perch B must be a present perch.",
                    nameof(perchB));
            }

            StableId = stableId;
            PerchA = perchA;
            PerchB = perchB;
        }

        public string StableId { get; }

        /// <summary>The anchor perch: authored against the roost's
        /// own geometry (a coping, a rail top, plaza gravel), with a
        /// plan-supplied height.</summary>
        public CemeteryRavenPerch PerchA { get; }

        /// <summary>The companion perch a few steps off A — authored
        /// on deck-anchored roosts, ring-resolved on terrain ones.
        /// </summary>
        public CemeteryRavenPerch PerchB { get; }

        /// <summary>
        /// The single point the pair's return gate and activation
        /// radius measure from: perch A's position, exactly as the
        /// cemetery controller passes A's distance twice — the pair
        /// has one home, not two.
        /// </summary>
        public Vector3 HomeReference => PerchA.Position;
    }

    /// <summary>
    /// Per-scene tuning for the roost pairs. Every value exists
    /// because the fog does not travel between scenes: visibility is
    /// ExpSq, v(d) = exp(-(density * d)^2), so the city's honest
    /// 46 m takeoff gate would leave a bird 23.9% visible on the
    /// road (density 0.026) and 54.2% visible in the village's base
    /// haze — a visible bird switched off mid-air is exactly the pop
    /// the cemetery pair was built to never show. Flush stays 3.5 m
    /// everywhere: an arm's length is an arm's length in any
    /// weather.
    /// </summary>
    public readonly struct RavenRoostSettings
    {
        /// <summary>
        /// A deactivated roost reactivates only this much INSIDE the
        /// full activation radius. Without the band, a hero strafing
        /// the exact boundary would toggle the roost every frame —
        /// and every re-entry from a non-idle phase constructs a
        /// fresh director, so a boundary flicker would allocate one
        /// per frame. The pedestrian director keeps the same kind of
        /// gap between its spawn and despawn distances.
        /// </summary>
        public const float ReactivationHysteresisMeters = 4f;

        public RavenRoostSettings(
            float flushMeters,
            float returnMeters,
            float doneMeters,
            float spawnMeters,
            float climbMeters,
            float takeoffTimeoutSeconds,
            float climbSpeed,
            float glideSpeed,
            float activationRadiusMeters,
            string logCategory)
        {
            FlushMeters = flushMeters;
            ReturnMeters = returnMeters;
            DoneMeters = doneMeters;
            SpawnMeters = spawnMeters;
            ClimbMeters = climbMeters;
            TakeoffTimeoutSeconds = takeoffTimeoutSeconds;
            ClimbSpeed = climbSpeed;
            GlideSpeed = glideSpeed;
            ActivationRadiusMeters = activationRadiusMeters;
            LogCategory = logCategory ?? string.Empty;
        }

        /// <summary>Closer than this to either bird and both flush.
        /// </summary>
        public float FlushMeters { get; }

        /// <summary>The return gate, measured from the roost's home
        /// reference: 0.7 of the scene's far plane, the cemetery's
        /// own ratio carried onto each scene's plane.</summary>
        public float ReturnMeters { get; }

        /// <summary>Where a takeoff may end — far enough into this
        /// scene's fog (or past its far plane) that hiding the bird
        /// there changes nothing on screen.</summary>
        public float DoneMeters { get; }

        /// <summary>Where arrival-free return flights spawn. Kept
        /// equal to <see cref="DoneMeters"/> by every factory, the
        /// cemetery controller's own coupling: a bird is only ever
        /// created exactly as far out as takeoffs end, past what the
        /// scene can see.</summary>
        public float SpawnMeters { get; }

        /// <summary>How high above its perch a flushed bird ends
        /// its takeoff. The cemetery pair keeps its own 8 m const —
        /// the burial lattice is open ground with nothing to clear —
        /// but a roost bird leaves through built streets, so each
        /// scene names the altitude that tops its own skyline. The
        /// flight model gains all of it inside the first third of
        /// the travel, and the takeoff clearance fan ray-tests that
        /// same climb.</summary>
        public float ClimbMeters { get; }

        /// <summary>The guard against a degenerate takeoff bearing,
        /// sized to each scene's longer travel at its own climb
        /// speed.</summary>
        public float TakeoffTimeoutSeconds { get; }

        /// <summary>Climb speed in m/s, fed to the flight model. The
        /// mountain scenes fly faster because their gates sit twice
        /// as far out and a 6.5 m/s bird would flap in view for a
        /// quarter of a minute.</summary>
        public float ClimbSpeed { get; }

        /// <summary>Return glide speed in m/s.</summary>
        public float GlideSpeed { get; }

        /// <summary>Past this planar distance from the home
        /// reference the whole roost stops ticking: actors disabled,
        /// renderers off, voices silent. The gate keeps twenty-odd
        /// bird renderers per scene from costing anything while the
        /// hero is districts away.</summary>
        public float ActivationRadiusMeters { get; }

        /// <summary>The scene's own GameLog category, so a roost
        /// event reads in the log beside the scene that owns it.
        /// </summary>
        public string LogCategory { get; }

        /// <summary>
        /// The city keeps the cemetery pair's gates and speeds —
        /// same fog, same far plane — with one exception: the
        /// climb. The cemetery's 8 m suits its open lattice, but a
        /// bird leaving a street at 8 m skims the facades, so the
        /// city climbs 16 m, above its rooflines. Activation 88 m
        /// mirrors CityPedestrianDirector.DespawnDistance, the
        /// city's one proven "far enough to stop simulating"
        /// figure.
        /// </summary>
        public static RavenRoostSettings City =>
            new RavenRoostSettings(
                CemeteryRavenDirectorModel.FlushDistanceMeters,
                CemeteryRavenDirectorModel.ReturnDistanceMeters,
                CemeteryRavenFlightModel.DoneDistanceMeters,
                CemeteryRavenFlightModel.DoneDistanceMeters,
                16f,
                CemeteryRavenFlightModel.TakeoffTimeoutSeconds,
                CemeteryRavenFlightModel.ClimbSpeedMetersPerSecond,
                CemeteryRavenFlightModel.GlideSpeedMetersPerSecond,
                88f,
                "city");

        /// <summary>
        /// The road's thin fog (density 0.026, plane 120): the gates
        /// ride the far plane — return 0.7 of it (84 m, 0.85%
        /// visibility), takeoff done at 96 m where 0.20% of a bird
        /// survives, half the city gate's own residue. The 46 m
        /// default would leave 23.9% of the bird to vanish in one
        /// frame. Timeout 14 s covers 96 m at the 9 m/s climb with
        /// margin. Climb 12 m: the road has no skyline, only the
        /// portal brow, the gallery roofs and the bridge ironwork
        /// to top.
        /// </summary>
        public static RavenRoostSettings MountainRoad =>
            new RavenRoostSettings(
                CemeteryRavenDirectorModel.FlushDistanceMeters,
                0.7f * RuntimeSceneSetup.MountainRoadFarClipPlane,
                96f,
                96f,
                12f,
                14f,
                9f,
                9f,
                RuntimeSceneSetup.MountainRoadFarClipPlane,
                "mountain_road");

        /// <summary>
        /// The village's haze breathes 0.017 to 0.045 by the second,
        /// so no fog-keyed gate is stationary; the done/spawn gate is
        /// keyed on the FAR PLANE instead — 112 m sits past the
        /// 110 m plane, where clipping hides the bird regardless of
        /// the gust, and at the plane crossing base haze leaves 3.0%
        /// of a sub-pixel bird against the ridge handoff. Return 77 m
        /// is 0.7 of the plane; 18% base visibility there is accepted
        /// because the return key is the hero's PLANAR distance, and
        /// in a gust the same spot reads ~0%. Climb 10 m tops the
        /// chalets' ridge lines — the village stands low.
        /// </summary>
        public static RavenRoostSettings AlpineVillage =>
            new RavenRoostSettings(
                CemeteryRavenDirectorModel.FlushDistanceMeters,
                0.7f * RuntimeSceneSetup.AlpineVillageFarClipPlane,
                RuntimeSceneSetup.AlpineVillageFarClipPlane + 2f,
                RuntimeSceneSetup.AlpineVillageFarClipPlane + 2f,
                10f,
                16f,
                9f,
                9f,
                RuntimeSceneSetup.AlpineVillageFarClipPlane,
                "alpine_village");
    }

    /// <summary>
    /// Pure geometry shared by the three scene roost planners:
    /// ground-perch resolution, the yaw rule, and the seed
    /// derivations. Nothing here touches a scene — every planner
    /// stays EditMode-testable, the cemetery plan's own contract.
    ///
    /// Seeds pass through <see cref="CemeteryRavenPlan"/> unchanged:
    /// its plot-id parameter already generalizes to any stable id,
    /// and one shared derivation means a roost bird and a grave bird
    /// draw their manners from the same well instead of two schemes
    /// drifting apart.
    /// </summary>
    public static class RavenRoostPlan
    {
        /// <summary>Probe directions around perch A. Sixteen at
        /// 22.5 degrees resolves against street-width geometry
        /// without ever scanning finer than the mask can answer.
        /// </summary>
        public const int GroundPerchRayCount = 16;

        public const float GroundPerchRingStepMeters = 0.5f;

        /// <summary>
        /// The widening fallback's end, past the cemetery band's
        /// 7 m preference. Unlike the cemetery — whose lattice
        /// guarantees SOME vacant plot, so its band is a preference
        /// and never a veto — an open street can genuinely offer
        /// nothing standable, and past 9 m the two birds stop
        /// reading as one pair; the roost is dropped instead.
        /// </summary>
        public const float GroundPerchFallbackMaximumMeters = 9f;

        /// <summary>
        /// The takeoff fan's candidate azimuths, in degrees off the
        /// straight away-from-hero bearing. Nine lines spanning most
        /// of the half-plane behind the bird: wide enough that some
        /// street canyon usually opens, narrow enough that every
        /// candidate still reads as "startled AWAY from the man".
        /// </summary>
        private static readonly float[] TakeoffFanDegrees =
            { 0f, 25f, -25f, 50f, -50f, 75f, -75f, 105f, -105f };

        /// <summary>
        /// Resolves the companion perch B for a TERRAIN-grounded
        /// roost: a seeded 16-ray ring around perch A, radii
        /// preferred at the cemetery's 3.5-7.0 m band and widened to
        /// 9 m, probed through the area's teleport ground — the one
        /// authority on "can something stand here" that already
        /// subtracts water, buildings and blocked footprints.
        ///
        /// Deck-anchored roosts (a mol coping, a barge gunwale, a
        /// landing platform, a bridge deck) must NOT come here: the
        /// teleport grounds know nothing about deck heights — water
        /// is skipped, decks are not surfaces — so their probes
        /// either fail or clamp to the nearest bank a level away.
        /// Those roosts author perch B with a plan-supplied Y,
        /// exactly as their perch A already does.
        ///
        /// The RESOLVED point is re-checked against the band, not
        /// just the probe: the ground answers with the nearest point
        /// it accepts, and a mask clamp can move an in-band probe to
        /// an out-of-band answer — under A's own feet or a street
        /// away. Height comes back down by
        /// <see cref="PlayerFactory.GroundedRootOffset"/> because
        /// the teleport wrappers ADD that capsule skin offset for
        /// the player, and a bird's root sits on the surface itself.
        ///
        /// Returns false when nothing fits; the caller drops the
        /// roost (and logs it) rather than forcing a perch — a bird
        /// wedged inside a wall is worse than no bird.
        /// </summary>
        public static bool TrySelectGroundPerch(
            string roostStableId,
            Vector3 perchAPosition,
            ICityMapTeleportGround ground,
            Func<Vector2, bool> excluded,
            out CemeteryRavenPerch perch)
        {
            if (string.IsNullOrEmpty(roostStableId))
            {
                throw new ArgumentException(
                    "A roost is addressed by its stable id.",
                    nameof(roostStableId));
            }

            if (ground == null)
            {
                throw new ArgumentNullException(nameof(ground));
            }

            var anchorXZ = new Vector2(
                perchAPosition.x,
                perchAPosition.z);

            // The direction ORDER is seeded from the id so different
            // roosts open their search on different compass sides:
            // sixteen pairs all seating their ground bird due north
            // of the landmark would read as one authored rule.
            int startOrdinal = (int)(
                StableHash(roostStableId) %
                (uint)GroundPerchRayCount);
            int radiusSteps = Mathf.RoundToInt(
                (GroundPerchFallbackMaximumMeters -
                 CemeteryRavenPlan.GroundPerchBandMinimumMeters) /
                GroundPerchRingStepMeters);

            for (int step = 0; step <= radiusSteps; step++)
            {
                float radius =
                    CemeteryRavenPlan.GroundPerchBandMinimumMeters +
                    step * GroundPerchRingStepMeters;
                for (int ray = 0; ray < GroundPerchRayCount; ray++)
                {
                    int ordinal =
                        (startOrdinal + ray) % GroundPerchRayCount;
                    float angleRadians =
                        ordinal *
                        (Mathf.PI * 2f / GroundPerchRayCount);
                    var direction = new Vector2(
                        Mathf.Sin(angleRadians),
                        Mathf.Cos(angleRadians));
                    Vector2 probe = anchorXZ + direction * radius;
                    if (!ground.TryResolveStandingPosition(
                            probe,
                            out Vector3 standing))
                    {
                        continue;
                    }

                    var resolvedXZ = new Vector2(
                        standing.x,
                        standing.z);
                    float resolvedDistance = Vector2.Distance(
                        resolvedXZ,
                        anchorXZ);
                    if (resolvedDistance <
                        CemeteryRavenPlan
                            .GroundPerchBandMinimumMeters ||
                        resolvedDistance >
                        GroundPerchFallbackMaximumMeters)
                    {
                        continue;
                    }

                    if (excluded != null && excluded(resolvedXZ))
                    {
                        continue;
                    }

                    var position = new Vector3(
                        resolvedXZ.x,
                        standing.y -
                        PlayerFactory.GroundedRootOffset,
                        resolvedXZ.y);
                    perch = new CemeteryRavenPerch(
                        true,
                        roostStableId,
                        position,
                        ComputeYawToward(position, perchAPosition));
                    return true;
                }
            }

            perch = default;
            return false;
        }

        /// <summary>Pass-through to the cemetery derivation — the
        /// roost's stable id stands in for the sealed plot id, one
        /// entropy scheme for every raven in the game.</summary>
        public static int DeriveRavenSeed(
            int areaSeed,
            string roostStableId,
            int ravenIndex)
        {
            return CemeteryRavenPlan.DeriveRavenSeed(
                areaSeed,
                roostStableId,
                ravenIndex);
        }

        /// <summary>Pass-through: one bird's idle timeline start
        /// offset, so a pair never preens in lockstep.</summary>
        public static double DeriveIdleStartOffsetSeconds(
            int ravenSeed)
        {
            return CemeteryRavenPlan.DeriveIdleStartOffsetSeconds(
                ravenSeed);
        }

        /// <summary>
        /// Picks a flushed bird's takeoff bearing: the
        /// away-from-hero line and eight alternates
        /// (<see cref="TakeoffFanDegrees"/>), walked in an order
        /// shuffled deterministically by the flight seed, taking the
        /// first direction the caller's clearance probe accepts.
        /// This is deliberately NOT pathfinding — the PS1 bird owns
        /// no map and plans no route; it simply refuses to fly
        /// through the first wall in front of it, and past the
        /// probed metres the fog owns the problem. The seeded
        /// shuffle also answers the playtest's "always the same
        /// line": consecutive flushes draw different orders even
        /// when every candidate is clear. When nothing is clear the
        /// last-tried candidate is flown — a bird hemmed in on nine
        /// sides still leaves, exactly as a real one would, and the
        /// clip is the price of the corner.
        /// <paramref name="awayDirection"/> is the controller's own
        /// planar away-from-hero unit bearing; the fan only ever
        /// yaws it, so candidates stay planar and unit-length.
        /// </summary>
        public static Vector3 SelectTakeoffAzimuth(
            Vector3 awayDirection,
            int flightSeed,
            Func<Vector3, bool> isClear)
        {
            if (isClear == null)
            {
                throw new ArgumentNullException(nameof(isClear));
            }

            var order = new int[TakeoffFanDegrees.Length];
            for (int index = 0; index < order.Length; index++)
            {
                order[index] = index;
            }

            // Fisher-Yates over the candidate ordinals, each draw
            // taken from a seed chain through the shared mixer:
            // pure, so one flight's fan is the same fan on every
            // machine and in every replay of that flush.
            uint state = unchecked((uint)flightSeed);
            for (int index = order.Length - 1; index > 0; index--)
            {
                state = MixHash(unchecked(
                    state ^ ((uint)index * 0x9E3779B9u)));
                int swap = (int)(state % (uint)(index + 1));
                int held = order[index];
                order[index] = order[swap];
                order[swap] = held;
            }

            Vector3 candidate = awayDirection;
            for (int index = 0; index < order.Length; index++)
            {
                candidate = Quaternion.Euler(
                    0f,
                    TakeoffFanDegrees[order[index]],
                    0f) * awayDirection;
                if (isClear(candidate))
                {
                    return candidate;
                }
            }

            return candidate;
        }

        /// <summary>
        /// Compass yaw from one point toward another, restated from
        /// <see cref="CemeteryRavenPlan"/> where it is private: the
        /// companion bird faces its anchor the way the cemetery's
        /// ground bird faces the grave — a pair reads as a pair
        /// because its heads relate.
        /// </summary>
        public static float ComputeYawToward(
            Vector3 from,
            Vector3 to)
        {
            float dx = to.x - from.x;
            float dz = to.z - from.z;
            if (dx * dx + dz * dz < 0.000001f)
            {
                return 0f;
            }

            return Mathf.Atan2(dx, dz) * Mathf.Rad2Deg;
        }

        /// <summary>
        /// FNV-1a over the roost's stable id, restated from
        /// <see cref="CemeteryRavenPlan"/> where it is private. The
        /// id is the roost's only entropy, and FNV keeps the probe
        /// order stable across runs and machines.
        /// </summary>
        private static uint StableHash(string value)
        {
            unchecked
            {
                uint hash = 2166136261u;
                string text = value ?? string.Empty;
                for (int index = 0; index < text.Length; index++)
                {
                    hash ^= text[index];
                    hash *= 16777619u;
                }

                return hash;
            }
        }

        /// <summary>The raven controllers' own uint mixer, restated
        /// for the fan shuffle: the same avalanche the takeoff arcs
        /// and degenerate bearings already draw their entropy from.
        /// </summary>
        private static uint MixHash(uint value)
        {
            unchecked
            {
                value ^= value >> 16;
                value *= 0x7feb352du;
                value ^= value >> 15;
                value *= 0x846ca68bu;
                value ^= value >> 16;
                return value;
            }
        }
    }
}
