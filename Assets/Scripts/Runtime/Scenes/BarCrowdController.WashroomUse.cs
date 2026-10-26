using UnityEngine;

namespace BarPromenade
{
    public enum BarNpcToiletActionKind { Pee, Poop }
    public enum BarKaraokeWashroomShotKind { None, PeeFace, PoopFace, PeeBowl, PoopBowl }

    public sealed partial class BarCrowdController
    {
        private const float ToiletUseHoldSeconds = 16f, BowlShotHoldSeconds = 8f;
        private enum NpcToiletPhase
        {
            Approaching, TurningToOpen, OpeningLid, Peeing, TurningToClose, ClosingLid, Completed
        }
        private sealed class NpcToiletUse
        {
            public BarNpcToiletActionKind Kind;
            public NpcToiletPhase Phase;
            public BarNpcToiletPresentation Presentation;
            public float Seconds, HoldEnd = ToiletUseHoldSeconds, LidStart;
            public int Corner;
            public bool TerminalPresented, BowlActive, BowlPlayed;
            public float BowlClock, PendingPeeSeconds;
            public uint AimSeed;
            public Vector3 DesiredAimPoint, LastAimPoint;
            public HomeToiletSeatedTimeline Sequence;
            public HomeToiletPlungeTimeline Plunge;
            public HomeToiletFlushVortex Vortex;
            public float SceneClock, SeatedTime = -1f;
            public bool Prepared, SeatClearAtRiseEnd, SeatClearAtFlushEnd, BowelBegun;
        }
        private bool bowlPrepared;

        public BarPatron TransitPatron => transit?.Patron;
        public BarNpcToiletActionKind? WashroomActionKind => washroomVisit?.ToiletUse?.Kind;
        public string WashroomActionPhaseName => washroomVisit?.ToiletUse?.Sequence != null
            ? washroomVisit.ToiletUse.Sequence.Phase.ToString() : washroomVisit?.ToiletUse?.Phase.ToString() ?? string.Empty;
        public bool IsWashroomUsingToilet => washroomVisit != null && washroomVisit.Phase == WashroomPhase.Occupied &&
            (washroomVisit.ToiletUse.Phase == NpcToiletPhase.Peeing ||
                washroomVisit.ToiletUse.Sequence?.Phase == HomeToiletSeatedPhase.Seated);
        public float WashroomActionRemainingSeconds => !CanShowWashroomFace ? 0f : washroomVisit.ToiletUse.Sequence != null
            ? PoopPresentationRemaining(washroomVisit.ToiletUse) : Mathf.Max(0f, washroomVisit.ToiletUse.HoldEnd - washroomVisit.ToiletUse.Seconds);
        public HomeToiletSeatedTimeline WashroomSeatedTimeline => washroomVisit?.ToiletUse?.Sequence;
        public HomeToiletSeatedPhase WashroomSeatedPhase => WashroomSeatedTimeline?.Phase ?? HomeToiletSeatedPhase.Idle;
        public float WashroomToiletSceneClock => washroomVisit?.ToiletUse?.SceneClock ?? 0f;
        public float WashroomToiletSeatedTime => washroomVisit?.ToiletUse?.SeatedTime ?? -1f;
        public bool CanShowWashroomFace => washroomVisit != null && washroomVisit.Phase == WashroomPhase.Occupied &&
            (washroomVisit.ToiletUse.Phase == NpcToiletPhase.Peeing || IsPoopPresentationPhase(washroomVisit.ToiletUse));
        public bool CanBeginWashroomBowlShot => CanShowWashroomFace && !washroomVisit.ToiletUse.BowlPlayed &&
            (washroomVisit.ToiletUse.Sequence != null ? CanBeginPoopBowlShot(washroomVisit.ToiletUse) :
                WashroomActionRemainingSeconds >= BowlShotHoldSeconds);
        public float WashroomFootContactError => washroomVisit?.ToiletUse?.Presentation?.FootContactError ?? 0f;
        public HomeToiletUnderwaterEffect WashroomUnderwater => washroom != null ? washroom.Plunge.Underwater : null;
        public HomeToiletBowelEffect WashroomBowelEffect => washroom != null ? washroom.Plunge.BowelEffect : null;
        public HomeUrineEffect WashroomUrine => washroom != null ? washroom.Toilet.Urine : null;
        public bool IsKaraokeBowlShotActive => washroomVisit?.ToiletUse?.BowlActive ?? false;
        public Vector3 WashroomPeeAimPoint => washroomVisit?.ToiletUse?.LastAimPoint ?? Vector3.zero;
        public Vector3 WashroomPeeDesiredAimPoint => washroomVisit?.ToiletUse?.DesiredAimPoint ?? Vector3.zero;
        public Transform WashroomBowelOutlet => washroomVisit?.ToiletUse?.Presentation?.BowelOutlet;
        public bool IsWashroomSeatedAppearanceActive => washroomVisit?.ToiletUse?.Presentation?.IsSeatedAppearanceActive ?? false;
        public float WashroomTrousersDown => washroomVisit?.ToiletUse?.Presentation?.TrousersDown ?? 0f;
        public Vector3 WashroomLeftSeatedPalm => washroomVisit?.ToiletUse?.Presentation?.LeftSeatedPalm ?? Vector3.zero;
        public Vector3 WashroomRightSeatedPalm => washroomVisit?.ToiletUse?.Presentation?.RightSeatedPalm ?? Vector3.zero;
        public float WashroomSeatedHandContactError => washroomVisit?.ToiletUse?.Presentation?.SeatedHandContactError ?? 0f;
        public float WashroomOuterwearLiftWeight => washroomVisit?.ToiletUse?.Presentation?.OuterwearLiftWeight ?? 0f;
        public float WashroomFlushContactError => washroomVisit?.ToiletUse?.Presentation?.FlushContactError ?? 0f;
        public bool IsWashroomFlushContact => washroomVisit?.ToiletUse?.Presentation?.IsFlushContact ?? false;

