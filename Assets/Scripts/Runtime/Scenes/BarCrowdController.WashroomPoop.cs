using UnityEngine;

namespace BarPromenade
{
    public sealed partial class BarCrowdController
    {
        private static bool IsPoopPresentationPhase(NpcToiletUse use) => use.Sequence != null &&
            use.Sequence.Phase >= HomeToiletSeatedPhase.Preparing && use.Sequence.Phase < HomeToiletSeatedPhase.ClosingLid;

        private static bool CanBeginPoopBowlShot(NpcToiletUse use) => use.BowelBegun &&
            (use.Sequence.Phase == HomeToiletSeatedPhase.Diving || use.Sequence.Phase == HomeToiletSeatedPhase.Sitting ||
                use.Sequence.Phase == HomeToiletSeatedPhase.Seated &&
                use.Sequence.PhaseElapsed < HomeToiletSeatedTimeline.EmissionStartsAt);

        private static float PoopPresentationRemaining(NpcToiletUse use)
        {
            HomeToiletSeatedTimeline sequence = use.Sequence;
            float rest = Mathf.Max(0f, sequence.Duration - sequence.PhaseElapsed);
            float seatedAndRise = HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Seated) +
                HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Rise);
            return sequence.Phase switch
            {
                HomeToiletSeatedPhase.Diving => rest +
                    HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Sit) -
                    (HomeToiletPlungeTimeline.EnterSeconds - HomeToiletSeatedTimeline.SitStartsDuringDive) + seatedAndRise,
                HomeToiletSeatedPhase.Sitting => rest + seatedAndRise,
                HomeToiletSeatedPhase.Seated => rest + HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Rise),
                _ => rest
            };
        }

        private bool BeginWashroomPoop()
        {
            NpcToiletUse use = washroomVisit.ToiletUse;
            // As in the hero interaction, all fallible resources are prepared
            // before the first lid gesture or clothing lease begins.
            if (!PrepareBowlEpisode() || !use.Presentation.PrepareSeatedAppearance())
            {
                AbortWashroomPoop("Visitor seated appearance unavailable");
                return false;
            }
            use.Sequence = new HomeToiletSeatedTimeline();
            use.Plunge = new HomeToiletPlungeTimeline();
            use.Vortex = new HomeToiletFlushVortex();
            use.SceneClock = 0f;
            use.SeatedTime = -1f;
            use.Sequence.Begin();
            MoveToPoopPhase(HomeToiletSeatedPhase.OpeningLid);
            return !washroomVisit.Aborted;
        }

        private bool AdvanceWashroomPoop(float dt)
        {
            NpcToiletUse use = washroomVisit.ToiletUse;
            use.Vortex.Advance(dt);
            if (use.Sequence.CanAdvance) AdvancePoopSequence();
            if (use.Sequence.IsCompleted || washroomVisit.Aborted) return true;
            use.Sequence.Advance(dt);
            switch (use.Sequence.Phase)
            {
                case HomeToiletSeatedPhase.TurningToSeat:
                case HomeToiletSeatedPhase.TurningToInspect:
                case HomeToiletSeatedPhase.TurningToLid:
                    IdleWashroom(dt);
                    bool toSeat = use.Sequence.Phase == HomeToiletSeatedPhase.TurningToSeat;
                    Quaternion target = washroom.Host.Toilet.EntryRotation * Quaternion.Euler(0f, toSeat ? 180f : 0f, 0f);
                    if (TurnWashroomToward(target, dt))
                    {
                        use.Presentation.CaptureNeutral(use.Prepared);
                        use.Sequence.CompleteExternal();
                    }
                    break;
                case HomeToiletSeatedPhase.Diving:
                    use.Plunge.Advance(Mathf.Min(dt, HomeToiletPlungeTimeline.EnterSeconds - use.Plunge.PhaseElapsed));
                    break;
                case HomeToiletSeatedPhase.Exiting:
                    use.Plunge.Advance(dt);
                    if (use.Plunge.IsCompleted) use.Sequence.CompleteExternal();
                    break;
                case HomeToiletSeatedPhase.VortexHold:
                    if (use.Vortex.Elapsed >= HomeToiletFlushVortex.HoldSeconds) use.Sequence.CompleteExternal();
                    break;
            }
            return false;
        }

        private void AdvancePoopSequence()
        {
            NpcToiletUse use = washroomVisit.ToiletUse;
            switch (use.Sequence.Phase)
            {
                case HomeToiletSeatedPhase.OpeningLid:
                    washroom.Host.Toilet.Lid.ReleaseAngleControl(use);
                    MoveToPoopPhase(HomeToiletSeatedPhase.TurningToSeat); break;
                case HomeToiletSeatedPhase.TurningToSeat: MoveToPoopPhase(HomeToiletSeatedPhase.Preparing); break;
                case HomeToiletSeatedPhase.Preparing: MoveToPoopPhase(HomeToiletSeatedPhase.Diving); break;
                case HomeToiletSeatedPhase.Diving: MoveToPoopPhase(HomeToiletSeatedPhase.Sitting); break;
                case HomeToiletSeatedPhase.Sitting: MoveToPoopPhase(HomeToiletSeatedPhase.Seated); break;
                case HomeToiletSeatedPhase.Seated: MoveToPoopPhase(HomeToiletSeatedPhase.Rising); break;
                case HomeToiletSeatedPhase.Rising:
                    if (!use.SeatClearAtRiseEnd) { AbortWashroomPoop(use.Presentation.SeatBlocker); return; }
                    MoveToPoopPhase(HomeToiletSeatedPhase.TurningToInspect); break;
                case HomeToiletSeatedPhase.TurningToInspect: MoveToPoopPhase(HomeToiletSeatedPhase.Inspecting); break;
                case HomeToiletSeatedPhase.Inspecting: MoveToPoopPhase(HomeToiletSeatedPhase.Flushing); break;
                case HomeToiletSeatedPhase.Flushing:
                    if (!use.SeatClearAtFlushEnd) { AbortWashroomPoop(use.Presentation.SeatBlocker); return; }
                    MoveToPoopPhase(HomeToiletSeatedPhase.VortexHold); break;
                case HomeToiletSeatedPhase.VortexHold: MoveToPoopPhase(HomeToiletSeatedPhase.Exiting); break;
                case HomeToiletSeatedPhase.Exiting:
                    if (use.Vortex.IsActive)
                    {
                        use.Presentation.EndSeatedAppearance();
                        MoveToPoopPhase(HomeToiletSeatedPhase.ClosingLid);
                    }
                    else MoveToPoopPhase(use.Prepared ? HomeToiletSeatedPhase.Dressing : HomeToiletSeatedPhase.TurningToLid);
                    break;
                case HomeToiletSeatedPhase.Dressing:
                    use.Presentation.EndSeatedAppearance();
                    MoveToPoopPhase(HomeToiletSeatedPhase.TurningToLid); break;
                case HomeToiletSeatedPhase.TurningToLid: MoveToPoopPhase(HomeToiletSeatedPhase.ClosingLid); break;
                case HomeToiletSeatedPhase.ClosingLid:
                    washroom.Host.Toilet.Lid.ReleaseAngleControl(use);
                    EndWashroomToilet();
                    use.Sequence.MoveTo(HomeToiletSeatedPhase.Completed);
                    break;
            }
        }

        private void MoveToPoopPhase(HomeToiletSeatedPhase phase)
        {
            NpcToiletUse use = washroomVisit.ToiletUse;
            use.Sequence.MoveTo(phase);
            if (phase == HomeToiletSeatedPhase.TurningToSeat || phase == HomeToiletSeatedPhase.TurningToInspect ||
                phase == HomeToiletSeatedPhase.TurningToLid) use.Presentation.Clear(use.Prepared);
            if (phase == HomeToiletSeatedPhase.Preparing)
            {
                use.Prepared = use.Presentation.BeginSeatedAppearance();
                if (!use.Prepared) { AbortWashroomPoop("Visitor seated appearance could not begin"); return; }
                ReadBowlCameraPose(out Pose fixedLens, out _);
                WashroomBowelEffect.Begin(use.Presentation.BowelOutlet, fixedLens);
                use.BowelBegun = true;
            }
            if (phase == HomeToiletSeatedPhase.Diving) use.Plunge.Begin();
            if (phase == HomeToiletSeatedPhase.Exiting) use.Plunge.BeginReturn();
            if (phase == HomeToiletSeatedPhase.OpeningLid || phase == HomeToiletSeatedPhase.ClosingLid)
            {
                use.LidStart = washroom.Host.Toilet.Lid.Angle;
                if (!washroom.Host.Toilet.Lid.TryAcquireAngleControl(use)) AbortWashroomPoop("Toilet lid owned");
            }
        }

        private void PresentWashroomPoop()
        {
            NpcToiletUse use = washroomVisit.ToiletUse;
            HomeToiletSeatedTimeline sequence = use.Sequence;
            switch (sequence.Phase)
            {
                case HomeToiletSeatedPhase.OpeningLid:
                case HomeToiletSeatedPhase.ClosingLid:
                    bool opening = sequence.Phase == HomeToiletSeatedPhase.OpeningLid;
                    washroom.Host.Toilet.Lid.SetOwnedAngle(use, Mathf.Lerp(use.LidStart, opening ? HomeToiletLid.OpenDegrees : 0f,
                        HomeToiletActorPresentation.LidProgress(sequence.Progress)));
                    use.Presentation.Sample(opening ? HomeToiletActorPhase.OpenLid : HomeToiletActorPhase.CloseLid, sequence.Progress);
                    WashroomHandContactError = use.Presentation.LidContactError;
                    break;
                case HomeToiletSeatedPhase.Preparing: use.Presentation.Sample(HomeToiletActorPhase.Prepare, sequence.Progress); break;
                case HomeToiletSeatedPhase.Diving:
                    float sit = (sequence.PhaseElapsed - HomeToiletSeatedTimeline.SitStartsDuringDive) /
                        HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Sit);
                    use.Presentation.Sample(sit <= 0f ? HomeToiletActorPhase.Prepare : HomeToiletActorPhase.Sit, sit <= 0f ? 1f : sit);
                    break;
                case HomeToiletSeatedPhase.Sitting:
                    use.Presentation.Sample(HomeToiletActorPhase.Sit,
                        (HomeToiletPlungeTimeline.EnterSeconds - HomeToiletSeatedTimeline.SitStartsDuringDive + sequence.PhaseElapsed) /
                        HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Sit)); break;
                case HomeToiletSeatedPhase.Seated:
                    use.Presentation.Sample(HomeToiletActorPhase.Seated, sequence.Progress);
                    use.SeatedTime = sequence.PhaseElapsed; break;
                case HomeToiletSeatedPhase.Rising: use.Presentation.Sample(HomeToiletActorPhase.Rise, sequence.Progress); break;
                case HomeToiletSeatedPhase.Inspecting: use.Presentation.Sample(HomeToiletActorPhase.Inspect, sequence.Progress); break;
                case HomeToiletSeatedPhase.Flushing: use.Presentation.Sample(HomeToiletActorPhase.Flush, sequence.Progress); break;
                case HomeToiletSeatedPhase.VortexHold: use.Presentation.Sample(HomeToiletActorPhase.Flush, 1f); break;
                case HomeToiletSeatedPhase.Exiting:
                    use.Presentation.Sample(use.Vortex.IsActive ? HomeToiletActorPhase.Dress : HomeToiletActorPhase.Prepare,
                        use.Vortex.IsActive ? sequence.PhaseElapsed / HomeToiletActorPresentation.Duration(HomeToiletActorPhase.Dress) : 1f);
                    break;
                case HomeToiletSeatedPhase.Dressing: use.Presentation.Sample(HomeToiletActorPhase.Dress, sequence.Progress); break;
                case HomeToiletSeatedPhase.TurningToInspect:
                case HomeToiletSeatedPhase.TurningToLid: use.Presentation.PresentClothing(1f); break;
            }
            if (sequence.Phase == HomeToiletSeatedPhase.Rising && sequence.AtEnd)
                use.SeatClearAtRiseEnd = use.Presentation.SeatClear;
            if (sequence.Phase == HomeToiletSeatedPhase.Flushing)
            {
                if (!use.Vortex.IsActive && sequence.PhaseElapsed >= HomeToiletActorPresentation.FlushCueSeconds)
                {
                    use.Vortex.Begin();
                    WashroomBowelEffect.BeginFlush();
                    sequence.MarkFlushPresented();
                }
                if (sequence.AtEnd) use.SeatClearAtFlushEnd = use.Presentation.SeatClear;
            }
            if (use.BowelBegun)
            {
                WashroomBowelEffect.Present(use.SceneClock, use.SeatedTime, sequence.Phase == HomeToiletSeatedPhase.Seated);
                if (use.Vortex.IsActive) WashroomBowelEffect.PresentFlush(use.Vortex.Elapsed, use.Vortex.Strength);
            }
            sequence.MarkPresented();
        }

        private void AbortWashroomPoop(string reason)
        {
            RecordWashroomAbort(reason);
            washroomVisit.Aborted = true;
            EndWashroomToilet(true);
            washroomVisit.ToiletUse.Sequence?.MoveTo(HomeToiletSeatedPhase.Completed);
        }
    }
}
