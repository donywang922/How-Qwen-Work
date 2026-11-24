using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace QwenWorld.Editor
{
    public static class QwenConsoleValidation
    {
        [MenuItem("Qwen World/Validation/Preview console and surrounding ring")]
        public static void ValidateBatch()
        {
            try
            {
                QwenModelPipeline.Compile();
                if (UnityEngine.SceneManagement.SceneManager.GetActiveScene().path != QwenProject.MainScene)
                    EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                var social = UnityEngine.Object.FindObjectOfType<QwenSocialUI>();
                var controller = social.controller;
                var corners = new Vector3[4];
                float largestRadius = 0;
                foreach (RectTransform panel in social.panels)
                {
                    panel.GetWorldCorners(corners);
                    foreach (var corner in corners)
                        largestRadius = Mathf.Max(largestRadius, new Vector2(corner.x, corner.z).magnitude);
                    var background = panel.Find("White panel").GetComponent<Image>().material;
                    Require(background.GetFloat("_Opacity") >= .85f, "Message background is too faint");
                }
                Require(largestRadius < QwenConsoleVisuals.InnerRadius, "Message panel is outside the table opening");
                var input = (RectTransform)social.inputRoot.transform;
                input.GetWorldCorners(corners);
                float inputBottom = corners.Min(c => c.y);
                var keyboard = GameObject.Find("Shared white keyboard");
                Require(input.parent == social.keyboardPivot && inputBottom > keyboard.transform.position.y + .02f,
                    "Input panel is below or detached from the keyboard");
                Require(social.keyboardDegreesPerSecond >= 360, "Keyboard tracking speed is too low");

                var ring = GameObject.Find("Orbital ring - imported Blender distant asset").GetComponent<MeshFilter>().sharedMesh;
                float ringNear = ring.vertices.Min(v => new Vector2(v.x, v.z).magnitude);
                float ringFar = ring.vertices.Max(v => new Vector2(v.x, v.z).magnitude);
                Require(ring.bounds.center.magnitude < .1f && ring.vertices.Min(v => v.magnitude) > QwenWalkwaysBuilder.WhiteRadius && ring.vertices.Max(v => v.magnitude) < QwenOrbitalRingMesh.Radius + 2,
                    "Outer ring does not surround the platform");
                Require(ring.triangles.Length / 3 == 19954, "Approved ring geometry changed");
                string output = QwenProject.DataPath("validation/console-layout");
                Directory.CreateDirectory(output);
                QwenConsoleVisuals.CaptureScreenPreviews(output);
                var panelView = social.panels[8];
                bool wasInput = input.gameObject.activeSelf, wasPanel = panelView.gameObject.activeSelf;
                var captionRoot = social.keyboardCaption.transform.parent.gameObject;
                bool wasCaption = captionRoot.activeSelf;
                string oldTitle = social.panelCaption[8].text, oldBody = social.panelText[8].text;
                input.gameObject.SetActive(true);
                panelView.gameObject.SetActive(true);
                captionRoot.SetActive(false);
                social.panelCaption[8].text = "共享对话";
                social.panelText[8].text = "你：请简单介绍一下自己。\n\n千问：你好，我是千问。\n这里显示共享对话，输入区位于键盘上方。\n\n中文 English 日本語 한국어";
                try
                {
                    QwenPreview.Capture(output + "/table-eye.png", new Vector3(0, 1.65f, -2.6f), new Vector3(0, 1.5f, -.3f), 58, 1600, 1100);
                    QwenPreview.Capture(output + "/platform-ring.png", new Vector3(24, 18, -28), new Vector3(0, 2.5f, 0), 48, 1600, 1100);
                }
                finally
                {
                    social.panelCaption[8].text = oldTitle;
                    social.panelText[8].text = oldBody;
                    input.gameObject.SetActive(wasInput);
                    panelView.gameObject.SetActive(wasPanel);
                    captionRoot.SetActive(wasCaption);
                }
                File.WriteAllText(output + "/validation.json", JsonUtility.ToJson(new Report
                {
                    passed = true, validatedAtUtc = DateTime.UtcNow.ToString("o"), panels = social.panels.Length,
                    largestPanelCornerRadius = largestRadius, inputBottom = inputBottom,
                    ringMinimumPlanarRadius = ringNear, ringMaximumPlanarRadius = ringFar,
                    keyboardDegreesPerSecond = social.keyboardDegreesPerSecond,
                    acquireDistance = social.exhibit.keyboardAcquireDistance, releaseDistance = social.exhibit.keyboardReleaseDistance,
                    scope = "Scene geometry, dark backgrounds, input raycasts and GPU-rendered previews. Runtime ownership and clipping use ClientSim integration validation."
                }, true));
                Debug.Log("QWEN_CONSOLE_LAYOUT_PASSED");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        [Serializable] class Report
        {
            public bool passed;
            public string validatedAtUtc, scope;
            public int panels;
            public float largestPanelCornerRadius, inputBottom, ringMinimumPlanarRadius, ringMaximumPlanarRadius;
            public float keyboardDegreesPerSecond, acquireDistance, releaseDistance;
        }
    }
}