        public bool TryGetKaraokeWashroomSubject(out BarPatron patron, out BarKaraokeWashroomShotKind kind)
        {
            patron = null; kind = BarKaraokeWashroomShotKind.None;
            if (!CanShowWashroomFace || washroomVisit.Owner == null ||
                !washroomVisit.Owner.activeInHierarchy || washroomVisit.Aborted) return false;
            patron = washroomVisit.Patron;
            NpcToiletUse use = washroomVisit.ToiletUse;
            kind = use.Kind == BarNpcToiletActionKind.Pee
                ? use.BowlActive ? BarKaraokeWashroomShotKind.PeeBowl : BarKaraokeWashroomShotKind.PeeFace
                : use.BowlActive ? BarKaraokeWashroomShotKind.PoopBowl : BarKaraokeWashroomShotKind.PoopFace;
            return true;
        }

        public bool TryGetKaraokeBowlCameraPose(out Pose pose, out Vector3 focus)
        {
            pose = default; focus = default;
            if (!CanShowWashroomFace || washroom == null) return false;
            NpcToiletUse use = washroomVisit.ToiletUse;
            if (!use.BowlActive && !CanBeginWashroomBowlShot) return false;
            if (use.Kind == BarNpcToiletActionKind.Poop &&
                (WashroomBowelOutlet == null || !IsWashroomSeatedAppearanceActive)) return false;
            if (!PrepareBowlEpisode()) return false;
            ReadBowlCameraPose(out pose, out focus);
            return true;
        }

        private void ReadBowlCameraPose(out Pose pose, out Vector3 focus)
        {
            Vector3 position = washroom.Plunge.SubmergedPosition;
            focus = position + washroom.Host.transform.up + washroom.Host.transform.right * .02f;
            pose = new Pose(position, Quaternion.LookRotation(focus - position, washroom.Host.transform.forward));
        }

