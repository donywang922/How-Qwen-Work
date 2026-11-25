using UdonSharp;
using UnityEngine;
using UnityEngine.UI;
using VRC.SDKBase;
using VRC.Udon.Common;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenLocalMenu : UdonSharpBehaviour
    {
        public QwenWorldController controller;
        public QwenTextureRunner runner;
        public QwenExhibitState exhibit;
        public QwenObservationPlatform observation;
        public Slider heightSlider;
        public Text heightLabel;
        public GameObject menuRoot;
        public GameObject[] textureRoots;
        public GameObject executionControls;
        public Renderer girlRenderer;
        public Transform girlOrigin;
        public Text visibilityLabel, runLabel, stateLabel;
        public Button runButton, stepButton;
        public ParticleSystem girlWishParticles, playerWishParticles;
        public float doubleClickSeconds = .32f;
        public float wishHoldSeconds = .45f;
        public float wishCooldownSeconds = 5;
        public float wishRange = 4.5f;
        [HideInInspector]
        public bool texturesVisible;
        private Material expressionMaterial;
        private bool initialized;
        private bool leftUse, rightUse, leftGrab, rightGrab;
        private float leftClickTime = -100, rightClickTime = -100;
        private bool leftClickHeld, rightClickHeld;
        private float wishStarted = -1, lastWish = -100, releaseStarted = -1;
        private bool wishLatched;
        private float nextGestureCheck, nextStateRefresh;
        private Vector3 frontMenuOffset;
        private bool hasFrontAnchor;
        public void Start()
        {
            if (initialized)
                return;
            initialized = true;
            if (girlRenderer != null)
                expressionMaterial = girlRenderer.material;
            texturesVisible = exhibit != null && exhibit.texturesShown;
            ApplyVisibility();
            if (menuRoot != null)
                menuRoot.SetActive(false);
        }

        public void Update()
        {
            if (Input.GetKeyDown(KeyCode.Tab) && (controller == null || controller.promptInput == null || !controller.promptInput.isFocused))
                ToggleMenu();
            if (menuRoot != null && menuRoot.activeSelf && Time.unscaledTime >= nextStateRefresh)
            {
                nextStateRefresh = Time.unscaledTime + .1f;
                RefreshMenu();
            }
        }

        public override void InputUse(bool value, UdonInputEventArgs args)
        {
            bool left = args.handType == HandType.LEFT;
            if (left)
                leftUse = value;
            else
                rightUse = value;
            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player) || !player.IsUserInVR())
                return;
            // Menu button clicks must not count as an opening gesture.
            if (menuRoot != null && menuRoot.activeSelf)
            {
                ResetTriggerClicks();
                return;
            }

            if (RegisterTriggerClick(value, left ? 0 : 1, Time.unscaledTime))
                OpenMenu();
        }

        public override void InputGrab(bool value, UdonInputEventArgs args)
        {
            if (args.handType == HandType.LEFT)
                leftGrab = value;
            else
                rightGrab = value;
        }

        public bool RegisterTriggerClick(bool down, int hand, float now)
        {
            bool held = hand == 0 ? leftClickHeld : rightClickHeld;
            if (hand == 0)
                leftClickHeld = down;
            else
                rightClickHeld = down;
            if (!down || held)
                return false;
            float previous = hand == 0 ? leftClickTime : rightClickTime;
            bool doubleClick = now >= previous && now - previous <= doubleClickSeconds;
            if (hand == 0)
                leftClickTime = doubleClick ? -100 : now;
            else
                rightClickTime = doubleClick ? -100 : now;
            return doubleClick;
        }

        private void ResetTriggerClicks()
        {
            leftClickTime = rightClickTime = -100;
            leftClickHeld = rightClickHeld = false;
        }

        public void ToggleMenu()
        {
            if (menuRoot != null && menuRoot.activeSelf)
                CloseMenu();
            else
                OpenMenu();
        }

        public void OpenMenu()
        {
            if (!initialized)
                Start();
            if (menuRoot == null)
                return;
            menuRoot.SetActive(true);
            ResetTriggerClicks();
            PlaceInFront();
            RefreshMenu();
        }

        public void CloseMenu()
        {
            if (menuRoot != null)
                menuRoot.SetActive(false);
            ResetTriggerClicks();
        }

        public void ToggleTextures()
        {
            if (exhibit != null)
            {
                exhibit.SetVisibilityLocal(!exhibit.texturesShown);
                return;
            }

            texturesVisible = !texturesVisible;
            ApplyVisibility();
        }

        public void SetSharedVisibility(bool value)
        {
            if (!initialized)
                Start();
            texturesVisible = value;
            ApplyVisibility();
        }

        private void ApplyVisibility()
        {
            if (textureRoots != null)
                for (int i = 0; i < textureRoots.Length; i++)
                    if (textureRoots[i] != null)
                        textureRoots[i].SetActive(texturesVisible);
            if (expressionMaterial != null)
                expressionMaterial.SetFloat("_EyesClosed", texturesVisible ? 1 : 0);
            if (runner != null)
                runner.SetPresentationMode(texturesVisible);
            RefreshMenu();
        }

        public void ToggleExecution()
        {
            if (!texturesVisible || controller == null || !controller.CanControlInference())
                return;
            if (runner.running)
                runner.PauseOrResume();
            else
                controller.SendPrompt();
            RefreshMenu();
        }

        public void OneOperator()
        {
            if (!texturesVisible || controller == null || !controller.CanControlInference() || runner == null || !runner.running)
                return;
            runner.StepOperator();
            RefreshMenu();
        }

        public void ChangeObservationHeight()
        {
            if (observation != null) observation.HeightChanged();
        }

        public void RefreshMenu()
        {
            bool inference = runner != null && runner.supportsInference;
            bool canControl = controller != null && controller.CanControlInference();
            if (visibilityLabel != null)
                visibilityLabel.text = controller.LocalText(texturesVisible ? QwenLocalization.HIDE : QwenLocalization.SHOW);
            if (executionControls != null)
                executionControls.SetActive(texturesVisible && inference && (controller.localization == null || !controller.localization.IsHelpOpen()));
            if (runLabel != null)
                runLabel.text = controller.LocalText(runner != null && runner.running ? (runner.paused ? QwenLocalization.RESUME : QwenLocalization.PAUSE) : QwenLocalization.RUN);
            if (runButton != null)
                runButton.interactable = texturesVisible && canControl;
            if (stepButton != null)
                stepButton.interactable = texturesVisible && canControl && runner != null && runner.running;
            if (stateLabel != null)
            {
                stateLabel.text = controller != null ? controller.GetProgressText() : "等待模型状态";
            }
        }

        private void PlaceInFront()
        {
            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player) || menuRoot == null)
                return;
            var head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            menuRoot.transform.SetPositionAndRotation(head.position + head.rotation * new Vector3(0, -.12f, .8f), head.rotation);
            frontMenuOffset=menuRoot.transform.position-player.GetPosition();
            hasFrontAnchor=true;
        }

        private void FollowFrontMenu(Vector3 playerPosition)
        {
            if(hasFrontAnchor&&menuRoot!=null) menuRoot.transform.position=playerPosition+frontMenuOffset;
        }

        public override void PostLateUpdate()
        {
            var player = Networking.LocalPlayer;
            if (!Utilities.IsValid(player))
                return;
            var head = player.GetTrackingData(VRCPlayerApi.TrackingDataType.Head);
            bool vr=player.IsUserInVR();
            if (menuRoot != null && menuRoot.activeSelf)
            {
                // Translate the front menu with the player, but keep its opening rotation
                // fixed so dragging the slider does not move the target under the pointer.
                FollowFrontMenu(player.GetPosition());
            }

            if (!vr || Time.unscaledTime < nextGestureCheck)
                return;
            nextGestureCheck = Time.unscaledTime + .08f;
            bool pose = IsWishPose(player, head.position, head.rotation);
            if (AdvanceWishHold(pose, Time.unscaledTime))
                RevealFromWish();
        }

        private bool IsWishPose(VRCPlayerApi player, Vector3 head, Quaternion headRotation)
        {
            if (girlOrigin == null || (player.GetPosition() - girlOrigin.position).sqrMagnitude > wishRange * wishRange)
                return false;
            Vector3 target = girlOrigin.position + Vector3.up * 1.15f;
            if (Vector3.Dot(headRotation * Vector3.forward, (target - head).normalized) < .5f)
                return false;
            Vector3 left = player.GetTrackingData(VRCPlayerApi.TrackingDataType.LeftHand).position;
            Vector3 right = player.GetTrackingData(VRCPlayerApi.TrackingDataType.RightHand).position;
            float scale = Mathf.Clamp(player.GetAvatarEyeHeightAsMeters() / 1.6f, .2f, 4);
            if ((left - right).sqrMagnitude > .22f * .22f * scale * scale)
                return false;
            int l = ReadHandPose(player, true), r = ReadHandPose(player, false);
            return (l == 1 && r == 2 && !rightUse && !rightGrab) || (r == 1 && l == 2 && !leftUse && !leftGrab);
        }

        private int ReadHandPose(VRCPlayerApi player, bool left)
        {
            float index = FingerBend(player, left ? HumanBodyBones.LeftIndexProximal : HumanBodyBones.RightIndexProximal, left ? HumanBodyBones.LeftIndexIntermediate : HumanBodyBones.RightIndexIntermediate, left ? HumanBodyBones.LeftIndexDistal : HumanBodyBones.RightIndexDistal);
            float middle = FingerBend(player, left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal, left ? HumanBodyBones.LeftMiddleIntermediate : HumanBodyBones.RightMiddleIntermediate, left ? HumanBodyBones.LeftMiddleDistal : HumanBodyBones.RightMiddleDistal);
            float ring = FingerBend(player, left ? HumanBodyBones.LeftRingProximal : HumanBodyBones.RightRingProximal, left ? HumanBodyBones.LeftRingIntermediate : HumanBodyBones.RightRingIntermediate, left ? HumanBodyBones.LeftRingDistal : HumanBodyBones.RightRingDistal);
            float little = FingerBend(player, left ? HumanBodyBones.LeftLittleProximal : HumanBodyBones.RightLittleProximal, left ? HumanBodyBones.LeftLittleIntermediate : HumanBodyBones.RightLittleIntermediate, left ? HumanBodyBones.LeftLittleDistal : HumanBodyBones.RightLittleDistal);
            int pose = ClassifyHandPose(index, middle, ring, little);
            if (pose != 1)
                return pose;
            // Reject a straight raised thumb (thumbs-up is not a closed fist).
            Vector3 wrist = player.GetBonePosition(left ? HumanBodyBones.LeftHand : HumanBodyBones.RightHand);
            Vector3 knuckle = player.GetBonePosition(left ? HumanBodyBones.LeftMiddleProximal : HumanBodyBones.RightMiddleProximal);
            Vector3 thumbBase = player.GetBonePosition(left ? HumanBodyBones.LeftThumbProximal : HumanBodyBones.RightThumbProximal);
            Vector3 thumbEnd = player.GetBonePosition(left ? HumanBodyBones.LeftThumbDistal : HumanBodyBones.RightThumbDistal);
            if (wrist == Vector3.zero || knuckle == Vector3.zero || thumbBase == Vector3.zero || thumbEnd == Vector3.zero)
                return 0;
            if ((thumbEnd - thumbBase).sqrMagnitude < .00000001f || (knuckle - wrist).sqrMagnitude < .00000001f)
                return 0;
            return Vector3.Dot((thumbEnd - thumbBase).normalized, (knuckle - wrist).normalized) > .8f ? 0 : 1;
        }

        private float FingerBend(VRCPlayerApi player, HumanBodyBones proximal, HumanBodyBones intermediate, HumanBodyBones distal)
        {
            Vector3 a = player.GetBonePosition(proximal), b = player.GetBonePosition(intermediate), c = player.GetBonePosition(distal);
            if (a == Vector3.zero || b == Vector3.zero || c == Vector3.zero || (b - a).sqrMagnitude < .00000001f || (c - b).sqrMagnitude < .00000001f)
                return -10;
            return Vector3.Dot((b - a).normalized, (c - b).normalized);
        }

        // 1 fist, 2 relaxed/default-like hand, 0 other/unavailable. Avatar gesture parameter IDs are not world API inputs.
        public int ClassifyHandPose(float index, float middle, float ring, float little)
        {
            if (index < -1 || middle < -1 || ring < -1 || little < -1)
                return 0;
            if (index < .65f && middle < .65f && ring < .65f && little < .65f)
                return 1;
            if (index > .72f && middle > .72f && ring > .72f && little > .72f)
                return 2;
            return 0;
        }

        public bool AdvanceWishHold(bool matching, float now)
        {
            if (!matching)
            {
                wishStarted = -1;
                if (releaseStarted < 0)
                    releaseStarted = now;
                if (now - releaseStarted >= .2f)
                    wishLatched = false;
                return false;
            }

            releaseStarted = -1;
            if (wishStarted < 0)
                wishStarted = now;
            if (wishLatched || now - wishStarted < wishHoldSeconds || now - lastWish < wishCooldownSeconds)
                return false;
            wishLatched = true;
            lastWish = now;
            return true;
        }

        public void RevealFromWish()
        {
            if (exhibit != null)
            {
                exhibit.WishLocal();
                return;
            }

            if (texturesVisible)
                return;
            texturesVisible = true;
            ApplyVisibility();
            var player = Networking.LocalPlayer;
            PlayWishBurst(Utilities.IsValid(player) ? player.GetPosition() : Vector3.zero);
        }

        public void PlayWishBurst(Vector3 feet)
        {
            if (playerWishParticles != null)
            {
                playerWishParticles.transform.position = feet + Vector3.up * .03f;
                playerWishParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                playerWishParticles.Play();
            }

            if (girlWishParticles != null && girlOrigin != null)
            {
                girlWishParticles.transform.position = girlOrigin.position + Vector3.up * .03f;
                girlWishParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                girlWishParticles.Play();
            }

            ResetTriggerClicks();
        }
    }
}
