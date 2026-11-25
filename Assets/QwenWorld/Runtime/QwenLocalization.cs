using UdonSharp;
using UnityEngine;
using UnityEngine.UI;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenLocalization : UdonSharpBehaviour
    {
        public const int MENU = 0;
        public const int SHOW = 1;
        public const int HIDE = 2;
        public const int RUN = 3;
        public const int PAUSE = 4;
        public const int RESUME = 5;
        public const int STEP = 6;
        // Slots 7 and 8 are reserved to preserve serialized localization keys.
        public const int READY = 9;
        public const int WAIT = 10;
        public const int VIEWER = 11;
        public const int YOU = 12;
        public const int QWEN = 13;
        public const int ASK = 14;
        public const int PLACEHOLDER = 15;
        public const int SEND = 16;
        public const int COLLAPSE = 17;
        public const int FLOOR_HIDDEN = 18;
        public const int FLOOR_HEIGHT = 19;
        public const int STEP_ONTO = 20;
        public const int LOWEST = 21;
        public const int HEIGHT_MAX = 22;
        public const int HELD = 23;
        public const int APPROACH = 24;
        public const int PC_INPUT = 25;
        public const int NEED_KEYBOARD = 26;
        public const int BUSY = 27;
        public const int SHORTEN = 28;
        public const int OWNERSHIP = 29;
        public const int CONTEXT = 30;
        public const int ERROR = 31;
        public const int STOPPED = 32;
        public const int COMPLETED = 33;
        public const int SYNC = 34;
        public const int PREFILL = 35;
        public const int GENERATED = 36;
        public const int PAUSED_PREFIX = 37;
        public const int TOKEN = 38;
        public const int LAYER = 39;
        public const int EMBEDDING = 40;
        public const int OPERATOR = 41;
        public const int HELP_TITLE = 42;
        public const int HELP_BODY = 43;
        public const int STOP = 44;
        public const int RUNNING_STATE = 45;
        public const int PAUSED_STATE = 46;
        public const int STOPPED_STATE = 47;
        public const int COMPLETED_STATE = 48;
        public const int KeyCount = 49;
        public QwenWorldController controller;
        public QwenSocialUI social;
        public QwenLocalMenu menu;
        public QwenObservationPlatform observation;
        public Text[] labels;
        public int[] keys;
        public Button[] languageButtons;
        public Button stopButton;
        public GameObject helpRoot;
        public GameObject[] helpHiddenControls;
        [HideInInspector] public int language;
        private bool helpOpen;
        private string[] messages = {
            "展示菜单", "Exhibit menu", "展示メニュー", "전시 메뉴", // MENU
            "显示贴图", "Show textures", "テクスチャを表示", "텍스처 표시", // SHOW
            "隐藏贴图", "Hide textures", "テクスチャを非表示", "텍스처 숨기기", // HIDE
            "开始", "Start", "開始", "시작", // RUN
            "暂停", "Pause", "一時停止", "일시 정지", // PAUSE
            "继续", "Resume", "再開", "계속", // RESUME
            "单算子", "Step operator", "演算を1つ実行", "연산 한 단계", // STEP
            "", "", "", "", // Reserved
            "", "", "", "", // Reserved
            "就绪 · 等待输入", "Ready · Ask a question", "準備完了 · 入力待ち", "준비 완료 · 입력 대기", // READY
            "等待输入。", "Waiting for a question.", "入力を待っています。", "질문을 기다리는 중입니다.", // WAIT
            "观看端", "Viewer", "閲覧のみ", "보기 전용", // VIEWER
            "你", "You", "あなた", "사용자", // YOU
            "千问", "Qwen", "Qwen", "Qwen", // QWEN
            "向千问提问", "Ask Qwen", "Qwenに質問", "Qwen에게 질문", // ASK
            "点击这里输入问题…", "Click here to type a question…", "ここを押して質問を入力…", "여기를 눌러 질문을 입력하세요…", // PLACEHOLDER
            "发送", "Send", "送信", "보내기", // SEND
            "收起", "Close", "閉じる", "닫기", // COLLAPSE
            "亚克力平台：最低 · 隐藏", "Deck: lowest · hidden", "足場：最低位置・非表示", "발판: 최저 높이 · 숨김", // FLOOR_HIDDEN
            "亚克力平台：{0} / {1} 米", "Deck: {0} / {1} m", "足場：{0} / {1} m", "발판: {0} / {1} m", // FLOOR_HEIGHT
            "先走到白色地面上再升起", "Step onto the white floor first", "白い床に乗ってから上げてください", "흰 바닥에 올라간 뒤 높여 주세요", // STEP_ONTO
            "最低隐藏", "Lowest: hidden", "最低位置で非表示", "최저 높이: 숨김", // LOWEST
            "5.5 米", "5.5 m", "5.5 m", "5.5 m", // HEIGHT_MAX
            "输入：{0}\n离开范围后自动交接", "Input: {0}\nPasses on when they walk away", "入力：{0}\n離れると自動で交代", "입력: {0}\n범위를 벗어나면 자동 인계", // HELD
            "靠近圆桌即可输入", "Approach the table to type", "テーブルに近づくと入力できます", "테이블에 다가가면 입력할 수 있습니다", // APPROACH
            "由附近 PC 玩家输入", "A nearby PC player can type", "近くのPCプレイヤーが入力できます", "근처 PC 플레이어가 입력할 수 있습니다", // PC_INPUT
            "靠近圆桌即可自动获得输入权限。", "Approach the table to get input control.", "テーブルに近づくと入力権限を得られます。", "테이블에 다가가면 입력 권한을 얻습니다.", // NEED_KEYBOARD
            "其他玩家正在生成，请稍候。", "Another player is generating. Please wait.", "別のプレイヤーが生成中です。お待ちください。", "다른 플레이어가 생성 중입니다. 기다려 주세요.", // BUSY
            "问题太长，请缩短后重试。", "Please shorten the question.", "質問を短くしてください。", "질문을 줄여 주세요.", // SHORTEN
            "等待计算权限交接，请稍后发送。", "Waiting for control. Please try again shortly.", "権限の移行中です。少し待って送信してください。", "권한을 넘기는 중입니다. 잠시 후 다시 보내 주세요.", // OWNERSHIP
            "问题超过上下文容量，请缩短。", "Question exceeds context capacity. Please shorten it.", "質問が文脈の上限を超えています。短くしてください。", "질문이 문맥 한도를 초과했습니다. 줄여 주세요.", // CONTEXT
            "推理出错 · 请查看执行端日志", "Error · Check the executing client log", "推論エラー · 実行側のログを確認", "추론 오류 · 실행 클라이언트 로그 확인", // ERROR
            "已停止 · 输出 {0} token", "Stopped · {0} token output", "停止 · 出力 {0} token", "중지됨 · {0} token 출력", // STOPPED
            "已完成 · 输出 {0} token", "Complete · {0} token output", "完了 · 出力 {0} token", "완료 · {0} token 출력", // COMPLETED
            "开始计算 · 等待进度同步", "Starting · Waiting for progress", "開始中 · 進捗の同期待ち", "시작 중 · 진행 상황 동기화 대기", // SYNC
            "读取上下文 {0}/{1} token · {2}%", "Context {0}/{1} token · {2}%", "文脈読込 {0}/{1} token · {2}%", "문맥 읽기 {0}/{1} token · {2}%", // PREFILL
            "生成中 · 已输出 {0} token", "Generating · {0} token output", "生成中 · 出力 {0} token", "생성 중 · {0} token 출력", // GENERATED
            "已暂停 · ", "Paused · ", "一時停止 · ", "일시 정지 · ", // PAUSED_PREFIX
            "token {0}%", "token {0}%", "token {0}%", "token {0}%", // TOKEN
            "层 {0}/24", "Layer {0}/24", "層 {0}/24", "레이어 {0}/24", // LAYER
            "嵌入 / 输出", "Embedding / output", "埋め込み / 出力", "임베딩 / 출력", // EMBEDDING
            "算子 {0}/{1}", "op {0}/{1}", "演算 {0}/{1}", "연산 {0}/{1}", // OPERATOR
            "操作与展示说明", "Controls and display", "操作と表示について", "조작 및 표시 안내", // HELP_TITLE
            "Tab / 双击板机：打开菜单。\n握拳与放松的双手靠近千问：显示贴图。\n键盘：靠近获得权限，走远后自动交接。\n平台：站上白地后拖动滑条，掉落会复位。\nCRT 和读写线仅在执行端显示。四边描边代表读，四角代表写。\n核心有24层；暂停时显示下一个算子。\n\nAI立绘：ZipZipPipe · Pixiv 148186519\n已抠图、修改表情 · 署名使用，不可商用。", "Tab / double trigger: open menu.\nFist + relaxed hand near Qwen: show textures.\nKeyboard: approach to claim; walk away to pass it on.\nDeck: step onto white floor, then raise slider. Falling resets it.\nCRT data and lines are local. Edges = read; corners = write.\nCore: 24 layers. Pause previews the next operator.\n\nAI art: ZipZipPipe · Pixiv 148186519\nCutout / expression edits. Credit; noncommercial.", "Tab / トリガー2回：メニュー。\nQwenの近くで片手を握り、もう片手を開いて近づけるとテクスチャを表示。\n入力権限は近づくと取得し、離れると自動で交代。\n白い床に乗り、スライダーで足場を上げます。落ちるとリセット。\nCRTと線は実行側のみ。辺の枠は読み込み、角の枠は書き込み。\nコアは24層。一時停止中は次の演算を表示。\n\nAI立ち絵：ZipZipPipe · Pixiv 148186519\n背景除去・表情変更。要クレジット・非商用。", "Tab / 트리거 두 번: 메뉴 열기.\nQwen 근처에서 한 손은 주먹, 다른 손은 펴서 모으면 텍스처 표시.\n키보드: 가까이 가면 권한 획득, 멀어지면 자동 인계.\n흰 바닥 위에서 슬라이더로 발판 높이 조절. 떨어지면 초기화.\nCRT와 선은 실행 측에만 표시. 변 테두리는 읽기, 모서리는 쓰기.\n코어는 24개 레이어. 일시 정지하면 다음 연산 표시.\n\nAI 일러스트: ZipZipPipe · Pixiv 148186519\n배경 제거·표정 수정. 출처 표시·비상업용.", // HELP_BODY
            "停止", "Stop", "停止", "중지", // STOP
            "运行中", "Running", "実行中", "실행 중", // RUNNING_STATE
            "已暂停", "Paused", "一時停止中", "일시 정지됨", // PAUSED_STATE
            "已停止", "Stopped", "停止済み", "중지됨", // STOPPED_STATE
            "已完成", "Complete", "完了", "완료", // COMPLETED_STATE
        };
        public void Start() { Apply(); }
        public string T(int key) { return messages[key * 4 + Mathf.Clamp(language,0,3)]; }
        public string F(int key,string a,string b,string c,string d)
        { return T(key).Replace("{0}",a).Replace("{1}",b).Replace("{2}",c).Replace("{3}",d); }
        public void Chinese() { language = 0; Apply(); }
        public void English() { language = 1; Apply(); }
        public void Japanese() { language = 2; Apply(); }
        public void Korean() { language = 3; Apply(); }
        public bool IsHelpOpen() { return helpOpen; }
        public void ToggleHelp() { helpOpen = !helpOpen; RefreshHelp(); if (menu != null) menu.RefreshMenu(); }
        private void RefreshHelp()
        {
            if (helpRoot != null) helpRoot.SetActive(helpOpen);
            if (helpHiddenControls != null)
                for (int i = 0; i < helpHiddenControls.Length; i++) helpHiddenControls[i].SetActive(!helpOpen);
        }
        public void Apply()
        {
            if (labels != null && keys != null)
                for (int i = 0; i < labels.Length; i++)
                    if (labels[i] != null)
                        labels[i].text = T(keys[i]) + (keys[i] == MENU && !controller.supportsInference ? " · " + T(VIEWER) : "");
            if (languageButtons != null)
                for (int i = 0; i < languageButtons.Length; i++)
                    languageButtons[i].GetComponent<Image>().color = i == language
                        ? new Color(.48f,.80f,1,1) : Color.white;
            RefreshHelp();
            if (controller != null) controller.RefreshLocalizedText();
            if (social != null) social.PermissionChanged();
            if (menu != null) menu.RefreshMenu();
            if (observation != null) observation.RefreshLocalizedLabel();
            if (social != null && social.panelProgress != null)
                for (int i = 0; i < social.panelProgress.Length; i++)
                    if (social.panelProgress[i] != null) social.panelProgress[i].text = controller.GetCompactProgressText();
        }
    }
}
