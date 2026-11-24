using UnityEngine;

namespace ShaderGPT.Udon {
public class GPTPipeline : UdonMonoBehaviour {
	public GPTTokenizer tokenizer;
	public GPTGenerator generator;

	public UnityEngine.UI.Text inputText;
	public UnityEngine.UI.Text outputText;
	public UnityEngine.UI.Slider temperatureSlider;

	public bool ignoreEosToken;

	private bool eos;
	public void OnEnable() {
		generator.eventTarget = this.AsUdonBehaviour();
		generator.eventMethod = nameof(OnOutputToken);

		var inputTokens = tokenizer.Encode(inputText.text.Replace("\r",""));
		if(generator.modelPrefab.GetComponentsInChildren<MeshRenderer>().Length > 1) { // encoder-decoder model
			generator.encoderTokens = inputTokens;
			generator.inputTokens = new int[]{Mathf.Max(0, tokenizer.bos_token_id)}; // TODO: decoder_start_token_id
			generator.inputIndex = 0;
		} else {
			var prefixSize = 0;
			var prevTokens = generator.inputTokens;
			if(prevTokens != null)
				for(; prefixSize<prevTokens.Length && prefixSize<inputTokens.Length; prefixSize++)
					if(prevTokens[prefixSize] != inputTokens[prefixSize])
						break;
			generator.inputTokens = inputTokens;
			generator.inputIndex = prefixSize;
		}

		tokenizer.decodeState = 0;
		outputText.text = "";
		eos = false;
		generator.Play();
	}
	public void OnDisable() {
		if(!generator)
			return;
		generator.Stop();
	}
	public void OnOutputToken() {
		if(eos)
			return;
		if(!generator)
			return;
		var token = generator.outputToken;
		var index = generator.outputIndex;
		if(token == tokenizer.eos_token_id && index >= generator.inputTokens.Length && !ignoreEosToken) {
			eos = true;
			generator.Stop();
			return;
		}
		var text = outputText.text;
		if(string.IsNullOrEmpty(text))
			for(int i=0; i<index; i++)
				text += tokenizer.Decode(generator.inputTokens[i]);
		outputText.text = text += tokenizer.Decode(token);
	}
	public void UpdateConfig() {
		generator.temperature = temperatureSlider.value / temperatureSlider.maxValue;
	}
}
}