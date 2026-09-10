using System;
using System.Collections;
using NUnit.Framework;
using UnityEngine;

namespace BarPromenade.Tests.PlayMode
{
    public sealed partial class AreaCaptureFixture
    {
        private static IEnumerator ValidatePortForemanSpeech(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            ValidatePortForemanConversationSchedule();
            ValidatePortForemanSnackTimeline();
            CityPortForeman foreman = crew.Foreman;
            CityPortForemanInteraction interaction = foreman.Interaction;
            CityPortConversationController speech = crew.GetComponent<CityPortConversationController>();
            PlayerInteractor hero = city.Player.Interactor;
            Vector3 savedHero = hero.transform.position;
            double savedPort = port.ElapsedSeconds, savedLife = crew.LifeElapsedSeconds;
            bool savedManual = crew.UseManualClock, savedForce = port.ForcePresentation;
            bool savedInput = hero.InputEnabled, savedMotor = city.Player.Motor.InputEnabled;
            bool savedRender = speech.Bubbles.RenderEnabled;
            double held = CityPortCycle.CycleDurationSeconds - .001d, life = 0d;
            try
            {
                crew.UseManualClock = true;
                port.ForcePresentation = true;
                city.Player.Motor.SetInputEnabled(false);
                hero.SetInputEnabled(true);
                city.Player.Motor.Teleport(foreman.transform.position + foreman.transform.forward * 1.25f);
                speech.Initialize(port, crew, camera, hero.transform, 1537);
                Sample(0d);

                // E can arrive during an actual bite, not only after the
                // foreman has already lowered his hand for another worker.
                // A prior visual capture may leave a partly eaten carrot;
                // the rewind preserves that custody until the next pocket take.
                Until(() => foreman.Snack.Phase == CityPortForemanSnackPhase.Bite1 &&
                    foreman.Snack.ActionSeconds >= 1.4d, 36d, .1d);
                Assert.That(Vector3.Distance(foreman.BiteTip.position, foreman.Mouth.position), Is.LessThan(.03f));
                interaction.Interact(hero);
                Sample(life + .1d);
                Assert.That(speech.ForemanInteractionPending, Is.True);
                Assert.That(speech.Bubbles.IsShowing(foreman), Is.False,
                    "The question waits while the carrot leaves his mouth.");
                Assert.That(foreman.IsSpeaking, Is.False);
                Until(IsOffer, 1d, .1d);
                Assert.That(foreman.EatingWeight, Is.LessThan(.01f));
                Assert.That(foreman.Snack.BitesTaken, Is.Zero, "An interrupted pre-contact bite is still available.");
                Assert.That(Vector3.Distance(foreman.CarrotTip.position, foreman.Mouth.position), Is.GreaterThan(.20f));
                interaction.Cancel();
                Sample(life + .1d);

                Until(() => foreman.Snack.Phase == CityPortForemanSnackPhase.Discard &&
                    foreman.Snack.ActionSeconds >= .4d, 32d, .1d);
                long oldCarrot = foreman.Snack.CycleCount;
                interaction.Interact(hero);
                Sample(life + .1d);
                Assert.That(speech.ForemanInteractionPending, Is.True, "E can queue while the stem is being discarded.");
                Assert.That(foreman.Snack.CanTalk, Is.False);
                Assert.That(speech.Bubbles.IsShowing(foreman), Is.False);
                Until(IsOffer, 6d, .1d);
                Assert.That(foreman.Snack.CycleCount, Is.EqualTo(oldCarrot + 1));
                Assert.That(foreman.Snack.BitesTaken, Is.Zero);
                Assert.That(foreman.Snack.HoldingCarrot, Is.True, "The pocket action completes before the queued question.");
                interaction.Cancel();
                Sample(life + .1d);

                // A real shore pair must finish after E, rather than being
                // replaced by an unrelated on-demand voice or detached reply.
                Until(() => speech.Schedule.Current.IsSpeaking &&
                    speech.Schedule.Current.Exchange.Kind == CityPortConversationKind.Foreman &&
                    speech.Schedule.Current.SpeakerRole == CityPortConversationCatalog.ForemanRole, 420d, 1d);
                CityPortConversationExchange current = speech.Schedule.Current.Exchange;
                interaction.Interact(hero);
                Assert.That(speech.ForemanInteractionPending, Is.True);
                Assert.That(interaction.IsOpen, Is.False);
                Assert.That(hero.InputEnabled, Is.True, "Waiting for the pair keeps the hero free.");
                Assert.That(speech.RequestForemanInteraction(hero, null), Is.False, "One E request is retained.");
                bool reply = false;
                double limit = life + 16d;
                while (!IsOffer())
                {
                    Sample(life + .1d);
                    Assert.That(life, Is.LessThan(limit));
                    CityPortConversationTurn turn = speech.Schedule.Current;
                    if (!turn.HasExchange) continue;
                    Assert.That(turn.Exchange.Kind, Is.EqualTo(current.Kind));
                    Assert.That(turn.Exchange.Variant, Is.EqualTo(current.Variant));
                    reply |= turn.IsSpeaking && turn.SpeakerRole == current.SecondRole && turn.LineKey == current.SecondKey;
                }
                Assert.That(reply, Is.True, "The subordinate completes this complaint's authored answer.");
                double offerStarted = life;
                interaction.Interact(hero);
                Assert.That(speech.LastLineKey, Is.EqualTo(CityPortConversationController.ForemanOfferKey));

                Until(() => life >= offerStarted + 1.1d, 2d, .1d);
                Vector3 hand = foreman.RightHand.position;
                Vector3 leftFoot = foreman.LeftFoot.position, rightFoot = foreman.RightFoot.position;
                Assert.That(foreman.IsSpeaking, Is.True);
                Assert.That(foreman.GestureWeight, Is.GreaterThan(.95f));
                Assert.That(foreman.EatingWeight, Is.LessThan(.01f), "Eating yields to the shared conversation.");
                Assert.That(Vector3.Distance(foreman.CarrotTip.position, foreman.Mouth.position), Is.GreaterThan(.20f),
                    "The carrot is lowered while he speaks.");
                Until(() => life >= offerStarted + 1.3d, 1d, .1d);
                Assert.That(Vector3.Distance(foreman.RightHand.position, hand), Is.GreaterThan(.04f),
                    "The actual speaking hand shakes after the blend has settled.");
                Assert.That(Vector3.Distance(foreman.LeftFoot.position, leftFoot), Is.LessThan(.008f));
                Assert.That(Vector3.Distance(foreman.RightFoot.position, rightFoot), Is.LessThan(.008f));
                yield return CapturePortSocialPose(camera, "port-foreman-02-grumble", camera.transform.position,
                    foreman.Head.position - Vector3.up * .35f, camera.fieldOfView);

                using (GameTimeScaleRuntime.AcquirePause())
                {
                    yield return null;
                    Pose stoppedHand = new Pose(foreman.RightHand.position, foreman.RightHand.rotation);
                    string text = speech.Bubbles.RevealedTextOf(foreman);
                    double stoppedLife = crew.LifeElapsedSeconds;
                    yield return null;
                    yield return null;
                    Assert.That(speech.Bubbles.RenderEnabled, Is.False);
                    Assert.That(crew.LifeElapsedSeconds, Is.EqualTo(stoppedLife));
                    Assert.That(speech.Bubbles.RevealedTextOf(foreman), Is.EqualTo(text));
                    Assert.That(Vector3.Distance(foreman.RightHand.position, stoppedHand.position), Is.LessThan(.001f));
                    Assert.That(Quaternion.Angle(foreman.RightHand.rotation, stoppedHand.rotation), Is.LessThan(.01f));
                }
                yield return null;
                Assert.That(speech.Bubbles.RenderEnabled, Is.True);
                Assert.That(speech.ForemanInteractionPending, Is.True);
                Assert.That(speech.LastLineKey, Is.EqualTo(CityPortConversationController.ForemanOfferKey));
                Until(() => interaction.IsOpen, 6d, .1d);
                Assert.That(speech.ForemanInteractionReady, Is.True);
                Assert.That(hero.InputEnabled, Is.False);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.True);
                Sample(life + .1d);
                Assert.That(interaction.IsOpen, Is.True, "The menu's own input lock must not cancel its request.");
                Assert.That(speech.Bubbles.IsShowing(foreman), Is.False);
                Assert.That(interaction.SelectChoice(true), Is.True);
                Assert.That(interaction.Confirm(), Is.True);
                Assert.That(hero.InputEnabled, Is.True);
                Assert.That(city.Player.Motor.InputEnabled, Is.False, "Restore the motor's pre-menu state.");
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                Sample(life + .1d);
                Assert.That(speech.LastLineKey, Is.EqualTo(CityPortConversationController.ForemanAcceptKey));
                Assert.That(speech.Bubbles.IsShowing(foreman), Is.True);
                Assert.That(speech.RequestForemanChoice(hero, false), Is.False, "A confirmed choice cannot replace his answer.");
                Until(() => !speech.ForemanInteractionPending, 6d, .1d);

                interaction.Interact(hero);
                Until(() => interaction.IsOpen, 7d, .1d);
                Assert.That(interaction.SelectChoice(false), Is.True);
                Assert.That(interaction.Confirm(), Is.True);
                Sample(life + .1d);
                Assert.That(speech.LastLineKey, Is.EqualTo(CityPortConversationController.ForemanDeclineKey));
                Until(() => !speech.ForemanInteractionPending, 6d, .1d);

                // Walking away from a displayed choice restores the same
                // modal input lease and leaves no delayed answer on return.
                interaction.Interact(hero);
                Until(() => interaction.IsOpen, 7d, .1d);
                Vector3 nearby = hero.transform.position;
                city.Player.Motor.Teleport(nearby + foreman.transform.forward * 5f);
                Sample(life + .1d);
                Assert.That(interaction.IsOpen || speech.ForemanInteractionPending, Is.False);
                Assert.That(hero.InputEnabled, Is.True);
                Assert.That(BarMinigameModalLock.IsAnyLocked, Is.False);
                city.Player.Motor.Teleport(nearby);
                Sample(life + .1d);
                Assert.That(speech.Bubbles.IsShowing(foreman), Is.False);

                interaction.Interact(hero);
                Until(IsOffer, 8d, .1d);
                foreman.gameObject.SetActive(false);
                Sample(life + .1d);
                Assert.That(speech.ForemanInteractionPending || speech.Bubbles.IsShowing(foreman), Is.False);
                foreman.gameObject.SetActive(true);
                Sample(life + .1d);
                Sample(life + .1d);
                Assert.That(speech.ForemanInteractionPending, Is.False);
                interaction.Interact(hero);
                Until(IsOffer, 8d, .1d);
                Sample(life + 500d);
                Assert.That(speech.ForemanInteractionPending || interaction.IsOpen || speech.Bubbles.IsShowing(foreman), Is.False,
                    "A reconstructed clock cannot deliver an old player question or its choices.");
                Assert.That(hero.InputEnabled, Is.True);
                Debug.Log("PORT FOREMAN SPEECH: paired reply precedes E; one bubble, moving speaking hand, pause/resume, both choices and bounded cancellation.");
            }
            finally
            {
                interaction.Cancel();
                foreman.gameObject.SetActive(true);
                port.ForcePresentation = savedForce;
                port.ApplyAt(savedPort, 15f);
                crew.ApplyAt(savedPort, savedLife);
                foreman.ApplyAt(savedLife);
                speech.Initialize(port, crew, camera, port.PresentationObserver);
                speech.Bubbles.RenderEnabled = savedRender;
                crew.UseManualClock = savedManual;
                city.Player.Motor.Teleport(savedHero);
                hero.SetInputEnabled(savedInput);
                city.Player.Motor.SetInputEnabled(savedMotor);
            }

