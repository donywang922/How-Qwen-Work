using UnityEngine;
using UnityEngine.Rendering;
#if UDON
using MonoBehaviour = VRC.Udon.Common.Interfaces.IUdonEventReceiver;
using Graphics = VRC.SDKBase.VRCGraphics;
using AsyncGPUReadbackRequest = VRC.SDK3.Rendering.VRCAsyncGPUReadbackRequest;
#endif

namespace ShaderGPT.Udon {
public class GPTGenerator : UdonMonoBehaviour {
	public GameObject modelPrefab;
	public int maxLength = 2048;
	public float temperature = 0;
	public float repetitionPenalty = 1f;
	public int idleFrames = 0; // negative means extra steps per frame
	[Min(1)] public int prepFrames = 1;
	public MonoBehaviour eventTarget;
	public string eventMethod;

	private Material[] matEncoders;
	private Material matInput;
	private Texture2D texInput;
	private float[] costs;
	private float totalCost;
	private int maxEncoder;
	void LoadEncoder() {
		totalCost = 0f;
		costs = new float[matEncoders.Length];
		var maxScatter = 0;
		for(int i=0; i<matEncoders.Length; i++) {
			var mat = matEncoders[i];
			var name = mat.shader.name;
			var rt = (RenderTexture)mat.GetTexture("TexO");
			if(name.Contains("Scatter"))
				maxScatter = Mathf.Max(maxScatter, ScatterCapacity(mat, out var peek));
			float cost = Mathf.Max(4, rt.width) * Mathf.Max(4, rt.height);
			if(name.Contains("Conv"))
				cost *= mat.GetVector("DimX").x;
			if(name.Contains("Linear"))
				cost *= mat.GetVector("DimW").x;
			costs[i] = cost;
			totalCost += cost;
		}
		maxEncoder = maxScatter;

		var tex0 = matEncoders[0].GetTexture("TexX");
		texInput = tex0 && tex0.GetType() == typeof(Texture2D) ? (Texture2D)tex0 : null; // polyfill "as" operator
		foreach(var mat in matEncoders)
			if(mat.shaderKeywords.Length == 0) {
				matInput = mat;
				break;
			}
	}
	static int ScatterCapacity(Material mat, out int logPeek) {
		var height = (int)mat.GetVector("DimO").x;
		logPeek = mat.HasInt("LogPeek") ? mat.GetInt("LogPeek") : -1;
		if(logPeek < 0)
			return height;
		var bucketCount = Mathf.Max(1, height >> logPeek);
		var logCapacity = bucketCount - 1 + logPeek;
		return logCapacity >= 30 ? int.MaxValue : 1 << logCapacity;
	}

	private Material[] matDecoders;
	private Material matRepeat;
	private Material matGumbel;
	private Material matOutput;
	private RenderTexture bufOutput;
	void LoadDecoder() {
		var maxScatter = 0;
		foreach(var mat in matDecoders) {
			var name = mat.shader.name;
			if(name.Contains("Scatter")) {
				maxScatter = Mathf.Max(maxScatter, ScatterCapacity(mat, out var peek));
				if(peek >= 0) {
					maxLogPeek = Mathf.Max(maxLogPeek, peek);
					minLogPeek = Mathf.Min(minLogPeek, peek);
				}
			}
		}
		if(maxScatter > 0)
			maxLength = Mathf.Min(maxLength, maxScatter);

		matOutput = matDecoders[matDecoders.Length-1];
		bufOutput = (RenderTexture)matOutput.GetTexture("TexO");
		foreach(var mat in matDecoders)
			if(mat.shaderKeywords.Length == 1) {
				var keyword = mat.shaderKeywords[0];
				if(keyword == "RELU")
					matRepeat = mat;
				if(keyword == "REDUCE_MINMAX")
					matGumbel = mat;
			}
	}

