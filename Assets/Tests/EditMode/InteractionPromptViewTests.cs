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
    }
}
