# Art and native tool entry points

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

Pipeline failure/rollback regressions use synthetic files and a mocked Blender
process, without generating art or compiling native code:

```powershell
python tools/test_asset_pipeline.py
```
