# Structured session diagnostics

`debug.log` is a bounded, low-frequency record of one playable session. It is
intended to answer four questions quickly:

1. Which build, scene and deterministic seed reproduced the issue?
2. Which state-changing actions led to it?
3. Did a transition or modal interaction start but fail to reach a terminal event?
4. What warning or exception did Unity report at that point?

## Location and profiles

| Runtime | Default profile | Location |
| --- | --- | --- |
| Unity Editor | `verbose` | repository root, `debug.log` |
| Development Player | `verbose` | `Application.persistentDataPath/Logs/debug.log` |
| Release Player | `basic` | `Application.persistentDataPath/Logs/debug.log` |
| Batch mode and command-line test runs | `off` | no file |

Override the profile with
`-bp-debug-log off`, `-bp-debug-log basic`, or
`-bp-debug-log verbose`. `basic` records state and result events;
`verbose` additionally records phase timings and rebuilt map paths.

Press `F8` in either gameplay scene to write and immediately flush a
`diagnostics/snapshot` event. Press `Shift+F8` to open the directory containing
the current log.

## Format

Every physical line is one UTF-8 JSON object. A typical envelope is:

```json
{"schema_version":1,"utc":"2026-07-29T18:24:17.483Z","mono_ms":8123,"seq":7,"level":"info","category":"scene","event":"transition_requested","session_id":"...","scene":"City","city_seed":20260727,"data":{"operation_id":"transition-1","target_scene":"BarInterior"}}
```

The stable top-level fields make the file sortable and machine-readable:

- `utc`, `mono_ms` and `seq` establish wall-clock and exact session order;
- `session_id`, `scene` and `city_seed` establish reproduction context;
- `level`, `category` and `event` identify the record;
- `data` holds typed event-specific values.

The important correlation fields are:

- `operation_id` across a requested scene transition and its result;
- `sequence` for a scheduled balance challenge;
- `snapshot_id` for a manual support snapshot.

Manual snapshots include the current `hunger`, `stress` and `fatigue` beside
`intoxication`, cash and drinking progress.

## Recorded boundaries

| Category | Recorded events |
| --- | --- |
| `session` | start/end, seed, route, active bar, return state, drinking mutations and resolved drink purchases with cash before/after |
| `needs`, `inventory` | visible hunger/fatigue passive-progression boundaries, explicit hunger/stress/fatigue mutations, committed alcohol relief and atomic item-use results |
| `scene` | loaded/ready plus transition requested, rejected, fallback, completed or failed |
| `city`, `bar`, `mountain_road`, `alpine_village` | deterministic layout/world summaries, bar placement, spawn choice and initialization timings, the session's first sealed grave (`cemetery_first_grave_sealed`), the cemetery raven pair (`cemetery_raven_spawned`, `cemetery_raven_provider_missing`, `cemetery_raven_plot_missing`) and each outdoor scene's raven roosts (`raven_roost_spawned`, `raven_roost_provider_missing`) |
| `interaction`, `map` | entrance/exit results, map lifecycle and City test-teleport mode/result events; path rebuilds are verbose-only |
| `intoxication`, `balance` | stage changes and balance scheduling, start, result, fall, recovery or cancellation |
| `diagnostics` | manual snapshots and support-directory commands |
| `unity` | Unity warnings, assertions, errors and exceptions with stack traces |

Frame updates, cursor motion, animation progress, smoothed presentation,
continuous input, physics substeps and ordinary `Debug.Log` messages are not
copied. Oversized strings are truncated to 16,384 characters.
Passive needs progression records one `needs/passive_progressed` boundary event
only when a visible integer level changes; per-frame fractional steps are not
logged.
Identical Unity messages emit three full records and then sparse summaries
during a 10-second burst. After that suppression, separate 10-second budgets
admit at most 32 warning records and 64 error/assertion/exception records, so a
warning storm cannot hide a later exception. `messages_rate_limited` records
report how many additional messages were dropped from each severity bucket.

## Retention and support workflow

The active file rotates at 5 MiB. At most three archives are retained as
`debug.1.log`, `debug.2.log` and `debug.3.log`. Errors flush immediately;
other events flush every 0.5 seconds and also on `F8`, pause, focus loss and
clean shutdown.

For a useful report:

1. Start a fresh session and reproduce the problem.
2. Press `F8` as soon as the bad state is visible.
3. Press `Shift+F8` and collect `debug.log` plus any numbered archives.
4. Search first for `"level":"error"` or the last transition
   `operation_id`.

The logger deliberately avoids recording usernames, save paths or arbitrary
per-frame telemetry. A filesystem path can still appear inside a Unity-provided
exception stack trace, so logs should be reviewed before public sharing.

## Optional performance capture

Ordinary sessions collect no frame samples. To capture a bounded interval after
entering an area, launch with:

```text
-bp-perf-scene=City -bp-perf-label=1080p-walk -bp-perf-warmup=5 -bp-perf-seconds=30 -bp-perf-target-fps=60
```

The capture waits for the named scene and the end of its transition; it does
not teleport the hero, change resolution or enable rendering features. The
Editor Play command is `Tools > Bar Promenade > Diagnostics > Capture Performance
(30 seconds)`. `RuntimePerformanceCapture.StartCapture(options, outputDirectory)`
also accepts an explicit output directory for automation.

At most eight JSON reports are retained under `Application.persistentDataPath/
PerformanceCaptures` by default. A capture holds at most 36,000 samples, runs
for 1–120 seconds after up to 60 seconds of warmup, and waits at most 300 seconds
for its scene. A scene/render-context change ends it with a named reason.

Reports include actual resolution, quality/render scale, pacing, hardware,
weather, time and intoxication alongside p50/p95/p99/max frame intervals,
main/render-thread counters, GC bytes and measured foot-bake/reflection work.
Named Profiler markers are `BarPromenade.FootSoleBake` and
`BarPromenade.WaterReflectionCube`. GPU data requires supported, enabled Frame
Timing Stats; capture does not turn that setting on. A metric with
`sampleCount = 0` is unavailable, not free. Frame intervals include pacing;
thread counters can include waits. Editor results are diagnostic, not player
benchmarks. Compare the same route and actual rendering context, and retain
the report's pause/focus counts when interpreting a result.
