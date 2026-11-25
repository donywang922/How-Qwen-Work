using System;
using System.Collections.Generic;
using System.Linq;
using UdonSharp;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.UI;

namespace QwenWorld.Editor
{
    public static class QwenLocalizationBuilder
    {
        static Text template;
        static Image imageTemplate;
        static List<Text> labels;
        static List<int> keys;

        public static QwenLocalization ConfigureScene()
        {
            var controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
            var menu = UnityEngine.Object.FindObjectOfType<QwenLocalMenu>();
            var social = UnityEngine.Object.FindObjectOfType<QwenSocialUI>();
            var observation = UnityEngine.Object.FindObjectOfType<QwenObservationPlatform>();
            var locale = UnityEngine.Object.FindObjectOfType<QwenLocalization>();
            if (locale == null)
            {
                var go = new GameObject("Local language settings");
                go.transform.SetParent(menu.transform,false);
                locale = go.AddUdonSharpComponent<QwenLocalization>();
            }
            locale.controller = controller; locale.menu = menu; locale.social = social; locale.observation = observation;
            controller.localization = locale; observation.localization = locale;
            template = menu.runLabel; imageTemplate = menu.runButton.GetComponent<Image>();
            labels = new List<Text>(); keys = new List<int>();
            var root = menu.menuRoot.GetComponent<RectTransform>();
            var oldMount = root.Find("Mount");
            if (oldMount != null) UnityEngine.Object.DestroyImmediate(oldMount.gameObject);
            root.sizeDelta = new Vector2(480,505);
            root.Find("White panel").GetComponent<RectTransform>().sizeDelta = root.sizeDelta;
            root.Find("White panel").GetComponent<Image>().material = Glass(480,505,.88f);
            menu.menuRoot.GetComponent<BoxCollider>().size = new Vector3(480,505,1);
            var title = root.Find("Title").GetComponent<Text>();
            title.rectTransform.sizeDelta = new Vector2(300,38); title.fontSize = 24;
            Bind(title,QwenLocalization.MENU);
            string[] names = {"中文","English","日本語","한국어"};
            string[] methods = {"Chinese","English","Japanese","Korean"};
            locale.languageButtons = new Button[4];
            for (int i = 0; i < 4; i++)
            {
                var button = MakeButton(root,"Language " + i,24 + i * 110,68,102,38,locale,methods[i]);
                button.GetComponentInChildren<Text>().text = names[i];
                locale.languageButtons[i] = button;
            }
            Move(root.Find("Textures"),126); Move(root.Find("Execution controls"),194);
            Move(menu.stateLabel.transform,254); Move(menu.heightLabel.transform,338);
            Move(menu.heightSlider.transform,386); Move(root.Find("Lowest"),428);
            Move(root.Find("Highest"),428);
            Bind(menu.stepButton.GetComponentInChildren<Text>(),QwenLocalization.STEP);
            Bind(root.Find("Lowest").GetComponent<Text>(),QwenLocalization.LOWEST);
            Bind(root.Find("Highest").GetComponent<Text>(),QwenLocalization.HEIGHT_MAX);
            var help = MakeButton(root,"Help",352,14,44,44,locale,"ToggleHelp");
            help.GetComponentInChildren<Text>().text = "?";
            var helpRect = Rect(root,"Language help",24,120,432,371);
            var helpImage = helpRect.GetComponent<Image>() ?? helpRect.gameObject.AddComponent<Image>();
            helpImage.sprite = imageTemplate.sprite; helpImage.material = Glass(432,371,.995f);
            helpImage.color = Color.white; helpImage.raycastTarget = true;
            Bind(Label(helpRect,"Help title",18,14,396,38,23),QwenLocalization.HELP_TITLE);
            Bind(Label(helpRect,"Help body",18,62,396,291,15),QwenLocalization.HELP_BODY);
            locale.helpRoot = helpRect.gameObject;
            locale.helpHiddenControls = new[]{root.Find("Textures").gameObject,menu.executionControls,menu.stateLabel.gameObject,
                menu.heightLabel.gameObject,menu.heightSlider.gameObject,root.Find("Lowest").gameObject,
                root.Find("Highest").gameObject};
            helpRect.SetAsLastSibling(); locale.helpRoot.SetActive(false);

            var input = social.inputRoot.transform;
            var inputTitle = input.GetComponentsInChildren<Text>(true).First(t => t.transform.parent == input
                && Mathf.Abs(t.rectTransform.anchoredPosition.y + 18) < .1f);
            inputTitle.rectTransform.sizeDelta = new Vector2(592,45); inputTitle.fontSize = 26;
            Bind(inputTitle,QwenLocalization.ASK);
            Bind((Text)controller.promptInput.placeholder,QwenLocalization.PLACEHOLDER);
            var obsoleteReply = input.Find("Reply language");
            if (obsoleteReply != null) UnityEngine.Object.DestroyImmediate(obsoleteReply.gameObject);
            var close = input.Find("收起").GetComponent<Button>();
            ResizeInputButton(controller.sendButton,24); ResizeInputButton(close,434);
            Bind(controller.sendButton.GetComponentInChildren<Text>(),QwenLocalization.SEND);
            Bind(close.GetComponentInChildren<Text>(),QwenLocalization.COLLAPSE);
            locale.stopButton = MakeButton(input,"Stop generation",229,380,182,48,controller,"StopGeneration");
            Bind(locale.stopButton.GetComponentInChildren<Text>(),QwenLocalization.STOP);
            locale.labels = labels.ToArray(); locale.keys = keys.ToArray();
            QwenConsoleVisuals.ConfigureInputLayout(social);
            locale.Apply();
            return locale;
        }

