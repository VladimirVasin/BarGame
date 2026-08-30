using UnityEngine;

namespace BarPromenade
{
    public enum CemeteryRavenPhase
    {
        /// <summary>No grave has ever been sealed this session.</summary>
        Unarmed = 0,

        /// <summary>The seal happened in this very build; the pair
        /// waits for the session to end and the hero to step back
        /// before flying in.</summary>
        WaitingToArrive = 1,

        ArrivalFlight = 2,
        PerchedIdle = 3,
        Startled = 4,

        /// <summary>Both birds are past the fog: hidden, waiting for
        /// the hero to be nearly out of sight of their home.</summary>
        Away = 5,

        ReturnFlight = 6,

        /// <summary>The span between the first touch and the last:
        /// one bird already folding while the other still flares.</summary>
        Landing = 7,

        /// <summary>Raven B's ground was taken for a new grave; it
        /// alone moves to a re-selected plot while A keeps sitting.</summary>
        RelocatingB = 8
    }

    /// <summary>
    /// Everything the director's state machine needs from one polled
    /// frame, as plain values: EditMode drives the machine
    /// exhaustively without a scene, and the controller's only job is
    /// to measure these and act on the phase that comes back.
    /// </summary>
    public readonly struct CemeteryRavenDirectorInput
    {
        public CemeteryRavenDirectorInput(
            float heroDistanceToPerchAMeters,
            float heroDistanceToPerchBMeters,
            float heroDistanceToCrownMeters,
            bool sessionActive,
            bool groundPerchDisplaced,
            bool flightDoneA,
            bool flightDoneB)
        {
            HeroDistanceToPerchAMeters = heroDistanceToPerchAMeters;
            HeroDistanceToPerchBMeters = heroDistanceToPerchBMeters;
            HeroDistanceToCrownMeters = heroDistanceToCrownMeters;
            SessionActive = sessionActive;
            GroundPerchDisplaced = groundPerchDisplaced;
            FlightDoneA = flightDoneA;
            FlightDoneB = flightDoneB;
        }

        /// <summary>Planar hero distance to raven A's perch (the
        /// mound crown).</summary>
        public float HeroDistanceToPerchAMeters { get; }

        /// <summary>Planar hero distance to raven B's ground perch.
        /// </summary>
        public float HeroDistanceToPerchBMeters { get; }

        /// <summary>Planar hero distance to the mound crown — the
        /// pair's home reference the return gate measures from.</summary>
        public float HeroDistanceToCrownMeters { get; }

        /// <summary>True while a grave-work session owns the camera
        /// and hides the hero.</summary>
        public bool SessionActive { get; }

        /// <summary>True when re-running the ground-perch selection
        /// no longer returns raven B's current plot: it entered the
        /// ledger, or a new job's coffin or spade now rests on it.</summary>
        public bool GroundPerchDisplaced { get; }

        public bool FlightDoneA { get; }
        public bool FlightDoneB { get; }
    }

    /// <summary>
    /// The pure state machine behind the cemetery raven pair. It
    /// knows no transforms, assets or scene — only distances, flags
    /// and time — so every transition is provable in EditMode. The
    /// controller resolves geometry, runs actors and voices, and
    /// reports back through <see cref="CemeteryRavenDirectorInput"/>;
    /// this class decides, and only decides.
    ///
    /// The guards all serve one rule: while a grave-work session owns
    /// the camera the birds do NOTHING observable — no arrival into
    /// an owned shot, no flush behind the hero's hidden back, and no
    /// visible head following the invisible man — because an event
    /// nobody could have witnessed is an event that did not happen.
    /// </summary>
    public sealed class CemeteryRavenDirectorModel
    {
        public const int RavenAIndex = 0;
        public const int RavenBIndex = 1;

        /// <summary>Closer than this to either bird and both flush:
        /// birds do not wait out a man at arm's length.</summary>
        public const float FlushDistanceMeters = 3.5f;

        /// <summary>
        /// The return gate: 70% of the city's visible slice, measured
        /// from the mound crown. Tied to the far plane by
        /// construction so a fog change moves the birds with it; the
        /// gap between this and <see cref="FlushDistanceMeters"/> IS
        /// the hysteresis — no timers.
        /// </summary>
        public const float ReturnDistanceMeters =
            0.7f * RuntimeSceneSetup.CityFarClipPlane;

