using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace BarPromenade.Tests.PlayMode
{
    public sealed class LastRouteRideSpeechPlayModeTests
    {
        [UnityTest]
        public IEnumerator RoadSpeech_PreservesSilenceAndBagAcrossLegsWithoutTakingThePrompt()
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
                        if (RussianSpeechPhonetics.HasRussianSpeech(view.FullText))
                            yield return WaitForSynthesizedClips(view.Bubbles, 0f, 1);
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
                if (RussianSpeechPhonetics.HasRussianSpeech(view.FullText))
                    yield return WaitForSynthesizedClips(view.Bubbles, 0f, 1);
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

        [UnityTest]
        public IEnumerator SharedVocalizer_UsesSpeakerIdentityClockAndPooledCleanup()
        {
            const string phrase = "Маршрут отменили. Я всё равно жду.";
            const float window = NpcSpeechBubbleView.VisibleSeconds;
            var host = new GameObject("Shared vocalizer contract");
            bool hadVoiceService = NpcSpeechVoice.Instance != null;
            GameTimeScaleRuntime previousTempo = Object.FindAnyObjectByType<GameTimeScaleRuntime>();
            int previousIntoxication = GameSessionState.IntoxicationLevel;
            float previousPresentation = GameTimeScaleRuntime.SmoothedPresentationLevel;
            DrinkId previousDrink = GameSessionState.LastAlcoholicDrink;
            int previousDrinks = GameSessionState.DrinksConsumed;
            try
            {
                SetSpeechIntoxication(0);
                var cameraHost = new GameObject("Speech camera and listener");
                cameraHost.transform.SetParent(host.transform, false);
                var camera = cameraHost.AddComponent<Camera>();
                camera.enabled = false;
                if (Object.FindAnyObjectByType<AudioListener>() == null) cameraHost.AddComponent<AudioListener>();
                var npc = new GameObject("NPC head");
                npc.transform.SetParent(host.transform, false);
                npc.transform.position = Vector3.forward;
                var hero = new GameObject("Hero head");
                hero.transform.SetParent(host.transform, false);
                var woman = new GameObject("Female NPC head");
                woman.transform.SetParent(host.transform, false);
                woman.transform.position = Vector3.left;
                var view = host.AddComponent<NpcSpeechBubbleView>();
                view.UseManualClock = true;
                view.Initialize(camera, cameraHost.transform);
                var npcSpeaker = new NpcSpeaker(npc, npc.transform, NpcVoiceCatalog.FerrymanDesignId,
                    NpcEarshotProfile.Conversation);
                var heroSpeaker = new NpcSpeaker(hero, hero.transform, NpcVoiceCatalog.HeroMutterDesignId,
                    NpcEarshotProfile.Conversation);
                var femaleSpeaker = new NpcSpeaker(woman, woman.transform, "mother_v1",
                    NpcEarshotProfile.Conversation);
                Assert.That(view.DeclareSpeaker(npcSpeaker), Is.True);
                Assert.That(view.DeclareSpeaker(heroSpeaker), Is.True);
                Assert.That(view.DeclareSpeaker(femaleSpeaker), Is.True);
                Assert.That(npcSpeaker.Voice.VocalizerVoice.Gender, Is.EqualTo(RussianVocalizerGender.Male));
                Assert.That(heroSpeaker.Voice.VocalizerVoice.Gender, Is.EqualTo(RussianVocalizerGender.Male));
                Assert.That(femaleSpeaker.Voice.VocalizerVoice.Gender, Is.EqualTo(RussianVocalizerGender.Female));
                NpcSpeechVoice service = NpcSpeechVoice.EnsureInstalled();
                int originalLeases = service.LeasedCount;
                int originalClips = service.ActiveSynthesizedClipCount;

                Assert.That(view.ShowAt(npc, phrase, 0f, window), Is.True);
                Assert.That(view.ShowAt(hero, phrase, 0f, window), Is.True);
                Assert.That(view.RevealedTextOf(npc), Is.Empty, "PCM preparation must not advance the words.");
                yield return WaitForSynthesizedClips(view, 0f, originalClips + 2);
                AudioSource npcSource = FindSource(service, "Speech_" + npcSpeaker.Voice.Id);
                AudioSource heroSource = FindSource(service, "Speech_" + heroSpeaker.Voice.Id);
                Assert.That(heroSource, Is.Not.SameAs(npcSource));
                float npcDuration = npcSource.clip.length;
                float heroDuration = heroSource.clip.length;
                Assert.That(npcDuration, Is.GreaterThan(0f).And.LessThanOrEqualTo(
                    window - NpcSpeechVoice.SynthesisWindowReserveSeconds + 0.01f));
                Assert.That(heroDuration, Is.GreaterThan(0f).And.LessThanOrEqualTo(
                    window - NpcSpeechVoice.SynthesisWindowReserveSeconds + 0.01f));
                Assert.That(npcSource.spatialBlend, Is.EqualTo(1f));
                Assert.That(npcSource.outputAudioMixerGroup, Is.SameAs(GameAudioMixer.SfxWorldGroup));
                Assert.That(npcSource.pitch, Is.EqualTo(1f), "Window fitting must preserve the speaker's pitch.");
                Assert.That(npcSource.GetComponent<AudioLowPassFilter>().cutoffFrequency,
                    Is.EqualTo(NpcSpeechVoice.VocalizerLowPassFrequencyHz).Within(0.01f));
                float[] npcPrefix = ReadPrefix(npcSource.clip);
                Assert.That(ReadPrefix(heroSource.clip), Is.Not.EqualTo(npcPrefix),
                    "The hero and the ferryman must retain different authored voices.");
                Assert.That(view.ShowAt(woman, phrase, 0f, window), Is.True);
                yield return WaitForSynthesizedClips(view, 0f, originalClips + 3);
                AudioSource femaleSource = FindSource(service, "Speech_" + femaleSpeaker.Voice.Id);
                Assert.That(femaleSource, Is.Not.SameAs(npcSource));
                Assert.That(ReadPrefix(femaleSource.clip), Is.Not.EqualTo(npcPrefix),
                    "A female character uses her assigned female preset on the shared playback path.");

                RussianVocalizerPlan npcPlan = RussianVocalizerSynthesis.Prepare(phrase,
                    npcSpeaker.Voice.VocalizerVoice, window - NpcSpeechVoice.SynthesisWindowReserveSeconds);
                for (float pause = .05f; pause < npcDuration * .5f - .03f; pause += .01f)
                {
                    if (npcPlan.IsSpeakingAt(pause) || npcPlan.IsSpeakingAt(pause + .02f)) continue;
                    view.AdvanceTo(pause);
                    Assert.That(view.TryGetSpeechFaceSample(npc, out SpeechFaceSample pauseFace), Is.True);
                    Assert.That(pauseFace.IsTyping, Is.False, "A real phonetic pause closes the shared mouth.");
                    Assert.That(view.RevealedTextOf(npc).Length,
                        Is.EqualTo(npcPlan.RevealedCharactersAt(pause)));
                    break;
                }
                float clock = npcDuration * 0.5f;
                view.AdvanceTo(clock);
                Assert.That(view.RevealedTextOf(npc).Length,
                    Is.EqualTo(npcPlan.RevealedCharactersAt(clock)),
                    "The reveal follows actual phone timing, including pauses and held vowels.");
                Assert.That(view.TryGetSpeechFaceSample(npc, out SpeechFaceSample face), Is.True);
                Assert.That(face.RevealedCharacters, Is.EqualTo(view.RevealedTextOf(npc).Length));
                Assert.That(face.ElapsedSeconds, Is.EqualTo(clock).Within(0.0001d));
                Assert.That(face.HasPhoneticTiming, Is.True);
                Assert.That(face.IsTyping, Is.EqualTo(npcPlan.IsSpeakingAt(clock)));

                npc.transform.position = Vector3.right * 8f;
                clock += 0.02f;
                view.AdvanceTo(clock);
                Assert.That(npcSource.transform.position, Is.EqualTo(npc.transform.position));
                Assert.That(view.OpacityOf(npc), Is.InRange(0.35f, 0.99f));
                Assert.That(npcSource.volume,
                    Is.EqualTo(npcSpeaker.Voice.Volume * view.OpacityOf(npc)).Within(0.0001f));
                AudioSource[] perceptionSources = { npcSource, heroSource, femaleSource };
                AudioClip[] perceptionClips = { npcSource.clip, heroSource.clip, femaleSource.clip };
                float[] soberVolumes = { npcSource.volume, heroSource.volume, femaleSource.volume };
                float[] previousVolumes = (float[])soberVolumes.Clone();
                float previousCutoff = NpcSpeechVoice.VocalizerLowPassFrequencyHz;
                float speechOpacity = view.OpacityOf(npc);
                int previousRevealed = view.RevealedTextOf(npc).Length;
                int previousSample = npcSource.timeSamples;
                int sourceCount = service.SourceCount;
                foreach (int level in new[] { 0, 20, 50, 80, 100 })
                {
                    SetSpeechIntoxication(level);
                    clock += 0.01f;
                    view.AdvanceTo(clock);
                    for (int voice = 0; voice < perceptionSources.Length; voice++)
                    {
                        AudioSource source = perceptionSources[voice];
                        float cutoff = source.GetComponent<AudioLowPassFilter>().cutoffFrequency;
                        Assert.That(source.clip, Is.SameAs(perceptionClips[voice]),
                            "Intoxication must process the playing clip without synthesizing it again.");
                        Assert.That(source.pitch, Is.EqualTo(GameTimeScaleRuntime.PerceptionPlaybackRate));
                        if (level > 0)
                        {
                            Assert.That(source.volume, Is.LessThan(previousVolumes[voice]),
                                "Male, female and hero speech must recede progressively.");
                            Assert.That(cutoff, Is.LessThan(previousCutoff));
                        }
                        if (level == 100)
                        {
                            Assert.That(source.volume / soberVolumes[voice], Is.InRange(.49f, .51f),
                                "Peak intoxication retains an audible voice instead of fading it away.");
                            Assert.That(source.pitch, Is.EqualTo(.25f).Within(.0001f));
                            Assert.That(cutoff, Is.EqualTo(1800f).Within(0.01f));
                        }
                        previousVolumes[voice] = source.volume;
                    }
                    previousCutoff = npcSource.GetComponent<AudioLowPassFilter>().cutoffFrequency;
                    Assert.That(npcSource.timeSamples, Is.GreaterThanOrEqualTo(previousSample),
                        "A perception change must not rewind the existing phone timeline.");
                    Assert.That(view.RevealedTextOf(npc).Length, Is.GreaterThanOrEqualTo(previousRevealed));
                    Assert.That(view.OpacityOf(npc), Is.EqualTo(speechOpacity).Within(0.0001f),
                        "Perceptual recession must retain the speaker's physical earshot.");
                    Assert.That(npcSource.transform.position, Is.EqualTo(npc.transform.position));
                    Assert.That(service.SourceCount, Is.EqualTo(sourceCount));
                    Assert.That(service.ActiveSynthesizedClipCount, Is.EqualTo(originalClips + 3));
                    Assert.That(service.RunningSynthesisCount, Is.Zero);
                    previousSample = npcSource.timeSamples;
                    previousRevealed = view.RevealedTextOf(npc).Length;
                }
                Assert.That(ReadPrefix(npcSource.clip), Is.EqualTo(npcPrefix),
                    "The listener effect must leave the authored PCM identity intact.");
                int revealed = view.RevealedTextOf(npc).Length;
                Assert.That(view.TryGetSpeechFaceSample(npc, out SpeechFaceSample beforePauseFace), Is.True);
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    SetSpeechIntoxication(0);
                    yield return null;
                    for (int voice = 0; voice < perceptionSources.Length; voice++)
                    {
                        Assert.That(perceptionSources[voice].volume,
                            Is.EqualTo(soberVolumes[voice]).Within(0.0001f),
                            "LateUpdate must restore an existing source even without another speech onset.");
                        Assert.That(perceptionSources[voice].GetComponent<AudioLowPassFilter>().cutoffFrequency,
                            Is.EqualTo(NpcSpeechVoice.VocalizerLowPassFrequencyHz).Within(0.01f));
                        Assert.That(perceptionSources[voice].clip, Is.SameAs(perceptionClips[voice]));
                        Assert.That(perceptionSources[voice].pitch, Is.EqualTo(1f));
                    }
                    view.AdvanceTo(clock);
                    Assert.That(npcSource.isPlaying, Is.False);
                    int pausedSample = npcSource.timeSamples;
                    yield return null;
                    view.AdvanceTo(clock);
                    Assert.That(npcSource.timeSamples, Is.EqualTo(pausedSample));
                    Assert.That(view.RevealedTextOf(npc).Length, Is.EqualTo(revealed));
                    Assert.That(view.TryGetSpeechFaceSample(npc, out SpeechFaceSample pausedFace), Is.True);
                    Assert.That(pausedFace.ElapsedSeconds, Is.EqualTo(beforePauseFace.ElapsedSeconds));
                }
                clock += 0.02f;
                view.AdvanceTo(clock);
                Assert.That(npcSource.isPlaying, Is.True);
                view.AdvanceTo(clock);
                Assert.That(npcSource.isPlaying, Is.False, "A repeated manual clock pauses the source.");
                clock += 0.02f;
                view.AdvanceTo(clock);
                revealed = view.RevealedTextOf(npc).Length;
                view.RenderEnabled = false;
                view.AdvanceTo(clock + 1f);
                yield return null;
                yield return null;
                Assert.That(npcSource.isPlaying, Is.False, "A hidden view supplies no playback heartbeat.");
                Assert.That(view.RevealedTextOf(npc).Length, Is.EqualTo(revealed));
                view.RenderEnabled = true;
                clock += 0.02f;
                view.AdvanceTo(clock);
                Assert.That(npcSource.isPlaying, Is.True);

                clock = Mathf.Max(npcDuration, heroDuration) + 0.02f;
                view.AdvanceTo(clock);
                Assert.That(npcSource.isPlaying, Is.False);
                npc.transform.position += Vector3.right;
                view.AdvanceTo(clock + 0.02f);
                Assert.That(npcSource.transform.position, Is.EqualTo(npc.transform.position),
                    "The pooled source follows its speaker through the reading tail.");
                Assert.That(npcSource.volume,
                    Is.EqualTo(npcSpeaker.Voice.Volume * view.OpacityOf(npc)).Within(0.0001f));
                cameraHost.transform.position = Vector3.right * 100f;
                view.AdvanceTo(clock + 0.04f);
                Assert.That(npcSource.clip, Is.Null);
                Assert.That(service.ActiveSynthesizedClipCount, Is.EqualTo(originalClips));
                cameraHost.transform.position = Vector3.zero;
                view.AdvanceTo(clock + 0.06f);
                Assert.That(view.OpacityOf(npc), Is.GreaterThan(0f));
                Assert.That(npcSource.clip, Is.Null, "Range re-entry cannot replay this active line.");
                Assert.That(service.LeasedCount, Is.EqualTo(originalLeases));
                view.DismissAll();

                // The same pooled slot receives a new version before its old worker drains.
                SetSpeechIntoxication(100);
                Assert.That(view.ShowAt(npc, phrase, 10f, window), Is.True);
                Assert.That(service.RunningSynthesisCount, Is.InRange(1, NpcSpeechVoice.MaximumSynthesisWorkers));
                Assert.That(view.Dismiss(npc), Is.True);
                Assert.That(view.ShowAt(hero, phrase, 10f, window), Is.True);
                yield return WaitForSynthesizedClips(view, 10f, originalClips + 1);
                Assert.That(FindSource(service, "Speech_" + heroSpeaker.Voice.Id), Is.SameAs(npcSource),
                    "The released NPC slot may be reused without accepting the old NPC result.");
                Assert.That(npcSource.GetComponent<AudioLowPassFilter>().cutoffFrequency,
                    Is.EqualTo(1800f).Within(0.01f));
                Assert.That(npcSource.volume / (heroSpeaker.Voice.Volume * view.OpacityOf(hero)),
                    Is.InRange(.49f, .51f));
                Assert.That(npcSource.pitch, Is.EqualTo(.25f).Within(.0001f));
                int slowStartSample = npcSource.timeSamples;
                float slowStarted = Time.realtimeSinceStartup;
                while (Time.realtimeSinceStartup - slowStarted < .4f)
                {
                    view.AdvanceTo(10f + Time.realtimeSinceStartup - slowStarted);
                    yield return null;
                }
                float slowWallTime = Time.realtimeSinceStartup - slowStarted;
                float delivered = (npcSource.timeSamples - slowStartSample) / (float)npcSource.clip.frequency;
                Assert.That(delivered, Is.GreaterThan(.01f).And.LessThan(slowWallTime * .5f),
                    "The source must really advance slowly; owner time must not seek past the stretched phones.");
                Assert.That(view.TryGetSpeechFaceSample(hero, out SpeechFaceSample slowFace), Is.True);
                Assert.That(slowFace.ElapsedSeconds, Is.LessThan(slowWallTime * .5f));
                int slowRevealed = view.RevealedTextOf(hero).Length;
                view.AdvanceTo(10f + window + .01f);
                Assert.That(view.IsShowing(hero), Is.False,
                    "A slowed perceptual fragment retains its finite authored window.");
                Assert.That(slowRevealed, Is.LessThan(phrase.Length),
                    "The missing fragment must not be dumped into the caption at its deadline.");
                Assert.That(view.Dismiss(hero), Is.False);
                yield return WaitForSynthesisWorkers(service);
                Assert.That(npcSource.clip, Is.Null);
                Assert.That(service.ActiveSynthesizedClipCount, Is.EqualTo(originalClips));
                Assert.That(service.LeasedCount, Is.EqualTo(originalLeases));

                GameSessionState.UpdateDrinkingProgress(0, previousDrink, previousDrinks);
                GameTimeScaleRuntime.ResetSession();
                Assert.That(view.ShowAt(npc, phrase, 20f, window), Is.True);
                yield return WaitForSynthesizedClips(view, 20f, originalClips + 1);
                Assert.That(ReadPrefix(FindSource(service, "Speech_" + npcSpeaker.Voice.Id).clip), Is.EqualTo(npcPrefix),
                    "Reusing a lease must retain the ferryman's deterministic voice identity.");
                Assert.That(npcSource.GetComponent<AudioLowPassFilter>().cutoffFrequency,
                    Is.EqualTo(NpcSpeechVoice.VocalizerLowPassFrequencyHz).Within(0.01f));
                Assert.That(npcSource.volume,
                    Is.EqualTo(npcSpeaker.Voice.Volume * view.OpacityOf(npc)).Within(0.0001f),
                    "A reset and reused slot must not retain the preceding drunk speaker's attenuation.");
                view.enabled = false;
                Assert.That(view.IsShowing(npc), Is.False);
                Assert.That(service.ActiveSynthesizedClipCount, Is.EqualTo(originalClips));
                Assert.That(service.LeasedCount, Is.EqualTo(originalLeases));
                view.enabled = true;

                var prompt = host.AddComponent<InteractionPromptView>();
                float now = Time.unscaledTime;
                Assert.That(prompt.ShowFeedbackAt("interaction.radio_on", 1f, now), Is.True);
                Assert.That(prompt.GetDisplayedTextAt(now), Is.EqualTo(LocalizationService.Get("interaction.radio_on")));
                Assert.That(prompt.IsSpeaking, Is.False);
                Assert.That(service.LeasedCount, Is.EqualTo(originalLeases));
                Assert.That(service.ActiveSynthesizedClipCount, Is.EqualTo(originalClips));
                prompt.ClearFeedback();
                Assert.That(view.ShowAt(npc, "The route is closed.", 30f, window), Is.True);
                view.AdvanceTo(30.2f);
                AudioSource blipSource = FindSource(service, "NpcSpeechBlip_" + npcSpeaker.Voice.Id);
                Assert.That(blipSource.GetComponent<AudioLowPassFilter>().cutoffFrequency,
                    Is.EqualTo(NpcSpeechVoice.LowPassFrequencyHz).Within(0.01f));
                AudioClip blipClip = blipSource.clip;
                float blipPitch = blipSource.pitch;
                float soberBlipVolume = blipSource.volume;
                SetSpeechIntoxication(100);
                yield return null;
                Assert.That(blipSource.clip, Is.SameAs(blipClip));
                Assert.That(blipSource.pitch, Is.EqualTo(blipPitch * .25f).Within(.0001f));
                Assert.That(blipSource.volume / soberBlipVolume, Is.InRange(.49f, .51f));
                Assert.That(blipSource.GetComponent<AudioLowPassFilter>().cutoffFrequency,
                    Is.EqualTo(1800f).Within(0.01f),
                    "Fallback clicks must receive the same listener effect between character onsets.");
                SetSpeechIntoxication(0);
                view.AdvanceTo(30.4f);
                Assert.That(blipSource.GetComponent<AudioLowPassFilter>().cutoffFrequency,
                    Is.EqualTo(NpcSpeechVoice.LowPassFrequencyHz).Within(0.01f));
                Assert.That(blipSource.volume, Is.EqualTo(soberBlipVolume).Within(0.0001f));
                Assert.That(service.ActiveSynthesizedClipCount, Is.EqualTo(originalClips));
                Assert.That(service.RunningSynthesisCount, Is.Zero);
                view.DismissAll();

                // Automatic views must move their epochs while global pause holds raw time.
                var autoHost = new GameObject("Automatic speech owner");
                autoHost.transform.SetParent(host.transform, false);
                var automatic = autoHost.AddComponent<NpcSpeechBubbleView>();
                automatic.Initialize(camera, cameraHost.transform);
                Assert.That(automatic.DeclareSpeaker(autoHost, autoHost.transform,
                    NpcVoiceCatalog.FerrymanDesignId, NpcEarshotProfile.Conversation), Is.True);
                yield return null;
                Assert.That(automatic.ShowAt(autoHost, "The road is quiet.", Time.unscaledTime, 0.2f), Is.True);
                using (GameTimeScaleRuntime.AcquirePause())
                {
                    automatic.RenderEnabled = false;
                    yield return new WaitForSecondsRealtime(0.3f);
                    Assert.That(automatic.IsShowing(autoHost), Is.True);
                    Assert.That(automatic.RevealedTextOf(autoHost), Is.Empty);
                }
                automatic.RenderEnabled = true;
                yield return null;
                Assert.That(automatic.IsShowing(autoHost), Is.True,
                    "Paused wall time cannot expire an automatic line on resume.");
                automatic.DismissAll();
            }
            finally
            {
                Object.DestroyImmediate(host);
                GameSessionState.UpdateDrinkingProgress(previousIntoxication, previousDrink, previousDrinks);
                if (previousTempo != null)
                    GameTimeScaleRuntime.SetIntoxicationLevel(previousPresentation);
                if (!hadVoiceService && NpcSpeechVoice.Instance != null)
                    Object.DestroyImmediate(NpcSpeechVoice.Instance.gameObject);
                if (previousTempo == null)
                {
                    GameTimeScaleRuntime tempo = Object.FindAnyObjectByType<GameTimeScaleRuntime>();
                    if (tempo != null) Object.DestroyImmediate(tempo.gameObject);
                }
            }
        }

        private static void SetSpeechIntoxication(int level)
        {
            GameSessionState.UpdateDrinkingProgress(level,
                GameSessionState.LastAlcoholicDrink, GameSessionState.DrinksConsumed);
            GameTimeScaleRuntime.SetIntoxicationLevel(level);
        }

        private static IEnumerator WaitForSynthesizedClips(NpcSpeechBubbleView view, float clock, int minimumClips)
        {
            NpcSpeechVoice service = NpcSpeechVoice.Instance;
            Assert.That(service, Is.Not.Null);
            float deadline = Time.realtimeSinceStartup + 10f;
            while (service.ActiveSynthesizedClipCount < minimumClips)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Bounded speech preparation timed out.");
                Assert.That(service.RunningSynthesisCount, Is.LessThanOrEqualTo(NpcSpeechVoice.MaximumSynthesisWorkers));
                view.AdvanceTo(clock);
                yield return null;
            }
            view.AdvanceTo(clock);
        }

        private static IEnumerator WaitForSynthesisWorkers(NpcSpeechVoice service)
        {
            float deadline = Time.realtimeSinceStartup + 10f;
            while (service.RunningSynthesisCount > 0)
            {
                Assert.That(Time.realtimeSinceStartup, Is.LessThan(deadline), "Cancelled speech workers did not drain.");
                Assert.That(service.RunningSynthesisCount, Is.LessThanOrEqualTo(NpcSpeechVoice.MaximumSynthesisWorkers));
                yield return null;
            }
        }

        private static AudioSource FindSource(NpcSpeechVoice service, string clipName)
        {
            foreach (AudioSource source in service.GetComponentsInChildren<AudioSource>())
                if (source.clip != null && source.clip.name == clipName) return source;
            Assert.Fail("No pooled speech source has clip " + clipName);
            return null;
        }

        private static float[] ReadPrefix(AudioClip clip)
        {
            var samples = new float[Mathf.Min(256, clip.samples)];
            Assert.That(clip.GetData(samples, 0), Is.True);
            return samples;
        }
    }
}
