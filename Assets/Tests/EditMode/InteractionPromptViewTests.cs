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
        [Test]
        public void SpokenFeedback_TypesOutButIsFramedForTheWholeLine()
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

                Assert.That(
                    view.ShowSpokenFeedbackAt(
                        CemeteryWatchmanQuips.LineKeys[0],
                        6f,
                        10f,
                        speaker),
                    Is.True);

                string whole = view.GetDisplayedTextAt(10f);
                Assert.That(whole, Is.Not.Empty);
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
                Assert.That(
                    view.GetDisplayedTextAt(10.2f),
                    Is.EqualTo(whole),
                    "The frame is still measured from the whole line.");

                view.AdvanceTo(
                    10f +
                    whole.Length / SpeechDelivery.CharactersPerSecond +
                    0.1f);
                Assert.That(
                    view.GetRevealedTextAt(10.1f),
                    Is.EqualTo(whole),
                    "And it finishes.");
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
        /// New behaviour: walking away used to leave the answer hanging
        /// at the bottom of the screen for its full duration. Only
        /// disabling input ever cleared it.
        /// </summary>
        [Test]
        public void SpokenFeedback_IsDroppedWhenTheHeroWalksAway()
        {
            var gameObject = new GameObject("Walk Away Prompt Test");
            var speakerObject = new GameObject("Speaker");
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
                    speakerObject.transform,
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

                heroObject.transform.position = new Vector3(
                    NpcEarshotProfile.ConversationFaintRadiusMeters +
                    2f,
                    0f,
                    0f);
                view.AdvanceTo(10.2f);
                Assert.That(
                    view.IsFeedbackVisibleAt(10.2f),
                    Is.False,
                    "Out of his earshot, the answer is gone.");
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

        [Test]
        public void PlayerInteractor_DisablingInputClearsTimedFeedback()
        {
            var gameObject = new GameObject("Player Interactor Test");
            var viewObject = new GameObject("Interaction Prompt Test");
            try
            {
                InteractionPromptView view =
                    viewObject.AddComponent<InteractionPromptView>();
                PlayerInteractor interactor =
                    gameObject.AddComponent<PlayerInteractor>();
                interactor.Initialize(view);

                Assert.That(
                    interactor.ShowFeedback(
                        "interaction.feedback",
                        5f),
                    Is.True);
                Assert.That(view.IsFeedbackVisible, Is.True);

                interactor.SetInputEnabled(false);

                Assert.That(view.IsFeedbackVisible, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
                Object.DestroyImmediate(viewObject);
            }
        }
    }
}
