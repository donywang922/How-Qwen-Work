using System;
using System.IO;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using VRC.SDK3.Components;
using VRC.SDKBase.Network;

namespace QwenWorld.Editor
{
    public static class QwenSceneInstaller
    {
        public const string RootName = "White Qwen exhibit";
        public const string Folder = "Assets/QwenWorld/Environment/WhiteExhibit";
        public static readonly string Results = QwenProject.DataPath("validation/full-project");
        // Platform and test scenes are generated from this single authored scene.
        public static readonly string[] ActiveSceneNames = {"QwenLaboratory"};
        public static string[] ActiveScenePaths => ActiveSceneNames.Select(name => "Assets/QwenWorld/Scenes/" + name + ".unity").ToArray();
        static int sceneIndex;
        static Action completed;
        static Font font;
        [MenuItem("Qwen World/Scene/Rebuild main exhibit")]
        public static void Install()
        {
            Begin(null);
        }

        public static void InstallBatch()
        {
            Begin(() =>
            {
                Debug.Log("QWEN_COMPLETE_INSTALL_FINISHED");
                EditorApplication.Exit(0);
            });
        }

        public static void DiagnoseShadersBatch()
        {
            bool failed = false;
            foreach (string name in new[]{"QwenWorld/LocalDataFlow", "QwenWorld/WhiteLayerCore", "QwenWorld/WhiteMotionBars", "QwenWorld/TensorCellRelief", "QwenWorld/OrbitalRingPBR", "QwenWorld/WhiteSatinFloor", "QwenWorld/QwenGirlStandee", "QwenWorld/LaserKeyboard", "QwenWorld/UI/GlassScreen", "QwenWorld/UI/GlassText"})
            {
                var shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogError("Missing " + name);
                    failed = true;
                    continue;
                }

                foreach (var message in ShaderUtil.GetShaderMessages(shader))
                    Debug.Log(name + " line " + message.line + ": " + message.message);
                failed |= ShaderUtil.ShaderHasError(shader);
            }

            EditorApplication.Exit(failed ? 1 : 0);
        }

        public static void Begin(Action onComplete)
        {
            if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;
            Directory.CreateDirectory(Results);
            Directory.CreateDirectory(Folder);
            AssetDatabase.Refresh();
            QwenModelPipeline.Compile();
            completed = onComplete;
            sceneIndex = 0;
            NextScene();
        }

        static void NextScene()
        {
            try
            {
                string[] scenes = ActiveSceneNames;
                if (sceneIndex == scenes.Length)
                {
                    AssetDatabase.SaveAssets();
                    EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                    EditorApplication.delayCall += () => QwenLighting.BeginBake(completed);
                    return;
                }

                string name = scenes[sceneIndex++], path = "Assets/QwenWorld/Scenes/" + name + ".unity";
                var scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                QwenSkyBuilder.ApplyToCurrentScene(false);
                var controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
                if (controller.runner.supportsInference)
                {
                    string model = Path.GetDirectoryName(AssetDatabase.GetAssetPath(controller.runner.steps[0])).Replace('\\', '/');
                    QwenGalleryBuilder.Add(controller.runner, model);
                }

                QwenMenuBuilder.AddToCurrentScene();
                BuildScene();
                foreach (var proxy in UnityEngine.Object.FindObjectsOfType<UdonSharpBehaviour>(true))
                    UdonSharpEditorUtility.CopyProxyToUdon(proxy, ProxySerializationPolicy.All);
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                Debug.Log("QWEN_COMPLETE_SCENE_INSTALLED " + name);
                EditorApplication.delayCall += () => EditorApplication.delayCall += NextScene;
            }
            catch (Exception error)
            {
                Directory.CreateDirectory(Results);
                File.WriteAllText(Results + "/failure.txt", error.ToString());
                Debug.LogException(error);
                if (Application.isBatchMode)
                    EditorApplication.Exit(1);
            }
        }

