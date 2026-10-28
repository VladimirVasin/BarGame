# Пошаговое обновление всех локаций

План от `2026-10-27`, **Current**: вся очередь принята.
У каждого пункта — выполненное изменение либо обоснованное сохранение.

## Основания и общий контракт

**Current:** исходники, [мир](current-world.md), [решения](architecture-notes.md).
Уточнение существующей формы/материалов/света соответствует проекту.
Legacy допустим; миграция отдельная. Гипотезы проверяются кадрами;
при отсутствии доказанного дефекта результат — сохранение.

- [x] Сверить актуальные Git diff, планы/ассеты и владельцев систем.
- [x] Обе библии: [форма](city-zones-art-bible.md), [смысл](city-story-bible.md),
  «Нельзя», §§6/16/21 и девять acceptance checks.
- [x] Зафиксировать исходный кадр/разрыв/цель; пройти всю матрицу эффектов.
- [x] Сохранить метры/оси/UV/anchors/коллизию/проходы/identity/контакты.
  Новая геометрия — Blender-генератор, интерьеры — `interior_kit`.
- [x] Ограничить GPU/CPU/memory/GC/loading: meshes/draw calls/textures,
  Light/cube/update/cache; общие материалы/буферы. Выигрыш пока не измерен.
- [x] Проверить culling без потери времени/коллизии/ввода;
  pause/focus/disable/dormancy/return/unload и освобождение ресурсов.
- [x] Выполнить адресную приёмку; закрыть пункт после просмотра доказательства.

## Очередь

Очередь закрыта адресными доказательствами; канатка 06–08 — общий пакет.
Source-preservation означает review владельцев/coverage без свежего прогона всех действий.

### 01. Исходный визуальный обход — Current

- [x] Просмотрены все 13 build scenes: MainMenu, City, DoorTransition,
  BarInterior, SupermarketInterior, StairwellInterior, HomeInterior, MountainRoad,
  AreaLoading, ChurchInterior, AlpineVillage, MothersHouseInterior, CombatTest.
- [x] Подлокации 02–44 приняты по указанным кадрам/контактам;
  день/ночь/погода/grayscale/опьянение, снег/метель — по применимости.
- [x] Дефекты исправлены, недоказанные GI/forest кандидаты удалены;
  сохранение отделено от новых runtime Pass. Матрица эффектов приведена ниже.

### 02. Подъезд — Current

- [x] `interior_kit`: оболочка/откосы/двери/лестницы, соединённые поручни,
  решётка/радиатор/трубы/щитки/завал; коллизия/камеры/насест кота сохранены.
- [x] `Stairwell`, `StairwellDoorHandleContacts` Passed: формы/опоры,
  actual handle/backplate и source docks согласованы с DoorUseLoop.
  Ground seam/спуск/catTalk/paired feeding Passed, renderer snapshot восстановлен;
  `Scene_BootstrapsVerticalRouteAndAtmosphere` Passed.
- [x] Source-preservation: фактуры/приборы и возврат Home прежние.

### 03. Дверной переход — Current

- [x] Passive authored полотно/коробка/порог/ручка согласованы с подъездом.
  `DirectLoad_IsIdleUntilExplicitInitialization` Passed: idle, оба начальных
  кадра и полный Enter.Play.
- [x] Source-preservation: pivots/таймлайн/звук/чёрный проём/FOV/крен/input прежние;
  phase/endpoints — review `DoorTransitionTimelineTests`, полного Exit.Play
  свежего Pass нет. Другие двери автоматически не мигрируются.

### 04. Кладбище: сторожка, ворота, ограда — Current

- [x] Blender-проёмы/кровля, ворота/опоры и метрические UV приняты:
  `ImportedKit_ValidatesAsPassiveFixedMeterGeometry`, `City`/близкий вид окна.
- [x] `CityChurchCourtyard`: общий проход восстановлен и пройден в обе стороны;
  compact-регрессия сохраняет ID/вид соседних могил. Ворота/прозрачная ограда
  осмотрены; могильные действия прежние.

