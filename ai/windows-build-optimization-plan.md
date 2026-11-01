# Desktop build: измерения и выполненные шаги

Статус на `2026-11-01`: **0/1/3 Current**, **2/4/5 Deferred**.
ZIP проверены;0.1.0 отозван. [Player collision audit](player-collision-audit.md).
Исторический macOS cold:3138.491с/64warnings/ZIP971462474B;Universal/ad hoc.

## Первый результат и прогретая итерация

Первый `BuildWindows`/exit 0: `0.1.0`, `b699a62b` + исправления подготовки.
BuildReport: **3616.890 с**, **180 warnings**, **1 950 768 132 B**.
ZIP: **959 312 026 B**; комплект без debug: **1 950 509 217 B**.
PE x64, Windows VHS и состав ZIP проверены; запуск на Windows не проверен.
`Writing asset files`:3583.531 с, включая shaders/сериализацию; это не shader timer.
Asset usage: textures54.9%,meshes22%,animations7.6%,shaders1.7%; это дисковые,
не runtime RAM/GPU показатели.

Прогретый Windows rebuild: **5.386 с, 0 warnings**, без shader pass work в логе.
Это cached/script-only путь: cache state различается, причинный выигрыш кода
не доказан. Дорогая повторная shader compilation не воспроизвелась; холодный
build сам по себе не оправдывает stripping/quality cuts.

## Соответствие проекту и границы

[AI](../AI.md), [FAST](prompt-templates.md), [эффекты](visual-effects-standard.md),
[арт](city-zones-art-bible.md)/[сюжет](city-story-bible.md) остаются обязательными.
Мир, звук, управление, §6/§16/§21 и девять арт-проверок сохранены.
Нет новых FX, света, passes, quality cuts или Unity/URP upgrade.

- Unity `6000.6.7f1`, URP `17.6.0`; Windows x64/Mono, DX11+DX12,
  `BuildOptions.None`. Тринадцать сцен, MainMenu первая; runtime composition.
- До первого билда исправлены stale provenance NPC/шарфа/secondary motion и
  warehouse-only gate фонаря. Source proofs восстановлены штатными средствами;
  фонарь проверяется как существующее session equipment.
- `Light.lightmapBakeType` — Editor-only API: присваивания защищены
  `UNITY_EDITOR`, игровой свет сохранён. Resource gate и player C# compile
  после ремонта прошли. Эта подготовка не является shader time.
- Общий `Bar Promenade/Lit`: **720 ForwardLit fragment вариантов/API**,
  **1440 DX11+DX12 entries**, не уникальных compilation jobs. Первый лог:
  dx11 wall935.31s/local hits0/compiled720/CPU9290.92s;
  dx12 wall10.07s/local hits715/compiled5/CPU42.94s.
  Это один pass, не семейство или весь build; DX12 почти весь из cache.
- `Strip Unused Variants` уже включён, общего Lit нет в Always Included;
  он в Resources, GBuffer уже отсеян. Полная shader family-разбивка и runtime
  loading не измерены. Ни одного нового безопасного удаления не доказано.

## 0. База и отчёты — Current

`tools/report-unity-build.py` читает существующий Unity log, извлекает BuildReport,
стадии, shader/pass/stage/API, retained/cache/compiled/skipped/CPU/wall и
дедуплицирует warning sites по shader/include/line/API. Неизвестное — `null`,
не ноль. CPU и перекрывающиеся pass wall не суммируются в elapsed; warning
occurrences отличаются от `BuildReport.warning_count`.

Сравнение требует одинаковых declared configuration/machine/cache provenance,
logged configuration и shader cache work. Несопоставимые отчёты не получают
timing delta; delta не доказывает причинный выигрыш.
Configuration ID задаёт не отражённые в логе настройки/сцены/options.
Абсолютные пути скрываются; JSON атомарен, вход не перезаписывается.

Bounds из кода: log64MiB, строка1MiB, JSON1MiB, shader rows4096,
warning sites512, text1024chars, context ID128chars.
Превышение — ошибка.