        public static void BuildScene()
        {
            Directory.CreateDirectory(Folder);
            Directory.CreateDirectory(Results);
            AssetDatabase.Refresh();
            font = AssetDatabase.LoadAssetAtPath<Font>("Assets/QwenWorld/Fonts/NotoSansCJKsc-Regular.otf");
            var scene = SceneManager.GetActiveScene();
            var controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
            var runner = controller.runner;
            var menu = UnityEngine.Object.FindObjectOfType<QwenLocalMenu>();
            var old = scene.GetRootGameObjects().FirstOrDefault(go => go.name == RootName);
            if (old != null)
                UnityEngine.Object.DestroyImmediate(old);
            var root = new GameObject(RootName);
            var state = new GameObject("Shared presentation state").AddUdonSharpComponent<QwenExhibitState>();
            state.transform.SetParent(root.transform, false);
            var social = new GameObject("Local player panels").AddUdonSharpComponent<QwenSocialUI>();
            social.transform.SetParent(root.transform, false);
            var display = new GameObject("Local compute display").AddUdonSharpComponent<QwenComputeDisplay>();
            display.transform.SetParent(root.transform, false);
            state.controller = controller;
            state.localMenu = menu;
            state.social = social;
            state.center = root.transform;
            social.exhibit = state;
            social.controller = controller;
            social.center = root.transform;
            display.exhibit = state;
            display.controller = controller;
            display.runner = runner;
            controller.exhibit = state;
            runner.computeDisplay = display;
            menu.exhibit = state;
            var white = QwenGraphDisplayBuilder.Material(Folder + "/White.mat", "Standard");
            foreach (var go in scene.GetRootGameObjects().Where(go => go.name == "Floor" || go.name.StartsWith("Grid X") || go.name.StartsWith("Grid Z")).ToArray())
                UnityEngine.Object.DestroyImmediate(go);
            var galleryFloor = AllTransforms().FirstOrDefault(t => t.name == "Gallery walkable floor");
            if (galleryFloor != null)
                UnityEngine.Object.DestroyImmediate(galleryFloor.gameObject);
            var floor = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            floor.name = "Floor";
            floor.transform.position = new Vector3(0, -.08f, 0);
            floor.transform.localScale = new Vector3(22.4f, .08f, 22.4f);
            UnityEngine.Object.DestroyImmediate(floor.GetComponent<CapsuleCollider>());
            floor.AddComponent<MeshCollider>().sharedMesh = floor.GetComponent<MeshFilter>().sharedMesh;
            floor.GetComponent<Renderer>().sharedMaterial = white;
            var tableGeo = new QwenExhibitGeometry();
            tableGeo.Ring(QwenConsoleVisuals.InnerRadius, QwenConsoleVisuals.OuterRadius, .025f, 0, new Vector3(0, .94f, 0), 160);
            var table = MeshObject("Floating circular desk", root.transform, tableGeo.Save(Folder + "/Desk.asset", "Floating white annular desk"), white);
            table.AddComponent<MeshCollider>().sharedMesh = table.GetComponent<MeshFilter>().sharedMesh;
            var coreGeo = new QwenExhibitGeometry();
            for (int layer = 0; layer < 24; layer++)
            {
                float radius = .35f + layer * .032f;
                coreGeo.Ring(radius - .008f, radius + .008f, .008f, layer, Vector3.zero);
            }

            var core = MeshObject("Twenty four unconnected layers", root.transform, coreGeo.Save(Folder + "/Core.asset", "24 white solid rings", new Bounds(Vector3.zero, Vector3.one * 2.4f)), QwenGraphDisplayBuilder.Material(Folder + "/Core.mat", "QwenWorld/WhiteLayerCore"));
            core.transform.localPosition = QwenPanelPlacement.CoreCenter;
            display.coreRenderer = core.GetComponent<Renderer>();
            var barsGeo = new QwenExhibitGeometry();
            barsGeo.Bars();
            display.barsRenderer = MeshObject("Six white moving bars", root.transform, barsGeo.Save(Folder + "/Bars.asset", "Six morphing white bars", new Bounds(new Vector3(0, 1, 0), new Vector3(2.5f, 6, 2.5f))), QwenGraphDisplayBuilder.Material(Folder + "/Bars.mat", "QwenWorld/WhiteMotionBars")).GetComponent<Renderer>();
            var orbital = AllTransforms().FirstOrDefault(t => t.name == "Orbital ring - imported Blender distant asset");
            // The shader whites only armour atlas regions, preserving the exposed framework.
            if (orbital != null)
            {
                var mat = orbital.GetComponent<Renderer>().sharedMaterial;
                mat.SetFloat("_WhitePaint", 1);
                EditorUtility.SetDirty(mat);
            }

            var descriptor = UnityEngine.Object.FindObjectOfType<VRCSceneDescriptor>();
            if (descriptor != null && descriptor.spawns != null && descriptor.spawns.Length > 0)
            {
                descriptor.spawns[0].position = new Vector3(0, .03f, -2.6f);
                descriptor.spawns[0].rotation = Quaternion.identity;
            }

            var camera = GameObject.Find("Editor preview camera");
            if (camera != null)
            {
                camera.transform.position = new Vector3(4, 2.7f, -6);
                camera.transform.LookAt(new Vector3(0, 1.8f, 0));
            }

            BuildKeyboard(root.transform, social, white);
            BuildInput(root.transform, social, controller);
            BuildPanels(social);
            if (runner.supportsInference)
            {
                QwenRunnerMaterials.Configure(runner);
                string model = "Full";
                string output = Folder + "/" + model;
                Directory.CreateDirectory(output);
                AssetDatabase.Refresh();
                QwenGraphDisplayBuilder.Build(display, output);
            }

            var legacy = GameObject.Find("Qwen Laboratory interface");
            if (legacy != null)
                UnityEngine.Object.DestroyImmediate(legacy);
            QwenSurfaceMaterials.ApplyToCurrentScene();
            QwenWalkwaysBuilder.Configure(root.transform, menu, descriptor);
            QwenLocalizationBuilder.ConfigureScene();
            if (File.Exists("Assets/QwenWorld/Environment/SpawnLounge/ReferenceAtlas.png"))
                QwenSpawnLoungeBuilder.ApplyToCurrentScene();
            menu.textureRoots = AllTransforms().Where(t => t.name == "Outer ring - static textures" || t.name == "Inner ring - live CRT").Select(t => t.gameObject).ToArray();
            if (descriptor != null)
            {
                descriptor.NetworkIDCollection = new System.Collections.Generic.List<NetworkIDPair>();
                NetworkIDAssignment.ConfigureNetworkIDs(descriptor, out var errors, NetworkIDAssignment.SetError.IncompatibleTypes);
                if (errors.Count > 0)
                    throw new InvalidOperationException("Network IDs failed: " + errors.Count);
                File.WriteAllText(Results + "/network-" + scene.name + ".json", "[" + string.Join(",", descriptor.NetworkIDCollection.Select(p => "{\"name\":\"" + p.gameObject.name + "\",\"id\":" + p.ID + "}")) + "]");
            }
        }

