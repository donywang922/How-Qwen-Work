using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenSocialUI : UdonSharpBehaviour
    {
        public QwenExhibitState exhibit;
        public QwenWorldController controller;
        public Transform center, keyboardPivot;
        public GameObject inputRoot;
        public Text keyboardCaption;
        public Transform[] panels;
        public Text[] panelText, panelCaption;
        public Text[] panelProgress;
        public RectTransform[] panelContent;
        public CanvasGroup[] panelFade;
        public Vector3[] slotPositions;
        [HideInInspector]
        public int[] slotPlayerIds;
        private VRCPlayerApi[] players = new VRCPlayerApi[80];
        private int playerCount;
        private bool rebuildPlayers = true, lastMine, inputCollapsed;
        private float nextPanelUpdate, nextCandidateRegistration;
        private float keyboardAngle = 180;
        public float keyboardDegreesPerSecond = 540;
        private string lastTranscript = "";
        public void Start()
        {
            if (keyboardPivot != null) keyboardAngle = keyboardPivot.eulerAngles.y;
            slotPlayerIds = new int[panels.Length];
            for (int i = 0; i < slotPlayerIds.Length; i++)
            {
                slotPlayerIds[i] = -1;
                panels[i].gameObject.SetActive(false);
            }

            if (inputRoot != null)
                inputRoot.SetActive(false);
            PermissionChanged();
        }

        public void KeyboardInteract()
        {
            if (exhibit == null || controller == null || !controller.supportsInference)
                return;
            if (exhibit.IsLocalHolder())
            {
                OpenInput();
                return;
            }

            exhibit.ClaimKeyboardLocal();
            if (exhibit.IsLocalHolder()) OpenInput();
        }

        public void PermissionChanged()
        {
            if (exhibit == null || controller == null)
                return;
            var holder = VRCPlayerApi.GetPlayerById(exhibit.keyboardHolder);
            if (keyboardCaption != null)
                keyboardCaption.text = Utilities.IsValid(holder) && controller.localization != null
                    ? controller.localization.F(QwenLocalization.HELD,holder.displayName,"","","")
                    : controller.LocalText(controller.supportsInference ? QwenLocalization.APPROACH : QwenLocalization.PC_INPUT);
            bool mine = exhibit.IsLocalHolder();
            if (controller.localization != null && controller.localization.stopButton != null)
                controller.localization.stopButton.interactable = controller.supportsInference && controller.IsGenerating() && controller.IsLocalExecutionOwner();
            if (keyboardCaption != null) keyboardCaption.transform.parent.gameObject.SetActive(!mine || inputCollapsed);
            if (mine != lastMine)
            {
                lastMine = mine;
                inputCollapsed = false;
                if (controller.promptInput != null) controller.promptInput.DeactivateInputField();
            }
            if (inputRoot != null)
                inputRoot.SetActive(mine && !inputCollapsed);
            if (controller.sendButton != null)
                controller.sendButton.interactable = mine && !controller.IsGenerating() && controller.supportsInference;
            if (controller.promptInput != null)
                controller.promptInput.interactable = mine && controller.supportsInference;
        }

        public void OpenInput()
        {
            if (exhibit == null || !exhibit.IsLocalHolder() || inputRoot == null)
                return;
            inputCollapsed = false;
            inputRoot.SetActive(true);
            PermissionChanged();
            if (controller.promptInput != null && controller.promptInput.interactable) controller.promptInput.ActivateInputField();
        }

        public void CloseInput()
        {
            inputCollapsed = true;
            if (controller != null && controller.promptInput != null) controller.promptInput.DeactivateInputField();
            if (inputRoot != null) inputRoot.SetActive(false);
        }

        public void SendQuestion()
        {
            if (controller == null || exhibit == null || !exhibit.IsLocalHolder() || controller.IsGenerating())
                return;
            controller.SendPrompt();
            PermissionChanged();
        }

        public override void OnPlayerJoined(VRCPlayerApi player)
        {
            rebuildPlayers = true;
        }

        public override void OnPlayerLeft(VRCPlayerApi player)
        {
            rebuildPlayers = true;
        }

        public void Update()
        {
            if (exhibit == null || center == null)
                return;
            var holder = VRCPlayerApi.GetPlayerById(exhibit.keyboardHolder);
            if (keyboardPivot != null && Utilities.IsValid(holder))
            {
                Vector3 delta = holder.GetPosition() - center.position;
                float target = Mathf.Atan2(delta.x, delta.z) * Mathf.Rad2Deg;
                keyboardAngle = Mathf.MoveTowardsAngle(keyboardAngle, target, keyboardDegreesPerSecond * Time.unscaledDeltaTime);
                keyboardPivot.rotation = Quaternion.Euler(0, keyboardAngle, 0);
            }

            var local = Networking.LocalPlayer;
            if (controller != null && controller.supportsInference && Utilities.IsValid(local)
                && (local.GetPosition() - center.position).sqrMagnitude <= exhibit.keyboardAcquireDistance * exhibit.keyboardAcquireDistance
                && Time.unscaledTime >= nextCandidateRegistration)
            {
                nextCandidateRegistration = Time.unscaledTime + 2;
                exhibit.ClaimKeyboardLocal();
            }

            if (panelFade != null)
                for (int i = 0; i < panelFade.Length; i++)
                    if (panels[i].gameObject.activeSelf && panelFade[i].alpha < 1)
                        panelFade[i].alpha = Mathf.MoveTowards(panelFade[i].alpha, 1, Time.unscaledDeltaTime * 7);

            if (Time.unscaledTime < nextPanelUpdate)
                return;
            nextPanelUpdate = Time.unscaledTime + .25f;
            PermissionChanged();
            if (rebuildPlayers)
            {
                rebuildPlayers = false;
                for (int i = 0; i < players.Length; i++)
                    players[i] = null;
                VRCPlayerApi.GetPlayers(players);
                playerCount = 0;
                for (int i = 0; i < players.Length; i++)
                    if (Utilities.IsValid(players[i]))
                        playerCount++;
                for (int i = 0; i < players.Length - 1; i++)
                    for (int j = i + 1; j < players.Length; j++)
                        if (Utilities.IsValid(players[j]) && (!Utilities.IsValid(players[i]) || players[j].playerId < players[i].playerId))
                        {
                            var swap = players[i];
                            players[i] = players[j];
                            players[j] = swap;
                        }
            }

            if (slotPlayerIds == null)
                return;
            // Reserve existing valid positions first; every active rectangle has a unique slot.
            for (int s = 0; s < slotPlayerIds.Length; s++)
                if (slotPlayerIds[s] >= 0)
                {
                    var p = VRCPlayerApi.GetPlayerById(slotPlayerIds[s]);
                    if (!Utilities.IsValid(p) || (p.GetPosition() - center.position).sqrMagnitude > 27.04f)
                    {
                        slotPlayerIds[s] = -1;
                        panels[s].gameObject.SetActive(false);
                    }
                }

            for (int p = 0; p < playerCount; p++)
            {
                var player = players[p];
                if (!Utilities.IsValid(player))
                    continue;
                int old = -1;
                for (int s = 0; s < slotPlayerIds.Length; s++)
                    if (slotPlayerIds[s] == player.playerId)
                        old = s;
                if (old < 0 && (player.GetPosition() - center.position).sqrMagnitude > 21.16f)
                    continue;
                Vector3 head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position;
                int best = old;
                float score = old < 0 ? float.PositiveInfinity : (slotPositions[old] - head).sqrMagnitude;
                for (int s = 0; s < slotPlayerIds.Length; s++)
                    if (slotPlayerIds[s] < 0)
                    {
                        float candidate = (slotPositions[s] - head).sqrMagnitude;
                        if (candidate + (old >= 0 ? .30f : 0) < score)
                        {
                            best = s;
                            score = candidate;
                        }
                    }

                if (best < 0 || best == old)
                    continue;
                if (old >= 0)
                {
                    slotPlayerIds[old] = -1;
                    panels[old].gameObject.SetActive(false);
                }

                slotPlayerIds[best] = player.playerId;
                panelCaption[best].text = player.displayName;
                panels[best].gameObject.SetActive(true);
                panelFade[best].alpha = 0;
            }

            string transcript = controller == null ? "" : controller.GetTranscript();
            bool changed = transcript != lastTranscript;
            lastTranscript = transcript;
            string progress = controller == null ? "" : controller.GetCompactProgressText();
            if (panelProgress != null)
                for (int s = 0; s < panelProgress.Length; s++)
                    if (slotPlayerIds[s] >= 0 && panelProgress[s] != null && panelProgress[s].text != progress)
                        panelProgress[s].text = progress;
            for (int s = 0; s < slotPlayerIds.Length; s++)
                if (slotPlayerIds[s] >= 0 && (changed || panelText[s].text != transcript))
                {
                    panelText[s].text = transcript;
                    var viewport = (RectTransform)panelContent[s].parent;
                    float width = viewport.rect.width;
                    panelContent[s].SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, width);
                    float height = Mathf.Max(viewport.rect.height, panelText[s].preferredHeight + 10);
                    panelContent[s].SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, height);
                    panelContent[s].anchoredPosition = new Vector2(0, Mathf.Max(0, height - viewport.rect.height));
                }
        }
    }
}