`Library` сохраняет idle **14 дней**, retention не повышен. Для обычной итерации
не чистить Library/Bee/ShaderCache и не применять Clean Build. Работать в
прогретом checkout; новый worktree холодный, cache между версиями произвольно
не переносить. [Unity: caches](https://docs.unity3d.com/kr/current/Manual/build-cache-location-reference.html).

## 1. Инвентаризация общего Lit — Current

`tools/check-lit-inventory.py` сверяет review snapshot
`tools/lit-keyword-inventory.json`: serialized keyword sets, runtime keyword
owners/material helpers, local shader include closure и package/project/context
fingerprints. Scanner работает офлайн, без runtime hot-loop обхода.

Общий Lit: **54 материала / 5 serialized sets** — обычные, emission,
карты героя, premultiplied glass, отключённые specular/environment reflections.
City Surface: десять материалов / один набор. Runtime review добавляет **3**
набора: alpha-test (blood/wear decal), straight-alpha (runtime transparency),
environment-reflections-off (bottle glass). Четвёртая runtime-запись emission
повторяет serialized set. Восемь evidenced local sets — **нижняя граница**,
не whitelist: inherited clone states, dynamic keywords, global fog/shadows,
SSR/SCGI/saved FX и package internals не закрыты семантически.

Lexical scan не доказывает C# dataflow, reflection, все loads/aliases или
runtime reachability. Changed/unknown inputs требуют review; snapshot
не обновляется автоматически. Всегда `variant_policy=retain_all`,
`removal_candidates=[]`. Exit0 — snapshot matched, exit2 — review needed,
exit1 — malformed input/path/bound. Ни одного варианта не удалено.

Bounds:16000 files,2MiB/file,64MiB read total,128 local includes,JSON1MiB;
первые32 diagnostics и общее число. Optional report только в `Build/reports/*.json`.

## 2. Build-only stripping — Deferred

`IPreprocessShaders` не добавлен: лишний retained tuple не доказан, warm build
без shader work. Недоказанные численные цели variants/wall сняты.

Возврат к задаче требует измеренного shader bottleneck и доказанной
недостижимости конкретного shader/pass/API/keyword/global-state сочетания с
учётом runtime materials. Unknown сохраняется. Depth/Normals/Motion/Meta,
CBUFFER, instancing/DOTS, Forward+, fog/shadows/probes и SSR/SCGI обязательны.
Не менять `multi_compile`/passes/семейства ради счётчика. [Unity: stripping и runtime variants](https://docs.unity3d.com/ja/6000.0/Manual/shader-variant-stripping.html).

## 3. Provenance diagnostics — Current; narrowing Deferred

Отдельный `SecondaryMotionBakeDiagnostics` регистрируется через
`InitializeOnLoadMethod` в `PlayerBuildAssetValidation`. Read-only helper
называет `source.path`, raw SHA-256 либо AssetDatabase dependency hash,
saved/current значения, missing manifest/source/recording, malformed schema
и unsafe path. Вывод: первые12 failures и total, текст поля ограничен384chars.
Repair: `-executeMethod BarPromenade.Editor.SecondaryMotionBakeAssetSetup.BakeAll`.

Пройдены **11 focused diagnostic cases** в существующем
`PlayerBuildAssetValidationTests.SecondaryMotionDiagnostics_NamesActualFailureWithoutReadingUnsafePaths`.
Исходный self-hashed `SecondaryMotionBakeAssetSetup` и реальный gate не изменены;
helper не импортирует assets, не пишет stamps и не запускает solver/bake.

Dependency narrowing не реализован. Hash нельзя подменять ради build:
semantic proof требует geometry/topology/skin/UV/shapes/rig/garments/contacts,
а secondary motion — clip/pose/collision/solver inputs. Repair-порядок:
source → stable import/prefab → physical recording → read-only gate → build.
Rig/cloth/clip/contact drift по-прежнему инвалидирует provenance.

## 4. DX11 dev-профиль — Deferred

DX11+DX12 сохранены. Отдельного dev-профиля, смены backend/Development/quality
или C# stripping нет. DX12 почти весь взят из cache уже в первом Lit pass;
исключение API без family-измерений не обосновано. Возврат возможен при
измеренном расходе и отдельном решении о совместимости.
[URP: renderer/quality assets](https://docs.unity3d.com/cn/6000.0/Manual/urp/shader-stripping.html).

## 5. Resource/import/loading оптимизация — Deferred

Resources/providers/адресация, textures/LOD/animations и сцены сохранены.
Resource bottleneck не доказан; Addressables/migration/cuts не добавлены. Ограниченные отчёты и упаковка реализованы отдельно; это не
доказательство ускорения import/loading. Рост ignored output регулирует
workspace policy; staging временный, лимиты хранения не повышаются.

## Desktop build и упаковка

`ProjectSceneSetup.BuildWindows` сохраняет путь
`Build/Windows/BarPromenade.exe` и прежний success log.
`BuildMacOS` использует тот же preparation/порядок13сцен/optionsNone и выводит
`Build/macOS/BarPromenade.app`: Universal x86_64+arm64, существующие Mono/Metal.
Проверяет backend/API без изменения настроек; target-local
`OSXUniversal/Architecture=x64arm64` устанавливает на время build и восстанавливает
в `finally`. Mac build пересоздаёт `.app`, сохраняя Library cache: нет старого ASCII executable после упаковки.
MinOS12.0/VHS Universal сохранены. [Unity: macOS architecture](https://docs.unity.com/en-us/engine/6000.6/script-reference/unityeditor/osxstandalone/userbuildsettings/architecture).

`tools/package-player.py` упаковывает готовый player без Unity. Windows:
проверяет PE x64 EXE/UnityPlayer/VHS, обязательные Data/Mono, исключает
PDB/DoNotShip/DS_Store; комплект распаковывается целиком. macOS: проверяет
Universal executable/UnityPlayer/VHS, executable bit, Data и codesign;
`ditto` сохраняет `.app`, внутренние links/permissions. Это не notarization
`--sign-adhoc`: ASCII executable/CFBundleExecutable, nested DR/signatures→root;
исправляет Unity Unicode self-seal/ARM64 signatures. RU title сохранён.

ZIP публикуется после сравнения file size/CRC с исходным player; SHA-256 и JSON
пишутся отдельно. `--verify-only` ничего не пишет. Bounds:100000 files,
200000 scanned entries,300000 ZIP entries,2MiB blocks;512 Mach-O/64KiB signature metadata; Windows links
и внешние macOS links отвергаются. Output `Build/BarPromenade-{Windows-x64|macOS-Universal}-{version}.zip`,
рядом `.zip.sha256`; report `Build/reports/package-{windows|macos}.json`.
Не коммитить build output. Gate/ZIP proof не является runtime compatibility proof.

## Workflow

Из корня репозитория, Unity закрыта; на macOS установлены нужные player modules.
Каждый build запускается только по запросу. Пример версии editor текущего проекта:

```sh
unity_editor=/Applications/Unity/Hub/Editor/6000.6.7f1/Unity.app/Contents/MacOS/Unity
"$unity_editor" -batchmode -quit -projectPath "$PWD" -buildTarget Win64 -executeMethod BarPromenade.Editor.ProjectSceneSetup.BuildWindows -logFile /tmp/bargame-windows-build.log
"$unity_editor" -batchmode -quit -projectPath "$PWD" -buildTarget OSXUniversal -executeMethod BarPromenade.Editor.ProjectSceneSetup.BuildMacOS -logFile /tmp/bargame-macos-build.log
python3 tools/report-unity-build.py --log /tmp/bargame-windows-build.log --output Build/reports/windows.json --configuration-id win64-mono-dx11dx12-scenes13-v1 --machine-id local-host --cache-state warm
python3 tools/check-lit-inventory.py --output Build/reports/lit-inventory.json
python3 tools/package-player.py --target windows
python3 tools/package-player.py --target windows --verify-only
python3 tools/package-player.py --target macos --sign-adhoc
```

Запускать только нужный target. `--cache-state warm` указывать
только при подтверждённом cache состоянии, иначе `unknown`; ID не переиспользовать
для изменённых options/settings/сцен. Parser принимает `--compare previous.json`,
но не выводит выигрыш для разных target/cache/configuration. Repair выполняется
только по конкретному gate, не как обязательная часть каждого build.

## Сохранность всех 24 категорий эффектов

Матрица проверяет границы пакета; новые визуальные сценарии/кадры не заявлены.

| Категории | Существующие владельцы и сохранённые ограничения |
| --- | --- |
| 1–6: GI(baked/APV,SSGI,SCGI),SSAO,height/depth fog,volumetric light,shadows,grade/tonemapping | Area light/fog/grade,SSAO,Bar SCGI pilot: active/off,occlusion,темнота без смены settings. Global GI/лучи не включены; новых passes/lights нет. |
| 7–16:probes+SSR,water,roughness/wetness,bevel/normals,vertex colour,wind/interaction,decals,particles,rim/Fresnel,outlines | Material/motion/reflection owners сохранены: receiver/camera exclusions,history,forward/depth/shadow/motion parity,wet/dry/contacts,caps/lifetime. Нет новых buffers/decals/outlines; inventory не разрешает stripping. |
| 17–24:Bloom,DoF,AA,vignette,grain/dither,flares,motion blur,chromatic/lens distortion | Shared settings/area profiles,SMAA,intox/scoped cameras и saved switches сохранены. Нет новых flare/blur/lens/history; runtime CPU/GPU/GC/loading не менялись этим пакетом. |

FAST: один primary, общей системе максимум один focused check; без автоматического
стека suites/build/smoke. 30с без прогресса → проверить процесс. Missing variant/quality regression/cache churn → остановка/откат пакета.
Variant count не заменяет player, ZIP proof не доказывает запуск.