### 05. Лодочная станция: будка, пирс, эспланада — Current

- [x] Blender-будка/пирс/эспланада приняты тем же validator и `City`:
  близкие виды двери/люка, плит/накрывок/ступеней показывают стыки.
- [x] Метры/коллизия/лодки/pitch UV сохранены; берег тёмный, свет местный.
  Действия рыбака не менялись и заново не испытывались.

### 06. Нижняя станция канатки — Current

- [x] Blender-каркас/кровля/привод/ограждения приняты в
  `MountainRoadNativeLighting`: близкий кадр, прежние метры/материалы/свет/cube.
- [x] `Ride_OnlyLeavesTheAreaOnceTheScreenIsBlack`: посадка/ожидание,
  grips и оба прибытия пройдены; кадры платформ/подходов просмотрены.

### 07. Кабина и опоры канатки — Current

- [x] Профили/крепления приняты в `MountainRoadNativeLighting`;
  габариты/сиденье/боковой вход/grips/траектория сохранены,
  освобождение shared meshes при выгрузке проверено.
- [x] Та же проверка: движение/выход на обоих концах, skip/blackout
  и освобождение ride ownership; контакты/кадры приняты.

### 08. Верхняя станция канатки — Current

- [x] Общий crafted pack подключён через прежний Return-builder;
  anchors/свой навес/натяжение сохранены, нижний привод не переносится.
- [x] Верхняя платформа/кабина/навес просмотрены в поездке туда и обратно;
  прибытие/отправление пройдены, snow/cold owners сохранены.

### 09. Горный тоннель — Current

- [x] `MountainRoadNativeLighting`: портал/lining/стыки у прежнего прибора
  просмотрены; открытое продолжение сохранено.
- [x] Source-preservation: relief без Collider, прежние путь/коллизия/автомобиль
  не изменены. Свежий пеший/автомобильный проезд не запускался.

### 10. Серпантин и горный мост — Current

- [x] `MountainRoadNativeLighting`: профили/узлы стали просмотрены снаружи;
  поверхность/уклоны/материалы и лимит коллизии сохранены.
- [x] Source-preservation: меняется MeshFilter, старый physical mesh/путь/звук
  ограждения прежние; свежий пеший/автомобильный проход не запускался.

### 11. Терминальная площадка и хозчасть — Current

- [x] Подпоры/парапет/навес/служебные узлы уточнены без смены въезда,
  разворота и связи кафе с канаткой.
- [x] `MountainRoadNativeLighting` прошёл: близкие узлы/день/ночь просмотрены,
  тёмная кромка сохранена; envelopes/коллизия, свет/cube/pause/unload проверены.

### 12. Лыжная база / lodge — Current: сохранение

- [x] `AlpineVillageNativeLighting`: дерево/ткань/приборы сохранены.
  `AlpineVillageLodgeBounce`: table A/B нулевой, tabletop patch не достигает
  полезных комнатных поверхностей. Prototype удалён; прежние свет/halo/air/cube
  сохранены без усиления энергии.
- [x] Source-preservation: двери/тепло/снег/шаги/сиденья/фото/люк/печь и cot/kettle
  stubs не изменены. `AlpineVillageLodgeShelter` прошёл четыре дверных состояния/
  коллизию/акустику, затем упал на paused-playhead assertion;
  свежая полная проверка дальнейших действий не выполнялась.

### 13. Жилые улицы, балконы, дворовые карманы — Current: сохранение

- [x] `City`: нижние этажи/входы/балконы и четыре кармана осмотрены;
  плотность/опоры/проходы сохранены.

Нетронутые 13–29 приняты по кадрам/source-контрактам действий/коллизии/owners;
это сохранение, без нового runtime Pass сценок. Свежие проверки названы отдельно.

### 14. Промышленные улицы и коммунальная инфраструктура — Current