        static void Bind(Text text,int key) { labels.Add(text); keys.Add(key); }
        static void Move(Transform target,float y)
        {
            var rect = (RectTransform)target;
            rect.anchoredPosition = new Vector2(rect.anchoredPosition.x,-y);
        }
        static void ResizeInputButton(Button button,float x)
        {
            var rect = button.GetComponent<RectTransform>();
            rect.anchoredPosition = new Vector2(x,-380); rect.sizeDelta = new Vector2(182,48);
            button.GetComponent<Image>().material = Glass(182,48,.94f);
            var text = button.GetComponentInChildren<Text>();
            text.rectTransform.sizeDelta = new Vector2(162,48); text.fontSize = 19;
        }
        static RectTransform Rect(Transform parent,string name,float x,float y,float w,float h)
        {
            var child = parent.Find(name);
            var rect = child == null ? new GameObject(name,typeof(RectTransform)).GetComponent<RectTransform>() : (RectTransform)child;
            rect.SetParent(parent,false);
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0,1);
            rect.anchoredPosition = new Vector2(x,-y); rect.sizeDelta = new Vector2(w,h);
            return rect;
        }
        static Text Label(Transform parent,string name,float x,float y,float w,float h,int size)
        {
            var rect = Rect(parent,name,x,y,w,h);
            var text = rect.GetComponent<Text>() ?? rect.gameObject.AddComponent<Text>();
            text.font = template.font; text.material = template.material; text.color = template.color;
            text.fontSize = size; text.alignment = TextAnchor.UpperLeft;
            text.raycastTarget = false; text.supportRichText = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap; text.verticalOverflow = VerticalWrapMode.Truncate;
            return text;
        }
        static Button MakeButton(Transform parent,string name,float x,float y,float w,float h,UdonSharpBehaviour target,string method)
        {
            var rect = Rect(parent,name,x,y,w,h);
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.sprite = imageTemplate.sprite; image.material = Glass(w,h,.94f); image.color = Color.white;
            var button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image; button.navigation = new Navigation {mode = Navigation.Mode.None};
            while (button.onClick.GetPersistentEventCount() > 0) UnityEventTools.RemovePersistentListener(button.onClick,0);
            UnityEventTools.AddStringPersistentListener(button.onClick,UdonSharpEditorUtility.GetBackingUdonBehaviour(target).SendCustomEvent,method);
            var label = Label(rect,"Label",5,0,w - 10,h,18);
            label.alignment = TextAnchor.MiddleCenter;
            return button;
        }
        static Material Glass(float w,float h,float opacity)
        {
            var mat = QwenGraphDisplayBuilder.Material(QwenSceneInstaller.Folder + "/LocaleGlass-" + w + "x" + h + "-" + Mathf.RoundToInt(opacity * 1000) + ".mat","QwenWorld/UI/GlassScreen");
            mat.CopyPropertiesFromMaterial(imageTemplate.material);
            mat.SetVector("_PanelSize",new Vector4(w,h,0,0)); mat.SetFloat("_Opacity",opacity);
            EditorUtility.SetDirty(mat);
            return mat;
        }
    }
}
