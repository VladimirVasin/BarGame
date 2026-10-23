# Session diagnostics

`debug.log`:context/actions/pending/Unity faults;`DuelJournal`:CombatTest rounds.

## Profiles

Editor:`verbose`→root `debug.log`;Player dev/release:`verbose`/`basic`→
`Application.persistentDataPath/Logs/debug.log`;batch:`off`,no file.

`-bp-debug-log off|basic|verbose`: state/results; verbose adds timings/build sizes.
F8:snapshot (CombatTest:legacy HUD);Shift+F8:log directory. API keeps duel marks.

## Format

NDJSON/UTF-8:schema_version/utc/mono_ms/seq/level/category/event/
session_id/scene/city_seed/data(typed). Order:utc/mono/seq;
Join:operation_id(transition)/sequence(balance)/snapshot_id(support).
Snapshot:hunger/stress/fatigue/intoxication/cash/drinking.

| Category | Events |
| --- | --- |
| `session` | start/end/seed/bar/return/drinking;drink purchases/cash before-after |
| `needs`, `inventory` | visible progression/explicit needs/committed alcohol-item use |
| `scene` | load/ready/transition;composition_frames:frames/path/budget/CPU-wall;door time incl.build |
| World | world/layout/spawn/initialize_phase;cemetery_first_grave_sealed/cemetery_raven_*;raven_roost_* provider/plot |
| Outdoor (verbose) | world_build_block/terrain mesh-cook/grid/water/cloth/inventory/cannery;city_plans_prime started/finished/joined:pool-thread City plans |
| `primitive` (verbose) | `combined_mesh`: source count/vertices/combine/collider time |
| `interaction`, `map` | entrance/exit/map lifecycle/City test-teleport mode-results |
| `intoxication`, `balance` | stages, scheduling/start/result/fall/recovery/cancellation |
| `combat` | Cached movement_summary before close/disable/exit:keys/request/focus/gates/motor/capsule/phase/scale/speed/turn;min-scale requires has_request_sample |
| `diagnostics` | snapshots/directory |
| `unity` | warnings/assertions/errors/exceptions + stacks |

session/new_game_started:menu_reset=menu;combat_test_start=range;
new_game_start=normal/legacy;new_game_start_rejected=transition rollback.

No per-frame/motion/physics samples or Debug.Log;strings≤16,384.
needs/passive_progressed:visible integers.
Unity repeats:3 full then10s summaries;per10s:32 warnings/64 errors-assertions-exceptions;
messages_rate_limited:drops/severity;exceptions separately budgeted.

## Retention

Rotate 5MiB; keep `debug.1.log`–`debug.3.log`. Flush: error/.5s/F8/pause/focus/exit.
Fault→F8→Shift+F8;collect archives/round;join by error/`operation_id`.
Exclude user/save paths/frames;review stacks before sharing.

## DuelJournal

-bp-duel-log on|off (default on,batch off).
Editor:CombatLogs;Player:persistentDataPath/CombatLogs;
duel_<session>_<round>:{summary.txt,duel.ndjson}.

Melee:MeleeCommandRejection→reason/reason_checked/cost/remaining/cooldown_seconds/phase/stamina.
recovery_step: rescue; fall_committed: refusal; observed_counter: whiff.
Pose/support/CPU/pistol:Standard20Hz/Endless10Hz;impact/F8 force.
*_shot_{requested,rejected,pose}/*_fired:request/action/reason/stage/phase/aim_error;
pose:muzzle/target/reachable;*_state:loaded/aim/reload/body;vectors:muzzle-target/forward-support.
ammunition_snapshot:both guns/rounds/reserve/cap/location/pending/progress/load_mask.
ammo_picked_up/pickup_rejected:accepted/reserve before-after/loaded/cap/reason.
reload_{rejected,interrupted},*_reload_{started,completed}:request/reserve/reason/resuming/masks.
weapon_carry:request/begin/transfer/complete/cancel/weapon/location/progress/stage/reserve.
input_gate:allowed/owner/focus/motor-interactor input;changes only.
movement_input:changes;keys=W1/S2/A4/D8,stick_x/y,effective x/y,
focused=app/awaiting_neutral/target_focused=enemy.
vectors/movement_drive:a=walk request,b=balance drift.
aim_locked:kind/yaw/open s.
support_pose_rejected: contact_*/depth/sweep/shape.
weapon_constraint:cause/depth.
Crowbar arm/grip _snapshot:wrist/elbow/shoulder_roll/elbow_signed;fresh/cached.
No solver:no arm/grip;grip=None/two_hand_support=false.
Guard:balance_recovery;attacks≠regrip.
End/focus clears input;opponent_style header.
phase: action_kind/kick outcome/from_action; impact_kind: impact_seq+kind.
kick_support: reason/gap/wait/support+strike side.
kick_surface_contact: sole/toe<=128.
kick_sweep_sample: boot/hit/world; weapon_blocked: metal stop/point.
recovery: steps/gaps/stability; ragdoll_snapshot: speed/settle/support≠landing.
rise_clearance_*: blocker/path/capsule/floor;change/1s.
suspected_stall: living no progress>2s.
rise_escape_started→rise_weapon_released;weapon_recovered:equipped.
post_round_time_discarded:aftermath;snapshots to exit/reset.
Impact events:source→victim;impact_seq;
kind=weapon/kick/shove/projectile;region/side/critical/finisher/pre-post phase/power.
Pre-hit phase:nullable.
revision_identity:compiled Runtime/Rules MVID≠workspace commit/state;bank hash/Player GUID;
unavailable=null;no replay/FPS guarantee.

