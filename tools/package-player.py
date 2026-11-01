#!/usr/bin/env python3
"""Package an existing player; never invokes Unity.

Windows: ZIP includes the entire player folder, excluding Unity debug backups.
macOS: ditto preserves the signed Universal .app and executable/link metadata.
Only explicit --sign-adhoc normalizes the internal macOS executable name and
repairs signatures in the player bundle; display/product names are preserved.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path
import plistlib
import re
import stat
import struct
import subprocess
import sys
import tempfile
import zipfile
import zlib

ROOT = Path(__file__).resolve().parents[1]
MAX_FILES = 100_000
MAX_MACHO_FILES = 512
MAX_SIGNATURE_METADATA = 64 * 1024
BLOCK = 2 * 1024 * 1024
MACHO_MAGIC = {bytes.fromhex(value) for value in (
    'feedface', 'cefaedfe', 'feedfacf', 'cffaedfe',
    'cafebabe', 'bebafeca', 'cafebabf', 'bfbafeca')}


def require(path: Path):
    if not path.is_file() or not path.stat().st_size:
        raise ValueError(f'Missing player file: {path.relative_to(ROOT)}')


def pe_x64(path: Path):
    require(path)
    with path.open('rb') as stream:
        header = stream.read(64)
        if len(header) < 64 or header[:2] != b'MZ':
            raise ValueError(f'Not a Windows PE: {path.name}')
        stream.seek(struct.unpack_from('<I', header, 0x3c)[0])
        pe = stream.read(6)
    if pe[:4] != b'PE\0\0' or len(pe) != 6 or struct.unpack_from('<H', pe, 4)[0] != 0x8664:
        raise ValueError(f'Not Windows x64: {path.name}')


def universal(path: Path):
    require(path)
    architectures = subprocess.check_output(['/usr/bin/lipo', '-archs', str(path)], text=True).split()
    if not {'x86_64', 'arm64'}.issubset(architectures):
        raise ValueError(f'Not Universal (Intel + Apple Silicon): {path.name}')


def native_bundle_executable(path: Path) -> Path:
    if path.is_file():
        return path
    with (path / 'Contents/Info.plist').open('rb') as source:
        info = plistlib.load(source)
    return path / 'Contents/MacOS' / info['CFBundleExecutable']


def inspect_player(target: str, sign_adhoc: bool = False):
    if target == 'windows':
        source = ROOT / 'Build/Windows'
        prefix = 'BarPromenade-Windows-x64'
        for name in ('BarPromenade.exe', 'UnityPlayer.dll'):
            pe_x64(source / name)
        for name in ('BarPromenade_Data/globalgamemanagers', 'BarPromenade_Data/boot.config',
                     'MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll'):
            require(source / name)
        plugins = list(source.rglob('AudioPluginIntoxicationVhs.dll'))
        if len(plugins) != 1:
            raise ValueError('Expected one Windows VHS plugin')
        pe_x64(plugins[0])
        return source, prefix, None
    source = ROOT / 'Build/macOS/BarPromenade.app'
    with (source / 'Contents/Info.plist').open('rb') as stream:
        info = plistlib.load(stream)
    executable = source / 'Contents/MacOS' / info['CFBundleExecutable']
    universal(executable)
    if not executable.stat().st_mode & 0o111:
        raise ValueError('macOS player is not executable')
    players = list(source.rglob('UnityPlayer.dylib'))
    plugins = list(source.rglob('AudioPluginIntoxicationVhs.bundle'))
    if len(players) != 1 or len(plugins) != 1:
        raise ValueError('Expected UnityPlayer.dylib and macOS VHS plugin')
    universal(players[0])
    universal(native_bundle_executable(plugins[0]))
    require(source / 'Contents/Resources/Data/globalgamemanagers')
    if sign_adhoc:
        executable = repair_adhoc_signatures(source, executable)
    subprocess.run(['/usr/bin/codesign', '--verify', '--deep', '--strict', str(source)], check=True)
    return source, source.name, executable.relative_to(source).as_posix()


def player_files(source: Path, target: str):
    files = []
    excluded = 0
    scanned = 0
    for path in source.rglob('*'):
        scanned += 1
        if scanned > MAX_FILES * 2:
            raise ValueError('Player exceeds directory-entry bound')
        if not path.is_file() and not path.is_symlink():
            continue
        relative = path.relative_to(source)
        if target == 'windows' and (path.name == '.DS_Store' or path.suffix.lower() == '.pdb' or
                any('donotship' in p.lower() or 'dontship' in p.lower() for p in relative.parts)):
            excluded += 1
            continue
        if path.is_symlink():
            if target == 'windows' or not path.resolve().is_relative_to(source.resolve()):
                raise ValueError(f'External/unsupported player link: {relative}')
        files.append((path, relative.as_posix()))
        if len(files) > MAX_FILES:
            raise ValueError('Player exceeds package file-count bound')
    files.sort(key=lambda item: item[1])
    return files, excluded


def repair_adhoc_signatures(source: Path, executable: Path):
    # Validate the complete bounded tree before any signature write. rglob does
    # not recurse into directory symlinks; player_files rejects external links.
    if source.is_symlink() or not source.resolve().is_relative_to(ROOT.resolve()):
        raise ValueError('Signing requires a local, non-symlink player bundle')
    files, _ = player_files(source, 'macos')
    info_path = source / 'Contents/Info.plist'
    info_bytes = info_path.read_bytes()
    info = plistlib.loads(info_bytes)
    name = info.get('CFBundleExecutable')
    if (not isinstance(name, str) or name in ('', '.', '..') or
            Path(name).name != name or '\\' in name or '\0' in name or
            executable != source / 'Contents/MacOS' / name or
            info_path.is_symlink() or executable.is_symlink() or
            not executable.resolve().is_relative_to(source.resolve())):
        raise ValueError('Unsafe macOS main executable or Info.plist')
    if name != 'BarPromenade':
        # codesign can include a Unicode-normalized main executable as nested
        # code in its own resource seal. An ASCII internal name avoids that
        # self-reference; the visible Russian product/display names stay intact.
        renamed = executable.with_name('BarPromenade')
        if renamed.exists() or renamed.is_symlink():
            raise ValueError('Internal macOS executable name conflicts with an existing file')
        info['CFBundleExecutable'] = renamed.name
        fmt = plistlib.FMT_BINARY if info_bytes.startswith(b'bplist') else plistlib.FMT_XML
        temporary = None
        try:
            with tempfile.NamedTemporaryFile(dir=info_path.parent, delete=False) as output:
                temporary = Path(output.name)
                os.fchmod(output.fileno(), stat.S_IMODE(info_path.stat().st_mode))
                output.write(plistlib.dumps(info, fmt=fmt, sort_keys=False))
            executable.rename(renamed)  # Retains executable mode and contents.
            try:
                os.replace(temporary, info_path)
            except OSError:
                renamed.rename(executable)
                raise
        finally:
            if temporary is not None:
                temporary.unlink(missing_ok=True)
        executable = renamed
        files, _ = player_files(source, 'macos')
    targets = set()
    native_count = 0
    for path, _ in files:
        if path.is_symlink():
            continue
        with path.open('rb') as stream:
            if stream.read(4) not in MACHO_MAGIC:
                continue
        native_count += 1
        if native_count > MAX_MACHO_FILES:
            raise ValueError('Player exceeds Mach-O signing bound')
        if path.resolve() != executable.resolve():
            targets.add(path)
        for parent in path.parents:
            if parent == source:
                break
            if parent.suffix.lower() in ('.app', '.bundle', '.framework', '.xpc'):
                targets.add(parent)

    def requirement_satisfied(path: Path):
        # Leaf integrity alone can pass with a stale Developer ID requirement;
        # the containing bundle then rejects that component when checking its seal.
        with tempfile.TemporaryFile() as metadata:
            subprocess.run(['/usr/bin/codesign', '--display', '-r-', str(path)],
                           stdout=metadata, stderr=subprocess.DEVNULL, check=True)
            if metadata.tell() > MAX_SIGNATURE_METADATA:
                raise ValueError('Code signature metadata exceeds byte bound')
            metadata.seek(0)
            text = metadata.read().decode('utf-8')
        requirement = re.search(r'^designated => (.+)$', text, re.MULTILINE)
        if requirement is None:  # An implicit requirement is regenerated by codesign.
            return True
        result = subprocess.run(['/usr/bin/codesign', '--verify', '--strict',
                                 '--test-requirement', '=' + requirement[1], str(path)],
                                stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        return result.returncode == 0

    def sign(path: Path):
        if path.is_symlink() or not path.resolve().is_relative_to(source.resolve()):
            raise ValueError('Signing target escaped the player bundle')
        command = ['/usr/bin/codesign', '--force', '--sign', '-', '--timestamp=none',
                   '--force-library-entitlements']
        metadata = 'identifier,entitlements,flags,runtime'
        subprocess.run(command + ['--preserve-metadata=' + metadata + ',requirements', str(path)], check=True)
        if not requirement_satisfied(path):
            # An ad-hoc identity cannot satisfy the previous vendor certificate
            # requirement. Regenerate only that incompatible signing policy.
            subprocess.run(command + ['--preserve-metadata=' + metadata, str(path)], check=True)
        subprocess.run(['/usr/bin/codesign', '--verify', '--strict', str(path)], check=True)
        if not requirement_satisfied(path):
            raise ValueError('Ad-hoc signature failed its designated requirement')

    # Children first; bundle resource seals depend on the final nested signatures.
    # Do not use --deep when signing or touch already valid nested components.
    for path in sorted(targets, key=lambda item: (-len(item.parts), item.as_posix())):
        verified = subprocess.run(['/usr/bin/codesign', '--verify', '--strict', str(path)],
                                  stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
        if verified.returncode or not requirement_satisfied(path):
            sign(path)
    sign(source)
    return executable


def content_crc(path: Path):
    if path.is_symlink():
        data = os.readlink(path).encode()
        return len(data), zlib.crc32(data) & 0xffffffff
    size = crc = 0
    with path.open('rb') as source:
        while data := source.read(BLOCK):
            size += len(data)
            crc = zlib.crc32(data, crc)
    return size, crc & 0xffffffff


def verify_zip(archive: Path, prefix: str, files, executable: str | None):
    total_bytes = 0
    with zipfile.ZipFile(archive) as source:
        if len(source.infolist()) > MAX_FILES * 3:
            raise ValueError('ZIP exceeds entry-count bound')
        members = {item.filename: item for item in source.infolist()}
        if len(members) != len(source.infolist()):
            raise ValueError('ZIP contains duplicate entries')
        for path, relative in files:
            member = members.get(prefix + '/' + relative)
            size, crc = content_crc(path)
            total_bytes += size
            if member is None or member.file_size != size or member.CRC != crc:
                raise ValueError(f'ZIP differs from player file: {relative}')
            if path.is_symlink() and not stat.S_ISLNK(member.external_attr >> 16):
                raise ValueError(f'ZIP lost symbolic link: {relative}')
        if executable and not members[prefix + '/' + executable].external_attr >> 16 & 0o111:
            raise ValueError('ZIP lost executable permissions')
    return total_bytes


def atomic_text(path: Path, text: str):
    path.parent.mkdir(parents=True, exist_ok=True)
    handle, name = tempfile.mkstemp(prefix=path.name + '.', dir=path.parent)
    try:
        with os.fdopen(handle, 'w') as stream:
            stream.write(text)
        os.replace(name, path)
    finally:
        Path(name).unlink(missing_ok=True)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--target', choices=('windows', 'macos'), required=True)
    parser.add_argument('--verify-only', action='store_true', help='Validate existing ZIP without writing anything')
    parser.add_argument('--sign-adhoc', action='store_true',
                        help='macOS only: normalize the internal executable name and repair ad-hoc signatures before packaging')
    args = parser.parse_args()
    if args.sign_adhoc and (args.target != 'macos' or args.verify_only):
        parser.error('--sign-adhoc requires --target macos and cannot be combined with --verify-only')
    try:
        settings = (ROOT / 'ProjectSettings/ProjectSettings.asset').read_text()
        version = re.search(r'^  bundleVersion: ([0-9A-Za-z._-]+)$', settings, re.M)
        if version is None:
            raise ValueError('Missing or unsafe player version')
        source, prefix, executable = inspect_player(args.target, args.sign_adhoc)
        label = 'Windows-x64' if args.target == 'windows' else 'macOS-Universal'
        output = ROOT / f'Build/BarPromenade-{label}-{version[1]}.zip'
        partial = output.with_suffix('.zip.partial')
        files, excluded = player_files(source, args.target)
        if not args.verify_only:
            try:
                if args.target == 'macos':
                    subprocess.run(['/usr/bin/ditto', '-c', '-k', '--sequesterRsrc', '--keepParent',
                                    '--zlibCompressionLevel', '1',
                                    str(source), str(partial)], check=True)
                else:
                    with zipfile.ZipFile(partial, 'w', zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
                        for path, relative in files:
                            archive.write(path, prefix + '/' + relative)
                player_bytes = verify_zip(partial, prefix, files, executable)
                os.replace(partial, output)
            finally:
                partial.unlink(missing_ok=True)
        else:
            player_bytes = verify_zip(output, prefix, files, executable)
        digest = hashlib.sha256()
        with output.open('rb') as stream:
            while data := stream.read(BLOCK):
                digest.update(data)
        result = dict(check='PlayerDistributionZip', target=args.target, version=version[1],
                      archive=output.relative_to(ROOT).as_posix(), zip_bytes=output.stat().st_size,
                      player_bytes=player_bytes,
                      files=len(files), excluded_debug_files=excluded, sha256=digest.hexdigest(),
                      runtime_launch_tested=False)
        if not args.verify_only:
            atomic_text(output.with_suffix('.zip.sha256'), digest.hexdigest() + '  ' + output.name + '\n')
            atomic_text(ROOT / f'Build/reports/package-{args.target}.json', json.dumps(result, indent=2) + '\n')
        print(json.dumps(result, indent=2))
        return 0
    except (OSError, ValueError, KeyError, zipfile.BadZipFile, subprocess.CalledProcessError) as error:
        print(f'Player packaging failed: {error}', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
