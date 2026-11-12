using System.Collections.Generic;
using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class RidgeKioskCourtLife
    {
        private static readonly (string First, string Reply)[] ExchangePool = BuildExchangePool();
        private NpcSpeechBubbleView speech;
        private BarSpeechVariantDeck exchanges;
        private Visit firstSpeaker, replySpeaker;
        private bool firstHeard, replying;
        private int exchange, nextPair;
        private float speechClock, nextExchange, replyAt;

        private void InitializeSpeech(Camera camera)
        {
            speech = gameObject.AddComponent<NpcSpeechBubbleView>();
            speech.UseManualClock = true;
            speech.Initialize(camera, hero);
            exchanges = new BarSpeechVariantDeck(ExchangePool.Length, unchecked(GameSessionState.CitySeed ^ 0x13B8096));
        }

        private static (string First, string Reply)[] BuildExchangePool()
        {
            var pool = new List<(string, string)>(BarConversationCatalog.CounterCount + 6)
            {
                ("kiosk.court.rain.a", "kiosk.court.rain.b"), ("kiosk.court.bus.a", "kiosk.court.bus.b"),
                ("kiosk.court.work.a", "kiosk.court.work.b"), ("kiosk.court.table.a", "kiosk.court.table.b"),
                ("kiosk.court.pies.a", "kiosk.court.pies.b"), ("kiosk.court.coat.a", "kiosk.court.coat.b")
            };
            // Coffee at the window and sitting here would describe the wrong venue.
            const int coffeeKiosk = 28 - 1, sittingHere = 39 - 1;
            for (int index = 0; index < BarConversationCatalog.CounterCount; index++)
            {
                if (index == coffeeKiosk || index == sittingHere) continue;
                var pair = BarConversationCatalog.Get(BarConversationKind.Counter, index);
                pool.Add((pair.FirstKey, pair.SecondKey));
            }
            return pool.ToArray();
        }

        internal void AdvanceSpeech(float step, int day, double minute)
        {
            if (!speech || step <= 0f) return;
            speechClock += step;
            if (firstSpeaker != null && (!Ready(firstSpeaker) || !Ready(replySpeaker))) CancelExchange();
            speech.AdvanceTo(speechClock);
            if (firstSpeaker != null)
            {
                if (replying)
                {
                    if (!speech.IsShowing(replySpeaker.Actor.Presentation)) CancelExchange();
                    return;
                }
                if (!firstHeard && speech.TryGetSpeechFaceSample(firstSpeaker.Actor.Presentation, out var sample) &&
                    sample.RevealedCharacters > 0)
                {
                    firstHeard = true;
                    exchanges.Commit(exchange);
                }
                if (speech.IsShowing(firstSpeaker.Actor.Presentation)) return;
                if (!firstHeard) { CancelExchange(); return; }
                if (replyAt < 0f) replyAt = speechClock + (float)BarConversationSchedule.ReplyMinimumGapSeconds;
                if (speechClock < replyAt) return;
                replying = BeginLine(replySpeaker, ExchangePool[exchange].Reply);
                if (!replying) CancelExchange();
                return;
            }

            if (speechClock < nextExchange || minute >= CityResidentLifeRules.KioskCourtEmptyMinute) return;
            double now = day * 1440d + minute;
            nextExchange = speechClock + 1.5f;
            for (int retry = 0; retry < visits.Length; retry++)
            {
                int start = nextPair++ % visits.Length;
                int table = start / RidgeKioskCourtPlan.PlacesPerTable * RidgeKioskCourtPlan.PlacesPerTable;
                Visit first = visits[start];
                Visit reply = visits[table + (start - table + 1) % RidgeKioskCourtPlan.PlacesPerTable];
                if (!Ready(first) || !Ready(reply) || first.Until - now < 3d || reply.Until - now < 3d) continue;
                exchange = exchanges.Peek();
                if (!BeginLine(first, ExchangePool[exchange].First)) continue;
                firstSpeaker = first;
                replySpeaker = reply;
                replyAt = -1f;
                first.Drinking.SetConversationPartner(reply.Head);
                reply.Drinking.SetConversationPartner(first.Head);
                return;
            }
        }

        private bool Ready(Visit visit)
        {
            if (visit == null || visit == aisleTrip || !visit.IsAtTable || !visit.Drinking || !visit.Drinking.CanConverse || visit.Sips <= 0) return false;
            float radius = NpcEarshotProfile.ConversationFaintRadiusMeters;
            return (visit.Head.position - hero.position).sqrMagnitude <= radius * radius;
        }

        private bool BeginLine(Visit visit, string key)
        {
            var owner = visit.Actor.Presentation;
            if (NpcSpeechBubbleView.IsPresentingAt(visit.Head)) return false;
            if (!speech.DeclareSpeaker(owner, visit.Head, owner.Registry.DesignId, NpcEarshotProfile.Conversation)) return false;
            string line = LocalizationService.Get(key);
            float duration = Mathf.Max(NpcSpeechBubbleView.VisibleSeconds,
                SpeechDelivery.ResolveSpokenDuration(line, SpeechDelivery.ReadingTailSeconds));
            if (speech.ShowAt(owner, line, speechClock, duration)) return true;
            speech.WithdrawSpeaker(owner);
            return false;
        }

        private bool SpeakingWith(Visit visit) => firstSpeaker == visit || replySpeaker == visit;
        private void EndSpeechFor(Visit visit) { if (SpeakingWith(visit)) CancelExchange(); }

        private void CancelExchange()
        {
            ReleaseSpeaker(firstSpeaker);
            ReleaseSpeaker(replySpeaker);
            firstSpeaker = replySpeaker = null;
            replying = firstHeard = false;
            nextExchange = speechClock + 8f;
        }

        private void ReleaseSpeaker(Visit visit)
        {
            if (visit == null) return;
            if (visit.Actor && visit.Actor.Presentation) speech.WithdrawSpeaker(visit.Actor.Presentation);
            if (visit.Drinking) visit.Drinking.SetConversationPartner(null);
        }
    }
}
