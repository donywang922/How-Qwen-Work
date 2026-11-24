"""Portable paths, safe payload restoration and public source exclusions; no ML imports."""
import contextlib
import hashlib
import io
import json
import tempfile
import unittest
from pathlib import Path

from qwen_paths import resolve_data_root
from restore_model import restore, safe_target
from source_layout import is_source_path
from source_audit import PRIVATE_PATH, SECRET


class SourceToolsTests(unittest.TestCase):
    def test_path_precedence_and_relative_default(self):
        with tempfile.TemporaryDirectory() as directory:
            project = Path(directory)
            self.assertEqual(resolve_data_root(project,{}),project / '.qwen-work')
            (project / '.qwen-local.json').write_text(json.dumps({'dataRoot':'local'}),encoding='utf-8')
            self.assertEqual(resolve_data_root(project,{}),project / 'local')
            self.assertEqual(resolve_data_root(project,{'QWEN_DATA_ROOT':'override'}),project / 'override')

    def test_payloads_restore_meta_and_are_idempotent(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory); source = base / 'export'; source.mkdir()
            (source / 'weight.png').write_bytes(b'fixture-data')
            payload = {'path':'weight.png','sha256':hashlib.sha256(b'fixture-data').hexdigest(),
                'meta':'fileFormatVersion: 2\nguid: 0123456789abcdef0123456789abcdef\n'}
            manifest = {'modelFolder':'Assets/Model','payloads':[payload]}
            project = base / 'project'
            with contextlib.redirect_stdout(io.StringIO()):
                restore(source,project,manifest=manifest)
                target = project / 'Assets/Model/weight.png'
                before = target.stat().st_mtime_ns
                restore(source,project,manifest=manifest)
                restore(source,project,check_only=True,manifest=manifest)
            self.assertEqual(target.stat().st_mtime_ns,before)
            self.assertEqual(Path(str(target)+'.meta').read_text(),payload['meta'])

    def test_corrupt_export_does_not_change_checkout(self):
        with tempfile.TemporaryDirectory() as directory:
            base = Path(directory); source = base / 'export'; source.mkdir()
            (source / 'weight.png').write_bytes(b'corrupt')
            manifest = {'modelFolder':'Assets/Model','payloads':[{'path':'weight.png','sha256':'0'*64,'meta':''}]}
            with self.assertRaises(ValueError): restore(source,base/'project',manifest=manifest)
            self.assertFalse((base/'project').exists())

    def test_traversal_rejected(self):
        with tempfile.TemporaryDirectory() as directory:
            for path in ['../outside','/absolute','..\\outside','folder/../../outside']:
                with self.assertRaises(ValueError): safe_target(Path(directory),path)

    def test_source_keeps_code_and_omits_local_generated_data(self):
        self.assertTrue(is_source_path('Assets/QwenWorld/Runtime/QwenTextureRunner.cs'))
        self.assertTrue(is_source_path('Assets/QwenWorld/Models/Qwen35-Full-Q4/model.prefab'))
        self.assertTrue(is_source_path('Tools/Qwen35/model-payloads.lock.json'))
        self.assertTrue(is_source_path('Packages/vpm-manifest.json'))
        for path in ['.qwen-local.json','Library/cache','Packages/com.vrchat.worlds/package.json',
                     'Assets/SerializedUdonPrograms/program.asset','Docs/Qwen35-Validation-Summary.json',
                     'Assets/QwenWorld/Models/Qwen35-Full-Q4/weight.png',
                     'Assets/QwenWorld/Models/Qwen35-Full-Q4/weight.png.meta',
                     'Assets/QwenWorld/Models/Qwen35-Full-Q4/tokenizer.json']:
            self.assertFalse(is_source_path(path),path)

    def test_private_values_are_detected_without_logging_them(self):
        self.assertIsNotNone(PRIVATE_PATH.search('C:/'+'Users/'+'example/private'))
        self.assertIsNotNone(SECRET.search('ghp_'+'x'*36))
        self.assertIsNone(PRIVATE_PATH.search('Tools/Qwen35/sources.lock.json'))


if __name__ == '__main__':
    unittest.main()
