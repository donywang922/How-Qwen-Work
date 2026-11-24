using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

namespace QwenWorld.Editor
{
    public static class QwenTextureGalleryBuilder
    {
        public const string RootName = "Qwen texture gallery";
        public const float LiveRadius = 7.4f;
        public const float StaticRadius = 9.2f;
        public const float StaticRadiusVariation = .38f;
        public const float LiveRadiusVariation = .32f;
        public const float OriginalDistanceAt90Fov = 1.55f;
        static readonly Vector2 StaticLongEdges = new Vector2(.16f, .50f);
        static readonly Vector2 LiveLongEdges = new Vector2(.14f, .56f);
        const int AtlasSize = 2048, Tile = 80, Border = 2, TilesPerRow = AtlasSize / Tile;
        static readonly float[] StaticBandHeights = {.40f, 1.06f, 1.74f, 2.48f, 3.28f, 4.12f};
        static readonly float[] StaticBandPhase = {.13f, .64f, .38f, .88f, .26f, .75f};
        static readonly float[] LiveBandHeights = {1.45f, 2.82f};
        static readonly float[] LiveBandPhase = {.12f, .61f};
        static readonly Vector2[] QuadUv = {Vector2.zero, Vector2.right, Vector2.one, Vector2.up};
        static readonly int[] QuadTriangles = {0, 1, 2, 0, 2, 3};
        public sealed class StaticGallery
        {
            public MeshRenderer preview;
            public MeshRenderer[] originals;
            public Texture2D atlas;
            public Texture2D[] sources;
        }

        public static StaticGallery AddStatic(Transform parent, Material[] steps, string modelFolder, string outputFolder)
        {
            var sources = steps.SelectMany(m => m.GetTexturePropertyNames().Select(m.GetTexture)).OfType<Texture2D>().Distinct().Where(t => AssetDatabase.GetAssetPath(t).StartsWith(modelFolder + "/", StringComparison.Ordinal)).OrderBy(t => AssetDatabase.GetAssetPath(t), StringComparer.Ordinal).ToArray();
            if (sources.Length == 0 || sources.Length > TilesPerRow * TilesPerRow)
                throw new InvalidOperationException($"Invalid static gallery count: {sources.Length}");
            Directory.CreateDirectory(outputFolder);
            AssetDatabase.Refresh();
            var shader = Shader.Find("QwenWorld/StaticTensorGallery");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Static gallery shader failed to compile.");
            string meshPath = outputFolder + "/StaticGallery.asset";
            var combined = AssetDatabase.LoadMainAssetAtPath(meshPath) as Mesh;
            if (combined == null)
            {
                combined = new Mesh{name = "Static preview mesh"};
                AssetDatabase.CreateAsset(combined, meshPath);
            }

            var assets = AssetDatabase.LoadAllAssetsAtPath(meshPath).ToDictionary(asset => asset.name);
            var materials = new Material[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                var material = SaveMaterial(assets, meshPath, "Original " + i.ToString("D3"), shader, sources[i]);
                material.SetFloat("_SignedData", AssetDatabase.GetAssetPath(sources[i]).EndsWith(".exr", StringComparison.OrdinalIgnoreCase) ? 1 : 0);
                material.SetFloat("_SurfaceOffset", -4);
                EditorUtility.SetDirty(material);
                materials[i] = material;
            }

            var atlas = BakeAtlas(materials, outputFolder + "/StaticPreview.png");
            var atlasMaterial = SaveMaterial(assets, meshPath, "Shared static preview", shader, atlas);
            var vertices = new List<Vector3>(sources.Length * 4);
            var uv = new List<Vector2>(sources.Length * 4);
            var triangles = new List<int>(sources.Length * 6);
            var originals = new MeshRenderer[sources.Length];
            var logRange = LogAreaRange(sources);
            var ring = new GameObject("Outer ring - static textures").transform;
            ring.SetParent(parent, false);
            int columns = Mathf.CeilToInt((float)sources.Length / StaticBandHeights.Length);
            float pitch = 2 * Mathf.PI / columns;
            var random = new System.Random(7329);
            for (int i = 0; i < sources.Length; i++)
            {
                var source = sources[i];
                var size = PanelSize(source.width, source.height, logRange, StaticLongEdges);
                var quad = SaveQuad(assets, meshPath, $"Static quad {source.width}x{source.height}", size.x, size.y);
                int band = i % StaticBandHeights.Length;
                float angle = SpreadAngle(i / StaticBandHeights.Length + StaticBandPhase[band], pitch, size.x, StaticRadius - StaticRadiusVariation, random);
                float radius = StaticRadius + StaticRadiusVariation * (.6f * Mathf.Sin(3 * angle + band) + .4f * Scatter(random));
                float heightRoom = Mathf.Clamp((.66f - size.y) * .5f - .035f, 0, .22f);
                float height = StaticBandHeights[band] + .15f * Mathf.Sin(2 * angle) + heightRoom * Scatter(random);
                height = Mathf.Max(height, size.y * .5f + .05f);
                var radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                var position = radial * radius + Vector3.up * height;
                var rotation = Quaternion.LookRotation(-radial);
                var layout = QwenPanelPlacement.Place(position, size, true, i);
                position = layout.position;
                rotation = layout.rotation;
                var transform = Matrix4x4.TRS(position, rotation, Vector3.one);
                int start = vertices.Count;
                var lower = new Vector2((i % TilesPerRow) * Tile + Border + .5f, (i / TilesPerRow) * Tile + Border + .5f);
                foreach (var corner in QuadUv)
                {
                    vertices.Add(transform.MultiplyPoint3x4(new Vector3((corner.x - .5f) * size.x, (corner.y - .5f) * size.y)));
                    uv.Add((lower + corner * (Tile - 2 * Border - 1)) / AtlasSize);
                }

                foreach (int index in QuadTriangles)
                    triangles.Add(start + index);
                // A depth bias puts the original above the same atlas tile on either side.
                var original = AddPanel(ring, $"{i:D3} {source.name}", quad, materials[i]);
                original.transform.SetLocalPositionAndRotation(position, rotation);
                var lod = original.gameObject.AddComponent<LODGroup>();
                lod.fadeMode = LODFadeMode.None;
                float longEdge = Mathf.Max(size.x, size.y);
                lod.SetLODs(new[]{new LOD(longEdge / (2 * OriginalDistanceAt90Fov), new Renderer[]{original})});
                lod.localReferencePoint = Vector3.zero;
                lod.size = longEdge;
                originals[i] = original;
            }

            combined.Clear();
            combined.SetVertices(vertices);
            combined.SetUVs(0, uv);
            combined.SetTriangles(triangles, 0);
            EditorUtility.SetDirty(combined);
            var preview = AddPanel(ring, "All static textures - shared atlas", combined, atlasMaterial);
            AssetDatabase.SaveAssets();
            return new StaticGallery{preview = preview, originals = originals, atlas = atlas, sources = sources};
        }

