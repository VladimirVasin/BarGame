# Аудит проекта

Продолжение: [принятые исправления](audit-remediation-plan.md).

Проверяемая версия — `8c43e547`, 28 октября 2026 года. Исходная рабочая копия
чистая. Подтверждены потеря обязательной банки до открытия задания кота и отказ
упаковочного валидатора из-за трёх устаревших зависимостей героя. Runtime и
ассеты не исправлялись; временный диагностический тест удалён после проверки.

Аудит охватывает все восемь согласованных пунктов. Свежие адресные проверки
отделены ниже от анализа кода и прежней визуальной приёмки. Это не заключение
о готовности Windows player или о прохождении каждого действия во всех сценах.

## Подтверждённые дефекты

### P1 Потеря банки блокирует задание кота

[GameSessionState.cs](../Assets/Scripts/Runtime/Core/GameSessionState.cs),
строки 806–810, резервирует `OpenStewCan` только при активном `FeedTheCat`.
Единственную открытую банку из `home.refrigerator.shelf-middle-right` можно
съесть через инвентарь до второго дня. Источник остаётся собранным;
[HomeRefrigeratorWorldBuilder.cs](../Assets/Scripts/Runtime/World/HomeRefrigeratorWorldBuilder.cs),
строки 877–881, не создаёт его повторно. Магазин предлагает другой предмет,
`ClosedStewCan`; преобразования в открытый нет. Кот требует открытую банку,
а активное задание блокирует спуск через
[StairwellQuestDescentBlocker.cs](../Assets/Scripts/Runtime/Interaction/StairwellQuestDescentBlocker.cs),
строки 111–130.

Временный диагностический тест
`ProjectAuditProbe.CatFoodSource_CanBeConsumedBeforeQuestOpens` подтвердил
следующую последовательность публичных операций:

```csharp
GameSessionState.BeginNewGame();
GameSessionState.TryStartGameTimeAt(7 * 60 + 40);
string source = HomeRefrigeratorInventoryAdapter.GetSourceId("shelf-middle-right");
GameSessionState.TryCollectWorldItem(source, InventoryItemId.OpenStewCan);
GameSessionState.AdvanceGameTime(900f);
GameSessionState.TryConsumeInventoryItem(InventoryItemId.OpenStewCan);
GameSessionState.AdvanceGameTime(1980f);
```

На втором дне задание активно, банок ноль, источник собран и повторное получение
из того же источника отклонено. Диагностика утверждала именно ошибочное
наблюдаемое состояние; её Pass не означает исправления игры.

Минимальная следующая проверка — постоянная регрессия этой междневной цепочки
после выбора исправления. Решение от 3 сентября в `architecture-notes.md`
сохраняет первый день без кормления/задания. Выбор между защитой будущего
обязательного предмета и восполнением должен учитывать это решение; автоматически
менять дату задания или резервирование аудит не разрешает.

### P1 Текущая версия не проходит упаковочный валидатор

`PlayerBuildAssetValidationTests.CurrentProject_PassesReadOnlyGate` завершился
отказом по трём зарегистрированным владельцам. Расхождения уже находятся в
`8c43e547`; рабочие файлы героя совпадают с commit. Для JSON дополнительно
исключено объяснение различием LF/CRLF.

| Производный ассет | Сохранённый SHA256 начинается с | Текущий upstream SHA256 начинается с |
| --- | --- | --- |
| Default NPC → hero Blender source | `d33560673956` | `e3faf9acb17e` |
| Шарф → hero Blender source | `b138c9221e99` | `e3faf9acb17e` |
| Нагрудный фонарь → hero manifest | `6bc8706edac7` | `3851703e4363` |

Проверки расположены в `DefaultNpcAssetSetup.cs`, строки 222–225,
`Assets/Scripts/Editor/Player3D/PlayerScarfAssetSetup.cs`, строки 48–50, и
`CombatFlashlightAssetSetup.cs`, строки 56–57, под `Assets/Scripts/Editor/`.
Hash генератора шарфа совпадает; устарела его зависимость от героя.

Доказан отказ build gate и отсутствие подтверждённой актуальности производных
ассетов. Видимая несовместимость тел, шарфа или контакта лампы из hash не следует:
изменение неиспользуемой анимации тоже меняет весь Blender source.

Ремонт: сохранить нынешний hero upstream; отдельно выполнить штатные цепочки
default NPC → damage refresh → importer, scarf → importer и flashlight → measured
binding rebuild из `tools/README.md`, затем повторить тот же gate. Подмена
сохранённых hash не подтверждает совместимость и не заменяет генерацию.

