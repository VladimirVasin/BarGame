#!/usr/bin/env python3
"""Bounded, deterministic Unity -logFile report; standard library only.

Usage: python tools/report-unity-build.py --log /tmp/build.log --output /tmp/build.json
Unknown metrics are null, never zero. Pass wall times overlap and are not summed.
Comparison requires matching declared configuration/machine/cache provenance as
well as matching logged configuration and shader cache work; it makes no causal
performance claim. A configuration ID must cover settings/options not in the log.
"""
from __future__ import annotations

import argparse
from collections import Counter
import hashlib
import json
import math
import os
from pathlib import Path
import re
import sys
import tempfile

MAX_LOG_BYTES = 64 * 1024 * 1024
MAX_REPORT_BYTES = 1024 * 1024
MAX_LINE_BYTES = 1024 * 1024
MAX_ROWS = 4096
MAX_WARNINGS = 512
MAX_TEXT = 1024
NUMBER = r"[0-9]+(?:\.[0-9]+)?"
SHADER = re.compile(r'^Compiling shader "(.+)"$')
PASS = re.compile(r'^\s+Pass "(.*)" \(([^,)]+)\)$')
API = re.compile(r'^\s+Target graphics API:\s*(\S+)')
RETAINED = re.compile(r'^\s+After scriptable stripping:\s*(\d+)')
FINISHED = re.compile(
    rf'^\s+Pass (.*?) \(([^,)]+), ([^,)]+)\) finished in ({NUMBER}) seconds\. '
    rf'Local cache hits (\d+) \(({NUMBER})s CPU time\), remote cache hits (\d+) '
    rf'\(({NUMBER})s CPU time\), compiled (\d+) variants \(({NUMBER})s CPU time\), skipped (\d+) variants\.')
SHADER_WARNING = re.compile(r"^Shader warning in '([^']+)': (.+)$")
WARNING_SITE = re.compile(r'^(.*?) at (.+)\((\d+)\) \(on ([^)]+)\)$')
SUCCESS = re.compile(r'^(.+?) build succeeded:\s*(\d+) bytes,\s*(\d+) warnings\.', re.I)
ABSOLUTE_PATH = re.compile(r'(?<![\w:])(?:[A-Za-z]:[\\/]|/)[^\s\)\]\"\']+')


def safe_text(value: str) -> str:
    """Preserve project-relative diagnostics without serializing user paths."""
    value = ABSOLUTE_PATH.sub('<absolute-path>', str(value)).strip()
    if len(value) > MAX_TEXT:
        raise ValueError('Diagnostic text exceeds report bound')
    return value


def nonnegative(value):
    if isinstance(value, bool) or not isinstance(value, (int, float)) or not math.isfinite(value) or value < 0:
        raise ValueError('Invalid nonnegative metric in PlayerBuildInfo')
    return value


def bounded_lines(path: Path):
    if path.stat().st_size > MAX_LOG_BYTES:
        raise ValueError('Log exceeds 64 MiB limit')
    total = 0
    with path.open('rb') as source:
        while raw := source.readline(MAX_LINE_BYTES + 1):
            total += len(raw)
            if len(raw) > MAX_LINE_BYTES or total > MAX_LOG_BYTES:
                raise ValueError('Log grew beyond its byte/line limit')
            yield raw


