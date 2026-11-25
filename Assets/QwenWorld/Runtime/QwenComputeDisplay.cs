using UdonSharp;
using UnityEngine;
using VRC.SDKBase;

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
        private bool[] previewEvents;
        private float[] layerExpiry = new float[24];
        private bool initialized, dirty, wasPaused;
        private int previewPass = -1, lastMask = -1;
        private bool lastBusy, lastPaused, lastDetailed;
        private float nextPublish, showBlend, busyBlend, motionTime, orbitIdleTime, orbitBusyTime;
        private float nextHeartbeat;
        private float hiddenAnimationClock;
        private int lastStateOwner = -1;
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
            previewEvents = new bool[width];
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
                {
                    eventTimes[i] = -1000;
                    previewEvents[i] = false;
                }
                dirty = true;
            }

            for (int i = 0; i < 24; i++)
                layerExpiry[i] = 0;
            wasPaused = false;
            previewPass = -1;
        }

        private void ReleasePreview()
        {
            if (eventsTexture == null)
                return;
            for (int i = 0; i < eventTimes.Length; i++)
                if(previewEvents[i])
                {
                    previewEvents[i]=false;
                    eventTimes[i]=Time.time;
                    eventsTexture.SetPixel(i,0,new Color(eventTimes[i],0,0,1));
                    dirty=true;
                }
        }

        private void Mark(int eventId, bool sticky)
        {
            if (eventsTexture == null || eventId < 0 || eventId >= resourceCount * 2)
                return;
            if(sticky) previewEvents[eventId]=true;
            else eventTimes[eventId] = Time.time;
            eventsTexture.SetPixel(eventId, 0, new Color(eventTimes[eventId], previewEvents[eventId] ? 1 : 0, 0, 1));
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
                ReleasePreview();
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

        private void PreviewPausedPass(int pass)
        {
            if(wasPaused&&previewPass==pass) return;
            // Unpin the previous preview into a timed pulse; retain its decay while
            // pinning the next operator. Shared read/write edges remain fully previewed.
            ReleasePreview();
            MarkPass(pass,true);
            wasPaused=true;
            previewPass=pass;
        }

        public override void PostLateUpdate()
        {
            if (!initialized)
                Initialize();
            if (exhibit == null || controller == null || runner == null)
                return;
            bool local = controller.IsLocalExecutionOwner();
            if (!local) lastMask = -1;
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

                    PreviewPausedPass(next);
                }
                else
                {
                    if (wasPaused)
                    {
                        ReleasePreview();
                        wasPaused = false;
                        previewPass = -1;
                    }

                    for (int layer = 0; layer < 24; layer++)
                        if (Time.time < layerExpiry[layer])
                            mask |= 1 << layer;
                }
            }

            else if(wasPaused)
            {
                ReleasePreview(); wasPaused=false; previewPass=-1;
            }

            bool busy = local && runner.running, paused = busy && runner.paused;
            bool detailed = exhibit.texturesShown;
            int publishedMask = detailed ? mask : 0;
            var stateOwner = Networking.GetOwner(exhibit.gameObject);
            int stateOwnerId = Utilities.IsValid(stateOwner) ? stateOwner.playerId : -1;
            bool stateChanged = lastMask < 0 || busy != lastBusy || paused != lastPaused || detailed != lastDetailed || stateOwnerId != lastStateOwner;
            bool layerChanged = detailed && (publishedMask != lastMask || Time.unscaledTime >= nextHeartbeat);
            if (local && (stateChanged || (Time.unscaledTime >= nextPublish && layerChanged)))
            {
                nextPublish = Time.unscaledTime + .1f;
                lastMask = publishedMask;
                lastBusy = busy;
                lastPaused = paused;
                lastDetailed = detailed;
                lastStateOwner = stateOwnerId;
                nextHeartbeat = Time.unscaledTime + 1;
                exhibit.PublishCoreLocal(publishedMask, busy, paused);
            }

            // Hidden textures use a local, deliberately readable illustration of activity.
            // Its phase is not inference progress and does not generate network updates.
            bool displayBusy = local ? busy : exhibit.computing;
            bool displayPaused = local ? paused : exhibit.computationPaused;
            if (detailed || !displayBusy) hiddenAnimationClock = 0;
            else if (!displayPaused) hiddenAnimationClock += Time.unscaledDeltaTime;
            int displayMask = detailed ? (local ? mask : exhibit.layerMask) : 0;
            if (!detailed && displayBusy)
            {
                int head = Mathf.FloorToInt(hiddenAnimationClock / .1f) % 24;
                displayMask = (1 << head) | (1 << ((head + 23) % 24));
            }
            if (coreMaterial != null)
                coreMaterial.SetFloat("_LayerMask", displayMask);
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
