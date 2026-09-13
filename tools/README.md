# Art and native tool entry points

`build-city-cannery-3d-model.py`: ten metre FBXs/manifest in `Assets/Resources/City/Cannery`,
source `ArtSource/City/Cannery`; port maps, outdoor scales, articulated `CartonStack`.
Validator: contacts/scale/truck/`.29 m` crew clearance/UVs/determinism.
`build-cannery-woman-3d-model.py`: `Woman/CanneryWoman{,Actions}.fbx`/JSONs,
wardrobe/painted-face PNGs; source/review views `ArtSource/City/CanneryWoman`.
Five clips, hero rig, mesh/atlas hashes, garment/hair clearance across phases:

```powershell
python tools/run-blender.py tools/build-city-cannery-3d-model.py --validate-only -- --validate-only
python tools/run-blender.py tools/build-cannery-woman-3d-model.py --validate-only -- --validate-only
```

`build-city-port-3d-model.py`: same launcher/`--validate-only`, nine FBXs/`CityPort3D.json` in
`Assets/Resources/City/Port`, source `ArtSource/City/Port`, grips/store/beam/
`PortAccessLayout.json`/metre UVs. Twelve `512 px` ImageGen maps (sRGB/mipmap/repeat):
originals/prompts/hashes `ArtSource/City/Port/Textures/generation.json`, outside mesh hashes;
asphalt city `12 m`. `build-city-port-foreman-3d-model.py`: `Foreman/PortForeman`
model/manifest/actions/atlas, continuous lower face/chin-jowl shapes/mouth/export validation.
`python tools/dialogue_face_atlas.py [--validate-only]`: face PNGs/manifest, no Blender.

The Ferryman's coin and glovebox contents share one small passive resource.
`build-last-route-coin-3d-model.py` preserves the earlier octagonal brass coin's
`54 x 9 mm` silhouette and combines its repeated geometry into one contained
pile mesh. Both export in bare-mesh Unity metres; runtime supplies the same
shared material and tint. The generator checks deterministic reconstruction,
outward faces and each coin's compartment/bulb clearance:

```powershell
python tools/run-blender.py tools/build-last-route-coin-3d-model.py --expect Assets/Resources/Vehicles/LastRouteCoin3D.fbx --expect Assets/Resources/Vehicles/LastRouteCoin3D.json
python tools/run-blender.py tools/build-last-route-coin-3d-model.py --validate-only -- --validate-only
```

`LastRouteCoinAssetValidation` adds the imported mesh bounds to the read-only
player-build gate. The editable source is `ArtSource/Vehicles/Blender/LastRouteCoin3D.blend`.

The final village household slice adds two isolated generators:
`build-village-outdoor-player-actions-3d-model.py` publishes thirteen optional
hero clips and matching metre-space prop tracks;
`build-village-errands-3d-model.py` publishes the household bucket and its
resident filling and station strap actions. Both use `run-blender.py`, validate real contacts and
preserve the production character models. The focused
`AreaCaptureFixture.VillageOutdoorLife` imports these banks and checks their
placed contacts, ordinary carried travel and outcomes after a scene reload.
`AreaCaptureFixture.VillageOutdoorPartners` starts with explicitly seeded basket,
porch and chair results to isolate the gate, station partner and remaining NPC
errands. Its `partners-verification.json` does not claim to verify player carrying.

Village household life uses separate deterministic prop, resident and doorway
packs. Keep Unity closed during generation; the focused
`AreaCaptureFixture.VillageLife` prebuild runs `VillageLifePropAssetSetup`,
`VillageResidentAssetSetup` and `VillageResidentDoorAssetSetup` to import metre
geometry, clips and resident prefabs. The original resident command builds the
first two people and their household bank; `--phase-two` adds the other four
people and the separate `VillageResidentLifeActions` bank:

