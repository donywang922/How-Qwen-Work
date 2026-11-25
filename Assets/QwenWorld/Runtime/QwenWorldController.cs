using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common;
using ShaderGPT.Udon;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.Manual)]
    public class QwenWorldController : UdonSharpBehaviour
    {
        public QwenTextureRunner runner;
        public GPTTokenizer tokenizer;
        public InputField promptInput;
        public Text statusText;
        public Button sendButton;
        public bool supportsInference = true;
        public int maxNewTokens = 64;
        public int maxPromptCharacters = 1200;
        public float jumpImpulse = 3;
        public QwenExhibitState exhibit;
        public QwenLocalization localization;
        [UdonSynced]
        private string sharedHistory = "";
        [UdonSynced]
        private string sharedPrompt = "";
        [UdonSynced]
        private string sharedResponse = "";
        [UdonSynced]
        private string sharedSpeaker = "";
        [UdonSynced]
        private int sharedState; // 0 idle, 1 generating, 2 stopped, 3 error
        [UdonSynced]
        private int sharedSequence;
        private string historyChatML = "";
        private bool dirty;
        private float lastSync;
        private float syncInterval = .5f;
        private float nextProgressSync, nextProgressUi, localStatusUntil;
        private bool progressWasRunning;
        private string readableTranscript = "";
        public void Start()
        {
            EnableLocalJump();
            if (runner != null)
                runner.controller = this;
            if (sendButton != null)
                sendButton.interactable = supportsInference;
            RefreshText();
        }

        private void EnableLocalJump()
        {
            var player=Networking.LocalPlayer;
            if(Utilities.IsValid(player)) player.SetJumpImpulse(jumpImpulse);
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            if(Utilities.IsValid(player)&&player.isLocal) EnableLocalJump();
        }

        public override void OnPlayerRespawn(VRCPlayerApi player)
        {
            if(Utilities.IsValid(player)&&player.isLocal) EnableLocalJump();
        }

        private bool IsLocalOwner()
        {
            return Networking.LocalPlayer == null || Networking.IsOwner(gameObject);
        }

        public bool CanControlInference()
        {
            return supportsInference && runner != null && (exhibit == null || exhibit.IsLocalHolder()) && (sharedState != 1 || IsLocalOwner());
        }

        public bool IsGenerating()
        {
            return sharedState == 1;
        }

        public bool IsLocalExecutionOwner()
        {
            return IsLocalOwner();
        }

        public string GetTranscript()
        {
            return readableTranscript;
        }

        public string GetSpeaker()
        {
            return sharedSpeaker;
        }

        public void SendPrompt()
        {
            if (!supportsInference || runner == null || tokenizer == null)
                return;
            if (exhibit != null && !exhibit.IsLocalHolder())
            {
                SetLocalStatus(LocalText(QwenLocalization.NEED_KEYBOARD));
                return;
            }

            if (sharedState == 1 && !IsLocalOwner())
            {
                SetLocalStatus(LocalText(QwenLocalization.BUSY));
                return;
            }

            string question = promptInput == null ? "你好，请简短介绍自己。" : promptInput.text.Trim();
            if (question.Length == 0)
                return;
            if (question.Length > maxPromptCharacters)
            {
                SetLocalStatus(LocalText(QwenLocalization.SHORTEN));
                return;
            }

            if (Networking.LocalPlayer != null)
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            if (!IsLocalOwner())
            {
                SetLocalStatus(LocalText(QwenLocalization.OWNERSHIP));
                return;
            }

            runner.StopRun();
            tokenizer.decodeState = 0;
            // Text history is reconstructed from completed shared turns. Retokenize after an owner change.
            historyChatML = sharedHistory;
            int[] ids = tokenizer.Encode(BuildChatInput(historyChatML,question));
            while (ids.Length + maxNewTokens > runner.contextLength && historyChatML.Length > 0)
            {
                int firstEnd = historyChatML.IndexOf("<|im_end|>\n");
                int secondEnd = firstEnd < 0 ? -1 : historyChatML.IndexOf("<|im_end|>\n", firstEnd + 11);
                historyChatML = secondEnd < 0 ? "" : historyChatML.Substring(secondEnd + 11);
                ids = tokenizer.Encode(BuildChatInput(historyChatML,question));
            }

            if (ids.Length + 1 >= runner.contextLength)
            {
                SetLocalStatus(LocalText(QwenLocalization.CONTEXT));
                return;
            }

            sharedHistory = historyChatML;
            sharedPrompt = question;
            sharedResponse = "";
            sharedSpeaker = Networking.LocalPlayer == null ? "Editor" : Networking.LocalPlayer.displayName;
            sharedState = 1;
            sharedSequence++;
            runner.Begin(ids, maxNewTokens);
            MarkDirty();
            RefreshText();
        }

        public void OnOutputToken()
        {
            if (!IsLocalOwner() || sharedState != 1)
                return;
            int token = runner.outputToken;
            if (token == tokenizer.eos_token_id || token == tokenizer.pad_token_id)
            {
                FinishTurn(0);
                return;
            }

            sharedResponse += tokenizer.Decode(token);
            if (sharedResponse.Length >= 4000)
            {
                FinishTurn(2);
                return;
            }

            MarkDirty();
            RefreshText();
        }

        public void OnEngineFinished()
        {
            if (IsLocalOwner() && sharedState == 1)
                FinishTurn(0);
        }

        public void OnEngineError(string message)
        {
            sharedState = 3;
            Debug.LogError("Qwen inference: " + message);
            SetLocalStatus(LocalText(QwenLocalization.ERROR));
            MarkDirty();
        }

        private void FinishTurn(int state)
        {
            runner.StopRun();
            tokenizer.decodeState = 0;
            sharedState = state;
            sharedHistory = historyChatML + "<|im_start|>user\n" + sharedPrompt + "<|im_end|>\n" + "<|im_start|>assistant\n" + sharedResponse + "<|im_end|>\n";
            if (sharedHistory.Length > 6000)
                sharedHistory = ""; // token-bound trimming is done before the next request.
            MarkDirty();
            RefreshText();
        }

        public void StopGeneration()
        {
            if (supportsInference && IsLocalOwner() && sharedState == 1)
                FinishTurn(2);
        }

        public void ResetConversation()
        {
            if (!supportsInference)
                return;
            if (exhibit != null && !exhibit.IsLocalHolder())
                return;
            if (sharedState == 1 && !IsLocalOwner())
                return;
            if (Networking.LocalPlayer != null)
                Networking.SetOwner(Networking.LocalPlayer, gameObject);
            runner.StopRun();
            tokenizer.decodeState = 0;
            sharedSequence++;
            sharedHistory = "";
            historyChatML = "";
            sharedPrompt = "";
            sharedResponse = "";
            sharedState = 0;
            MarkDirty();
            RefreshText();
        }

        private void SetLocalStatus(string value)
        {
            localStatusUntil = Time.unscaledTime + 3;
            if (statusText != null)
                statusText.text = value;
        }

        private void MarkDirty()
        {
            dirty = true;
        }

        private void RefreshText()
        {
            string display = sharedHistory;
            if (sharedState == 1)
                display += "<|im_start|>user\n" + sharedPrompt + "<|im_end|>\n<|im_start|>assistant\n" + sharedResponse;
            string colon = localization == null || localization.language == 0 || localization.language == 2 ? "：" : ": ";
            readableTranscript = display.Replace("<|im_start|>user\n",LocalText(QwenLocalization.YOU) + colon)
                .Replace("<|im_start|>assistant\n",LocalText(QwenLocalization.QWEN) + colon).Replace("<|im_end|>\n", "\n\n");
            if (readableTranscript.Length == 0)
                readableTranscript = LocalText(QwenLocalization.WAIT);
            if (!supportsInference && promptInput != null)
                promptInput.text = sharedPrompt;
            if (statusText != null) statusText.text = GetProgressText();
        }

        public string LocalText(int key) { return localization == null ? "" : localization.T(key); }
        private string FormatText(int key,string a,string b,string c,string d)
        { return localization == null ? "" : localization.F(key,a,b,c,d); }
        public string BuildChatInput(string history,string question)
        {
            return history + "<|im_start|>user\n" + question + "<|im_end|>\n<|im_start|>assistant\n<think>\n\n</think>\n\n";
        }
        public void RefreshLocalizedText() { RefreshText(); }
        public string GetCompactProgressText()
        {
            string value = GetProgressText();
            int newline = value.IndexOf("\n");
            return newline < 0 ? value : value.Substring(0,newline);
        }

        public string GetProgressText()
        {
            bool local = supportsInference && runner != null && IsLocalOwner();
            int total = local ? runner.GetPromptTokenCount() : exhibit == null ? 0 : exhibit.progressPromptTotal;
            int done = local ? Mathf.Min(runner.position,total) : exhibit == null ? 0 : exhibit.progressPromptDone;
            int tokens = local ? runner.generatedCount : exhibit == null ? 0 : exhibit.progressOutputTokens;
            int op = local ? runner.passCursor : exhibit == null ? 0 : exhibit.progressOperator;
            int ops = local && runner.steps != null ? runner.steps.Length : exhibit == null ? 849 : exhibit.progressOperatorTotal;
            int layer = local ? runner.GetProgressLayer() : exhibit == null ? 0 : exhibit.progressLayer;
            if (sharedState == 3) return LocalText(QwenLocalization.ERROR);
            if (sharedState != 1)
                return sharedState == 2 ? FormatText(QwenLocalization.STOPPED,tokens.ToString(),"","","")
                    : sharedResponse.Length > 0 ? FormatText(QwenLocalization.COMPLETED,tokens.ToString(),"","","") : LocalText(QwenLocalization.READY);
            if (total <= 0) return LocalText(QwenLocalization.SYNC);
            bool paused = local ? runner.paused : exhibit != null && exhibit.computationPaused;
            float fraction = (float)op / Mathf.Max(1,ops);
            string stage = done < total ? FormatText(QwenLocalization.PREFILL,done.ToString(),total.ToString(),
                Mathf.FloorToInt((done + fraction) / total * 100).ToString(),"")
                : FormatText(QwenLocalization.GENERATED,tokens.ToString(),"","","");
            return (paused ? LocalText(QwenLocalization.PAUSED_PREFIX) : "") + stage + "\n"
                + FormatText(QwenLocalization.TOKEN,Mathf.FloorToInt(fraction * 100).ToString(),"","","") + " · "
                + (layer > 0 ? FormatText(QwenLocalization.LAYER,layer.ToString(),"","","") : LocalText(QwenLocalization.EMBEDDING))
                + " · " + FormatText(QwenLocalization.OPERATOR,op.ToString(),ops.ToString(),"","");
        }

        public void Update()
        {
            if (supportsInference && runner != null && exhibit != null && IsLocalOwner()
                && Time.unscaledTime >= nextProgressSync && (runner.running || progressWasRunning))
            {
                nextProgressSync = Time.unscaledTime + .5f;
                progressWasRunning = runner.running;
                exhibit.PublishProgressLocal(Mathf.Min(runner.position,runner.GetPromptTokenCount()),runner.GetPromptTokenCount(),
                    runner.generatedCount,runner.passCursor,runner.steps == null ? 849 : runner.steps.Length,runner.GetProgressLayer());
            }
            if (Time.unscaledTime >= nextProgressUi && Time.unscaledTime >= localStatusUntil)
            {
                nextProgressUi = Time.unscaledTime + .1f;
                if (statusText != null) statusText.text = GetProgressText();
            }
            if (!dirty || !IsLocalOwner() || Time.realtimeSinceStartup - lastSync < syncInterval)
                return;
            lastSync = Time.realtimeSinceStartup;
            // Bound average full-snapshot bandwidth; late joiners receive complete current state.
            int estimate = (sharedHistory.Length + sharedPrompt.Length + sharedResponse.Length + sharedSpeaker.Length) * 2 + 512;
            syncInterval = Mathf.Max(.5f, estimate / 6000f);
            dirty = false;
            RequestSerialization();
        }

        public override void OnPostSerialization(SerializationResult result)
        {
            if (!result.success)
                dirty = true;
        }

        public override void OnDeserialization()
        {
            RefreshText();
        }

        public override void OnOwnershipTransferred(VRCPlayerApi player)
        {
            if (!IsLocalOwner() && runner != null)
                runner.StopRun();
            if (IsLocalOwner() && sharedState == 1 && (runner == null || !runner.running))
            {
                sharedState = 2;
                MarkDirty();
                RefreshText();
            }
        }

        public override bool OnOwnershipRequest(VRCPlayerApi requestingPlayer, VRCPlayerApi requestedOwner)
        {
            if (exhibit == null)
                return sharedState != 1;
            return sharedState != 1 && Utilities.IsValid(requestedOwner) && requestedOwner.playerId == exhibit.keyboardHolder;
        }
    }
}
