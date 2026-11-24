using System;
using System.Collections.Generic;
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

namespace QwenWorld.Editor
{
    public static class QwenMenuBuilder
    {
        public const string RootName = "Qwen local presentation";
        public static readonly string Results = QwenProject.DataPath("validation/local-menu");
        static readonly Color Ink = new Color(.10f, .13f, .20f);
        static Font font;
        [MenuItem("Qwen World/Interface/Install local menu and wishing gesture")]
        public static void Install()
        {
            QwenSceneInstaller.Install();
        }

        public static void BeginInstall(Action completed)
        {
            QwenSceneInstaller.Begin(completed);
        }

        public static QwenLocalMenu AddToCurrentScene()
        {
            var scene = SceneManager.GetActiveScene();
            var root = scene.GetRootGameObjects().FirstOrDefault(o => o.name == RootName);
            if (root == null)
                root = new GameObject(RootName);
            else
                foreach (Transform child in root.transform.Cast<Transform>().ToArray())
                    UnityEngine.Object.DestroyImmediate(child.gameObject);
            var controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
            if (controller == null)
                throw new InvalidOperationException("Missing world controller.");
            font = AssetDatabase.LoadAssetAtPath<Font>(QwenModelPipeline.Root + "/Fonts/NotoSansCJKsc-Regular.otf");
            if (font == null)
                throw new FileNotFoundException("Noto font missing.");
            var menu = root.GetComponent<QwenLocalMenu>();
            if (menu == null)
                menu = root.AddUdonSharpComponent<QwenLocalMenu>();
            menu.texturesVisible = false;
            menu.mountOnArm = true;
            menu.controller = controller;
            menu.runner = controller.runner;
            var transforms = scene.GetRootGameObjects().SelectMany(o => o.GetComponentsInChildren<Transform>(true)).ToArray();
            var girl = transforms.FirstOrDefault(t => t.name == "Qwen girl standee");
            if (girl != null)
            {
                menu.girlOrigin = girl;
                menu.girlRenderer = girl.GetComponent<Renderer>();
            }

            string[] textureNames = {"Outer ring - static textures", "Inner ring - live CRT", "Tensor display"};
            menu.textureRoots = transforms.Where(t => textureNames.Contains(t.name)).Select(t => t.gameObject).ToArray();
            string[] obsoleteControls = {"Faster", "Slower", "Pause", "One op", "One layer", "One token"};
            foreach (var oldSpeed in transforms.Where(t => obsoleteControls.Contains(t.name)).ToArray())
                UnityEngine.Object.DestroyImmediate(oldSpeed.gameObject);
            menu.runner.SetPresentationMode(false);
            var canvasObject = new GameObject("Local menu", typeof(RectTransform), typeof(Canvas), typeof(GraphicRaycaster), typeof(BoxCollider));
            var canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            var rect = canvasObject.GetComponent<RectTransform>();
            rect.SetParent(root.transform, false);
            rect.sizeDelta = new Vector2(480, 485);
            rect.localScale = Vector3.one * .001f;
            canvasObject.GetComponent<BoxCollider>().size = new Vector3(480, 485, 1);
            Type uiShape = AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType("VRC.SDK3.Components.VRCUiShape") ?? a.GetType("VRC.SDKBase.VRC_UiShape")).FirstOrDefault(t => t != null);
            if (uiShape == null)
                throw new InvalidOperationException("VRChat UI shape unavailable.");
            canvasObject.AddComponent(uiShape);
            var background = Rect("White panel", rect, 0, 0, 480, 485).gameObject.AddComponent<Image>();
            background.color = new Color(1, 1, 1, .97f);
            background.raycastTarget = false;
            Label("Title", rect, "展示菜单", 24, 18, 350, 38, 25);
            MakeButton("Close", rect, "×", 416, 14, 44, 44, menu, "CloseMenu");
            var visibility = MakeButton("Textures", rect, "显示贴图", 24, 70, 432, 54, menu, "ToggleTextures");
            menu.visibilityLabel = visibility.GetComponentInChildren<Text>();
            var group = Rect("Execution controls", rect, 24, 138, 432, 54);
            menu.executionControls = group.gameObject;
            menu.runButton = MakeButton("Run", group, "开始", 0, 0, 209, 54, menu, "ToggleExecution");
            menu.runLabel = menu.runButton.GetComponentInChildren<Text>();
            menu.stepButton = MakeButton("One operator", group, "单算子", 223, 0, 209, 54, menu, "OneOperator");
            menu.stateLabel = Label("Status", rect, "就绪 · 等待输入", 24, 198, 432, 68, 18);
            menu.heightLabel = Label("Observation height", rect, "亚克力平台：最低 · 隐藏", 24, 282, 432, 34, 21);
            menu.heightSlider = HeightSlider(rect, menu);
            Label("Lowest", rect, "最低隐藏", 24, 372, 180, 26, 17);
            Label("Highest", rect, "5.5 米", 370, 372, 86, 26, 17).alignment = TextAnchor.MiddleRight;
            menu.mountButton = MakeButton("Mount", rect, "位置：左手臂", 24, 415, 432, 50, menu, "ToggleMount");
            menu.mountLabel = menu.mountButton.GetComponentInChildren<Text>();
            menu.menuRoot = canvasObject;
            menu.girlWishParticles = MakeParticles("Girl wishing burst", root.transform, .4f);
            menu.playerWishParticles = MakeParticles("Player wishing burst", root.transform, .35f);
            canvasObject.SetActive(false);
            return menu;
        }

