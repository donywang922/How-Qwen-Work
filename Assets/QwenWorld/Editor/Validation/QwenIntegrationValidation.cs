using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UdonSharpEditor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.ClientSim;
using VRC.SDK3.Network;
using VRC.SDK3.UdonNetworkCalling;
using VRC.SDKBase;
using VRC.Udon;

namespace QwenWorld.Editor
{
    [InitializeOnLoad]
    public static class QwenIntegrationValidation
    {
        const string Key = "QwenFullValidationPhase";
        static double since, rateSince;
        static int beforePass, ratePass, lastGenerated, firstReferenceToken, secondReferenceToken;
        static bool pausedMotionCaptured, approachedConsole;
        static float pausedIdleClock, pausedBusyClock;
        static UdonBehaviour state, menu, social, engine, world, visual;
        static QwenExhibitState stateProxy;
        static QwenLocalMenu menuProxy;
        static QwenSocialUI socialProxy;
        static QwenWorldController controller;
        static QwenComputeDisplay display;
        static VRCPlayerApi remote;
        static Report report;
        static QwenIntegrationValidation()
        {
            EditorApplication.update += Tick;
        }

        public static void InstallAndValidateBatch()
        {
            QwenSceneInstaller.Begin(BeginValidation);
        }

        public static void ValidateBatch()
        {
            QwenModelPipeline.Compile();
            // Finish the SDK's delayed proxy setup before replacing scenes.
            EditorApplication.delayCall += BeginValidation;
        }

        static void Require(bool ok, string reason)
        {
            if (!ok)
                throw new InvalidOperationException(reason);
        }

        static bool B(UdonBehaviour u, string key) => (bool)u.GetProgramVariable(key);
        static int I(UdonBehaviour u, string key) => (int)u.GetProgramVariable(key);
        static void Next(string phase)
        {
            SessionState.SetString(Key, phase);
            since = EditorApplication.timeSinceStartup;
        }

