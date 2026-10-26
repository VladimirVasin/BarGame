# Mandatory speech presentation standard

Accepted `2026-09-11`; Russian vocalizer rollout explicitly accepted
`2026-10-24`. Applies to spoken E replies, ambient exchanges, dialogue and hero
mutters. Story §§6/16/21 and the art bible govern permission, text and appearance.
No new pool, speaker, lore, gesture or interaction is granted.

## One presentation

Every spoken line uses `NpcSpeechBubbleView` above its actual speaker, including
`ShowSpokenFeedback`. Bottom `InteractionPromptView` descriptions, hints, choice
labels and pages are silent. Only an approved graph node speaks a chosen reply;
never duplicate speech below.

Keep retro canvas/measurement/font/geometry/head/earshot fade/cull. Dialogue
panels/tails/choices use 72% opacity; text stays crisp (`2026-10-24`).
Missing speakers cancel delivery; no invented anchor or bottom fallback.

## Shared owners

- `NpcSpeechBubbleView` owns active lines, immutable captured windows and cleanup;
  scenes/channels may own shared views.
- `SpeechDelivery` owns reveal/duration. Russian PCM, text and mouths share one
  immutable phone/sample timeline. Pauses hold text/close mouths; held final vowels
  retain articulation. Window fitting reduces pauses/drawl before uniform timing,
  without pitch shifts.
  English fallback retains `24` chars/s and clicks at least `.13 s` apart.
  English reading tail defaults to `2.5 s`; Russian uses the remaining window.
  Ambient window defaults to `4.4 s`; authored windows stay. Preparation holds
  reveal; overdue/unready results cancel rather than replay or overrun the window.
- `NpcSpeaker` supplies the actual head, declared voice and earshot profile.
  Explicit male/female `NpcVoiceCatalog` pools retain speaker identity; shared
  models use permanent character IDs. The hero remains exclusive.
- `NpcSpeechVoice` owns pooled spatial playback and bounded cancellable background
  synthesis. Unity clips stay on the main thread. Bound workers, pending requests
  and retained audio; no sentence PCM cache. Staged prefabs gain no source.
  Results belong to live line tokens; late completion never revives a closed line.
  Smoothed intoxication slows RU/EN speech, lowers gain/cutoff and scatters
  wrapped captions. Text/mouth follow slowed PCM inside the same finite window.
  Music shares the rate/VHS; sobriety restores. Menu auditions stay dry.
- Controllers own selection/order/admission, not another renderer or clock.
  Register once; release lines, declarations, jobs and leases on cancellation,
  cull, disable, destruction and scene exit.
- `SpeechFaceAnimation` reads shared reveal; punctuation/tail close mouths,
  brows/blink stay independent. Pause freezes faces/sound; termination restores.
  `SpeechFaceAtlasPresenter` remains the shared presenter.

## Ordering and lifetime

One owner arbitrates ambient/pairs/E: one live head/view. Typing/reading cannot
be replaced; defer/reject before consuming a shuffle entry. Keep pair order/no-repeat.
Owned silence cancels bubbles/voice/jobs and blocks admission until release.
No replay after restoration/pause/distance exit/seek.

Use shared duration/active state for busy gates, replies, gestures and completion.
Measure whole-line fit within authored windows. Pause freezes the owning clock,
sound and gestures without catch-up, including manual clocks that stop advancing.
Loss of speaker/range/scene/ownership leaves no bubble, voice, late result,
delayed reply or blocked input. Silent feedback keeps its own semantics.

## Prohibited copies and verification

No feature-specific panels, typewriters, synthesizers, voice pools, earshot logic
or copies of timing constants. Extend shared owners. Deviations need an explicit
user decision in `architecture-notes.md`; new content grants no exception.

Use one focused existing check for routing, fit, identity/earshot, order, pause
and teardown as relevant. Inspect a frame if appearance changes. Update this
standard when its contract changes; run `python tools/check-docs.py`.
