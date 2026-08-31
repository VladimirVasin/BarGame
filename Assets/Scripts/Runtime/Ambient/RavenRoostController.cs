using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The outdoor roost pairs' thin scene adapter: the cemetery
    /// raven controller's polling loop, restated for N triggerless
    /// roosts on one scene. All decisions stay in the pure
    /// <see cref="CemeteryRavenDirectorModel"/> — this component only
    /// measures hero distances, polls the scene's session provider,
    /// feeds each roost's machine and executes whatever phase comes
    /// back, exactly as the cemetery Update does minus everything an
    /// open street cannot have: no gravedigging ledger, no
    /// ground-perch displacement, no RelocatingB — a kerb is never
    /// claimed for a grave. GroundPerchDisplaced is therefore polled
    /// as a constant false, and the phases that hang off it are
    /// omitted rather than cloned dead: with every director armed
    /// alreadySealedAtFirstPoll, WaitingToArrive, ArrivalFlight and
    /// RelocatingB are unreachable by construction.
    ///
    /// The pairs arm that way because a roost has no trigger event
    /// the way the grave pair has a sealing: these birds have simply
    /// always been there, so the first thing the hero can ever see
    /// is a perched pair — never a landing out of thin air.
    ///
    /// What the cemetery never needed is the activation radius. One
    /// pair at one grave always ticks; up to eight roosts of two
    /// birds each would tick, look around and render across a whole
    /// map. Past <see cref="RavenRoostSettings.ActivationRadiusMeters"/>
    /// (planar, from the roost's home reference) a roost freezes
    /// wholesale, and re-entry — a hysteresis band inside the radius,
    /// so a hero strafing the boundary never toggles it — snaps the
    /// pair to PerchedIdle, through a fresh director when the frozen
    /// one was mid-story, rather than resuming a flight nobody could
    /// have watched: an event without a witness is an event that did
    /// not happen, the cemetery's own philosophy.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class RavenRoostController : MonoBehaviour
    {
        public const string RuntimeObjectName =
            "Raven Roost Controller";

        /// <summary>Each roost's child host carries its stable id so
        /// a hierarchy dump reads like the plan's table — and so the
        /// audio discipline stays countable per roost: exactly two
        /// AudioSources under one roost host, never more.</summary>
        public const string RoostHostNamePrefix = "Raven Roost ";

        public const string RavenAHostName = "Raven A";
        public const string RavenBHostName = "Raven B";

        private IReadOnlyList<RavenRoostDescriptor> descriptors;
        private RavenRoostSettings settings;
        private Transform player;
        private Func<bool> sessionActive;
        private int areaSeed;

        private bool inert;
        private RavenCallClipCache.Lease clipLease;
        private readonly List<Roost> roosts = new List<Roost>();

        /// <summary>How many roosts actually stood up. Zero on an
        /// inert controller (missing art provider).</summary>
        public int RoostCount => roosts.Count;

        /// <summary>The named roost's machine phase. Always answers —
        /// a frozen (deactivated) roost keeps its director, parked in
        /// whatever phase the freeze caught.</summary>
        public CemeteryRavenPhase GetRoostPhase(int roostIndex)
        {
            return roosts[roostIndex].Director.Phase;
        }

        /// <summary>True when the roost armed with no arrival to
        /// replay — which every roost does, and a fresh re-entry
        /// director does again; pinned by tests as the proof that no
        /// arrival flight can ever have played.</summary>
        public bool DidRoostSpawnPerchedWithoutArrival(int roostIndex)
        {
            return roosts[roostIndex]
                .Director.SpawnedPerchedWithoutArrival;
        }

        /// <summary>False while the roost is frozen beyond the
        /// activation radius: actors disabled, renderers off, voices
        /// silent, director not advanced.</summary>
        public bool IsRoostActive(int roostIndex)
        {
            return roosts[roostIndex].IsActive;
        }

        /// <summary>The roost's child host — the subtree budget
        /// sweeps count against (two AudioSources, zero Lights).
        /// </summary>
        public Transform GetRoostHost(int roostIndex)
        {
            return roosts[roostIndex].Host;
        }

        /// <summary>
        /// Builds one controller owning every roost of a scene.
        /// <paramref name="parent"/> must be the scene's ROOT
        /// transform, never a decorated world root — the root-scoped
        /// light and audio sweeps stay honest only because nothing
        /// ambient hides under the roots they count.
        ///
        /// An empty (or absent) roost list returns null outright: a
        /// custom blueprint whose planner found no legal roost simply
        /// has no roost birds, the cemetery's own silent-absence rule
        /// for a plan-less yard. A null session provider reads as "no
        /// session ever active", exactly as the cemetery treats a
        /// missing grave-work controller in headless composition.
        /// </summary>
        public static RavenRoostController Create(
            Transform parent,
            IReadOnlyList<RavenRoostDescriptor> roosts,
            RavenRoostSettings settings,
            Transform player,
            Func<bool> sessionActive,
            int areaSeed)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            if (roosts == null || roosts.Count == 0)
            {
                return null;
            }

            var controller = new GameObject(RuntimeObjectName)
                .AddComponent<RavenRoostController>();
            controller.transform.SetParent(parent, false);
            controller.descriptors = roosts;
            controller.settings = settings;
            controller.player = player;
            controller.sessionActive = sessionActive;
            controller.areaSeed = areaSeed;
            controller.BuildRoosts();
            return controller;
        }

        /// <summary>
        /// Spawns every roost up front — there is nothing to wait
        /// for, unlike the cemetery's sealed-grave flag — over ONE
        /// clip-cache lease shared by all the voices. Any bird the
        /// art provider cannot dress makes the WHOLE controller
        /// inert: half a scene's roosts standing while the other half
        /// silently failed would be a lie about the world, and the
        /// factory's degrade-don't-throw rule already covers the
        /// only failure this path can see.
        /// </summary>
        private void BuildRoosts()
        {
            clipLease = RavenCallClipCache.Acquire();
            for (int index = 0; index < descriptors.Count; index++)
            {
                if (!TrySpawnRoost(descriptors[index]))
                {
                    TearDownRoosts();
                    if (clipLease != null)
                    {
                        clipLease.Dispose();
                        clipLease = null;
                    }

                    GameLog.Warning(
                        settings.LogCategory,
                        "raven_roost_provider_missing");
                    inert = true;
                    return;
                }
            }

            GameLog.Info(
                settings.LogCategory,
                "raven_roost_spawned",
                GameLog.Field("count", roosts.Count));
        }

        private bool TrySpawnRoost(in RavenRoostDescriptor descriptor)
        {
            var host = new GameObject(
                RoostHostNamePrefix + descriptor.StableId);
            host.transform.SetParent(transform, false);
            var roost = new Roost(descriptor, host.transform);
            // Registered before the birds spawn so a mid-roost
            // failure is torn down with everything else.
            roosts.Add(roost);

            roost.Seeds[CemeteryRavenDirectorModel.RavenAIndex] =
                RavenRoostPlan.DeriveRavenSeed(
                    areaSeed,
                    descriptor.StableId,
                    CemeteryRavenDirectorModel.RavenAIndex);
            roost.Seeds[CemeteryRavenDirectorModel.RavenBIndex] =
                RavenRoostPlan.DeriveRavenSeed(
                    areaSeed,
                    descriptor.StableId,
                    CemeteryRavenDirectorModel.RavenBIndex);

            for (int index = 0;
                 index < roost.Actors.Length;
                 index++)
            {
                if (!TrySpawnRaven(roost, index))
                {
                    return false;
                }
            }

            // Armed as "already sealed": PerchedIdle from the first
            // instant, no arrival to replay — the roost pairs' whole
            // premise.
            roost.Director = new CemeteryRavenDirectorModel(
                roost.Seeds[CemeteryRavenDirectorModel.RavenAIndex],
                roost.Seeds[CemeteryRavenDirectorModel.RavenBIndex],
                settings.FlushMeters,
                settings.ReturnMeters);
            roost.Director.Arm(true);
            roost.LastPhase = roost.Director.Phase;

            for (int index = 0;
                 index < roost.Actors.Length;
                 index++)
            {
                CemeteryRavenPerch perch = GetPerch(roost, index);
                roost.Actors[index].SetPerched(
                    perch.Position,
                    perch.YawDegrees);
                roost.Actors[index].SetVisible(true);
            }

            return true;
        }

        /// <summary>
        /// One bird, exactly as the cemetery spawns its own: host,
        /// authored visual, actor over the untouched pure timelines —
        /// only the voice differs, built over the shared clip lease
        /// because dozens of roost voices playing byte-identical
        /// buffers have no business each owning a copy.
        /// </summary>
        private bool TrySpawnRaven(Roost roost, int ravenIndex)
        {
            var host = new GameObject(
                ravenIndex == CemeteryRavenDirectorModel.RavenAIndex
                    ? RavenAHostName
                    : RavenBHostName);
            host.transform.SetParent(roost.Host, false);
            CemeteryRavenRigAnchors anchors =
                CemeteryRavenFactory.CreateVisual(host.transform);
            if (anchors == null)
            {
                // The factory already logged the missing provider
                // under the city category; the roost-level warning is
                // the caller's, once, per scene.
                if (Application.isPlaying)
                {
                    Destroy(host);
                }
                else
                {
                    DestroyImmediate(host);
                }

                return false;
            }

            var actor = host.AddComponent<CemeteryRavenActor>();
            actor.Initialize(
                anchors,
                roost.Seeds[ravenIndex],
                RavenRoostPlan.DeriveIdleStartOffsetSeconds(
                    roost.Seeds[ravenIndex]));
            roost.Actors[ravenIndex] = actor;
            roost.Voices[ravenIndex] = CemeteryRavenVoice.Create(
                host.transform,
                roost.Seeds[ravenIndex],
                clipLease.Clips);
            return true;
        }

        private void Update()
        {
            if (player == null || inert)
            {
                return;
            }

            float delta = Time.deltaTime;
            bool session =
                sessionActive != null && sessionActive();
            Vector3 heroPosition = player.position;
            for (int index = 0; index < roosts.Count; index++)
            {
                Roost roost = roosts[index];
                float homeDistance = PlanarDistance(
                    heroPosition,
                    roost.Descriptor.HomeReference);
                if (roost.IsActive)
                {
                    if (homeDistance >
                        settings.ActivationRadiusMeters)
                    {
                        Deactivate(roost);
                        continue;
                    }

                    AdvanceRoost(
                        roost,
                        delta,
                        session,
                        heroPosition,
                        homeDistance);
                }
                else if (homeDistance <=
                         settings.ActivationRadiusMeters -
                         RavenRoostSettings
                             .ReactivationHysteresisMeters)
                {
                    // The fresh-director snap happens in here, ONCE,
                    // on the actual crossing — never per frame.
                    Reactivate(roost);
                    AdvanceRoost(
                        roost,
                        delta,
                        session,
                        heroPosition,
                        homeDistance);
                }
            }
        }

        /// <summary>
        /// The cemetery's Update body for one roost: measure, feed
        /// the machine, execute the phase, push the head target,
        /// advance the voices, flush the staggered caw. The caw
        /// separation clock is PER ROOST — two roosts seventy meters
        /// apart may cry in the same instant, and only two birds of
        /// ONE pair atop each other would read as a single loud bird.
        /// </summary>
        private void AdvanceRoost(
            Roost roost,
            float delta,
            bool session,
            Vector3 heroPosition,
            float homeDistance)
        {
            roost.ClockSeconds += delta;
            // The roost's home reference IS perch A, so the return
            // gate measures the same distance as A's flush gate —
            // the cemetery passes A's distance twice for the same
            // reason: the pair has one home, not two.
            float distanceToA = homeDistance;
            float distanceToB = PlanarDistance(
                heroPosition,
                roost.Descriptor.PerchB.Position);
            var input = new CemeteryRavenDirectorInput(
                distanceToA,
                distanceToB,
                distanceToA,
                session,
                false,
                GetFlightDone(
                    roost,
                    CemeteryRavenDirectorModel.RavenAIndex),
                GetFlightDone(
                    roost,
                    CemeteryRavenDirectorModel.RavenBIndex));
            roost.Director.Advance(delta, input);
            if (roost.Director.Phase != roost.LastPhase)
            {
                OnPhaseEntered(roost, roost.Director.Phase);
                roost.LastPhase = roost.Director.Phase;
            }

            ExecutePhase(roost);

            // The head target: the hero, unless a session hides him —
            // a bird visibly following an invisible man through
            // somebody else's shot would give the trick away.
            bool hasHeadTarget =
                !roost.Director.IsHeadTargetSuppressed;
            for (int index = 0;
                 index < roost.Actors.Length;
                 index++)
            {
                if (roost.Actors[index] != null)
                {
                    roost.Actors[index].SetHeadTarget(
                        hasHeadTarget,
                        heroPosition);
                }
            }

            bool canCall =
                roost.Director.Phase ==
                CemeteryRavenPhase.PerchedIdle &&
                !session;
            for (int index = 0;
                 index < roost.Voices.Length;
                 index++)
            {
                roost.Voices[index]?.Advance(delta, canCall);
            }

            FlushPendingTakeoffCaw(roost);
        }

        /// <summary>
        /// Freezes the roost the moment the hero is past the
        /// activation radius. Any flight is cancelled by seating the
        /// bird — <see cref="CemeteryRavenActor.SetPerched"/> drops
        /// the flight model on the floor — because whatever the pair
        /// was doing out there, nobody was watching it. Renderers
        /// off, actors disabled (their self-tick with them), the caw
        /// clock and voices simply not advanced, and the pending
        /// staggered caw cleared: a supposedly inert roost must not
        /// owe the world a one-shot.
        /// </summary>
        private void Deactivate(Roost roost)
        {
            for (int index = 0;
                 index < roost.Actors.Length;
                 index++)
            {
                CemeteryRavenActor actor = roost.Actors[index];
                if (actor == null)
                {
                    continue;
                }

                CemeteryRavenPerch perch = GetPerch(roost, index);
                actor.SetPerched(perch.Position, perch.YawDegrees);
                actor.SetVisible(false);
                actor.enabled = false;
                roost.FlightStarted[index] = false;
            }

            roost.PendingCawRaven = -1;
            roost.IsActive = false;
        }

        /// <summary>
        /// Thaws the roost on re-entry, always into PerchedIdle. A
        /// director frozen mid-story (Startled, Away, ReturnFlight)
        /// is replaced by a fresh one with the SAME seeds and gates,
        /// armed as already-sealed: the pure model has no reset, a
        /// fresh instance is deterministic, and snapping to the
        /// perches is the only honest option — replaying a flight the
        /// hero walked away from would show him an event that
        /// happened while nobody was there.
        /// </summary>
        private void Reactivate(Roost roost)
        {
            if (roost.Director.Phase !=
                CemeteryRavenPhase.PerchedIdle)
            {
                roost.Director = new CemeteryRavenDirectorModel(
                    roost.Seeds[
                        CemeteryRavenDirectorModel.RavenAIndex],
                    roost.Seeds[
                        CemeteryRavenDirectorModel.RavenBIndex],
                    settings.FlushMeters,
                    settings.ReturnMeters);
                roost.Director.Arm(true);
            }

            roost.LastPhase = roost.Director.Phase;
            for (int index = 0;
                 index < roost.Actors.Length;
                 index++)
            {
                CemeteryRavenActor actor = roost.Actors[index];
                if (actor == null)
                {
                    continue;
                }

                CemeteryRavenPerch perch = GetPerch(roost, index);
                actor.enabled = true;
                actor.SetPerched(perch.Position, perch.YawDegrees);
                actor.SetVisible(true);
                roost.FlightStarted[index] = false;
            }

            roost.IsActive = true;
        }

        private void OnDestroy()
        {
            TearDownRoosts();
            // The lease goes LAST — the cache's disposal contract is
            // "voices before lease", because the last lease's
            // disposal buries the shared clips out from under any
            // voice still holding them.
            if (clipLease != null)
            {
                clipLease.Dispose();
                clipLease = null;
            }
        }

        private void TearDownRoosts()
        {
            for (int index = 0; index < roosts.Count; index++)
            {
                Roost roost = roosts[index];
                for (int voice = 0;
                     voice < roost.Voices.Length;
                     voice++)
                {
                    roost.Voices[voice]?.Dispose();
                    roost.Voices[voice] = null;
                }

                for (int actor = 0;
                     actor < roost.Actors.Length;
                     actor++)
                {
                    roost.Actors[actor] = null;
                }

                if (roost.Host != null)
                {
                    GameObject host = roost.Host.gameObject;
                    if (Application.isPlaying)
                    {
                        Destroy(host);
                    }
                    else
                    {
                        // EditMode teardown has no deferred frame to
                        // run a queued Destroy in — the village
                        // soundscape's rule, without which the
                        // EditMode leak guard trips on every test.
                        DestroyImmediate(host);
                    }

                    roost.Host = null;
                }
            }

            roosts.Clear();
        }

        private void OnPhaseEntered(
            Roost roost,
            CemeteryRavenPhase phase)
        {
            if (phase == CemeteryRavenPhase.Landing)
            {
                // A continuation of ReturnFlight, not a fresh phase:
                // one bird is still in the air and its started/done
                // bookkeeping must survive the label change.
                return;
            }

            roost.FlightStarted[
                CemeteryRavenDirectorModel.RavenAIndex] = false;
            roost.FlightStarted[
                CemeteryRavenDirectorModel.RavenBIndex] = false;
        }

        /// <summary>
        /// The cemetery's phase executor minus the unreachable
        /// branches: no ArrivalFlight (every director armed as
        /// already-sealed) and no RelocatingB (displacement is polled
        /// false forever).
        /// </summary>
        private void ExecutePhase(Roost roost)
        {
            switch (roost.Director.Phase)
            {
                case CemeteryRavenPhase.ReturnFlight:
                {
                    for (int index = 0;
                         index < roost.Actors.Length;
                         index++)
                    {
                        if (!roost.FlightStarted[index] &&
                            roost.Director.IsFlightDue(index))
                        {
                            StartReturnFlight(
                                roost,
                                index,
                                GetPerch(roost, index));
                        }
                    }

                    break;
                }

                case CemeteryRavenPhase.Startled:
                {
                    for (int index = 0;
                         index < roost.Actors.Length;
                         index++)
                    {
                        if (!roost.FlightStarted[index] &&
                            roost.Director.IsFlightDue(index))
                        {
                            StartTakeoff(roost, index, true);
                        }
                    }

                    break;
                }
            }
        }

        private bool GetFlightDone(Roost roost, int ravenIndex)
        {
            switch (roost.Director.Phase)
            {
                case CemeteryRavenPhase.Startled:
                case CemeteryRavenPhase.ReturnFlight:
                case CemeteryRavenPhase.Landing:
                    return roost.FlightStarted[ravenIndex] &&
                           roost.Actors[ravenIndex].IsFlightDone;
                default:
                    return false;
            }
        }

        /// <summary>
        /// Where along a takeoff candidate the clearance probe
        /// samples the flight path, in planar metres. Three
        /// stations: inside the climb, just past its top, and well
        /// down the cruise — beyond ~32 m every scene's fog owns
        /// the bird, and nobody can read a clip that far out.
        /// </summary>
        private static readonly float[]
            TakeoffClearanceSampleMeters = { 8f, 18f, 32f };

        /// <summary>
        /// The cemetery takeoff with this scene's gates: the travel
        /// distance, the climb altitude and the flight model's own
        /// done/timeout/speed contract all come from the settings,
        /// because a bird that stopped 46 m out would still be a
        /// fifth visible in the road's thin fog and half visible in
        /// the village's. The bearing goes through the seeded
        /// azimuth fan with a physics clearance probe: the playtest
        /// caught flushed birds flying one scripted low line
        /// THROUGH the buildings, and the PS1-honest answer is not
        /// pathfinding — just a bird that picks, per flush, the
        /// first of nine seeded lines that does not start inside a
        /// wall.
        /// </summary>
        private void StartTakeoff(
            Roost roost,
            int ravenIndex,
            bool playCaw)
        {
            CemeteryRavenActor actor = roost.Actors[ravenIndex];
            Vector3 start = actor.transform.position;
            float startYaw = actor.transform.eulerAngles.y;
            Vector3 away = PlanarDirection(
                roost,
                start - player.position,
                ravenIndex);
            // One seed serves the fan and the flight, derived ONCE:
            // the flight ordinal still advances exactly once per
            // flush, so replays and staggers keep their meaning.
            int flightSeed = DeriveFlightSeed(roost, ravenIndex);
            Vector3 bearing = RavenRoostPlan.SelectTakeoffAzimuth(
                away,
                flightSeed,
                direction => IsTakeoffLineClear(start, direction));
            Vector3 end = start +
                          bearing * settings.DoneMeters +
                          Vector3.up * settings.ClimbMeters;
            actor.BeginFlight(new CemeteryRavenFlightModel(
                start,
                startYaw,
                end,
                YawOf(bearing),
                CemeteryRavenFlightKind.Takeoff,
                flightSeed,
                settings.DoneMeters,
                settings.TakeoffTimeoutSeconds,
                settings.ClimbSpeed,
                settings.GlideSpeed));
            roost.FlightStarted[ravenIndex] = true;
            if (playCaw)
            {
                RequestTakeoffCaw(roost, ravenIndex);
            }
        }

        /// <summary>
        /// True when the takeoff profile along one candidate
        /// bearing hits no collider over the probed stations. The
        /// sample heights ride the flight model's own early-climb
        /// curve, so the segments ray-tested here are chords of the
        /// very path the bird will fly; triggers are ignored and
        /// the default raycast layers stand — a wall is a wall
        /// whoever authored it. Three raycasts per candidate, a
        /// couple of dozen per flush at the very worst: cheap
        /// enough to spend on never flying through the first
        /// facade.
        /// </summary>
        private bool IsTakeoffLineClear(
            Vector3 start,
            Vector3 direction)
        {
            Vector3 previous = start;
            for (int index = 0;
                 index < TakeoffClearanceSampleMeters.Length;
                 index++)
            {
                float planarMeters =
                    TakeoffClearanceSampleMeters[index];
                float climb01 = CemeteryRavenFlightModel
                    .TakeoffClimb01(
                        planarMeters / settings.DoneMeters);
                Vector3 point = start +
                                direction * planarMeters +
                                Vector3.up *
                                (climb01 * settings.ClimbMeters);
                Vector3 leg = point - previous;
                if (Physics.Raycast(
                        previous,
                        leg.normalized,
                        leg.magnitude,
                        Physics.DefaultRaycastLayers,
                        QueryTriggerInteraction.Ignore))
                {
                    return false;
                }

                previous = point;
            }

            return true;
        }

        private void StartReturnFlight(
            Roost roost,
            int ravenIndex,
            in CemeteryRavenPerch perch)
        {
            CemeteryRavenActor actor = roost.Actors[ravenIndex];
            Vector3 target = perch.Position;
            // Spawn on the hero's far side of the perch, bowed off
            // the straight line by a seeded arc: the bird comes out
            // of the fog, never through the man watching for it.
            Vector3 away = PlanarDirection(
                roost,
                target - player.position,
                ravenIndex);
            int seed = DeriveFlightSeed(roost, ravenIndex);
            float arc =
                (Hash01(unchecked((uint)seed ^ 0x5AFEu)) * 2f - 1f) *
                CityCemeteryRavenController.SpawnAzimuthArcDegrees;
            Vector3 bearing = Quaternion.Euler(0f, arc, 0f) * away;
            Vector3 spawn = target +
                            bearing * settings.SpawnMeters +
                            Vector3.up *
                            CityCemeteryRavenController
                                .FlightSpawnHeightMeters;
            actor.BeginFlight(new CemeteryRavenFlightModel(
                spawn,
                YawOf(target - spawn),
                target,
                perch.YawDegrees,
                CemeteryRavenFlightKind.Return,
                seed,
                settings.DoneMeters,
                settings.TakeoffTimeoutSeconds,
                settings.ClimbSpeed,
                settings.GlideSpeed));
            roost.FlightStarted[ravenIndex] = true;
        }

        private static CemeteryRavenPerch GetPerch(
            Roost roost,
            int ravenIndex)
        {
            return ravenIndex ==
                   CemeteryRavenDirectorModel.RavenAIndex
                ? roost.Descriptor.PerchA
                : roost.Descriptor.PerchB;
        }

        /// <summary>Per-flight seed: the raven's own seed folded with
        /// how many flights it has made, so consecutive takeoff arcs
        /// differ while any single flight stays deterministic.</summary>
        private static int DeriveFlightSeed(
            Roost roost,
            int ravenIndex)
        {
            roost.FlightOrdinals[ravenIndex]++;
            unchecked
            {
                return roost.Seeds[ravenIndex] ^
                       (roost.FlightOrdinals[ravenIndex] *
                        0x01000193);
            }
        }

        /// <summary>
        /// One cry per bird per flush, never two inside the same
        /// sixtieth of a second — the cemetery's rule, kept per
        /// roost: only a single pair's caws can smear into one loud
        /// bird, and other roosts are a street away by the spacing
        /// rule.
        /// </summary>
        private void RequestTakeoffCaw(Roost roost, int ravenIndex)
        {
            if (roost.ClockSeconds -
                roost.LastTakeoffCawSeconds >=
                CityCemeteryRavenController
                    .MinimumTakeoffCawSeparationSeconds)
            {
                roost.Voices[ravenIndex]?.PlayTakeoffCaw();
                roost.LastTakeoffCawSeconds = roost.ClockSeconds;
            }
            else
            {
                roost.PendingCawRaven = ravenIndex;
            }
        }

        private void FlushPendingTakeoffCaw(Roost roost)
        {
            if (roost.PendingCawRaven < 0 ||
                roost.ClockSeconds -
                roost.LastTakeoffCawSeconds <
                CityCemeteryRavenController
                    .MinimumTakeoffCawSeparationSeconds)
            {
                return;
            }

            roost.Voices[roost.PendingCawRaven]?.PlayTakeoffCaw();
            roost.LastTakeoffCawSeconds = roost.ClockSeconds;
            roost.PendingCawRaven = -1;
        }

        private Vector3 PlanarDirection(
            Roost roost,
            Vector3 delta,
            int ravenIndex)
        {
            var planar = new Vector3(delta.x, 0f, delta.z);
            if (planar.sqrMagnitude > 0.000001f)
            {
                return planar.normalized;
            }

            // Degenerate geometry (the hero standing exactly on the
            // reference point): any seeded compass bearing is as
            // honest as another.
            float yaw = Hash01(unchecked(
                (uint)roost.Seeds[ravenIndex] ^ 0xD1Bu)) * 360f;
            return Quaternion.Euler(0f, yaw, 0f) * Vector3.forward;
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            float dx = a.x - b.x;
            float dz = a.z - b.z;
            return Mathf.Sqrt(dx * dx + dz * dz);
        }

        private static float YawOf(Vector3 direction)
        {
            return Mathf.Atan2(direction.x, direction.z) *
                   Mathf.Rad2Deg;
        }

        private static float Hash01(uint value)
        {
            value ^= value >> 16;
            value *= 0x7feb352du;
            value ^= value >> 15;
            value *= 0x846ca68bu;
            value ^= value >> 16;
            return (value & 0x00FFFFFFu) / 16777215f;
        }

        /// <summary>
        /// One roost's live state: the descriptor, the two actors and
        /// voices, the director, and the per-roost bookkeeping the
        /// cemetery controller keeps as its own fields — flight
        /// started/ordinal arrays, the caw separation clock, the
        /// pending staggered caw and the activation flag. A plain
        /// nested class rather than a component because none of it is
        /// scene state: the host GameObject already anchors the
        /// hierarchy, and everything here is just the controller's
        /// ledger sliced per roost.
        /// </summary>
        private sealed class Roost
        {
            public Roost(
                in RavenRoostDescriptor descriptor,
                Transform host)
            {
                Descriptor = descriptor;
                Host = host;
            }

            public RavenRoostDescriptor Descriptor { get; }

            public Transform Host;

            public readonly CemeteryRavenActor[] Actors =
                new CemeteryRavenActor[2];

            public readonly CemeteryRavenVoice[] Voices =
                new CemeteryRavenVoice[2];

            public readonly int[] Seeds = new int[2];
            public readonly bool[] FlightStarted = new bool[2];
            public readonly int[] FlightOrdinals = new int[2];

            public CemeteryRavenDirectorModel Director;
            public CemeteryRavenPhase LastPhase;

            /// <summary>Roosts wake ACTIVE and the first Update
            /// freezes the far ones — simpler than re-deriving the
            /// activation rule at spawn, and the freeze lands before
            /// anything renders.</summary>
            public bool IsActive = true;

            public double ClockSeconds;

            public double LastTakeoffCawSeconds =
                double.NegativeInfinity;

            public int PendingCawRaven = -1;
        }
    }
}