```powershell
python tools/run-blender.py tools/build-village-life-props-3d-model.py --expect Assets/Resources/VillageLife/VillageLifeProps3D.fbx --expect Assets/Resources/VillageLife/VillageLifeProps3D.json -- --no-preview
python tools/run-blender.py tools/build-village-residents-3d-model.py --expect Assets/Resources/VillageLife/StationWorker.fbx --expect Assets/Resources/VillageLife/WoodWoman.fbx --expect Assets/Resources/VillageLife/VillageResidentActions.fbx --expect Assets/Resources/VillageLife/VillageResidentActions.json -- --no-preview
python tools/run-blender.py tools/build-village-residents-3d-model.py `
  --expect Assets/Resources/VillageLife/RepairNeighbor.fbx `
  --expect Assets/Resources/VillageLife/SewingWoman.fbx `
  --expect Assets/Resources/VillageLife/SnowNeighbor.fbx `
  --expect Assets/Resources/VillageLife/BasketVisitor.fbx `
  --expect Assets/Resources/VillageLife/VillageResidentLifeActions.fbx `
  --expect Assets/Resources/VillageLife/VillageResidentLifeActions.json -- --phase-two --no-preview
python tools/run-blender.py tools/build-village-resident-doors-3d-model.py --expect Assets/Resources/VillageLife/VillageResidentDoors3D.fbx --expect Assets/Resources/VillageLife/VillageResidentDoors3D.json -- --no-preview
```

Each generator accepts `--validate-only` (also pass the launcher's
`--validate-only`) to reconstruct and compare its recorded manifests without
publishing; retain `--phase-two` when validating the second resident bank.
That bank checks the original two people and their actions remain byte-for-byte
unchanged. The seventeen-kind prop pack pins the first eleven geometry/anchor
recipes and adds a shovel/rack, closed basket, gate posts/leaf and porch mat.
The doorway generator retains the three existing exterior envelopes, carves
real openings and validates six concealed docks behind solid vestibule turns.

Source and review images live in `ArtSource/VillageLife`. The one household
journey writes frames plus `verification.json` and
`neighbours-verification.json` to `Captures/VillageLife`; the latter extension
covers all six actual bodies, two-hand props, reserved doors, wall/prop
clearance, yielding, gusts, pause and day/night returns. Resident detail must
meet the production hero's floor; triangle counts support, but do not replace,
equal-scale rendered comparisons. The focused journey passed for both parts;
generator validation alone does not establish gameplay acceptance.

Part 3 uses three separate generators with Unity closed:

```powershell
python tools/run-blender.py tools/build-village-workroom-3d-model.py --expect Assets/Resources/VillageLife/VillageWorkroom3D.fbx --expect Assets/Resources/VillageLife/VillageWorkroom3D.json -- --no-preview
python tools/run-blender.py tools/build-village-residents-3d-model.py --expect Assets/Resources/VillageLife/VillageResidentWorkroomActions.fbx --expect Assets/Resources/VillageLife/VillageResidentWorkroomActions.json -- --workroom --no-preview
python tools/run-blender.py tools/build-village-workroom-player-actions.py --expect Assets/Resources/Player/VillageWorkroomPlayerActions.fbx --expect Assets/Resources/Player/VillageWorkroomPlayerActions.json -- --preview
```

The room uses `interior_kit`, the measured house `08` envelope, two actual
windows and finite furniture/prop hierarchies. Resident authoring preserves
the six bodies and earlier banks and records metre poses, hand contacts and
cloth/lid hinges beside nine new clips. Hero help has its own bone-only bank
and preserves the production prefab. `AreaCaptureFixture.VillageWorkroom`
imports these packs through the dedicated setups and records the focused
room journey in `Captures/VillageWorkroom`.

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
Generators supporting `--validate-only` can instead use the launcher's option
of that name. Existing direct-generator commands now also include
`--python-exit-code 1`.

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

To refresh only the hero's two cold actions from the existing production
source, without rebuilding its geometry, atlases or other 45 actions:

```powershell
python tools/run-blender.py tools/player_cold_actions.py --expect Captures/Tooling/cold-actions/PlayerCharacter3DV2Animations.fbx --expect Captures/Tooling/cold-actions/PlayerCharacter3DV2.json --expect Captures/Tooling/cold-actions/PlayerCharacter3DV2.blend -- --refresh-actions --stage-dir Captures/Tooling/cold-actions
```

This writes staging files only. It checks the source manifest, fixed lower
body, shared endpoints, continuous hand travel, evaluated arm separation and
exact cold-curve determinism, and verifies that the rig, meshes, weights and
other actions remain unchanged. Publish the staged animation bank, manifest
and Blender source together with Unity closed, preserving their `.meta` files;
then refresh `Player3DV2` through its existing asset setup.

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