        public static MeshRenderer[] AddLive(Transform parent, RenderTexture[] buffers, string outputFolder)
        {
            string path = outputFolder + "/LiveGallery.asset";
            var quad = AssetDatabase.LoadMainAssetAtPath(path) as Mesh;
            if (quad == null)
            {
                quad = new Mesh{name = "Live quad"};
                FillQuad(quad, .58f, .50f);
                AssetDatabase.CreateAsset(quad, path);
            }

            var assets = AssetDatabase.LoadAllAssetsAtPath(path).ToDictionary(asset => asset.name);
            var shader = Shader.Find("QwenWorld/TensorCellRelief");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Live gallery shader failed to compile.");
            var logRange = LogAreaRange(buffers);
            var ring = new GameObject("Inner ring - live CRT").transform;
            ring.SetParent(parent, false);
            var panels = new MeshRenderer[buffers.Length];
            int columns = Mathf.CeilToInt((float)buffers.Length / LiveBandHeights.Length);
            float pitch = 2 * Mathf.PI / columns;
            var random = new System.Random(3819);
            for (int i = 0; i < buffers.Length; i++)
            {
                var source = buffers[i];
                var material = SaveMaterial(assets, path, "Live " + i.ToString("D3"), shader, source);
                SetLiveDisplayFormat(material, source);
                int width = BandWidth(source);
                int height = source.height * (source.width / width);
                var size = PanelSize(width, height, logRange, LiveLongEdges);
                var panelMesh = SaveQuad(assets, path, $"Live quad {width}x{height}", size.x, size.y);
                material.SetFloat("_BandWidth", width);
                if (width < source.width)
                    material.EnableKeyword("RELIEF_BANDED_LAYOUT");
                else
                    material.DisableKeyword("RELIEF_BANDED_LAYOUT");
                int band = i % LiveBandHeights.Length;
                float angle = SpreadAngle(i / LiveBandHeights.Length + LiveBandPhase[band], pitch, size.x, LiveRadius - LiveRadiusVariation, random);
                float radius = LiveRadius + LiveRadiusVariation * (.6f * Mathf.Sin(3 * angle + band) + .4f * Scatter(random));
                float elevation = LiveBandHeights[band] + .26f * Mathf.Sin(2 * angle + .35f * band) + .23f * Scatter(random);
                var radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                var layout = QwenPanelPlacement.Place(radial * radius + Vector3.up * elevation, size, false, i);
                material.SetVector("_PanelSize", new Vector4(size.x, size.y, 0, 0));
                var panel = AddPanel(ring, $"Live {i:D3} {source.name}", panelMesh, material);
                panel.transform.SetLocalPositionAndRotation(layout.position, layout.rotation);
                panels[i] = panel;
            }

            AssetDatabase.SaveAssets();
            return panels;
        }

        static Vector2 LogAreaRange(IEnumerable<Texture> textures)
        {
            var areas = textures.Select(texture => LogArea(texture.width, texture.height)).ToArray();
            return new Vector2(areas.Min(), areas.Max());
        }

