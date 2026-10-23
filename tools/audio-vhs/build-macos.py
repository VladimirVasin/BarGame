"""Build, validate and publish the universal macOS Unity audio plugin."""
from __future__ import annotations

import argparse
import hashlib
from pathlib import Path
import subprocess
import sys

TOOLS = Path(__file__).resolve().parents[1]
sys.dont_write_bytecode = True
sys.path.insert(0, str(TOOLS))

from asset_pipeline import publish_files, workspace_temporary_directory
from toolchain import ROOT, check_python, find_macos_toolchain, load_config


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--compile-only', action='store_true',
                        help='Stage a candidate under Captures without validating or publishing')
    args = parser.parse_args()
    config = load_config()
    settings = config['native']['macos']
    check_python(settings, ('numpy',))
    compiler, sdk, lipo = find_macos_toolchain(config)
    source = Path(__file__).resolve().parent / 'AudioPluginIntoxicationVhs.cpp'
    name = 'AudioPluginIntoxicationVhs.bundle'
    destination = ROOT / 'Assets/Plugins/AudioVhs/macOS' / name

    def build(output: Path) -> Path:
        output.mkdir(parents=True, exist_ok=True)
        candidate = output / name
        architectures = [argument for architecture in settings['architectures']
                         for argument in ('-arch', architecture)]
        subprocess.run([str(compiler), *architectures, '-isysroot', str(sdk),
                        '-mmacosx-version-min=' + settings['deployment_target'],
                        '-std=c++17', '-O2', '-Wall', '-Wextra', '-Werror',
                        '-fvisibility=hidden', '-ffp-contract=off', '-bundle',
                        str(source), '-o', str(candidate)], check=True)
        actual = subprocess.run([str(lipo), '-archs', str(candidate)], capture_output=True,
                                text=True, check=True, timeout=15).stdout.split()
        if set(actual) != set(settings['architectures']):
            raise RuntimeError(f'Plugin architectures {actual} do not match the pinned toolchain')
        # An ad-hoc signature permits local Apple Silicon loading, without an identity
        # or timestamp. Distribution signing remains the enclosing player's concern.
        subprocess.run(['codesign', '--force', '--sign', '-', '--timestamp=none', str(candidate)],
                       check=True, timeout=15)
        subprocess.run(['codesign', '--verify', '--strict', str(candidate)], check=True, timeout=15)
        return candidate

    if args.compile_only:
        candidate = build(ROOT / 'Captures/AudioVhs/native-build-macos')
        print(f'Compiled staging plugin only: {candidate}')
        return
    with workspace_temporary_directory('audio-vhs-macos-') as staging:
        candidate = build(staging)
        # The same shipping ABI/DSP checks as Windows run against the candidate.
        # Publication leaves the shipping plugin and its .meta intact on failure.
        subprocess.run([sys.executable, str(source.parent / 'validate.py'),
                        '--plugin', str(candidate), '--output', str(staging / 'validation')], check=True)
        publish_files([(candidate, destination)])
    print(f'Published universal macOS audio plugin: {destination}')
    print('SHA256: ' + hashlib.sha256(destination.read_bytes()).hexdigest())


if __name__ == '__main__':
    try:
        main()
    except (RuntimeError, OSError, subprocess.SubprocessError) as error:
        print(f'Native macOS audio build failed: {error}', file=sys.stderr)
        raise SystemExit(1)
