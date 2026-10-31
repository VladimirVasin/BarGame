using UnityEngine;

namespace BarPromenade
{
    /// <summary>
    /// Sofa contacts and breath share the action's actual clock and finite audio
    /// pools. The sleep director pauses/releases all of its voices together.
    /// </summary>
    public static class HomeSleepAudioCue
    {
        public const RetroSfxId CueId = RetroSfxId.HomeSleepStartle;
        public const float DurationSeconds = RetroSfxLibrary.HomeSleepStartleDurationSeconds;

        public static bool Trigger(RetroAudioService audio, object owner)
            => Play(audio, owner, CueId, Vector3.zero, 1f);

        private static bool Play(RetroAudioService audio, object owner, RetroSfxId cue,
            Vector3 position, float gain)
        {
            if (audio == null || owner == null || !Application.isPlaying)
                return false;
            object previous = RetroAudio.SetPresentationCueOwner(owner);
            try { return audio.TryPlay(cue, position, gain); }
            finally { RetroAudio.SetPresentationCueOwner(previous); }
        }

        public static void SetPaused(RetroAudioService audio, object owner, bool paused)
        {
            if (audio != null && owner != null) audio.SetPresentationPaused(owner, paused);
        }

        public static void Stop(RetroAudioService audio, object owner)
        {
            if (audio != null && owner != null) audio.ReleasePresentation(owner);
        }

        /// <summary>
        /// No free-running clock: contact landmarks use the sampled action,
        /// breathing uses the same clocks as the visible chest. One crossed
        /// contact at most per tick; a hitch never replays old movement as a burst.
        /// </summary>
        public sealed class Sequence
        {
            private enum Anchor { Body, LeftHand, RightHand, Head, LeftFoot, RightFoot }
            private readonly struct Contact
            {
                public readonly float Progress, Gain;
                public readonly RetroSfxId Cue;
                public readonly Anchor At;
                public Contact(float progress, RetroSfxId cue, Anchor at = Anchor.Body, float gain = 1f)
                { Progress = progress; Cue = cue; At = at; Gain = gain; }
            }

            // The same seat/transfer/boot landmarks are authored in
            // HomeBedInteractionPlan and the production BedEnter/BedExit clips.
            private static readonly Contact[] EnterContacts =
            {
                new Contact(.13f, RetroSfxId.HomeBedCloth, Anchor.LeftHand, .7f),
                new Contact(.22f, RetroSfxId.HomeBedSettle),
                new Contact(.30f, RetroSfxId.HomeBedCreak, Anchor.LeftHand, .8f),
                new Contact(.44f, RetroSfxId.HomeBedSettle, gain: .75f),
                new Contact(.51f, RetroSfxId.HomeBedCreak, Anchor.RightHand, .75f),
                new Contact(.61f, RetroSfxId.HomeBedSettle, gain: .7f),
                new Contact(.72f, RetroSfxId.HomeBedCloth, Anchor.RightHand, .85f),
                new Contact(.84f, RetroSfxId.HomeBedCreak, gain: .65f),
                new Contact(.97f, RetroSfxId.HomeBedSettle, Anchor.Head, .5f)
            };
            private static readonly Contact[] ExitContacts =
            {
                new Contact(.14f, RetroSfxId.HomeBedCloth, Anchor.RightHand),
                new Contact(.29f, RetroSfxId.HomeBedCreak, gain: .8f),
                new Contact(.43f, RetroSfxId.HomeBedSettle, gain: .75f),
                new Contact(.49f, RetroSfxId.HomeBedCloth, Anchor.RightHand, .8f),
                new Contact(.57f, RetroSfxId.HomeBedSettle, gain: .8f),
                new Contact(.70f, RetroSfxId.FootstepWood, Anchor.RightFoot, .65f),
                new Contact(.80f, RetroSfxId.FootstepWood, Anchor.LeftFoot, .6f),
                new Contact(.91f, RetroSfxId.HomeBedCreak, gain: .65f)
            };

            private PlayerAnimatedInteractionPhase previousAction;
            private HomeSleepPhase previousPhase;
            private float previousProgress, previousBreathProgress;
            private int breathMode;
            private int previousBreathHalf;
            private double nextBreathAt;
            public int ContactCueCount { get; private set; }
            public int BreathCueCount { get; private set; }
            public RetroSfxId LastContactCue { get; private set; }
            public float LastContactProgress { get; private set; }
            public PlayerAnimatedInteractionPhase LastContactPhase { get; private set; }

            public void Reset()
            {
                previousAction = PlayerAnimatedInteractionPhase.Idle;
                previousPhase = HomeSleepPhase.Idle;
                previousProgress = previousBreathProgress = -1f;
                breathMode = 0;
                previousBreathHalf = -1;
                nextBreathAt = 0d;
                ContactCueCount = BreathCueCount = 0;
                LastContactCue = RetroSfxId.None;
                LastContactProgress = 0f;
                LastContactPhase = PlayerAnimatedInteractionPhase.Idle;
            }

