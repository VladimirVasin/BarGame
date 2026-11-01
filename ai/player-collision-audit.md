# Player collision audit

Срез 2026-11-01: source/reimport/focused checks и Mac `0.1.1` GUI. Corrupted
подъём повторно пройден в финальном ZIP; City cooking errors отсутствуют.
Source audit не означает прохождение десяти сцен или полных suites.

## Вывод и границы доказательства

**Подтверждённый исходный дефект:** corrupted Stairwell готовил MeshCollider
из FBX с отключённым Read/Write. Старый Mac Player сообщил `CollisionMeshData couldn't be created because
the mesh has been marked as non-accessible` для `Stage1__C-1_0__Collision` и
других Stage parts. Этот контракт уже существовал в `b699a62b`, до оптимизации
сборки. Shader stripping и последний target switch причиной не установлены.

Падение объясняет исчезновение героя/фиксированный кадр; отдельный camera/hide
дефект и Windows-воспроизведение не доказаны. Второй конфликт — City café roof
rain trigger, не пол. Последующий Mac GUI выявил третий: gazebo пытался
при создании collider готовить уже выгруженный visual batch; source audit это пропустил.

**Current:** Labyrinth/Café имеют read1 после штатного Unity reimport; read-only
build guard реализован; Mac GUI подтвердил опору на первом corrupted подъёме.
**Partial:** Windows/x86_64 Mac и прочие маршруты не пройдены.

## Почему Editor пропустил лестницу

