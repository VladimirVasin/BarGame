# Mandatory speech presentation standard

Accepted by the user on `2026-09-11`. Applies to all existing and future
spoken lines, including NPC replies to `E`, ambient exchanges and hero mutters.
Read before changing any speech or dialogue. Story-bible §§6/16/21 and the
art bible still govern permission to speak, text and appearance. This standard
grants no new pool, voice acting, lore, gesture or interaction.

## One presentation

Every spoken line uses `NpcSpeechBubbleView`, projected above its actual
speaker. An interaction reply uses the same presentation as dock dialogue.
The bottom `InteractionPromptView` contains silent descriptions, action hints
and choice labels. Only an explicitly approved graph node turns a silent choice
into the hero's next spoken line.
`ShowSpokenFeedback` is a compatibility entry into shared bubble delivery;
it must never render a second spoken copy in the bottom panel.

Keep the shared retro canvas, full-line measurement, font, bubble geometry,
head anchor and earshot fade/cull. Missing or destroyed speakers cancel or
reject delivery; never invent an anchor or fall back to bottom speech.

## Shared owners

- `NpcSpeechBubbleView` owns presentation, active-line lifetime and cleanup.
  Captured per-line duration is immutable; scenes/channels may own shared views.
- `SpeechDelivery` owns reveal, audible-letter pacing and duration calculation.
  Current defaults: `24` chars/s, clicks at least `.13 s` apart, adaptive
  reading tail `2.5 s`; the shared ambient bubble default is `4.4 s`.
- `NpcSpeaker` supplies the real head/voice anchor, declared voice and earshot
  profile. The line belongs to that speaker, not the camera or interaction target.
- `NpcSpeechVoice` owns pooled writing-click leases and spatial sound. Existing
  voice profiles stay with their speakers; staged NPC prefabs gain no audio source.
- The dialogue/channel controller owns selection, ordering and admission, not
  another renderer or reveal clock. Register speakers once and release owned
  lines/declarations/leases on cancellation, disable, destruction and scene exit.
- Faces read bubble reveal through `SpeechFaceAnimation`; no second clock.
  Punctuation/reading tail close mouths; brows/blink remain independent.
  Pause freezes all face
  motion; termination restores faces. Hero/foreman/cannery woman use `SpeechFaceAtlasPresenter`.

## Ordering and lifetime

One speaker/channel owner arbitrates ambient lines, paired replies and E.
One live head, one view. Silent restoration rebuilds without replay.
Typing/reading lines cannot be replaced: bounded admission defers or rejects
without consuming a shuffle entry. Preserve pair order/no-repeat; never
accumulate missed events after pause, distance exit or seek.

Use the shared resolved duration/active-line state for busy gates, reply delay,
gestures and completion. Existing authored windows may supply parameters to the
shared API; measure that the whole localized line fits. Pause must freeze the
owning speech clock and its coupled gestures/sound without a catch-up burst.
Loss of the speaker, range, scene or interaction ownership must leave no orphan
bubble, voice lease, delayed reply or blocked input. Silent feedback keeps its
own non-speaking UI semantics.

## Prohibited copies and verification

Do not add feature-specific speech panels, typewriters, letter audio, voice
pools, earshot logic or independent copies of lifetime/CPS/click constants.
Extend the shared owner and reuse its contracts. New dialogue content cannot
be an excuse to fork presentation. Any deviation requires an explicit user
decision recorded in `architecture-notes.md`; do not infer an exception.

Use one focused existing check for the changed contract: shared bubble routing,
RU/EN full-line fit, speaker/voice/earshot, admission/order, pause and teardown
as relevant. When appearance changes, inspect an affected rendered frame.
Update this standard when its contract changes and run `python tools/check-docs.py`.