        public void SetKaraokeBowlShot(bool active)
        {
            NpcToiletUse use = washroomVisit?.ToiletUse;
            if (!active)
            {
                if (use == null || !use.BowlActive) return;
                use.BowlActive = false;
                WashroomUnderwater.End();
                washroom.Plunge.Lighting.End();
                return;
            }
            if (use == null || use.BowlActive || !TryGetKaraokeBowlCameraPose(out Pose pose, out _) ||
                WashroomUnderwater.IsActive) return;
            use.BowlActive = use.BowlPlayed = true;
            use.BowlClock = 0f;
            WashroomUnderwater.Begin(washroom.Host.transform, washroom.Host.Toilet.Water);
            WashroomUnderwater.Present(pose.position.y, 0f);
            washroom.Plunge.Lighting.Begin(pose.position, washroom.Host.transform.right, washroom.Host.transform.forward,
                washroom.Host.Toilet.Bowl, washroom.Host.Toilet.Water, washroom.Host.Toilet.Seat,
                washroomVisit.Owner.transform, WashroomBowelEffect.transform);
            washroom.Plunge.Lighting.Present(1f);
        }

        private bool PrepareBowlEpisode()
        {
            if (bowlPrepared) return true;
            if (washroom == null || WashroomUnderwater == null || !WashroomUnderwater.Prepare() ||
                !WashroomBowelEffect.Prepare(washroom.Host, washroom.Host.Toilet.Water, washroom.Host.CameraFollow.Camera)) return false;
            bowlPrepared = true;
            return true;
        }

        private bool AdvanceWashroomToilet(float dt)
        {
            WashroomVisit current = washroomVisit;
            NpcToiletUse use = current.ToiletUse;
            use.Seconds += dt;
            if (use.Sequence != null)
            {
                use.SceneClock += dt;
                return AdvanceWashroomPoop(dt);
            }
            if (use.BowlActive) use.BowlClock += dt;
            if (use.Phase == NpcToiletPhase.Peeing) use.PendingPeeSeconds = Mathf.Min(.1f, use.PendingPeeSeconds + dt);
            switch (use.Phase)
            {
                case NpcToiletPhase.Approaching:
                    Vector3 target = use.Corner == 0 ? visit.Root.TransformPoint(new Vector3(-4.55f, 0f, 5.62f))
                        : Grounded(washroom.Host.Toilet.EntryPosition);
                    if (MoveWashroomToward(target, dt) && ++use.Corner == 2) MoveToToiletPhase(NpcToiletPhase.TurningToOpen);
                    else if (current.BlockedSeconds >= WashroomBlockedTimeout)
                    {
                        RecordWashroomAbort("Toilet approach blocked");
                        current.Aborted = true;
                        return true;
                    }
                    break;
                case NpcToiletPhase.TurningToOpen:
                    IdleWashroom(dt);
                    if (TurnWashroomToward(washroom.Host.Toilet.EntryRotation, dt))
                    {
                        use.Presentation = new BarNpcToiletPresentation(current.Patron, washroom.Host.Toilet);
                        use.Presentation.CaptureNeutral();
                        if (use.Kind == BarNpcToiletActionKind.Poop)
                        {
                            if (!BeginWashroomPoop()) return true;
                        }
                        else MoveToToiletPhase(NpcToiletPhase.OpeningLid);
                    }
                    break;
                case NpcToiletPhase.OpeningLid:
                    if (ToiletPhaseFinished())
                    {
                        washroom.Host.Toilet.Lid.ReleaseAngleControl(use);
                        WashroomUrine.BeginEmission(); MoveToToiletPhase(NpcToiletPhase.Peeing);
                    }
                    break;
                case NpcToiletPhase.Peeing:
                    if (ToiletPhaseFinished())
                    {
                        SetKaraokeBowlShot(false);
                        WashroomUrine.StopEmission();
                        use.Presentation.Clear();
                        MoveToToiletPhase(NpcToiletPhase.TurningToClose);
                    }
                    break;
                case NpcToiletPhase.TurningToClose:
                    IdleWashroom(dt);
                    if (TurnWashroomToward(washroom.Host.Toilet.EntryRotation, dt))
                    { use.Presentation.CaptureNeutral(); MoveToToiletPhase(NpcToiletPhase.ClosingLid); }
                    break;
                case NpcToiletPhase.ClosingLid:
                    if (ToiletPhaseFinished())
                    {
                        washroom.Host.Toilet.Lid.ReleaseAngleControl(use);
                        use.Presentation.Clear();
                        MoveToToiletPhase(NpcToiletPhase.Completed);
                    }
                    break;
                case NpcToiletPhase.Completed: return true;
            }
            return false;
        }

