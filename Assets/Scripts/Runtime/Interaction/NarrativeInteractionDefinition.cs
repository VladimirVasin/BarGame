using System;
using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    public enum NarrativePageKind { HeroThought, DocumentText }
    public enum NarrativeCameraMode { ObjectSide, DocumentCloseUp, ObjectCloseUp }

    /// <summary>Optional final-page choice, passive prop attempt and held text outcome.</summary>
    public sealed class NarrativeConfirmation
    {
        public NarrativeConfirmation(string yesKey, string noKey, string replyKey, float attemptSeconds)
        {
            if (string.IsNullOrWhiteSpace(yesKey) || string.IsNullOrWhiteSpace(noKey) ||
                string.IsNullOrWhiteSpace(replyKey) || float.IsNaN(attemptSeconds) ||
                float.IsInfinity(attemptSeconds) || attemptSeconds <= 0f)
                throw new ArgumentException("An inspection confirmation needs keyed answers/reply and a finite attempt.");
            YesKey = yesKey; NoKey = noKey; ReplyKey = replyKey; AttemptSeconds = attemptSeconds;
        }
        public string YesKey { get; }
        public string NoKey { get; }
        public string ReplyKey { get; }
        public float AttemptSeconds { get; }
    }

    public readonly struct NarrativePage
    {
        public NarrativePage(string textKey, bool document = false, string attributionKey = null)
        {
            if (string.IsNullOrWhiteSpace(textKey)) throw new ArgumentException("A narrative page needs a localization key.", nameof(textKey));
            TextKey = textKey;
            Kind = document ? NarrativePageKind.DocumentText : NarrativePageKind.HeroThought;
            AttributionKey = attributionKey;
        }
        public string TextKey { get; }
        public NarrativePageKind Kind { get; }
        public string AttributionKey { get; }
    }

    /// <summary>Immutable content shared by any number of physical approach anchors.</summary>
    public sealed class NarrativeInteractionDefinition
    {
        public NarrativeInteractionDefinition(string id, string promptKey, IReadOnlyList<NarrativePage> pages)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("A narrative needs a stable ID.", nameof(id));
            if (string.IsNullOrWhiteSpace(promptKey)) throw new ArgumentException("A narrative needs a keyed prompt.", nameof(promptKey));
            if (pages == null || pages.Count == 0) throw new ArgumentException("A narrative needs pages.", nameof(pages));
            var copy = new NarrativePage[pages.Count];
            for (int index = 0; index < copy.Length; index++)
            {
                if (string.IsNullOrWhiteSpace(pages[index].TextKey)) throw new ArgumentException("A page has no key.", nameof(pages));
                copy[index] = pages[index];
            }
            Id = id; PromptKey = promptKey; Pages = Array.AsReadOnly(copy);
        }
        public string Id { get; }
        public string PromptKey { get; }
        public IReadOnlyList<NarrativePage> Pages { get; }
    }

    public readonly struct NarrativeStagingPlan
    {
        public NarrativeStagingPlan(PlayerAnimatedInteractionPose entry, Vector3 actionHip,
            PlayerAnimatedInteractionPose exit, Bounds focusBounds, Vector3 cameraSideHint,
            NarrativeCameraMode cameraMode = NarrativeCameraMode.ObjectSide, Vector3 cameraFront = default,
            float initialVerticalTolerance = .5f)
        {
            if (float.IsNaN(initialVerticalTolerance) || float.IsInfinity(initialVerticalTolerance) || initialVerticalTolerance < 0f)
                throw new ArgumentOutOfRangeException(nameof(initialVerticalTolerance));
            Entry = entry; ActionHip = actionHip; Exit = exit;
            FocusBounds = focusBounds; CameraSideHint = cameraSideHint;
            CameraMode = cameraMode; CameraFront = cameraFront; InitialVerticalTolerance = initialVerticalTolerance;
        }
        public PlayerAnimatedInteractionPose Entry { get; }
        public Vector3 ActionHip { get; }
        public PlayerAnimatedInteractionPose Exit { get; }
        public Bounds FocusBounds { get; }
        public Vector3 CameraSideHint { get; }
        public NarrativeCameraMode CameraMode { get; }
        public Vector3 CameraFront { get; }
        public float InitialVerticalTolerance { get; }

        /// <summary>The floor is authored by the scene's walkable plan, never guessed from the trigger.</summary>
        public static NarrativeStagingPlan Standing(Vector3 groundedFloor, Quaternion facing,
            Bounds focusBounds, Vector3 cameraSideHint, NarrativeCameraMode cameraMode = NarrativeCameraMode.ObjectSide,
            Vector3 cameraFront = default, float initialVerticalTolerance = .5f)
        {
            var entry = new PlayerAnimatedInteractionPose(
                groundedFloor + facing * PlayerDialogueActions.EntryGroundOffset + Vector3.up * PlayerFactory.GroundedRootOffset,
                facing, groundedFloor + facing * PlayerDialogueActions.EntryPelvisFromGround);
            var exit = new PlayerAnimatedInteractionPose(
                groundedFloor + facing * PlayerDialogueActions.ExitGroundOffset + Vector3.up * PlayerFactory.GroundedRootOffset,
                facing, groundedFloor + facing * PlayerDialogueActions.ExitPelvisFromGround);
            return new NarrativeStagingPlan(entry, groundedFloor + facing * PlayerDialogueActions.ActionPelvisFromGround,
                exit, focusBounds, cameraSideHint, cameraMode, cameraFront, initialVerticalTolerance);
        }
    }
}