        static Slider HeightSlider(Transform parent, QwenLocalMenu target)
        {
            var root = Rect("Observation height slider", parent, 24, 330, 432, 36);
            var slider = root.gameObject.AddComponent<Slider>();
            slider.navigation = new Navigation { mode = Navigation.Mode.None };
            slider.minValue = 0;
            slider.maxValue = 5.5f;
            var hit = Rect("Slider hit area", root, 0, 0, 432, 36).gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var track = Rect("Slider track", root, 0, 14, 432, 8).gameObject.AddComponent<Image>();
            track.color = new Color(.16f, .29f, .38f, 1);
            track.raycastTarget = false;
            var fillArea = Rect("Fill area", root, 16, 14, 400, 8);
            var fill = Rect("Slider fill", fillArea, 0, 0, 400, 8).gameObject.AddComponent<Image>();
            fill.color = new Color(.40f, .73f, .88f, 1);
            fill.raycastTarget = false;
            fill.rectTransform.anchorMin = Vector2.zero;
            fill.rectTransform.anchorMax = Vector2.one;
            fill.rectTransform.sizeDelta = Vector2.zero;
            fill.rectTransform.anchoredPosition = Vector2.zero;
            slider.fillRect = fill.rectTransform;
            var handleArea = Rect("Handle area", root, 16, 0, 400, 36);
            var handle = Rect("Slider handle", handleArea, 0, 0, 14, 28).gameObject.AddComponent<Image>();
            handle.rectTransform.pivot = new Vector2(.5f, .5f);
            handle.rectTransform.anchorMin = Vector2.zero;
            handle.rectTransform.anchorMax = new Vector2(0, 1);
            handle.rectTransform.sizeDelta = new Vector2(14, -8);
            handle.rectTransform.anchoredPosition = Vector2.zero;
            handle.color = new Color(.85f, .96f, 1, 1);
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
            UnityEventTools.AddStringPersistentListener(slider.onValueChanged,
                UdonSharpEditorUtility.GetBackingUdonBehaviour(target).SendCustomEvent, "ChangeObservationHeight");
            slider.value = 0;
            return slider;
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

        static Text Label(string name, Transform parent, string value, float x, float y, float width, float height, int size)
        {
            var rect = Rect(name, parent, x, y, width, height);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = font;
            text.fontSize = size;
            text.color = Ink;
            text.text = value;
            text.raycastTarget = false;
            text.alignment = TextAnchor.MiddleLeft;
            text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }

        static Button MakeButton(string name, Transform parent, string value, float x, float y, float width, float height, QwenLocalMenu target, string method)
        {
            var rect = Rect(name, parent, x, y, width, height);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(.90f, .93f, .97f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.navigation = new Navigation{mode = Navigation.Mode.None};
            var text = Label("Label", rect, value, 8, 0, width - 16, height, 21);
            text.alignment = TextAnchor.MiddleCenter;
            UnityEventTools.AddStringPersistentListener(button.onClick, UdonSharpEditorUtility.GetBackingUdonBehaviour(target).SendCustomEvent, method);
            return button;
        }

        static ParticleSystem MakeParticles(string name, Transform parent, float radius)
        {
            string path = "Assets/QwenWorld/Environment/QwenGirl/WishParticles.mat";
            var shader = Shader.Find("QwenWorld/WishParticles");
            if (shader == null || ShaderUtil.ShaderHasError(shader))
                throw new InvalidOperationException("Wish particle shader failed.");
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader)
                {name = "White wishing particles"};
                AssetDatabase.CreateAsset(material, path);
            }

            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localRotation = Quaternion.Euler(-90, 0, 0);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = .18f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.1f, 2.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3.8f, 6.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(.025f, .075f);
            main.startColor = Color.white;
            main.gravityModifier = .45f;
            main.maxParticles = 900;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.rateOverTime = 0;
            emission.SetBursts(new[]{new ParticleSystem.Burst(0, 650)});
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 17;
            shape.radius = radius;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var gradient = new Gradient();
            gradient.SetKeys(new[]{new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1)}, new[]{new GradientAlphaKey(0, 0), new GradientAlphaKey(1, .08f), new GradientAlphaKey(1, .45f), new GradientAlphaKey(0, 1)});
            color.color = new ParticleSystem.MinMaxGradient(gradient);
            var renderer = ps.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = material;
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            renderer.lightProbeUsage = LightProbeUsage.Off;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            return ps;
        }
    }
}
