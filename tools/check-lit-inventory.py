#!/usr/bin/env python3
"""Offline, conservative Bar Promenade/Lit inventory; never strips variants.

Usage: python tools/check-lit-inventory.py [--output Build/reports/lit-inventory.json]
Exit 0 means the registered lexical inventory matches, not semantic completeness.
Exit 2 requests review of changed/unknown input; all variants remain retained.
No Unity invocation, build callback, cache access or Assets writes are performed.
"""
from __future__ import annotations

import argparse
import hashlib
import json
import os
from pathlib import Path, PurePosixPath
import re
import sys
import tempfile

MAX_FILES = 16000
MAX_FILE_BYTES = 2 * 1024 * 1024
MAX_TOTAL_BYTES = 64 * 1024 * 1024
MAX_JSON_BYTES = 1024 * 1024
MAX_DIAGNOSTICS = 32
MAX_INCLUDES = 128
GUID = re.compile(r'^guid:\s*([0-9a-f]{32})\s*$', re.M)
SHADER_GUID = re.compile(r'^\s*m_Shader:\s*\{[^}\n]*guid:\s*([0-9a-f]{32})[^}\n]*\}', re.M)
INCLUDE = re.compile(r'^\s*#\s*include(?:_with_pragmas)?\s+')
INCLUDE_LITERAL = re.compile(r'^\s*#\s*include(?:_with_pragmas)?\s*["<]([^">]+)[">]')
LOCAL_PRAGMA = re.compile(r'^\s*#\s*pragma\s+shader_feature_local\w*\s+(.+)$')
KEYWORD_PATTERNS = {
    'keyword_call': re.compile(r'\b\w*Keyword\w*\s*\('),
    'keyword_state': re.compile(r'\b(?:shaderKeywords|enabledKeywords|keywordSpace|LocalKeyword|GlobalKeyword|ShaderKeyword\w*)\b'),
}
MATERIAL_PATTERNS = {
    'material_create': re.compile(r'\bnew\s+Material\s*\('),
    'material_shader_assignment': re.compile(r'\.\s*shader\s*=(?!=)'),
    'material_copy': re.compile(r'\bCopyPropertiesFromMaterial\s*\('),
    'material_or_shader_load': re.compile(r'\b(?:Load|LoadAll|LoadAsync)\s*<\s*(?:Material|Shader)\s*>'),
    'material_helper': re.compile(r'\bMaterial(?:\s*\[\s*\])?\s+\w+\s*\('),
    'material_clone': re.compile(r'\bInstantiate\s*<\s*Material\s*>'),
    'default_material': re.compile(r'\bRuntimePrimitiveFactory\s*\.\s*DefaultMaterial\b'),
    'shader_find': re.compile(r'\bShader\s*\.\s*Find\s*\('),
}


def relative_path(value: str) -> str:
    path = PurePosixPath(value)
    if not value or path.is_absolute() or '..' in path.parts or '\\' in value:
        raise ValueError('Inventory paths must be project-relative POSIX paths')
    return path.as_posix()


def inside(root: Path, value: str) -> Path:
    path = root / relative_path(value)
    if not path.resolve().is_relative_to(root.resolve()):
        raise ValueError('Input escapes project root')
    return path


def code_mask(text: str) -> str:
    """Mask comments/string/char literals, preserving offsets; lexical, not C# parsing.

    Ordinary, verbatim and raw strings are masked. Interpolated expressions are
    deliberately opaque, so this does not prove helper/data-flow completeness.
    """
    chars = list(text)
    index, length = 0, len(text)
    while index < length:
        start = index
        if text.startswith('//', index):
            end = text.find('\n', index)
            index = length if end < 0 else end
        elif text.startswith('/*', index):
            end = text.find('*/', index + 2)
            index = length if end < 0 else end + 2
        elif text[index] in ('"', "'"):
            quote = text[index]
            raw = len(text[index:]) - len(text[index:].lstrip('"')) if quote == '"' else 0
            if raw >= 3:
                end = text.find('"' * raw, index + raw)
                index = length if end < 0 else end + raw
            else:
                verbatim = quote == '"' and index > 0 and text[index - 1] == '@'
                index += 1
                while index < length:
                    if verbatim and text.startswith('""', index):
                        index += 2
                    elif not verbatim and text[index] == '\\':
                        index += 2
                    elif text[index] == quote:
                        index += 1
                        break
                    else:
                        index += 1
        else:
            index += 1
            continue
        for offset in range(start, min(index, length)):
            if chars[offset] not in ('\n', '\r'):
                chars[offset] = ' '
    return ''.join(chars)


