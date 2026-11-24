using System;
using System.IO;
using System.Linq;
using ShaderGPT.Udon;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Components;

namespace QwenWorld.Editor
{
    /// <summary>Creates the single authored world from the full Q4 graph and exhibit installers.</summary>
    public static class QwenWorldBootstrap
    {
        [MenuItem("Qwen World/Setup/Recreate main world from model")]
        public static void Build()
        {
            QwenProject.EnsureAssetFolder(QwenProject.Root + "/Scenes");
            QwenModelPipeline.Compile();
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var descriptor = new GameObject("World descriptor").AddComponent<VRCSceneDescriptor>();
            var spawn = new GameObject("Spawn").transform;
            spawn.position = new Vector3(0, .03f, -2.6f);
            descriptor.spawns = new[]{spawn};
            var light = new GameObject("Laboratory key light").AddComponent<Light>();
            light.type = LightType.Directional;
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            floor.GetComponent<Renderer>().sharedMaterial = AssetDatabase.LoadAssetAtPath<Material>(QwenSceneInstaller.Folder + "/White.mat");
            var root = new GameObject("Qwen shared session");
            var controller = root.AddUdonSharpComponent<QwenWorldController>();
            var runner = new GameObject("PC texture execution").AddUdonSharpComponent<QwenTextureRunner>();
            runner.transform.SetParent(root.transform);
            var tokenizer = new GameObject("PC tokenizer").AddUdonSharpComponent<GPTTokenizer>();
            tokenizer.transform.SetParent(root.transform);
            tokenizer.tokenizerJson = AssetDatabase.LoadAssetAtPath<TextAsset>(QwenProject.ModelFolder + "/tokenizer.json");
            controller.runner = runner;
            controller.tokenizer = tokenizer;
            runner.controller = controller;
            runner.vocabularySize = JsonUtility.FromJson<VocabConfig>(File.ReadAllText(QwenProject.ModelFolder + "/config.json")).vocab_size;
            runner.contextLength = 512;
            runner.passesPerFrame = 64;
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(QwenModelPipeline.PrefabPath);
            if (prefab == null)
                throw new FileNotFoundException(QwenModelPipeline.PrefabPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            model.name = "PC MODEL DATA - excluded from Android";
            foreach (var renderer in model.GetComponentsInChildren<MeshRenderer>())
                renderer.enabled = false;
            runner.steps = model.GetComponentsInChildren<MeshRenderer>().Last().sharedMaterials;
            runner.buffers = runner.steps.SelectMany(m => m.GetTexturePropertyNames().Select(m.GetTexture)).OfType<RenderTexture>().Distinct().ToArray();
            runner.controlMaterial = QwenGraphDisplayBuilder.Material(QwenProject.Root + "/Generated/TokenControl.mat", "QwenWorld/TokenControl");
            runner.clearMaterial = QwenGraphDisplayBuilder.Material(QwenProject.Root + "/Generated/ClearTensor.mat", "QwenWorld/ClearTensor");
            new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem), typeof(UnityEngine.EventSystems.StandaloneInputModule));
            var camera = new GameObject("Editor preview camera").AddComponent<Camera>();
            camera.gameObject.tag = "EditorOnly";
            camera.transform.position = new Vector3(3, 2.8f, -4.4f);
            camera.transform.LookAt(new Vector3(0, .9f, 0));
            camera.clearFlags = CameraClearFlags.Skybox;
            QwenSkyBuilder.ApplyToCurrentScene();
            QwenStandeeBuilder.AddToCurrentScene();
            QwenGalleryBuilder.Add(runner, QwenProject.ModelFolder);
            QwenMenuBuilder.AddToCurrentScene();
            QwenSceneInstaller.BuildScene();
            QwenProject.SaveUdonFields(scene);
            EditorSceneManager.SaveScene(scene, QwenProject.MainScene);
            EditorBuildSettings.scenes = new[]{new EditorBuildSettingsScene(QwenProject.MainScene, true)};
            AssetDatabase.SaveAssets();
        }

        [Serializable]
        private class VocabConfig
        {
            public int vocab_size;
        }
    }
}