- [x] Восемь функций/вводы/опоры/task lights сохранены; street-view/day/night
  осмотрены. `CityDistrictAcousticContrast`: жилая/рабочая улица/парк без
  музыки различимы по физическим источникам; новый ambient не нужен.

### 15. Рабочий порт и склад — Current: сохранение

- [x] Порт в новой ткани осмотрен: доки/суда/краны/склад/груз/стыки сохранены;
  метрический kit/передачи/экипаж/позиционный звук прежние.

### 16. Консервный завод и весовая — Current: сохранение

- [x] Оболочка/подъезд сохранены; нового доказанного разрыва нет.
  FIFO/весовая/dry-wet/производственные контакты прежние.

### 17. Двор водоразбора — Current

- [x] `CityDomesticCourts`: входы/трубы/ремонт/вёдра/лампа/опоры и близкий
  вид реальной воды приняты; бытовой смысл/текст прежние.

### 18. Сушильный двор — Current

- [x] `CityDomesticCourts`: проходы, живые ткань/ветер, две выбивалки с боков
  у края ковра и коридор курящей бабушки приняты; плотность сохранена.

### 19. Остров последнего маршрута и автомобиль — Current: сохранение

- [x] `City` Passed: пассажирский обзор и водительская дверь/ручка/порог/dock
  просмотрены с доступной поверхности, без перекрытия оградой.
- [x] Остров/навес/павильон сохранены; пустой центр/посадка/поездка прежние.

### 20. Городские ориентиры — Current: сохранение

- [x] Контраст часов уточнён пигментом; close/day/night подтверждают dial/стрелки.
- [x] `CityLandmarksFromStreets` Passed: street colour/gray силуэты приняты.
  С улицы стрелки слабо различимы в native fog; теплица — открытый каркас/крыша,
  без стеклянных стен. Кинофасад/промышленный силуэт сохранены; pose/LOS проверены.

### 21. Двор слева от бара — Current

- [x] `CityBarYardCircuit`: живой круг/вращение колёс/контакт шин/свободное
  тело приняты; дерево/мебель/игрушка/урна/бутылка/прожектор сохранены.

### 22. Арка и убежище — Current

- [x] Проём/опоры/ступени/крепления осмотрены; люди/укрытие/огонь/речь прежние.
  `CityArchShelter`: физический проход юг↔север через `ClearLanes[0]` пройден.

### 23. Ярмарка — Current: сохранение

- [x] Четыре вида `City`: товар/люди/прилавки/укрытия/гирлянда читаются;
  междурядья/орган/колокол/функции сохранены.

### 24. Парк — Current

- [x] Белые треугольники настила исправлены; `City` day/night: поверхность/
  опоры/ступени чистые. Шахматы/фонтан/площадка/качели, посадки/укрытие/звук
  и тёмный центр сохранены.

### 25. Река, мосты, променады и устье — Current: сохранение

- [x] Day/night река/накрывки/крепления/ступени/завершения осмотрены, стыки
  эспланады приняты вблизи. Проходы/вода/свет прежние; новый обход не заявлен.

### 26. Церковь и приходской сад — Current

- [x] `ChurchRemodelAcceptance` Passed: exterior/interior метры, окна/роза/кровля,
  дверь/порог/проходы, City roundtrip, день/ночь/свет/cleanup.
- [x] `CityChurchCourtyard` Passed: скамьи/фонтан/полка/горшки, проходы/опоры/
  вода/свет, перенос/установка горшка. Храм пуст, святилище закрыто;
  служба/звон/действия со свечами не добавлены.

### 27. Пляж, море, маяк и дальние суда — Current

- [x] City/day/night: тёмный берег/вода/недоступный маяк/суда сохранены.
  `CityShoreSwash`: temporal swash/пена/sand contacts приняты; exact
  manifest-owned albedo/response общие у сухого песка и дна.

### 28. Окраинные дворы и горная граница City — Current

