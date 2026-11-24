using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QwenWorld.Editor
{
    /// <summary>Imports the approved Blender distant asset. No procedural ring fallback.</summary>
    public static class QwenOrbitalRingMesh
    {
        public static readonly Vector3 Origin = Vector3.zero;
        // Same world-space direction used by the NASA panorama bake (yaw 66, elevation -35).
        public static readonly Vector3 EarthDirection = new Vector3(
            Mathf.Sin(66 * Mathf.Deg2Rad) * Mathf.Cos(35 * Mathf.Deg2Rad),
            -Mathf.Sin(35 * Mathf.Deg2Rad),
            Mathf.Cos(66 * Mathf.Deg2Rad) * Mathf.Cos(35 * Mathf.Deg2Rad));
        public static readonly Quaternion Rotation = Quaternion.FromToRotation(Vector3.up, EarthDirection);
        public const float Radius = QwenWalkwaysBuilder.OuterRadius;
        public const string SourceFolder = "Assets/QwenWorld/Environment/ImportedOrbitalRing";
        public const string ModelPath = SourceFolder + "/Orbital_Ring_20K_Distant.fbx";
        public static void ConfigureImport()
        {
            var model = AssetImporter.GetAtPath(ModelPath) as ModelImporter;
            if (model == null)
                throw new FileNotFoundException("Approved Blender FBX is missing", ModelPath);
            model.importAnimation = false;
            model.materialImportMode = ModelImporterMaterialImportMode.None;
            model.importNormals = ModelImporterNormals.Import;
            model.importTangents = ModelImporterTangents.CalculateMikk;
            model.meshCompression = ModelImporterMeshCompression.Off;
            model.isReadable = true;
            model.bakeAxisConversion = true;
            model.globalScale = 1;
            model.useFileScale = true;
            model.SaveAndReimport();
            foreach (string name in new[]{"BaseColor", "Normal", "Metallic", "Roughness", "Emission"})
            {
                string path = SourceFolder + "/textures/OrbitalRing_" + name + ".png";
                var texture = AssetImporter.GetAtPath(path) as TextureImporter;
                if (texture == null)
                    throw new FileNotFoundException("Missing Blender atlas", path);
                bool normal = name == "Normal", color = name == "BaseColor" || name == "Emission", scalar = name == "Metallic" || name == "Roughness";
                texture.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                texture.sRGBTexture = color;
                texture.isReadable = false;
                texture.mipmapEnabled = true;
                texture.wrapMode = TextureWrapMode.Clamp;
                texture.filterMode = FilterMode.Trilinear;
                texture.anisoLevel = 4;
                texture.alphaSource = TextureImporterAlphaSource.None;
                texture.textureCompression = TextureImporterCompression.CompressedHQ;
                int pcSize = name == "BaseColor" ? 4096 : name == "Emission" ? 1024 : 2048;
                int androidSize = name == "BaseColor" ? 2048 : 1024;
                texture.maxTextureSize = pcSize;
                texture.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Standalone", overridden = true, maxTextureSize = pcSize, format = normal ? TextureImporterFormat.BC5 : scalar ? TextureImporterFormat.BC4 : TextureImporterFormat.BC7, compressionQuality = 100});
                texture.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Android", overridden = true, maxTextureSize = androidSize, format = TextureImporterFormat.ASTC_6x6, compressionQuality = 100});
                texture.SaveAndReimport();
            }
        }

        public static void ConfigureMaterial(Material material)
        {
            string[] properties = {"_MainTex", "_NormalMap", "_MetallicMap", "_RoughnessMap", "_EmissionMap"};
            string[] names = {"BaseColor", "Normal", "Metallic", "Roughness", "Emission"};
            for (int i = 0; i < names.Length; i++)
                material.SetTexture(properties[i], AssetDatabase.LoadAssetAtPath<Texture2D>(SourceFolder + "/textures/OrbitalRing_" + names[i] + ".png"));
            material.SetFloat("_NormalStrength", .18f);
            material.SetFloat("_EmissionStrength", 3);
            material.SetColor("_KeyColor", new Color(1.65f, 1.62f, 1.55f));
            material.SetColor("_AmbientColor", new Color(.20f, .22f, .25f));
        }

        public static Mesh Build()
        {
            var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            if (model == null)
                throw new FileNotFoundException("FBX did not import", ModelPath);
            var filters = model.GetComponentsInChildren<MeshFilter>(true);
            if (filters.Length != 1 || filters[0].sharedMesh.subMeshCount != 1)
                throw new InvalidOperationException("Expected one imported mesh and one submesh.");
            var filter = filters[0];
            var source = filter.sharedMesh;
            if (source.triangles.Length / 3 != 19954)
                throw new InvalidOperationException("Wrong Blender variant or missing source geometry.");
            var sourceMatrix = filter.transform.localToWorldMatrix;
            var imported = source.vertices.Select(p => sourceMatrix.MultiplyPoint3x4(p)).ToArray();
            var bounds = new Bounds(imported[0], Vector3.zero);
            foreach (var p in imported)
                bounds.Encapsulate(p);
            // FBX axis conversion varies with exporter versions. Detect the thin axis once,
            // then use only a proper rotation to put the ring plane in Unity XZ.
            int thin = bounds.size.x < bounds.size.y && bounds.size.x < bounds.size.z ? 0 : bounds.size.z < bounds.size.y ? 2 : 1;
            var correction = thin == 0 ? Quaternion.FromToRotation(Vector3.right, Vector3.up) : thin == 2 ? Quaternion.FromToRotation(Vector3.forward, Vector3.up) : Quaternion.identity;
            float sourceOuterRadius = imported.Max(p =>
            {
                var aligned = correction * p;
                return new Vector2(aligned.x,aligned.z).magnitude;
            });
            float worldScale = Radius / sourceOuterRadius;
            var pose = Matrix4x4.TRS(Origin, Rotation * correction, Vector3.one * worldScale) * sourceMatrix;
            var vertices = source.vertices.Select(p => pose.MultiplyPoint3x4(p)).ToArray();
            var normalMatrix = pose.inverse.transpose;
            var normals = source.normals.Select(n => normalMatrix.MultiplyVector(n).normalized).ToArray();
            var tangents = source.tangents.Select(t =>
            {
                var d = pose.MultiplyVector(new Vector3(t.x, t.y, t.z)).normalized;
                return new Vector4(d.x, d.y, d.z, t.w * (pose.determinant < 0 ? -1 : 1));
            }).ToArray();
            var indices = source.triangles;
            if (pose.determinant < 0)
                for (int i = 0; i < indices.Length; i += 3)
                {
                    int b = indices[i + 1];
                    indices[i + 1] = indices[i + 2];
                    indices[i + 2] = b;
                }

            var mesh = new Mesh{name = "OrbitalRing", indexFormat = vertices.Length > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16};
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = source.uv;
            mesh.triangles = indices;
            if (tangents.Length == vertices.Length)
                mesh.tangents = tangents;
            else
                mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            Directory.CreateDirectory(QwenProject.DataPath("validation/sky/imported-ring"));
            File.WriteAllText(QwenProject.DataPath("validation/sky/imported-ring/unity-import.json"), JsonUtility.ToJson(new ImportReport{model = ModelPath, sourceTriangles = source.triangles.Length / 3, sourceVertices = source.vertexCount, sourceBounds = bounds.size, sourceThinAxis = thin, axisCorrection = correction.eulerAngles, worldScale = worldScale, worldBounds = mesh.bounds.size, worldTriangles = mesh.triangles.Length / 3, doubleSided = true}, true));
            return mesh;
        }

        [Serializable]
        class ImportReport
        {
            public string model;
            public int sourceTriangles, sourceVertices, sourceThinAxis, worldTriangles;
            public Vector3 sourceBounds, axisCorrection, worldBounds;
            public float worldScale;
            public bool doubleSided;
        }
    }
}