        static async void BeginValidation()
        {
            try
            {
                approachedConsole = false;
                Directory.CreateDirectory(QwenSceneInstaller.Results);
                if (File.Exists(QwenSceneInstaller.Results + "/failure.txt")) File.Delete(QwenSceneInstaller.Results + "/failure.txt");
                EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                var checks = new List<SceneReport>();
                string viewer = QwenPlatformBuild.CreateViewerScene();
                await NextEditorFrame();
                foreach (string path in new[]{QwenProject.MainScene, viewer})
                {
                    string name = Path.GetFileNameWithoutExtension(path);
                    EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
                    var d = UnityEngine.Object.FindObjectOfType<QwenComputeDisplay>();
                    var s = UnityEngine.Object.FindObjectOfType<QwenSocialUI>();
                    Require(d != null && s != null && s.panels.Length == 80, "Missing full exhibit in " + name);
                    var core = d.coreRenderer.GetComponent<MeshFilter>().sharedMesh;
                    Require(core.uv.Select(uv => Mathf.RoundToInt(uv.x)).Distinct().Count() == 24, "Core ring IDs are not 24 layers");
                    Require((d.coreRenderer.transform.position - QwenPanelPlacement.CoreCenter).sqrMagnitude < .000001f, "Core anchor changed without layout rebuild");
                    Require(GameObject.Find("Qwen Laboratory interface") == null, "Legacy interface remains");
                    var floor = GameObject.Find("Floor");
                    Require(floor != null && floor.GetComponent<MeshCollider>() != null && floor.GetComponent<CapsuleCollider>() == null, "Incorrect floor collision");
                    int overlaps = 0;
                    for (int i = 0; i < s.panels.Length; i++)
                        for (int j = i + 1; j < s.panels.Length; j++)
                            if (PanelOverlap(s.panels[i], s.panels[j]))
                                overlaps++;
                    Require(overlaps == 0, "Read-only panel slots overlap");
                    var dependencies = AssetDatabase.GetDependencies(path, true);
                    bool android = name.EndsWith("Android");
                    if (android)
                        Require(dependencies.All(p => !p.Contains("/Models/")) && d.flowRenderer == null && d.resourceCount == 0, "Viewer contains model/flow assets");
                    else
                        Require(d.resourceCount == 573 && d.readOffsets.Length == 850 && d.writeResources.Length == 849, "Invalid graph metadata");
                    checks.Add(new SceneReport{name = name, chatSlots = s.panels.Length, chatOverlaps = overlaps, coreTriangles = core.triangles.Length / 3, flowResources = d.resourceCount, modelFreeViewer = android});
                    await NextEditorFrame();
                }

                File.WriteAllText(QwenSceneInstaller.Results + "/scene-validation.json", JsonUtility.ToJson(new SceneReports{scenes = checks.ToArray()}, true));
                report = new Report{scope = "Real Udon + GPU dispatch in ClientSim; sender contexts and virtual players are synthetic, not multiple VRChat clients."};
                EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                ValidateGestureInput();
                QwenConsoleVisuals.CaptureScreenPreviews(QwenProject.DataPath("validation/glass-console"));
                Next("enter");
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        static Task NextEditorFrame()
        {
            var resumed = new TaskCompletionSource<bool>();
            EditorApplication.delayCall += () => resumed.SetResult(true);
            return resumed.Task;
        }

        static void ValidateGestureInput()
        {
            var input = UnityEngine.Object.FindObjectOfType<QwenLocalMenu>();
            Require(input != null, "Input controller missing");
            Require(!input.RegisterTriggerClick(true, 0, 1), "First click opened menu");
            Require(!input.RegisterTriggerClick(true, 0, 1.1f), "Held trigger counted twice");
            input.RegisterTriggerClick(false, 0, 1.11f);
            Require(!input.RegisterTriggerClick(true, 1, 1.12f), "Opposite hands combined");
            Require(input.RegisterTriggerClick(true, 0, 1.2f), "Same-hand double click failed");
            input.RegisterTriggerClick(false, 1, 1.21f);
            Require(!input.RegisterTriggerClick(true, 1, 1.8f), "Slow clicks combined");
            Require(input.ClassifyHandPose(.2f, .2f, .2f, .2f) == 1, "Fist classification failed");
            Require(input.ClassifyHandPose(.9f, .9f, .9f, .9f) == 2, "Relaxed hand classification failed");
            Require(input.ClassifyHandPose(.9f, .2f, .2f, .2f) == 0, "Pointing hand accepted as default");
            Require(input.ClassifyHandPose(-10, .2f, .2f, .2f) == 0, "Missing bone accepted");
            Require(!input.AdvanceWishHold(true, 10), "Wish fired immediately");
            Require(!input.AdvanceWishHold(true, 10.4f), "Wish hold too short");
            Require(input.AdvanceWishHold(true, 10.5f), "Held wish did not trigger");
            Require(!input.AdvanceWishHold(true, 20), "Held wish repeated");
            input.AdvanceWishHold(false, 20.1f);
            input.AdvanceWishHold(false, 20.4f);
            Require(!input.AdvanceWishHold(true, 21), "Rearmed wish skipped hold");
            Require(input.AdvanceWishHold(true, 21.5f), "Released wish did not rearm");
        }

        static bool PanelOverlap(Transform a, Transform b)
        {
            Vector3[] x = {a.right, a.up, a.forward}, y = {b.right, b.up, b.forward};
            var ar = ((RectTransform)a).rect;
            var br = ((RectTransform)b).rect;
            Vector3 half = new Vector3(ar.width * a.lossyScale.x * .5f, ar.height * a.lossyScale.y * .5f, .002f), otherHalf = new Vector3(br.width * b.lossyScale.x * .5f, br.height * b.lossyScale.y * .5f, .002f), delta = b.position - a.position;
            var axes = new List<Vector3>(x.Concat(y));
            foreach (var p in x)
                foreach (var q in y)
                    axes.Add(Vector3.Cross(p, q));
            foreach (var axis in axes)
            {
                if (axis.sqrMagnitude < .0000001f)
                    continue;
                var n = axis.normalized;
                float r0 = 0, r1 = 0;
                for (int k = 0; k < 3; k++)
                {
                    r0 += half[k] * Mathf.Abs(Vector3.Dot(n, x[k]));
                    r1 += otherHalf[k] * Mathf.Abs(Vector3.Dot(n, y[k]));
                }

                if (Mathf.Abs(Vector3.Dot(delta, n)) > r0 + r1 + .001f)
                    return false;
            }

            return true;
        }

        static void Rpc(VRCPlayerApi sender, string name, params (string, object)[] args)
        {
            var metadata = state.GetNetworkCallingMetadata(name);
            for (int i = 0; i < args.Length; i++)
                args[i] = (metadata.Parameters[i].Name, args[i].Item2);
            Action action = () =>
            {
                if (args.Length == 0)
                    state.RunEventAdvanced(name, canRunBeforeStart: true);
                else if (args.Length == 1)
                    state.RunEventAdvanced<object>(name, mangleParameterNames: false, canRunBeforeStart: true, args[0]);
                else if (args.Length == 3)
                    state.RunEventAdvanced<object, object, object>(name, mangleParameterNames: false, canRunBeforeStart: true, args[0], args[1], args[2]);
            };
            var context = typeof(NetworkCalling).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Single(m => m.Name == "WithNetworkCallingContext" && m.GetParameters().Length == 2);
            context.Invoke(null, new object[]{sender, action});
        }

        static void Tick()
        {
            string phase = SessionState.GetString(Key, "");
            if (phase == "")
                return;
            try
            {
                double now = EditorApplication.timeSinceStartup;
                if (phase == "enter" || phase == "viewer-enter")
                {
                    if (Environment.StackTrace.Contains("VRC.Editor.EnvConfig") || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    {
                        since = now;
                        return;
                    }

                    if (now - since < 4)
                        return;
                    Next(phase == "enter" ? "ready" : "viewer");
                    EditorApplication.isPlaying = true;
                    return;
                }

                if (phase == "ready" && EditorApplication.isPlaying)
                {
                    controller = UnityEngine.Object.FindObjectOfType<QwenWorldController>();
                    stateProxy = UnityEngine.Object.FindObjectOfType<QwenExhibitState>();
                    menuProxy = UnityEngine.Object.FindObjectOfType<QwenLocalMenu>();
                    socialProxy = UnityEngine.Object.FindObjectOfType<QwenSocialUI>();
                    display = UnityEngine.Object.FindObjectOfType<QwenComputeDisplay>();
                    if (controller == null || stateProxy == null || !Utilities.IsValid(Networking.LocalPlayer))
                        return;
                    var tokenizer = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.tokenizer);
                    menu = UdonSharpEditorUtility.GetBackingUdonBehaviour(menuProxy);
                    if ((int)tokenizer.GetProgramVariable("vocab_size") <= 0 || !B(menu, "initialized"))
                        return;
                    if (report == null)
                        report = new Report{scope = "Real Udon + GPU dispatch in ClientSim; sender contexts and virtual players are synthetic, not multiple VRChat clients."};
                    state = UdonSharpEditorUtility.GetBackingUdonBehaviour(stateProxy);
                    social = UdonSharpEditorUtility.GetBackingUdonBehaviour(socialProxy);
                    engine = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller.runner);
                    world = UdonSharpEditorUtility.GetBackingUdonBehaviour(controller);
                    visual = UdonSharpEditorUtility.GetBackingUdonBehaviour(display);
                    Require(!B(state, "texturesShown") && !B(menu, "texturesVisible"), "Global default visibility incorrect");
                    if (!approachedConsole)
                    {
                        Vector3 spawn = Networking.LocalPlayer.GetPosition();
                        float distance = new Vector2(spawn.x, spawn.z).magnitude;
                        Require(distance > 11.2f && distance < 12.5f, "Spawn is not on the outer transparent step");
                        report.outerGroundSpawn = true;
                        Networking.LocalPlayer.TeleportTo(new Vector3(0, .03f, -2.6f), Quaternion.identity);
                        approachedConsole = true;
                        return;
                    }
                    if (I(state, "keyboardHolder") != Networking.LocalPlayer.playerId)
                    {
                        Require(now - since < 6, "Nearby PC did not automatically acquire the keyboard");
                        return;
                    }
                    Require(socialProxy.inputRoot.activeSelf && !controller.promptInput.isFocused, "Input must expand without stealing focus");
                    ClientSimMain.SpawnRemotePlayer("Remote requester");
                    remote = VRCPlayerApi.GetPlayerById(2);
                    var remoteObject = ClientPlayers().First(p => p.Player == remote);
                    remoteObject.transform.position = new Vector3(1.4f, .03f, -1.4f);
                    Rpc(remote, "_ClaimKeyboard", ("inferenceCapable", true));
                    Require(I(state, "keyboardHolder") == Networking.LocalPlayer.playerId, "Closer player displaced a nearby holder");
                    ClientSimMain.SpawnRemotePlayer("Viewer then PC candidate");
                    var third = VRCPlayerApi.GetPlayerById(3);
                    var thirdObject = ClientPlayers().First(p => p.Player == third);
                    thirdObject.transform.position = new Vector3(.4f, .03f, -.4f);
                    Rpc(third, "_ClaimKeyboard", ("inferenceCapable", false));
                    Networking.LocalPlayer.TeleportTo(new Vector3(0, .03f, -5), Quaternion.identity);
                    state.SendCustomEvent("RefreshKeyboardAssignment");
                    Require(I(state, "keyboardHolder") == remote.playerId, "Distance handover failed or viewer received keyboard");
                    world.SendCustomEvent("SendPrompt");
                    Require(!B(engine, "running"), "Non-holder started inference");
                    Rpc(third, "_ClaimKeyboard", ("inferenceCapable", true));
                    Require(I(state, "keyboardHolder") == remote.playerId, "New closer PC displaced current holder");
                    remoteObject.transform.position = new Vector3(0, .03f, 5);
                    state.SendCustomEvent("RefreshKeyboardAssignment");
                    Require(I(state, "keyboardHolder") == third.playerId, "Nearest registered PC was not selected");
                    Networking.LocalPlayer.TeleportTo(new Vector3(2.6f, .03f, 0), Quaternion.LookRotation(Vector3.left));
                    thirdObject.transform.position = new Vector3(0, .03f, 5);
                    state.SendCustomEvent("RefreshKeyboardAssignment");
                    Require(I(state, "keyboardHolder") == Networking.LocalPlayer.playerId, "Automatic return handover failed");
                    remoteObject.transform.position = new Vector3(1.4f, .03f, -1.4f);
                    Rpc(remote, "_SetVisibility", ("value", true));
                    Require(B(state, "texturesShown") && B(menu, "texturesVisible") && B(engine, "presentationMode"), "Remote global visibility failed");
                    int mask = I(state, "layerMask");
                    Rpc(remote, "_Core", ("mask", 16777215), ("busy", true), ("paused", false));
                    Require(I(state, "layerMask") == mask, "Non-executor published core state");
                    Rpc(remote, "_SetVisibility", ("value", false));
                    Rpc(remote, "_Wish");
                    Require(B(state, "texturesShown") && menuProxy.girlWishParticles.isPlaying && menuProxy.playerWishParticles.isPlaying, "Global wish failed");
                    Require((menuProxy.playerWishParticles.transform.position - (remote.GetPosition() + Vector3.up * .03f)).sqrMagnitude < .001f, "Wish burst uses wrong player's feet");
                    Rpc(remote, "_Wish");
                    Require(B(state, "texturesShown"), "Wish toggled mode off");
                    Rpc(remote, "_SetVisibility", ("value", false));
                    report.automaticKeyboardHandover = true;
                    report.retainsNearbyHolder = true;
                    report.nearestPcSelection = true;
                    report.permissions = true;
                    report.globalVisibility = true;
                    report.globalWish = true;
                    report.coreSenderValidation = true;
                    Next("keyboard-track");
                    return;
                }

                if (phase == "keyboard-track")
                {
                    if (now - since < .4) return;
                    Require(Mathf.Abs(Mathf.DeltaAngle(socialProxy.keyboardPivot.eulerAngles.y, 90)) < 2, "Keyboard tracking is too slow");
                    Require(socialProxy.inputRoot.transform.parent == socialProxy.keyboardPivot && socialProxy.inputRoot.transform.position.y > 1.2f,
                        "Input is not above the keyboard");
                    report.keyboardTracking = true;
                    report.inputAboveKeyboard = true;
                    string longText = string.Concat(Enumerable.Repeat("中文 English 日本語 한국어。这是一段用于检查自动换行和长消息裁切的文本。\n", 60));
                    world.SetProgramVariable("sharedHistory", "<|im_start|>assistant\n" + longText + "<|im_end|>\n");
                    world.RunProgram("_onDeserialization");
                    Next("console-content");
                    return;
                }

                if (phase == "console-content")
                {
                    if (now - since < 2.5) return;
                    var slots = (int[])social.GetProgramVariable("slotPlayerIds");
                    int slot = Array.IndexOf(slots, Networking.LocalPlayer.playerId);
                    Require(slot >= 0, "Local message slot missing");
                    var content = socialProxy.panelContent[slot];
                    var viewport = (RectTransform)content.parent;
                    Require(Mathf.Abs(content.rect.width - viewport.rect.width) < .01f, "Message width exceeds viewport");
                    Require(content.rect.height > viewport.rect.height && content.anchoredPosition.y > 0, "Long message does not scroll to the end");
                    Capture("hidden-idle");
                    report.longMessageClipping = true;
                    Capture("console-long-message", new Vector3(2.8f, 1.65f, .2f), new Vector3(.8f, 1.65f, 0), 55);
                    world.SetProgramVariable("maxNewTokens", 2);
                    var reference = QwenWorldReference.Load()[0];
                    Require(reference.generated_ids.Length >= 2,"Integration reference needs two output tokens");
                    firstReferenceToken = reference.generated_ids[0]; secondReferenceToken = reference.generated_ids[1];
                    controller.promptInput.text = reference.question;
                    world.SendCustomEvent("ResetConversation");
                    social.SendCustomEvent("SendQuestion");
                    Require(B(engine, "running"), "Private input did not start real GPU inference");
                    lastGenerated = 0;
                    Next("first-token");
                    return;
                }

                if (phase == "first-token")
                {
                    if (I(engine, "generatedCount") == 0)
                    {
                        if (now - since > 90)
                            throw new TimeoutException("First token timeout");
                        return;
                    }

                    Require(I(engine, "outputToken") == firstReferenceToken, "Full-model first token differs from reference");
                    report.firstToken = I(engine, "outputToken");
                    lastGenerated = 1;
                    menu.SendCustomEvent("ToggleTextures");
                    menu.SendCustomEvent("ToggleExecution");
                    Require(B(engine, "paused") && B(state, "texturesShown"), "Global showcase pause failed");
                    beforePass = I(engine, "passCursor");
                    pausedMotionCaptured = false;
                    Next("pause");
                    return;
                }

                if (phase == "pause")
                {
                    if (!pausedMotionCaptured && now - since > .1)
                    {
                        pausedIdleClock = (float)visual.GetProgramVariable("orbitIdleTime");
                        pausedBusyClock = (float)visual.GetProgramVariable("orbitBusyTime");
                        pausedMotionCaptured = true;
                    }
                    if (now - since < .25)
                        return;
                    Require(pausedMotionCaptured && (float)visual.GetProgramVariable("orbitIdleTime") == pausedIdleClock
                        && (float)visual.GetProgramVariable("orbitBusyTime") == pausedBusyClock, "Paused motion clocks advanced");
                    Require(I(engine, "passCursor") == beforePass, "Paused GPU advanced");
                    Require(display.flowRenderer.enabled, "Executor does not see local links");
                    var activity = (Texture2D)visual.GetProgramVariable("eventsTexture");
                    int sticky = activity.GetPixels().Count(c => c.g > .5f);
                    int expected = display.readOffsets[beforePass + 1] - display.readOffsets[beforePass] + (display.writeResources[beforePass] >= 0 ? 1 : 0);
                    Require(sticky == expected, "Paused links do not describe next operator");
                    Capture("shown-paused");
                    menu.SendCustomEvent("OneOperator");
                    Next("step");
                    return;
                }

                if (phase == "step")
                {
                    if (!B(engine, "paused"))
                        return;
                    Require(I(engine, "passCursor") == ((beforePass + 1) % controller.runner.steps.Length), "One operator advanced wrong number of passes");
                    report.singleOperator = true;
                    ratePass = I(engine, "passCursor");
                    rateSince = now;
                    menu.SendCustomEvent("ToggleExecution");
                    Next("rate");
                    return;
                }

                if (phase == "rate")
                {
                    if (now - rateSince < .5)
                        return;
                    report.slowRatePasses = I(engine, "passCursor") - ratePass;
                    report.slowRateSeconds = (float)(now - rateSince);
                    float totalMotion = (float)visual.GetProgramVariable("motionTime");
                    float totalOrbit = (float)visual.GetProgramVariable("orbitIdleTime") + (float)visual.GetProgramVariable("orbitBusyTime");
                    Require(Mathf.Abs(totalMotion - totalOrbit) < .02f, "Orbit clocks lost elapsed time through pause/resume");
                    var bars = (Material)visual.GetProgramVariable("barsMaterial");
                    Require(Mathf.Abs(bars.GetFloat("_OrbitIdleTime") + bars.GetFloat("_OrbitBusyTime") - totalOrbit) < .02f, "Motion shader clocks are not bound");
                    Require(report.slowRatePasses > 2 && report.slowRatePasses <= Math.Ceiling(90 * (now - rateSince)) + 4, "Fixed slowdown failed");
                    Capture("shown-computing");
                    menu.SendCustomEvent("ToggleTextures");
                    Next("finish");
                    return;
                }

                if (phase == "finish")
                {
                    int count = I(engine, "generatedCount");
                    if (count < 2)
                    {
                        if (now - since > 60)
                            throw new TimeoutException("Second token timeout");
                        return;
                    }

                    Require(I(engine, "outputToken") == secondReferenceToken, "Second token differs from reference");
                    if (B(engine, "running"))
                        return;
                    report.secondToken = I(engine, "outputToken");
                    report.actualGpuInference = true;
                    Networking.LocalPlayer.TeleportTo(new Vector3(9, .03f, 0), Quaternion.identity);
                    menuProxy.heightSlider.value = 2.2f;
                    Next("observation-rise");
                    return;
                }

                if (phase == "observation-rise")
                {
                    if (menuProxy.observation.deck.position.y < 2.18f)
                    {
                        Require(now - since < 7, "Observation deck did not reach slider height");
                        return;
                    }
                    Require(Networking.LocalPlayer.GetPosition().y > 2.1f, "Player did not ride the observation deck");
                    Require(menuProxy.observation.deckCollider.enabled && menuProxy.observation.deckRenderer.enabled, "Raised deck is not visible/solid");
                    Networking.LocalPlayer.TeleportTo(new Vector3(11.8f, 1.5f, 0), Quaternion.identity);
                    Next("observation-fall");
                    return;
                }

                if (phase == "observation-fall")
                {
                    if (now - since < .3) return;
                    Require(menuProxy.heightSlider.value == 0 && !menuProxy.observation.deckRenderer.enabled
                        && !menuProxy.observation.deckCollider.enabled, "Fallen player's deck did not reset and hide");
                    Networking.LocalPlayer.TeleportTo(new Vector3(14, -3, 0), Quaternion.identity);
                    Next("observation-recover");
                    return;
                }

                if (phase == "observation-recover")
                {
                    if (now - since < .3) return;
                    var feet = Networking.LocalPlayer.GetPosition();
                    float radius = new Vector2(feet.x, feet.z).magnitude;
                    Require(feet.y > -.4f && radius > 11.2f && radius < 12.5f, "Void fall did not recover onto outer ground");
                    report.observationLift = report.observationFallReset = report.voidRecovery = true;
                    Networking.LocalPlayer.TeleportTo(new Vector3(2.6f, .03f, 0), Quaternion.LookRotation(Vector3.left));
                    for (int i = 0; i < 29; i++)
                        ClientSimMain.SpawnRemotePlayer("Observer " + i.ToString("D2"));
                    int index = 0;
                    foreach (var p in ClientPlayers())
                        if (!p.Player.isLocal)
                        {
                            float angle = index++ * Mathf.PI * 2 / 31;
                            p.transform.position = new Vector3(Mathf.Sin(angle) * 3.4f, .1f, Mathf.Cos(angle) * 3.4f);
                        }

                    Next("crowd");
                    return;
                }

                if (phase == "crowd")
                {
                    if (now - since < 1)
                        return;
                    var assigned = (int[])social.GetProgramVariable("slotPlayerIds");
                    var active = assigned.Where(id => id >= 0).ToArray();
                    Require(active.Length == 32 && active.Distinct().Count() == 32, "Each nearby player did not get one unique panel: " + active.Length);
                    report.chatParticipants = active.Length;
                    report.panelAllocation = true;
                    Capture("thirty-two-panels");
                    for (int i = 0; i < 48; i++)
                        ClientSimMain.SpawnRemotePlayer("Overflow observer " + i.ToString("D2"));
                    foreach (var p in ClientPlayers())
                        if (!p.Player.isLocal)
                            p.transform.position = new Vector3(0, .1f, -3.3f);
                    Next("eighty");
                    return;
                }

                if (phase == "eighty")
                {
                    if (now - since < 1.2)
                        return;
                    var active = ((int[])social.GetProgramVariable("slotPlayerIds")).Where(id => id >= 0).ToArray();
                    Require(active.Length == 80 && active.Distinct().Count() == 80, "80 crowded players do not have unique panels: " + active.Length);
                    report.eightyParticipants = true;
                    File.WriteAllText(QwenSceneInstaller.Results + "/pc-validation.json", JsonUtility.ToJson(report, true));
                    Next("switch-viewer");
                    EditorApplication.isPlaying = false;
                    return;
                }

                if (phase == "switch-viewer" && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    EditorSceneManager.OpenScene(QwenProject.ViewerScene, OpenSceneMode.Single);
                    Next("viewer-enter");
                    return;
                }

                if (phase == "viewer" && EditorApplication.isPlaying)
                {
                    stateProxy = UnityEngine.Object.FindObjectOfType<QwenExhibitState>();
                    menuProxy = UnityEngine.Object.FindObjectOfType<QwenLocalMenu>();
                    if (stateProxy == null || !Utilities.IsValid(Networking.LocalPlayer))
                        return;
                    state = UdonSharpEditorUtility.GetBackingUdonBehaviour(stateProxy);
                    menu = UdonSharpEditorUtility.GetBackingUdonBehaviour(menuProxy);
                    if (!B(menu, "initialized"))
                        return;
                    var d = UnityEngine.Object.FindObjectOfType<QwenComputeDisplay>();
                    Require(!d.runner.supportsInference && d.flowRenderer == null, "Viewer owns inference or links");
                    Rpc(Networking.LocalPlayer, "_SetVisibility", ("value", true));
                    Require(B(menu, "texturesVisible") && !menuProxy.executionControls.activeSelf, "Viewer global visibility/execution controls incorrect");
                    Rpc(Networking.LocalPlayer, "_Core", ("mask", 1 << 13), ("busy", true), ("paused", true));
                    // This is the authoritative local owner context only; remote receipt is checked by explicit deserialization.
                    state.SetProgramVariable("layerMask", 1 << 13);
                    state.RunProgram("_onDeserialization");
                    report = JsonUtility.FromJson<Report>(File.ReadAllText(QwenSceneInstaller.Results + "/pc-validation.json"));
                    report.viewer = true;
                    report.passed = true;
                    Capture("android-viewer");
                    File.WriteAllText(QwenSceneInstaller.Results + "/validation.json", JsonUtility.ToJson(report, true));
                    Next("done");
                    EditorApplication.isPlaying = false;
                    return;
                }

                if (phase == "done" && !EditorApplication.isPlaying && !EditorApplication.isPlayingOrWillChangePlaymode)
                {
                    SessionState.SetString(Key, "");
                    EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                    if (File.Exists(QwenSceneInstaller.Results + "/failure.txt"))
                        File.Delete(QwenSceneInstaller.Results + "/failure.txt");
                    Debug.Log("QWEN_FULL_PROJECT_VALIDATION_PASSED");
                    EditorApplication.Exit(0);
                }
            }
            catch (Exception e)
            {
                Fail(e);
            }
        }

        static void Capture(string name, Vector3? eye = null, Vector3? target = null, float fov = 65)
        {
            var hiddenRenderers = ClientPlayers().SelectMany(p => p.GetComponentsInChildren<Renderer>(true)).Where(r => r.enabled).ToArray();
            foreach (var r in hiddenRenderers)
                r.enabled = false;
            var hiddenCanvases = UnityEngine.Object.FindObjectsOfType<Canvas>().Where(c => c.enabled && c.transform.root.name != QwenSceneInstaller.RootName && c.GetComponentInParent<QwenLocalMenu>() == null).ToArray();
            foreach (var c in hiddenCanvases)
                c.enabled = false;
            try
            {
                QwenPreview.Capture(QwenSceneInstaller.Results + "/" + name + ".png", eye ?? new Vector3(4.5f, 3.1f, -6.5f), target ?? new Vector3(0, 1.8f, 0), fov, 1600, 1000);
            }
            finally
            {
                foreach (var r in hiddenRenderers)
                    if (r != null)
                        r.enabled = true;
                foreach (var c in hiddenCanvases)
                    if (c != null)
                        c.enabled = true;
            }
        }

        static ClientSimPlayer[] ClientPlayers() => Resources.FindObjectsOfTypeAll<ClientSimPlayer>().Where(p => p != null && p.Player != null && p.gameObject.scene.IsValid()).ToArray();
        static void Fail(Exception e)
        {
            SessionState.SetString(Key, "");
            Directory.CreateDirectory(QwenSceneInstaller.Results);
            File.WriteAllText(QwenSceneInstaller.Results + "/failure.txt", e.ToString());
            Debug.LogException(e);
            if (EditorApplication.isPlaying)
                EditorApplication.isPlaying = false;
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }

        [Serializable]
        class References
        {
            public Case[] cases;
        }

        [Serializable]
        class Case
        {
            public string question;
        }

        [Serializable]
        class SceneReports
        {
            public SceneReport[] scenes;
        }

        [Serializable]
        class SceneReport
        {
            public string name;
            public int chatSlots, chatOverlaps, coreTriangles, flowResources;
            public bool modelFreeViewer;
        }

        [Serializable]
        class Report
        {
            public bool passed, permissions, globalVisibility, globalWish, coreSenderValidation, singleOperator, actualGpuInference, panelAllocation, eightyParticipants, viewer;
            public bool outerGroundSpawn, observationLift, observationFallReset, voidRecovery;
            public bool automaticKeyboardHandover, retainsNearbyHolder, nearestPcSelection, keyboardTracking, inputAboveKeyboard, longMessageClipping;
            public int firstToken, secondToken, slowRatePasses, chatParticipants;
            public float slowRateSeconds;
            public string scope;
        }
    }
}