        /// <summary>The pair never moves as one object: takeoffs are
        /// split by a fraction of a second, returns by most of one.
        /// </summary>
        public const float TakeoffStaggerMinimumSeconds = 0.12f;
        public const float TakeoffStaggerMaximumSeconds = 0.30f;
        public const float ReturnStaggerMinimumSeconds = 0.4f;
        public const float ReturnStaggerMaximumSeconds = 0.9f;

        private readonly float[] takeoffStaggerSeconds;
        private readonly float[] returnStaggerSeconds;

        public CemeteryRavenDirectorModel(
            int ravenSeedA,
            int ravenSeedB)
        {
            takeoffStaggerSeconds = new[]
            {
                DeriveStagger(
                    ravenSeedA,
                    0x7A11u,
                    TakeoffStaggerMinimumSeconds,
                    TakeoffStaggerMaximumSeconds),
                DeriveStagger(
                    ravenSeedB,
                    0x7A11u,
                    TakeoffStaggerMinimumSeconds,
                    TakeoffStaggerMaximumSeconds)
            };
            returnStaggerSeconds = new[]
            {
                DeriveStagger(
                    ravenSeedA,
                    0x9E77u,
                    ReturnStaggerMinimumSeconds,
                    ReturnStaggerMaximumSeconds),
                DeriveStagger(
                    ravenSeedB,
                    0x9E77u,
                    ReturnStaggerMinimumSeconds,
                    ReturnStaggerMaximumSeconds)
            };
        }

        public CemeteryRavenPhase Phase { get; private set; } =
            CemeteryRavenPhase.Unarmed;

        public double PhaseElapsedSeconds { get; private set; }

        public bool IsArmed { get; private set; }

        /// <summary>True when the pair was already owed on this
        /// build's very first poll and spawned sitting, with no
        /// arrival to replay.</summary>
        public bool SpawnedPerchedWithoutArrival { get; private set; }

        /// <summary>
        /// True while the last polled frame had a session active: the
        /// head models are handed a neutral target for the duration,
        /// so the birds do not visibly lead the hidden hero through
        /// somebody else's shot.
        /// </summary>
        public bool IsHeadTargetSuppressed { get; private set; }

        /// <summary>
        /// Arms the machine once, forever. The first-sealed plot id
        /// never changes within a session, so a second call — a
        /// second sealed grave, a duplicate poll — is a no-op by
        /// design rather than by caller discipline.
        /// </summary>
        public void Arm(bool alreadySealedAtFirstPoll)
        {
            if (IsArmed)
            {
                return;
            }

            IsArmed = true;
            SpawnedPerchedWithoutArrival = alreadySealedAtFirstPoll;
            // A flag already standing at the first poll means the
            // seal happened on some earlier build: the arrival played
            // without a witness, and the pair is simply THERE, the
            // mourner's philosophy.
            Phase = alreadySealedAtFirstPoll
                ? CemeteryRavenPhase.PerchedIdle
                : CemeteryRavenPhase.WaitingToArrive;
            PhaseElapsedSeconds = 0d;
        }