class Reader:
    def __init__(self, root: Path):
        self.root, self.cache, self.total_bytes = root, {}, 0

    def read(self, name: str) -> str:
        name = relative_path(name)
        if name not in self.cache:
            path = inside(self.root, name)
            size = path.stat().st_size
            if size > MAX_FILE_BYTES or self.total_bytes + size > MAX_TOTAL_BYTES:
                raise ValueError('Inventory input exceeds per-file/total byte bound')
            with path.open('rb') as source:
                data = source.read(MAX_FILE_BYTES + 1)
            if len(data) > MAX_FILE_BYTES or self.total_bytes + len(data) > MAX_TOTAL_BYTES:
                raise ValueError('Inventory input grew beyond byte bound')
            self.total_bytes += len(data)
            self.cache[name] = (data.decode('utf-8-sig'), hashlib.sha256(data).hexdigest())
        return self.cache[name][0]

    def digest(self, name: str) -> str:
        self.read(name)
        return self.cache[name][1]


def source_sites(text: str) -> list[dict]:
    masked = code_mask(text)
    rows = []
    for kind, pattern in {**KEYWORD_PATTERNS, **MATERIAL_PATTERNS}.items():
        for match in pattern.finditer(masked):
            line = text.count('\n', 0, match.start()) + 1
            rows.append(dict(kind=kind, line=line))
    return sorted(rows, key=lambda row: (row['line'], row['kind']))


def material_keywords(text: str) -> tuple[str, ...]:
    match = re.search(r'^([ \t]*)m_ValidKeywords:[ \t]*([^\n]*)', text, re.M)
    if match:
        inline = match.group(2).strip()
        if inline == '[]':
            return ()
        if inline:
            raise ValueError('Unsupported m_ValidKeywords inline YAML; review required')
        keys = []
        for line in text[match.end():].splitlines():
            if not line.strip():
                continue
            item = re.fullmatch(r'[ \t]*-\s+([A-Za-z_][A-Za-z_0-9]*)\s*', line)
            if not item:
                break
            keys.append(item.group(1))
        return tuple(sorted(set(keys)))
    legacy = re.search(r'^\s*m_ShaderKeywords:\s*(.*)$', text, re.M)
    if legacy:
        return tuple(sorted(set(legacy.group(1).split())))
    raise ValueError('Material keyword serialization unavailable; review required')


def shader_inputs(reader: Reader, shader_path: str):
    pending, found, external, opaque, local_keywords = [shader_path], {}, set(), [], set()
    while pending:
        name = pending.pop()
        if name in found:
            continue
        if len(found) >= MAX_INCLUDES:
            raise ValueError('Shader include graph exceeds bound')
        try:
            text = reader.read(name)
        except FileNotFoundError:
            opaque.append(dict(path=name, reason='missing local include'))
            continue
        found[name] = reader.digest(name)
        for line, masked in zip(text.splitlines(), code_mask(text).splitlines()):
            if LOCAL_PRAGMA.match(masked):
                local_keywords.update(token for token in LOCAL_PRAGMA.match(line).group(1).split()
                                      if re.fullmatch(r'[A-Za-z_][A-Za-z_0-9]*', token) and token != '_')
            if not INCLUDE.match(masked):
                continue
            match = INCLUDE_LITERAL.match(line)
            if not match:
                opaque.append(dict(path=name, reason='nonliteral include'))
                continue
            target = match.group(1)
            if target.startswith('Packages/'):
                external.add(relative_path(target))
            else:
                candidate = reader.root / target if target.startswith('Assets/') else inside(reader.root, name).parent / target
                try:
                    local_name = candidate.resolve().relative_to(reader.root.resolve()).as_posix()
                except ValueError:
                    opaque.append(dict(path=name, reason='include escapes project'))
                else:
                    pending.append(relative_path(local_name))
    return found, sorted(external), opaque, sorted(local_keywords)


