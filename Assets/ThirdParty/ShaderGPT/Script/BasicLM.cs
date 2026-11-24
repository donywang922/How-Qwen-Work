using UnityEngine;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShaderGPT {
public class BasicLM : MonoBehaviour {
	[Header("Model")]
	public Shader[] shaders;
	public Texture[] textures;
	public TextAsset configJson;
	public TextAsset tokenizerJson;
	public TextAsset testcaseJson;
	public GenerationConfig generationConfig;
	public int encoderMaxLength = 2048;

	public enum Task {
		Run = 0,
		Test,
		Bake,
	}
	[Header("Task")]
	public Task task = Task.Test;
	public UnityEngine.UI.Text outputText;
	public float interval = 0.1f;

	private Functional fn;
	private TensorContext ctx {
		get => fn.ctx;
		set { fn.ctx = value; }
	}
	private Cache cache;
	private ModelForCausalLM model;
	private Tokenizer tokenizer;

	private List<int> tokens;
	private float nextTime;
	private int positionId;

	public void OnEnable() {
		fn = new Functional(){
			ctx = new TensorContext(),
			kernels = shaders.ToDictionary(x => x.name.Split('/')[1], x => x),
		};
		cache = new(fn, generationConfig.max_length);
		model = AutoModel.FromPretrained<ModelForCausalLM>(fn, configJson, textures);
		model.generation_config = generationConfig;
		tokenizer = JsonUtility.FromJson<Tokenizer>(tokenizerJson.text);
		var testcase = testcaseJson ? JsonUtility.FromJson<Testcase>(testcaseJson.text) : null;

		if(task == Task.Run) {
			if(model is ModelForSeq2SeqLM)
				RunEncoder(testcase);
			nextTime = Time.time;
			positionId = 0;
			if(tokens == null)
				tokens = new List<int>(testcase.input_ids);

			var text = "";
			for(int i=0; i<tokens.Count; i++)
				text += tokenizer.model.vocab[tokens[i]];
			if(outputText)
				outputText.text = text;
			else
				Debug.Log(text);
		} else if(task == Task.Test) {
			Debug.Log($"Testing {this.name} ({model.config.model_type}: {model})");
			Test(testcase);
			Debug.Assert(ctx.TensorCount() == cache.Count);
		} else if(task == Task.Bake) {
#if UNITY_EDITOR
			var path = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(configJson));
			path = System.IO.Path.Join(path, System.IO.Path.GetFileName(path) + ".prefab");
			Debug.Log(path);

			Bake();
			Debug.Assert(ctx.TensorCount() == cache.Count);
			EditorGUIUtility.PingObject(ctx.ExportTracing(path));
#endif
		}
		RenderTexture.active = null; // suppress warning
	}
	public void OnDisable() {
		cache.Reset();
	}
	public void Update() {
		if(task == Task.Run) {
			if(tokens.Count >= generationConfig.max_length)
				return;
			if(Time.time < nextTime)
				return;
			nextTime = Time.time + interval;

			var token = Run(positionId);
			Debug.Assert(ctx.TensorCount() == cache.Count);
			positionId = tokens.Count;
			tokens.Add(token);
			if(outputText)
				outputText.text += tokenizer.model.vocab[token];
			else
				Debug.Log(tokenizer.model.vocab[token]);
		}
	}
	int encoder_input_length;
	void RunEncoder(Testcase testcase) {
		encoder_input_length = testcase.encoder_input_ids.Length;
		var encoder_input = InputTensor(testcase.encoder_input_ids, chan2:encoder_input_length);
		var encoder_output = ((ModelForSeq2SeqLM)model).ForSeq2SeqLM(encoder_input, null, cache);
		ctx.Release(encoder_output.encoder_hidden_states);
		ctx.Release(encoder_input);
	}
	int Run(int positionId) {
		var input = InputTensor(tokens, positionId, chan2:encoder_input_length);
		var output = model.ForCausalLM(input, cache);
		ctx.Release(output.hidden_states);
		var next_tokens = model.Generate(input, ref output.logits, cache);
		ctx.Release(input);
		var data = next_tokens.GetData<float>();
		ctx.Release(next_tokens);
		return Mathf.RoundToInt(data[0]);
	}
	void Test(Testcase testcase) {
		var hidden_states_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(CausalLMOutput.hidden_states));
		var logits_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(CausalLMOutput.logits));
		if(model is ModelForSeq2SeqLM) {
			var encoder_hidden_states_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(Seq2SeqLMOutput.encoder_hidden_states));
			encoder_input_length = testcase.encoder_input_ids.Length;
			var encoder_input = InputTensor(testcase.encoder_input_ids, chan2:encoder_input_length);
			var encoder_output = ((ModelForSeq2SeqLM)model).ForSeq2SeqLM(encoder_input, null, cache);
			ctx.Release(encoder_input);
			ctx.AssertEqual(encoder_output.encoder_hidden_states, testcase.encoder_hidden_states, atol:encoder_hidden_states_err);
			ctx.Release(encoder_output.encoder_hidden_states);
		} else {
			encoder_input_length = testcase.input_ids.Length; // for masked lm
		}
		var input = InputTensor(testcase.input_ids, chan2:encoder_input_length);
		var output = model.ForCausalLM(input, cache);
		ctx.Release(input);
		var skip = Mathf.Min(1, output.hidden_states.size0-1); // skip first token which has more outlier features
		ctx.AssertEqual(output.hidden_states[skip.., ..], testcase.hidden_states[(skip*output.hidden_states.size1*4)..], atol:hidden_states_err);
		ctx.AssertEqual(output.logits[^1.., ..], testcase.logits, atol:logits_err);
		ctx.Release(output.hidden_states);
		ctx.Release(output.logits);
	}
	void Bake() {
		if(model is ModelForSeq2SeqLM) {
			using var _ = ctx.Tracing();
			var encoder_input = ctx.CPUTensor(encoderMaxLength, 1);
			ctx.Release(fn.Clone(encoder_input[..1, ..1]));
			var encoder_output = ((ModelForSeq2SeqLM)model).ForSeq2SeqLM(encoder_input, null, cache);
			ctx.Release(encoder_output.encoder_hidden_states);
			ctx.Release(encoder_input);
		}
		using(ctx.Tracing()) {
			var input = ctx.GPUTensor(1, 1);
			var output = model.ForCausalLM(input, cache);
			ctx.Release(output.hidden_states);
			var next_tokens = model.Generate(input, ref output.logits, cache);
			fn.Copy_(input, next_tokens);
			ctx.Release(next_tokens);
			ctx.Release(input);
		}
	}

	Tensor InputTensor(IList<int> input_ids, int position_id=0, int? size0=null, int chan2=0) {
		var n = input_ids.Count-position_id;
		var inputData = new float[(size0??n)*4];
		for(int i=0; i<n; i++) {
			inputData[i*4+0] = input_ids[i+position_id];
			inputData[i*4+1] = i+position_id;
			inputData[i*4+2] = chan2;
		}
		var input = ctx.CPUTensor((size0??n), 1);
		input.SetData(inputData);
		return input;
	}

#pragma warning disable CS0649, CA1812, CA1852
	[System.Serializable]
	class Tokenizer {
		public TokenizerModel model;
	}
	[System.Serializable]
	class TokenizerModel {
		public string[] vocab;
	}
	[System.Serializable]
	class Testcase {
		public int[] encoder_input_ids;
		public float[] encoder_hidden_states;
		public int[] input_ids;
		public float[] hidden_states;
		public float[] logits;
	}
#pragma warning restore CS0649, CA1812, CA1852
}
}