[Labyrinth builder:50](../Assets/Scripts/Runtime/World/StairwellLabyrinthWorldBuilder.cs#L50)
добавляет MeshCollider для Collision/ShellCollision/DecorCollision и назначает
sharedMesh. В `b699a62b` импортёр задавал `isReadable=false`,
[meta:34](../Assets/Resources/Stairwell/Labyrinth/Labyrinth3D.fbx.meta#L34) —
`isReadable: 0`, `addColliders: 0`. Теперь meta содержит read1, а
[валидатор](../Assets/Scripts/Editor/Stairwell/StairwellLabyrinthAssetSetup.cs#L50)
проверяет ModelImporter-флаг и Collision/ShellCollision/DecorCollision meshes. Возможность Editor прочитать
данные и ненулевой sharedMesh не доказывают готовую физическую форму в Player;
здесь независимое свидетельство — фактическая ошибка Mac.

[PlayerMotor:648](../Assets/Scripts/Runtime/Player/PlayerMotor.cs#L648) обнуляет Y
у constrained planar delta и отдельно применяет гравитацию: XZ walkable mask
не поддерживает тело по высоте. [FootProbeSurface](../Assets/Scripts/Runtime/Core/FootProbeSurface.cs#L13)
— вспомогательные triggers стоп, не запасной пол CharacterController.
[Camera selector:175](../Assets/Scripts/Runtime/Scenes/StairwellFixedCameraController.cs#L175)
сохраняет последний кадр вне всех зон, что согласуется с падением.
[Atmosphere:226](../Assets/Scripts/Runtime/Scenes/StairwellInteriorAtmosphere.cs#L226)
меняет свет; совпадение с темнотой не доказывает скрытие тела или сбой анимации.
Общего Y/last-safe rescue в motor нет; местный Stairwell retry обрабатывает
боевое поражение. Новые телепорты/kill-plane не заменяют исправление коллизии.

## Матрица десяти gameplay roots

MainMenu, DoorTransition, AreaLoading не являются gameplay roots.

| Root | Физические источники | Результат этого аудита |
| --- | --- | --- |
| City | Terrain/ground/roads; Port/Cannery/Laundry/Fair/EastExit/Misc; plan boxes | Café read1; gazebo separate host; финальный Mac City load без cooking errors |
| MountainRoad | Terrain/roads, structural batches, café boxes | Collision CPU mesh сохранён |
| AlpineVillage | Ground/lane/brook; Village/Expansion/Life/Doors/Workroom/Narrative | Read1; fitted clones readable |
| CombatTest | Arena, Warehouse/stairs/gates/lockers/fish | Read1 |
| StairwellInterior | Обычные boxes/ramps; corrupted labyrinth meshes | Read1; Mac GUI прошёл вход и первый длинный подъём |
| BarInterior | Плановые boxes из размеров частей | Опора независима от visual mesh |
| SupermarketInterior | Floor/wall/fixture boxes | Опора независима от visual mesh |
| HomeInterior | Primitive proxies; visual subassets read1 | Конфликта этого класса нет |
| ChurchInterior | Плановые boxes, authored colliders отключены | Visual read0 не ломает пол |
| MothersHouseInterior | Floor slabs, стены, stair proxies | Опора независима от visual mesh |

Источники primitive-путей: [Bar:343](../Assets/Scripts/Runtime/Scenes/BarInteriorWorldBuilder.cs#L343),
[Supermarket:160](../Assets/Scripts/Runtime/World/SupermarketInteriorWorldBuilder.cs#L160),
[Home:42](../Assets/Scripts/Runtime/World/HomeAuthoredVisualFactory.cs#L42),
[Church:154](../Assets/Scripts/Runtime/World/ChurchInteriorWorldBuilder.cs#L154),
[MothersHouse:446](../Assets/Scripts/Runtime/World/MothersHouseInteriorWorldBuilder.cs#L446),
[обычная Stairwell ramp:388](../Assets/Scripts/Runtime/World/StairwellWorldBuilder.cs#L388).

## Импортированные физические семейства

Пути моделей относительно `Assets/`, расширение `.fbx`; read1 — наблюдаемый
флаг, не Player smoke. Проверены реальные metas и ссылки providers.

| Семейство / точные источники | Runtime consumer | Наблюдение |
| --- | --- | --- |
| `Resources/Stairwell/Labyrinth/Labyrinth3D` | [Labyrinth builder:53](../Assets/Scripts/Runtime/World/StairwellLabyrinthWorldBuilder.cs#L53) | Исходно read0 и Mac отказ; теперь read1/reimport passed |
| `MountainRoad/Cafe/Models/MountainRoadCafe3D` | [City roof shelter:367](../Assets/Scripts/Runtime/World/CityWorldBuilder.cs#L367) | Исправлен read0→1; convex trigger, не пол. [Пол кафе](../Assets/Scripts/Runtime/World/MountainRoadCafeCollisionWorldBuilder.cs#L208) — boxes |
| `City/Models/CityMisc3D` | [Gazebo:34,56](../Assets/Scripts/Runtime/World/CityParkGazeboWorld.cs#L34) | read1; новый child без MeshFilter исключает auto-cook выгруженной visual copy до назначения part.Mesh |
| `Resources/City/Port/{Dock,Trawler,CraneBase,CraneBoom,Hook,Cargo,Trolley,RopeSegment,AccessRoad}` | [Port:59](../Assets/Scripts/Runtime/World/CityPortAssetProvider.cs#L59) | Закрытый ModelNames; COL_ и DockVisible__Tare, все источники read1 |
| `Resources/City/Cannery/{Hall,Equipment,Truck,Pallet,RetortBasket,CanTray,CartonStack,Trolley,Yard,Workwear}` | [Cannery:37](../Assets/Scripts/Runtime/World/CityCanneryAssetProvider.cs#L37) | Закрытый ModelNames; COL_, read1. [Yard fitting:112](../Assets/Scripts/Runtime/World/CityCanneryAssetProvider.cs#L112) обновляет readable clone и collider вместе |
| `Resources/City/Laundry/StaticLaundry` | [Laundry:30](../Assets/Scripts/Runtime/World/CityLaundryAssetProvider.cs#L30) | read1 |
| `Resources/City/Fair/{Stall,Bench,Clutter}`; `Resources/City/FairChild/LowTable` | [Fair:131](../Assets/Scripts/Runtime/World/CityFairWorld.cs#L131), [LowTable:60](../Assets/Scripts/Runtime/World/CityFairChildren.cs#L60) | read1; эти fair placements запрашивают mesh collision |
| `Resources/City/EastExit/{CityEastExit3D,CityEastExitDressing3D}` | [Road:77](../Assets/Scripts/Runtime/World/CityEastExitWorldBuilder.cs#L77), [dressing:77](../Assets/Scripts/Runtime/World/CityEastExitDressingWorldBuilder.cs#L77) | read1; CPU fitting/combine создают доступную итоговую геометрию |
| `Village/Models/Village3D`; `Resources/VillageLife/VillageResidentDoors3D` | [Village placement:1723](../Assets/Scripts/Runtime/World/AlpineVillageWorldBuilder.cs#L1723), [house solids:1991](../Assets/Scripts/Runtime/World/AlpineVillageWorldBuilder.cs#L1991), [doors:106](../Assets/Scripts/Runtime/World/VillageResidentDoors.cs#L106) | read1; в collider идёт тот же размещённый mesh |
| `Resources/Village/Expansion/VillageExpansion3D` | [Expansion solids:138](../Assets/Scripts/Runtime/World/VillageExpansionAssetProvider.cs#L138), [bridge:112](../Assets/Scripts/Runtime/World/AlpineVillageExpansionBuilder.cs#L112) | read1; bridge/avalanche fitting не вызывает UploadMeshData(true) |
| `Resources/VillageLife/{VillageLifeProps3D,VillageWorkroom3D}`; `Resources/VillageNarrative/VillageNarrative3D` | [Life:143](../Assets/Scripts/Runtime/World/AlpineVillageLifeController.cs#L143), [woodpile:21](../Assets/Scripts/Runtime/World/AlpineVillageWoodpileBuilder.cs#L21), [workroom:84](../Assets/Scripts/Runtime/World/VillageWorkroomAssets.cs#L84), [narrative:115](../Assets/Scripts/Runtime/World/VillageNarrativeLibrary.cs#L115) | read1; включая neighbour gate |
| `Resources/Combat/Arena` | [Combat provider:171](../Assets/Scripts/Runtime/Combat/CombatAssetProvider.cs#L171) | read1; collider=true для Arena |
| `Resources/CombatWarehouse/` + [ModelNames](../Assets/Scripts/Runtime/Combat/WarehouseArenaAssetProvider.cs#L12) | [Warehouse:65](../Assets/Scripts/Runtime/Combat/WarehouseArenaAssetProvider.cs#L65) | Все перечисленные источники read1; включает shell, platforms/stair, rail/rack/cargo, gates/fan/lift/pusher, props, signs и fish station |
| `Resources/CombatWarehouse/BrokenStair`; `GateChain/{GatePlatformLeft,GatePlatformRight,ValveChain}`; `Lockers/WarehouseLocker` | [Broken stair:36](../Assets/Scripts/Runtime/Combat/WarehouseBrokenStairFactory.cs#L36), [gate:29](../Assets/Scripts/Runtime/Combat/WarehouseGateAssets.cs#L29), [locker:37](../Assets/Scripts/Runtime/Combat/WarehouseLockerFactory.cs#L37) | read1; каждый путь сохраняет импортированную единицу масштаба |
| `Resources/CombatWarehouse/FishAssembly/{FishHead,FishTail,FishBelly,FishPlaySurface,WarehouseGateKey,AssembledFish}`; `FishLampBurst/LampShards` | [Fish:113](../Assets/Scripts/Runtime/Combat/WarehouseFishAssemblyAssets.cs#L113), [shards:68](../Assets/Scripts/Runtime/Combat/WarehouseFishLamp.Burst.cs#L68) | read1; static/convex по назначению, shards не пол |

Исключения: read0 CraneRails/Bridge/Hook/WristSocket получают только visual
presentation в [Crane provider:354](../Assets/Scripts/Runtime/Combat/CombatCraneAssetProvider.cs#L354);
OffshoreBoats не получают MeshCollider. CityBuildings/Church/BarFacade/
Supermarket/транспорт используют primitive physics. Сам по себе read0 не дефект.

## Процедурная геометрия, cooking и трансформации

[ProjectSettings:101](../ProjectSettings/ProjectSettings.asset#L101):
`bakeCollisionMeshes: 0`. Physics.BakeMesh, custom cookingOptions и
сериализованные MeshCollider в Assets не найдены. Cooking-поля mesh subassets
не доказывают готовую форму будущего runtime collider.

Mac `0.1.1`, session `201a4ae3b55b442dac18df7bd4aa40bf`, seq26–33:
`CollisionMeshData` errors для `Masonry_Stone`, `Residential_Timber`,
`Masonry_Timber`, `Street_PaintedMetal Combined Mesh`. Это gazebo: основание/ступени,
deck/балки, колонны, крыша/ограждение. Старый Build сначала освобождал CPU data
visual batch, затем добавлял MeshCollider на тот же host. Unity
[автоматически назначает MeshFilter mesh](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Physics.BakeMesh.html)
при добавлении collider; последующее `sharedMesh=part.Mesh` не предотвращало ошибку.
Потеря конечной опоры не доказана: final source readable. Теперь collider создаётся
на identity child без MeshFilter с тем же source/layer/tag/FootstepGround ancestry;
visual CPU copy по-прежнему освобождается. Importer guard этот runtime lifecycle не покрывает.

| Производитель | Доступность и время жизни |
| --- | --- |
| [RuntimePrimitiveFactory:537–548](../Assets/Scripts/Runtime/Core/RuntimePrimitiveFactory.cs#L537), [второй batch:682–694](../Assets/Scripts/Runtime/Core/RuntimePrimitiveFactory.cs#L682) | UploadMeshData получает `!collider && !keepReadable`; физический batch остаётся readable, mesh принадлежит RuntimeGeneratedMeshOwner |
| [Cylinder source:1000](../Assets/Scripts/Runtime/Core/RuntimePrimitiveFactory.cs#L1000) | Shared procedural cylinder остаётся readable; поздние MeshCollider в City POI не зависят от импортированного read0 |
| [CityGroundSurfaceSystem:582](../Assets/Scripts/Runtime/World/CityGroundSurfaceSystem.cs#L582), CityTerrainSurfaceWorldBuilder, CityMountainBoundaryMeshFactory, CityChurchWorldBuilder/GroundWorldBuilder, CityEntranceForecourtWorldBuilder | Новые meshes; нет освобождения CPU-копии перед физическим назначением. Явные uploads физических поверхностей сохраняют чтение |
| [MountainRoad terrain:464](../Assets/Scripts/Runtime/World/MountainRoadWorldBuilder.cs#L464), [общий mesh host:1351](../Assets/Scripts/Runtime/World/MountainRoadWorldBuilder.cs#L1351) | Новый mesh назначается в collider без UploadMeshData(true) |
| [MountainRoadStructureDetails:123](../Assets/Scripts/Runtime/World/MountainRoadStructureDetails.cs#L123) | Старый box mesh остаётся физическим и сохраняет owner; UploadMeshData(true) относится только к новой визуальной оболочке |
| [Alpine ground:646](../Assets/Scripts/Runtime/World/AlpineVillageWorldBuilder.cs#L646), [lane:1603](../Assets/Scripts/Runtime/World/AlpineVillageWorldBuilder.cs#L1603), [brook:500](../Assets/Scripts/Runtime/World/AlpineVillageBrookBuilder.cs#L500) | Новые readable meshes; chunks не заменяют физическую опору выгруженной визуальной копией |
| [CitySandTreading:60](../Assets/Scripts/Runtime/World/CitySandTreading.cs#L60) | Явно требует отдельный неизменяемый collision mesh; деформирует только visual skin |

UploadMeshData(true) у puddles/water/vista/home exterior/inventory preview/
player shadow относится к visuals; позднего физического назначения не найдено.
Imported wrappers сохраняют FBX unit scale. Неравномерные Village/Expansion
placements используют readable sources; отрицательный Village roof snow scale
визуальный. Все pose-dependent degeneracies не проверены; Player отказ по ним не установлен.

## Герой, CPU-чтение и переходы

Фактический read1 проверен у
[PlayerCharacter3DV2](../Assets/Player3D/V2/Models/PlayerCharacter3DV2.fbx.meta#L34),
[ScarfWorn](../Assets/Resources/Player/Scarf/ScarfWorn.fbx.meta#L34),
[ScarfFolded](../Assets/Resources/Player/Scarf/ScarfFolded.fbx.meta#L34),
[StationWorker](../Assets/Resources/VillageLife/StationWorker.fbx.meta#L34),
а также Village3D/Expansion, MountainRoadMisc, CityLitter, BarServiceProps.
Дополнительного определённого CPU/import конфликта hero/scarf/NPC не найдено.
[Foot probe:209](../Assets/Scripts/Runtime/Player3D/Player3DFootGroundProbe.cs#L209)
читает собственный результат SkinnedMeshRenderer.BakeMesh; это не Physics.BakeMesh
и не подготовка collision уровня. Editor-only ветки в Player/Player3D,
объясняющие этот сбой, не обнаружены.

[Interaction cleanup](../Assets/Scripts/Runtime/Interaction/PlayerAnimatedInteractionController.cs#L1139),
[CompositionDriver](../Assets/Scripts/Runtime/Core/CompositionDriver.cs#L188),
[AreaTravelService](../Assets/Scripts/Runtime/Core/AreaTravelService.cs#L455)
освобождают ownership/композицию и восстанавливают исходную сцену при ошибке.
Новая утечка не установлена; неготовая native collision shape не обязана
превращаться в исключение загрузки.

## Покрытие, исправление и следующая проверка

**Существующее покрытие:** [CityMisc](../Assets/Tests/EditMode/CityMiscAssetTests.cs#L624),
[MountainRoadMisc](../Assets/Tests/EditMode/MountainRoadMiscAssetTests.cs#L266)
и [Warehouse](../Assets/Scripts/Editor/CombatWarehouseAssetValidation.cs#L180)
проверяют readability своих источников. Прежний общий build gate не покрывал
каждый runtime MeshCollider; café primitive test не охватывает City rain trigger.
[Stairwell capture](../Assets/Tests/PlayMode/AreaCaptureFixture.cs#L3140) проверяет
физический путь в Editor;
[puzzle/flashlight test](../Assets/Tests/PlayMode/AreaCaptureFixture.StairwellCorruption.cs#L259)
перемещает героя к controls, пропуская вход от квартиры. Ни один не заменяет
воспроизведение дефекта в Player.

**Current — выполненные исправления и proof:**

- `RuntimeMeshReadabilityValidation.ReimportCollisionFixes` завершился exit0:
  штатный reimport Labyrinth/Café и проверка всех **136** записей registry прошли.
  Обе `.fbx.meta` теперь read1; ручной правки metas не было.
- [Read-only guard](../Assets/Scripts/Editor/RuntimeMeshReadabilityValidation.cs#L35)
  подключён к `PlayerBuildAssetValidation`; проверяет ModelImporter Read/Write
  и наличие/nonempty/readable mesh subassets.
  Отчёт называет source/consumer/repair, выводит первые16 failures и total;
  нет vertex read, bake, stamps или скрытого ремонта. Registry явно ограничен
  audited CPU/cooking sources, включая закрытые combat damage/wound/body sets.
- Пройдены **13** параметризованных cases:
  `StairwellLabyrinth_RequiresPlayerReadableCollisionWithoutRepairingAssets` и
  `RuntimeMeshReadability_RejectsPlayerMissingDataWithBoundedReadOnlyDiagnostics`.
  Отдельный `AuthoredCafe_UsesPlayerReadableImporter` также прошёл.
- Широкий `AuthoredCafe_IsExactPassivePrefabWithSharedGlass` после importer checks
  упал в `AssertPassiveHeroMenuBindings`: **0.193204597**, expected `<0.0001`.
  FBX/manifest/prefab/runtime registry равны HEAD; meta отличается лишь read0→1.
  Несвязанный menu-grip mismatch не исправлялся; baseline replay не заявляется.

Геометрия/свет/камера/сюжет сохранены. Финальные Mac/Windows `0.1.1` build и
package verification пройдены; промежуточные ZIP заменены.
Старые player folders/ZIP `0.1.0` удалены по просьбе пользователя, отчёты сохранены.

**MacStairwellAscent passed (финальный ZIP, Apple Silicon):** Debug City →
MainMenu → Debug Home → настоящий сон → corrupted ApartmentDoor → UpperFlight
(Y4.339) → UpperLanding (Y4.84) → orbit camera → первый длинный марш → площадка.
Герой поддержан, отвечает на обратный ход; камера следует. Затемнение входа
сохранено. В этой сессии нет error/CollisionMeshData; City load также завершился.
Идентификатор сессии/пакета и source hashes: `Build/reports/macos-collision-route.json`.

Пройден focused
`ParkBandstand_KeepsSourceCollisionSeparateFromReleasedVisualMeshes`:
source/visual lifetime, transforms, footsteps и raycast deck/ступеней.
Остались City warnings `JobTempAlloc` о времени жизни allocation; владелец не локализован.
Это не подтверждение потери пола; отдельный memory/profile разбор не выполнен.

Windows traversal и прочие маршруты не проверены. Build/ZIP/Editor proof не
заменяет Player traversal; source audit не означает полные suites.

## Стоимость и сохранение художественного контракта

Read/Write сохраняет CPU-копию: прирост памяти двух моделей не измерен.
Область ограничена реальными cooking/CPU consumers; blanket read1, дублирование
коллизий и общий prebake не предлагаются. Guard работает при сборке, не в кадре;
runtime owners и readable upload сохраняются. Gazebo добавляет четыре collider
hosts при создании, без recurring work и удержания visual CPU copies. Ускорение не заявлено.

Влияние на все категории [effects standard](visual-effects-standard.md):
матрица не утверждает, что все эффекты активны во всех сценах.

| Группа / все категории | Изменение, предел стоимости, доказательство |
| --- | --- |
| 1–6: GI/SSGI/SCGI, SSAO, height/depth fog, volumetric light, shadows, grading/tonemapping | Настройки/провайдеры/свет прежние; новых pass нет. Player proof проверяет опору на пороге света |
| 7–16: probes/SSR, water, roughness/wetness, bevels/normals, vertex gradients, wind/interaction, decals, ambient particles, rim/Fresnel, outlines | Геометрия/response прежние. Восстанавливается café rain shelter; новых эффектов нет |
| 17–24: bloom, DOF, AA, vignette, grain/dither, flares, motion blur, chromatic aberration/distortion | Настройки и camera owners прежние; новых GPU passes и неподтверждённого camera fix нет |

Соблюдены [сюжет](city-story-bible.md#16-законы-сюжета) и
[арт](city-zones-art-bible.md#16-художественные-acceptance-checks): восстановлен
разрешённый проход; нового события/текста/обхода загадок нет. Исключений не требуется.
