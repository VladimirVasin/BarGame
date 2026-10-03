# Structured session diagnostics

`debug.log`: session context, actions, unfinished operations, Unity warnings/exceptions.
`DuelJournal`: CombatTest rounds. Optional area performance captures are separate.

## General log: location and profiles

| Runtime | Default | Location |
| --- | --- | --- |
| Editor | `verbose` | repository root, `debug.log` |
| Development/Release Player | `verbose`/`basic` | `Application.persistentDataPath/Logs/debug.log` |
| Batch/command-line tests | `off` | no file |

Use `-bp-debug-log` with `off`, `basic` or `verbose`. Basic records state/results;
verbose adds phase timings/build sizes. F8 writes and flushes a
`diagnostics/snapshot`; in CombatTest it also marks the duel. Shift+F8 opens
CombatLogs in CombatTest, otherwise the general log directory.

## General log format and boundaries

UTF-8 JSON line:
`schema_version`, `utc`, `mono_ms`, `seq`, `level`, `category`, `event`,
`session_id`, `scene`, `city_seed`, typed `data`. UTC/mono/seq order events;
session/scene/seed give context. Join by `operation_id` (transitions),
`sequence` (balance), `snapshot_id` (support). Snapshots: hunger/stress/fatigue,
intoxication/cash/drinking progress.

| Category | Boundaries/results |
| --- | --- |
| `session` | start/end, seed, active bar, return state, drinking mutations, resolved drink purchases/cash before-after |
| `needs`, `inventory` | visible hunger/fatigue progression, explicit hunger/stress/fatigue mutations, committed alcohol relief, atomic item-use results |
| `scene` | loaded/ready, transition requested/rejected/fallback/completed/failed; `composition_frames`: frames, `path` (area/door), `frame_budget_ms`, `AdvanceFrame` CPU/wall time. Door completion adds composition frames and `duration_ms` including build. |
| `city`, `bar`, `mountain_road`, `alpine_village`, `home`, `stairwell`, `supermarket`, `church`, `mothers_house` | world/layout summaries, bar placement, spawn choice, `initialize_phase`; first `cemetery_first_grave_sealed`, `cemetery_raven_{spawned,provider_missing,plot_missing}`, outdoor `raven_roost_{spawned,provider_missing}` |
| `city`, `mountain_road`, `alpine_village` (verbose) | `world_build_block` (`seacoast/*`, `roads_and_river/east_exit*` sub-rows), `terrain_mesh` (`sample_ms`/`cook_ms`/`primed`), `terrain_grid`, `water_surface`, `cloth_panels`, `world_inventory`, `cannery_phase`; `city_plans_prime` started/finished/joined when an interior New Game primes City plans on a pool thread |
| `primitive` (verbose) | `combined_mesh`: source count/vertices/combine/collider time |
| `interaction`, `map` | entrance/exit, map lifecycle, City test-teleport mode/results |
| `intoxication`, `balance` | stages, scheduling/start/result/fall/recovery/cancellation |
| `combat` | exit/quit `movement_summary`: W/S/A/D, requested/gated frames, motor/input/capsule gates, last phase, minimum scale, `maximum_requested_speed`, `requested_turn_changed`. Cached; off disables. Last-request fields/minimum scale require `has_request_sample=true` (`requestedSamples>0`). |
| `diagnostics` | manual snapshots, support-directory commands |
| `unity` | warnings/assertions/errors/exceptions and stacks |

`session/new_game_started` keeps its name. `reason`: `menu_reset` (menu entry),
`combat_test_start` (range), `new_game_start` (normal/legacy `BeginNewGame`),
`new_game_start_rejected` (rejected transition rollback).

The general log excludes frame updates, cursor/input motion, animation progress,
smoothed presentation, physics substeps and ordinary `Debug.Log`. Strings cap at
16,384 characters. `needs/passive_progressed` logs visible integer changes, not fractions.
Identical Unity messages emit three full records then sparse summaries within
a 10-second burst. Separate 10-second budgets then admit 32 warnings and
64 errors/assertions/exceptions; `messages_rate_limited` reports dropped counts
per severity, so warning storms cannot consume the exception budget.

## General log retention and reporting

