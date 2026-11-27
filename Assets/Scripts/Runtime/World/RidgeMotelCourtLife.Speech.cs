using UnityEngine;

namespace BarPromenade
{
    internal sealed partial class RidgeMotelCourtLife
    {
        private static readonly string[] Topics = { "opener", "chair", "rain", "shirt", "return", "bus" };
        private NpcSpeechBubbleView speech;
        private BarSpeechVariantDeck deck;
        private Guest speaker, listener;
        private float speechTime, nextExchange, replyAt;
        private bool replying, heard;
        private int topic, nextPair;
        internal int CompletedExchanges { get; private set; }

        private void Talk(float elapsed, bool seek)
        {
            if (!speech) return;
            speechTime += elapsed;
            speech.AdvanceTo(speechTime);
            if (seek) { nextExchange = speechTime + 2f; return; }
            if (speaker != null)
            {
                if (!CanHear(speaker) || !CanHear(listener)) { EndExchange(); return; }
                Guest talking = replying ? listener : speaker;
                if (speech.TryGetSpeechFaceSample(talking.Body, out var sample))
                {
                    talking.Face.TrySetSpeechFace(this, SpeechFaceAnimation.Resolve(sample, SpeechFaceProfile.Foreman, speechTime));
                    if (!replying && !heard && sample.RevealedCharacters > 0)
                    {
                        deck.Commit(topic);
                        heard = true;
                    }
                }
                if (speech.IsShowing(talking.Body)) return;
                talking.Face.ReleaseSpeechFace(this);
                if (replying || !heard)
                {
                    if (replying) CompletedExchanges++;
                    EndExchange();
                    return;
                }
                if (replyAt < 0) replyAt = speechTime + .85f;
                if (speechTime < replyAt) return;
                replying = ShowLine(listener, true);
                if (!replying) EndExchange();
                return;
            }
            if (speechTime < nextExchange) return;
            nextExchange = speechTime + 1.7f;
            for (int n = 0; n < guests.Length; n++)
            {
                int index = nextPair++ % guests.Length;
                Guest first = guests[index], second = guests[index ^ 1];
                if (!CanHear(first) || !CanHear(second) || !first.Pose.CanConverse || !second.Pose.CanConverse) continue;
                topic = deck.Peek();
                if (!ShowLine(first, false)) continue;
                speaker = first; listener = second;
                replying = heard = false; replyAt = -1f;
                first.Pose.SetConversationPartner(second.Character.Head);
                second.Pose.SetConversationPartner(first.Character.Head);
                break;
            }
        }

        private bool CanHear(Guest g) => g.Pose && g.Body.gameObject.activeInHierarchy &&
            (observer.position - g.Character.Head.position).sqrMagnitude <
            NpcEarshotProfile.ConversationFaintRadiusMeters * NpcEarshotProfile.ConversationFaintRadiusMeters;

        private bool ShowLine(Guest g, bool reply)
        {
            if (NpcSpeechBubbleView.IsPresentingAt(g.Character.Head) ||
                !speech.DeclareSpeaker(NpcSpeaker.FromRegistry(g.Body, g.Body.Registry, NpcEarshotProfile.Conversation))) return false;
            string text = LocalizationService.Get("motel.court." + Topics[topic] + (reply ? ".b" : ".a"));
            float duration = Mathf.Max(NpcSpeechBubbleView.VisibleSeconds,
                SpeechDelivery.ResolveSpokenDuration(text, SpeechDelivery.ReadingTailSeconds));
            if (speech.ShowAt(g.Body, text, speechTime, duration)) return true;
            speech.WithdrawSpeaker(g.Body);
            return false;
        }

        private void EndExchange()
        {
            Release(speaker); Release(listener);
            speaker = listener = null;
            nextExchange = speechTime + 12f + 2f * (nextPair % 4);
            void Release(Guest g)
            {
                if (g == null) return;
                if (speech && g.Body) speech.WithdrawSpeaker(g.Body);
                g.Face?.ReleaseSpeechFace(this);
                if (g.Pose) g.Pose.SetConversationPartner(null);
            }
        }
    }
}
