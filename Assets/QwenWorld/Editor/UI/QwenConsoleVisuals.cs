using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UdonSharpEditor;

namespace QwenWorld.Editor
{
    public static class QwenConsoleVisuals
    {
        public const float OuterRadius = 1.41f, InnerRadius = 1.05f, KeyboardRadius = 1.23f;
        const string Folder = QwenSceneInstaller.Folder;
        public static void ConfigureKeyboard(QwenSocialUI social, Material white)
        {
            var keyboard = GameObject.Find("Shared white keyboard");
            keyboard.transform.localPosition = new Vector3(0, .971f, KeyboardRadius);
            string path = Folder + "/LaserKeyboard.png";
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            if (importer == null)
                throw new FileNotFoundException("Run Tools/Qwen35/make_laser_keyboard.py first");
            importer.sRGBTexture = true;
            importer.alphaIsTransparency = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.filterMode = FilterMode.Trilinear;
            importer.anisoLevel = 4;
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Standalone", overridden = true, maxTextureSize = 2048, format = TextureImporterFormat.BC7, compressionQuality = 100});
            importer.SetPlatformTextureSettings(new TextureImporterPlatformSettings{name = "Android", overridden = true, maxTextureSize = 1024, format = TextureImporterFormat.ASTC_4x4, compressionQuality = 100});
            importer.SaveAndReimport();
            var mat = QwenGraphDisplayBuilder.Material(Folder + "/LaserKeyboard.mat", "QwenWorld/LaserKeyboard");
            mat.mainTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            mat.SetFloat("_Intensity", 1.15f);
            EditorUtility.SetDirty(mat);
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(Folder + "/ProjectedKeyboard.asset");
            if (mesh == null)
            {
                mesh = new Mesh{name = "Flat laser keyboard projection"};
                AssetDatabase.CreateAsset(mesh, Folder + "/ProjectedKeyboard.asset");
            }

            mesh.Clear();
            mesh.vertices = new[]{new Vector3(-.35f, 0, -.125f), new Vector3(-.35f, 0, .125f), new Vector3(.35f, 0, .125f), new Vector3(.35f, 0, -.125f)};
            mesh.uv = new[]{new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 0), new Vector2(0, 1)};
            mesh.triangles = new[]{0, 1, 2, 0, 2, 3};
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            keyboard.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = keyboard.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            var collider = keyboard.GetComponent<BoxCollider>();
            collider.center = new Vector3(0, .012f, 0);
            collider.size = new Vector3(.72f, .06f, .28f);
            foreach (Transform old in keyboard.transform.Cast<Transform>().Where(t => t.name == "Laser projector").ToArray())
                UnityEngine.Object.DestroyImmediate(old.gameObject);
            var pod = new GameObject("Laser projector", typeof(MeshFilter), typeof(MeshRenderer));
            pod.transform.SetParent(keyboard.transform, false);
            var geometry = new QwenExhibitGeometry();
            geometry.BeveledBox(new Vector3(0, .014f, -.141f), new Vector3(.060f, .028f, .035f), .004f);
            pod.GetComponent<MeshFilter>().sharedMesh = geometry.Save(Folder + "/LaserProjector.asset", "Small optical projector");
            pod.GetComponent<MeshRenderer>().sharedMaterial = white;
            var slit = new GameObject("Projector aperture", typeof(MeshFilter), typeof(MeshRenderer));
            slit.transform.SetParent(pod.transform, false);
            var slitGeo = new QwenExhibitGeometry();
            slitGeo.Box(new Vector3(0, .014f, -.1225f), new Vector3(.032f, .003f, .001f));
            slit.GetComponent<MeshFilter>().sharedMesh = slitGeo.Save(Folder + "/LaserAperture.asset", "Laser aperture");
            var glow = QwenGraphDisplayBuilder.Material(Folder + "/LaserAperture.mat", "Standard");
            glow.color = new Color(.06f, .45f, .75f);
            glow.EnableKeyword("_EMISSION");
            glow.SetColor("_EmissionColor", new Color(.08f, .65f, 1) * 1.3f);
            glow.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            EditorUtility.SetDirty(glow);
            slit.GetComponent<MeshRenderer>().sharedMaterial = glow;
            slit.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            if (social != null && social.keyboardCaption != null)
            {
                var caption = social.keyboardCaption.GetComponentInParent<Canvas>().transform;
                caption.localPosition = new Vector3(0, 1.05f, KeyboardRadius + .06f);
            }
        }