def load_inventory(path: Path) -> dict:
    if path.stat().st_size > MAX_JSON_BYTES:
        raise ValueError('Registered inventory exceeds JSON bound')
    data = json.loads(path.read_text(encoding='utf-8'))
    if data.get('schema_version') != 1 or data.get('variant_policy') != 'retain_all':
        raise ValueError('Unsupported inventory schema/policy')
    for name in ('shader_path',):
        relative_path(data[name])
    for group in ('keyword_owners', 'material_helpers', 'shader_inputs', 'context_inputs'):
        for name, row in data[group].items():
            relative_path(name)
            if not re.fullmatch(r'[0-9a-f]{64}', row['sha256']):
                raise ValueError('Invalid source fingerprint')
    return data


def inspect(root: Path, inventory: dict) -> dict:
    reader = Reader(root)
    diagnostics, diagnostic_count = [], 0

    def diagnostic(kind, path):
        nonlocal diagnostic_count
        diagnostic_count += 1
        if len(diagnostics) < MAX_DIAGNOSTICS:
            diagnostics.append(dict(kind=kind, path=relative_path(path)))

    shader = inventory['shader_path']
    meta = GUID.search(reader.read(shader + '.meta'))
    if not meta or meta.group(1) != inventory['shader_guid']:
        diagnostic('shader_guid_changed', shader + '.meta')
    shader_sources, external, opaque, declared = shader_inputs(reader, shader)
    for row in opaque:
        diagnostic(row['reason'], row['path'])

    assets = inside(root, 'Assets')
    files = sorted(path.relative_to(root).as_posix() for path in assets.rglob('*')
                   if path.is_file() and path.suffix in ('.cs', '.mat'))
    if len(files) > MAX_FILES:
        raise ValueError('Inventory file count exceeds bound')
    materials, owners, helpers = [], {}, {}
    for name in files:
        if name.endswith('.mat'):
            text = reader.read(name)
            match = SHADER_GUID.search(text)
            if match and match.group(1) == inventory['shader_guid']:
                try:
                    keywords = material_keywords(text)
                except ValueError:
                    diagnostic('unparsed_material_keywords', name)
                    materials.append(dict(path=name, keywords=None))
                else:
                    materials.append(dict(path=name, keywords=list(keywords)))
            continue
        # Scan runtime C# anywhere under Assets, not only the current Runtime folder.
        if 'Editor' in PurePosixPath(name).parts or 'Tests' in PurePosixPath(name).parts:
            continue
        sites = source_sites(reader.read(name))
        if not sites:
            continue
        kinds = sorted({row['kind'] for row in sites})
        row = dict(sha256=reader.digest(name), sites=sites, kinds=kinds)
        if any(kind in KEYWORD_PATTERNS for kind in kinds):
            owners[name] = row
        if any(kind in MATERIAL_PATTERNS for kind in kinds):
            helpers[name] = row

    def compare(group, current):
        registered = inventory[group]
        for name in sorted(set(current) | set(registered)):
            if name not in registered:
                diagnostic('unregistered_' + group, name)
            elif name not in current:
                diagnostic('missing_' + group, name)
            elif current[name] != registered[name]['sha256']:
                diagnostic('changed_' + group, name)

    compare('keyword_owners', {name: row['sha256'] for name, row in owners.items()})
    compare('material_helpers', {name: row['sha256'] for name, row in helpers.items()})
    compare('shader_inputs', shader_sources)
    contexts = {}
    for name in inventory['context_inputs']:
        try:
            contexts[name] = reader.digest(name)
        except FileNotFoundError:
            pass
    compare('context_inputs', contexts)
    if external != inventory['external_package_includes']:
        diagnostic('changed_external_include_set', shader)

    actual_sets = sorted({tuple(row['keywords']) for row in materials if row['keywords'] is not None})
    registered_sets = {tuple(row['keywords']) for row in inventory['static_local_sets']}
    for keywords in actual_sets:
        if keywords not in registered_sets:
            diagnostic('unregistered_static_local_set', next(row['path'] for row in materials if row['keywords'] == list(keywords)))
    for keywords in sorted(registered_sets - set(actual_sets)):
        diagnostic('missing_static_local_set', shader)
    extensions = inventory['runtime_local_sets']
    for row in extensions:
        for owner in row['owners']:
            if owner not in inventory['keyword_owners']:
                raise ValueError('Runtime extension owner must be an explicitly registered keyword owner')
    observed_union = sorted(set(actual_sets) | {tuple(row['keywords']) for row in extensions})
    static_rows = [dict(keywords=list(keys), materials=[row['path'] for row in materials if row['keywords'] == list(keys)])
                   for keys in actual_sets]
    return dict(schema_version=1, shader=shader, review_status='needs_review' if diagnostic_count else 'matched',
                variant_policy='retain_all', removal_candidates=[],
                scope='Lexical project scan with reviewed snapshots; not C# semantics or a stripping allowlist.',
                counts=dict(materials=len(materials), static_sets=len(actual_sets), keyword_owners=len(owners),
                            material_helpers=len(helpers), diagnostics=diagnostic_count,
                            diagnostics_omitted=max(0, diagnostic_count - MAX_DIAGNOSTICS)),
                diagnostics=diagnostics, static_local_sets=static_rows,
                runtime_local_sets=extensions, evidenced_local_set_lower_bound=[list(keys) for keys in observed_union],
                keyword_owners={name: {**row, 'review': inventory['keyword_owners'].get(name, {}).get('review', 'unregistered')}
                                for name, row in owners.items()},
                material_helpers=helpers, shader_inputs=shader_sources,
                external_package_includes=external, declared_local_keywords=declared,
                context_inputs=contexts, limitations=inventory['limitations'])


