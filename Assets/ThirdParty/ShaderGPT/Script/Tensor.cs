using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Experimental.Rendering;
using System.Diagnostics.CodeAnalysis;

namespace ShaderGPT {
public readonly struct Tensor : System.IEquatable<Tensor> {
	private readonly Texture tex;
	public Tensor(Texture tex) => this.tex = tex;
	public static implicit operator Tensor(Texture tex) => new(tex);
	public static implicit operator bool(Tensor t) => t.tex;
	public static explicit operator Texture(Tensor t) => t.tex;
	public static bool operator == (Tensor a, Tensor b) => a.tex == b.tex;
	public static bool operator != (Tensor a, Tensor b) => a.tex != b.tex;
	[ExcludeFromCodeCoverage]
	public bool Equals(Tensor other) => this == other;
	[ExcludeFromCodeCoverage]
	public override bool Equals(object obj) => obj is Tensor tex && this == tex;
	[ExcludeFromCodeCoverage]
	public override int GetHashCode() => tex.GetHashCode();
	public GraphicsFormat dtype => tex.graphicsFormat;
	public static Tensor operator ~(Tensor t) {
		TensorContext.ScopeRelease(t);
		return t;
	}

	// shape
	public Vector2Int shape => new(size0, size1);
	public int size0 {
		get => (tex.height >> lod) >> tile1; // tiling shifts LSBs of size1 into size0 to support size1 > 16384
		internal set => tile1 = (int)Mathf.Log((tex.height >> lod) / value, 2); // infer tiling from size0
	}
	public int size1 => (tex.width >> lod) << tile1;
	internal int tile1 {
		get => (int)tex.mipMapBias; set => tex.mipMapBias = value; // repurposed to store tiling
	}
	internal int lod => tex.mipmapCount-1;
	public TensorView this[System.Range range0, System.Range range1] => ((TensorView)this)[range0, range1];
	public (TensorView,TensorView) Chunk2(int axis) => ((TensorView)this).Chunk2(axis);
	public (TensorView,TensorView,TensorView) Chunk3(int axis) => ((TensorView)this).Chunk3(axis);
	public (TensorView,TensorView) Split((int x,int y) sizes, int axis) => ((TensorView)this).Split(sizes, axis);
	public (TensorView,TensorView,TensorView) Split((int x,int y,int z) sizes, int axis) => ((TensorView)this).Split(sizes, axis);

	// data
	public System.ReadOnlySpan<T> GetData<T>() where T : struct => ((TensorView)this).GetData<T>();
	public void SetData<T>(T[] data) where T : struct {
		if(tile1 != 0)
			throw new System.InvalidOperationException("SetData() doesn't support tiled texture");
		var tex2d = (Texture2D)tex;
		tex2d.SetPixelData(data, lod, 0);
		tex2d.Apply(updateMipmaps:false, makeNoLongerReadable:true);
	}
}
public readonly struct TensorView : System.IEquatable<TensorView> {
	private readonly (Tensor t, int s0, int s1, int o0, int o1) data;
	public TensorView(Tensor t, int size0, int size1, int offset0=0, int offset1=0)
		=> data = (t, size0, size1, offset0, offset1);
	public static implicit operator TensorView(Tensor t) => t ? new TensorView(t, t.size0, t.size1) : default;
	public static implicit operator TensorView(Texture tex) => new Tensor(tex);
	public static implicit operator bool(TensorView view) => view.data.t;
	public static bool operator == (TensorView a, TensorView b) => a.data == b.data;
	public static bool operator != (TensorView a, TensorView b) => a.data != b.data;
	[ExcludeFromCodeCoverage]
	public bool Equals(TensorView other) => this == other;
	[ExcludeFromCodeCoverage]
	public override bool Equals(object obj) => obj is TensorView view && this == view;
	[ExcludeFromCodeCoverage]
	public override int GetHashCode() => data.GetHashCode();
	public Tensor tensor => data.t;
	public GraphicsFormat dtype => data.t.dtype;
	public static TensorView operator ~(TensorView t) {
		TensorContext.ScopeRelease(t.tensor);
		return t;
	}

	// shape
	public Vector2Int shape => new(data.s0, data.s1);
	public int size0 => data.s0;
	public int size1 => data.s1;
	public int offset0 => data.o0;
	public int offset1 => data.o1;
	internal int lod => data.t.lod;
	public TensorView this[System.Range range0, System.Range range1] { get {
		var (offset0, size0) = range0.GetOffsetAndLength(data.s0);
		var (offset1, size1) = range1.GetOffsetAndLength(data.s1);
		return new TensorView(data.t, size0, size1, data.o0+offset0, data.o1+offset1);
	}}
	public (TensorView,TensorView) Chunk2(int axis) => axis == 1
		? (this[.., ..(size1/2)], this[.., (size1/2)..])
		: (this[..(size0/2), ..], this[(size0/2).., ..]);
	public (TensorView,TensorView,TensorView) Chunk3(int axis) => axis == 1
		? (this[.., ..(size1/3)], this[.., (size1/3)..(2*size1/3)], this[.., (2*size1/3)..])
		: (this[..(size0/3), ..], this[(size0/3)..(2*size0/3), ..], this[(2*size0/3).., ..]);
	public (TensorView,TensorView) Split((int x,int y) sizes, int axis) => axis == 1
		? (this[.., ..sizes.x], this[.., sizes.x..(sizes.x+sizes.y)])
		: (this[..sizes.x, ..], this[sizes.x..(sizes.x+sizes.y), ..]);
	public (TensorView,TensorView,TensorView) Split((int x,int y,int z) sizes, int axis) => axis == 1
		? (this[.., ..sizes.x], this[.., sizes.x..(sizes.x+sizes.y)], this[.., (sizes.x+sizes.y)..(sizes.x+sizes.y+sizes.z)])
		: (this[..sizes.x, ..], this[sizes.x..(sizes.x+sizes.y), ..], this[(sizes.x+sizes.y)..(sizes.x+sizes.y+sizes.z), ..]);
	public TensorView Split(int size, int idx, int axis) => axis == 1
		? this[.., (idx*size)..Mathf.Min((idx+1)*size, size1)]
		: this[(idx*size)..Mathf.Min((idx+1)*size, size0), ..];

	// data
	public System.ReadOnlySpan<T> GetData<T>() where T : struct {
		if((Texture)tensor is RenderTexture rt && rt.mipmapCount > 1) {
			if(!rt.autoGenerateMips)
				rt.GenerateMips();
			else { // trigger auto generation with a dummy blit
				var tmp = RenderTexture.GetTemporary(1, 1, 0, rt.graphicsFormat);
				Graphics.Blit(rt, tmp);
				RenderTexture.ReleaseTemporary(tmp);
			}
		}
		var tile1 = tensor.tile1;
		var y0 = offset0<<tile1;
		var y1 = (offset0+size0)<<tile1;
		var x0 = offset1>>tile1;
		var x1 = (offset1+size1+(1<<tile1)-1)>>tile1;
		var req = AsyncGPUReadback.Request((Texture)tensor, lod, x0, x1-x0, y0, y1-y0, 0, 1, ((Texture)tensor).graphicsFormat);
		req.WaitForCompletion();
		if(req.hasError)
			throw new System.InvalidOperationException("GetData() failed");

		var data = req.GetData<T>();
		if(tile1 != 0) {
			var output = data.ToArray();
			for(int p=0,i=offset0; i<offset0+size0; i++)
			for(int     j=offset1; j<offset1+size1; j++) {
				var y = (i<<tile1) + (j&((1<<tile1)-1)) - y0;
				var x = (j>>tile1) - x0;
				var q = (y * (x1-x0) + x) * 4;
				output[p++] = data[q++];
				output[p++] = data[q++];
				output[p++] = data[q++];
				output[p++] = data[q++];
			}
			data.CopyFrom(output);
		}
		return data;
	}
}
public static class TensorFormatter {
	const int precision = 4;
	const int edgeitems = 3;
#pragma warning disable CA1305, CA1834
	static void AppendFormat(System.Text.StringBuilder sb, System.ReadOnlySpan<float> data, int size0, int size1, bool summarize) {
		var summarize0 = summarize && size0 > 4*edgeitems;
		var summarize1 = summarize && size1 > 4*edgeitems;
		var format0 = $"{{0:F{precision}}}";
		var format1 = $", {{0:F{precision}}}";
		sb.Append("[\n");
		for(int i=0; i<size0; i++) {
			if(summarize0 && edgeitems <= i && i < size0-edgeitems) {
				if(i == edgeitems)
					sb.Append("...,\n");
				continue;
			}
			var row = data.Slice(i*size1, size1);
			sb.Append("[");
			if(0 < size1)
				sb.AppendFormat(format0, row[0]);
			for(int n=summarize1?edgeitems:size1, j=1; j<n; j++)
				sb.AppendFormat(format1, row[j]);
			if(summarize1) {
				sb.Append(", ...");
				for(int j=size1-edgeitems; j<size1; j++)
					sb.AppendFormat(format1, row[j]);
			}
			sb.Append("],\n");
		}
		sb.Append("]");
	}
	public static string Format(System.ReadOnlySpan<float> data, int size0, int size1, bool full=false, string prefix=null) {
		var sb = new System.Text.StringBuilder();
		sb.Append(prefix);
		sb.AppendFormat("tensor(shape=({0}, {1}), ", size0, size1);
		AppendFormat(sb, data, size0, size1, summarize:!full);
		sb.Append(")");
		return sb.ToString();
	}
#pragma warning restore CA1305, CA1834
}
}