        private void MoveToToiletPhase(NpcToiletPhase phase)
        {
            NpcToiletUse use = washroomVisit.ToiletUse;
            use.Phase = phase; use.Seconds = 0f; use.TerminalPresented = false;
            if (phase == NpcToiletPhase.Peeing)
                use.AimSeed = ToiletAimSeed(washroomVisit.Patron.Anchor.Id, CompletedWashroomVisits + 1);
            if (phase == NpcToiletPhase.OpeningLid || phase == NpcToiletPhase.ClosingLid)
            {
                use.LidStart = washroom.Host.Toilet.Lid.Angle;
                if (!washroom.Host.Toilet.Lid.TryAcquireAngleControl(use))
                {
                    RecordWashroomAbort("Toilet lid owned");
                    washroomVisit.Aborted = true;
                    EndWashroomToilet();
                    use.Phase = NpcToiletPhase.Completed;
                }
            }
        }

        private float ToiletPhaseDuration(NpcToiletUse use) =>
            use.Phase == NpcToiletPhase.Peeing ? use.HoldEnd : 2f;
        private bool ToiletPhaseFinished() => washroomVisit.ToiletUse.TerminalPresented &&
            washroomVisit.ToiletUse.Seconds >= ToiletPhaseDuration(washroomVisit.ToiletUse);

