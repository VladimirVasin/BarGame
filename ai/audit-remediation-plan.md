# Исправления и оптимизации после аудита

Пользователь 28 октября 2026 года принял все двенадцать пакетов из плана после
[аудита версии 8c43e547](project-audit-2026-10-28.md). Включено изменение правила
банки; дата задания кота и отсутствие кормления/барьера в первый день сохранены.
Все пакеты получили итоговое решение и адресную приёмку. GPU/loading candidates
без убедительного выигрыша отклонены; это не полная регрессия или player build.

## Двенадцать пакетов

| № | Пакет | Реализация и приёмка |
| --- | --- | --- |
| 1 | Банка кота | Защита до Completed в session; скрытое Use до задания. Междневная регрессия и независимая пищевая арифметика. |
| 2 | Производные героя | NPC/damage, scarf и flashlight пересобраны штатно через staging. Geometry signatures/contacts прежние; HeroDerivedAssetSetup.RebuildBatch выполнил Unity rebind и read-only gate. |
| 3 | Build gate | StreetProfile read-only validator; явный weapon binding rebuild отделён от проверки. Негативные topology/contact случаи и отсутствие записи. |
| 4 | NonAlloc грузовика | Общий runtime/debug collector: удерживаемый рост и полный fallback. Saturation cases с игнорируемыми и реальными препятствиями. |
| 5 | Combat coverage | Имена убраны из SetUp admission; legacy/diagnostic маркированы явно. Ammo, carry journal и handoff возвращены в production coverage. |
| 6 | Метрики | Requested/actual targets и отказ до samples при mismatch; фиксированный offscreen harness. |
| 7 | City CPU и GC | По trace: owner-held audio geometry, сохранённые Transform рабочих точек и классификация света при регистрации. Повтор и parity/zero-GC checks пройдены. |
| 8 | Анимация и одежда | ScarfPerformance прошёл; повторного spatial rebuild нет. Carry oracle выявил кромку ремня в клапане M65; rig-only guides учитывают сечение ленты. Guards/solver сохранены. |
| 9 | GPU и эффекты | SSR, volumetrics и halation проверены по одному. Разница в шуме; качество сохранено. |
| 10 | Loading и память | Stage costs, retained exterior и owner plateau измерены. Cut-workspace candidate удалён: выигрыш в шуме. Бюджеты/геометрия сохранены. |
| 11 | Toolchain и provenance | Установленные clang/SDK проверяются ABI/DSP; source generations получают staged output; NPC damage публикуется с rollback. Semantic fingerprints меняются только при доказанном смысле. |
| 12 | Игровые доказательства и канон | Адресные сценарии реального мира, current docs и принятый cat exception; flares coverage исправлено. |

## Правила измерения и завершения

Функциональный пакет принимается одной адресной проверкой; у общего framework
может быть дополнительная узкая проверка. Зелёная selection не расширяется на
полные suites. XML totals и skipped сверяются с выбранными именами.

Оптимизация принимается по сопоставимому workload с фактическими dimensions,
seed/погодой/камерой/состоянием, CPU/GPU/GC/loading/memory и кадровой приёмкой,
если меняется представление. Editor evidence не считается Windows benchmark.
Кандидат без доказанного выигрыша удаляется. Отсутствующий эффект не добавляется
ради матрицы; все 24 категории и девять художественных критериев сохраняются.

## Измеренный результат

`CityIdlePortAndCannery_KeepComparableRenderAndCpuEvidence`: Unity Editor
6000.6.7f1, Apple M5/Metal, production camera → RT 1280×720, seed 20260727,
Clear, день 1 около 12:00, трезвый герой, без pause/focus потерь. Port window
после начался на игровую минуту раньше; clock продолжал идти. Экран имел
1710×854/852; он не выдаётся за размер рендера. Окна короткие, по две секунды;
профилирование выполнялось отдельно от таблицы. Между запусками отличаются
накопленные Editor caches; это локальное диагностическое сравнение.

