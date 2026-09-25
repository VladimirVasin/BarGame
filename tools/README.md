# Art and native tool entry points

`build-city-east-{exit,guards}-3d-model.py`; exit selectors:
`--near-only`, `--distance-only` (`city_east_distance.py`), `--dressing-only`.
`Assets/Resources/City/EastExit`; source `ArtSource/City/EastExit`.
`build-city-east-ground-texture.py --verify`: grass/soil seam.

`build-city-litter-3d-model.py`: 36 props; `--validate-only`: geometry/determinism.

`build-city-fair-3d-model.py`: 10 props; `--verify-fbx`.
`build-city-fair-child-3d-model.py`: child base/3 outfits/toys.
FBXs/JSON: `Assets/Resources/City/{Fair,FairChild}`;
source: `ArtSource/City/{Fair,FairChild}`.
`build-city-fair-{player,child}-actions-3d-model.py`: 6 hero/15 child clips;
`Assets/Resources/Player/CityFairPlayerActions`,
`Assets/Resources/City/FairChild/ChildActions` (`.fbx/.json`).
Use `run-blender.py`/`--validate-only`.

`build-city-cannery-3d-model.py`: ten metre FBXs/JSON in `Assets/Resources/City/Cannery`,
source `ArtSource/City/Cannery`; port maps/outdoor scales/`CartonStack`;
contacts/scale/truck/`.29 m` ordinary crew clearance/UVs/determinism.
`build-cannery-{woman,receiver}-3d-model.py`: `Woman/CanneryWoman{,Actions}` and
`Receiver/CanneryReceiver{,Actions}` FBX/JSONs, wardrobe/painted-face PNGs;
source/reviews `ArtSource/City/Cannery{Woman,Receiver}`. Five clips/shared rig,
mesh/atlas hashes, layered clothes; woman hair/receiver glasses contacts.
Validate each:

```powershell
python tools/run-blender.py tools/build-cannery-receiver-3d-model.py --validate-only -- --validate-only
```

`build-city-port-3d-model.py`: same launcher/`--validate-only`, nine FBXs/`CityPort3D.json` in
`Assets/Resources/City/Port`, source `ArtSource/City/Port`, grips/store/beam/
`PortAccessLayout.json`/metre UVs. Twelve `512 px` ImageGen maps (sRGB/mipmap/repeat):
originals/prompts/hashes `ArtSource/City/Port/Textures/generation.json`, outside mesh hashes;
asphalt city `12 m`. `build-city-port-foreman-3d-model.py`: `Foreman/PortForeman`
model/manifest/actions/atlas, continuous lower face/chin-jowl shapes/mouth/export validation.
`python tools/dialogue_face_atlas.py [--validate-only]`: face PNGs/manifest, no Blender.

`build-last-route-coin-3d-model.py`: `54 x 9 mm` brass coin/glovebox pile.
`run-blender.py`/`--validate-only`: determinism, winding, compartment/light
clearance. Outputs: `Assets/Resources/Vehicles/LastRouteCoin3D.{fbx,json}`;
source `ArtSource/Vehicles/Blender/LastRouteCoin3D.blend`.
`LastRouteCoinAssetValidation` measures import bounds at the build gate.

Village: `build-village-{outdoor-player-actions,errands}-3d-model.py` via `run-blender.py`.
Checks: `VillageOutdoorLife` (contacts/carry/reload), `VillageOutdoorPartners` (completion).
`build-village-expansion-3d-model.py`: houses/yards/lodge/stove/flue/warehouse/bridge;
`VillageExpansionAssetSetup` imports `Assets/Resources/Village/Expansion/VillageExpansion3D.{fbx,json}`.
`--validate-only`: bounds/winding/openings/determinism; `--preview-kind`: views.
`build-lodge-wood-textures.py [--validate-only]`: 7 RGB ImageGen maps;
`Assets/Resources/Village/Textures/LodgeWood`; originals/prompts/hashes:
`ArtSource/Village/LodgeWood/generation.json`. `village_lodge_wood.py`: member
UVs/roles; `LodgeWoodAppearance`: shared material/metre pitch.
`build-village-narrative-3d-model.py`: 30 props; same launcher/check.
`Assets/Resources/VillageNarrative/VillageNarrative3D.{fbx,json}`;
source `ArtSource/VillageNarrative`. `VillageNarrativeLibrary`: IDs 1–32 except
26/27, `Create/GetBounds/GetAnchor`; importer `VillageNarrativeAssetSetup`.
`build-village-abandonment-textures.py [--validate-only]`: 3 aged maps.
`Bar Promenade/Village`: `Bake Junction Textures` keeps masks;
`Regenerate Junction Masks And Bake` resets them; both bake albedos/atlas.
R blends asphalt; G: reference only, no mesh/snow effect.