        static float LogArea(int width, int height) => Mathf.Log((float)width * height, 2);
        static float Scatter(System.Random random) => (float)(2 * random.NextDouble() - 1);
        static float SpreadAngle(float slot, float pitch, float width, float minimumRadius, System.Random random)
        {
            float freeAngle = pitch * .5f - Mathf.Atan2(width * .5f, minimumRadius) - .0015f;
            return slot * pitch + Scatter(random) * Mathf.Clamp(freeAngle, 0, pitch * .26f);
        }

        static Vector2 PanelSize(int width, int height, Vector2 logRange, Vector2 longEdges)
        {
            float scale = Mathf.InverseLerp(logRange.x, logRange.y, LogArea(width, height));
            float longEdge = Mathf.Lerp(longEdges.x, longEdges.y, scale);
            return new Vector2(width, height) * (longEdge / Mathf.Max(width, height));
        }

        public static void SetLiveDisplayFormat(Material material, RenderTexture source)
        {
            material.SetFloat("_NormalizedData", GraphicsFormatUtility.IsUNormFormat(source.graphicsFormat) ? 1 : 0);
        }

        public static int BandWidth(RenderTexture texture)
        {
            if (texture.width < 512 || texture.width <= texture.height * 4)
                return texture.width;
            int best = texture.width;
            float bestError = float.PositiveInfinity;
            for (int bands = 1; bands <= texture.width; bands++)
            {
                if (texture.width % bands != 0)
                    continue;
                int width = texture.width / bands;
                float aspect = (float)width / (texture.height * bands);
                float error = Mathf.Abs(Mathf.Log(aspect / (.58f / .50f)));
                if (error >= bestError)
                    continue;
                best = width;
                bestError = error;
            }

            return best;
        }

        static Texture2D BakeAtlas(Material[] materials, string path)
        {
            var previous = RenderTexture.active;
            var target = RenderTexture.GetTemporary(AtlasSize, AtlasSize, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var pixels = new Texture2D(AtlasSize, AtlasSize, TextureFormat.RGBA32, false, true);
            var quad = new Mesh();
            FillQuad(quad, 2, 2);
            var bake = new Material(materials[0].shader);
            bake.EnableKeyword("STATIC_PREVIEW_BAKE");
            try
            {
                using (var commands = new CommandBuffer{name = "Bake static tensor previews"})
                {
                    commands.SetRenderTarget(target);
                    commands.ClearRenderTarget(false, true, Color.black);
                    commands.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                    var properties = new MaterialPropertyBlock();
                    for (int i = 0; i < materials.Length; i++)
                    {
                        properties.SetTexture("_MainTex", materials[i].mainTexture);
                        properties.SetFloat("_SignedData", materials[i].GetFloat("_SignedData"));
                        commands.SetViewport(new Rect(i % TilesPerRow * Tile, i / TilesPerRow * Tile, Tile, Tile));
                        commands.DrawMesh(quad, Matrix4x4.identity, bake, 0, 0, properties);
                    }

                    Graphics.ExecuteCommandBuffer(commands);
                }

                RenderTexture.active = target;
                pixels.ReadPixels(new Rect(0, 0, AtlasSize, AtlasSize), 0, 0, false);
                File.WriteAllBytes(path, pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(target);
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(bake);
                UnityEngine.Object.DestroyImmediate(quad);
            }

            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.sRGBTexture = false;
            importer.mipmapEnabled = false;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Bilinear;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Standalone", overridden = true, maxTextureSize = AtlasSize, format = TextureImporterFormat.BC7, compressionQuality = 100});
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Mesh SaveQuad(Dictionary<string, UnityEngine.Object> assets, string path, string name, float width, float height)
        {
            if (assets.TryGetValue(name, out var asset))
            {
                var existing = (Mesh)asset;
                if (!Mathf.Approximately(existing.bounds.size.x, width) || !Mathf.Approximately(existing.bounds.size.y, height))
                {
                    FillQuad(existing, width, height);
                    EditorUtility.SetDirty(existing);
                }

                return existing;
            }

            var quad = new Mesh{name = name};
            FillQuad(quad, width, height);
            AssetDatabase.AddObjectToAsset(quad, path);
            assets.Add(name, quad);
            return quad;
        }

        static void FillQuad(Mesh mesh, float width, float height)
        {
            mesh.vertices = QuadUv.Select(uv => new Vector3((uv.x - .5f) * width, (uv.y - .5f) * height)).ToArray();
            mesh.uv = QuadUv;
            mesh.triangles = QuadTriangles;
        }

        static Material SaveMaterial(Dictionary<string, UnityEngine.Object> assets, string path, string name, Shader shader, Texture texture)
        {
            if (!assets.TryGetValue(name, out var asset))
            {
                asset = new Material(shader)
                {name = name};
                AssetDatabase.AddObjectToAsset(asset, path);
            }

            var material = (Material)asset;
            material.shader = shader;
            material.mainTexture = texture;
            EditorUtility.SetDirty(material);
            return material;
        }

        static MeshRenderer AddPanel(Transform parent, string name, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return renderer;
        }
    }
}
