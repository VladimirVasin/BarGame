using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.EditMode
{
    public sealed class InteractionPromptViewTests
    {
        [Test]
        public void SetPrompt_WithAction_InvokesCurrentAction()
        {
            var gameObject = new GameObject("Interaction Prompt Test");
            try
            {
                InteractionPromptView view =
                    gameObject.AddComponent<InteractionPromptView>();
                int invocationCount = 0;

                view.SetPrompt(
                    "interaction.test",
                    () =>
                    {
                        invocationCount++;
                        return true;
                    });

                Assert.That(view.IsClickable, Is.True);
                Assert.That(view.TryInvokePrompt(), Is.True);
                Assert.That(invocationCount, Is.EqualTo(1));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SetPrompt_WithoutActionOrKey_ClearsPreviousAction()
        {
            var gameObject = new GameObject("Interaction Prompt Test");
            try
            {
                InteractionPromptView view =
                    gameObject.AddComponent<InteractionPromptView>();
                int invocationCount = 0;
                view.SetPrompt(
                    "interaction.test",
                    () =>
                    {
                        invocationCount++;
                        return true;
                    });

                view.SetPrompt("interaction.status");
                Assert.That(view.IsClickable, Is.False);
                Assert.That(view.TryInvokePrompt(), Is.False);

                view.SetPrompt(
                    "interaction.test",
                    () =>
                    {
                        invocationCount++;
                        return true;
                    });
                view.SetPrompt(string.Empty);
                Assert.That(view.IsClickable, Is.False);
                Assert.That(view.TryInvokePrompt(), Is.False);
                Assert.That(invocationCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void TimedFeedback_TemporarilyOverridesAndDisablesPrompt()
        {
            var gameObject = new GameObject("Interaction Prompt Test");
            try
            {
                InteractionPromptView view =
                    gameObject.AddComponent<InteractionPromptView>();
                view.SetPrompt("interaction.test", () => true);

                Assert.That(
                    view.ShowFeedbackAt(
                        "interaction.feedback",
                        2.5f,
                        10f),
                    Is.True);
                Assert.That(
                    view.GetPromptKeyAt(10f),
                    Is.EqualTo("interaction.feedback"));
                Assert.That(view.IsClickableAt(12.49f), Is.False);
                Assert.That(view.IsFeedbackVisibleAt(12.49f), Is.True);
                Assert.That(
                    view.GetPromptKeyAt(12.5f),
                    Is.EqualTo("interaction.test"));
                Assert.That(view.IsClickableAt(12.5f), Is.True);
                Assert.That(view.IsFeedbackVisibleAt(12.5f), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void FormattedFeedback_KeepsTheKeyAndComposesTheValue()
        {
            var gameObject = new GameObject("Interaction Prompt Test");
            try
            {
                InteractionPromptView view =
                    gameObject.AddComponent<InteractionPromptView>();
                view.SetPrompt("interaction.test", () => true);

                // The catalog owns the wording around the number, so
                // what the view keeps is still a key — everything that
                // reads PromptKey must go on seeing one.
                Assert.That(
                    view.ShowFormattedFeedbackAt(
                        "cemetery.gravedigging.paid",
                        3f,
                        10f,
                        150),
                    Is.True);
                Assert.That(
                    view.GetPromptKeyAt(10f),
                    Is.EqualTo("cemetery.gravedigging.paid"));
                string composed = view.GetDisplayedTextAt(10f);
                Assert.That(composed, Does.Contain("150"));
                Assert.That(composed, Does.Not.Contain("{0}"));

                // The prompt underneath never takes the arguments with
                // it once the line expires.
                Assert.That(
                    view.GetDisplayedTextAt(13f),
                    Is.EqualTo("interaction.test"));

                // And an ordinary unformatted line clears them.
                Assert.That(
                    view.ShowFeedbackAt(
                        "cemetery.gravedigging.paid",
                        3f,
                        20f),
                    Is.True);
                Assert.That(
                    view.GetDisplayedTextAt(20f),
                    Does.Contain("{0}"));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        // -- Spoken lines ------------------------------------------------

        /// <summary>
        /// The panel is a fixed frame the line is typed into. Sizing it
        /// off the growing substring would step the box a row taller
        /// mid-word, which is the whole reason the overhead bubble has
        /// always measured from the whole line.
        /// </summary>
        [TestCase(0.1f, false)]
        [TestCase(6f, true)]
        public void SpokenFeedback_TypesOutButIsFramedForTheWholeLine(float requestedDuration, bool pause)
        {
            var gameObject = new GameObject("Spoken Prompt Test");
            var speakerObject = new GameObject("Speaker");
            try
            {
                InteractionPromptView view =
                    gameObject.AddComponent<InteractionPromptView>();
                var speaker = new NpcSpeaker(
                    speakerObject,
                    speakerObject.transform,
                    NpcVoiceCatalog.WatchmanDesignId,
                    NpcEarshotProfile.Conversation);

                string line = LocalizationService.Get(CemeteryWatchmanQuips.LineKeys[0]);
                float duration = Mathf.Max(requestedDuration,
                    SpeechDelivery.ResolveSpokenDuration(line, SpeechDelivery.ReadingTailSeconds));
                Assert.That(
                    view.ShowSpokenFeedbackAt(
                        CemeteryWatchmanQuips.LineKeys[0],
                        requestedDuration,
                        10f,
                        speaker),
                    Is.True);

                string whole = view.GetDisplayedTextAt(10f);
                Assert.That(whole, Is.Not.Empty);
                Assert.That(view.SpokenBubbles, Is.Not.Null);
                Assert.That(view.SpokenBubbles.IsShowing(speakerObject), Is.True);
                Assert.That(view.GetBottomPromptKeyAt(10f), Is.Empty,
                    "A spoken answer is never duplicated in the bottom panel.");
                Assert.That(
                    view.GetRevealedTextAt(10f),
                    Is.Empty,
                    "Nothing is typed on the frame it opens.");

                view.AdvanceTo(10.2f);
                string partial = view.GetRevealedTextAt(10.2f);
                Assert.That(partial.Length, Is.GreaterThan(0));
                Assert.That(
                    partial.Length,
                    Is.LessThan(whole.Length),
                    "A fifth of a second is not a whole line.");
                Assert.That(whole, Does.StartWith(partial));
                Assert.That(view.SpokenBubbles.RevealedTextOf(speakerObject), Is.EqualTo(partial),
                    "The facade observes the bubble's one typewriter.");
                Assert.That(view.ShowSpokenFeedbackAt(CemeteryWatchmanQuips.LineKeys[1],
                    6f, 10.2f, speaker), Is.False,
                    "Repeated E cannot replace a sentence which is still being read.");
                Assert.That(
                    view.GetDisplayedTextAt(10.2f),
                    Is.EqualTo(whole),
                    "The frame is still measured from the whole line.");

                float pausedSeconds = pause ? 20f : 0f;
                if (pause)
                {
                    view.AdvanceTo(10.2f, true);
                    view.AdvanceTo(10.2f + pausedSeconds, true);
                    Assert.That(view.IsFeedbackVisibleAt(10.2f + pausedSeconds), Is.True);
                    Assert.That(view.GetRevealedTextAt(10.2f + pausedSeconds), Is.EqualTo(partial));
                    Assert.That(view.SpokenBubbles.RenderEnabled, Is.False);
                    view.AdvanceTo(10.2f + pausedSeconds, false);
                    Assert.That(view.SpokenBubbles.RenderEnabled, Is.True);
                }

                view.AdvanceTo(
                    10f + pausedSeconds +
                    whole.Length / SpeechDelivery.CharactersPerSecond +
                    0.1f);
                Assert.That(
                    view.GetRevealedTextAt(10.1f),
                    Is.EqualTo(whole),
                    "And it finishes.");
                Assert.That(duration - whole.Length / SpeechDelivery.CharactersPerSecond,
                    Is.GreaterThanOrEqualTo(SpeechDelivery.ReadingTailSeconds - 0.0001f),
                    "The completed sentence keeps the shared reading tail.");
                view.AdvanceTo(10f + pausedSeconds + duration - 0.01f);
                Assert.That(view.IsFeedbackVisibleAt(10f + pausedSeconds + duration - 0.01f), Is.True);
                Assert.That(view.GetRevealedTextAt(10f + pausedSeconds + duration - 0.01f), Is.EqualTo(whole));
                view.AdvanceTo(10f + pausedSeconds + duration + 0.01f);
                Assert.That(view.IsFeedbackVisibleAt(10f + pausedSeconds + duration + 0.01f), Is.False);
                Assert.That(view.SpokenBubbles.IsShowing(speakerObject), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(speakerObject);
                Object.DestroyImmediate(gameObject);
            }
        }

        /// <summary>
        /// A locked door, a cashier who does not blink, a prompt saying
        /// what E does: none of those is somebody talking, and none of
        /// them types or ticks.
        /// </summary>
        [Test]
        public void NarrationAndPrompts_StayWholeAndSilent()
        {
            var gameObject = new GameObject("Narration Prompt Test");
            try
            {
                InteractionPromptView view =
                    gameObject.AddComponent<InteractionPromptView>();
                view.SetPrompt("interaction.test", () => true);

                Assert.That(
                    view.GetRevealedTextAt(0f),
                    Is.EqualTo(view.GetDisplayedTextAt(0f)),
                    "A prompt is whole from the first frame.");

                Assert.That(
                    view.ShowFeedbackAt(
                        "city.dumpster.placeholder",
                        3f,
                        10f),
                    Is.True);
                Assert.That(view.IsSpeaking, Is.False);
                Assert.That(view.ShowSpokenFeedbackAt("city.dumpster.placeholder", 3f, 10f,
                    NpcSpeaker.None), Is.False,
                    "A missing speaker cannot turn an attempted speech into bottom narration.");
                Assert.That(view.SpokenBubbles, Is.Null);
                Assert.That(view.GetBottomPromptKeyAt(10f), Is.EqualTo("city.dumpster.placeholder"));
                Assert.That(
                    view.GetRevealedTextAt(10f),
                    Is.EqualTo(view.GetDisplayedTextAt(10f)),
                    "Narration arrives whole and instantly.");

                view.AdvanceTo(10.05f);
                Assert.That(
                    view.GetRevealedTextAt(10.05f),
                    Is.EqualTo(view.GetDisplayedTextAt(10.05f)));
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        /// <summary>
        /// A spoken answer belongs to a live, nearby speaker and to the
        /// active interaction view; teardown must release its bubble too.
        /// </summary>
        [TestCase("distance")]
        [TestCase("anchor")]
        [TestCase("speaker")]
        [TestCase("disable")]
        [TestCase("clear")]
        public void SpokenFeedback_ReleasesItsBubbleWhenTheInteractionEnds(string reason)
        {
            var gameObject = new GameObject("Walk Away Prompt Test");
            var speakerObject = new GameObject("Speaker");
            var headObject = new GameObject("Actual Head");
            headObject.transform.SetParent(speakerObject.transform, false);
            var heroObject = new GameObject("Hero");
            try
            {
                InteractionPromptView view =
                    gameObject.AddComponent<InteractionPromptView>();
                view.SetListener(heroObject.transform);
                speakerObject.transform.position = Vector3.zero;
                heroObject.transform.position = Vector3.zero;

                var speaker = new NpcSpeaker(
                    speakerObject,
                    headObject.transform,
                    NpcVoiceCatalog.FishermanDesignId,
                    NpcEarshotProfile.Conversation);

                Assert.That(
                    view.ShowSpokenFeedbackAt(
                        SeacoastFishermanQuips.LineKeys[0],
                        6f,
                        10f,
                        speaker),
                    Is.True);
                view.AdvanceTo(10.1f);
                Assert.That(view.IsFeedbackVisibleAt(10.1f), Is.True);

                if (reason == "distance")
                    heroObject.transform.position = new Vector3(
                        NpcEarshotProfile.ConversationFaintRadiusMeters + 2f, 0f, 0f);
                else if (reason == "anchor") Object.DestroyImmediate(headObject);
                else if (reason == "speaker") Object.DestroyImmediate(speakerObject);
                else if (reason == "disable") view.enabled = false;
                else view.ClearFeedback();
                view.AdvanceTo(10.2f);
                Assert.That(
                    view.IsFeedbackVisibleAt(10.2f),
                    Is.False,
                    "The ended interaction cannot leave an orphaned line.");
                Assert.That(view.SpokenBubbles.IsShowing(speakerObject), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(heroObject);
                Object.DestroyImmediate(speakerObject);
                Object.DestroyImmediate(gameObject);
            }
        }

        /// <summary>
        /// A wage is still his line, so it types out — and the number
        /// still has to be in it.
        /// </summary>
        [Test]
        public void FormattedSpokenFeedback_TypesTheComposedLine()
        {
            var gameObject = new GameObject("Formatted Spoken Test");
            var speakerObject = new GameObject("Speaker");
            try
            {
                InteractionPromptView view =
                    gameObject.AddComponent<InteractionPromptView>();
                var speaker = new NpcSpeaker(
                    speakerObject,
                    speakerObject.transform,
                    NpcVoiceCatalog.WatchmanDesignId,
                    NpcEarshotProfile.Conversation);

                Assert.That(
                    view.ShowSpokenFeedbackAt(
                        CemeteryWatchmanInteraction.PaidFeedbackKey,
                        6f,
                        10f,
                        speaker,
                        150),
                    Is.True);

                string whole = view.GetDisplayedTextAt(10f);
                Assert.That(whole, Does.Contain("150"));
                Assert.That(whole, Does.Not.Contain("{0}"));

                // Well inside the six seconds it is up: past the end of
                // the window nothing is stepped, because there is
                // nothing left on screen to step.
                view.AdvanceTo(14f);
                Assert.That(
                    view.GetRevealedTextAt(14f),
                    Is.EqualTo(whole),
                    "The typed line carries the composed number.");
            }
            finally
            {
                Object.DestroyImmediate(speakerObject);
                Object.DestroyImmediate(gameObject);
            }
        }

        [TestCase(false, false)]
        [TestCase(true, false)]
        [TestCase(false, true)]
        [TestCase(true, true)]
        public void PlayerInteractor_DisablingInputClearsFeedbackExceptSpeechHeldByAMenu(bool spoken, bool modal)
        {
            var gameObject = new GameObject("Player Interactor Test");
            var viewObject = new GameObject("Interaction Prompt Test");
            var modalLock = new BarMinigameModalLock();
            try
            {
                InteractionPromptView view =
                    viewObject.AddComponent<InteractionPromptView>();
                PlayerInteractor interactor =
                    gameObject.AddComponent<PlayerInteractor>();
                interactor.Initialize(view);

                bool shown = spoken
                    ? interactor.ShowSpokenFeedback(CemeteryWatchmanQuips.LineKeys[0], 5f,
                        new NpcSpeaker(gameObject, gameObject.transform,
                            NpcVoiceCatalog.WatchmanDesignId, NpcEarshotProfile.Conversation))
                    : interactor.ShowFeedback("interaction.feedback", 5f);
                Assert.That(shown, Is.True);
                Assert.That(view.IsFeedbackVisible, Is.True);

                if (modal) Assert.That(modalLock.TryCaptureAndDisable(interactor, null, null), Is.True);
                else interactor.SetInputEnabled(false);

                Assert.That(view.IsFeedbackVisible, Is.EqualTo(spoken && modal));
            }
            finally
            {
                modalLock.Restore();
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(viewObject);
            }
        }
    }
}
