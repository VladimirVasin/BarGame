# Structured session diagnostics

`debug.log`:context/actions/pending operations/Unity warnings/exceptions.
`DuelJournal`: CombatTest rounds.

## General log: location and profiles

| Runtime | Default | Location |
| --- | --- | --- |
| Editor | `verbose` | repository root, `debug.log` |
| Development/Release Player | `verbose`/`basic` | `Application.persistentDataPath/Logs/debug.log` |
| Batch/command-line tests | `off` | no file |

`-bp-debug-log off|basic|verbose`: state/results; verbose adds timings/build sizes.
`F8`:`diagnostics/snapshot`+CombatTest mark;`Shift+F8`:CombatLogs or general log directory.

## General log format and boundaries

UTF-8 NDJSON:
`schema_version`, `utc`, `mono_ms`, `seq`, `level`, `category`, `event`,
`session_id`, `scene`, `city_seed`, typed `data`. Order:utc/mono/seq;
Join:operation_id=transition/sequence=balance/snapshot_id=support.
Snapshots:hunger/stress/fatigue/intoxication/cash/drinking.

| Category | Boundaries/results |
| --- | --- |
| `session` | start/end/seed/bar/return/drinking;resolved drink purchases/cash before-after |
| `needs`, `inventory` | visible hunger/fatigue progression;explicit hunger/stress/fatigue;committed alcohol relief;atomic item-use results |
| `scene` | loaded/ready;transition requested/rejected/fallback/completed/failed;`composition_frames`:frames/path(area/door)/`frame_budget_ms`/`AdvanceFrame` CPU-wall;door adds frames/`duration_ms` incl.build. |
| `city`, `bar`, `mountain_road`, `alpine_village`, `home`, `stairwell`, `supermarket`, `church`, `mothers_house` | world/layout/bar/spawn/`initialize_phase`;first `cemetery_first_grave_sealed`/`cemetery_raven_{spawned,provider_missing,plot_missing}`;outdoor `raven_roost_{spawned,provider_missing}` |
| `city`, `mountain_road`, `alpine_village` (verbose) | `world_build_block` (`seacoast/*`, `roads_and_river/east_exit*` sub-rows), `terrain_mesh` (`sample_ms`/`cook_ms`/`primed`), `terrain_grid`, `water_surface`, `cloth_panels`, `world_inventory`, `cannery_phase`; `city_plans_prime` started/finished/joined when an interior New Game primes City plans on a pool thread |
| `primitive` (verbose) | `combined_mesh`: source count/vertices/combine/collider time |
| `interaction`, `map` | entrance/exit, map lifecycle, City test-teleport mode/results |
| `intoxication`, `balance` | stages, scheduling/start/result/fall/recovery/cancellation |
| `combat` | Cached exit/quit `movement_summary`: WASD/request/app-focus-neutral gates,motor/input/capsule/phase/scale/speed/turn;request/min-scale require `has_request_sample=true`;off disables. |
| `diagnostics` | manual snapshots/directory commands |
| `unity` | warnings/assertions/errors/exceptions and stacks |

`session/new_game_started`: `menu_reset`=menu; `combat_test_start`=range;
`new_game_start`=normal/legacy; `new_game_start_rejected`=transition rollback.

No frame/motion/animation/physics samples or `Debug.Log`.
Strings≤16,384; `needs/passive_progressed`: visible integers.
Unity repeats:3 full,then10s summaries.
Per10s:32 warnings/64 errors-assertions-exceptions;
`messages_rate_limited`: drops/severity; exceptions retain their own budget.

## General log retention and reporting

Rotate 5MiB; keep `debug.1.log`–`debug.3.log`. Flush: error/.5s/F8/pause/focus/exit.
Fresh session→fault→F8→Shift+F8;collect archives/round;read error/`operation_id`.
No intended usernames/save paths/frame telemetry;stacks may expose paths;review before sharing.

## DuelJournal: CombatTest rounds

`-bp-duel-log on|off`:Editor/Player on,batch off.
Editor:CombatLogs;Player:`Application.persistentDataPath/CombatLogs`.
`duel_<session>_<round>`: `summary.txt`/`duel.ndjson`.

