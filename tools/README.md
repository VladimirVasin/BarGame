# Art/native tools

`build-stairwell-labyrinth-3d-model.py`:`--validate-only`;puzzles/gates/abyss/metres.
Home:`build-home-{interior-3d-model,textures,corruption-textures,furniture-finishes}.py`;
`build-surface-damage-shader.py --check`.
Bar:`build-bar-3d-model.py --validate-only [--geometry-only]`;
`check-bar-interior-surfaces.py --validate-only`:FBX UV0/1.
Church:`build-church-3d-model.py`+`church_metric_geometry.py`/kit:
`--validate-only`;`build-church-textures.py`:albedo/physical response.
Garden:`build-church-garden-3d-model.py`:geometry/contacts/response/determinism;
staged `--model-dir`/`--source-dir`/`--texture-dir`, `--no-preview`.
`Church{,Garden}AssetSetup.BuildOrThrow`:import/bounds/materials.
`build-pawnshop-{3d-model,finishes}.py --validate-only`:models/fitted sheets.
`PawnshopAssetSetup.EnsureAssetsReady`:import/bounds/materials.
Bench:`build-city-bench-{access-stair,pigeon-statue,flowerbed}-3d-model.py`

`build-city-east-{exit,guards}-3d-model.py`: EastExit in Resources/City and ArtSource/City.
Exit: `--near-only`, `--distance-only`, `--dressing-only`.
`build-east-checkpoint-finishes.py --check`: atlas; `build-city-east-ground-texture.py --verify`: verge.

`build-city-{buildings,coastal-infrastructure,street-profile,terrace,misc}-3d-model.py`:
launcher/`--validate-only`. Buildings: `Assets/City/Models/CityBuildings3D.{fbx,json}`;
`CityBuildingAssetSetup.BuildOrThrow`. Coast: `Resources/City/CoastalInfrastructure`;
`CityCoastalInfrastructureAssetSetup.{Build,Validate}OrThrow`.
`City/StreetProfile`: Blender tops/edges/curbs for `CityStreetSupportPlan`.
Terraces: `Assets/Resources/CityTerraces`. `build-city-hillside-{house,bridge}-3d-model.py`:
FBXs/JSON→`Assets/Resources/City/Hillside`;house `--model GlazedStairBay`.

`build-city-litter-3d-model.py`:36 props;`--validate-only`:geometry/determinism.
`build-city-laundry-3d-model.py --validate-only`:metres/sign+SHA.
`build-city-residential-courtyard-3d-model.py`:6;`--validate-only`.

`build-city-fair-3d-model.py`:10 props;`--verify-fbx`.
`build-city-fair-child-3d-model.py`: 3 outfits/toys; `--body-only` updates child art.
FBXs/JSON: `Assets/Resources/City/{Fair,FairChild}`;
source: `ArtSource/City/{Fair,FairChild}`.
`build-city-fair-{player,child}-actions-3d-model.py`:6 hero/15 child clips;
`Assets/Resources/{Player/CityFairPlayerActions,City/FairChild/ChildActions}.{fbx,json}`;
launcher/`--validate-only`.

`build-city-cannery-3d-model.py`:10 FBXs/JSON→`Assets/Resources/City/Cannery`;
source:`ArtSource/City/Cannery`;port maps/scale/`CartonStack`/contacts/truck/
`.29m` clearance/UVs/determinism.
`build-cannery-{woman,receiver}-3d-model.py`: `Woman/CanneryWoman{,Actions}` and
`Receiver/CanneryReceiver{,Actions}` FBX/JSONs, wardrobe/painted-face PNGs;
source/reviews `ArtSource/City/Cannery{Woman,Receiver}`. Five clips/shared rig,
mesh/atlas hashes/layered clothes/hair-glasses contacts;launcher/`--validate-only`.

