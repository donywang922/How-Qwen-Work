using System;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace QwenWorld.Editor
{
    public static class QwenNativeTestScene
    {
        public static string Create()
        {
            var reference = QwenWorldReference.Load();
            const string directory = QwenProject.Root + "/Tests/";
            foreach (string name in new[]{"QwenClientSelfTest", "QwenExhibitNativeBenchmark"})
            {
                string path = directory + name + ".asset";
                if (AssetDatabase.LoadAssetAtPath<UdonSharpProgramAsset>(path) != null)
                    continue;
                var asset = ScriptableObject.CreateInstance<UdonSharpProgramAsset>();
                asset.sourceCsScript = AssetDatabase.LoadAssetAtPath<MonoScript>(directory + name + ".cs");
                AssetDatabase.CreateAsset(asset, path);
            }

            QwenModelPipeline.Compile();
            var scene = EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
            var controller = QwenProject.Components<QwenWorldController>(scene).Single();
            var test = new GameObject("SDK test only - inference regression").AddUdonSharpComponent<QwenClientSelfTest>();
            test.controller = controller;
            test.languages = reference.Select(c => c.lang).ToArray();
            test.questions = reference.Select(c => c.question).ToArray();
            test.expected = reference.SelectMany(c => c.generated_ids).ToArray();
            test.offsets = new int[reference.Length + 1];
            for (int i = 0; i < reference.Length; i++) test.offsets[i + 1] = test.offsets[i] + reference[i].generated_ids.Length;
            test.observation = QwenProject.Components<QwenObservationPlatform>(scene).Single();
            var benchmark = new GameObject("SDK test only - exhibit benchmark").AddUdonSharpComponent<QwenExhibitNativeBenchmark>();
            benchmark.controller = controller;
            benchmark.exhibit = controller.exhibit;
            test.benchmark = benchmark;
            var panels = GameObject.Find("Inner ring - live CRT").GetComponentsInChildren<MeshRenderer>(true);
            benchmark.focusPanel = panels.OrderBy(r => Mathf.Abs(r.transform.position.y - 1.65f) * 2 - Mathf.Log((float)r.sharedMaterial.mainTexture.width * r.sharedMaterial.mainTexture.height) * .03f).First().transform;
            QwenProject.SaveUdonFields(scene);
            QwenProject.EnsureAssetFolder(QwenProject.Root + "/Generated/Build");
            // Save As changes the active path without destroying newly created Udon proxies.
            // The authored scene file is untouched, and SDK delayed setup can finish normally.
            EditorSceneManager.SaveScene(scene, QwenProject.NativeTestScene);
            return QwenProject.NativeTestScene;
        }
    }
}
