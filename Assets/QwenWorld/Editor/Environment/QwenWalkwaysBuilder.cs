using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UdonSharp;
using UdonSharpEditor;
using VRC.SDK3.Components;

namespace QwenWorld.Editor
{
    public static class QwenWalkwaysBuilder
    {
        public const float WhiteRadius = 11.2f, OuterRadius = WhiteRadius * 2, OuterHeight = -.18f;
        public static readonly Vector3 SpawnPosition = new Vector3(0, -.14f, -11.8f);
        const string Folder = QwenSceneInstaller.Folder;

        [MenuItem("Qwen World/Environment/Apply outer ground size only")]
        public static void ResizeOuterGroundBatch()
        {
            var scene = EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
            var outer = GameObject.Find("Outer translucent hex ground");
            if (outer == null) throw new InvalidOperationException("Outer ground is missing");
            var scale = outer.transform.localScale;
            scale.x = scale.z = OuterRadius;
            outer.transform.localScale = scale;
            var bounds = outer.GetComponent<MeshFilter>().sharedMesh.bounds;
            float diameter = bounds.size.x * scale.x;
            var spawn = UnityEngine.Object.FindObjectOfType<QwenObservationPlatform>().recoveryPoint.position;
            float spawnRadius = new Vector2(spawn.x,spawn.z).magnitude;
            if (Mathf.Abs(diameter - 44.8f) > .01f || spawnRadius <= WhiteRadius || spawnRadius >= OuterRadius
                || !outer.GetComponent<MeshCollider>().enabled)
                throw new InvalidOperationException("Outer ground dimensions, spawn or collision are invalid");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            string output = QwenProject.DataPath("validation/observation-platform/expanded-outer-ground.png");
            var gallery = GameObject.Find(QwenTextureGalleryBuilder.RootName);
            bool wasGallery = gallery != null && gallery.activeSelf;
            try
            {
                if (gallery != null) gallery.SetActive(false);
                QwenPreview.Capture(output, -QwenOrbitalRingMesh.EarthDirection * 50, Vector3.zero, 55, 1600, 1100);
            }
            finally { if (gallery != null) gallery.SetActive(wasGallery); }
            File.WriteAllText(Path.ChangeExtension(output,".json"),
                "{\"outerDiameter\":44.8,\"whiteDiameter\":22.4,\"step\":0.18,\"spawnOnOuterGround\":true,\"packagesUpdated\":false}");
            Debug.Log("QWEN_OUTER_GROUND_RESIZED diameter=" + diameter + " spawnRadius=" + spawnRadius);
        }

        public static void Configure(Transform parent, QwenLocalMenu menu, VRCSceneDescriptor descriptor)
        {
            var mesh = UnitDisk();
            var outerMaterial = Material("OuterHexGlass", .16f, .85f, 0, 0);
            var acrylicMaterial = Material("ObservationAcrylic", .095f, .06f, .8f, .015f);
            var outer = Deck("Outer translucent hex ground", parent, mesh, outerMaterial, OuterRadius, .10f, OuterHeight);
            var deck = Deck("Personal acrylic observation deck", parent, mesh, acrylicMaterial, WhiteRadius, .06f, -.04f);
            var body = deck.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            var controls = new GameObject("Personal observation controls").AddUdonSharpComponent<QwenObservationPlatform>();
            controls.transform.SetParent(parent, false);
            controls.deck = deck.transform;
            controls.deckRenderer = deck.GetComponent<MeshRenderer>();
            controls.deckCollider = deck.GetComponent<MeshCollider>();
            controls.deckRenderer.enabled = false;
            controls.deckCollider.enabled = false;
            controls.heightSlider = menu.heightSlider;
            controls.heightLabel = menu.heightLabel;
            controls.radius = WhiteRadius;
            controls.maximumHeight = 5.5f;
            menu.observation = controls;
            if (descriptor != null && descriptor.spawns != null && descriptor.spawns.Length > 0)
            {
                descriptor.spawns[0].position = SpawnPosition;
                descriptor.spawns[0].rotation = Quaternion.identity;
                controls.recoveryPoint = descriptor.spawns[0];
            }
        }

        static GameObject Deck(string name, Transform parent, Mesh mesh, Material material, float radius, float thickness, float height)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.up * height;
            go.transform.localScale = new Vector3(radius, thickness, radius);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            go.GetComponent<MeshCollider>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            return go;
        }

        static Material Material(string name, float opacity, float grid, float anisotropy, float grain)
        {
            var material = QwenGraphDisplayBuilder.Material(Folder + "/" + name + ".mat", "QwenWorld/AcrylicHexGround");
            material.SetColor("_Color", new Color(.10f, .19f, .24f, 1));
            material.SetFloat("_Opacity", opacity);
            material.SetFloat("_GridStrength", grid);
            material.SetFloat("_HexSize", anisotropy > 0 ? 1.662769f : .85f);
            material.SetFloat("_BrushAnisotropy", anisotropy);
            material.SetFloat("_BrushGrainStrength", grain);
            material.SetTexture("_BrushDirectionMap", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/HexBrushDirections.png"));
            material.SetTexture("_DetailTex", AssetDatabase.LoadAssetAtPath<Texture2D>(Folder + "/SurfaceGrain.asset"));
            EditorUtility.SetDirty(material);
            return material;
        }

        static Mesh UnitDisk()
        {
            const int segments = 192;
            var vertices = new Vector3[segments * 12];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[vertices.Length];
            int offset = 0;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                var p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                var q = new Vector3(Mathf.Cos(b), 0, Mathf.Sin(b));
                var down = Vector3.down;
                var face = new[] { Vector3.zero, q, p, down, p + down, q + down,
                    p, q, q + down, p, q + down, p + down };
                for (int j = 0; j < face.Length; j++)
                {
                    vertices[offset] = face[j];
                    uv[offset] = new Vector2(face[j].x, face[j].z) * .5f + Vector2.one * .5f;
                    triangles[offset] = offset;
                    offset++;
                }
            }
            string path = Folder + "/CircularGlassDeck.asset";
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null) { mesh = new Mesh { name = "192 segment circular glass slab" }; AssetDatabase.CreateAsset(mesh, path); }
            mesh.Clear();
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