        public void Advance(
            float deltaSeconds,
            in CemeteryRavenDirectorInput input)
        {
            if (float.IsNaN(deltaSeconds) ||
                float.IsInfinity(deltaSeconds) ||
                deltaSeconds <= 0f)
            {
                return;
            }

            IsHeadTargetSuppressed = input.SessionActive;
            if (!IsArmed)
            {
                return;
            }

            PhaseElapsedSeconds += deltaSeconds;
            switch (Phase)
            {
                case CemeteryRavenPhase.WaitingToArrive:
                {
                    // Quiet arrival, and only onto clear ground:
                    // birds do not land beside a standing man, and
                    // never into a session's owned camera.
                    if (!input.SessionActive &&
                        input.HeroDistanceToPerchAMeters >
                        FlushDistanceMeters &&
                        input.HeroDistanceToPerchBMeters >
                        FlushDistanceMeters)
                    {
                        TransitionTo(
                            CemeteryRavenPhase.ArrivalFlight);
                    }

                    break;
                }

                case CemeteryRavenPhase.ArrivalFlight:
                {
                    if (input.FlightDoneA && input.FlightDoneB)
                    {
                        TransitionTo(CemeteryRavenPhase.PerchedIdle);
                    }

                    break;
                }

                case CemeteryRavenPhase.PerchedIdle:
                {
                    // The flush outranks the relocation: a man at
                    // arm's length moves BOTH birds now, and B's new
                    // ground is simply where the return lands it.
                    // Both wait out a session — the hero's transform
                    // parks at the worksite while he is hidden, and a
                    // flush nobody can see is an off-screen event.
                    if (!input.SessionActive &&
                        (input.HeroDistanceToPerchAMeters <=
                         FlushDistanceMeters ||
                         input.HeroDistanceToPerchBMeters <=
                         FlushDistanceMeters))
                    {
                        TransitionTo(CemeteryRavenPhase.Startled);
                    }
                    else if (!input.SessionActive &&
                             input.GroundPerchDisplaced)
                    {
                        TransitionTo(CemeteryRavenPhase.RelocatingB);
                    }

                    break;
                }

                case CemeteryRavenPhase.Startled:
                {
                    if (input.FlightDoneA && input.FlightDoneB)
                    {
                        TransitionTo(CemeteryRavenPhase.Away);
                    }

                    break;
                }

                case CemeteryRavenPhase.Away:
                {
                    if (!input.SessionActive &&
                        input.HeroDistanceToCrownMeters >=
                        ReturnDistanceMeters)
                    {
                        TransitionTo(CemeteryRavenPhase.ReturnFlight);
                    }

                    break;
                }

                case CemeteryRavenPhase.ReturnFlight:
                {
                    if (input.FlightDoneA && input.FlightDoneB)
                    {
                        TransitionTo(CemeteryRavenPhase.PerchedIdle);
                    }
                    else if (input.FlightDoneA || input.FlightDoneB)
                    {
                        TransitionTo(CemeteryRavenPhase.Landing);
                    }

                    break;
                }

                case CemeteryRavenPhase.Landing:
                {
                    if (input.FlightDoneA && input.FlightDoneB)
                    {
                        TransitionTo(CemeteryRavenPhase.PerchedIdle);
                    }

                    break;
                }

                case CemeteryRavenPhase.RelocatingB:
                {
                    // A displaced bird finishes its move before the
                    // pair answers the hero again; per-raven flush
                    // bookkeeping is exactly what this machine
                    // avoids, and the move takes seconds.
                    if (input.FlightDoneB)
                    {
                        TransitionTo(CemeteryRavenPhase.PerchedIdle);
                    }

                    break;
                }
            }
        }

        /// <summary>
        /// Whether the given raven's flight for the CURRENT phase
        /// should be running by now: takeoffs use the short stagger,
        /// arrivals and returns the long one, and a relocation moves
        /// B alone with no stagger at all. The controller keeps its
        /// own started/not-started bookkeeping — that is
        /// presentation, not decision.
        /// </summary>
        public bool IsFlightDue(int ravenIndex)
        {
            switch (Phase)
            {
                case CemeteryRavenPhase.Startled:
                    return PhaseElapsedSeconds >=
                           GetTakeoffStaggerSeconds(ravenIndex);
                case CemeteryRavenPhase.ArrivalFlight:
                case CemeteryRavenPhase.ReturnFlight:
                    return PhaseElapsedSeconds >=
                           GetReturnStaggerSeconds(ravenIndex);
                case CemeteryRavenPhase.RelocatingB:
                    return ravenIndex == RavenBIndex;
                default:
                    return false;
            }
        }

        public float GetTakeoffStaggerSeconds(int ravenIndex)
        {
            return takeoffStaggerSeconds[
                ValidateRavenIndex(ravenIndex)];
        }

        public float GetReturnStaggerSeconds(int ravenIndex)
        {
            return returnStaggerSeconds[
                ValidateRavenIndex(ravenIndex)];
        }

        private void TransitionTo(CemeteryRavenPhase phase)
        {
            Phase = phase;
            PhaseElapsedSeconds = 0d;
        }

        private static int ValidateRavenIndex(int ravenIndex)
        {
            if (ravenIndex != RavenAIndex &&
                ravenIndex != RavenBIndex)
            {
                throw new System.ArgumentOutOfRangeException(
                    nameof(ravenIndex));
            }

            return ravenIndex;
        }

        private static float DeriveStagger(
            int seed,
            uint salt,
            float minimumSeconds,
            float maximumSeconds)
        {
            uint hash = Hash(unchecked((uint)seed ^ salt));
            return Mathf.Lerp(
                minimumSeconds,
                maximumSeconds,
                (hash & 0x00FFFFFFu) / 16777215f);
        }

        private static uint Hash(uint value)
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