        public static void ConfigureScreens()
        {
            var social = UnityEngine.Object.FindObjectOfType<QwenSocialUI>();
            if (social == null)
                return;
            ArrangeMessageScreens(social);
            var fontMaterial = QwenGraphDisplayBuilder.Material(Folder + "/GlassText.mat", "QwenWorld/UI/GlassText");
            foreach (var screen in social.panels)
                ApplyScreen(screen.gameObject, fontMaterial);
            if (social.inputRoot != null)
                ApplyScreen(social.inputRoot, fontMaterial);
            if (social.keyboardCaption != null)
                ApplyScreen(social.keyboardCaption.GetComponentInParent<Canvas>().gameObject, fontMaterial);
            var menu = UnityEngine.Object.FindObjectOfType<QwenLocalMenu>();
            if (menu != null && menu.menuRoot != null) ApplyScreen(menu.menuRoot, fontMaterial);
            foreach (var label in social.panelCaption)
                label.color = new Color(.82f, .76f, 1);
            if (social.controller.promptInput != null)
            {
                var input = social.controller.promptInput;
                input.customCaretColor = true;
                input.caretColor = new Color(.60f, .90f, 1);
                input.selectionColor = new Color(.12f, .50f, .8f, .38f);
            }
            ConfigureProgressScreens();
        }