Rotate at 5 MiB; keep `debug.1.log`–`debug.3.log`. Flush on error, else every
0.5 s/F8/pause/focus loss/clean shutdown. Fresh session→F8 at fault→Shift+F8;
collect log/archives and CombatTest round. Start at last error/`operation_id`.
No intentional usernames/save paths/frame telemetry. Unity exception stacks may
contain paths; review before public sharing.

## DuelJournal: CombatTest rounds

Editor/Player on, batch off/test opt-in; `-bp-duel-log on|off` independent.
Editor: repository CombatLogs; Player: `Application.persistentDataPath/CombatLogs`.
`duel_<session>_<round>`: `summary.txt`, then `duel.ndjson`.

`rules_rejected`: opaque Rules/`reason_checked=false`/phase/stamina/cost.
`balance_buffer`: .20s press; `observed_counter`: whiff reply. 20Hz root/bones/
grip/support/queries/CPU. `support_pose_rejected`: `contact_*`/weapon commit
depth/sweep/shape. `arm_snapshot`: wrist/elbow/shoulder_roll/elbow_signed.
`contact_metrics_current`: fresh/cached. Guard: `two_hand_support` (grip) or
`balance_recovery` (catch); attacks ignore regrip. End/focus cancels held/buffered.
`recovery`: steps/gaps/stability; `ragdoll_snapshot`: speeds/settling/live rise
support vs central landing. `suspected_stall`: >2s without phase/rise progress,
excluding defeated. `post_round_time_discarded`: aftermath loss; snapshots to exit/R.
`impact_anatomy`: links `impact_seq` to region/side/critical/finisher, target phase
before/after and power; diagnostic pre-hit phase may be null.
`revision_identity`: Runtime/Rules ModuleVersionIds; Editor git commit/clean-dirty
and bank dependency hashes once/play; Player build GUID for banks. Workspace
commit≠compiled code; missing=null/`unavailable`. No replay/FPS guarantee.

`frame_detail`: `late_pose_ms`=hero LateUpdate CPU, `impact_apply_ms`=impact,
`update_to_late_ms`=Update→observer LateStart (simulation/other updates included).
`latest_target_wait_ms`=Unity marker; `latest_{present_wait,cpu_main,cpu_render}_ms`
=latest FrameTiming; `latest_timing_repeat_frames`: -1 unavailable/0 new/>0 repeats,
not age. `pose_work`: per-actor Present/WeaponConstraint/SupportGrip CPU and calls,
including outside Tick. `frame_delivery/late_to_next_update_ms`: LateStart→next
Update wall: rendering/editor/scheduling/waits, not proof of CPU/GPU cost.
Nested timings overlap: never sum. Unsupported=null; main/render
Profiler/GPU: latest. Header: render interval/capture delta/target-wait.

Producer: 2048/4096 default/max packets/eight control slots; reused chars,
no strings/field copies. Explicit drops/I/O/limits; max two live workers
including stalled I/O, extra starts=`worker_limit`. 20MiB/round/ten closed/100MiB;
prune ordinary before marked/abandoned, never live/foreign. Bounded priority
marks; one CombatLogs/no sweep. Cloth/hair cache matrices; 120Hz/4 steps.

## Optional area performance capture

General log takes no frame samples. For an area capture:

```text
-bp-perf-scene=City -bp-perf-label=1080p-walk -bp-perf-warmup=5 -bp-perf-seconds=30 -bp-perf-target-fps=60
```

Capture waits for scene/transition end; no teleport/resolution/render changes.
Editor Play: `Tools > Bar Promenade > Diagnostics > Capture Performance (30 seconds)`.
API: `RuntimePerformanceCapture.StartCapture(options, outputDirectory)`.

Default: `Application.persistentDataPath/PerformanceCaptures`, eight JSON reports,
≤36,000 samples each; capture 1–120s/warmup≤60s/scene wait≤300s.
Scene/render changes end capture with a named reason.

Reports: resolution/quality/render scale/pacing/hardware/weather/time/intoxication,
p50/p95/p99/max frame intervals, main/render counters, GC, foot-bake/reflection
work. Markers: `BarPromenade.FootSoleBake`, `BarPromenade.WaterReflectionCube`.
GPU needs supported/enabled Frame Timing Stats; capture never enables it.
`sampleCount = 0` means unavailable, not free. Frames include pacing, threads
may include waits; Editor diagnostics are not player benchmarks. Compare the
same route/render context; retain pause/focus counts.