def parse_log(path: Path, *, configuration_id=None, machine_id=None, cache_state='unknown') -> dict:
    configuration = dict(unity_version=None, editor_architecture=None, player_architecture=None,
                         scripting_backend=None, cli_target=None,
                         execute_method=None, graphics_apis=[], cli_flags=[])
    build = dict(success=None, result=None, duration_ms=None, size_bytes=None,
                 warning_count=None, phases_ms=None)
    shaders, warnings, parse_notes = [], {}, set()
    shader = pass_name = stage = None
    shader_ordinal = pass_ordinal = 0
    current_row = None
    command = False
    next_argument = None
    digest = hashlib.sha256()
    total_bytes = utp_count = 0

    def add_row(name, program_stage, api):
        if len(shaders) >= MAX_ROWS:
            raise ValueError('Shader row limit exceeded')
        row = dict(shader=shader, shader_ordinal=shader_ordinal, pass_name=name,
                   pass_ordinal=pass_ordinal, stage=program_stage, api=api,
                   retained_variants=None, local_cache_hits=None, remote_cache_hits=None,
                   compiled_variants=None, skipped_variants=None, wall_seconds=None,
                   local_cache_cpu_seconds=None, remote_cache_cpu_seconds=None,
                   compiled_cpu_seconds=None)
        shaders.append(row)
        return row

    def add_warning(kind, name, message, include=None, line=None, api=None):
        name = safe_text(name) if name else None
        message = safe_text(message)
        include = safe_text(include) if include else None
        key = (kind, name, include, line, api, message if include is None else None)
        if key not in warnings:
            if len(warnings) >= MAX_WARNINGS:
                raise ValueError('Unique warning limit exceeded')
            warnings[key] = dict(kind=kind, shader=name, include=include, line=line,
                                 api=api, messages=[], occurrences=0)
        entry = warnings[key]
        entry['occurrences'] += 1
        if message not in entry['messages']:
            if len(entry['messages']) >= 32:
                raise ValueError('Warning message limit exceeded')
            entry['messages'].append(message)

    for raw in bounded_lines(path):
        digest.update(raw)
        total_bytes += len(raw)
        line = raw.decode('utf-8', errors='replace').rstrip('\r\n')
        if line.startswith('Unity Editor version:'):
            configuration['unity_version'] = safe_text(line.split(':', 1)[1])
        elif line.startswith('Architecture:'):
            configuration['editor_architecture'] = safe_text(line.split(':', 1)[1])
        if line == 'COMMAND LINE ARGUMENTS:':
            command = True
            continue
        if command:
            if line.startswith('Successfully changed project path') or not line:
                command = False
            elif next_argument:
                if re.fullmatch(r'[\w.\-]{1,256}', line):
                    configuration[next_argument] = line
                else:
                    parse_notes.add('CLI argument not recognized; left unavailable')
                next_argument = None
            elif line == '-buildTarget':
                next_argument = 'cli_target'
            elif line == '-executeMethod':
                next_argument = 'execute_method'
            elif line in ('-batchmode', '-nographics', '-development', '-debugCodeOptimization', '-enableCodeCoverage'):
                configuration['cli_flags'].append(line)
        if line.startswith('##utp:'):
            try:
                event = json.loads(line[6:])
                if not isinstance(event, dict):
                    raise ValueError('Malformed Unity telemetry event')
                if event.get('type') == 'PlayerBuildInfo':
                    utp_count += 1
                    if utp_count > 1:
                        raise ValueError('Multiple player builds in one log are ambiguous; use one -logFile per build')
                    build['success'] = event.get('success') if isinstance(event.get('success'), bool) else None
                    build['duration_ms'] = nonnegative(event['duration']) if 'duration' in event else None
                    steps = event.get('steps')
                    if isinstance(steps, list):
                        if len(steps) > 256:
                            raise ValueError('Player build phase limit exceeded')
                        build['phases_ms'] = [dict(name=safe_text(s['description']), duration_ms=nonnegative(s['duration'])) for s in steps]
            except (json.JSONDecodeError, KeyError, TypeError) as error:
                raise ValueError('Malformed PlayerBuildInfo event') from error
        if match := SUCCESS.match(line):
            build.update(success=True, result='Success', size_bytes=int(match[2]), warning_count=int(match[3]))
        elif line.startswith('Build Finished, Result:'):
            build['result'] = safe_text(line.split(':', 1)[1].rstrip('.'))
            build['success'] = build['result'].lower() in ('success', 'succeeded')
        if match := SHADER.match(line):
            shader, shader_ordinal = safe_text(match[1]), shader_ordinal + 1
            pass_name = stage = current_row = None
            pass_ordinal = 0
        elif match := PASS.match(line):
            pass_name, stage = safe_text(match[1]), safe_text(match[2])
            pass_ordinal += 1
            current_row = None
        elif match := API.match(line):
            if shader is not None and pass_name is not None:
                current_row = add_row(pass_name, stage, safe_text(match[1]))
        elif match := RETAINED.match(line):
            if current_row is not None:
                current_row['retained_variants'] = int(match[1])
        elif match := FINISHED.match(line):
            name, program_stage, api = map(safe_text, match.group(1, 2, 3))
            total = sum(int(match[i]) for i in (5, 7, 9, 11))
            candidates = [r for r in shaders if r['shader_ordinal'] == shader_ordinal and
                          (r['pass_name'], r['stage'], r['api']) == (name, program_stage, api) and r['wall_seconds'] is None]
            exact = [r for r in candidates if r['retained_variants'] == total]
            row = (exact or candidates)[0] if candidates else add_row(name, program_stage, api)
            row.update(wall_seconds=float(match[4]), local_cache_hits=int(match[5]),
                       local_cache_cpu_seconds=float(match[6]), remote_cache_hits=int(match[7]),
                       remote_cache_cpu_seconds=float(match[8]), compiled_variants=int(match[9]),
                       compiled_cpu_seconds=float(match[10]), skipped_variants=int(match[11]))
        if match := SHADER_WARNING.match(line):
            if site := WARNING_SITE.match(match[2]):
                add_warning('shader', match[1], site[1], site[2], int(site[3]), safe_text(site[4]))
            else:
                add_warning('shader', match[1], match[2])
        elif re.search(r'(?i)(?:^warning\b|\bwarning (?:CS|BC)\d+\b|\bwarning:)', line):
            add_warning('other', None, line)

    configuration['cli_flags'] = sorted(set(configuration['cli_flags']))
    configuration['graphics_apis'] = sorted({r['api'] for r in shaders})
    if not shaders:
        configuration['graphics_apis'] = None
    for warning in warnings.values():
        warning['messages'].sort()
    unavailable = [f'build.{key}' for key, value in build.items() if value is None]
    unavailable += [f'configuration.{key}' for key, value in configuration.items() if value is None]
    if not shaders:
        unavailable.append('shader_metrics: no shader pass records (cached/script-only is possible)')
    elif any(any(value is None for key, value in r.items() if key.endswith(('_variants', '_hits', '_seconds'))) for r in shaders):
        unavailable.append('shader_metrics: null fields were not reported for those passes')
    return dict(schema_version=1, source=dict(sha256=digest.hexdigest(), bytes=total_bytes),
                configuration=configuration,
                declared_context=dict(configuration_id=configuration_id, machine_id=machine_id, cache_state=cache_state),
                build=build, shaders=shaders,
                warnings=dict(observed_occurrences=sum(w['occurrences'] for w in warnings.values()),
                              unique_causes=len(warnings), entries=sorted(warnings.values(), key=lambda w: json.dumps(w, sort_keys=True))),
                unavailable=unavailable,
                notes=sorted(parse_notes | {'Null means unavailable, not zero.',
                    'Shader/pass wall times can overlap; no wall total is inferred.',
                    'Warning occurrences from log lines are distinct from BuildReport.warning_count.',
                    'CLI target is not the editor architecture; target architecture/backend are unavailable unless separately recorded.',
                    'No timing difference alone proves an optimization caused a gain.'}))