            public void Tick(HomeInteriorRoot home, object owner, HomeSleepPhase phase,
                bool normalOpening, float wakeSeconds)
            {
                if (home == null || owner == null || home.Audio == null || home.AnimatedInteraction == null ||
                    phase == HomeSleepPhase.Idle || PauseMenuController.IsAnyPaused || GameTimeScaleRuntime.IsPaused)
                    return;
                var hero = home.Player.Visual as Player3DCharacterPresentation;
                if (hero == null || hero.Registry == null) return;
                Player3DBoneAnchors anchors = hero.Registry.Anchors;
                PlayerAnimatedInteractionPhase action = home.AnimatedInteraction.Phase;
                float progress = home.AnimatedInteraction.ClipProgress;
                if (action != previousAction) previousProgress = -1f;
                Contact[] contacts = action == PlayerAnimatedInteractionPhase.Entering ? EnterContacts :
                    action == PlayerAnimatedInteractionPhase.Exiting ? ExitContacts : null;
                if (contacts != null)
                {
                    int latest = -1;
                    for (int i = 0; i < contacts.Length; i++)
                        if (contacts[i].Progress > previousProgress && contacts[i].Progress <= progress) latest = i;
                    // Discard a contact whose visible pose was skipped by a hitch.
                    if (latest >= 0 && progress - contacts[latest].Progress <= .075f)
                    {
                        Contact contact = contacts[latest];
                        if (Play(home.Audio, owner, contact.Cue, Position(anchors, contact.At), contact.Gain))
                        {
                            ContactCueCount++;
                            LastContactCue = contact.Cue;
                            LastContactProgress = contact.Progress;
                            LastContactPhase = action;
                        }
                    }
                }
                previousProgress = progress;
                previousAction = action;

                if (phase != previousPhase && !normalOpening)
                {
                    if (phase == HomeSleepPhase.Nightmare)
                        Breath(home, owner, anchors, RetroSfxId.HomeBedExhale, .8f);
                    else if (phase == HomeSleepPhase.Startle)
                        Breath(home, owner, anchors, RetroSfxId.HomeBedGasp, 1f, immediate: true);
                }
                previousPhase = phase;

                int mode = action == PlayerAnimatedInteractionPhase.Looping &&
                    (phase == HomeSleepPhase.OpeningSleep || phase == HomeSleepPhase.FaceHold ||
                     phase == HomeSleepPhase.LyingDown) ? 1 :
                    phase == HomeSleepPhase.Recovering ? 2 : 0;
                bool changed = mode != breathMode;
                if (changed)
                {
                    breathMode = mode;
                    previousBreathProgress = -1f;
                    previousBreathHalf = -1;
                }
                if (mode == 1)
                {
                    // Clip progress already includes the authored unequal
                    // inhale/exhale holds; wall-clock looping would drift.
                    if (progress < previousBreathProgress) previousBreathProgress = -1f;
                    if (previousBreathProgress < .44f && progress >= .44f && progress < .64f)
                        Breath(home, owner, anchors, RetroSfxId.HomeBedExhale, .7f);
                    previousBreathProgress = progress;
                }
                else if (mode == 2)
                {
                    // Recovery still follows the chest; the wide return and
                    // seated idle have no close-miked audible breathing loop.
                    float radians = wakeSeconds * 4.2f + Mathf.PI * .5f;
                    int half = Mathf.FloorToInt(radians / Mathf.PI);
                    if (half != previousBreathHalf)
                    {
                        // Starting halfway through a motion waits for its next
                        // breath boundary.
                        if (!changed)
                            Breath(home, owner, anchors, half % 2 == 0 ? RetroSfxId.HomeBedInhale :
                                RetroSfxId.HomeBedExhale, .65f);
                        previousBreathHalf = half;
                    }
                }
            }

            private void Breath(HomeInteriorRoot home, object owner, Player3DBoneAnchors anchors,
                RetroSfxId cue, float gain, bool immediate = false)
            {
                // Motion chooses the onset; spacing only discards excess air
                // cues. It never schedules a second breath or catches up later.
                double now = Time.timeAsDouble;
                if (!immediate && now < nextBreathAt) return;
                if (Play(home.Audio, owner, cue, Position(anchors, Anchor.Head), gain))
                {
                    BreathCueCount++;
                    nextBreathAt = now + 6d;
                }
            }

            private static Vector3 Position(Player3DBoneAnchors anchors, Anchor at)
            {
                Transform anchor = at == Anchor.Head ? anchors.Mouth ?? anchors.Head :
                    at == Anchor.LeftHand ? anchors.LeftGrip : at == Anchor.RightHand ? anchors.RightGrip :
                    at == Anchor.LeftFoot ? anchors.LeftFoot : at == Anchor.RightFoot ? anchors.RightFoot : anchors.Pelvis;
                return anchor != null ? anchor.position : Vector3.zero;
            }
        }
    }
}