`build-city-port-3d-model.py`: launcher/`--validate-only`; FBX/JSON/wear →
`{Assets/Resources,ArtSource}/City/Port`; `PortAccessLayout.json`.
Port ImageGen 512px: `ArtSource/City/Port/Textures/generation.json`; asphalt 12m.
`trawler_finishes.py [--check]`: 2K paint/response before Blender export.
`build-city-port-foreman-3d-model.py`: `Foreman/PortForeman` mesh, actions,
atlas, chin/jowls and mouth checks.
`dialogue_face_atlas.py [--validate-only]`:face PNGs/manifest;no Blender.

Sedan/coin/actions: `build-last-route-{car,coin}-3d-model.py`,
`build-last-route-cabin-actions.py` (`--validate-only`); `LastRouteCabinAssetSetup`.
Bus: `build-city-bus-textures.py --verify`, `build-city-bus-3d-model.py`, `CityBusAssetSetup`.

Village: `build-village-{outdoor-player-actions,errands}-3d-model.py` via `run-blender.py`.
Checks: `VillageOutdoorLife` (contacts/carry/reload), `VillageOutdoorPartners` (completion).
`build-village-expansion-3d-model.py`:houses/yards/lodge/stove/flue/warehouse/bridge;
`VillageExpansionAssetSetup` imports `Assets/Resources/Village/Expansion/VillageExpansion3D.{fbx,json}`.
`--validate-only`:bounds/winding/openings/determinism;`--preview-kind`:views.
`build-village-unique-surfaces.py`: 4K/2K expansion/workroom/life/narrative; `--unmapped-only` for non-atlas expansion edits.
`build-village-surface-textures.py [--validate-only] [--bakery-only]`:
BC7≤96MiB. `build-village-surface-shader.py --check`: Lit/wind parity.
`ArtSource/Village/Surfaces/Source/generation.json`.
`VillageSurfaceTextureSetup.BuildOrThrow`:junction bake/masks kept/hash/gate/publish.
`build-village-narrative-3d-model.py`:30 props;launcher/check;
`Assets/Resources/VillageNarrative/VillageNarrative3D.{fbx,json}`;
source:`ArtSource/VillageNarrative`. `VillageNarrativeLibrary`:IDs1–32 except26/27,
`Create/GetBounds/GetAnchor`;import:`VillageNarrativeAssetSetup`.
`Bar Promenade/Village`:`Bake Junction Textures` keeps masks;
`Regenerate Junction Masks And Bake` resets them; both bake albedo/response atlases.
R:asphalt incl.under snow;G:reference only,no geometry/depth effects.

`build-ground-surface-{maps,shader}.py --check`:response/URP parity.
`build-city-surface-textures.py --ground-grain`:City asphalt/concrete response.
`VillageJunctionTextureSetup.BuildOrThrow`:rebake both junction atlases.

House:`build-mothers-house-interior-3d-model.py` via launcher;
`build-mothers-house-textures.py`:4K+2K/aux1K;both `--validate-only`.
`build-mothers-house-shader.py --check`:URP parity;packs/prompts/hashes:
`ArtSource/MothersHouse/Textures/Source/generation.json`.

`python tools/build-player-{clothing,body-hair}-surfaces.py [--check]`: maps/regions/hash; clothing refreshes model binding first.
`ArtSource/Player/player-{clothing,body-hair}-surfaces.json`.

Close Unity. Hero export → portraits, scarf, lamp and
`refresh-default-npc-damage.py --hero` → `HeroDerivedAssetSetup.RebuildBatch`.
Worker: `DefaultMaleAnatomy.blend` → `refresh-default-npc-damage --npc-head`.
Both rebuild Walks. Staging: `--model-dir` / `--source-dir`.
Residents: WoodWoman/actions; `--phase-two`: two bodies and shared actions. House 08 uses the default cast.

