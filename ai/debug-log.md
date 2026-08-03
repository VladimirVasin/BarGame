# Structured session diagnostics

`debug.log` is a bounded, low-frequency record of one playable session. It is
intended to answer four questions quickly:

1. Which build, scene and deterministic seed reproduced the issue?
2. Which state-changing actions led to it?
3. Did a transition or minigame start but fail to reach a terminal event?
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
- `minigame_run_id` across one opening, actions and terminal result;
- `sequence` for a scheduled balance challenge;
- `snapshot_id` for a manual support snapshot.

Manual snapshots include the current `hunger` and `stress` beside
`intoxication`, cash and drinking progress.

## Recorded boundaries

| Category | Recorded events |
| --- | --- |
| `session` | start/end, seed, route, visited bars, active bar, return state, drinking mutations and resolved drink purchases with cash before/after |
| `needs`, `inventory` | hunger/stress mutations, committed alcohol relief and atomic item-use results |
| `scene` | loaded/ready plus transition requested, rejected, fallback, completed or failed |
| `city`, `bar` | deterministic layout/world summaries, bar placement, spawn choice and initialization timings |
| `interaction`, `map` | entrance/exit results and map lifecycle; path rebuilds are verbose-only |
| minigame categories | opening, bounded player actions, committed round/throw/move results, cancellation and completion |
| `intoxication`, `balance` | stage changes and balance scheduling, start, result, fall, recovery or cancellation |
| `diagnostics` | manual snapshots and support-directory commands |
| `unity` | Unity warnings, assertions, errors and exceptions with stack traces |

Frame updates, cursor motion, animation progress, smoothed presentation,
continuous input, physics substeps and ordinary `Debug.Log` messages are not
copied. Oversized strings are truncated to 16,384 characters.
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
4. Search first for `"level":"error"`, the last transition `operation_id`, or
   the last minigame `minigame_run_id`.

The logger deliberately avoids recording usernames, save paths or arbitrary
per-frame telemetry. A filesystem path can still appear inside a Unity-provided
exception stack trace, so logs should be reviewed before public sharing.
