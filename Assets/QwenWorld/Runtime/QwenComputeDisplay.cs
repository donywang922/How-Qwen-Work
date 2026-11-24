using UdonSharp;
using UnityEngine;

namespace QwenWorld
{
    [UdonBehaviourSyncMode(BehaviourSyncMode.None)]
    public class QwenComputeDisplay : UdonSharpBehaviour
    {
        public QwenExhibitState exhibit;
        public QwenWorldController controller;
        public QwenTextureRunner runner;
        public Renderer coreRenderer, barsRenderer, flowRenderer;
        public int resourceCount;
        public int[] readOffsets, readResources, writeResources;
        private Material coreMaterial, barsMaterial, flowMaterial;
        private Texture2D eventsTexture;
        private Color[] blank;
        private float[] eventTimes;
        private float[] layerExpiry = new float[24];
        private bool initialized, dirty, wasPaused;
        private int previewPass = -1, lastMask = -1;
        private bool lastBusy, lastPaused;
        private float nextPublish, showBlend, busyBlend, motionTime, orbitIdleTime, orbitBusyTime;
        private float nextHeartbeat;
        public void Start()
        {
            Initialize();
        }

        private void Initialize()
        {
            if (initialized)
                return;
            initialized = true;
            if (coreRenderer != null)
                coreMaterial = coreRenderer.material;
            if (barsRenderer != null)
                barsMaterial = barsRenderer.material;
            if (flowRenderer == null || resourceCount == 0)
                return;
            flowMaterial = flowRenderer.material;
            int width = Mathf.NextPowerOfTwo(resourceCount * 2);
            eventsTexture = new Texture2D(width, 1, TextureFormat.RGBAFloat, false, true);
            eventsTexture.filterMode = FilterMode.Point;
            eventsTexture.wrapMode = TextureWrapMode.Clamp;
            blank = new Color[width];
            eventTimes = new float[width];
            for (int i = 0; i < width; i++)
            {
                blank[i] = new Color(-1000, 0, 0, 0);
                eventTimes[i] = -1000;
            }

            eventsTexture.SetPixels(blank);
            eventsTexture.Apply(false, false);
            flowMaterial.SetTexture("_ActivityTex", eventsTexture);
        }

        public void ResetSequence()
        {
            if (!initialized)
                Initialize();
            if (eventsTexture != null)
            {
                eventsTexture.SetPixels(blank);
                for (int i = 0; i < eventTimes.Length; i++)
                    eventTimes[i] = -1000;
                dirty = true;
            }

            for (int i = 0; i < 24; i++)
                layerExpiry[i] = 0;
            wasPaused = false;
            previewPass = -1;
        }

        private void ClearActivity()
        {
            if (eventsTexture == null)
                return;
            eventsTexture.SetPixels(blank);
            for (int i = 0; i < eventTimes.Length; i++)
                eventTimes[i] = -1000;
            dirty = true;
        }

        private void Mark(int eventId, bool sticky)
        {
            if (eventsTexture == null || eventId < 0 || eventId >= resourceCount * 2)
                return;
            eventTimes[eventId] = Time.time;
            eventsTexture.SetPixel(eventId, 0, new Color(Time.time, sticky ? 1 : 0, 0, 1));
            dirty = true;
        }

        private void MarkPass(int pass, bool sticky)
        {
            if (readOffsets == null || pass < 0 || pass >= readOffsets.Length - 1)
                return;
            for (int i = readOffsets[pass]; i < readOffsets[pass + 1]; i++)
                Mark(readResources[i] * 2, sticky);
            if (writeResources != null && pass < writeResources.Length && writeResources[pass] >= 0)
                Mark(writeResources[pass] * 2 + 1, sticky);
        }

        public void RecordStep(int pass)
        {
            if (!initialized)
                Initialize();
            if (wasPaused)
            {
                ClearActivity();
                wasPaused = false;
                previewPass = -1;
            }

            MarkPass(pass, false);
            if (runner.stepLayers != null && pass >= 0 && pass < runner.stepLayers.Length)
            {
                int layer = runner.stepLayers[pass];
                if (layer >= 0 && layer < 24)
                    layerExpiry[layer] = Time.time + .12f;
            }
        }

        public override void PostLateUpdate()
        {
            if (!initialized)
                Initialize();
            if (exhibit == null || controller == null || runner == null)
                return;
            bool local = controller.IsLocalExecutionOwner();
            int mask = 0;
            if (local && runner.running)
            {
                if (runner.paused)
                {
                    int next = runner.passCursor;
                    if (runner.stepLayers != null && next >= 0 && next < runner.stepLayers.Length)
                    {
                        int layer = runner.stepLayers[next];
                        if (layer >= 0 && layer < 24)
                            mask = 1 << layer;
                    }

                    if (!wasPaused || previewPass != next)
                    {
                        ClearActivity();
                        MarkPass(next, true);
                        wasPaused = true;
                        previewPass = next;
                    }
                }
                else
                {
                    if (wasPaused)
                    {
                        ClearActivity();
                        wasPaused = false;
                        previewPass = -1;
                    }

                    for (int layer = 0; layer < 24; layer++)
                        if (Time.time < layerExpiry[layer])
                            mask |= 1 << layer;
                }
            }

            bool busy = local && runner.running, paused = busy && runner.paused;
            if (local && Time.unscaledTime >= nextPublish && (mask != lastMask || busy != lastBusy || paused != lastPaused || Time.unscaledTime >= nextHeartbeat))
            {
                nextPublish = Time.unscaledTime + .1f;
                lastMask = mask;
                lastBusy = busy;
                lastPaused = paused;
                nextHeartbeat = Time.unscaledTime + 1;
                exhibit.PublishCoreLocal(mask, busy, paused);
            }

            if (coreMaterial != null)
                coreMaterial.SetFloat("_LayerMask", local ? mask : exhibit.layerMask);
            showBlend = Mathf.MoveTowards(showBlend, exhibit.texturesShown ? 1 : 0, Time.unscaledDeltaTime * 2.5f);
            busyBlend = Mathf.MoveTowards(busyBlend, exhibit.computing ? 1 : 0, Time.unscaledDeltaTime * 3);
            if (!exhibit.computationPaused)
            {
                motionTime += Time.deltaTime;
                // Integrate both contributions instead of blending two unbounded angles.
                orbitIdleTime += Time.deltaTime * (1 - busyBlend);
                orbitBusyTime += Time.deltaTime * busyBlend;
            }
            if (barsMaterial != null)
            {
                barsMaterial.SetFloat("_ShowBlend", showBlend);
                barsMaterial.SetFloat("_BusyBlend", busyBlend);
                barsMaterial.SetFloat("_MotionTime", motionTime);
                barsMaterial.SetFloat("_OrbitIdleTime", orbitIdleTime);
                barsMaterial.SetFloat("_OrbitBusyTime", orbitBusyTime);
            }

            if (flowRenderer != null)
                flowRenderer.enabled = exhibit.texturesShown && local && runner.supportsInference;
            if (flowMaterial != null)
                flowMaterial.SetFloat("_Clock", Time.time);
            if (dirty && eventsTexture != null)
            {
                eventsTexture.Apply(false, false);
                dirty = false;
            }
        }
    }
}