```powershell
python tools/run-blender.py tools/build-village-life-props-3d-model.py --expect Assets/Resources/VillageLife/VillageLifeProps3D.fbx --expect Assets/Resources/VillageLife/VillageLifeProps3D.json -- --no-preview
python tools/run-blender.py tools/build-default-npc-3d-model.py --expect Assets/Resources/VillageLife/StationWorker.fbx --expect Assets/Resources/VillageLife/StationWorker.json -- --no-preview
python tools/run-blender.py tools/build-village-residents-3d-model.py --expect Assets/Resources/VillageLife/WoodWoman.fbx --expect Assets/Resources/VillageLife/VillageResidentActions.fbx --expect Assets/Resources/VillageLife/VillageResidentActions.json -- --no-preview
python tools/run-blender.py tools/build-village-residents-3d-model.py `
  --expect Assets/Resources/VillageLife/SnowNeighbor.fbx `
  --expect Assets/Resources/VillageLife/BasketVisitor.fbx `
  --expect Assets/Resources/VillageLife/VillageResidentLifeActions.fbx `
  --expect Assets/Resources/VillageLife/VillageResidentLifeActions.json -- --phase-two --no-preview
```

Validate:`--validate-only`;retain `--phase-two` and earlier bodies/actions.
`build-default-npc-woman-3d-model.py` uses launcher/`--validate-only`.
Unity `Default NPC/Rebuild Ordinary Worker` / `Rebuild Ordinary Woman`.
Stable looks via `DefaultNpcPopulation` + `CreateForCharacter`.
Props:17 recipes,first eleven kept. `build-village-resident-doors-3d-model.py`:three envelopes,real openings,
six concealed docks behind vestibule turns.

`ArtSource/VillageLife`;temporary review:`Captures/VillageLife`.
`VillageLife`:bodies/props/doors/clearance/yield/gust/pause/return;judge hero scale.

Workroom(close Unity):launcher + `build-village-workroom-3d-model.py`,
`build-village-residents-3d-model.py --workroom`,
`build-village-workroom-player-actions.py --preview`.
Room:kit/house08/two windows; six residents/prior banks/nine clips;
hero bone bank retained. `AreaCaptureFixture.VillageWorkroom`:imports/journey.

Frost:ImageGen/prompt/hash/linear import;runtime reveal/tint:
[alpine-cold-frost-mask.md](alpine-cold-frost-mask.md).

Art pins:`python tools/toolchain.py --scope all`.
`python -m pip install Pillow==12.3.0 numpy==2.4.4`.
`BP_BLENDER`/`--blender`:Blender;Windows:MSVC/SDK.
Mac:`native.macos`:Python/NumPy/clang/SDK;`python3 tools/toolchain.py --scope native`.

Desktop:[workflow](../ai/windows-build-optimization-plan.md).
Repo root;`--`:args. Launcher:Python/errors/fresh `--expect`/JSON;
`--validate-only`:both.
Grass:`build-city-grass-3d-model.py`;`build-city-grass-shader.py --check`.
NPC:`principal_npc_detail.py`/`service_npc_detail.py`/`npc_detail_geometry.py`.
Cashier:`supermarket_cashier_geometry.py`.
Shop:`supermarket_surface_response.py --validate-only`, `build-supermarket-shop-finishes.py --check`.

`Captures/Tooling`:repeat `--stage-output=--model-dir=Assets/path`/other args kept;
Publish checked/Unity closed;keep `.meta`/rollback;non-atomic.

VHS:Windows `tools/audio-vhs/build.ps1`;Mac `tools/audio-vhs/build-macos.py`.
Both:ABI/DSP;compile-only keeps temporary output.
[audio-vhs/README.md](audio-vhs/README.md).

`build-player-3d-model-v2.py`: `--{surfaces,hand-grip,corruption,smoking,bed,strides}-only`;
`--skip-animation-export --no-previews`;`player_hand_gestures.validate`:FBX.
`build-player-corruption-portraits.py`:views.
`build-player-bar-repeat-actions.py`:wave/wrist;`validate`.
`build-player-karaoke-actions-3d-model.py`:43-bone Enter/Loop/Exit, planted feet,
neutral/seam/determinism; `PlayerKaraokeActionAssetSetup`:imports.

