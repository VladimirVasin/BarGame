"""Focused log-contract checks; no Unity, cache changes or generated project assets."""
from __future__ import annotations

import importlib.util
import json
from pathlib import Path
import tempfile
import sys
import unittest
from unittest import mock

# Repository cache policy forbids __pycache__ even when invoked without -B.
sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location('build_report', Path(__file__).with_name('report-unity-build.py'))
reporter = importlib.util.module_from_spec(spec)
spec.loader.exec_module(reporter)

HEADER = '''Unity Editor version:    6000.6.7f1 (b22767f04f04)
Architecture:            arm64
COMMAND LINE ARGUMENTS:
/Applications/Unity/Unity
-batchmode
-buildTarget
Win64
-projectPath
/Users/private/Project
-executeMethod
BarPromenade.Editor.ProjectSceneSetup.BuildWindows
-logFile
/tmp/build.log
Successfully changed project path to: /Users/private/Project
'''
FOOTER = '''Build Finished, Result: Success.
##utp:{"success":true,"type":"PlayerBuildInfo","time":123,"processId":456,"steps":[{"description":"Writing asset files","duration":1300}],"duration":1500}
Windows build succeeded: 50000 bytes, 4 warnings.
'''


def shader_block(api='dx11', cached=0):
    return f'''Compiling shader "Bar Promenade/Lit"
  Pass "ForwardLit" (fp)
    Target graphics API: {api}
    After scriptable stripping: 5
  Pass ForwardLit (fp, {api}) finished in 1.25 seconds. Local cache hits {cached} (0.01s CPU time), remote cache hits 0 (0.00s CPU time), compiled {5-cached} variants (2.34s CPU time), skipped 0 variants.
'''


class UnityBuildReportTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='bargame-build-report-')
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)

    def parse(self, body):
        path = self.directory / 'build.log'
        path.write_text(HEADER + body + FOOTER, encoding='utf-8')
        return reporter.parse_log(path, configuration_id='windows-production', machine_id='host1', cache_state='warm')

    def test_phases_shader_metrics_and_warning_identity_without_private_paths(self):
        warning = "Shader warning in 'Bar Promenade/Lit': use of potentially uninitialized variable (x) at Assets/Resources/Shaders/Test.hlsl(12) (on dx11)\n"
        report = self.parse(shader_block() + warning * 2 + warning.replace('dx11', 'dx12'))
        self.assertEqual(report['build']['duration_ms'], 1500)
        self.assertEqual(report['build']['phases_ms'], [dict(name='Writing asset files', duration_ms=1300)])
        self.assertEqual(report['configuration']['cli_target'], 'Win64')
        self.assertEqual(report['configuration']['editor_architecture'], 'arm64')
        row = report['shaders'][0]
        self.assertEqual((row['retained_variants'], row['compiled_variants'], row['wall_seconds'], row['compiled_cpu_seconds']), (5, 5, 1.25, 2.34))
        self.assertEqual(report['warnings']['unique_causes'], 2)
        self.assertEqual(report['warnings']['observed_occurrences'], 3)
        payload = json.dumps(report)
        self.assertNotIn('/Users/', payload)
        self.assertNotIn('processId', payload)
        self.assertNotIn('wall_total', payload)

    def test_no_shader_warm_build_and_incomplete_pass_are_unavailable(self):
        report = self.parse('')
        self.assertEqual(report['shaders'], [])
        self.assertIsNone(report['configuration']['graphics_apis'])
        self.assertFalse(reporter.compare_reports(report, report)['comparable'])
        partial = self.parse('Compiling shader "Test"\n  Pass "Shadow" (vp)\n    Target graphics API: metal\n    After scriptable stripping: 0\n')
        row = partial['shaders'][0]
        self.assertEqual(row['retained_variants'], 0)
        self.assertIsNone(row['compiled_variants'])
        self.assertIsNone(row['wall_seconds'])

    def test_same_named_passes_remain_separate_and_no_overlapping_wall_sum(self):
        body = shader_block().replace('Compiling shader "Bar Promenade/Lit"\n', '', 1)
        report = self.parse('Compiling shader "Bar Promenade/Lit"\n' + body + body)
        self.assertEqual([r['pass_ordinal'] for r in report['shaders']], [1, 2])
        self.assertEqual([r['wall_seconds'] for r in report['shaders']], [1.25, 1.25])
        self.assertEqual(report['build']['duration_ms'], 1500)

    def test_comparison_refuses_cache_and_configuration_differences(self):
        original = self.parse(shader_block())
        same = self.parse(shader_block())
        self.assertTrue(reporter.compare_reports(same, original)['comparable'])
        cached = self.parse(shader_block(cached=4))
        comparison = reporter.compare_reports(cached, original)
        self.assertFalse(comparison['comparable'])
        self.assertIsNone(comparison['duration_delta_ms'])
        same['configuration']['cli_target'] = 'OSXUniversal'
        self.assertFalse(reporter.compare_reports(same, original)['comparable'])

    def test_deterministic_atomic_output_bounds_and_ambiguous_log(self):
        report = self.parse(shader_block())
        path = self.directory / 'report.json'
        reporter.write_report(path, report)
        first = path.read_bytes()
        reporter.write_report(path, report)
        self.assertEqual(path.read_bytes(), first)
        with mock.patch.object(reporter, 'MAX_REPORT_BYTES', 12):
            with self.assertRaises(ValueError):
                reporter.write_report(path, report)
        self.assertEqual(path.read_bytes(), first)
        self.assertFalse(list(self.directory.glob('.unity-build-*')))
        with mock.patch.object(reporter.os, 'replace', side_effect=OSError('output locked')):
            with self.assertRaises(OSError):
                reporter.write_report(path, report)
        self.assertEqual(path.read_bytes(), first)
        self.assertFalse(list(self.directory.glob('.unity-build-*')))
        with mock.patch.object(reporter, 'MAX_LOG_BYTES', 12):
            with self.assertRaises(ValueError):
                self.parse('')
        with self.assertRaisesRegex(ValueError, 'Multiple player builds'):
            self.parse(FOOTER)


if __name__ == '__main__':
    unittest.main()
