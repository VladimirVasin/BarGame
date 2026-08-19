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
