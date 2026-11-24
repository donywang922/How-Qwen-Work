using UnityEngine;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShaderGPT {
public class BasicSTT : MonoBehaviour {
	[Header("Model")]
	public Shader[] shaders;
	public Texture[] textures;
	public TextAsset configJson;
	public TextAsset tokenizerJson;
	public TextAsset testcaseJson;
	public TextAsset processorJson;
	public GenerationConfig generationConfig;
	public int sampleCount = 16000*30;
	public int downsampleFactor = 6;

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
	private ModelForSpeechSeq2Seq model;
	private AudioProcessor processor;
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
		model = AutoModel.FromPretrained<ModelForSpeechSeq2Seq>(fn, configJson, textures);
		model.generation_config = generationConfig;
		processor = AutoProcessor.FromPretrained<AudioProcessor>(fn, processorJson);
		processor.state_dict = model.state_dict;
		tokenizer = JsonUtility.FromJson<Tokenizer>(tokenizerJson.text);
		var testcase = testcaseJson ? JsonUtility.FromJson<Testcase>(testcaseJson.text) : null;

		if(task == Task.Run) {
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
		var feature_size = processor.feature_extractor_config.feature_size;
		encoder_input_length = testcase.encoder_input_features.Length/feature_size;
		var encoder_input = ctx.CPUTensor(feature_size, encoder_input_length/4);
		encoder_input.SetData(testcase.encoder_input_features);

		var encoder_output = model.ForSeq2SeqLM(encoder_input, null, cache);
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
		var input_features_err = ParityToleranceRegistry.Get(processor.GetType(), "input_features");
		var encoder_hidden_states_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(Seq2SeqLMOutput.encoder_hidden_states));
		var hidden_states_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(CausalLMOutput.hidden_states));
		var logits_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(CausalLMOutput.logits));
		{
			Tensor encoder_input;
			if(testcase.audio != null) {
				var size0 = GetRowCount(testcase.audio.Length/4);
				var audio = ctx.CPUTensor(size0, testcase.audio.Length/4/size0);
				audio.SetData(testcase.audio);
				audio = ~ctx|fn.Reshape(~audio, 1, null);
				encoder_input = ~ctx|processor.Process(~audio);
				ctx.AssertEqual(encoder_input, testcase.encoder_input_features, atol:input_features_err);
				ctx.Release(encoder_input);
			}
			{
				var size0 = GetRowCount(testcase.encoder_input_features.Length/4);
				encoder_input = ctx.CPUTensor(size0, testcase.encoder_input_features.Length/4/size0);
				encoder_input.SetData(testcase.encoder_input_features);
				encoder_input = ~ctx|fn.Reshape(~encoder_input, processor.feature_extractor_config.feature_size, null);
				encoder_input_length = encoder_input.size1*4;
			}

			var encoder_output = ((ModelForSpeechSeq2Seq)model).ForSpeechSeq2Seq(encoder_input, null, cache);
			ctx.Release(encoder_input);
			ctx.AssertEqual(encoder_output.encoder_hidden_states, testcase.encoder_hidden_states, atol:encoder_hidden_states_err);
			ctx.Release(encoder_output.encoder_hidden_states);
		}

		// Debug.Log($"testcase.input_ids.Length={testcase.input_ids.Length}");
		var input = InputTensor(testcase.input_ids, chan2:encoder_input_length);
		var output = model.ForCausalLM(input, cache);
		ctx.Release(input);
		ctx.AssertEqual(output.hidden_states, testcase.hidden_states, atol:hidden_states_err);
		ctx.AssertEqual(output.logits[^1.., ..], testcase.logits, atol:logits_err);
		ctx.Release(output.hidden_states);
		ctx.Release(output.logits);
	}
	void Bake() {
		using(ctx.Tracing()) {
			using var _ = fn.SetBatchSize(128);
			var size0 = GetRowCount(sampleCount/4);
			var audio = ctx.CPUTensor(size0, sampleCount/4/size0*downsampleFactor);
			ctx.Release(fn.Clone(audio[..1, ..1]));
			var encoder_input = ~ctx|fn.DownsampleNearest(~audio, downsampleFactor);
			encoder_input = ~ctx|fn.Reshape(~encoder_input, ctx.GPUTensor(1, sampleCount/4));

			Debug.Log($"encoder_input.shape={encoder_input.shape}");
			encoder_input = ~ctx|processor.Process(~encoder_input);

			var encoder_output = model.ForSeq2SeqLM(encoder_input, null, cache);
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
	static int GetRowCount(int size1) {
		for(int i=Mathf.FloorToInt(Mathf.Sqrt(size1)); i>1; i--)
			if(size1%i == 0)
				return i;
		return 1;
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
	class Testcase : ISerializationCallbackReceiver {
		public float[] encoder_input_features;
		public float[] encoder_input_values;
		public float[] encoder_hidden_states;
		public int[] input_ids;
		public float[] hidden_states;
		public float[] logits;
		public float[] audio;
		public void OnBeforeSerialize() {}
		public void OnAfterDeserialize() {
			encoder_input_features ??= encoder_input_values;
		}
	}
#pragma warning restore CS0649, CA1812, CA1852
}
}