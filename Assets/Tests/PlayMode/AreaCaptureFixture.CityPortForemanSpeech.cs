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
            yield return ValidateForemanDialogueReservation(camera, city, port, crew);
        }

        private static IEnumerator ValidateForemanDialogueReservation(Camera camera, CityGameRoot city,
            CityPortController port, CityPortCrew crew)
        {
            var foreman = crew.Foreman;
            var channel = crew.GetComponent<CityPortConversationController>();
            var hero = city.Player.Interactor;
            Vector3 savedHero = hero.transform.position;
            double savedPort = port.ElapsedSeconds, savedLife = crew.LifeElapsedSeconds;
            bool savedInput = hero.InputEnabled, savedMotor = city.Player.Motor.InputEnabled;
            double held = CityPortCycle.CycleDurationSeconds - .001d, life = savedLife;
            try
            {
                city.Player.Motor.SetInputEnabled(false);
                hero.SetInputEnabled(true);
                city.Player.Motor.Teleport(foreman.transform.position + foreman.transform.forward * 1.25f + Vector3.up * .04f);
                channel.Initialize(port, crew, camera, hero.transform, 1537);
                Sample(life);
                double limit = life + 420d;
                while (!(channel.Schedule.Current.IsSpeaking &&
                    channel.Schedule.Current.Exchange.Kind == CityPortConversationKind.Foreman &&
                    channel.Schedule.Current.SpeakerRole == CityPortConversationCatalog.ForemanRole))
                {
                    Assert.That(life, Is.LessThan(limit), "The real port channel must admit an ambient foreman pair.");
                    Sample(life + 1d);
                }
                CityPortConversationExchange exchange = channel.Schedule.Current.Exchange;
                bool began = false, replied = false;
                Assert.That(channel.RequestForemanInteraction(hero, () => began = true), Is.True);
                Assert.That(channel.RequestForemanInteraction(hero, null), Is.False);
                Assert.That(hero.InputEnabled, Is.True, "Reservation waits without locking the hero.");
                limit = life + 18d;
                while (!began)
                {
                    Assert.That(life, Is.LessThan(limit));
                    Sample(life + .1d);
                    var turn = channel.Schedule.Current;
                    if (!turn.HasExchange) continue;
                    Assert.That(turn.Exchange.Kind, Is.EqualTo(exchange.Kind));
                    Assert.That(turn.Exchange.Variant, Is.EqualTo(exchange.Variant));
                    replied |= turn.IsSpeaking && turn.SpeakerRole == exchange.SecondRole && turn.LineKey == exchange.SecondKey;
                }
                Assert.That(replied, Is.True, "The subordinate finishes the actual pending pair before dialogue owns the channel.");
                Assert.That(channel.ForemanInteractionReady, Is.True);
                Assert.That(channel.Bubbles.IsShowing(foreman), Is.False);
                channel.CancelForemanInteraction(hero);
                Assert.That(channel.ForemanInteractionPending, Is.False);
                Debug.Log("FOREMAN DIALOGUE: the ambient pair finishes before the single reserved interaction.");
            }
            finally
            {
                channel.CancelForemanInteraction();
                port.ApplyAt(savedPort, 15f); crew.ApplyAt(savedPort, savedLife); foreman.ApplyAt(savedLife);
                channel.Initialize(port, crew, camera, port.PresentationObserver);
                city.Player.Motor.Teleport(savedHero);
                hero.SetInputEnabled(savedInput); city.Player.Motor.SetInputEnabled(savedMotor);
            }
            yield return null;

            void Sample(double seconds)
            {
                life = seconds;
                port.ApplyAt(held, 15f); crew.ApplyAt(held, life); channel.ApplyAt(); foreman.ApplyAt(life);
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
