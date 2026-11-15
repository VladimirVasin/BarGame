# Mandatory speech presentation standard

Accepted `2026-09-11`; Russian vocalizer `2026-10-24`. Covers ambient speech,
E replies, dialogue and mutters. Story §§6/16/21 and art canon govern scope.

## One presentation

Every spoken line uses `NpcSpeechBubbleView`. Ambient speech and
`ShowSpokenFeedback` project above the speaker. User decision `2026-11-14`:
`DialogueSessionController` uses centered bottom subtitles with the whole line.
The last question holds above the choices until selection. Descriptions,
hints and choices are silent. An approved graph node speaks the chosen reply.

Keep retro canvas/measurement/font/geometry/head/earshot fade/cull. Dialogue
panels/tails/choices use 72% opacity; text stays crisp (`2026-10-24`).
Missing speakers cancel delivery. Both layouts use the real speaker for voice
and facial timing.

## Shared owners

- `NpcSpeechBubbleView` owns active lines, immutable captured windows and cleanup;
  scenes/channels may own shared views.
- `SpeechDelivery` owns reveal/duration:immutable Russian phone/sample timeline
  for PCM/text/mouth. Pause holds text/closes mouths;final vowels articulate.
  Fit pauses/drawl then uniform timing,no pitch shift. EN:24chars/s,click gap≥.13s,
  default tail2.5s;RU default:remaining window. Optional captured full-text tail
  reserves shared2.5s+caller extra;pause shifts it. StepElapsed advances once/frame
  without epoch subtraction;PCM end=plan duration,not clip length.
  Ambient4.4s,authored windows kept.
  Prep holds reveal;late/unready cancels,no replay/window overrun.
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

Extend shared owners for panels, reveal, synthesis, pools, earshot and timing.
Deviations require a user decision in `architecture-notes.md`.

Use one focused existing check for routing, fit, identity/earshot, order, pause
and teardown as relevant. Inspect a frame if appearance changes. Update this
standard when its contract changes; run `python tools/check-docs.py`.