Close Unity for Blender. Worker: body/wardrobe, `DefaultNpcAssetSetup`.
Residents: WoodWoman/shared actions; `--phase-two`: other four.
`VillageLife` prebuild imports residents/props/doors.

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

Validate: launcher/generator `--validate-only`, keep `--phase-two`.
Worker retains five other bodies/actions; later banks retain earlier ones.
`Bar Promenade/Default NPC/Rebuild Ordinary Worker`: worker/12 face-hair atlases;
Inspector selects faces/hair/presets/slots.
Register new IDs/constraints in `DefaultNpcPopulation`, use `CreateForCharacter`;
global assignment avoids repeats and restores the same look across loads.
Props: 17 recipes, first eleven preserved. Doors: three envelopes, real openings,
six concealed docks behind vestibule turns.

Sources: `ArtSource/VillageLife`; captures: `Captures/VillageLife`.
`VillageLife`: bodies, held props, doors/clearance, yielding, gusts, pause/returns.
Compare at hero scale; counts do not prove quality.

Part 3 uses three separate generators with Unity closed:

```powershell
python tools/run-blender.py tools/build-village-workroom-3d-model.py --expect Assets/Resources/VillageLife/VillageWorkroom3D.fbx --expect Assets/Resources/VillageLife/VillageWorkroom3D.json -- --no-preview
python tools/run-blender.py tools/build-village-residents-3d-model.py --expect Assets/Resources/VillageLife/VillageResidentWorkroomActions.fbx --expect Assets/Resources/VillageLife/VillageResidentWorkroomActions.json -- --workroom --no-preview
python tools/run-blender.py tools/build-village-workroom-player-actions.py --expect Assets/Resources/Player/VillageWorkroomPlayerActions.fbx --expect Assets/Resources/Player/VillageWorkroomPlayerActions.json -- --preview
```

Room: `interior_kit`, measured house `08`, two windows/finite furniture/props.
Residents: retain six bodies/earlier banks; nine clips/metre poses/hand contacts/
cloth-lid hinges. Hero: own bone-only bank, unchanged production prefab.
`AreaCaptureFixture.VillageWorkroom`: dedicated imports/room journey in `Captures/VillageWorkroom`.

The image-generated Alpine frost texture is a fixed source asset; its final
prompt, hash and linear-mask import settings are recorded in
[alpine-cold-frost-mask.md](alpine-cold-frost-mask.md). Runtime only reveals and
tints that bitmap; it does not generate images.

`toolchain.json` records the supported tool versions. Check the local installation
without producing assets:

```powershell
python tools/toolchain.py --scope all
```

Python packages can be installed at the exact versions in the config with
`python -m pip install Pillow==12.3.0 numpy==2.4.4`. `BP_BLENDER` or the launcher's
`--blender` option can select another installation of the same pinned Blender
build. The native launcher locates the pinned MSVC tools and SDK rather than
silently using the newest installed compiler.

Use the common launcher from the repository root and name the outputs that
prove the requested generation completed. Arguments following `--` belong to
the original generator:

```powershell
python tools/run-blender.py tools/build-city-pedestrian-3d-model.py --expect Assets/Pedestrians/Models/CityPedestrian3D.fbx --expect Assets/Pedestrians/Models/CityPedestrian3D.json -- --archetype lampshade --no-preview
```

The launcher checks the pinned Python/Blender, resets Blender startup state,
enables nonzero exit codes for Python exceptions, propagates failure and checks
that each expected file was refreshed and is nonempty (JSON must parse).
Generators supporting `--validate-only` can use the launcher's option of that name.