CPU:frame.pose_ms=Present incl.Endless;frame_detail:late_pose_ms/impact_apply_ms;
update_to_late_ms=Update→cloth observer. pose_work:Present/Weapon/Support/calls/candidates/budget,0cloth.
latest_target_wait_ms=Unity;latest_{present_wait,cpu_main,cpu_render}_ms=FrameTiming;
latest_timing_repeat_frames:-1 n/a/0 new/>0 repeated≠age.
population_settings.actor_work_samples=active_or_called;
corpse_settings:maximum/total_pool/oldest_defeat_first;corpse_retired:generation/reason/count.
opponent_spawn:search/activation/reset ms/graph builds/corpses.
frame_delivery:late_to_next_update_ms wall≠CPU/GPU.
SRP:render_context_captured/late_to_render_begin_ms/render_context_span_ms/
render_end_to_next_update_ms/render_contexts;submit≠GPU finish;absent=null.
weapon_constraint_sample:candidates/sweeps/world/anatomy queries/reuse/core/shoulder/budget.
frame_stage_sample:capture/flight/effects/aim/proxy/recovery;
{damage,blood,head}_stage_sample/body_sync_sample:CPU;
projectile_defeat_stage_sample:total/preparation/ragdoll/drop.
cloth_{stage,budget}_sample:sling/jacket/hair/scarf CPU/calls/steps/iterations/discard/baked_calls.
secondary_motion_cache:decode/load/eviction/residency;part/reason counts in snapshots.
Prior frames;nested≠sum;unsupported=null;GPU latest.

Editor:`Tools/Bar Promenade/Diagnostics/Capture Combat CPU Timeline (15 seconds)`:
trace/manifest;timeout/scene/Play/reload restores Profiler;refuses while active;overhead.
Rendered Editor explicit:`Range_EndlessWarehouseRetainsRenderedPerformanceEvidence`.

Queue2048/max4096+8controls;reused chars;envelope=v1.
v3:absent_numeric_zero damage/sync/blood/head/defeat:lossless measured frames only;
thinned/limited gaps unknown.
20MiB incl.summary/footer;70%telemetry/causal reserve.
Telemetry:55%detail/30%essential/15%forced;adaptive stride1/4/16/64.
Forced70%routine/20%F8-stall/10%result-end.
Samples:priority/group/group_stride/reason;detail/essential_sampled_out_records≠loss;
{telemetry,causal,producer}_dropped_records/limit flags;first-last omitted seq.
file_limit:telemetry_budget|round_budget;causal survives sample cap.
32-action tail≠solver;summary:original envelopes≤half budget/clipped strings.
Flush live/end final/reset clear;≤2workers/I/O explicit;10closed/100MiB;
ordinary→marked/abandoned;never live/foreign;bounded marks/no sweep.

## Performance capture

CLI:`-bp-perf-scene=City -bp-perf-seconds=30`.

Waits scene/transition;pose/render kept. API:`RuntimePerformanceCapture.StartCapture`.
Editor:`Tools > Bar Promenade > Diagnostics > Capture Performance (30 seconds)`.

Output:`Application.persistentDataPath/PerformanceCaptures`;8 JSON/≤36,000 samples.
Capture1–120s/warmup≤60s/wait≤300s;scene/render change ends with reason.

Reports:context + frame/CPU/GPU/GC/foot-bake/reflection quantiles/max;
markers:`BarPromenade.FootSoleBake`/`BarPromenade.WaterReflectionCube`.
GPU:Frame Timing Stats;sampleCount=0 unavailable. Frames include waits;
Editor≠player;match route/render/pause/focus.