| Workload | Frame p95, ms до → после | GC mean, B/frame до → после | GPU mean, ms до → после |
| --- | --- | --- | --- |
| city-idle | 31.63 → 22.20 | 46004 → 43044 | 14.41 → 17.17 |
| port-near | 29.79 → 25.83 | 44634 → 30329 | 13.55 → 12.67 |
| cannery-near | 27.96 → 19.69 | 37741 → 23924 | 13.24 → 12.75 |

У фабрики mean GC ниже на 36.6%; frame p95 лучше в этом запуске, но не является
гарантией FPS. GPU idle ухудшился: общего GPU ускорения не заявляем. Отдельный
trace ограничен 16000 строками, последний кадр может быть неполным. Ранее видимые
alloc scopes SoundscapeDirector, Surf, Cannery.Update/LateUpdate в повторе не
наблюдались. Доказательство причины дополнено zero-allocation/parity tests:
неизменные полигоны больше не строятся при каждом sound refresh; сохранённые
Transform продолжают давать живые позиции; moving/pooled lights продолжают
менять район. Глобального кэша и ослабления симуляции нет.

GPU A/B на той же cannery camera, mean ms off → on: SSR 12.70 → 12.83;
volumetrics 13.20 → 13.03; halation 13.06 → 12.91. Отключение providers не дало
убедительного выигрыша; все production настройки возвращены. Пересмотрены три
группы effects standard, включая post-processing: новые эффекты/свет/история не
добавлены. Городские кадры диагностические и местами перекрыты героем; они не
подменяют художественную приёмку всего города.

`ScarfPerformance`: production шарф в доме/деревне, inventory preview, бег,
повороты, остановка, pause/teleport. Whole scarf p95 0.123 ms в доме,
0.161 ms снаружи, 0.137/0.139 ms в беге/поворотах. Preview переиспользует модель,
камеру/texture, остаётся passive; pause drift 0; повторные world discovery,
collection и spatial build равны нулю. Текущий путь не запускал native solver
samples; это не benchmark неактивного физического backend. Движение хвоста и
кадры после остановки/телепорта просмотрены; affine/contact guards не менялись.

Восстановленный handoff case выявил реальное проникновение кромки ремня на
2.297 mm в `CLO_JacketFlapChest.L`. Клапан — отдельная жёсткая skinned-поверхность,
а не устаревший cloth output; oracle измерял ту же rig pose. `PresentKinematic`
обходил contact solve, а передние guides не учитывали сечение ribbon.
Только rig-only front guides получили envelope `HalfWidth + HalfThickness`
(13.5 mm); это геометрический запас, не подгонка порога теста. Крепления,
ладонь/плечо, physical/baked path и строгий visible-surface oracle сохранены.
Новых mesh bake, contact iterations, buffers или hot-loop allocations нет.

Тот же сценарий обнаружил jump кисти/оружия при disable в промежуточном palm/catch
hold. Родитель и ammo сохранялись, но `ReleaseOwnedClip → EndClip → EvaluateGraph`
немедленно переводил руку в locomotion. `CombatActor.OnDisable` теперь использует
существующий `EndClipForHandoff` до cleanup только при активном transient hold и
собственном clip owner. Вся очистка выполняется; нет нового долгоживущего lease,
общего изменения release API или перемещения одного оружия отдельно от кисти.

При двух прямых dormancy/resume циклах City root/mesh owners/materials/meshes/
clips остались 1/1507/431/5006/158; RT 69 → 68, native allocated ~1991 → 1990 MB.
Managed heap вырос ~12 MB без принудительного GC: это не доказательство утечки
или её отсутствия. `HomeVisits_KeepTheSameCityThroughTheStairwell_ThenTheBarAgain`
подтвердил настоящий retained exterior roundtrip. Cold-process scene-ready
16.76/15.69 s в разных Editor runs не считается улучшением loading.