- [x] Дворы/подпоры/дренаж/колея/запасы/чаша осмотрены.
  `CityFringeApproaches`: четыре motor in/out/stop пройдены; у тоннеля
  проверен настоящий край corridor `WalkableDepth`, без новой калитки.

### 29. Восточный пост и дальняя долина — Current: сохранение

- [x] `City`: край/пост/караульные/сарай/навес/пассивная долина осмотрены;
  сопряжение/закрытый проезд/свет/дальние машины/охрана сохранены.

### 30. Супермаркет — Current

- [x] `Supermarket` Passed: касса/кассир/товары/ценники/опоры/холодильная ниша;
  линолеум/краска/металл/bloom сохранены. Покупка/возврат Passed:
  `ShelfBrowser_CentersProducts_CrossesAndSkipsEmptyShelf_ThenRestoresState`.
  Внешний вход — source-preservation.
- [x] CCTV imported basis нормализована при прежних world poses детей.
  `Supermarket` Passed: actual iris ray/normal после Track; четыре фронта просмотрены.
- [x] Индекс: четыре камеры, без surveillance gameplay.

### 31. Квартира героя и балкон — Current: сохранение

- [x] `AuthoredHome_PreviewsAllDaysAndKeepsLockedRoomClosed` Passed: комнаты/диван/
  кухня/стул/шкаф/картина/западный объём, дни 1–7/ночь; закрытая дверь,
  сон/обед/картина, контакты/state restore.
- [x] Source-preservation: материалы/тени/быт/одежда/зеркало/пороги,
  балкон/курение/City cut/изоляция внешних эффектов прежние.
  Review `HomeBathroomMirrorPlayModeTests`, `HomeBalconySmokingInteractionPlayModeTests`.

### 32. Бар — Current: сохранение

- [x] `Bar` Passed: вход/стойка/табуреты/booths, лица/руки/посудa/опоры,
  питьё и герой у караоке просмотрены. Actual WC envelope/2 стола и допустимая
  counter-замена Bottle→ShotGlass учтены в fixture, runtime не менялся.
- [x] Source-preservation: посещаемость/подача/уборка, взгляды/камеры/WC/порог/
  свет, часы/речь/service ownership прежние.

### 33. Горное кафе — Current: сохранение

- [x] `MountainRoadNativeLighting` Passed: kit/стойка/стулья/кухня/персонажи/
  меню/приборы/отражения у терминала; материалы/контакты сохранены.
- [x] Source-preservation: review `CounterSeat_FocusesConfirmsAndRetrievesWorldMenu`;
  цикл без нового заказа/покупки/обслуживания.

### 34. Лес дороги и деревни — Current: сохранение

- [x] Native road/village силуэты/стволы/ветер просмотрены;
  `ForestCrownAuthoredPilot` дал roadside/branch/tip/underside/wind A/B.
- [x] Новая ромбовидная крона со светлой гранью ухудшила силуэт:
  кандидат удалён, исходный лес сохранён; rollout в деревню не нужен.
  Bounds/материал/ветровые UV/коллизия/lifetime были сохранены;
  визуального выигрыша нет, ускорение не измерялось.

### 35. Жилая деревня и рабочие дворы — Current

- [x] `AlpineVillageNativeLighting`: улица/фасад матери/workroom materials
  просмотрены. `VillageWorkroomWalls` после terrain-fix прошёл:
  пики у стены убраны, actual-triangle/floor/exterior controls сохранены.
- [x] Source-preservation: cavity ограничена plinth; наружный terrain/снег,
  walk mask и dock/contact owners прежние. Остальные ворота/запасы/распорядок/
  помощь/identities/session state сохранены; свежий прогон этих действий не выполнялся.

### 36. Деревенские общественные комплексы — Current: сохранение

- [x] `AlpineVillageAbandonment`: front/yard/rear ратуши/школы/пекарни,
  мастерской/спасателей просмотрены; функции/силуэты/снег/опоры читаются,
  метрические модели/пассивность сохранены.