            bool IsOffer() => speech.LastLineKey == CityPortConversationController.ForemanOfferKey &&
                speech.ForemanInteractionPending && !speech.Schedule.Current.HasExchange && speech.Bubbles.IsShowing(foreman);

            void Until(Func<bool> ready, double maximum, double step)
            {
                double end = life + maximum;
                while (!ready())
                {
                    Assert.That(life, Is.LessThan(end), "The requested speech transition must stay bounded.");
                    Sample(life + step);
                }
            }

            void Sample(double seconds)
            {
                life = seconds;
                port.ApplyAt(held, 15f);
                crew.ApplyAt(held, life);
                speech.ApplyAt();
                foreman.ApplyAt(life);
                int showing = speech.Bubbles.IsShowing(foreman) ? 1 : 0;
                for (int role = 0; role < CityPortConversationCatalog.ForemanRole; role++)
                {
                    var actor = crew.GetWorker(role);
                    if (actor != null && speech.Bubbles.IsShowing(actor)) showing++;
                }
                Assert.That(showing, Is.LessThanOrEqualTo(1), "All port speech, including the player's stub, has one owner.");
            }
        }

        private static void ValidatePortForemanSnackTimeline()
        {
            var snack = new CityPortForemanSnackTimeline();
            double life = 0d;
            snack.Advance(life, false);
            Assert.That(snack.Current.HoldingCarrot, Is.True);
            int[] seenBites = new int[4];
            int lastBites = 0, throws = 0;
            bool held = true, sawFlight = false;
            while (snack.Current.CycleCount == 0)
            {
                life += .05d;
                Assert.That(life, Is.LessThan(32d));
                CityPortForemanSnackSnapshot state = snack.Advance(life, false);
                if (state.BitesTaken > lastBites)
                {
                    Assert.That(state.BitesTaken, Is.EqualTo(lastBites + 1));
                    seenBites[state.BitesTaken]++;
                    Assert.That(state.ActionSeconds, Is.GreaterThanOrEqualTo(CityPortForemanSnackTimeline.BiteCommitSeconds));
                }
                if (held && !state.HoldingCarrot)
                {
                    throws++;
                    Assert.That(state.Phase, Is.EqualTo(CityPortForemanSnackPhase.Discard));
                    Assert.That(state.BitesTaken, Is.EqualTo(3));
                    Assert.That(state.ActionSeconds, Is.GreaterThanOrEqualTo(CityPortForemanSnackTimeline.DiscardReleaseSeconds));
                }
                if (state.StemInFlight)
                {
                    sawFlight = true;
                    Assert.That(state.HoldingCarrot, Is.False);
                    Assert.That(state.ThrowProgress, Is.InRange(0f, 1f));
                }
                if (state.CycleCount > 0)
                {
                    Assert.That(state.Phase, Is.EqualTo(CityPortForemanSnackPhase.Take));
                    Assert.That(state.ActionSeconds, Is.GreaterThanOrEqualTo(CityPortForemanSnackTimeline.TakePickupSeconds));
                    Assert.That(state.BitesTaken, Is.Zero);
                    Assert.That(state.HoldingCarrot, Is.True);
                }
                bool atomic = state.Phase == CityPortForemanSnackPhase.Discard || state.Phase == CityPortForemanSnackPhase.Take;
                Assert.That(state.CanTalk, Is.EqualTo(!atomic));
                CityPortForemanSnackSnapshot repeated = snack.Advance(life, false);
                Assert.That(repeated.Phase, Is.EqualTo(state.Phase));
                Assert.That(repeated.ActionSeconds, Is.EqualTo(state.ActionSeconds));
                Assert.That(repeated.BitesTaken, Is.EqualTo(state.BitesTaken));
                Assert.That(repeated.CycleCount, Is.EqualTo(state.CycleCount), "A duplicate frame cannot take another carrot.");
                lastBites = state.BitesTaken;
                held = state.HoldingCarrot;
            }
            CollectionAssert.AreEqual(new[] { 0, 1, 1, 1 }, seenBites);
            Assert.That(throws, Is.EqualTo(1));
            Assert.That(sawFlight, Is.True);

            snack = new CityPortForemanSnackTimeline(); life = 0d;
            snack.Advance(life, false);
            AdvanceUntil(CityPortForemanSnackPhase.Bite1, 1.3d);
            snack.Advance(life += .1d, true);
            Assert.That(snack.Current.Phase, Is.EqualTo(CityPortForemanSnackPhase.Idle));
            Assert.That(snack.Current.BitesTaken, Is.Zero);
            for (int sample = 0; sample < 30; sample++) snack.Advance(life += .1d, true);
            Assert.That(snack.Current.BitesTaken, Is.Zero, "Talking cannot consume the interrupted bite.");
            AdvanceUntil(CityPortForemanSnackPhase.Bite1, 1.8d);
            Assert.That(snack.Current.BitesTaken, Is.EqualTo(1));
            snack.Advance(life += .1d, true);
            Assert.That(snack.Current.Phase, Is.EqualTo(CityPortForemanSnackPhase.Idle));
            Assert.That(snack.Current.BitesTaken, Is.EqualTo(1));
            AdvanceUntil(CityPortForemanSnackPhase.Bite2, 0d);
            Assert.That(snack.Current.BitesTaken, Is.EqualTo(1), "A committed bite is never retried.");
            AdvanceUntil(CityPortForemanSnackPhase.Discard, 1.2d);
            Assert.That(snack.Current.HoldingCarrot, Is.False);
            snack.Advance(life += 500d, false);
            Assert.That(snack.Current.Phase, Is.EqualTo(CityPortForemanSnackPhase.Idle));
            Assert.That(snack.Current.HoldingCarrot || snack.Current.StemInFlight, Is.False);
            Assert.That(snack.Current.BitesTaken, Is.EqualTo(3));
            Assert.That(snack.Current.CycleCount, Is.Zero, "A seek cannot take a skipped pocket carrot.");
            AdvanceUntil(CityPortForemanSnackPhase.Take, 1.3d);
            Assert.That(snack.Current.CycleCount, Is.EqualTo(1));
            snack.Advance(life += 500d, false);
            Assert.That(snack.Current.CycleCount, Is.EqualTo(1));
            Assert.That(snack.Current.BitesTaken, Is.Zero);
            Assert.That(snack.Current.HoldingCarrot, Is.True);

            void AdvanceUntil(CityPortForemanSnackPhase phase, double seconds)
            {
                double end = life + 24d;
                while (snack.Current.Phase != phase || snack.Current.ActionSeconds < seconds)
                {
                    Assert.That(life, Is.LessThan(end));
                    snack.Advance(life += .1d, false);
                }
            }
        }
    }
}
