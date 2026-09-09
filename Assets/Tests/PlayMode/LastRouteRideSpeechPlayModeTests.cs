using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class LastRouteRideSpeechPlayModeTests
    {
        [Test]
        public void RoadSpeech_PreservesSilenceAndBagAcrossLegsWithoutTakingThePrompt()
        {
            var state = new LastRouteRideSpeechState(982451653u);
            var unique = new HashSet<int>();
            var host = new GameObject("Road speech contract");
            bool hadVoiceService = NpcSpeechVoice.Instance != null;
            try
            {
                var prompt = host.AddComponent<InteractionPromptView>();
                bool invoked = false;
                prompt.SetPrompt("interaction.radio_on", () => invoked = true);
                LastRouteRideSpeechView view = LastRouteRideSpeechView.Create(host.transform);
                var cameraHost = new GameObject("Untagged passenger camera");
                cameraHost.transform.SetParent(host.transform, false);
                var passengerCamera = cameraHost.AddComponent<Camera>();
                view.BindCamera(passengerCamera);
                var speaker = new NpcSpeaker(host, host.transform,
                    NpcVoiceCatalog.FerrymanDesignId, NpcEarshotProfile.Conversation);

                for (int trip = 0; trip < 2; trip++)
                {
                    state.BeginTrip();
                    Assert.That(state.SilenceRemaining, Is.InRange(12f, 18f));
                    float remaining = state.SilenceRemaining;
                    Assert.That(state.AdvanceSilence(remaining - 0.1f), Is.EqualTo(-1));
                    state.EnsureTrip(); // The replacement scene must not reset its half-trip.
                    Assert.That(state.SilenceRemaining, Is.EqualTo(0.1f).Within(0.0001f));

                    for (int ordinal = 0; ordinal < 5; ordinal++)
                    {
                        int line = state.AdvanceSilence(state.SilenceRemaining + 0.01f);
                        Assert.That(unique.Add(line), Is.True, "The bag repeats before exhaustion.");
                        view.Show(LastRouteRideSpeechState.LineKey(line), speaker);
                        Assert.That(view.Bubbles.IsDeclared(host), Is.True);
                        Assert.That(view.Bubbles.UseManualClock, Is.True);
                        Assert.That(view.Speaker.Anchor, Is.SameAs(host.transform));
                        Assert.That(view.BoundCamera, Is.SameAs(passengerCamera),
                            "Showing a line must retain the passenger's explicit camera.");
                        Assert.That(view.FullText, Is.Not.EqualTo(view.LineKey));
                        view.Advance(0.2f);
                        int revealed = view.RevealedCharacters;
                        Assert.That(revealed, Is.GreaterThan(0).And.LessThan(view.FullText.Length));
                        view.Advance(0f); // A paused journey supplies no clock advance.
                        Assert.That(view.RevealedCharacters, Is.EqualTo(revealed));
                        Assert.That(state.AdvanceSilence(500f), Is.EqualTo(-1),
                            "Typing and reading must not consume the next silence.");
                        Assert.That(prompt.IsFeedbackVisible, Is.False);
                        Assert.That(prompt.TryInvokePrompt(), Is.True);
                        Assert.That(invoked, Is.True);

                        // Scene/skip cuts consume the current line once, with a fresh
                        // silence, just as ordinary reading completion does.
                        view.Close();
                        state.FinishLine();
                        float silence = state.SilenceRemaining;
                        state.EnsureTrip();
                        state.FinishLine();
                        Assert.That(state.SilenceRemaining, Is.EqualTo(silence));
                        Assert.That(silence, Is.InRange(30f, 45f));
                        Assert.That(state.LinesThisTrip, Is.EqualTo(ordinal + 1));
                    }
                    Assert.That(state.AdvanceSilence(1000f), Is.EqualTo(-1));
                    state.EndTrip();
                    Assert.That(state.AdvanceSilence(1000f), Is.EqualTo(-1));
                }

                int previous = state.LastLineIndex;
                state.BeginTrip();
                Assert.That(state.AdvanceSilence(18.01f), Is.Not.EqualTo(previous),
                    "A new bag must not immediately repeat the previous bag's last line.");
                int quotaBeforeRemark = state.LinesThisTrip;
                int bagBeforeRemark = state.LastLineIndex;
                state.FinishLine();
                view.Show(LastRouteRideController.GloveboxReactionKey, speaker);
                Assert.That(view.FullText, Is.Not.EqualTo(view.LineKey));
                view.Advance(0.3f);
                Assert.That(view.IsSpeaking, Is.True);
                view.Close();
                state.FinishCabinRemark();
                Assert.That(state.LinesThisTrip, Is.EqualTo(quotaBeforeRemark));
                Assert.That(state.LastLineIndex, Is.EqualTo(bagBeforeRemark));
                Assert.That(state.SilenceRemaining, Is.InRange(30f, 45f));

                Assert.That(LastRouteRideController.RadioReactionDelaySeconds, Is.EqualTo(10f));
                int dislikedStation = state.DislikedRadioStationIndex;
                Assert.That(dislikedStation, Is.InRange(0, 2));
                Assert.That(state.HasReactedToRadioThisTrip, Is.False);
                state.ArmRadioReaction(LastRouteRideController.RadioReactionDelaySeconds);
                Assert.That(state.AdvanceRadioReaction(3f), Is.False);
                state.CancelRadioReaction();
                Assert.That(state.AdvanceRadioReaction(11f), Is.False,
                    "Switching away or off cancels unfinished listening without spending the reaction.");
                Assert.That(state.HasReactedToRadioThisTrip, Is.False);

                state.ArmRadioReaction(LastRouteRideController.RadioReactionDelaySeconds);
                Assert.That(state.AdvanceRadioReaction(2f), Is.False,
                    "The old two-second delay must not produce the complaint.");
                state.FinishLine();
                state.EnsureTrip(); // The tunnel hands this same pending state to the new controller.
                Assert.That(state.DislikedRadioStationIndex, Is.EqualTo(dislikedStation),
                    "The scene handover must keep this trip's randomly chosen station.");
                Assert.That(state.HasReactedToRadioThisTrip, Is.False);
                Assert.That(state.AdvanceRadioReaction(0f), Is.False);
                Assert.That(state.RadioReactionRemaining, Is.EqualTo(8f),
                    "Loading and pause must preserve the remaining listening time.");
                Assert.That(state.AdvanceRadioReaction(7.99f), Is.False);
                Assert.That(state.AdvanceRadioReaction(0.02f), Is.True);
                Assert.That(state.HasReactedToRadioThisTrip, Is.True);
                Assert.That(state.AdvanceRadioReaction(11f), Is.False,
                    "A delayed complaint is delivered once, without a backlog.");
                state.EnsureTrip();
                Assert.That(state.DislikedRadioStationIndex, Is.EqualTo(dislikedStation));
                Assert.That(state.HasReactedToRadioThisTrip, Is.True);
                state.ArmRadioReaction(LastRouteRideController.RadioReactionDelaySeconds);
                Assert.That(state.HasPendingRadioReaction, Is.False);
                state.CancelRadioReaction();
                state.ArmRadioReaction(LastRouteRideController.RadioReactionDelaySeconds);
                Assert.That(state.AdvanceRadioReaction(11f), Is.False,
                    "Returning to the station or toggling power cannot repeat a used trip reaction.");
                state.EndTrip();
                Assert.That(state.DislikedRadioStationIndex, Is.EqualTo(dislikedStation));
                Assert.That(state.HasReactedToRadioThisTrip, Is.True);
                state.BeginTrip();
                Assert.That(state.DislikedRadioStationIndex, Is.InRange(0, 2));
                Assert.That(state.HasReactedToRadioThisTrip, Is.False);
                Assert.That(state.HasPendingRadioReaction, Is.False);
                state.ArmRadioReaction(LastRouteRideController.RadioReactionDelaySeconds);
                Assert.That(state.AdvanceRadioReaction(10.01f), Is.True,
                    "The next trip receives its own single reaction.");

                var newSession = new LastRouteRideSpeechState(1u);
                Assert.That(newSession.DislikedRadioStationIndex, Is.EqualTo(-1));
                newSession.ArmRadioReaction(LastRouteRideController.RadioReactionDelaySeconds);
                Assert.That(newSession.HasPendingRadioReaction, Is.False,
                    "A station is not selected before any trip starts.");
            }
            finally
            {
                Object.DestroyImmediate(host);
                if (!hadVoiceService && NpcSpeechVoice.Instance != null)
                    Object.DestroyImmediate(NpcSpeechVoice.Instance.gameObject);
            }
        }
    }
}
