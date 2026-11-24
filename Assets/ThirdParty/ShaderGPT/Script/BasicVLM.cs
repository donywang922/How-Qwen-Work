using UnityEngine;
using UnityEngine.Experimental.Rendering;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShaderGPT {
public class BasicVLM : MonoBehaviour {
	[Header("Model")]
	public Shader[] shaders;
	public Texture[] textures;
	public TextAsset configJson;
	public TextAsset tokenizerJson;
	public TextAsset testcaseJson;
	public TextAsset processorJson;
	public GenerationConfig generationConfig;
	public Vector2Int encoderImageSize = new(512, 512);

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
	private ModelForImageTextToText model;
	private Processor.ImageProcessor image_processor;
	private Tokenizer tokenizer;
	private int image_token_id;

	private List<int> tokens;
	private float nextTime;
	private int positionId;

	public void OnEnable() {
		fn = new Functional(){
			ctx = new TensorContext(),
			kernels = shaders.ToDictionary(x => x.name.Split('/')[1], x => x),
		};
		cache = new(fn, generationConfig.max_length);
		model = AutoModel.FromPretrained<ModelForImageTextToText>(fn, configJson, textures);
		model.GetDecoder().generation_config = generationConfig;
		image_processor = JsonUtility.FromJson<Processor>(processorJson.text).image_processor;
		image_token_id = JsonUtility.FromJson<Config>(configJson.text).image_token_id;
		tokenizer = JsonUtility.FromJson<Tokenizer>(tokenizerJson.text);
		var testcase = testcaseJson ? JsonUtility.FromJson<Testcase>(testcaseJson.text) : null;

		if(task == Task.Run) {
			RunEncoder(testcase);
			nextTime = Time.time;
			positionId = 0;
			if(tokens == null)
				tokens = new List<int>(ReplaceImageTokens(testcase.input_ids));

			var text = "";
			for(int i=0; i<tokens.Count; i++)
				text += tokenizer.model.vocab[tokens[i] < 0 ? image_token_id : tokens[i]];
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
	void RunEncoder(Testcase testcase) {
		var image_mean = image_processor.image_mean[0];
		var image_std = image_processor.image_std[0];
		var rescale_factor = image_processor.rescale_factor;
		var image = new Texture2D(testcase.size[0], testcase.size[1],
			TextureFormat.RGB24, mipChain:false, linear:true){hideFlags = HideFlags.DontSave};
		image.SetPixelData(testcase.image, 0);
		image.Apply();
		var pixel_values = fn.ToFloat(image);
		pixel_values = ~ctx|fn.Mul(~pixel_values, 255*rescale_factor/image_std, add:-image_mean/image_std);
		Destroy(image);
		var pixel_ids = model.ProcessImage(ref pixel_values);

		var output = model.ForImageTextToText(null, pixel_values, pixel_ids, cache);
		ctx.Release(pixel_values);
		ctx.Release(pixel_ids);
		ctx.Release(output.image_hidden_states);
	}
	int Run(int positionId) {
		var input = InputTensor(tokens, positionId);
		var output = model.ForImageTextToText(input, null, null, cache);
		ctx.Release(output.hidden_states);
		var next_tokens = model.GetDecoder().Generate(input, ref output.logits, cache);
		ctx.Release(input);
		var data = next_tokens.GetData<float>();
		ctx.Release(next_tokens);
		return Mathf.RoundToInt(data[0]);
	}
	void Test(Testcase testcase) {
		var image_hidden_states_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(VlCausalLMOutput.image_hidden_states));
		var hidden_states_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(VlCausalLMOutput.hidden_states));
		var logits_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(VlCausalLMOutput.logits));
		var image_mean = image_processor.image_mean[0];
		var image_std = image_processor.image_std[0];
		var rescale_factor = image_processor.rescale_factor;
		var image = new Texture2D(testcase.size[0], testcase.size[1],
			TextureFormat.RGB24, mipChain:false, linear:true){hideFlags = HideFlags.DontSave};
		image.SetPixelData(testcase.image, 0);
		image.Apply();
		var pixel_values = fn.ToFloat(image);
		pixel_values = ~ctx|fn.Mul(~pixel_values, 255*rescale_factor/image_std, add:-image_mean/image_std);
		Destroy(image);
		var pixel_ids = model.ProcessImage(ref pixel_values);
		var input_ids = InputTensor(ReplaceImageTokens(testcase.input_ids));

		var output = model.ForImageTextToText(input_ids, pixel_values, pixel_ids, cache);
		ctx.Release(input_ids);
		ctx.Release(pixel_values);
		ctx.Release(pixel_ids);
		ctx.AssertEqual(output.image_hidden_states, testcase.image_hidden_states, atol:image_hidden_states_err);
		ctx.AssertEqual(output.hidden_states, testcase.hidden_states, atol:hidden_states_err);
		ctx.AssertEqual(output.logits[^1.., ..], testcase.logits, atol:logits_err);
		ctx.Release(output.image_hidden_states);
		ctx.Release(output.hidden_states);
		ctx.Release(output.logits);
	}
	void Bake() {
		using(ctx.Tracing()) {
			var image_mean = image_processor.image_mean[0];
			var image_std = image_processor.image_std[0];
			var rescale_factor = image_processor.rescale_factor;
			var pixel_values = fn.FromImage(null, 255*rescale_factor/image_std, -image_mean/image_std,
				@out:ctx.GPUTensor(encoderImageSize.y, encoderImageSize.x, dtype:GraphicsFormat.R32G32B32A32_SFloat));
			var pixel_ids = model.ProcessImage(ref pixel_values);
			pixel_ids = ~ctx|fn.Clone(~pixel_ids);

			var output = model.ForImageTextToText(null, pixel_values, pixel_ids, cache);
			ctx.Release(output.image_hidden_states);
			ctx.Release(pixel_values);
			ctx.Release(pixel_ids);
		}
		using(ctx.Tracing()) {
			var input = ctx.GPUTensor(1, 1, persistent:true);
			var output = model.ForImageTextToText(input, null, null, cache);
			ctx.Release(output.hidden_states);
			var next_tokens = model.GetDecoder().Generate(input, ref output.logits, cache);
			fn.Copy_(input, next_tokens);
			ctx.Release(next_tokens);
			ctx.Release(input);
		}
	}

	int[] ReplaceImageTokens(int[] input_ids) {
		for(int j=0, i=0; i<input_ids.Length; i++)
			if(input_ids[i] == image_token_id)
				input_ids[i] = -1 - j++;
		return input_ids;
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
	class Config {
		public int image_token_id;
	}
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
		public byte[] image;
		public int[] size;
		public float[] image_hidden_states;
		public int[] input_ids;
		public float[] hidden_states;
		public float[] logits;
	}
	[System.Serializable]
	class Processor {
		public ImageProcessor image_processor;
		[System.Serializable]
		public class ImageProcessor {
			public float[] image_mean;
			public float[] image_std;
			public float rescale_factor;
		}
	}
#pragma warning restore CS0649, CA1812, CA1852
}
}