Melee:`MeleeCommandRejection`→reason/`reason_checked`/cost/remaining/`cooldown_seconds`/phase/stamina.
`recovery_step`: rescue; `fall_committed`: refusal; `observed_counter`: whiff.
20Hz pose/support/CPU.
Pistol:`pistol_shot_{requested,rejected,pose}`/`pistol_fired`:request/action IDs.
Reject:reason/stage(input/commit/cancel)/phase/aim_error_degrees;pose:muzzle/target/reachable.
20Hz`pistol_state`:ammo/aim/reload/body;`vectors`:pistol_{muzzle_and_target,forward_and_support_error}.
`movement_input`:changes;keys=W1/S2/A4/D8,stick_x/y,effective x/y,
focused=app/awaiting_neutral/target_focused=enemy.
`vectors`/`movement_drive`:a=walk request,b=balance drift.
`aim_locked`:kind/yaw/open s.
`support_pose_rejected`: `contact_*`/depth/sweep/shape.
`weapon_constraint`:cause/depth.
Crowbar arm/grip `_snapshot`:wrist/elbow/shoulder_roll/elbow_signed;fresh/cached.
No solver:no arm/grip;`grip=None`/`two_hand_support=false`.
Guard:`balance_recovery`;attacks≠regrip.
End/focus clears input;`opponent_style` header.
`phase`: `action_kind`/kick `outcome`/`from_action`; `impact_kind`: impact_seq+kind.
`kick_support`: reason/gap/wait/support+strike side.
`kick_surface_contact`: sole/toe<=64.
`kick_sweep_sample`: boot/hit/world; `weapon_blocked`: metal stop/point.
`recovery`: steps/gaps/stability; `ragdoll_snapshot`: speed/settle/support≠landing.
`rise_clearance_*`: blocker/path/capsule/floor;change/1s.
`suspected_stall`: living no progress>2s.
`rise_escape_started`→`rise_weapon_released`;`weapon_recovered`:equipped.
`post_round_time_discarded`:aftermath;snapshots to exit/reset.
Impact/applied/geometry/anatomy/impulse:source→victim;`impact_seq`;
kind=weapon/kick/shove/projectile;region/side/critical/finisher/pre-post phase/power.
Pre-hit phase:nullable.
`revision_identity`:Runtime/Rules MVID≠Editor commit/state;bank hash/play
(Player GUID);missing=null/`unavailable`;no replay/FPS guarantee.

`frame_detail`:hero LateUpdate=`late_pose_ms`;impact=`impact_apply_ms`;
`update_to_late_ms`:Update→LateStart.
`latest_target_wait_ms`=Unity;`latest_{present_wait,cpu_main,cpu_render}_ms`=FrameTiming.
`latest_timing_repeat_frames`:-1 unavailable/0 new/>0 repeats≠age.
`pose_work`:Present/Weapon/Support CPU/calls/candidates/budget,incl.outside Tick.
`frame_delivery/late_to_next_update_ms`:LateStart→Update wall;render/editor/waits≠CPU/GPU.
Game-camera SRP:`render_context_captured`/`late_to_render_begin_ms`/
`render_context_span_ms`/`render_end_to_next_update_ms`/`render_contexts`;
same frame;submission≠GPU finish;absent=null.
`weapon_constraint_sample`:candidates/sweeps/world/`anatomy_queries`/reuse/core/shoulder/budget.
`frame_stage_sample`:capture/flight/effects/aim/proxy/recovery CPU;aftermath.
`{damage,blood}_stage_sample`/`body_sync_sample`:impact/wound/sync CPU.
Stages:prior-frame deltas;nested≠sum;unsupported=null;main/render/GPU:latest.

Editor CombatTest:`Tools/Bar Promenade/Diagnostics/Capture Combat CPU Timeline (15 seconds)`.
Trace+manifest/`Time.frameCount`:`TestResults/Test duel diagnostics`.
Timeout/scene/Play/reload restores Profiler;active capture refuses;overhead.

Producer:2048/4096 default/max packets+8 control slots;reused chars,no string/field copies.
Drops/I/O/limits explicit;≤2 live/stalled workers;extra=`worker_limit`.
20MiB/round/10 closed/100MiB;prune ordinary before marked/abandoned;never live/foreign.
Bounded marks;one CombatLogs/no sweep. Cloth/hair cached;120Hz/4 steps.

## Area performance capture

Area frame samples:

```text
-bp-perf-scene=City -bp-perf-label=1080p-walk -bp-perf-warmup=5 -bp-perf-seconds=30 -bp-perf-target-fps=60
```

Waits for scene/transition;keeps position/render.
Editor: `Tools > Bar Promenade > Diagnostics > Capture Performance (30 seconds)`.
API: `RuntimePerformanceCapture.StartCapture(options, outputDirectory)`.

Default:`Application.persistentDataPath/PerformanceCaptures`;8 JSON/≤36,000 samples.
Capture1–120s/warmup≤60s/wait≤300s;scene/render changes end:reason.

Reports:render/pacing/hardware/world;frame p50/p95/p99/max/main/render/GC/foot-bake/reflection.
Markers:`BarPromenade.FootSoleBake`/`BarPromenade.WaterReflectionCube`.
GPU:needs Frame Timing Stats,unchanged.`sampleCount=0`:unavailable.
Frames include waits/threads/pacing;Editor≠player.Compare same route/render;keep pause/focus counts.