- [x] `AlpineVillageAbandonment` Passed после уточнения sheet/identity ожиданий;
  runtime население, свет и звук сохранены.

### 37. Покинутые дома, закрытые дворы и руины — Current: сохранение

- [x] `AlpineVillageAbandonment`: дневные виды домов/хозпостроек,
  ночные/gust соседские силуэты просмотрены; разрушение/снег сохранены.
- [x] `AlpineVillageAbandonment` Passed; бок outbuilding-05 просмотрен.
  Опоры/проходы, пассивность и уровень распада сохранены без новых жителей/света.

### 38. Старая дорога, обрыв, складской двор и лавина — Current: сохранение

- [x] `AlpineVillageAvalanche` Passed: подход/фронт/руина/сосед просмотрены;
  физическое закрытие и проходы сохранены.
- [x] `AlpineVillageExpansion` Passed: подъёмник/дорога/обрыв/склад/грузовик/
  стулья/погрузка/ремонт просмотрены; снег/пассивность/осмотры сохранены.

### 39. Часовня, исток, ручей, каскады и мостик — Current: сохранение

- [x] `AlpineVillageBrookDetails` прошёл: порог/труба/лоток,
  струи/перелив/малые каскады и оба подхода у часовни просмотрены.
- [x] `AlpineVillageFootbridge` прошёл: настил/берега/вода и оба подхода
  просмотрены, production-capsule прошла мостик; снег/опоры сохранены.
  Исток бытовой: без большого водопада/ритуалов/подсветки/кладбища.

### 40. Дом матери — Current: сохранение

- [x] `MothersHouseNativeLighting` Passed; гостиная/чайный стол/ниша/кресло/
  камин/диван/лестница/коридор/спальни/ванная/окна просмотрены.
  Материалы/фурнитура/починки/свет/отражения, полутень/пассивное зеркало сохранены.
- [x] Source-preservation: сидение/речь/внимание/шарф/камеры/возврат/session прежние;
  review sofa/quip/interior coverage и владельцев.

### 41. CombatTest: склад — Current: сохранение

- [x] `CombatWarehouseDetailVisuals` Passed: метры/route plans, комнаты/ворота/
  шкафы/механизмы/стыки и production/blackout кадры просмотрены; арт сохранён.
- [x] Прежние герой/оружие/camera/FOV/roll и combat owners сохранены по source
  review; no new horror rollout. Fixture учитывает actual LockerFactory,
  hero-only BodyFill и клапанный свет; runtime свет ради теста не менялся.

### 42. CombatTest: старый полигон / лаборатория — Current: сохранение

- [x] `CombatLaboratoryArtVisuals` Passed: арена/герой, тела/лица всех шести
  production-целей просмотрены; силуэты/опора/свет сохранены.
- [x] Подготовка/reset/изоляция сохранены по source review. Старые duel-grip
  и Lab second-headshot assertions не соответствуют unarmed Docker и Crane
  regeneration; их не выдали за Passed и runtime ради них не меняли.

### 43. MainMenu — Current

- [x] `MainMenuManualNative`: реальные клавиши/клики — время 07:40→02:40,
  закрытый бар/назад, оружие/режим, список лабораторных врагов, ввод/фильтр
  вокализатора и возврат. Игра/синтез не запускались.
- [x] Широкий кадр и 1280×960: список мест/вокализатор читаются, hitboxes работают.
  Hover читает общий pointer; stationary cursor не забирает keyboard focus.
  Input System only: общий pointer/capture/scroll и bounded Unicode editor.
  `InputSystemUiPlayModeTests` Passed: scaled groups, drag/scroll/focus cleanup,
  Pause/MainMenu, русский ввод/выделение/вставка, Backspace/стрелка/Delete и close;
  финальные кадры просмотрены, предупреждения Input Manager нет.

### 44. AreaLoading — Current: сохранение

- [x] `FourDirectionsAndThreeAspectRatios`: иллюстрации четырёх направлений,
  16:9/4:3/ultrawide и нижняя полоса просмотрены; арт сохранён.
