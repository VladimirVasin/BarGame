using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// The cemetery raven pair's thin scene adapter. All decisions
    /// live in the pure <see cref="CemeteryRavenDirectorModel"/>;
    /// this controller only measures the polled inputs — hero
    /// distances, the grave-work session flag, the ledger — feeds the
    /// machine, and executes whatever phase comes back: spawning the
    /// birds perched, flying arrivals and returns, flushing both at
    /// arm's length, relocating raven B off a claimed plot, and
    /// advancing the two voices. Owned by the City root and polled
    /// like the mourner, because no cemetery stage change raises any
    /// event to subscribe to.
    ///
    /// The pair is armed by <see cref="GameSessionState"/>'s
    /// first-sealed plot id. A flag already standing at the very
    /// first poll means the seal happened on an earlier build: the
    /// arrival played without a witness and the birds are simply
    /// THERE — the mourner's philosophy of unobserved rites.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class CityCemeteryRavenController : MonoBehaviour
    {
        public const string RuntimeObjectName =
            "Cemetery Raven Controller";
        public const string RavenAHostName = "Cemetery Raven A";
        public const string RavenBHostName = "Cemetery Raven B";

        /// <summary>Where arrival and return flights spawn: as far
        /// out as a takeoff ends, so a bird is only ever created past
        /// the fog it will emerge from.</summary>
        public const float FlightSpawnDistanceMeters =
            CemeteryRavenFlightModel.DoneDistanceMeters;
        public const float FlightSpawnHeightMeters = 7f;
        public const float TakeoffClimbHeightMeters = 8f;

        /// <summary>The incoming bearing is bowed off the straight
        /// away-from-hero line by up to this much, seeded per flight,
        /// so the pair never descends on two parallel rails.</summary>
        public const float SpawnAzimuthArcDegrees = 30f;

        /// <summary>Two startle cries closer together than this read
        /// as one sound; the second waits it out.</summary>
        public const float MinimumTakeoffCawSeparationSeconds = 0.06f;

        private CityCemeteryPlan cemeteryPlan;
        private CemeteryGravediggingRegister gravedigging;
        private CemeteryGraveWorkController graveWork;
        private Transform player;
        private int citySeed;

        private bool polledOnce;
        private bool inert;
        private CemeteryRavenDirectorModel director;
        private CemeteryRavenPhase lastPhase;
        private CemeteryGravediggingPlan gravePlan;
        private CemeteryRavenPerch perchA;
        private CemeteryRavenPerch perchB;

        private readonly CemeteryRavenActor[] actors =
            new CemeteryRavenActor[2];
        private readonly CemeteryRavenVoice[] voices =
            new CemeteryRavenVoice[2];
        private readonly int[] seeds = new int[2];
        private readonly bool[] flightStarted = new bool[2];
        private readonly int[] flightOrdinals = new int[2];
        private int relocationStage;
        private int ledgerChecksum = int.MinValue;
        private bool groundPerchDisplaced;
        private CemeteryRavenPerch reselectedGroundPerch;
        private double clockSeconds;
        private double lastTakeoffCawSeconds = double.NegativeInfinity;
        private int pendingCawRaven = -1;

        /// <summary>The machine's phase, or null before the first
        /// grave is sealed.</summary>
        public CemeteryRavenPhase? Phase => director?.Phase;

        public bool IsArmed => director != null;

        public CemeteryRavenActor RavenA =>
            actors[CemeteryRavenDirectorModel.RavenAIndex];

        public CemeteryRavenActor RavenB =>
            actors[CemeteryRavenDirectorModel.RavenBIndex];

        /// <summary>Raven A's home: the sealed grave's mound crown.
        /// </summary>
        public CemeteryRavenPerch MoundPerch => perchA;

        /// <summary>Raven B's home: the selected vacant plot. Updated
        /// when a relocation adopts a new one, so every later build
        /// re-derives the same spot from the same ledger.</summary>
        public CemeteryRavenPerch GroundPerch => perchB;

        public static CityCemeteryRavenController Create(
            Transform parent,
            CityCemeteryPlan cemeteryPlan,
            CemeteryGravediggingRegister gravedigging,
            CemeteryGraveWorkController graveWork,
            Transform player,
            Camera camera,
            int citySeed)
        {
            if (parent == null)
            {
                throw new ArgumentNullException(nameof(parent));
            }

            if (gravedigging == null)
            {
                throw new ArgumentNullException(nameof(gravedigging));
            }

            if (player == null)
            {
                throw new ArgumentNullException(nameof(player));
            }

            // The pair is governed by distances alone, but a City
            // without a camera is a broken build and should say so
            // here rather than somewhere downstream.
            if (camera == null)
            {
                throw new ArgumentNullException(nameof(camera));
            }

            // A custom blueprint without a cemetery simply has no
            // ravens — the same silent absence as its graves.
            if (cemeteryPlan == null)
            {
                return null;
            }

            var controller = new GameObject(RuntimeObjectName)
                .AddComponent<CityCemeteryRavenController>();
            controller.transform.SetParent(parent, false);
            controller.cemeteryPlan = cemeteryPlan;
            controller.gravedigging = gravedigging;
            // Null in headless composition without a work session:
            // the guard then reads "no session ever active", which is
            // exactly true.
            controller.graveWork = graveWork;
            controller.player = player;
            controller.citySeed = citySeed;
            return controller;
        }

        private void Update()
        {
            if (player == null || inert)
            {
                return;
            }

            float delta = Time.deltaTime;
            clockSeconds += delta;
            if (director == null)
            {
                TryArm();
                if (director == null)
                {
                    return;
                }
            }

            bool sessionActive =
                graveWork != null && graveWork.IsActive;
            RefreshGroundPerchDisplacement();
            float distanceToA = PlanarDistance(
                player.position,
                perchA.Position);
            float distanceToB = PlanarDistance(
                player.position,
                perchB.Position);
            var input = new CemeteryRavenDirectorInput(
                distanceToA,
                distanceToB,
                // The pair's home reference IS the mound crown, so
                // the return gate measures the same distance as A's
                // flush gate.
                distanceToA,
                sessionActive,
                groundPerchDisplaced,
                GetFlightDone(
                    CemeteryRavenDirectorModel.RavenAIndex),
                GetFlightDone(
                    CemeteryRavenDirectorModel.RavenBIndex));
            director.Advance(delta, input);
            if (director.Phase != lastPhase)
            {
                OnPhaseEntered(director.Phase);
                lastPhase = director.Phase;
            }

            ExecutePhase();

            // The head target: the hero, unless a session hides him —
            // a bird visibly following an invisible man through
            // somebody else's shot would give the trick away. The
            // 18 m cutoff lives in the head model itself.
            bool hasHeadTarget = !director.IsHeadTargetSuppressed;
            for (int index = 0; index < actors.Length; index++)
            {
                if (actors[index] != null)
                {
                    actors[index].SetHeadTarget(
                        hasHeadTarget,
                        player.position);
                }
            }

            bool canCall =
                director.Phase == CemeteryRavenPhase.PerchedIdle &&
                !sessionActive;
            for (int index = 0; index < voices.Length; index++)
            {
                voices[index]?.Advance(delta, canCall);
            }

            FlushPendingTakeoffCaw();
        }

        private void OnDestroy()
        {
            for (int index = 0; index < voices.Length; index++)
            {
                voices[index]?.Dispose();
                voices[index] = null;
            }

            for (int index = 0; index < actors.Length; index++)
            {
                if (actors[index] == null)
                {
                    continue;
                }

                GameObject host = actors[index].gameObject;
                if (Application.isPlaying)
                {
                    Destroy(host);
                }
                else
                {
                    // EditMode teardown has no deferred frame to run
                    // a queued Destroy in — the village soundscape's
                    // own rule, without which the EditMode leak guard
                    // trips on every test.
                    DestroyImmediate(host);
                }

                actors[index] = null;
            }
        }

        /// <summary>
        /// The once-per-session arming. Every consequence of the
        /// first poll's timing is decided here: a flag already set
        /// means spawn perched with no arrival to replay; a flag that
        /// appears later is the live transition and the pair flies in.
        /// </summary>
        private void TryArm()
        {
            string plotId = GameSessionState.FirstSealedGravePlotId;
            if (plotId == null)
            {
                polledOnce = true;
                return;
            }

            bool alreadySealedAtFirstPoll = !polledOnce;
            polledOnce = true;

            gravePlan = FindGravePlan(plotId);
            if (gravePlan == null || !gravePlan.IsPresent)
            {
                // The seed that sealed this grave is not the seed
                // standing here. Nothing to claim, and nothing to be
                // done about it either — the register's own idiom.
                GameLog.Info(
                    "city",
                    "cemetery_raven_plot_missing",
                    GameLog.Field("plot", plotId));
                inert = true;
                return;
            }

            perchA = CemeteryRavenPlan.CreateMoundPerch(gravePlan);
            perchB = SelectGroundPerchNow();
            if (!perchB.IsPresent)
            {
                GameLog.Info(
                    "city",
                    "cemetery_raven_plot_missing",
                    GameLog.Field("plot", plotId),
                    GameLog.Field("reason", "no_vacant_ground"));
                inert = true;
                return;
            }

            seeds[CemeteryRavenDirectorModel.RavenAIndex] =
                CemeteryRavenPlan.DeriveRavenSeed(
                    citySeed,
                    plotId,
                    CemeteryRavenDirectorModel.RavenAIndex);
            seeds[CemeteryRavenDirectorModel.RavenBIndex] =
                CemeteryRavenPlan.DeriveRavenSeed(
                    citySeed,
                    plotId,
                    CemeteryRavenDirectorModel.RavenBIndex);

            for (int index = 0; index < actors.Length; index++)
            {
                if (!TrySpawnRaven(index))
                {
                    TearDownSpawned();
                    inert = true;
                    return;
                }
            }

            director = new CemeteryRavenDirectorModel(
                seeds[CemeteryRavenDirectorModel.RavenAIndex],
                seeds[CemeteryRavenDirectorModel.RavenBIndex]);
            director.Arm(alreadySealedAtFirstPoll);
            lastPhase = director.Phase;

            for (int index = 0; index < actors.Length; index++)
            {
                CemeteryRavenPerch perch = GetPerch(index);
                if (director.Phase == CemeteryRavenPhase.PerchedIdle)
                {
                    actors[index].SetPerched(
                        perch.Position,
                        perch.YawDegrees);
                    actors[index].SetVisible(true);
                }
                else
                {
                    // Waiting to arrive: parked on the spot but
                    // hidden and not perched, so nothing animates a
                    // bird that has not flown in yet.
                    actors[index].transform.SetPositionAndRotation(
                        perch.Position,
                        Quaternion.Euler(0f, perch.YawDegrees, 0f));
                    actors[index].SetVisible(false);
                }
            }
        }

        private bool TrySpawnRaven(int ravenIndex)
        {
            var host = new GameObject(
                ravenIndex == CemeteryRavenDirectorModel.RavenAIndex
                    ? RavenAHostName
                    : RavenBHostName);
            host.transform.SetParent(transform, false);
            CemeteryRavenRigAnchors anchors =
                CemeteryRavenFactory.CreateVisual(host.transform);
            if (anchors == null)
            {
                // The factory already logged the missing provider;
                // an ambient bird degrades instead of breaking City.
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
                seeds[ravenIndex],
                CemeteryRavenPlan.DeriveIdleStartOffsetSeconds(
                    seeds[ravenIndex]));
            actors[ravenIndex] = actor;
            voices[ravenIndex] = CemeteryRavenVoice.Create(
                host.transform,
                seeds[ravenIndex]);
            return true;
        }

        private void TearDownSpawned()
        {
            for (int index = 0; index < voices.Length; index++)
            {
                voices[index]?.Dispose();
                voices[index] = null;
            }

            for (int index = 0; index < actors.Length; index++)
            {
                if (actors[index] == null)
                {
                    continue;
                }

                GameObject host = actors[index].gameObject;
                if (Application.isPlaying)
                {
                    Destroy(host);
                }
                else
                {
                    DestroyImmediate(host);
                }

                actors[index] = null;
            }
        }

        /// <summary>
        /// The sealed grave's plan: from the register's own job when
        /// the yard has it, else derived pure from the plot id — a
        /// test may seal a plot the register never offered.
        /// </summary>
        private CemeteryGravediggingPlan FindGravePlan(string plotId)
        {
            IReadOnlyList<CemeteryGravediggingController> jobs =
                gravedigging.Jobs;
            for (int index = 0; index < jobs.Count; index++)
            {
                CemeteryGravediggingController job = jobs[index];
                if (job != null &&
                    job.HasJob &&
                    string.Equals(
                        job.PlotId,
                        plotId,
                        StringComparison.Ordinal))
                {
                    return job.Plan;
                }
            }

            return CemeteryGravediggingPlan.CreateFor(
                cemeteryPlan,
                plotId);
        }

        /// <summary>
        /// The pure ground-perch selection over the CURRENT ledger:
        /// every taken plot id, plus the coffin and spade rest points
        /// of jobs between Marked and Filled — the span in which
        /// those props actually stand in the world, legally past
        /// their own plot's edge.
        /// </summary>
        private CemeteryRavenPerch SelectGroundPerchNow()
        {
            IReadOnlyList<CemeteryGraveWorkRecord> records =
                GameSessionState.GraveWork;
            var taken = new List<string>(records.Count);
            for (int index = 0; index < records.Count; index++)
            {
                taken.Add(records[index].PlotId);
            }

            var restPoints = new List<Vector3>();
            IReadOnlyList<CemeteryGravediggingController> jobs =
                gravedigging.Jobs;
            for (int index = 0; index < jobs.Count; index++)
            {
                CemeteryGravediggingController job = jobs[index];
                if (job == null || !job.HasJob)
                {
                    continue;
                }

                CemeteryGraveWorkStage stage = job.Stage;
                if (stage >= CemeteryGraveWorkStage.Marked &&
                    stage <= CemeteryGraveWorkStage.Filled)
                {
                    restPoints.Add(job.Plan.CoffinRestGround);
                    restPoints.Add(job.Plan.SpadeRestGround);
                }
            }

            return CemeteryRavenPlan.SelectGroundPerch(
                cemeteryPlan,
                gravePlan,
                taken,
                restPoints);
        }

        /// <summary>
        /// Re-runs the ground selection only when the ledger actually
        /// changed — records only grow and stages only rise, so a
        /// fold over both is a cheap change detector — and reports
        /// displacement as "the argmin no longer answers B's plot".
        /// </summary>
        private void RefreshGroundPerchDisplacement()
        {
            int checksum = ComputeLedgerChecksum();
            if (checksum == ledgerChecksum)
            {
                return;
            }

            ledgerChecksum = checksum;
            reselectedGroundPerch = SelectGroundPerchNow();
            groundPerchDisplaced =
                reselectedGroundPerch.IsPresent &&
                !string.Equals(
                    reselectedGroundPerch.PlotId,
                    perchB.PlotId,
                    StringComparison.Ordinal);
        }

        private static int ComputeLedgerChecksum()
        {
            IReadOnlyList<CemeteryGraveWorkRecord> records =
                GameSessionState.GraveWork;
            int checksum = records.Count;
            for (int index = 0; index < records.Count; index++)
            {
                unchecked
                {
                    checksum = checksum * 31 +
                               (int)records[index].Stage;
                }
            }

            return checksum;
        }

        private void OnPhaseEntered(CemeteryRavenPhase phase)
        {
            if (phase == CemeteryRavenPhase.Landing)
            {
                // A continuation of ReturnFlight, not a fresh phase:
                // one bird is still in the air and its started/done
                // bookkeeping must survive the label change.
                return;
            }

            flightStarted[CemeteryRavenDirectorModel.RavenAIndex] =
                false;
            flightStarted[CemeteryRavenDirectorModel.RavenBIndex] =
                false;
            relocationStage = 0;
        }

        private void ExecutePhase()
        {
            switch (director.Phase)
            {
                case CemeteryRavenPhase.ArrivalFlight:
                case CemeteryRavenPhase.ReturnFlight:
                {
                    for (int index = 0;
                         index < actors.Length;
                         index++)
                    {
                        if (!flightStarted[index] &&
                            director.IsFlightDue(index))
                        {
                            StartReturnFlight(
                                index,
                                GetPerch(index));
                        }
                    }

                    break;
                }

                case CemeteryRavenPhase.Startled:
                {
                    for (int index = 0;
                         index < actors.Length;
                         index++)
                    {
                        if (!flightStarted[index] &&
                            director.IsFlightDue(index))
                        {
                            StartTakeoff(index, true);
                        }
                    }

                    break;
                }

                case CemeteryRavenPhase.RelocatingB:
                {
                    AdvanceRelocation();
                    break;
                }
            }
        }

        /// <summary>
        /// B's move off a claimed plot, in two legs the pure director
        /// sees as one flight: a silent takeoff into the fog, then a
        /// return onto the re-selected plot. Silent because the caw
        /// belongs to the startle alone — a displaced bird just moves
        /// over. The new perch is adopted as B's home the moment the
        /// move starts; the selection is pure over the ledger, so
        /// every later build derives the same spot.
        /// </summary>
        private void AdvanceRelocation()
        {
            int ravenB = CemeteryRavenDirectorModel.RavenBIndex;
            if (relocationStage == 0)
            {
                if (reselectedGroundPerch.IsPresent)
                {
                    perchB = reselectedGroundPerch;
                }

                groundPerchDisplaced = false;
                StartTakeoff(ravenB, false);
                relocationStage = 1;
                return;
            }

            if (relocationStage == 1 && actors[ravenB].IsFlightDone)
            {
                StartReturnFlight(ravenB, perchB);
                relocationStage = 2;
            }
        }

        private bool GetFlightDone(int ravenIndex)
        {
            switch (director.Phase)
            {
                case CemeteryRavenPhase.ArrivalFlight:
                case CemeteryRavenPhase.Startled:
                case CemeteryRavenPhase.ReturnFlight:
                case CemeteryRavenPhase.Landing:
                    return flightStarted[ravenIndex] &&
                           actors[ravenIndex].IsFlightDone;
                case CemeteryRavenPhase.RelocatingB:
                    return ravenIndex ==
                           CemeteryRavenDirectorModel.RavenBIndex &&
                           relocationStage == 2 &&
                           actors[ravenIndex].IsFlightDone;
                default:
                    return false;
            }
        }

        private void StartTakeoff(int ravenIndex, bool playCaw)
        {
            CemeteryRavenActor actor = actors[ravenIndex];
            Vector3 start = actor.transform.position;
            float startYaw = actor.transform.eulerAngles.y;
            Vector3 away = PlanarDirection(
                start - player.position,
                ravenIndex);
            Vector3 end = start +
                          away *
                          CemeteryRavenFlightModel
                              .DoneDistanceMeters +
                          Vector3.up * TakeoffClimbHeightMeters;
            actor.BeginFlight(new CemeteryRavenFlightModel(
                start,
                startYaw,
                end,
                YawOf(away),
                CemeteryRavenFlightKind.Takeoff,
                DeriveFlightSeed(ravenIndex)));
            flightStarted[ravenIndex] = true;
            if (playCaw)
            {
                RequestTakeoffCaw(ravenIndex);
            }
        }

        private void StartReturnFlight(
            int ravenIndex,
            in CemeteryRavenPerch perch)
        {
            CemeteryRavenActor actor = actors[ravenIndex];
            Vector3 target = perch.Position;
            // Spawn on the hero's far side of the perch, bowed off
            // the straight line by a seeded arc: the bird comes out
            // of the fog, never through the man watching for it.
            Vector3 away = PlanarDirection(
                target - player.position,
                ravenIndex);
            int seed = DeriveFlightSeed(ravenIndex);
            float arc =
                (Hash01(unchecked((uint)seed ^ 0x5AFEu)) * 2f - 1f) *
                SpawnAzimuthArcDegrees;
            Vector3 bearing = Quaternion.Euler(0f, arc, 0f) * away;
            Vector3 spawn = target +
                            bearing * FlightSpawnDistanceMeters +
                            Vector3.up * FlightSpawnHeightMeters;
            actor.BeginFlight(new CemeteryRavenFlightModel(
                spawn,
                YawOf(target - spawn),
                target,
                perch.YawDegrees,
                CemeteryRavenFlightKind.Return,
                seed));
            flightStarted[ravenIndex] = true;
        }

        private CemeteryRavenPerch GetPerch(int ravenIndex)
        {
            return ravenIndex ==
                   CemeteryRavenDirectorModel.RavenAIndex
                ? perchA
                : perchB;
        }

        /// <summary>Per-flight seed: the raven's own seed folded with
        /// how many flights it has made, so consecutive takeoff arcs
        /// differ while any single flight stays deterministic.</summary>
        private int DeriveFlightSeed(int ravenIndex)
        {
            flightOrdinals[ravenIndex]++;
            unchecked
            {
                return seeds[ravenIndex] ^
                       (flightOrdinals[ravenIndex] * 0x01000193);
            }
        }

        /// <summary>
        /// One cry per bird per flush, at that bird's own takeoff —
        /// and never two inside the same sixtieth of a second: seeded
        /// staggers CAN collide, and two caws atop each other would
        /// read as one loud bird instead of two.
        /// </summary>
        private void RequestTakeoffCaw(int ravenIndex)
        {
            if (clockSeconds - lastTakeoffCawSeconds >=
                MinimumTakeoffCawSeparationSeconds)
            {
                voices[ravenIndex]?.PlayTakeoffCaw();
                lastTakeoffCawSeconds = clockSeconds;
            }
            else
            {
                pendingCawRaven = ravenIndex;
            }
        }

        private void FlushPendingTakeoffCaw()
        {
            if (pendingCawRaven < 0 ||
                clockSeconds - lastTakeoffCawSeconds <
                MinimumTakeoffCawSeparationSeconds)
            {
                return;
            }

            voices[pendingCawRaven]?.PlayTakeoffCaw();
            lastTakeoffCawSeconds = clockSeconds;
            pendingCawRaven = -1;
        }

        private Vector3 PlanarDirection(
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
                (uint)seeds[ravenIndex] ^ 0xD1Bu)) * 360f;
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
    }
}
