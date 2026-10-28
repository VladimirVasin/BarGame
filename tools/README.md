# Art/native tools

Home:`build-home-interior-3d-model.py`+`home_interior_remodel.py`/`interior_kit`;
`--validate-only`; maps `build-home-textures.py --room-only`.
Bar:`build-bar-3d-model.py`/`bar_interior_unique_surfaces.py`: `--validate-only`;
`check-bar-interior-surfaces.py --validate-only`:FBX UV0/1.
Church:`build-church-3d-model.py`+`church_metric_geometry.py`/kit:
`--validate-only`;`build-church-textures.py`:albedo/physical response.
Garden:`build-church-garden-3d-model.py`:geometry/contacts/response/determinism;
staged `--model-dir`/`--source-dir`/`--texture-dir`, `--no-preview`.
`Church{,Garden}AssetSetup.BuildOrThrow`:import/bounds/materials.

`build-city-east-{exit,guards}-3d-model.py`: `--near-only`, `--distance-only`
(`city_east_distance.py`), `--dressing-only`; `Assets/Resources/City/EastExit`,
source `ArtSource/City/EastExit`.
`build-city-east-ground-texture.py --verify`: grass/soil seam.

`build-city-buildings-3d-model.py` v4.0.0;
`Assets/City/Models/CityBuildings3D.{fbx,json}`;
import `CityBuildingAssetSetup.BuildOrThrow`.
`build-city-coastal-infrastructure-3d-model.py` v1.0.0;
`Assets/Resources/City/CoastalInfrastructure/CityCoastalInfrastructure3D.{fbx,json}`;
`CityCoastalInfrastructureAssetSetup.{Build,Validate}OrThrow`.
Both:launcher/`--validate-only`.

`build-city-street-profile-3d-model.py`:launcher/`--validate-only`;
`Assets/Resources/City/StreetProfile/CityStreetProfile3D.{fbx,json}`,
source:`ArtSource/City/StreetProfile`. Passive top/edge topology follows
`CityStreetSupportPlan`; `CityStreetSurfaceWorldBuilder` measures imported metres.

`build-city-litter-3d-model.py`:36 props;`--validate-only`:geometry/determinism.

`build-city-fair-3d-model.py`:10 props;`--verify-fbx`.
`build-city-fair-child-3d-model.py`: child base/3 outfits/toys.
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

`build-city-port-3d-model.py`:launcher/`--validate-only`;9 FBXs/`CityPort3D.json`→
`Assets/Resources/City/Port`, source `ArtSource/City/Port`;grips/store/beam/
`PortAccessLayout.json`/metre UVs. 12×512px ImageGen maps (sRGB/mipmap/repeat):
originals/prompts/hashes:`ArtSource/City/Port/Textures/generation.json`;mesh hashes separate;
city asphalt12m. `build-city-port-foreman-3d-model.py`:`Foreman/PortForeman`
model/manifest/actions/atlas/lower face/chin-jowl/mouth/export validation.
`dialogue_face_atlas.py [--validate-only]`:face PNGs/manifest;no Blender.

`build-last-route-coin-3d-model.py`: `54 x 9 mm` brass coin/glovebox pile.
Launcher/`--validate-only`:determinism/winding/compartment-light clearance.
`Assets/Resources/Vehicles/LastRouteCoin3D.{fbx,json}`;
source `ArtSource/Vehicles/Blender/LastRouteCoin3D.blend`.
`LastRouteCoinAssetValidation`:build-gate import bounds.

Village: `build-village-{outdoor-player-actions,errands}-3d-model.py` via `run-blender.py`.
Checks: `VillageOutdoorLife` (contacts/carry/reload), `VillageOutdoorPartners` (completion).
`build-village-expansion-3d-model.py`:houses/yards/lodge/stove/flue/warehouse/bridge;
`VillageExpansionAssetSetup` imports `Assets/Resources/Village/Expansion/VillageExpansion3D.{fbx,json}`.
`--validate-only`:bounds/winding/openings/determinism;`--preview-kind`:views.
`build-village-unique-surfaces.py`:4K/2K;rebuild expansion/workroom/life/narrative. `build-village-surface-textures.py [--validate-only]`:
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

`build-ground-surface-{maps,shader}.py --check`:read-only response/URP parity,
albedos kept. Generate→`VillageJunctionTextureSetup.BuildOrThrow`: both
junction atlases; recheck upstream changes.

House:`build-mothers-house-interior-3d-model.py` via launcher;
`build-mothers-house-textures.py`:4K+2K/aux1K;both `--validate-only`.
`build-mothers-house-shader.py --check`:URP parity;packs/prompts/hashes:
`ArtSource/MothersHouse/Textures/Source/generation.json`.

`python tools/build-player-{clothing,body-hair}-surfaces.py [--check]`: UV maps/regions/hash;
`ArtSource/Player/player-{clothing,body-hair}-surfaces.json`.

Close Unity. Order:hero source→`build-default-npc-3d-model.py`→
`refresh-default-npc-damage.py` (launcher)→`DefaultNpcAssetSetup`.
Residents:WoodWoman/actions;`--phase-two`:four;prebuild imports props/doors.