### P2 Обязательный StreetProfile не охвачен build gate

[CityStreetSurfaceWorldBuilder.cs](../Assets/Scripts/Runtime/World/CityStreetSurfaceWorldBuilder.cs),
строки 177–188, требует `City/StreetProfile/CityStreetProfile3D` и оба меша
`SurfacePatch`/`BoundaryStrip`. Этот путь безусловно используют City и Home.
В `PlayerBuildAssetValidation` и остальных prebuild-регистрациях нет его
read-only validator. Поэтому отсутствие или неправильная топология нового
семейства не получает собственного раннего отказа до runtime-композиции.

Текущая геометрия присутствует и адресные проверки проходят. Ошибка относится
к охвату валидатора, а не к доказанному дефекту данного FBX. Минимальный ремонт —
зарегистрировать проверку модели/manifest, читаемости, имён, топологии и метров;
добавить отрицательные случаи в существующее покрытие build gate.

### P2 Датчик грузовика не обрабатывает полный буфер

[CityCanneryController.cs](../Assets/Scripts/Runtime/World/CityCanneryController.cs),
строки 245–256, получает максимум 64 результата `OverlapBoxNonAlloc`. После
отбрасывания собственных и низких коллайдеров он может объявить путь свободным,
хотя реальное препятствие не попало в заполненный буфер. У debug-аналога в
`CityCanneryController.Debug.cs`, строка 83, полный буфер уже означает отказ.

Недостающая обработка насыщения подтверждена кодом; насыщение в текущем City
не воспроизводилось. Минимальный ремонт — grow/retry либо корректный ограниченный
fallback. Проверка должна создать полный буфер отбрасываемых контактов и
дополнительное реальное препятствие, а не только проверять размер массива.

## Риски и расхождения документации

- **P3 Запись bindings во время упаковки.**
  `CombatWeaponCarryAssetSetup.OnPreprocessBuild` вызывает `BuildOrThrow`;
  строки 200–207 создают или пересохраняют производный `Bindings.asset` при
  расхождении. Это подтверждённый побочный эффект, но не доказанное нарушение
  узкого read-only обещания самого `PlayerBuildAssetValidation`. Полезно разделить
  явный rebuild и сравнение сохранённых slots; менять packaging-контракт следует
  явно. Runtime-дефект этим обзором не установлен.
- **P3 Необоснованное утверждение про lens flares.** Строка 22 матрицы
  `world-location-update-plan.md` заявляет существующий эффект. Найдены только
  capability/resources URP и компонент с intensity `0`, без runtime provider или
  serialized flare objects. `CleanHalation` — другой эффект. Исправление касается
  описания охвата; добавление flares не требуется.
- **Пробел combat coverage.** `CombatPerformancePlayModeTests.SetUp` допускает
  только две мигрированные проверки, остальные legacy-сценарии явно пропускает;
  `CombatTestPlayModeTests` тоже содержит migration admission. Наличие тестового
  метода не доказывает текущий coverage. Нужна миграция нужных сценариев на
  production zombies, а не снятие Ignore без исправления fixtures.
- **Окружение native authoring.** Штатный macOS preflight под Python `3.12.14`
  отказал по Apple clang: установлен `clang-2100.3.34.2`, в контракте
  `clang-2100.1.1.101`. Системный Python `3.9.6` также не подходит. Это расхождение
  окружения с declared toolchain, не доказательство неисправности готового DSP.

## Проверка восьми пунктов