	// you need to set encoderTokens or encoderInputs before Play()
	[System.NonSerialized] public int[] encoderTokens;
	[System.NonSerialized] public float[] encoderInputs;
	[System.NonSerialized] public Texture encoderImage;
	[System.NonSerialized] public int encoderIndex;
	[System.NonSerialized] public int encoderStep;
	void PrepEncoder() {
		if(encoderTokens == null && encoderInputs == null && encoderImage == null) {
			blitIndex = matEncoders.Length; // skip encoder
			return;
		}
		if(encoderInputs != null)
			SetPixelData(texInput, encoderInputs);
		else if(encoderImage != null) {
			matEncoders[0].SetTexture("TexX", encoderImage);
			matInput.SetVector("VecAdd", new Vector4(0,0,0, encoderIndex));
			encoderIndex += encoderStep;
			if(encoderIndex + encoderStep > maxEncoder)
				encoderIndex = 0;
		} else {
			var maxTokens = texInput.height;
			var numTokens = Mathf.Min(maxTokens, encoderTokens.Length);
			var tokenData = new float[maxTokens*4];
			for(int i=0; i<maxTokens; i++) {
				if(i < numTokens)
					tokenData[i*4+0] = encoderTokens[i];
				tokenData[i*4+1] = i;
				tokenData[i*4+2] = numTokens;
			}
			SetPixelData(texInput, tokenData);
		}
		blitIndex = 0;
		reservoir = 0;
	}
	private byte[] pixelData;
	void SetPixelData(Texture2D tex, float[] data) {
		Resize(ref pixelData, data.Length*4);
		System.Buffer.BlockCopy(data, 0, pixelData, 0, pixelData.Length);
		tex.LoadRawTextureData(pixelData);
		tex.Apply(false, false);
	}
	private int blitIndex;
	private float reservoir;
	void StepEncoder() {
		reservoir += totalCost / prepFrames;
		var executed = false;
		while(blitIndex < matEncoders.Length) {
			var cost = costs[blitIndex];
			if(cost > reservoir && executed)
				break;
			reservoir -= cost;
			Blit(matEncoders[blitIndex++]);
			executed = true;
		}
	}

	// you need to set inputIndex and inputTokens before Play()
	[System.NonSerialized] public int inputIndex;
	[System.NonSerialized] public int[] inputTokens;
	[System.NonSerialized] public int outputIndex;
	[System.NonSerialized] public int outputToken;
	private int maxLogPeek = -1, minLogPeek = int.MaxValue;
	private readonly int[] logNext = new int[32];
	private int recentIndexFirst, recentIndexLast;
	void PrepDecoder() {
		if(matRepeat) {
			matRepeat.SetFloat("Bias", repetitionPenalty);
			matRepeat.SetFloat("Scale", 1f/repetitionPenalty);
		}
		if(matGumbel) {
			matGumbel.SetFloat("Bias", temperature);
		}

		// detect external changes to inputIndex
		if(maxLogPeek >= 0 && recentIndexLast != inputIndex) {
			if(inputIndex < recentIndexFirst || recentIndexLast < inputIndex) {
				// rewind to the last usable block in log cache
				var best = 0;
				foreach(var x in logNext)
					if(x <= inputIndex)
						best = Mathf.Max(best, x);
				recentIndexFirst = inputIndex = best;
			}
			recentIndexLast = inputIndex;
		}
	}
	void StepDecoder() {
		var chan2 = encoderTokens == null ? 0 : encoderTokens.Length;
		// make sure the buffer stores the last token
		if(0 < inputIndex && inputIndex <= inputTokens.Length) {
			matOutput.SetVector("VecMul", Vector4.zero);
			matOutput.SetVector("VecAdd", new Vector4(inputTokens[inputIndex-1],inputIndex-1,chan2,0));
			Graphics.Blit(null, bufOutput, matOutput, 0);
		}
		if(inputIndex < inputTokens.Length) {
			matOutput.SetVector("VecMul", Vector4.zero);
			matOutput.SetVector("VecAdd", new Vector4(inputTokens[inputIndex],inputIndex,chan2,0));
		} else {
			matOutput.SetVector("VecMul", new Vector4(1,0,0,0));
			matOutput.SetVector("VecAdd", new Vector4(0,inputIndex,chan2,0));
		}
		foreach(var mat in matDecoders)
			Blit(mat);

		if(maxLogPeek >= 0 && inputIndex > 0) {
			// (inputIndex+1)-th token generation needs to process inputIndex-th token,
			// which conservatively requires the (1<<logPeek)-wide block ending at (inputIndex-1) to remain intact,
			// whose block index in log cache is ctz(1+((inputIndex-1)>>logPeek)).
			// a block is unusable if it's partially updated, i.e. (inputIndex-1) % (1<<logPeek) < (1<<logPeek)-1
			var x = 1+((inputIndex-1)>>maxLogPeek);
			logNext[Mathf.RoundToInt(Mathf.Log(x & -x, 2))] = inputIndex % (1<<maxLogPeek) == 0 ? inputIndex+1 : 0;
		}
		recentIndexLast = ++inputIndex;
		recentIndexFirst = Mathf.Max(recentIndexFirst, recentIndexLast-(1<<minLogPeek));
	}
	static void Blit(Material mat) {
		var rt = (RenderTexture)mat.GetTexture("TexO");
		Graphics.Blit(null, rt, mat, -1);
	}

