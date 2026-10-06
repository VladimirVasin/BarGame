# Structured session diagnostics

`debug.log`: context/actions/unfinished operations/Unity warnings/exceptions.
`DuelJournal`: CombatTest rounds.

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

Rotate 5MiB; keep `debug.1.log`–`debug.3.log`. Flush: error/.5s/F8/pause/focus/exit.
Fresh session→fault→F8→Shift+F8; collect archives/round; start at error/`operation_id`.
No intended usernames/save paths/frame telemetry. Exception stacks may contain
paths; review before public sharing.

## DuelJournal: CombatTest rounds

Editor/Player on, batch off/test opt-in; `-bp-duel-log on|off` independent.
Editor: repository CombatLogs; Player: `Application.persistentDataPath/CombatLogs`.
`duel_<session>_<round>`: `summary.txt`, then `duel.ndjson`.

`rules_rejected`: opaque Rules/`reason_checked=false`/phase/stamina/cost.
`balance_buffer`: .20s press; `observed_counter`: whiff reply. 20Hz pose/support/CPU.
`support_pose_rejected`: `contact_*`/weapon commit depth/sweep/shape.
`weapon_constraint`: cause survives rollback; remaining depth=rendered pose.
`arm_snapshot`: wrist/elbow/shoulder_roll/elbow_signed; metrics fresh/cached.
Guard: `two_hand_support`/`balance_recovery`; attacks ignore regrip.
End/focus cancels held/buffered. Header: `opponent_style`.
`phase`: `action_kind`/kick `outcome`/`from_action`; `impact_kind`: impact_seq+kind.
`kick_support`: reason/gap/wait; `kick_sweep_sample`: boot endpoints/hit/obstacle.
`recovery`: steps/gaps/stability; `ragdoll_snapshot`: speeds/settling/rise support
vs central landing. `suspected_stall`: >2s without phase/rise progress, except
defeated. `post_round_time_discarded`: aftermath loss; snapshots to exit/R.
`impact_anatomy`: `impact_seq`/region/side/critical/finisher/target phase before-after/
power; diagnostic pre-hit phase may be null.
`revision_identity`: compiled Runtime/Rules MVID≠Editor commit/state; bank
dependency hashes once/play (Player build GUID). Missing=null/`unavailable`;
no replay/FPS guarantee.

`frame_detail`: `late_pose_ms`=hero LateUpdate CPU, `impact_apply_ms`=impact;
`update_to_late_ms`=Update→observer LateStart, includes simulation/updates.
`latest_target_wait_ms`=Unity; `latest_{present_wait,cpu_main,cpu_render}_ms`
=FrameTiming. `latest_timing_repeat_frames`:-1 unavailable/0 new/>0 repeats≠age.
`pose_work`: Present/Weapon/Support CPU/calls, support candidates/budget; outside Tick.
`frame_delivery/late_to_next_update_ms`: LateStart→Update wall incl.
render/editor/scheduling/waits, not CPU/GPU cost.
`weapon_constraint_sample`: candidates/sweeps/queries/reuse/core/shoulder/budget;
world gates live.
Nested timings: never sum. Unsupported=null; main/render/GPU: latest.
Header: pacing/capture/wait.

Producer: 2048/4096 default/max packets + eight control slots; reused chars,
no strings/field copies. Drops/I/O/limits explicit; ≤2 live workers (stalled too),
extra=`worker_limit`. 20MiB/round/10 closed/100MiB; prune ordinary before marked/
abandoned, never live/foreign. Priority marks bounded; one CombatLogs/no sweep.
Cloth/hair matrix cache;120Hz/4 steps.

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