```powershell
python tools/run-blender.py tools/build-village-life-props-3d-model.py --expect Assets/Resources/VillageLife/VillageLifeProps3D.fbx --expect Assets/Resources/VillageLife/VillageLifeProps3D.json -- --no-preview
python tools/run-blender.py tools/build-default-npc-3d-model.py --expect Assets/Resources/VillageLife/StationWorker.fbx --expect Assets/Resources/VillageLife/StationWorker.json -- --no-preview
python tools/run-blender.py tools/build-village-residents-3d-model.py --expect Assets/Resources/VillageLife/WoodWoman.fbx --expect Assets/Resources/VillageLife/VillageResidentActions.fbx --expect Assets/Resources/VillageLife/VillageResidentActions.json -- --no-preview
python tools/run-blender.py tools/build-village-residents-3d-model.py `
  --expect Assets/Resources/VillageLife/RepairNeighbor.fbx `
  --expect Assets/Resources/VillageLife/SewingWoman.fbx `
  --expect Assets/Resources/VillageLife/SnowNeighbor.fbx `
  --expect Assets/Resources/VillageLife/BasketVisitor.fbx `
  --expect Assets/Resources/VillageLife/VillageResidentLifeActions.fbx `
  --expect Assets/Resources/VillageLife/VillageResidentLifeActions.json -- --phase-two --no-preview
python tools/run-blender.py tools/build-village-resident-doors-3d-model.py --expect Assets/Resources/VillageLife/VillageResidentDoors3D.fbx --expect Assets/Resources/VillageLife/VillageResidentDoors3D.json -- --no-preview
```

Validate:`--validate-only`;retain `--phase-two` and earlier bodies/actions.
`Bar Promenade/Default NPC/Rebuild Ordinary Worker`:12 face-hair atlases;
Inspector:faces/hair/presets/slots.
Register new IDs/constraints in `DefaultNpcPopulation`, use `CreateForCharacter`;
global assignment avoids repeats and restores the same look across loads.
Props: 17 recipes, first eleven preserved. Doors: three envelopes, real openings,
six concealed docks behind vestibule turns.

Source:`ArtSource/VillageLife`;review:`Captures/VillageLife`.
`VillageLife`:bodies/props/doors/clearance/yielding/gusts/pause/returns.
Judge at hero scale, not by counts.

Workroom(close Unity):launcher + `build-village-workroom-3d-model.py`,
`build-village-residents-3d-model.py --workroom`,
`build-village-workroom-player-actions.py --preview`.
Room:kit/house08/two windows; six residents/prior banks/nine clips;
hero bone bank retained. `AreaCaptureFixture.VillageWorkroom`:imports/journey.

Frost:ImageGen/prompt/hash/linear import;runtime reveal/tint:
[alpine-cold-frost-mask.md](alpine-cold-frost-mask.md).

Windows art/toolchain pins:

```powershell
python tools/toolchain.py --scope all
```

`python -m pip install Pillow==12.3.0 numpy==2.4.4`.
`BP_BLENDER`/`--blender`:same pinned build; native Windows:MSVC/SDK.
Mac:`native.macos` pins Python/NumPy/clang/SDK;
`python3 tools/toolchain.py --scope native`.

Repo root;`--`:args. Launcher:Python pin/startup/errors/fresh `--expect`/JSON;
both launcher/generator:`--validate-only`.
Ordinary: `principal_npc_detail.py` (fish/mother), `service_npc_detail.py`
(cashier/driver). `npc_detail_geometry.py` shares shapes;
`npc_detail_atlas.py`: neutral cloth/leather/hair. Cashier atlas/bizarre variants stay.

Empty `Captures/Tooling`:repeat `--stage-output=--model-dir=Assets/path`;
other args kept;`--expect`:destinations. Publish checked/Unity closed;
keep `.meta`/rollback;import non-atomic.

VHS:Windows `tools/audio-vhs/build.ps1`;Mac `tools/audio-vhs/build-macos.py`.
Both validate ABI/DSP;compile-only retains temporary output.
See [audio-vhs/README.md](audio-vhs/README.md).

`build-player-3d-model-v2.py`:31core+12hair;`--surfaces-only`:UV
`--hand-grip-only --skip-animation-export --no-previews`:grips/`hand_grip.gestures`;
`player_hand_gestures.validate`:shapes/FBX.
`build-player-bar-repeat-actions.py`:wave/wrist;`validate`.
`build-player-karaoke-actions-3d-model.py`:43-bone Enter/Loop/Exit, planted feet,
neutral/seam/determinism checks; `PlayerKaraokeActionAssetSetup` checks imports.
`run-blender.py`: Python pin by host; installed Blender, no version/hash pin.

`build-combat-{test,blood,pistol,shotgun,gore,body,skull,zombie,crane}-3d-model.py`:
test:--simple-strike-only/--actions-only+--kick-only/--recovery-only/--footwork-only;
--refresh-published-contracts/--reuse-unchanged-actions/--resume-npc-bank file.blend;
blood:--texture-only;all:--validate-only;gore:hero/NPC body-skull.
Pistol: `run-blender.py tools/build-combat-pistol-3d-model.py --validate-only -- --normal-pilot-only`;
`Slide__Steel` normals only;geometry/UV/contacts/action bank kept.
`build-combat-crane-surfaces.py`:sculpt/2K+1K.
`build-combat-{room,imprint,long-exposure}-{3d-model,damage-3d-model}.py`:body/surfaces/damage.
`build-combat-room-attack-3d-model.py`:clips/tableau.
`--pointing-only`/`--npc-only`/`--tableau-only`/`--validate-only`;after body rebuild.

`build-combat-{firearm-walk,weapon-carry}-3d-model.py`→`Combat{FirearmWalk,WeaponCarry}`;
8gaits/1.6m/C2feet/pelvis/200Hz;holster-belt-sling/6draw-stow.
`build-combat-warehouse-*`/`build-warehouse-*-3d-model.py`:kit/fish/key-chain/glass/locker.
`python -B tools/warehouse-layout-export/export.py`→kit→maps→shader;
`--prepare-signs`;`--validate-only`/`--check`;4K+2K/BC7mips<28MiB;world-metrejoins.
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