For generators with output-directory flags, repeat
`--stage-output=--model-dir=Assets/path` (and the corresponding source, texture,
animation and other output flags). Redirect **every output directory used by
that invocation**; other generator arguments/defaults remain unchanged. Each
mapped directory gets an empty staging directory in `Captures/Tooling`, and
every `--expect` must belong to one of those mapped destinations. Only after
the generator and all expected outputs pass are files published. Existing
`.meta` files are preserved; replacement failure rolls back already published
files. This is file replacement with rollback, not a transaction visible to a
running Unity importer: keep Unity closed during staged publication.

The native command `tools/audio-vhs/build.ps1` validates the staged DLL before
publishing it. `-Validate` remains compatible; `-CompileOnly` leaves its output
in `Captures` and does not publish. See [audio-vhs/README.md](audio-vhs/README.md).

`build-player-3d-model-v2.py`: M-65/skin/hair; 31 body/12 hair bones.
`--hand-grip-only --skip-animation-export --no-previews` refreshes hand shapes.
Output: `Assets/Player3D/V2/Models/PlayerCharacter3DV2.{fbx,json}`.

`build-combat-{test,blood}-3d-model.py`: launcher/`--validate-only`.
Test: `--actions-only` builds combat/rise banks; `--reuse-unchanged-actions`
checks curve hashes; `--resume-npc-bank <checkpoint.blend>` resumes saved
NPC actions. Blood: `--texture-only`.
`Assets/Resources/{Combat,CombatBlood}`.

`player_jacket_cloth.py --write` derives hem/cuff metadata only; `--check` verifies it.
Refresh `Player3DV2` through its asset setup. Lower-body changes also require
`build-home-toilet-seated-3d-model.py`: trousers supply fabric, anatomy skin.
`player_cold_actions.py --refresh-actions --stage-dir
Captures/Tooling/cold-actions` stages an isolated cold refresh through the same
launcher. It checks endpoints, fixed lower body, hand travel, arm clearance and
determinism. Publish bank/manifest/Blender source with Unity closed; preserve
`.meta` files and refresh `Player3DV2`.

Pipeline failure/rollback regressions use synthetic files and a mocked Blender
process, without generating art or compiling native code:

```powershell
python tools/test_asset_pipeline.py
```

The shower's independent bone-only action bank uses the production hero rig:

```powershell
python tools/run-blender.py tools/build-home-shower-curtain-actions.py --expect Assets/Resources/Player/HomeShowerCurtainActions.fbx --expect Assets/Resources/Player/HomeShowerCurtainActions.json -- --preview
python tools/run-blender.py tools/build-home-shower-curtain-actions.py --validate-only -- --validate-only
```

Keep Unity closed while publishing the bank. Its generator checks neutral
entry/exit, fixed pelvis/feet, moving grip contact and both arms against the
evaluated body meshes throughout each action at 60 Hz. The second command
rebuilds and compares the manifest without publishing. Unity's
`Bar Promenade/Player 3D/Validate Shower Curtain Actions` additionally checks
the imported clips against the production Idle pose and world grip targets.

The mother's ordinary scarf has its own folded and worn Blender pack. Its
45 cm rear tail uses a 6 by 20 cloth grid; the wrap and knot retain their
authored silhouette, and the `MouthLowered` shape opens the actual face.
Both forms sample the existing mother's-house `BookCloth` atlas tile.

```powershell
python tools/run-blender.py tools/build-player-scarf-3d-model.py --expect Assets/Resources/Player/Scarf/ScarfWorn.fbx --expect Assets/Resources/Player/Scarf/ScarfFolded.fbx --expect Assets/Resources/Player/Scarf/PlayerScarf3D.json
python tools/run-blender.py tools/build-player-scarf-3d-model.py --validate-only -- --validate-only --no-preview
```

Keep Unity closed during publication. The first command also renders front,
lowered and rear views under `ArtSource/PlayerScarf`; append `-- --no-preview`
to skip those images. The validator checks weights, exact geometry/shape
determinism and clearance against the real production face. In Unity,
`PlayerScarfAssetSetup.BuildOrThrow` imports and measures the two FBXs without
generating geometry; player builds call its read-only `ValidateOrThrow` gate.
