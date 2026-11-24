using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

namespace QwenWorld
{
    // Added only to the SDK validation scene, never to either production PC scene.
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenClientSelfTest : UdonSharpBehaviour
    {
        public QwenWorldController controller;
        public QwenExhibitNativeBenchmark benchmark;
        public QwenObservationPlatform observation;
        public string[] languages, questions;
        public int[] offsets, expected;
        private int caseIndex, cursor, lastCount, phase, frames, stepPosition;
        private float started;
        public void Start()
        {
            SendCustomEventDelayedSeconds(nameof(ApproachConsole), .5f);
        }

        public void ApproachConsole()
        {
            if (!Utilities.IsValid(Networking.LocalPlayer))
            {
                SendCustomEventDelayedSeconds(nameof(ApproachConsole), .5f);
                return;
            }
            Vector3 spawn = Networking.LocalPlayer.GetPosition();
            float spawnRadius = new Vector2(spawn.x, spawn.z).magnitude;
            if (spawnRadius < 11.2f || spawnRadius > 12.5f)
            {
                Fail("Outer ground spawn " + spawn);
                return;
            }
            Networking.LocalPlayer.TeleportTo(new Vector3(0, .03f, -2.6f), Quaternion.identity);
            SendCustomEventDelayedSeconds(nameof(RunCase), 2);
        }

        public void RunCase()
        {
            controller.ResetConversation();
            controller.promptInput.text = questions[caseIndex];
            controller.maxNewTokens = offsets[caseIndex + 1] - offsets[caseIndex];
            cursor = offsets[caseIndex];
            lastCount = 0;
            started = Time.realtimeSinceStartup;
            controller.SendPrompt();
            if (!controller.runner.running)
            {
                Fail("Runner did not start");
                return;
            }

            phase = 1;
            if (caseIndex == 0)
            {
                controller.runner.PauseOrResume();
                phase = 2;
                frames = 0;
            }

            Debug.Log("QWEN_NATIVE_CASE_START " + languages[caseIndex]);
        }

        public void Update()
        {
            if (phase == 0)
                return;
            QwenTextureRunner runner = controller.runner;
            if (Time.realtimeSinceStartup - started > 90)
            {
                Fail("Timeout");
                return;
            }

            if (phase >= 6)
            {
                CheckObservation();
                return;
            }

            if (phase == 2)
            {
                if (++frames < 5)
                    return;
                if (!runner.paused || runner.passCursor != 0)
                {
                    Fail("Pause");
                    return;
                }

                runner.StepOperator();
                phase = 3;
                return;
            }

            if (phase == 3)
            {
                if (!runner.paused)
                    return;
                if (runner.passCursor != 1)
                {
                    Fail("Single operator");
                    return;
                }

                runner.StepLayer();
                phase = 4;
                return;
            }

            if (phase == 4)
            {
                if (!runner.paused)
                    return;
                if (runner.passCursor <= 1)
                {
                    Fail("Single layer");
                    return;
                }

                stepPosition = runner.position;
                runner.StepToken();
                phase = 5;
                return;
            }

            if (phase == 5)
            {
                if (!runner.paused)
                    return;
                if (runner.position != stepPosition + 1 || runner.passCursor != 0)
                {
                    Fail("Single token");
                    return;
                }

                Debug.Log("QWEN_NATIVE_CONTROLS_PASS");
                runner.PauseOrResume();
                phase = 1;
                return;
            }

            if (runner.generatedCount != lastCount)
            {
                if (runner.generatedCount != lastCount + 1 || cursor >= offsets[caseIndex + 1] || runner.outputToken != expected[cursor])
                {
                    Fail("Token mismatch " + runner.generatedCount + " actual " + runner.outputToken);
                    return;
                }

                lastCount = runner.generatedCount;
                cursor++;
            }

            if (!runner.running && lastCount > 0)
            {
                if (cursor != offsets[caseIndex + 1])
                {
                    Fail("Early stop");
                    return;
                }

                Debug.Log("QWEN_NATIVE_CASE_PASS " + languages[caseIndex] + " tokens=" + lastCount + " seconds=" + (Time.realtimeSinceStartup - started));
                phase = 0;
                if (++caseIndex < 4)
                    SendCustomEventDelayedSeconds(nameof(RunCase), 1);
                else
                {
                    Debug.Log("QWEN_NATIVE_VALIDATION_PASS four languages, exact token sequences, pause and single stepping");
                    Networking.LocalPlayer.TeleportTo(new Vector3(9, .03f, 0), Quaternion.identity);
                    observation.heightSlider.value = 2.2f;
                    started = Time.realtimeSinceStartup;
                    phase = 6;
                }
            }
        }

        private void CheckObservation()
        {
            var player = Networking.LocalPlayer;
            float elapsed = Time.realtimeSinceStartup - started;
            if (phase == 6)
            {
                if (observation.deck.position.y < 2.18f) return;
                if (player.GetPosition().y < 2.1f || !observation.deckCollider.enabled || !observation.deckRenderer.enabled)
                { Fail("Observation rise " + player.GetPosition()); return; }
                observation.heightSlider.value = .9f;
                phase = 7;
                return;
            }
            if (phase == 7)
            {
                if (observation.deck.position.y > .92f) return;
                if (Mathf.Abs(player.GetPosition().y - .9f) > .2f)
                { Fail("Observation descent " + player.GetPosition()); return; }
                player.TeleportTo(new Vector3(11.8f, .3f, 0), Quaternion.identity);
                phase = 8;
                started = Time.realtimeSinceStartup;
                return;
            }
            if (phase == 8 && elapsed > .4f)
            {
                if (observation.deckCollider.enabled || observation.deckRenderer.enabled || observation.heightSlider.value != 0)
                { Fail("Observation fall reset"); return; }
                player.TeleportTo(new Vector3(14, -3, 0), Quaternion.identity);
                phase = 9;
                started = Time.realtimeSinceStartup;
                return;
            }
            if (phase == 9 && elapsed > .4f)
            {
                Vector3 feet = player.GetPosition();
                float radius = new Vector2(feet.x, feet.z).magnitude;
                if (feet.y < -.4f || radius < 11.2f || radius > 12.5f)
                { Fail("Observation void recovery " + feet); return; }
                Debug.Log("QWEN_NATIVE_OBSERVATION_PASS outer spawn, rise, descent, fall reset, hidden lowest, void recovery");
                player.TeleportTo(new Vector3(0, .03f, -2.6f),Quaternion.identity);
                phase = 10;
                started = Time.realtimeSinceStartup;
                return;
            }
            if (phase == 10 && elapsed > 2)
            {
                if (!controller.exhibit.IsLocalHolder())
                { Fail("Reacquire keyboard after observation"); return; }
                phase = 0;
                if (benchmark != null) benchmark.Begin();
            }
        }

        private void Fail(string reason)
        {
            phase = 0;
            controller.StopGeneration();
            Debug.LogError("QWEN_NATIVE_VALIDATION_FAIL " + reason);
        }
    }
}
