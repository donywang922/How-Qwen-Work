"""Full-vocabulary tokenizer coverage and shader-format Q4 roundtrip invariants."""
import unittest
import json
import numpy as np
import torch
from transformers import AutoTokenizer
from prepare_model import load_exporter, make_quant_class
from qwen_paths import MODEL_ROOT, shadergpt_vendor


class PreparationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.quant = make_quant_class(load_exporter(shadergpt_vendor()))

    def test_shader_q4_roundtrip(self):
        torch.manual_seed(7)
        weights = torch.randn(8, 96) * .02
        packed = self.quant(weights)
        q, scales, _ = packed.export()
        scales = scales.transpose(0, 2, 1).reshape(8, 3).view(np.int32)
        zero = scales & ((1 << 14) - 1)
        core = ((scales >> 2) & np.uint32(0x9ffff000)).astype(np.uint32).view(np.float32)
        scale = core * np.float32(2.0 ** 96)
        add = .5 - zero.astype(np.float32) * (16.0 / (1 << 14))
        decoded = (q.reshape(8, 3, 32).astype(np.float32) / 17 + add[..., None]) * scale[..., None]
        np.testing.assert_allclose(decoded.reshape(8, 96), packed.dequantize().numpy(), rtol=2e-6, atol=1e-8)

    def test_full_tokenizer_unicode_and_chat_template(self):
        tokenizer = AutoTokenizer.from_pretrained(MODEL_ROOT, local_files_only=True)
        # The embedding matrix is padded; tokenizer length is not the model vocabulary size.
        config = json.loads((MODEL_ROOT / 'config.json').read_text(encoding='utf-8'))
        self.assertEqual(config['text_config']['vocab_size'], 248320)
        self.assertEqual(len(tokenizer), 248077)
        self.assertLess(max(tokenizer.get_vocab().values()), config['text_config']['vocab_size'])
        for text in ["简体與繁體 日本語 한국어 English 😀 0123\n{}[]", "<|im_start|>user\n你好<|im_end|>"]:
            ids = tokenizer.encode(text, add_special_tokens=False)
            self.assertEqual(tokenizer.decode(ids, skip_special_tokens=False), text)


if __name__ == '__main__':
    unittest.main()