`AreaTravel_ComposesEachDestinationBeforeCompleting` с verbose stage logging
прошёл MountainRoad → AlpineVillage → City: overlay закрывает незавершённый мир,
time/audio pause и progress/ready/cleanup сохранены. Первый City build в процессе,
seed 20260727: advance 15.439 s, wall 15.526 s, overhead 0.087 s; ground 4.605 s,
ground finalization 5.376 s. Основной расход — CPU stages, а не yield overhead.
Residential/industrial/beach sampling: 914/799/1059 ms. Finalization:
partition 1971 ms, conform 1590 ms; cooking отдельно от mesh preparation.

Проверен cut-workspace candidate без per-polygon копирования street cuts.
Границы и pool/main bit parity прошли четыре существующих terrain cases;
vertices/indices, 492986 triangles, 78 sectors и bucket counters совпали.
Однако ground 4.605 → 4.560 s, advance 15.439 → 15.339 s: около 1% при
изменении стороннего world-plans stage на 2.6% и ухудшении Yard sampling.
Убедительного выигрыша нет, candidate удалён. Budget 8/250 ms, render interval,
partition/conform и intentional dormant City не переписывались. Эти измерения
не означают холодный OS cache или доказанное ускорение загрузки.

## Именованные доказательства

- `HeroDerivedAssetSetup.RebuildBatch`: переимпорт, явные bindings и общий gate.
  Geometry signatures и face/lamp contacts сохранены; hash перестал быть stale.
- `PlayerBuildAssetValidationTests`, `PerformanceCaptureTests`, stew reservation,
  truck saturation, sound occlusion snapshot и moving light metadata: адресная
  EditMode selection, 53 passed, без скрытых skips.
- `SelectionAndExamine_UsePureMenuState`, `ClosedStewPurchase_RemovesProductAndPersistsAcrossReload`,
  `PerformanceCapture_RejectsMismatchedExplicitTargetBeforeRecording`,
  `Ride_OnlyLeavesTheAreaOnceTheScreenIsBlack` и retained City roundtrip прошли.
- `Scene_FeedingConsumesOneStewAndCompletesPairedAnimation` прошёл с новой
  session reservation. `MountainRoadCityTunnel` проверил фактическую капсулу
  от тоннеля до терминала и обратно: XZ progress, Below, walkable area, уклон,
  опору реальных треугольников и авторские высотные datums. Это проход геометрии,
  а не timed ride или новая проверка всего автомобильного hull.
- `Range_AmmoReservesAndRespawningPickupsStayFinite`,
  `Range_QueuedWeaponCarryJournalKeepsItsSelectionRequests` и
  `Range_WeaponCarrySwitchesKeepModelsAmmoAndPhysicalHandoffs` прошли на текущем
  warehouse. Fixtures больше не зависят от ResetActor при подходе к патронам,
  pre-LateUpdate pose или retired cloth solver. Невозможный living-hero drop
  заменён принятым правилом удержания и настоящим terminal defeat/reset.
  Сохранены шесть ordered transfers, finite ammo, model identity, точная pause,
  обе временные ладони, resume/reset/clip cleanup. Visible-jacket oracle с прежним
  порогом −0.5 mm теперь измеряет минимум +7.260 mm; кадры передач просмотрены.
- `tools/test_asset_pipeline.py`: staging/rollback/meta contracts; генераторы
  отдельно проверили геометрию и детерминизм. macOS native preflight, universal
  arm64/x86_64 build, ABI/DSP и подпись прошли с установленными clang/SDK.
  Windows binary не пересобирался; semantic fingerprints не ослаблялись ради
  отсутствующего измеренного выигрыша.

Последние результаты по каждому из 13 различных PlayMode cases — Passed,
без skipped в selections. Это совокупность адресных запусков с повтором только
исправленных failures, не новая общая selection. Основная EditMode selection —
53 Passed; отдельные terrain cases относятся к затем отклонённому candidate.
`check-docs`, `check-workspace` и `git diff --check` прошли. Временные
frames/audio/trace/staging удалены; сохранены named checks и эта сводка.
Unity serialization noise без изменения import settings удалён из diff.
Полные suites, Windows benchmark и player build не запускались.
