using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace QwenWorld.Editor
{
    public static class QwenLocalizationValidation
    {
        public static void ApplyBatch() { ApplyCore(false); }
        public static void RemoveReplyBatch() { ApplyCore(true); }
        static void ApplyCore(bool requestOnly)
        {
            try
            {
                QwenModelPipeline.Compile();
                var scene = EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
                var locale = QwenLocalizationBuilder.ConfigureScene();
                locale.language = 0;
                locale.Apply();
                QwenProject.SaveUdonFields(scene);
                var descriptor = QwenProject.Components<VRC.SDKBase.VRC_SceneDescriptor>(scene).Single();
                VRC.SDKBase.Network.NetworkIDAssignment.ConfigureNetworkIDs(descriptor,out var idErrors,
                    VRC.SDKBase.Network.NetworkIDAssignment.SetError.IncompatibleTypes);
                Require(idErrors.Count == 0,"Network object registration failed");
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.SaveAssets();
                string output = QwenProject.DataPath(requestOnly ? "validation/no-system" : "validation/localization");
                Directory.CreateDirectory(output);
                if (requestOnly) CheckNoSystem(locale,output);
                else ValidateAndPreview(locale,output);
                EditorSceneManager.OpenScene(QwenProject.MainScene,OpenSceneMode.Single);
                File.WriteAllText(output + "/validation.json",JsonUtility.ToJson(new Report
                {
                    passed = true, languages = 4, translationKeys = QwenLocalization.KeyCount,
                    validatedAtUtc = DateTime.UtcNow.ToString("o"),
                    scope = requestOnly ? "Udon compilation; exact request assembly without system messages across UI languages; removed reply control; one input preview. No inference, live clients or package builds."
                        : "Udon compilation; local language selection; no system messages; shared original text; font coverage, UI fit, viewer controls and four previews. No inference or package build."
                },true));
                Debug.Log(requestOnly ? "QWEN_NO_SYSTEM_PASSED" : "QWEN_LOCALIZATION_PASSED languages=4");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void CheckNoSystem(QwenLocalization locale,string output)
        {
            const string history = "<|im_start|>user\n你好<|im_end|>\n<|im_start|>assistant\n你好！<|im_end|>\n";
            const string question = "Hello こんにちは 안녕하세요";
            string expected = history + "<|im_start|>user\n" + question
                + "<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n";
            for (int language = 0; language < 4; language++)
            {
                locale.language = language; locale.Apply();
                string input = locale.controller.BuildChatInput(history,question);
                Require(input == expected && !input.Contains("<|im_start|>system"),"Request includes system content or language instructions");
            }
            Require(locale.social.inputRoot.transform.Find("Reply language") == null,"Reply selector remains");
            Require(typeof(QwenWorldController).GetField("systemPrompt") == null,"System prompt field remains");
            Require(locale.languageButtons.Length == 4,"Interface language selection was removed");
            locale.language = 0; locale.Apply();
            locale.menu.menuRoot.SetActive(false);
            locale.social.keyboardCaption.transform.parent.gameObject.SetActive(false);
            locale.social.inputRoot.SetActive(true);
            Center(locale.social.inputRoot,new Vector3(0,1.55f,-1.8f));
            QwenPreview.Capture(output + "/input.png",new Vector3(0,1.55f,-3),new Vector3(0,1.55f,-1.8f),30,1300,1000);
        }

        static void ValidateAndPreview(QwenLocalization locale,string output)
        {
            var controller = locale.controller;
            var runner = controller.runner;
            var social = locale.social;
            var menu = locale.menu;
            var gallery = GameObject.Find(QwenTextureGalleryBuilder.RootName);
            if (gallery != null) gallery.SetActive(false);
            social.keyboardCaption.transform.parent.gameObject.SetActive(false);
            var history = Private(typeof(QwenWorldController),"sharedHistory");
            const string raw = "<|im_start|>user\n你好 Hello こんにちは 안녕하세요<|im_end|>\n<|im_start|>assistant\n原文 Original 原文 원문<|im_end|>\n";
            history.SetValue(controller,raw);
            Private(typeof(QwenWorldController),"sharedState").SetValue(controller,1);
            Private(typeof(QwenTextureRunner),"promptTokens").SetValue(runner,new int[512]);
            runner.position = 511; runner.passCursor = 300; runner.generatedCount = 16; runner.running = true; runner.paused = true;
            string[] names = {"zh","en","ja","ko"};
            for (int language = 0; language < 4; language++)
            {
                locale.language = language;
                locale.Apply();
                Require(!controller.BuildChatInput("","Hello").Contains("<|im_start|>system"),"System message remains");
                Require((string)history.GetValue(controller) == raw,"Language switching changed shared history");
                Require(controller.GetTranscript().Contains("你好 Hello こんにちは 안녕하세요"),"Conversation original text was translated");
                Require(controller.GetProgressText().Contains("511/512") && controller.GetProgressText().Contains("300/849"),"Localized progress lost numeric data");
                for (int key = 0; key < QwenLocalization.KeyCount; key++)
                {
                    string text = locale.T(key);
                    Require(text.Length > 0,"Missing translation " + language + ":" + key);
                    foreach (char c in text)
                        if (!char.IsControl(c)) Require(menu.stateLabel.font.HasCharacter(c),"Missing glyph " + c + " in language " + language);
                }
                menu.menuRoot.SetActive(true); social.inputRoot.SetActive(true);
                Canvas.ForceUpdateCanvases();
                var checkedText = locale.labels.Concat(new[]{menu.visibilityLabel,menu.runLabel,menu.mountLabel,menu.stateLabel,menu.heightLabel,
                    controller.statusText,social.keyboardCaption}).Concat(locale.languageButtons.Select(b => b.GetComponentInChildren<Text>()));
                foreach (var text in checkedText)
                    Require(text.preferredHeight <= text.rectTransform.rect.height + .5f,
                        "Text overflow [" + names[language] + "] " + text.name + ": " + text.text);
                foreach (var footer in social.panelProgress)
                    Require(footer.preferredHeight <= footer.rectTransform.rect.height + .5f,"Chat footer overflow in " + names[language]);
                foreach (int key in new[]{QwenLocalization.NEED_KEYBOARD,QwenLocalization.BUSY,QwenLocalization.SHORTEN,QwenLocalization.OWNERSHIP,QwenLocalization.CONTEXT,QwenLocalization.ERROR})
                {
                    controller.statusText.text = locale.T(key);
                    Require(controller.statusText.preferredHeight <= controller.statusText.rectTransform.rect.height,"Input warning overflow");
                }
                controller.statusText.text = controller.GetProgressText();
                Center(menu.menuRoot,new Vector3(-.43f,1.55f,-1.8f));
                Center(social.inputRoot,new Vector3(.41f,1.55f,-1.8f));
                QwenPreview.Capture(output + "/" + names[language] + ".png",new Vector3(0,1.55f,-3.6f),new Vector3(0,1.55f,-1.8f),28,1800,1100);
                controller.supportsInference = false;
                locale.Apply();
                Require(!locale.stopButton.interactable,"Viewer can use inference controls");
                Require(!menu.runButton.interactable && !menu.stepButton.interactable,"Viewer execution controls enabled");
                Require(menu.menuRoot.transform.Find("Title").GetComponent<Text>().text.Contains(locale.T(QwenLocalization.VIEWER)),"Viewer badge missing");
                controller.supportsInference = true;
            }
            locale.language = 1; locale.Apply(); locale.ToggleHelp(); menu.menuRoot.SetActive(true);
            Require(locale.helpHiddenControls.All(go => !go.activeSelf),"Help view leaves underlying controls visible");
            social.inputRoot.SetActive(false);
            Center(menu.menuRoot,new Vector3(0,1.55f,-1.8f));
            QwenPreview.Capture(output + "/help.png",new Vector3(0,1.55f,-2.8f),new Vector3(0,1.55f,-1.8f),38,1000,1100);
            Require(locale.languageButtons.All(b => b.onClick.GetPersistentEventCount() == 1),"Language buttons are not wired");
        }

        static FieldInfo Private(Type type,string name) => type.GetField(name,BindingFlags.Instance | BindingFlags.NonPublic);
        static void Center(GameObject go,Vector3 position)
        {
            go.transform.rotation = Quaternion.identity;
            var corners = new Vector3[4];
            go.transform.Find("White panel").GetComponent<RectTransform>().GetWorldCorners(corners);
            go.transform.position += position - (corners[0] + corners[2]) * .5f;
        }
        static void Require(bool ok,string message) { if (!ok) throw new InvalidOperationException(message); }
        [Serializable] class Report
        {
            public bool passed;
            public int languages,translationKeys;
            public string validatedAtUtc,scope;
        }
    }
}