`build-combat-{test,blood,pistol,shotgun,gore,body,skull,zombie,crane}-3d-model.py`:
test:--{simple-strike,actions,kick,recovery,footwork}-only;
--{refresh-published-contracts,reuse-unchanged-actions}/--resume-npc-bank file.blend;
blood:--texture-only;all:--validate-only;gore:hero/NPC body-skull.
Pistol:`--validate-only -- --normal-pilot-only`;`Slide__Steel` normals only.
`build-combat-crane-surfaces.py`:2K+1K.
`build-combat-degraded-zombie-3d-model.py`/degraded_zombie_geometry.py:--textures-only
`combat_degraded_zombie_audio.py`: `--validate-only`.
`build-combat-{room,imprint,long-exposure}-{3d-model,damage-3d-model}.py`:body/surfaces/damage.
`build-combat-room-attack-3d-model.py`:clips/tableau.
`--{pointing,npc,tableau,validate}-only`;after rebuild.

`build-combat-{firearm-walk,weapon-carry}-3d-model.py`→`Combat{FirearmWalk,WeaponCarry}`;
8gaits/1.6m/C2feet/pelvis/200Hz;holster-belt-sling/6draw-stow.
`build-combat-warehouse-*`/`build-warehouse-*-3d-model.py`:kit/fish/key-chain/glass/locker.
`warehouse-layout-export/export.py`→kit→maps→shader;Mac/Win;
`--target-framework net8.0`:SDK8(defaultnet10.0).
`--prepare-signs`/`--validate-only`/`--check`;4K+2K/BC7mips<28MiB/metrejoins.
`build-player-flashlight-3d-model.py`:clip/lens/metres;
`CombatFlashlight{AssetSetup,AssetProvider}`;CombatTest Lchest.

`player_jacket_cloth.py --write` derives hem/cuff metadata only; `--check` verifies it.
Refresh `Player3DV2` via asset setup. Lower-body changes require
`build-home-toilet-seated-3d-model.py`: trousers supply fabric, anatomy skin.
`player_cold_actions.py --refresh-actions --stage-dir Captures/Tooling/cold-actions`:
endpoint/lower-body/hand/arm/determinism checks; publish with Unity closed,
keep `.meta`, refresh `Player3DV2`.
Unity:`Tools/Bar Promenade/Player/Bake secondary motion`:hair/jacket/sling120→30Hz;
build rejects stale hashes.

Pipeline rollback:synthetic files/mock Blender;no art/native compile:

```powershell
python tools/test_asset_pipeline.py
```

Shower: own bone-only hero bank:

```powershell
python tools/run-blender.py tools/build-home-shower-curtain-actions.py --expect Assets/Resources/Player/HomeShowerCurtainActions.fbx --expect Assets/Resources/Player/HomeShowerCurtainActions.json -- --preview
python tools/run-blender.py tools/build-home-shower-curtain-actions.py --validate-only -- --validate-only
```

Checks:neutral ends/pelvis-feet/grip/posed arms at60Hz;second:manifest/no publication.
Unity `Bar Promenade/Player 3D/Validate Shower Curtain Actions`:imports/Idle/world grips.

Scarf:folded/worn Blender pack;45cm tail/6×20 cloth, authored wrap/knot;
`MouthLowered` opens the face. Both use mother's-house `BookCloth` tile.

```powershell
python tools/run-blender.py tools/build-player-scarf-3d-model.py --expect Assets/Resources/Player/Scarf/ScarfWorn.fbx --expect Assets/Resources/Player/Scarf/ScarfFolded.fbx --expect Assets/Resources/Player/Scarf/PlayerScarf3D.json
python tools/run-blender.py tools/build-player-scarf-3d-model.py --validate-only -- --validate-only --no-preview
```

Views:front/lowered/rear→`ArtSource/PlayerScarf`;`-- --no-preview` skips.
Validation:weights/geometry/shapes/determinism/face clearance.
`PlayerScarfAssetSetup`:BuildOrThrow imports/measures;ValidateOrThrow:read-only gate.

`build-hawthorn-3d-model.py`/`--validate-only`;`HawthornTinctureAssetSetup`.