- [x] `AreaTravel_ComposesEachDestinationBeforeCompleting`: overlay до готовности
  мира и cleanup пройдены; неподвижность/строки/время поездки сохранены.

### 45. Закрытие очереди и память — Current

- [x] Каждый пункт содержит изменение/сохранение, основание и именованное
  доказательство; отклонённые кандидаты не входят в runtime.
- [x] Факты current-world/overview/systems-map и README/release-notes/work-log
  обновлены. Канон и принятые архитектурные решения сохранены.
- [x] Временные кадры/видео/отчёты удалены; check-docs, diff review и
  check-workspace пройдены. Рабочие деревья сессии архивированы.

## Матрица эффектов — общий результат обзора

**Current:** общие providers/receivers и применимость сверены с
[стандартом](visual-effects-standard.md); локальные результаты — в пунктах выше.
Реальные native кадры сохраняют production stack. Отсутствие эффекта не долг;
новые речь/NPC/улики не следуют из полировки. Измеренного общего ускорения нет.

| № / effect | Coverage / disposition / proof |
| --- | --- |
| 1. GI | Existing source bounce/cache. Lodge table A/B rejected; no baked/APV/SSGI or bar SCGI rollout. |
| 2. SSAO | Shared provider retained; native contacts/thin rails/faces reviewed. |
| 3. Fog | City/road/village ranges and snow shelters retained; visibility unchanged. |
| 4. Volumetric | Existing finite source air/occlusion retained; NativeLighting. |
| 5. Shadows | Existing soft/hard/shadowless ranges; imported contacts/normals reviewed. No new Light. |
| 6. Grade | Local palette/tonemapping retained; native day/night/grayscale/interior views. |
| 7. Probes/SSR | Local cubes and City/warehouse SSR retained; same tracing/capture budgets. Road unload checked. |
| 8. Water | Basin/brook/cascades/bridge/coast views; existing type-specific normal/Fresnel/foam/reflection. |
| 9. Response | Existing pigment/roughness/wetness/metre UV; manifest maps, shared materials/MPB, shop/home views. |
| 10. Bevels | New Blender joints/profiles keep envelopes; actual triangles, manifests and close views. |
| 11. COLOR | Road RGB pilot retained; new crown rejected in A/B; no village rollout. |
| 12. Wind/contact | Existing weather/bounds/rig; live cloth strikes, feeding, door grips and bridge legs. |
| 13. Wear | Existing causal masks/atlases retained; no new projectors or clues. |
| 14. Particles | Local rain/snow/dust/smoke retained; native lighting/shelter views; population unchanged. |
| 15. Rim | Existing source-dependent response; no independent silhouette glow. |
| 16. Outlines | Existing interaction visibility/distance/owners; no permanent world outline. |
| 17. Bloom | Existing emitters/windows day/night; clock changed by pigment, not emission. |
| 18. DOF | Existing gameplay/inspection/cinematic owners; borrowed production cameras and cleanup. |
| 19. AA | Native/SMAA and preview AA retained; no TAA/history change. |
| 20. Vignette | Soft stairwell and shared state weights retained; routes/handles/corners readable. |
| 21. Grain/dither | Existing weak interior grain/crisp UI; no unproven banding or retro pass. |
| 22. Flares | URP capability only; no active flare provider or visible contribution. No new effect. |
| 23. Blur | No new motion blur: no demonstrated benefit to gameplay readability. |
| 24. CA/distortion | Shared intoxication optics/cleanup retained; native state views. |

Стоимость новых деталей ограничена общими meshes/материалами и сборкой мира;
повторные поиски/генерация в hot loop не добавлены. Лес/GI кандидаты удалены,
так как визуальная польза не доказана; оценки стоимости не выдаются за замер.

## Минимальная приёмка

FAST: адресные validator/Unity selections и просмотр кадров; широкие suites/build
не выполнялись. Имена доказательств и границы сохранения указаны по пунктам.