        private void PresentWashroomToilet()
        {
            NpcToiletUse use = washroomVisit.ToiletUse;
            if (use == null || use.Presentation == null) return;
            if (use.Sequence != null) { PresentWashroomPoop(); PresentKaraokeBowlShot(); return; }
            float progress = Mathf.Clamp01(use.Seconds / ToiletPhaseDuration(use));
            switch (use.Phase)
            {
                case NpcToiletPhase.OpeningLid:
                case NpcToiletPhase.ClosingLid:
                    bool opening = use.Phase == NpcToiletPhase.OpeningLid;
                    washroom.Host.Toilet.Lid.SetOwnedAngle(use, Mathf.Lerp(use.LidStart, opening ? HomeToiletLid.OpenDegrees : 0f,
                        HomeToiletActorPresentation.LidProgress(progress)));
                    use.Presentation.Sample(opening ? HomeToiletActorPhase.OpenLid : HomeToiletActorPhase.CloseLid, progress);
                    WashroomHandContactError = use.Presentation.LidContactError;
                    break;
                case NpcToiletPhase.Peeing:
                    float amount = Mathf.SmoothStep(0f, 1f, use.Seconds / .55f) *
                        Mathf.SmoothStep(0f, 1f, (use.HoldEnd - use.Seconds) / .55f);
                    use.Presentation.SamplePee(amount);
                    use.DesiredAimPoint = EvaluatePeeAim(use);
                    float emissionSeconds = use.PendingPeeSeconds;
                    use.PendingPeeSeconds = 0f;
                    if (!GameTimeScaleRuntime.IsPaused && emissionSeconds > 0f && use.Seconds < use.HoldEnd - .55f)
                    {
                        Vector3 origin = use.Presentation.PeeOutlet;
                        Vector3 destination = use.DesiredAimPoint;
                        Vector3 planar = Vector3.ProjectOnPlane(destination - origin, Vector3.up);
                        float horizontal = planar.magnitude;
                        float drop = destination.y - origin.y;
                        // Solve a modest upward arc to the visible centre of
                        // the water. Lower speed gives gravity time to clear
                        // the annular seat while retaining a full visible jet.
                        const float launchPitch = 18f * Mathf.Deg2Rad;
                        float cosine = Mathf.Cos(launchPitch);
                        float fullSpeed = Mathf.Sqrt(HomeUrineTrajectory.Gravity * horizontal * horizontal /
                            (2f * cosine * cosine * (horizontal * Mathf.Tan(launchPitch) - drop)));
                        float velocity = fullSpeed * Mathf.Sqrt(amount);
                        float discriminant = velocity * velocity * velocity * velocity - HomeUrineTrajectory.Gravity *
                            (HomeUrineTrajectory.Gravity * horizontal * horizontal + 2f * drop * velocity * velocity);
                        // Pressure ramps from zero. No ballistic solution yet
                        // means no emitted packet, rather than a falling dribble
                        // into the seat's solid front edge.
                        if (discriminant >= 0f)
                        {
                            float pitch = Mathf.Atan((velocity * velocity - Mathf.Sqrt(discriminant)) /
                                Mathf.Max(.001f, HomeUrineTrajectory.Gravity * horizontal));
                            Vector3 direction = (planar.normalized * Mathf.Cos(pitch) + Vector3.up * Mathf.Sin(pitch)).normalized;
                            int previousPackets = WashroomUrine.EmittedPacketCount;
                            WashroomUrine.EmitStep(origin, direction, emissionSeconds, amount, false,
                                fullSpeed / HomeUrineEffect.StreamSpeed);
                            if (WashroomUrine.EmittedPacketCount != previousPackets) use.LastAimPoint = destination;
                        }
                    }
                    break;
            }
            if (use.Seconds >= ToiletPhaseDuration(use)) use.TerminalPresented = true;
            PresentKaraokeBowlShot();
        }

        private void PresentKaraokeBowlShot()
        {
            NpcToiletUse use = washroomVisit.ToiletUse;
            if (!use.BowlActive) return;
            if (!GameCursor.ApplicationFocused) { SetKaraokeBowlShot(false); return; }
            Camera camera = washroom.Host.CameraFollow.Camera;
            WashroomUnderwater.Present(camera.transform.position.y, use.Sequence != null ? use.SceneClock : use.BowlClock);
            float inspection = use.Sequence?.Phase == HomeToiletSeatedPhase.Inspecting
                ? HomeToiletPlungeTimeline.Ease(use.Sequence.Progress)
                : use.Sequence?.Phase == HomeToiletSeatedPhase.Flushing
                    ? 1f - HomeToiletPlungeTimeline.Ease((use.Sequence.PhaseElapsed - HomeToiletActorPresentation.FlushCueSeconds) /
                        (HomeToiletActorPresentation.FlushDurationSeconds - HomeToiletActorPresentation.FlushCueSeconds)) : 0f;
            washroom.Plunge.Lighting.Present(1f, inspection);
            if (use.Vortex != null) WashroomUnderwater.PresentVortex(use.Vortex.Strength, use.Vortex.RollDegrees * Mathf.Deg2Rad);
        }

