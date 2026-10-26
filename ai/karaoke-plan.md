# Free karaoke MVP

Accepted 2026-10-26: free wordless singing, hall attention, real drunk pitch drift,
dynamic shots and rare WC inserts. Story §6/art §12 bound local exceptions in the
ordinary warm bar. No score/lyrics/judgement/applause/new speech/relationship/story.

## Voice and pitch

The hero's registered voice/shared source-filter DSP sustains one vowel. Held
LMB gates sound; mouse Y immediately controls a continuous 110–440Hz base pitch.
Current-block DSP avoids prefetch; 6ms glide, phase/filters/attack/release and
fixed gain stay continuous.
Bounded interpolation resamples the whole voice for perception rate/formants;
shared spatial audio, perception and VHS apply once.

Intoxication 0–100 smoothly increases deterministic drift to ±1.5 semitones,
exactly zero sober. Mouse corrects it immediately. One effective source frequency
drives DSP and the fixed 96-logical-pixel panel's marker/four-second trace;
silence makes trace gaps. Semitone marks/envelope stay crisp. This is audible
drift, not cosmetic motion; source pitch precedes perception/VHS rather than
measuring final mixed frequency.

## Performance and hall

The first admitted LMB vowel begins performance; approach/silent-ready alone do
not demand attention or suppress hall speech. A .9s breath grace keeps hall and
sequence stable between notes. Longer silence releases hall attention/silence;
a new vowel begins again.

Present hall guests/bartender fall silent and look at the singer. Shared speech
ownership cancels bubbles, voice, synthesis jobs and delayed replies together;
new admission waits, with no replay. Crowd murmur/reaction voices stop;
ventilation/object sounds stay. Actual membership/WC presence govern attention,
never through a closed door. Bounded head/upper-body look preserves anatomical
limits, seated roots/feet and service grips. Attendance, safe service, WC return
and closing retain their owners; no frozen crowd or added population.

## Interaction and camera

E starts the shared grounded side approach; KaraokeEnter/Loop/Exit use the same
production rig, independent neutral entry/exit data and fixed foot contacts.
The microphone stays fixed; shared face follows the gate. E requests authored
exit; Escape pauses. Entry/pause/focus restoration require releasing LMB first.
Music waits the whole session; closed business hours retain priority.

A bounded sequence changes shots every 5–8s: six hero angles, two close variants
for each present guest and bartender. Medium/profile/wide views combine a slow
push-in, lateral travel or pull-back. Each authored start/end path owns position,
FOV, roll and shared scoped DOF focus together; eligibility, endpoints and path
clearance are bounded. Skip invalid/stale/blocked shots; no per-frame scene search.
One shared camera, no extra render target; UI/mouse pitch stay fixed, singing HUD
visible on every frame including WC. Cuts never conceal mismatched endpoints.

WC inserts are much rarer: first eligible after 60 active performance seconds,
then a bounded 90–150s cooldown. They require the actual current visitor/action;
no actor/event is invented for a shot. Four variants: peeFace, poopFace,
peeBowl, poopBowl. Faces follow the current action; both inside-bowl views stay
STATIC while urine or stool lands toward the camera. PeeBowl smoothly randomizes
its seeded aim within the visible toilet bowl/water cone, with rim clearance;
never offscreen or outside the fixture. They reuse the Home toilet bank/effects,
existing water, temporary shadowless bowl fill (.17), scoped
underwater optics/audio (420Hz/−5dB plus water bed); each cut releases scope.
Held LMB keeps hero singing through BOTH bowl shots: the live
KaraokeVoice→SfxWorld→Perception route continues, only underwater-filtered.
Other shots move; bowls stay still. The actual NPC uses the FULL shared
HomeToiletSeatedTimeline/appearance/bowel: prepare/sit, 3s seated, emerge .5s/
release 1.05s, rise/inspect/flush/float/dress/close. Emission advances once with
that timeline, independent of camera; no replay or added hold for a shot.
Existing lower-body meshes/proxies bind to NPC bones/pigment/ToiletOutlet.
The selected longcoat hem rises physically with clothing progress, exposing
rear/outlet; low live hands follow knees. Restore outfit before hall. Same NPC,
no hidden actor/teleport/new identity; hero keeps note input/WC priority.

Pause/focus loss freezes drift, ends performance and releases hall ownership;
the current camera frame holds. Resume resets the base stand view and needs a
fresh click. It does not resume an abandoned shot path. Closing ends karaoke.
Exit/cancel/disable/destroy/unload idempotently release hall, camera/DOF, voice,
face, HUD, cursor, input and temporary WC-shot resources.

## Effects and acceptance

Scene-shaping keeps eight practicals/three shadows/four finite bounces, SSAO and
Neutral; no new GI/fog/volumetric pass. The warm stage practical stays before the
face with a 120° pool. Materials/accent retain probes, roughness/wear, bevels,
vertex response, dust/emission/outlines; existing toilet bowl/waste effects are
scoped to the actual action. Wind is inapplicable; no new SSR/SSGI/SCGI, rim,
decal or concert-light provider. Saved bloom/AA/vignette/grain/dither/flares/blur/
lens effects stay; shared DOF follows focus, with scoped reused underwater
render/audio and borrowed fill only for bowl inserts. Review faces, contacts, bowl
readability and crisp UI at maximal intoxication. Art §16's nine checks hold.
Work is bounded by present NPCs, shot slots and reused buffers/assets.

Singing_UsesContinuousHeroAndRestoresEveryOwner passed full karaoke/body/audio
lifecycle; SeatedAppearance_PreservesMixedJointSkinAtFabricEndpoints passed shared
fabric deformation. Reviewed visitor/WC HUD, visible rear/outlet, stool-water-lens,
coat lift, low hands and bounded urine aim. Existing synthesis/asset validators
retain buffer/rate/seam/neutral/foot proof. FAST: focused check, no build/full run.