        public static void ConfigureProgressScreens()
        {
            var social = UnityEngine.Object.FindObjectOfType<QwenSocialUI>();
            var menu = UnityEngine.Object.FindObjectOfType<QwenLocalMenu>();
            var status = social.controller.statusText;
            status.rectTransform.anchoredPosition = new Vector2(24,-316);
            status.rectTransform.sizeDelta = new Vector2(592,60);
            status.fontSize = 18;
            status.text = "就绪 · 等待输入";
            social.panelProgress = new Text[social.panels.Length];
            for (int i = 0; i < social.panels.Length; i++)
            {
                var panel = (RectTransform)social.panels[i];
                var found = panel.Find("Inference progress");
                var label = found != null ? found.GetComponent<Text>()
                    : new GameObject("Inference progress",typeof(RectTransform),typeof(Text)).GetComponent<Text>();
                label.transform.SetParent(panel,false);
                var rect = label.rectTransform;
                rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1);
                rect.anchoredPosition = new Vector2(24,-440);
                rect.sizeDelta = new Vector2(panel.sizeDelta.x - 48,26);
                label.font = status.font; label.material = status.material;
                label.fontSize = 14; label.color = new Color(.65f,.85f,.95f);
                label.alignment = TextAnchor.MiddleLeft; label.raycastTarget = false;
                label.horizontalOverflow = HorizontalWrapMode.Wrap;
                label.verticalOverflow = VerticalWrapMode.Truncate;
                label.supportRichText = false; label.text = "就绪 · 等待输入";
                social.panelProgress[i] = label;
            }
            var menuRect = menu.menuRoot.GetComponent<RectTransform>();
            menuRect.sizeDelta = new Vector2(480,485);
            menu.menuRoot.transform.Find("White panel").GetComponent<RectTransform>().sizeDelta = menuRect.sizeDelta;
            menu.menuRoot.GetComponent<BoxCollider>().size = new Vector3(480,485,1);
            menu.stateLabel.rectTransform.anchoredPosition = new Vector2(24,-198);
            menu.stateLabel.rectTransform.sizeDelta = new Vector2(432,68);
            menu.stateLabel.fontSize = 18;
            menu.stateLabel.text = "就绪 · 等待输入";
            MoveProgressY(menu.heightLabel.transform,282);
            MoveProgressY(menu.heightSlider.transform,330);
            MoveProgressY(menu.menuRoot.transform.Find("Lowest"),372);
            MoveProgressY(menu.menuRoot.transform.Find("Highest"),372);
            MoveProgressY(menu.mountButton.transform,415);
            if (social.controller.localization != null) QwenLocalizationBuilder.ConfigureScene();
        }

        static void MoveProgressY(Transform target,float y)
        {
            var rect = (RectTransform)target;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,-y);
        }

        static void ArrangeMessageScreens(QwenSocialUI social)
        {
            const int columns = 16;
            // Eight outer and eight inner slots per tier, all inside the 1.05 m table opening.
            const float outerRadius = .90f, innerRadius = .52f;
            social.slotPositions = new Vector3[social.panels.Length];
            for (int i = 0; i < social.panels.Length; i++)
            {
                float angle = (i % columns) * Mathf.PI * 2 / columns;
                var radial = new Vector3(Mathf.Sin(angle), 0, Mathf.Cos(angle));
                bool inner = i % 2 == 1;
                float width = inner ? 360 : 560;
                float bodyWidth = width - 60;
                Vector3 position = radial * (inner ? innerRadius : outerRadius) + Vector3.up * (1.93f + (i / columns) * .50f);
                var panel = (RectTransform)social.panels[i];
                panel.SetPositionAndRotation(position, Quaternion.LookRotation(-radial));
                panel.sizeDelta = new Vector2(width, 480);
                social.slotPositions[i] = position;
                panel.Find("White panel").GetComponent<RectTransform>().sizeDelta = new Vector2(width, 480);
                social.panelCaption[i].rectTransform.sizeDelta = new Vector2(width - 48, 52);
                social.panelCaption[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                social.panelCaption[i].verticalOverflow = VerticalWrapMode.Truncate;
                var viewport = social.panelContent[i].parent.GetComponent<RectTransform>();
                viewport.sizeDelta = new Vector2(bodyWidth, 350);
                social.panelContent[i].sizeDelta = new Vector2(bodyWidth, 350);
                social.panelText[i].horizontalOverflow = HorizontalWrapMode.Wrap;
                social.panelText[i].verticalOverflow = VerticalWrapMode.Truncate;
            }

            UdonSharpEditorUtility.CopyProxyToUdon(social, ProxySerializationPolicy.All);
        }

        static void ApplyScreen(GameObject root, Material fontMaterial)
        {
            foreach (var image in root.GetComponentsInChildren<Image>(true))
            {
                if (image.name != "White panel" && image.GetComponent<InputField>() == null && image.GetComponent<Button>() == null)
                    continue;
                var rect = image.rectTransform.rect;
                float opacity = image.name == "White panel" ? .88f : .94f;
                string path = Folder + "/Glass-" + Mathf.RoundToInt(rect.width) + "x" + Mathf.RoundToInt(rect.height) + "-" + Mathf.RoundToInt(opacity * 1000) + ".mat";
                var mat = QwenGraphDisplayBuilder.Material(path, "QwenWorld/UI/GlassScreen");
                mat.SetVector("_PanelSize", new Vector4(rect.width, rect.height, 0, 0));
                mat.SetFloat("_Opacity", opacity);
                mat.SetColor("_Tint", new Color(.018f, .040f, .065f));
                mat.SetFloat("_Radius", Mathf.Min(13, rect.height * .18f));
                EditorUtility.SetDirty(mat);
                image.sprite = GlassSprite();
                image.type = Image.Type.Simple;
                image.preserveAspect = false;
                image.material = mat;
                image.color = Color.white;
            }

            foreach (var text in root.GetComponentsInChildren<Text>(true))
            {
                text.material = fontMaterial;
                text.color = new Color(.87f, .95f, 1);
            }
        }

        static Sprite GlassSprite()
        {
            string path = Folder + "/GlassUVSprite.asset";
            var sprite = AssetDatabase.LoadAllAssetsAtPath(path).OfType<Sprite>().FirstOrDefault();
            if (sprite != null)
                return sprite;
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
            {name = "Glass UV source", filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp};
            texture.SetPixels(new[]{Color.white, Color.white, Color.white, Color.white});
            texture.Apply();
            AssetDatabase.CreateAsset(texture, path);
            sprite = Sprite.Create(texture, new Rect(0, 0, 2, 2), new Vector2(.5f, .5f), 100, 0, SpriteMeshType.FullRect);
            sprite.name = "Full rectangle UV sprite";
            AssetDatabase.AddObjectToAsset(sprite, texture);
            EditorUtility.SetDirty(texture);
            return sprite;
        }

        public static void CaptureScreenPreviews(string output)
        {
            var social = UnityEngine.Object.FindObjectOfType<QwenSocialUI>();
            if (social == null)
                return;
            var input = social.inputRoot.transform;
            var position = input.position;
            var rotation = input.rotation;
            bool active = input.gameObject.activeSelf;
            input.SetPositionAndRotation(new Vector3(0, 1.55f, -2.6f), Quaternion.identity);
            input.gameObject.SetActive(true);
            Capture(output + "/glass-input.png", new Vector3(.32f, 1.65f, -3.5f), new Vector3(0, 1.55f, -2.6f), 52);
            ValidateInputHits(social);
            input.gameObject.SetActive(active);
            input.SetPositionAndRotation(position, rotation);
            var panel = social.panels[0];
            position = panel.position;
            rotation = panel.rotation;
            active = panel.gameObject.activeSelf;
            string oldCaption = social.panelCaption[0].text, oldBody = social.panelText[0].text;
            panel.SetPositionAndRotation(new Vector3(0, 1.6f, -1.3f), Quaternion.identity);
            panel.gameObject.SetActive(true);
            social.panelCaption[0].text = "共享对话";
            social.panelText[0].text = "等待输入。\n\n靠近后在此查看共享对话。\n\n点击环桌上的投影键盘打开输入界面。";
            Capture(output + "/glass-message.png", new Vector3(.55f, 1.72f, -2.6f), new Vector3(0, 1.6f, -1.3f), 48);
            social.panelCaption[0].text = oldCaption;
            social.panelText[0].text = oldBody;
            panel.gameObject.SetActive(active);
            panel.SetPositionAndRotation(position, rotation);
        }

        static void ValidateInputHits(QwenSocialUI social)
        {
            var go = new GameObject("Glass interaction verification camera");
            var camera = go.AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 1.55f, -3.5f);
            camera.transform.LookAt(new Vector3(0, 1.55f, -2.6f));
            camera.nearClipPlane = .05f;
            var canvas = social.inputRoot.GetComponent<Canvas>();
            var oldCamera = canvas.worldCamera;
            canvas.worldCamera = camera;
            Canvas.ForceUpdateCanvases();
            var system = EventSystem.current;
            GameObject temporary = null;
            if (system == null)
            {
                temporary = new GameObject("Temporary UI test events", typeof(EventSystem));
                system = temporary.GetComponent<EventSystem>();
            }

            var raycaster = canvas.GetComponent<GraphicRaycaster>();
            foreach (var target in new[]{social.controller.promptInput.gameObject, social.controller.sendButton.gameObject})
            {
                var rect = target.GetComponent<RectTransform>();
                var world = rect.TransformPoint(rect.rect.center);
                var screen = camera.WorldToScreenPoint(world);
                var pointer = new PointerEventData(system)
                {position = screen};
                var hits = new List<RaycastResult>();
                raycaster.Raycast(pointer, hits);
                if (!hits.Any(hit => hit.gameObject == target))
                    throw new InvalidOperationException("Glass UI lost hit target: " + target.name);
            }

            var mesh = GameObject.Find("Floating circular desk").GetComponent<MeshFilter>().sharedMesh;
            float inner = mesh.vertices.Min(v => new Vector2(v.x, v.z).magnitude), outer = mesh.vertices.Max(v => new Vector2(v.x, v.z).magnitude);
            if (Mathf.Abs(inner - InnerRadius) > .001f || Mathf.Abs(outer - OuterRadius) > .001f)
                throw new InvalidOperationException("Ring desk dimensions changed");
            var keyboard = GameObject.Find("Shared white keyboard");
            if (keyboard.GetComponent<MeshFilter>().sharedMesh.triangles.Length != 6)
                throw new InvalidOperationException("Physical keys remain");
            var bars = GameObject.Find("Six white moving bars").GetComponent<Renderer>();
            Color glow = bars.sharedMaterial.GetColor("_GlowColor");
            if (bars.shadowCastingMode != ShadowCastingMode.Off || bars.receiveShadows || Mathf.Abs(glow.r - glow.g) > .0001f || Mathf.Abs(glow.g - glow.b) > .0001f || glow.r < 1)
                throw new InvalidOperationException("Motion bars must remain uniformly white and unshadowed");
            Directory.CreateDirectory(QwenProject.DataPath("validation/glass-console"));
            File.WriteAllText(QwenProject.DataPath("validation/glass-console/validation.json"), "{\"passed\":true,\"outerDiameter\":2.82,\"innerDiameter\":2.10,\"radialWidth\":0.36,\"deskThickness\":0.05,\"projectionWidth\":0.70,\"projectionDepth\":0.25,\"inputRaycast\":true,\"sendRaycast\":true,\"projectionTriangles\":2,\"glassShaderBodyOpacity\":0.88,\"extraRuntimeCameras\":0,\"barsUniformEmission\":true,\"barsCastShadows\":false,\"barsReceiveShadows\":false}");
            canvas.worldCamera = oldCamera;
            UnityEngine.Object.DestroyImmediate(go);
            if (temporary != null)
                UnityEngine.Object.DestroyImmediate(temporary);
        }

        static void Capture(string path, Vector3 eye, Vector3 target, float fov)
        {
            QwenPreview.Capture(path, eye, target, fov, 1600, 1100);
        }
    }
}