| Пункт | Выполненное покрытие | Граница вывода |
| --- | --- | --- |
| 1 Проверяемое состояние | Clean `8c43e547`; Unity `6000.6.7f1`, URP `17.6.0`, Input System `1.20.1`, Navigation `2.0.16`; 13 build scenes и 10 gameplay roots. | Исходная версия отделена от этой записи аудита. |
| 2 Архитектура и жизненный цикл | Transition/AreaTravel/CompositionDriver, resident dormancy, NewGame, pause/input/cursor, generation-safe leases, speech jobs/version cleanup; свежие loading, pause UI и stopped-owner проверки. | Не все adapters получили отдельный runtime-прогон. Нового подтверждённого дефекта в просмотренных owners нет. |
| 3 Игровые цепочки | Повторные City → Stairwell → Home → City и затем Bar; travel MountainRoad → AlpineVillage → City; источник/потребление банки. Покупки/refund, могильные commit/restore, supply catch-up и Combat reset просмотрены по коду. | Полные магазинные, могильные, караоке, канаточные и combat действия заново не проходились. |
| 4 Физическая связность | Свежие support stations, terrain/paving boundaries на трёх видах земли, material/footstep/priority общего меша; реальные door roundtrips и lodge collision/тепло/укрытие. | Горный серпантин и тоннель не получили нового полного пешего/автомобильного прохождения. |
| 5 Ассеты и воспроизводимость | Read-only gate, StreetProfile validator/manifest signature, staging/rollback tests, native import/publication source review. | Gate отказал по трём upstream-зависимостям; Windows/native rebuild не выполнялся. |
| 6 Производительность и ресурсы | Свежие ограниченные City measurements; mesh/RT/material/recorder/voice lifetime по общим владельцам. | Нет нового combat/cloth/snow player benchmark и длительного теста роста памяти. |
| 7 Представление и канон | Все 24 категории по providers/settings/callers; новые lodge/door кадры; lodge wind DSP; §§6/16/21 и выборка бар/порт/завод/деревня/Combat исключений. | Девять художественных критериев сверены с прежней приёмкой; нового обхода всех сцен/seed/погоды и субъективного прослушивания районов нет. |
| 8 Проверки и документация | Точные regex-фильтры сверены с XML totals; Explicit реально выполнен; docs/workspace и diff; различены сохранение, свежий Pass и Deferred. | Полные suites не являются доказательством этого адресного аудита. |

### Свежесть доказательств по сценам

| Сцена | Доказательство этого аудита |
| --- | --- |
| MainMenu | Settings/root/input source review; прежняя native/UI приёмка сохранена. |
| City | Повторные exterior roundtrips, area composition, idle measurements и pure/mesh surface contracts. |
| DoorTransition | Direct Enter.Play и оба начальных кадра; production выходы через RequestDoorLoad в roundtrip. Отдельный direct Exit.Play тест не добавлялся. |
| BarInterior | Повторный production вход/выход после Home; gameplay/speech owners по коду. |
| SupermarketInterior | Purchase/refund/source/stock и UI ownership по коду. |
| StairwellInterior | Повторный переход через Home; cat/descent contracts и междневная диагностическая цепочка. |
| HomeInterior | Повторный вход/выход, fridge source lifecycle, shared terrain contracts. |
| MountainRoad | Загрузка/готовность/cleanup в area travel; route geometry owners по коду. |
| AreaLoading | Overlay до готовности, progress/time/audio cleanup и interrupted owner. |
| ChurchInterior | Metric/door/shared render owners и предыдущая ChurchRemodelAcceptance. |
| AlpineVillage | Area composition и lodge двери/свет/музыка/акустика/тепло/reset; кадры просмотрены. |
| MothersHouseInterior | Resident/inspection/item/speech owners и предыдущая native приёмка. |
| CombatTest | Reset/isolation, asset/render/audio owners и текущие ограничения test admission по коду. |

### Фактические измерения City

Apple M5, Metal, Editor, seed `20260727`, HeavyRain, sober, focus active,
render scale `1`, target `60`. Два пятсекундных окна после двухсекундного
прогрева. Оба фактических target — **1710×854**, хотя запрашивались 1080p и 4K.
Это два последовательных окна одного разрешения, а не сравнение разрешений.

| Метрика | Первое окно | Второе окно |
| --- | --- | --- |
| Средний frame interval | 23.01 ms | 26.91 ms |
| Frame p95 | 27.10 ms | 34.63 ms |
| GPU p95 | 16.14 ms | 19.79 ms |
| Средний GC allocation/frame | 34.22 kB | 36.39 kB |
| Средний foot bake/frame | 0.326 ms | 0.341 ms |

Frame p95 превышает локальный бюджет `16.67 ms`; источник задержек этим замером
не атрибутирован. Render-thread counter недоступен. Размер total allocated
memory вырос примерно на 1.02 MB и 0.013 MB соответственно; managed heap в первом
окне уменьшился, во втором вырос. Из этих коротких окон нельзя вывести утечку,
стоимость всех NPC/отражений, Windows FPS или выигрыш какого-либо изменения.
Следующий performance-шаг — локализовать GC/CPU в сопоставимом текущем workload,
затем отдельно измерять конкретные тяжёлые действия.

### Проверка эффектов

