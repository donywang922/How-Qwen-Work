using UdonSharp;
using UnityEngine;
using VRC.SDK3.Rendering;
using VRC.SDKBase;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenTextureRunner : UdonSharpBehaviour
    {
        public bool supportsInference = true;
        public Material[] steps;
        public int[] stepLayers;
        public RenderTexture[] buffers;
        public Material controlMaterial;
        public Material clearMaterial;
        [HideInInspector]
        public Renderer outputUniforms, tokenUniforms;
        public QwenWorldController controller;
        public QwenComputeDisplay computeDisplay;
        public int contextLength = 512;
        public int vocabularySize = 248320;
        public int passesPerFrame = 32;
        [HideInInspector]
        public bool presentationMode;
        public float presentationOperatorsPerSecond = 90;
        [HideInInspector]
        public bool running;
        [HideInInspector]
        public bool paused;
        [HideInInspector]
        public int outputToken;
        [HideInInspector]
        public int outputPosition;
        [HideInInspector]
        public int position;
        [HideInInspector]
        public int passCursor;
        [HideInInspector]
        public int generatedCount;
        private int[] promptTokens;
        private int maxNewTokens;
        private int epoch;
        private int stepMode;
        private int layerAtStepStart;
        private bool pendingReadback;
        private RenderTexture output;
        private Material outputMaterial, tokenMaterial;
        private float[] readback;
        private float presentationBudget;
        public int GetPromptTokenCount() { return promptTokens == null ? 0 : promptTokens.Length; }
        public int GetProgressLayer()
        {
            if (stepLayers == null || stepLayers.Length == 0) return 0;
            int layer = stepLayers[Mathf.Clamp(passCursor,0,stepLayers.Length - 1)];
            return layer >= 0 && layer < 24 ? layer + 1 : 0;
        }
        public void SetPresentationMode(bool visible)
        {
            if (presentationMode == visible)
                return;
            presentationMode = visible;
            presentationBudget = 0;
        }

        public void Begin(int[] tokens, int tokenLimit)
        {
            StopRun();
            if (!supportsInference || steps == null || steps.Length == 0)
            {
                controller.OnEngineError("This client has no inference model.");
                return;
            }

            if (tokens == null || tokens.Length == 0 || tokens.Length >= contextLength)
            {
                controller.OnEngineError("Prompt is empty or exceeds context capacity.");
                return;
            }

            epoch++;
            // Only these two materials receive sequence-specific uniforms. Keep imported assets immutable.
            if (outputMaterial == null)
                outputMaterial = outputUniforms.material;
            if (tokenMaterial == null)
                tokenMaterial = tokenUniforms.material;
            output = (RenderTexture)outputMaterial.GetTexture("TexO");
            int readbackLength = output.width * output.height * 4;
            if (readback == null || readback.Length != readbackLength)
                readback = new float[readbackLength];
            for (int i = 0; i < buffers.Length; i++)
            {
                if (buffers[i] != null)
                    VRCGraphics.Blit(null, buffers[i], clearMaterial, 0);
            }

            promptTokens = tokens;
            maxNewTokens = Mathf.Min(tokenLimit, contextLength - tokens.Length);
            position = 0;
            passCursor = 0;
            generatedCount = 0;
            pendingReadback = false;
            stepMode = 0;
            paused = false;
            running = true;
            presentationBudget = 0;
        }

        public void StopRun()
        {
            running = false;
            paused = false;
            pendingReadback = false;
            epoch++; // readback contains the sequence epoch; stale callbacks are ignored.
            if (computeDisplay != null)
                computeDisplay.ResetSequence();
        }

        public void PauseOrResume()
        {
            if (running)
            {
                paused = !paused;
                stepMode = 0;
                presentationBudget = 0;
            }
        }

        public void StepOperator()
        {
            if (running)
            {
                stepMode = 1;
                paused = false;
            }
        }

        public void StepToken()
        {
            if (running)
            {
                stepMode = 2;
                paused = false;
            }
        }

        public void StepLayer()
        {
            if (!running)
                return;
            stepMode = 3;
            layerAtStepStart = stepLayers[Mathf.Clamp(passCursor, 0, stepLayers.Length - 1)];
            paused = false;
        }

        public void Update()
        {
            if (!running || paused || pendingReadback)
                return;
            int allowance = stepMode == 1 ? 1 : Mathf.Max(1, passesPerFrame);
            if (presentationMode && stepMode != 1)
            {
                // Bound accumulated work after stalls; fixed showcase speed is independent of FPS.
                presentationBudget = Mathf.Min(4, presentationBudget + Time.deltaTime * presentationOperatorsPerSecond);
                allowance = Mathf.FloorToInt(presentationBudget);
                presentationBudget -= allowance;
            }

            for (int count = 0; count < allowance; count++)
            {
                if (!running || paused || pendingReadback)
                    break;
                if (position >= contextLength || generatedCount >= maxNewTokens)
                {
                    running = false;
                    controller.OnEngineFinished();
                    break;
                }

                if (passCursor == 0)
                {
                    int inputToken = position < promptTokens.Length ? promptTokens[position] : outputToken;
                    tokenMaterial.SetFloat("_Token", inputToken);
                    tokenMaterial.SetFloat("_Position", position);
                    tokenMaterial.SetFloat("_Epoch", epoch);
                    VRCGraphics.Blit(null, output, tokenMaterial, 0);
                    outputMaterial.SetVector("VecMul", new Vector4(1, 0, 0, 0));
                    outputMaterial.SetVector("VecAdd", new Vector4(0, position, 0, epoch));
                }

                Material material = passCursor == steps.Length - 1 ? outputMaterial : steps[passCursor];
                RenderTexture target = (RenderTexture)material.GetTexture("TexO");
                VRCGraphics.Blit(null, target, material, -1);
                if (computeDisplay != null)
                    computeDisplay.RecordStep(passCursor);
                passCursor++;
                if (stepMode == 1)
                    paused = true;
                if (passCursor == steps.Length)
                {
                    passCursor = 0;
                    int completedPosition = position++;
                    if (completedPosition >= promptTokens.Length - 1)
                    {
                        pendingReadback = true;
                        VRCAsyncGPUReadback.Request(output, 0, TextureFormat.RGBAFloat, this);
                    }

                    if (stepMode == 2 || stepMode == 3)
                        paused = true;
                }
                else if (stepMode == 3 && stepLayers[passCursor] != layerAtStepStart)
                    paused = true;
            }
        }

        public override void OnAsyncGpuReadbackComplete(VRCAsyncGPUReadbackRequest request)
        {
            if (!running || !pendingReadback)
                return;
            if (request.hasError || !request.TryGetData(readback))
            {
                StopRun();
                controller.OnEngineError("GPU readback failed.");
                return;
            }

            if (Mathf.RoundToInt(readback[3]) != epoch)
                return;
            int tokenPosition = Mathf.RoundToInt(readback[1]);
            if (tokenPosition != position - 1)
                return;
            pendingReadback = false;
            outputToken = Mathf.RoundToInt(readback[0]);
            outputPosition = tokenPosition;
            if (outputToken < 0 || outputToken >= vocabularySize)
            {
                StopRun();
                controller.OnEngineError("GPU returned an invalid token ID.");
                return;
            }

            generatedCount++;
            controller.OnOutputToken();
        }

        public void OnDestroy()
        {
            if (outputMaterial != null)
                Destroy(outputMaterial);
            if (tokenMaterial != null)
                Destroy(tokenMaterial);
        }
    }
}
