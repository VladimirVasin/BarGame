"""Focused pipeline regressions; no Blender generation, Unity or native compilation."""
from __future__ import annotations

import argparse
import importlib.util
import os
from pathlib import Path
import subprocess
import unittest
from unittest import mock

import asset_pipeline
import toolchain

spec = importlib.util.spec_from_file_location("blender_launcher", Path(__file__).with_name("run-blender.py"))
launcher = importlib.util.module_from_spec(spec)
spec.loader.exec_module(launcher)


class AssetPipelineTests(unittest.TestCase):
    def setUp(self):
        self.temporary = asset_pipeline.workspace_temporary_directory("test-")
        self.directory = self.temporary.__enter__().resolve()
        assert self.directory.is_relative_to(toolchain.ROOT / "Captures")
        self.addCleanup(self.temporary.__exit__, None, None, None)

    def test_old_empty_missing_and_invalid_json_cannot_satisfy_expected_output(self):
        path = self.directory / "manifest.json"
        with self.assertRaises(RuntimeError):
            asset_pipeline.validate_output(path, None)
        path.write_text("", encoding="utf-8")
        with self.assertRaises(RuntimeError):
            asset_pipeline.validate_output(path, None)
        path.write_text("{}", encoding="utf-8")
        before = asset_pipeline.output_stamp(path)
        with self.assertRaises(RuntimeError):
            asset_pipeline.validate_output(path, before)
        path.write_text("not json", encoding="utf-8")
        with self.assertRaises(ValueError):
            asset_pipeline.validate_output(path, None)

    def test_failed_second_replacement_restores_first_and_preserves_meta(self):
        source_a, source_b = self.directory / "new-a", self.directory / "new-b"
        target_a, target_b = self.directory / "a.dll", self.directory / "b.dll"
        sidecar = self.directory / "a.dll.meta"
        for path, contents in ((source_a, "new a"), (source_b, "new b"),
                               (target_a, "old a"), (target_b, "old b"), (sidecar, "guid: retained")):
            path.write_text(contents, encoding="utf-8")
        replace = os.replace

        def fail_second(source, destination):
            if destination == target_b:
                raise PermissionError("locked shipping DLL")
            replace(source, destination)

        with mock.patch.object(asset_pipeline.os, "replace", side_effect=fail_second):
            with self.assertRaises(PermissionError):
                asset_pipeline.publish_files([(source_a, target_a), (source_b, target_b)])
        self.assertEqual(target_a.read_text(), "old a")
        self.assertEqual(target_b.read_text(), "old b")
        self.assertEqual(sidecar.read_text(), "guid: retained")
        self.assertFalse(list(self.directory.glob(".bp-publish-*")))

    def test_publication_rejects_repository_escape_before_any_change(self):
        source = self.directory / "candidate"
        source.write_text("candidate", encoding="utf-8")
        with self.assertRaises(ValueError):
            asset_pipeline.publish_files([(source, toolchain.ROOT / "tools/should-not-exist")])

    def run_staged(self, child):
        target = self.directory / "published"
        expected = target / "model.json"
        target.mkdir()
        expected.write_text('{"old": true}', encoding="utf-8")
        args = argparse.Namespace(generator="tools/build-city-pedestrian-3d-model.py", blender=None,
                                  expect=[str(expected)], validate_only=False,
                                  stage_output=["--model-dir=" + str(target)])
        with mock.patch.object(toolchain, "check_python"), mock.patch.object(toolchain, "check_blender"), \
             mock.patch.object(toolchain, "blender_path", return_value=Path("blender.exe")), \
             mock.patch.object(launcher.subprocess, "run", side_effect=child):
            launcher.run(args, ["--no-preview"])
        return expected

    def test_generator_failure_propagates_and_never_publishes(self):
        def failed(command, **kwargs):
            self.assertIn("--factory-startup", command)
            self.assertEqual(command[command.index("--python-exit-code") + 1], "1")
            staged = Path(command[command.index("--model-dir") + 1])
            (staged / "model.json").write_text('{"partial": true}', encoding="utf-8")
            raise subprocess.CalledProcessError(1, command)

        with self.assertRaises(subprocess.CalledProcessError):
            self.run_staged(failed)
        self.assertEqual((self.directory / "published/model.json").read_text(), '{"old": true}')

    def test_success_publishes_only_after_expected_output_validation(self):
        def success(command, **kwargs):
            self.assertTrue(kwargs["check"])
            staged = Path(command[command.index("--model-dir") + 1])
            (staged / "model.json").write_text('{"complete": true}', encoding="utf-8")
            (staged / "model.json.meta").write_text("must not publish", encoding="utf-8")

        expected = self.run_staged(success)
        self.assertEqual(expected.read_text(), '{"complete": true}')
        self.assertFalse(expected.with_name("model.json.meta").exists())

    def test_missing_expected_output_blocks_successful_process_publication(self):
        def incomplete(command, **kwargs):
            staged = Path(command[command.index("--model-dir") + 1])
            (staged / "other.json").write_text("{}", encoding="utf-8")

        with self.assertRaises(RuntimeError):
            self.run_staged(incomplete)
        self.assertEqual((self.directory / "published/model.json").read_text(), '{"old": true}')

    def test_toolchain_mismatch_fails_with_installation_instruction(self):
        with mock.patch.object(toolchain.platform, "python_version", return_value="0.0.0"):
            with self.assertRaisesRegex(RuntimeError, "required.*tools/toolchain.json"):
                toolchain.check_python(toolchain.load_config())


if __name__ == "__main__":
    unittest.main()