        private Vector3 EvaluatePeeAim(NpcToiletUse use)
        {
            ReadBowlCameraPose(out Pose pose, out _);
            Vector3 up = washroom.Host.transform.up;
            Vector3 forward = pose.rotation * Vector3.forward;
            Vector3 water = washroom.Host.Toilet.Water.position;
            float depth = Vector3.Dot(water - pose.position, up) / Vector3.Dot(forward, up);
            Vector3 centre = pose.position + forward * depth;
            // Static bowl FOV is 65 degrees. Reserve almost half its water-plane
            // cone for packet width/variation; the resulting disk also lies
            // well inside the authored water's .17 x .157 m oval.
            float aspect = Mathf.Min(1f, washroom.Host.CameraFollow.Camera.aspect);
            float radius = Mathf.Min(.04f, depth * Mathf.Tan(32.5f * Mathf.Deg2Rad) * aspect * .55f);
            float cursor = use.Seconds / 1.15f;
            int segment = Mathf.FloorToInt(cursor);
            float t = cursor - segment;
            float smooth = t * t * t * (t * (t * 6f - 15f) + 10f);
            Vector2 a = PeeAimWaypoint(use.AimSeed, segment), b = PeeAimWaypoint(use.AimSeed, segment + 1);
            Vector2 offset = Vector2.LerpUnclamped(a, b, smooth) * radius;
            Vector3 right = Vector3.ProjectOnPlane(pose.rotation * Vector3.right, up).normalized;
            Vector3 vertical = Vector3.Cross(right, up).normalized;
            return centre + right * offset.x + vertical * offset.y;
        }

        private uint ToiletAimSeed(string characterId, int visitOrdinal)
        {
            unchecked
            {
                uint value = 2166136261u ^ (uint)seed;
                for (int index = 0; index < characterId.Length; index++) value = (value ^ characterId[index]) * 16777619u;
                return MixToiletAim(value ^ (uint)visitOrdinal * 0x9E3779B9u);
            }
        }

        private static Vector2 PeeAimWaypoint(uint visitSeed, int segment)
        {
            uint value = MixToiletAim(visitSeed ^ unchecked((uint)segment * 0x9E3779B9u));
            float angle = (value & 0xffffu) * (2f * Mathf.PI / 65536f);
            float distance = .65f + .35f * Mathf.Sqrt((value >> 16) / 65535f);
            return new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * distance;
        }

        private static uint MixToiletAim(uint value)
        {
            unchecked
            {
                value ^= value >> 16; value *= 0x7FEB352Du;
                value ^= value >> 15; value *= 0x846CA68Bu;
                return value ^ (value >> 16);
            }
        }

        private void EndWashroomToilet(bool releasePrepared = false)
        {
            NpcToiletUse use = washroomVisit?.ToiletUse;
            SetKaraokeBowlShot(false);
            if (washroom != null) WashroomUrine?.StopEmission();
            if (use == null) return;
            if (use.BowelBegun) { WashroomBowelEffect.End(); use.BowelBegun = false; }
            washroom?.Host?.Toilet.Lid.ReleaseAngleControl(use);
            if (washroomVisit.Owner != null)
            {
                if (releasePrepared) use.Presentation?.Dispose();
                else use.Presentation?.Clear();
            }
        }

        private void OnEnable()
        {
            NpcToiletUse use = washroomVisit?.ToiletUse;
            if (use?.Presentation == null || washroomVisit.Owner == null) return;
            use.Presentation.CaptureNeutral();
            if (use.Phase == NpcToiletPhase.Peeing) WashroomUrine.BeginEmission();
            if (use.Phase == NpcToiletPhase.OpeningLid || use.Phase == NpcToiletPhase.ClosingLid)
                washroom.Host.Toilet.Lid.TryAcquireAngleControl(use);
        }
        private void OnDisable()
        {
            NpcToiletUse use = washroomVisit?.ToiletUse;
            // The hero's scene also cancels on disable. Never resume a bowel
            // episode by replaying its already emitted physical object.
            if (use?.Sequence != null && !use.Sequence.IsCompleted)
            {
                if (washroomVisit.Owner != null) RecordWashroomAbort("Visitor toilet action disabled");
                washroomVisit.Aborted = true;
                use.Sequence.MoveTo(HomeToiletSeatedPhase.Completed);
            }
            EndWashroomToilet();
        }
        private void OnApplicationFocus(bool focused) { if (!focused) SetKaraokeBowlShot(false); }
    }
}
