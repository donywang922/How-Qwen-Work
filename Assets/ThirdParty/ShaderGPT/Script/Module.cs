using UnityEngine;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT {
public abstract class Module {
	protected Functional fn;
	protected TensorContext ctx => fn.ctx;
	protected ModuleNN nn => new(fn, state_dict);
	public Dictionary<string, Tensor> state_dict;

	protected Module(Functional fn) {
		this.fn = fn;
		this.state_dict = new Dictionary<string, Tensor>();
	}
	public void LoadStateDict([NotNull] IReadOnlyList<Texture> textures) {
		foreach(var tex in textures)
			state_dict[tex.name] = tex;
		foreach(var tex in textures)
			for(int i=1; i<=8; i++)
				if(state_dict.TryGetValue($"{tex.name}.q{i}", out var quantizer)) {
					fn.quantizers[tex] = quantizer;
					if(state_dict.TryGetValue($"{tex.name}.q{i}.idx", out var permuter))
						fn.permuters[tex] = permuter;
					break;
				}
	}

	// utilities
	protected void FixSize0(string name, int size0) {
		if(!state_dict.TryGetValue(name, out var tensor))
			return; // in case a fallback is used
		tensor.size0 = size0;
		for(int i=1; i<=8; i++)
			if(state_dict.TryGetValue($"{name}.q{i}", out var quantizer)) {
				quantizer.tile1 = tensor.tile1;
				if(state_dict.TryGetValue($"{name}.q{i}.idx", out var permuter))
					permuter.size0 = 2;
				break;
			}
	}
}

[System.Serializable]
public class PretrainedConfig {
	public string model_type;
	public string processor_class;
	public int vocab_size;
	protected virtual void FromPretrained(string json) {
		if(json != null) // null for json postprocessing
			JsonUtility.FromJsonOverwrite(json, this);
	}
	internal void FromPretrained() => FromPretrained(null);
	public static PretrainedConfig FromPretrained<T>(TextAsset configJson) where T : PretrainedConfig {
		if(configJson == null)
			throw new System.ArgumentNullException(nameof(configJson), "Config JSON TextAsset is null.");
		var config = System.Activator.CreateInstance<T>();
		config.FromPretrained(configJson.text);
		return config;
	}
}
public abstract class PretrainedModel : Module {
	public PretrainedConfig config;
	protected PretrainedModel(Functional fn, PretrainedConfig config): base(fn) {
		this.config = config;
	}
#pragma warning disable CA1305
	protected int LayerIdx(string path) => int.Parse(System.Text.RegularExpressions.Regex.Match(path, @"\.(\d+)(?:\.|$)").Groups[1].Value);
#pragma warning restore CA1305
}
}