def compare_reports(current: dict, previous: dict) -> dict:
    reasons = []
    if previous.get('schema_version') != current['schema_version']:
        return dict(comparable=False, reasons=['report schema differs'], duration_delta_ms=None)
    for field in ('unity_version', 'editor_architecture', 'cli_target', 'execute_method', 'graphics_apis', 'cli_flags'):
        a, b = current['configuration'].get(field), previous.get('configuration', {}).get(field)
        if a is None or b is None or a != b:
            reasons.append('logged configuration differs or unavailable: ' + field)
    for field in ('configuration_id', 'machine_id', 'cache_state'):
        a, b = current['declared_context'].get(field), previous.get('declared_context', {}).get(field)
        if not a or a == 'unknown' or a != b:
            reasons.append('declared context differs or unavailable: ' + field)
    def cache_work(report):
        fields = ('shader', 'pass_name', 'stage', 'api', 'retained_variants', 'local_cache_hits',
                  'remote_cache_hits', 'compiled_variants', 'skipped_variants')
        return Counter(tuple(row.get(key) for key in fields) for row in report.get('shaders', []))
    work = cache_work(current)
    if not work or any(None in row for row in work) or work != cache_work(previous):
        reasons.append('shader cache/work coverage differs or unavailable')
    if any(r.get('build', {}).get('success') is not True or r.get('build', {}).get('duration_ms') is None for r in (current, previous)):
        reasons.append('successful measured build missing')
    return dict(comparable=not reasons, reasons=reasons,
                duration_delta_ms=None if reasons else current['build']['duration_ms'] - previous['build']['duration_ms'])


def write_report(path: Path, report: dict):
    data = (json.dumps(report, ensure_ascii=False, sort_keys=True, separators=(',', ':'), allow_nan=False) + '\n').encode('utf-8')
    if len(data) > MAX_REPORT_BYTES:
        raise ValueError('Report exceeds 1 MiB limit; no partial report written')
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(prefix='.unity-build-', suffix='.tmp', dir=path.parent, delete=False) as output:
            temporary = Path(output.name)
            output.write(data)
            output.flush()
            os.fsync(output.fileno())
        os.replace(temporary, path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--log', required=True, type=Path)
    parser.add_argument('--output', required=True, type=Path)
    parser.add_argument('--configuration-id', help='Same ID only for matching build settings/options/scenes/APIs; not inferred')
    parser.add_argument('--machine-id', help='Opaque matching host identity, without user or host paths')
    parser.add_argument('--cache-state', choices=('unknown', 'cold', 'warm', 'mixed'), default='unknown')
    parser.add_argument('--compare', type=Path, help='Optional previous report; noncomparable runs get no timing delta')
    args = parser.parse_args(argv)
    try:
        if args.log.resolve() == args.output.resolve() or args.compare and args.compare.resolve() == args.output.resolve():
            raise ValueError('Output must not overwrite an input')
        for value in (args.configuration_id, args.machine_id):
            if value is not None and not re.fullmatch(r'[A-Za-z0-9_.-]{1,128}', value):
                raise ValueError('Context IDs must be opaque identifiers, at most 128 characters')
        report = parse_log(args.log, configuration_id=args.configuration_id, machine_id=args.machine_id, cache_state=args.cache_state)
        if args.compare:
            if args.compare.stat().st_size > MAX_REPORT_BYTES:
                raise ValueError('Comparison report exceeds 1 MiB')
            report['comparison'] = compare_reports(report, json.loads(args.compare.read_text(encoding='utf-8')))
        write_report(args.output, report)
        print(f"Build result={report['build']['result']}, shader records={len(report['shaders'])}, unique warnings={report['warnings']['unique_causes']}")
        return 0
    except (OSError, ValueError, KeyError, TypeError) as error:
        print('Build report failed: ' + safe_text(str(error)), file=sys.stderr)
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
