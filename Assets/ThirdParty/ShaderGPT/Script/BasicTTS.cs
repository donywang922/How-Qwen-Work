using UnityEngine;
using System.Collections.Generic;
using System.Linq;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace ShaderGPT {
public class BasicTTS : MonoBehaviour {
	[Header("Model")]
	public Shader[] shaders;
	public Texture[] textures;
	public TextAsset configJson;
	public TextAsset tokenizerJson;
	public TextAsset testcaseJson;
	public int maxLength = 768;
	[Min(0.01f)] public float maxTime = 10f;

	public enum Task {
		Run = 0,
		Test,
		Bake,
	}
	[Header("Task")]
	public Task task = Task.Test;
	public int speakerId;
	public AudioSource outputSource;

	private Functional fn;
	private TensorContext ctx {
		get => fn.ctx;
		set { fn.ctx = value; }
	}
	private ModelForTextToWaveform model;

	public void OnEnable() {
		fn = new Functional(){
			ctx = new TensorContext(),
			kernels = shaders.ToDictionary(x => x.name.Split('/')[1], x => x),
		};
		model = AutoModel.FromPretrained<ModelForTextToWaveform>(fn, configJson, textures);
		var testcase = testcaseJson ? JsonUtility.FromJson<Testcase>(testcaseJson.text) : null;

		if(task == Task.Run) {
			Run(testcase);
			Debug.Assert(ctx.TensorCount() == 0);
		} else if(task == Task.Test) {
			Debug.Log($"Testing {this.name} ({model})");
			Test(testcase);
			Debug.Assert(ctx.TensorCount() == 0);
		} else if(task == Task.Bake) {
#if UNITY_EDITOR
			var path = System.IO.Path.GetDirectoryName(AssetDatabase.GetAssetPath(configJson));
			path = System.IO.Path.Join(path, System.IO.Path.GetFileName(path) + ".prefab");
			Debug.Log(path);

			Bake(testcase);
			Debug.Assert(ctx.TensorCount() == 0);
			EditorGUIUtility.PingObject(ctx.ExportTracing(path));
#endif
		}
		RenderTexture.active = null; // suppress warning
	}
	public void OnDisable() {
		if(outputSource && outputSource.clip)
			Object.Destroy(outputSource.clip);
	}

	void Run(Testcase testcase) {
		var maxIndices = Mathf.CeilToInt(maxTime * model.sampling_rate / model.upsample_rate);
		var speaker_id = InputTensor(new int[]{speakerId});
		var speaker_embeddings = ~ctx|model.GetSpeakerEmbedding((~speaker_id, 0));
		var inputs = InputTensor(testcase.input_ids, chan2:testcase.input_ids.Length,
			chan3:model is Models.Vits ? testcase.custom.durations : null);
		var output = model.ForTextToWaveform(inputs, speaker_embeddings, new TensorView(null, maxIndices, 1));
		ctx.Release(inputs);
		if(speaker_embeddings)
			ctx.Release(speaker_embeddings);
		var waveformData = output.waveform.GetData<float>();
		var sampleCount = Mathf.RoundToInt(output.sequence_lengths.GetData<float>()[0]);
		ctx.Release(output.waveform);
		ctx.Release(output.sequence_lengths);

		Debug.Log($"sampleCount={sampleCount}");
		Debug.Assert(0 <= sampleCount && sampleCount <= waveformData.Length);
		var waveformDataClip = waveformData[..sampleCount].ToArray();

		if(outputSource.clip)
			Object.Destroy(outputSource.clip);
		var clip = AudioClip.Create("tts", sampleCount, 1, model.sampling_rate, false);
		clip.SetData(waveformDataClip, 0);
		outputSource.clip = clip;
		outputSource.Play();
	}
	void Test(Testcase testcase) {
		var maxIndices = Mathf.CeilToInt((float)testcase.waveform.Length / model.upsample_rate) + 16; // test waveform padding
		var speaker_id = InputTensor(new int[]{testcase.speaker_id});
		var speaker_embeddings = ~ctx|model.GetSpeakerEmbedding((~speaker_id, 0));
		var inputs = InputTensor(testcase.input_ids, chan2:testcase.input_ids.Length,
			chan3:model is Models.Vits ? testcase.custom.durations : null,
			size0:testcase.input_ids.Length + 16); // test token padding
		var stopWatch = System.Diagnostics.Stopwatch.StartNew();
		switch(model) {
		case Models.Vits vits: {
			var hidden_states_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(Models.VitsModelOutput.hidden_states));
			var spectrogram_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(Models.VitsModelOutput.spectrogram));
			var waveform_err = ParityToleranceRegistry.Get(model.config.model_type, nameof(Models.VitsModelOutput.waveform));
			vits.config.noise_scale = 0;
			var output = vits.VitsModel(null, inputs, speaker_embeddings, new TensorView(null, maxIndices, 1), output_hidden_states:true);
			ctx.Release(inputs);
			if(speaker_embeddings)
				ctx.Release(speaker_embeddings);
			output.waveform.GetData<float>();
			Debug.Log($"ElapsedMilliseconds: {stopWatch.ElapsedMilliseconds}");

			ctx.AssertEqual(output.hidden_states, testcase.custom.hidden_states, atol:hidden_states_err);
			ctx.AssertEqual(output.spectrogram, testcase.custom.spectrogram, atol:spectrogram_err, trim:true);
			ctx.AssertEqual(output.sequence_lengths, new float[]{testcase.waveform.Length});
			ctx.AssertEqual(output.waveform, testcase.waveform, atol:waveform_err);
			ctx.Release(output.hidden_states);
			ctx.Release(output.spectrogram);
			ctx.Release(output.sequence_lengths);
			ctx.Release(output.waveform);
			break;
		}
		default:
			throw new System.NotSupportedException($"Unsupported model: {model}");
		}
	}
	void Bake(Testcase testcase) {
		using var _ = ctx.Tracing();
		var maxIndices = Mathf.CeilToInt(maxTime * model.sampling_rate / model.upsample_rate);
		var max_waveform_length = maxIndices * model.upsample_rate;

		Tensor inputs = ctx.CPUTensor(maxLength, 1);
		var speakers = ctx.CPUTensor(1, 1);
		var metadata = ctx.CPUTensor(1, 1);
		metadata.SetData(new float[]{model.sampling_rate, max_waveform_length, 0, 0});
		var waveform = ctx.GPUTensor((max_waveform_length+3)/65536+1, 16384);
		fn.Copy_(waveform[..1, ..1], inputs[..1, ..1]);
		fn.Copy_(waveform[..1, ..1], speakers[..1, ..1]);
		fn.Copy_(waveform[..1, ..1], metadata[..1, ..1]);
		ctx.Release(metadata);

		if(model is Models.Vits vits) {
			var weight = vits.VitsDurationEmbedding(testcase?.input_ids, testcase?.custom?.durations);
			var duration = ~ctx|fn.IndexSelect(~weight, (inputs, 0));
			inputs = ~ctx|fn.Add(~inputs, ~duration);
		}
		var speaker_embeddings = ~ctx|model.GetSpeakerEmbedding((~speakers, 0));
		var o = model.ForTextToWaveform(inputs, speaker_embeddings, new TensorView(null, maxIndices, 1));
		if(speaker_embeddings)
			ctx.Release(speaker_embeddings);
		ctx.Release(inputs);

		fn.Reshape(o.waveform, @out:waveform, expand:true);
		fn.Reshape(o.sequence_lengths, @out:waveform[^1.., ^1..]);
		ctx.Release(o.waveform);
		ctx.Release(o.sequence_lengths);
		ctx.Release(waveform);
	}

	Tensor InputTensor(IList<int> input_ids, int position_id=0, int? size0=null, int chan2=0, IList<float> chan3=null) {
		var n = input_ids.Count-position_id;
		var inputData = new float[(size0??n)*4];
		for(int i=0; i<n; i++) {
			inputData[i*4+0] = input_ids[i+position_id];
			inputData[i*4+1] = i+position_id;
			inputData[i*4+2] = chan2;
			inputData[i*4+3] = chan3?[i] ?? 0;
		}
		var input = ctx.CPUTensor((size0??n), 1);
		input.SetData(inputData);
		return input;
	}

#pragma warning disable CS0649, CA1812, CA1852
	[System.Serializable]
	class Testcase {
		public int[] input_ids;
		public int speaker_id;
		public float[] waveform;
		public TestcaseCustom custom;
	}
	[System.Serializable]
	class TestcaseCustom {
		public float[] hidden_states;
		public float[] spectrogram;
		public float[] durations;
	}
#pragma warning restore CS0649, CA1812, CA1852
}
}