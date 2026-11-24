using UdonSharp;
using UnityEngine;
using VRC.SDKBase;
using VRC.SDK3.Rendering;

namespace QwenWorld
{
    // Included only in the SDK test scene. These figures include the client's frame cap.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenExhibitNativeBenchmark : UdonSharpBehaviour
    {
        public QwenWorldController controller;
        public QwenExhibitState exhibit;
        public Transform focusPanel;
        private int stage = -1, count;
        private int[] histogram = new int[251];
        private float started, sum, maximum, previousFrame;
        private string[] names = {"hidden-idle", "shown-idle", "shown-computing", "near-full-pixel-relief", "wish-burst"};
        public void Begin()
        {
            stage = 0;
            Configure();
        }

        private void Configure()
        {
            started = Time.realtimeSinceStartup;
            previousFrame = started;
            sum = maximum = 0;
            count = 0;
            for (int i = 0; i < histogram.Length; i++)
                histogram[i] = 0;
            if (stage == 0)
            {
                controller.StopGeneration();
                controller.ResetConversation();
                exhibit.SetVisibilityLocal(false);
                var p = Networking.LocalPlayer;
                // Test-only camera positioning must survive gravity for small avatars.
                p.SetGravityStrength(0);
                p.SetVelocity(Vector3.zero);
                Vector3 eyeOffset = p.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position - p.GetPosition();
                Vector3 feet = new Vector3(0, 1.65f, -3.2f) - eyeOffset;
                p.TeleportTo(feet, Quaternion.identity);
            }

            if (stage == 1)
                exhibit.SetVisibilityLocal(true);
            if (stage == 2)
            {
                controller.maxNewTokens = 1;
                controller.promptInput.text = "Answer briefly in English: what is two plus three?";
                controller.SendPrompt();
            }

            if (stage == 3)
            {
                controller.StopGeneration();
                if (focusPanel != null && Utilities.IsValid(Networking.LocalPlayer))
                {
                    Vector3 eyeOffset = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position - Networking.LocalPlayer.GetPosition();
                    Vector3 target = focusPanel.position + focusPanel.forward * .5f + focusPanel.right * .20f - eyeOffset;
                    Networking.LocalPlayer.TeleportTo(target, Quaternion.LookRotation(-focusPanel.forward, Vector3.up));
                    Debug.Log("QWEN_EXHIBIT_RELIEF_TARGET panel=" + focusPanel.name + " cameraTarget=" + (target + eyeOffset));
                }
            }

            if (stage == 4)
            {
                Vector3 eyeOffset = Networking.LocalPlayer.GetTrackingData(VRCPlayerApi.TrackingDataType.Head).position - Networking.LocalPlayer.GetPosition();
                Networking.LocalPlayer.TeleportTo(new Vector3(0, 1.65f, -3.2f) - eyeOffset, Quaternion.identity);
                exhibit.SetVisibilityLocal(false);
                exhibit.WishLocal();
            }

            Debug.Log("QWEN_EXHIBIT_BENCH_START " + names[stage]);
        }

        public void Update()
        {
            if (stage < 0)
                return;
            float now = Time.realtimeSinceStartup, delta = now - previousFrame;
            previousFrame = now;
            float elapsed = now - started, warmup = stage == 4 ? .15f : 2, duration = stage == 4 ? 1.6f : 6;
            if (elapsed < warmup)
                return;
            if (elapsed < warmup + duration)
            {
                if (delta <= 0)
                    return;
                count++;
                sum += delta;
                maximum = Mathf.Max(maximum, delta);
                histogram[Mathf.Clamp(Mathf.CeilToInt(delta * 1000), 0, 250)]++;
                return;
            }

            int total = 0, p50 = 0, p95 = 0;
            for (int i = 0; i < histogram.Length; i++)
            {
                total += histogram[i];
                if (p50 == 0 && total >= count * .5f)
                    p50 = i;
                if (total >= count * .95f)
                {
                    p95 = i;
                    break;
                }
            }

            Debug.Log("QWEN_EXHIBIT_FRAME_REPORT {\"mode\":\"" + names[stage] + "\",\"samples\":" + count + ",\"meanFrameMs\":" + (sum / Mathf.Max(1, count) * 1000).ToString("F3") + ",\"p50FrameMs\":" + p50 + ",\"p95FrameMs\":" + p95 + ",\"maxFrameMs\":" + (maximum * 1000).ToString("F3") + ",\"width\":" + VRCCameraSettings.ScreenCamera.PixelWidth + ",\"height\":" + VRCCameraSettings.ScreenCamera.PixelHeight + ",\"vr\":" + (Networking.LocalPlayer.IsUserInVR() ? "true" : "false") + "}");
            stage++;
            if (stage == names.Length)
            {
                stage = -1;
                Networking.LocalPlayer.SetGravityStrength(1);
                Networking.LocalPlayer.TeleportTo(new Vector3(0, .03f, -3.8f), Quaternion.identity);
                Debug.Log("QWEN_EXHIBIT_BENCH_DONE");
                return;
            }

            Configure();
        }
    }
}
