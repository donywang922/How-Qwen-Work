using UnityEngine;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;

namespace ShaderGPT {
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class RegisterModelAttribute : Attribute {
	public string[] ModelTypes { get; }
	public Type ConfigType { get; }
	public RegisterModelAttribute(Type configType, params string[] modelTypes) {
		ConfigType = configType;
		ModelTypes = modelTypes;
	}
}
public static class AutoModel {
	internal static readonly Dictionary<string, (Type modelType, Type configType)> models;
	static readonly MethodInfo fromPretrained;
#pragma warning disable CA1810
	static AutoModel() {
		models = new();
		foreach(var type in Assembly.GetExecutingAssembly().GetTypes()) {
			var attributes = type.GetCustomAttributes(typeof(RegisterModelAttribute), false);
			foreach(RegisterModelAttribute attr in attributes)
				foreach (var modelType in attr.ModelTypes)
					models[modelType] = (type, attr.ConfigType);
		}
		fromPretrained = typeof(PretrainedConfig).GetMethod(nameof(FromPretrained),
			BindingFlags.Public | BindingFlags.Static);
	}
#pragma warning restore CA1810
	public static bool Contains<T>(string model_type) where T : PretrainedModel {
		if(!models.TryGetValue(model_type, out var types))
			return false;
		return typeof(T).IsAssignableFrom(types.modelType);
	}
	internal static T FromPretrained<T>(Functional fn, TextAsset configJson, string model_type) where T : PretrainedModel {
		if(!models.TryGetValue(model_type, out var types))
			throw new NotSupportedException($"Model type {model_type} is not supported");
		if(!typeof(T).IsAssignableFrom(types.modelType))
			throw new InvalidOperationException($"Model type {model_type} does not implement {typeof(T).Name}");

		var configInstance = fromPretrained.MakeGenericMethod(types.configType).Invoke(null, new object[] { configJson });
		var modelInstance = Activator.CreateInstance(types.modelType, new object[] { fn, configInstance });
		return (T)modelInstance;
	}
	public static T FromPretrained<T>(Functional fn, TextAsset configJson, Texture[] textures=null) where T : PretrainedModel {
		if(configJson == null)
			throw new ArgumentNullException(nameof(configJson), "Config JSON TextAsset is null.");
		var config = JsonUtility.FromJson<PretrainedConfig>(configJson.text);
		var model = FromPretrained<T>(fn, configJson, config.model_type);
		if(textures != null)
			model.LoadStateDict(textures);
		return model;
	}
}
public static class AutoProcessor {
	public static T FromPretrained<T>(Functional fn, TextAsset configJson) where T : PretrainedModel {
		if(configJson == null)
			throw new ArgumentNullException(nameof(configJson), "Config JSON TextAsset is null.");
		var config = JsonUtility.FromJson<PretrainedConfig>(configJson.text);
		return AutoModel.FromPretrained<T>(fn, configJson, config.processor_class);
	}
}

[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class ParityToleranceAttribute : Attribute {
	public string FieldName { get; }
	public float Atol { get; }
	public string ModelType { get; set; }
	public ParityToleranceAttribute(string fieldName, float atol) {
		FieldName = fieldName;
		Atol = atol;
	}
}
public static class ParityToleranceRegistry {
	static readonly Dictionary<(string,string), float> tolerances;
#pragma warning disable CA1810
	static ParityToleranceRegistry() {
		tolerances = new();
		foreach(var (model_type, types) in AutoModel.models) {
			var attributes = types.modelType.GetCustomAttributes(typeof(ParityToleranceAttribute), false);
			foreach(ParityToleranceAttribute attr in attributes)
				if(attr.ModelType == null || attr.ModelType == model_type)
					tolerances[(model_type, attr.FieldName)] = attr.Atol;
		}
	}
#pragma warning restore CA1810
	public static float Get(string model_type, string fieldName) {
		if(tolerances.TryGetValue((model_type, fieldName), out var atol))
			return atol;
		throw new KeyNotFoundException($"No test tolerance is registered for {model_type}.{fieldName}.");
	}
	public static float Get(Type modelType, string fieldName) {
		if(modelType == null)
			throw new ArgumentNullException(nameof(modelType));
		return Get(AutoModel.models.FirstOrDefault(kv => kv.Value.modelType == modelType).Key ??
			throw new KeyNotFoundException($"{modelType.FullName} is not a registered model or processor type."), fieldName);
	}
}
}