def write_report(root: Path, path: Path, report: dict):
    path = path if path.is_absolute() else root / path
    destination = path.resolve()
    report_root = (root / 'Build' / 'reports').resolve()
    if not report_root.is_relative_to(root.resolve()) or not destination.is_relative_to(report_root) or destination.suffix != '.json':
        raise ValueError('Output must be a JSON file under Build/reports')
    data = (json.dumps(report, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False) + '\n').encode('utf-8')
    if len(data) > MAX_JSON_BYTES:
        raise ValueError('Report exceeds JSON byte bound')
    destination.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(prefix='.lit-inventory-', dir=destination.parent, delete=False) as output:
            temporary = Path(output.name)
            output.write(data)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, destination)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--root', type=Path, default=Path(__file__).resolve().parent.parent)
    parser.add_argument('--inventory', default='tools/lit-keyword-inventory.json', help='Project-relative reviewed input; never updated automatically')
    parser.add_argument('--output', type=Path, help='Optional JSON report under Build/reports; otherwise summary only')
    args = parser.parse_args(argv)
    try:
        root = args.root.resolve()
        inventory = load_inventory(inside(root, args.inventory))
        report = inspect(root, inventory)
        if args.output:
            write_report(root, args.output, report)
        counts = report['counts']
        print(f"Lit inventory {report['review_status']}: {counts['materials']} materials, {counts['static_sets']} static sets, "
              f"{counts['keyword_owners']} keyword owners; retain_all, no stripping.")
        for row in report['diagnostics']:
            print(f"Review: {row['kind']}: {row['path']}")
        if counts['diagnostics_omitted']:
            print(f"Further diagnostics omitted: {counts['diagnostics_omitted']}")
        return 2 if counts['diagnostics'] else 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        # No absolute paths or source contents are emitted by failures.
        print('Lit inventory failed: ' + type(error).__name__ + '; check input schema, paths and bounds.', file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