	private int frameIndex;
	public void Play() {
		if(matDecoders == null) {
#if UNITY_EDITOR // avoid modifying asset in editor
			if(modelPrefab.GetInstanceID() >= 0) {
				modelPrefab = Object.Instantiate(modelPrefab);
				modelPrefab.SetActive(false);
			}
#endif
			var renderers = modelPrefab.GetComponentsInChildren<MeshRenderer>();
			Debug.Assert(renderers.Length == 1 || renderers.Length == 2);
			if(renderers.Length == 2) {
				var renderer = renderers[0];
				matEncoders = renderer.GetInstanceID() < 0 ? renderer.materials : renderer.sharedMaterials;
				LoadEncoder();
			}
			if(renderers.Length >= 1) {
				var renderer = renderers[renderers.Length-1];
				// if renderer is instanced, use ".materials" to avoid saving modification
				matDecoders = renderer.GetInstanceID() < 0 ? renderer.materials : renderer.sharedMaterials;
				LoadDecoder();
			}
		}
		if(matEncoders != null)
			PrepEncoder();
		PrepDecoder();
		frameIndex = 0;
		this.enabled = true;
	}
	public void Stop() {
		this.enabled = false;
	}
	public void Update() {
		if(matEncoders != null && blitIndex < matEncoders.Length) {
			StepEncoder();
			return;
		}
		if(frameIndex != 0) {
			frameIndex--;
			return;
		}
		frameIndex = Mathf.Max(0, idleFrames);
		for(int i=Mathf.Min(0, idleFrames); i<=0; i++) {
			if(inputIndex >= maxLength) {
				this.enabled = false;
				return;
			}
			StepDecoder();
			AsyncGPUReadback_Request(bufOutput, 0, TextureFormat.RGBAFloat);
		}
	}
	void OnGPUReadback(float[] data) {
		outputToken = Mathf.RoundToInt(data[0]);
		outputIndex = Mathf.RoundToInt(data[1]);
		if(outputIndex >= inputIndex)
			return; // skip tokens from last sequence
		if(eventTarget != null)
			eventTarget.SendMessage(eventMethod);
	}

	private float[] readbackData;
	public override void OnAsyncGpuReadbackComplete(AsyncGPUReadbackRequest request) {
		if(!(this && this.enabled))
			return;
		if(request.hasError || !request.done)
			return;
		Resize(ref readbackData, request.width*request.height*4);
		request.GetData<float>().CopyTo(readbackData);
		OnGPUReadback(readbackData);
	}
	static void Resize<T>(ref T[] array, int size) {
		if(array == null || array.Length != size)
			array = new T[size];
	}
}
}