"""Focused conservative-inventory contract; stdlib only, no Unity/cache access."""
from __future__ import annotations

from contextlib import redirect_stdout
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tempfile
import sys
import unittest

# Repository cache policy forbids __pycache__ even when invoked without -B.
sys.dont_write_bytecode = True

spec = importlib.util.spec_from_file_location('lit_inventory', Path(__file__).with_name('check-lit-inventory.py'))
inventory_tool = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inventory_tool)

SHADER = 'Assets/Shaders/Lit.shader'
OWNER = 'Assets/Runtime/Producer.cs'
CONTEXT = 'Packages/manifest.json'
GUID = 'b5743a506241315419f3a2ffcfef4d20'


class LitInventoryTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix='bargame-lit-inventory-')
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.put(SHADER, 'Shader "Bar Promenade/Lit"\n#pragma shader_feature_local _ALPHATEST_ON\n#include "Common.hlsl"\n')
        self.put(SHADER + '.meta', 'guid: ' + GUID + '\n')
        self.put('Assets/Shaders/Common.hlsl', '// No additional local keywords.\n')
        self.put(OWNER, '''Material Make() {
    // Shader.EnableKeyword("COMMENT_ONLY");
    string text = "material.shaderKeywords = false;";
    Material material = new Material(shader);
    material.EnableKeyword("_ALPHATEST_ON");
    return material;
}''')
        self.put(CONTEXT, '{}\n')
        self.material('Assets/Plain.mat', [])
        self.material('Assets/Emission.mat', ['_EMISSION'])
        self.inventory = dict(
            schema_version=1, variant_policy='retain_all', shader_path=SHADER, shader_guid=GUID,
            keyword_owners={OWNER: dict(sha256=self.digest(OWNER), review='Reviewed fixture alpha-test producer.')},
            material_helpers={OWNER: dict(sha256=self.digest(OWNER))},
            shader_inputs={name: dict(sha256=self.digest(name)) for name in (SHADER, 'Assets/Shaders/Common.hlsl')},
            context_inputs={CONTEXT: dict(sha256=self.digest(CONTEXT))}, external_package_includes=[],
            static_local_sets=[dict(keywords=[]), dict(keywords=['_EMISSION'])],
            runtime_local_sets=[dict(keywords=['_ALPHATEST_ON'], owners=[OWNER], evidence='Runtime constructor.', uncertainty='Not exhaustive.')],
            limitations=['Lexical lower bound; retain all variants.'])

    def put(self, name, text):
        path = self.root / name
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text, encoding='utf-8')

    def digest(self, name):
        return hashlib.sha256((self.root / name).read_bytes()).hexdigest()

    def material(self, name, keys):
        block = '\n' + ''.join('  - ' + key + '\n' for key in keys) if keys else ' []\n'
        self.put(name, f'Material:\n  m_Shader: {{fileID: 4800000, guid: {GUID}, type: 3}}\n  m_ValidKeywords:' + block + '  m_InvalidKeywords: []\n')

    def test_static_and_runtime_extension_are_preserved_without_pruning_or_source_writes(self):
        before = {path.relative_to(self.root): path.read_bytes() for path in self.root.rglob('*') if path.is_file()}
        report = inventory_tool.inspect(self.root, self.inventory)
        self.assertEqual(report['review_status'], 'matched')
        self.assertEqual(report['counts']['static_sets'], 2)
        self.assertIn(['_ALPHATEST_ON'], report['evidenced_local_set_lower_bound'])
        self.assertNotIn(['_ALPHATEST_ON'], [row['keywords'] for row in report['static_local_sets']])
        self.assertEqual(report['variant_policy'], 'retain_all')
        self.assertEqual(report['removal_candidates'], [])
        self.assertEqual([site['kind'] for site in report['keyword_owners'][OWNER]['sites']].count('keyword_call'), 1)
        after = {path.relative_to(self.root): path.read_bytes() for path in self.root.rglob('*') if path.is_file()}
        self.assertEqual(before, after)
        with self.assertRaisesRegex(ValueError, 'Build/reports'):
            inventory_tool.write_report(self.root, Path('Assets/report.json'), report)

    def test_new_keyword_owner_helper_include_and_static_set_require_bounded_review_and_keep_all(self):
        # Cover global/CoreUtils/material-array/typed-keyword APIs without claiming semantics.
        body = '''Material Create() {
    Shader.EnableKeyword("GLOBAL");
    Shader.DisableKeyword("GLOBAL");
    CoreUtils.SetKeyword(material, key, true);
    material.shaderKeywords = keys;
    material.enabledKeywords = localKeys;
    var local = new LocalKeyword(shader, key);
    var global = GlobalKeyword.Create(key);
    cmd.EnableShaderKeyword(key);
    return new Material(shader);
}'''
        self.put('Assets/Runtime/Unregistered00.cs', body)
        self.put(SHADER, (self.root / SHADER).read_text() + '#include "Unregistered.hlsl"\n')
        self.put('Assets/Shaders/Unregistered.hlsl', '// Unknown dependency must not authorize stripping.\n')
        self.material('Assets/Unknown.mat', ['_UNREGISTERED'])
        report = inventory_tool.inspect(self.root, self.inventory)
        self.assertEqual(report['review_status'], 'needs_review')
        kinds = {row['kind'] for row in report['diagnostics']}
        self.assertTrue({'unregistered_keyword_owners', 'unregistered_material_helpers',
                         'unregistered_shader_inputs', 'unregistered_static_local_set'} <= kinds)
        for index in range(1, 40):
            self.put(f'Assets/Runtime/Unregistered{index:02}.cs', body)
        report = inventory_tool.inspect(self.root, self.inventory)
        self.assertGreater(report['counts']['diagnostics_omitted'], 0)
        self.assertEqual(len(report['diagnostics']), inventory_tool.MAX_DIAGNOSTICS)
        self.assertIn(['_UNREGISTERED'], report['evidenced_local_set_lower_bound'])
        self.assertIn(['_ALPHATEST_ON'], report['evidenced_local_set_lower_bound'])
        self.assertEqual((report['variant_policy'], report['removal_candidates']), ('retain_all', []))
        self.put('tools/inventory.json', json.dumps(self.inventory))
        with redirect_stdout(io.StringIO()) as output:
            code = inventory_tool.main(['--root', str(self.root), '--inventory', 'tools/inventory.json',
                                        '--output', 'Build/reports/lit.json'])
        self.assertEqual(code, 2)
        self.assertLessEqual(len(output.getvalue().splitlines()), inventory_tool.MAX_DIAGNOSTICS + 2)
        saved = json.loads((self.root / 'Build/reports/lit.json').read_text())
        self.assertEqual(saved['removal_candidates'], [])
        self.assertEqual(saved['variant_policy'], 'retain_all')
        self.assertFalse(list((self.root / 'Build/reports').glob('.lit-inventory-*')))


if __name__ == '__main__':
    unittest.main()
