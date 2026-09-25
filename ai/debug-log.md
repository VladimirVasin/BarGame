# Structured session diagnostics

`debug.log` records session context, state-changing actions, unfinished operations
and Unity warnings/exceptions. The independent `DuelJournal` records CombatTest
rounds; optional area performance captures remain a third output.

## General log: location and profiles

| Runtime | Default | Location |
| --- | --- | --- |
| Editor | `verbose` | repository root, `debug.log` |
| Development Player | `verbose` | `Application.persistentDataPath/Logs/debug.log` |
| Release Player | `basic` | `Application.persistentDataPath/Logs/debug.log` |
| Batch/command-line tests | `off` | no file |

Use `-bp-debug-log` with `off`, `basic` or `verbose`. Basic records state/results;
verbose adds phase timings/build sizes. F8 writes and flushes a
`diagnostics/snapshot`; in CombatTest it also marks the duel. Shift+F8 opens
CombatLogs in CombatTest, otherwise the general log directory.

## General log format and boundaries

Each physical line is a UTF-8 JSON object. The stable envelope contains
`schema_version`, `utc`, `mono_ms`, `seq`, `level`, `category`, `event`,
`session_id`, `scene`, `city_seed` and typed `data`. UTC/monotonic time/sequence
establish order; session/scene/seed establish reproduction context. Correlate
transitions by `operation_id`, balance challenges by `sequence`, manual support
snapshots by `snapshot_id`. Snapshots include hunger/stress/fatigue alongside
intoxication, cash and drinking progress.

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
| `combat` | exit/quit `movement_summary`: W/S/A/D seen, requested/gated frames, motor/input/capsule gates, last phase, minimum movement scale, `maximum_requested_speed`, `requested_turn_changed`. Cached aggregates; profile off disables it. |
| `diagnostics` | manual snapshots, support-directory commands |
| `unity` | warnings/assertions/errors/exceptions and stacks |

The general log excludes frame updates, cursor/input motion, animation progress,
smoothed presentation, physics substeps and ordinary `Debug.Log`. Strings cap at
16,384 characters. `needs/passive_progressed` logs visible integer changes, not fractions.
Identical Unity messages emit three full records then sparse summaries within
a 10-second burst. Separate 10-second budgets then admit 32 warnings and
64 errors/assertions/exceptions; `messages_rate_limited` reports dropped counts
per severity, so warning storms cannot consume the exception budget.

## General log retention and reporting

Rotate at 5 MiB; keep `debug.1.log` through `debug.3.log`. Errors flush immediately;
otherwise every 0.5 seconds, plus F8, pause, focus loss and clean shutdown.
Reproduce in a fresh session, press F8 at the fault, open the folder with
Shift+F8 and collect the active log/archives. Start with the last error or
transition `operation_id`. In CombatTest collect its round folder as well.
No usernames/save paths/arbitrary per-frame telemetry are deliberately recorded
in this general log. Unity exception stacks may contain paths; review before
public sharing.

## DuelJournal: CombatTest rounds

Manual Editor and Player sessions enable the separate journal automatically.
Batch runs default off; a focused test must explicitly enable it, or launch with
`-bp-duel-log on`. `-bp-duel-log off` disables it independently of `debug.log`.

Editor output is the repository's CombatLogs directory; Player output is
`Application.persistentDataPath/CombatLogs`. Each round gets
`duel_<session>_<round>` containing `duel.ndjson` and `summary.txt`.

1. Enter CombatTest and reproduce the problem normally.
2. Press F8 when it occurs: this marks the duel and retains the ordinary
   diagnostic snapshot behavior.
3. Shift+F8 opens CombatLogs. Collect the matching round folder; start with
   `summary.txt`, then inspect the ordered records in `duel.ndjson`.

Outer gates name checked rejection reasons; Rules denials remain opaque:
`rules_rejected`, `reason_checked=false`, with phase/stamina/cost context.
Final-state snapshots sample root/selected bones, grip and support at 20 Hz;
impact links, physics-query counters and frame/CPU telemetry accompany them.
Available GPU timing is explicitly the latest sample. Code/animation revision
fields say `unavailable`. This is sampled evidence, not full-rig replay or an
FPS guarantee.

The fixed producer defaults to 2048 event packets, capped at 4096, plus eight
control slots. Serialization/writing is asynchronous. Drops, I/O failures
and reached limits are explicit rather than silently appearing as normal play.
At most two workers may live, including stalled I/O; further starts report `worker_limit`.
The journal caps each round at 20 MiB, retains at most ten completed rounds and
bounds the folder to 100 MiB. Cleanup removes old ordinary rounds before
marked/abandoned partials, never live leases or foreign contents. Marks give
priority, not permanent retention. Runtime owns caps/pruning; workspace permits
one CombatLogs directory without sweeping children.

## Optional area performance capture

The general support log takes no frame samples. For a bounded area capture:

```text
-bp-perf-scene=City -bp-perf-label=1080p-walk -bp-perf-warmup=5 -bp-perf-seconds=30 -bp-perf-target-fps=60
```

Capture waits for the named scene/transition end; it never teleports or changes
resolution/rendering features. Editor Play command:
`Tools > Bar Promenade > Diagnostics > Capture Performance (30 seconds)`.
`RuntimePerformanceCapture.StartCapture(options, outputDirectory)` permits an
explicit automation directory.

Default output is `Application.persistentDataPath/PerformanceCaptures`: eight
JSON reports maximum. Each holds up to 36,000 samples, captures 1–120 seconds
after up to 60 seconds warmup, and waits at most 300 seconds for its scene.
Scene/render-context changes end it with a named reason.

Reports include resolution/quality/render scale/pacing/hardware, weather/time/
intoxication, p50/p95/p99/max frame intervals, main/render-thread counters,
GC bytes and foot-bake/reflection work. Profiler markers:
`BarPromenade.FootSoleBake`, `BarPromenade.WaterReflectionCube`. GPU timing needs
supported, enabled Frame Timing Stats; capture never enables it. A metric with
`sampleCount = 0` is unavailable, not free. Frame intervals include pacing;
thread counters may include waits. Editor measurements are diagnostic, not
player benchmarks. Compare the same route/rendering context and retain
pause/focus counts when interpreting results.