        static Transform[] AllTransforms() => SceneManager.GetActiveScene().GetRootGameObjects().SelectMany(g => g.GetComponentsInChildren<Transform>(true)).ToArray();
        static GameObject MeshObject(string name, Transform parent, Mesh mesh, Material material)
        {
            var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(parent, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        static void BuildKeyboard(Transform parent, QwenSocialUI social, Material white)
        {
            var pivot = new GameObject("Keyboard orbital pivot").transform;
            pivot.SetParent(parent, false);
            pivot.localRotation = Quaternion.Euler(0, 180, 0);
            social.keyboardPivot = pivot;
            var keyboard = MeshObject("Shared white keyboard", pivot, AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/ProjectedKeyboard.asset"), white);
            keyboard.transform.localPosition = new Vector3(0, .971f, QwenConsoleVisuals.KeyboardRadius);
            var collider = keyboard.AddComponent<BoxCollider>();
            collider.center = new Vector3(0, .02f, 0);
            collider.size = new Vector3(.72f, .09f, .28f);
            var interaction = keyboard.AddUdonSharpComponent<QwenKeyboard>();
            interaction.social = social;
            var backing = UdonSharpEditorUtility.GetBackingUdonBehaviour(interaction);
            var serialized = new SerializedObject(backing);
            var text = serialized.FindProperty("interactText");
            if (text != null)
                text.stringValue = "点击输入问题";
            var proximity = serialized.FindProperty("proximity");
            if (proximity != null)
                proximity.floatValue = 4;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            var caption = Canvas("Keyboard caption", pivot, 500, 100, .001f, false);
            caption.transform.localPosition = new Vector3(0, 1.05f, QwenConsoleVisuals.KeyboardRadius + .06f);
            caption.transform.localRotation = Quaternion.Euler(35, 180, 0);
            social.keyboardCaption = Label(caption.transform, "靠近圆桌即可输入", 20, 5, 460, 90, 24);
        }

        static void BuildInput(Transform parent, QwenSocialUI social, QwenWorldController controller)
        {
            var canvas = Canvas("Private keyboard input", parent, 640, 450, .001f, true);
            social.inputRoot = canvas.gameObject;
            canvas.transform.SetParent(social.keyboardPivot, false);
            canvas.transform.localPosition = new Vector3(0, 1.24f, 1.23f);
            canvas.transform.localRotation = Quaternion.Euler(12, 180, 0);
            Label(canvas.transform, "向千问提问", 24, 18, 570, 45, 28);
            var inputRect = Rect("Question", canvas.transform, 24, 80, 592, 230);
            var image = inputRect.gameObject.AddComponent<Image>();
            image.color = new Color(.92f, .94f, .97f);
            var input = inputRect.gameObject.AddComponent<InputField>();
            input.targetGraphic = image;
            input.characterLimit = 1200;
            input.lineType = InputField.LineType.MultiLineNewline;
            inputRect.gameObject.AddComponent<RectMask2D>();
            input.textComponent = Label(inputRect, "", 14, 10, 564, 210, 25);
            input.textComponent.alignment = TextAnchor.UpperLeft;
            var placeholder = Label(inputRect, "点击这里输入问题…", 14, 10, 564, 210, 25);
            placeholder.alignment = TextAnchor.UpperLeft;
            input.placeholder = placeholder;
            input.navigation = new Navigation{mode = Navigation.Mode.None};
            input.text = "";
            controller.promptInput = input;
            controller.statusText = Label(canvas.transform, "就绪 · 等待输入", 24, 316, 592, 60, 18);
            controller.sendButton = Button(canvas.transform, "发送", 24, 380, 280, social, "SendQuestion");
            Button(canvas.transform, "收起", 336, 380, 280, social, "CloseInput");
            canvas.gameObject.SetActive(false);
        }

        static void BuildPanels(QwenSocialUI social)
        {
            const int count = 80;
            social.panels = new Transform[count];
            social.panelText = new Text[count];
            social.panelCaption = new Text[count];
            social.panelContent = new RectTransform[count];
            social.panelFade = new CanvasGroup[count];
            social.slotPositions = new Vector3[count];
            for (int i = 0; i < count; i++)
            {
                int tier = i / 16, column = i % 16;
                float angle = column * Mathf.PI * 2 / 16;
                var radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                var canvas = Canvas("Read-only chat " + i.ToString("D2"), social.transform, 560, 480, .0009f, false);
                Vector3 position = radial * .90f + Vector3.up * (1.93f + tier * .50f);
                canvas.transform.SetPositionAndRotation(position, Quaternion.LookRotation(-radial));
                social.panels[i] = canvas.transform;
                social.slotPositions[i] = position;
                social.panelFade[i] = canvas.gameObject.AddComponent<CanvasGroup>();
                social.panelFade[i].blocksRaycasts = false;
                social.panelFade[i].interactable = false;
                social.panelCaption[i] = Label(canvas.transform, "", 24, 16, 512, 52, 26);
                var viewport = Rect("Read-only viewport", canvas.transform, 30, 86, 500, 350);
                viewport.gameObject.AddComponent<RectMask2D>();
                var body = Label(viewport, "", 0, 0, 500, 350, 23);
                body.alignment = TextAnchor.UpperLeft;
                social.panelText[i] = body;
                social.panelContent[i] = body.rectTransform;
                canvas.gameObject.SetActive(false);
            }
        }

        static Canvas Canvas(string name, Transform parent, float width, float height, float scale, bool interactive)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 2;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.sizeDelta = new Vector2(width, height);
            rect.localScale = Vector3.one * scale;
            if (interactive)
            {
                go.AddComponent<GraphicRaycaster>();
                go.AddComponent<BoxCollider>().size = new Vector3(width, height, 1);
                var type = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Components.VRCUiShape") ?? a.GetType("VRC.SDKBase.VRC_UiShape")).First(t => t != null);
                go.AddComponent(type);
            }

            var bg = Rect("White panel", rect, 0, 0, width, height).gameObject.AddComponent<Image>();
            bg.color = new Color(1, 1, 1, .97f);
            bg.raycastTarget = false;
            return canvas;
        }

        static RectTransform Rect(string name, Transform parent, float x, float y, float width, float height)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(x, -y);
            rect.sizeDelta = new Vector2(width, height);
            return rect;
        }

        static Text Label(Transform parent, string value, float x, float y, float width, float height, int size)
        {
            var rect = Rect("Text", parent, x, y, width, height);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = new Color(.10f, .12f, .17f);
            text.text = value;
            text.supportRichText = false;
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        static Button Button(Transform parent, string value, float x, float y, float width, UdonSharpBehaviour target, string method)
        {
            var rect = Rect(value, parent, x, y, width, 48);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.89f, .92f, .96f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation{mode = Navigation.Mode.None};
            Label(rect, value, 10, 0, width - 20, 48, 23).alignment = TextAnchor.MiddleCenter;
            UnityEventTools.AddStringPersistentListener(button.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(target).SendCustomEvent, method);
            return button;
        }
    }
}