| № | Категория | Подтверждённое покрытие |
| --- | --- | --- |
| 1 | GI | Source-derived finite bounce; bar SCGI только fixture pilot; production baked/APV/SSGI provider не найден. |
| 2 | SSAO | Активный shared renderer feature. |
| 3 | Fog | Runtime exponential-squared depth fog; общего height-fog provider нет. |
| 4 | Volumetric | Ограниченные owner-scoped composite passes. |
| 5 | Shadows | URP main/additional и местные Soft/Hard/None. |
| 6 | Grade | Реальные scene volumes/Neutral/ACES overrides. |
| 7 | Probes и SSR | Scene cubes; SSR City/warehouse, camera exclusions, half-resolution; temporal filtering выключен. Home balcony получает probes без City SSR. |
| 8 | Water | Type-specific depth/normal/Fresnel/foam/reflection; общего caustics provider нет. |
| 9 | Response | Physical maps, wetness registry и MPB. |
| 10 | Bevels | Blender topology. |
| 11 | COLOR | Ограниченный road crown pilot, не общий forest rollout. |
| 12 | Wind и contact | Weather/wind/cloth owners; coastal root metadata и motion-vector include. |
| 13 | Wear | Causal masks/atlases/geometry; общего DecalProjector нет. |
| 14 | Particles | Ограниченные rain/snow/dust/smoke owners. |
| 15 | Rim | Source-dependent material/Fresnel; независимого постоянного glow нет. |
| 16 | Outlines | Локальные interaction affordances, не общий world outline. |
| 17 | Bloom | Реальные volume overrides; CleanHalation — отдельная ветвь. |
| 18 | DOF | Shared Gaussian и scoped cinematic owners/settings. |
| 19 | AA | Native target/SMAA; отдельный preview MSAA; TAA не включён. |
| 20 | Vignette | Scene/state overrides. |
| 21 | Grain и dither | Слабый интерьерный grain, технический surface/occluder dither. |
| 22 | Flares | Capability есть; эффективного эффекта нет. |
| 23 | Blur | Production intensity нулевая. |
| 24 | CA и distortion | Shared intoxication volume/settings с нейтральным трезвым состоянием. |

Saved graphics preferences задают запрос, а не гарантируют provider/receiver в
каждой сцене. Необязательный эффект не становится долгом от одного отсутствия.

По проверенным активным текстам новых нарушений канона не найдено: бармен/три
пары и караоке, заводские Work/Wait/Receiving, бригадир, выборка порта, деревенские
мысли и две разрешённые длинные записки. Речь использует bubbles; Narrative
pages немые; HighTables не допущен к речи; warehouse ownerless audio ограничен
принятым полигонным исключением. Это не полная литературная редактура каталога.

## Выполненные проверки

- PlayMode: шесть выбранных cases, все Passed, без skipped. Имена:
  `AlpineVillageLodgeShelter`, `DirectLoad_IsIdleUntilExplicitInitialization`,
  `Pause_UsesItsOwnPointerContextWhileGameplayMenusAreBlocked`,
  `HomeVisits_KeepTheSameCityThroughTheStairwell_ThenTheBarAgain`,
  `AreaTravel_ComposesEachDestinationBeforeCompleting`,
  `StoppedTransitionOwner_ReleasesGuardAndSceneQueue`.
- EditMode: семь cases; шесть Passed, один Failed. Диагностическая цепочка банки,
  shared street stations, три terrain/paving варианта и combined material/steps
  прошли. Единственный Failed — `CurrentProject_PassesReadOnlyGate`, описанный выше.
- `CITY STREET PROFILE DIRECT VALIDATION OK`; signature совпадает с опубликованным
  manifest. Первый запуск Blender упал до Python в sandbox; тот же read-only
  запуск вне sandbox прошёл. Ассеты не публиковались.
- `test_asset_pipeline.py`: синтетические failure/staging/rollback проверки Passed.
- `check-docs`, `check-workspace` и `git diff --check` прошли. Временные
  frames/audio/контактный лист и probe удалены.

Предыдущее падение lodge paused-playhead **не воспроизвелось**. Текущий Pass
охватывает shelter/music/warmth; отдельный `AlpineVillageLodgeFurnishings` с
креслами/осмотрами/фотографией в эту selection не входил.

## Порядок дальнейших исправлений

1. Выбрать совместимое исправление уникальной банки и закрепить междневную
   регрессию; сохранить отсутствие кормления в первый день.
2. Восстановить актуальность NPC/шарфа/фонаря штатными dependency-цепочками;
   повторить общий read-only gate без подмены hash.
3. Добавить StreetProfile к build gate и корректный NonAlloc overflow fallback.
4. Исправить описание flares; уточнить допустимость записи derived bindings на
   build и выбрать действительно нужные legacy combat tests для миграции.
5. Локализовать наблюдаемые City GC/CPU costs, не выдавая Editor измерение за
   player benchmark. Новые полные suites/build/smoke остаются отдельным запросом.
