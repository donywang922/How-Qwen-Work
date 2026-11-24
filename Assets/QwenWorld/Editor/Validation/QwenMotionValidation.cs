using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace QwenWorld.Editor
{
    public static class QwenMotionValidation
    {
        static Material probe;
        static RenderTexture target;
        static Texture2D pixels;

        [MenuItem("Qwen World/Validation/Check motion continuity")]
        public static void ValidateBatch()
        {
            var previous = RenderTexture.active;
            try
            {
                QwenModelPipeline.Compile();
                var source = Shader.Find("QwenWorld/WhiteMotionBars");
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(QwenProject.Root + "/Editor/Validation/Shaders/QwenMotionProbe.shader");
                Require(source != null && !ShaderUtil.ShaderHasError(source), "Motion shader failed.");
                Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Motion probe failed.");
                probe = new Material(shader);
                target = new RenderTexture(12, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
                target.Create();
                pixels = new Texture2D(12, 1, TextureFormat.RGBAFloat, false, true);
                var cases = new List<Case>();
                foreach (float age in new[] { 0f, 600f, 10800f, 43200f })
                    foreach (int fps in new[] { 30, 60, 144 })
                        cases.Add(CheckTransitions(age, fps));

                var grow = new int[6];
                var shrink = new int[6];
                var last = Sample(0, 0, 0, 1);
                for (int step = 1; step <= 600; step++)
                {
                    var next = Sample(step * .02f, 0, step * .02f, 1);
                    for (int bar = 0; bar < 6; bar++)
                    {
                        float tail = Vector3.Distance(last[bar * 2], next[bar * 2]);
                        float head = Vector3.Distance(last[bar * 2 + 1], next[bar * 2 + 1]);
                        if (tail < .0002f && head > .001f) grow[bar]++;
                        if (head < .0002f && tail > .001f) shrink[bar]++;
                    }
                    last = next;
                }
                for (int bar = 0; bar < 6; bar++)
                    Require(grow[bar] > 20 && shrink[bar] > 20, "Head/tail anchoring failed for bar " + bar);

                string folder = QwenProject.DataPath("validation/motion-bars");
                Directory.CreateDirectory(folder);
                File.WriteAllText(folder + "/validation.json", JsonUtility.ToJson(new Report
                {
                    passed = true, validatedAtUtc = DateTime.UtcNow.ToString("o"),
                    device = SystemInfo.graphicsDeviceName, cases = cases.ToArray(),
                    growthAnchoredSamples = grow, shrinkAnchoredSamples = shrink,
                    scope = "Real GPU samples of the production motion function; hidden orbit start/stop, rapid toggles, cycle boundaries and fixed-end growth/shrink."
                }, true));
                Debug.Log("QWEN_MOTION_VALIDATION_PASSED cases=" + cases.Count);
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
            finally
            {
                RenderTexture.active = previous;
                if (target != null) { target.Release(); UnityEngine.Object.DestroyImmediate(target); }
                if (pixels != null) UnityEngine.Object.DestroyImmediate(pixels);
                if (probe != null) UnityEngine.Object.DestroyImmediate(probe);
            }
        }

        static Case CheckTransitions(float age, int fps)
        {
            float idle = age * .4f, busy = age * .6f, blend = 0, dt = 1f / fps, maximum = 0;
            var last = Sample(age, idle, busy, blend);
            for (int step = 1; step <= fps * 5; step++)
            {
                float elapsed = step * dt;
                bool computing = elapsed < 1 ? ((int)(elapsed / .12f) % 2 == 1) : elapsed < 2.5f || elapsed > 3.5f;
                blend = Mathf.MoveTowards(blend, computing ? 1 : 0, dt * 3);
                idle += dt * (1 - blend);
                busy += dt * blend;
                var next = Sample(age + elapsed, idle, busy, blend);
                for (int point = 0; point < next.Length; point++)
                {
                    float distance = Vector3.Distance(next[point], last[point]);
                    Require(!float.IsNaN(distance) && distance < 8 * dt + .01f,
                        "Orbit discontinuity at " + age + " seconds / " + fps + " fps: " + distance + " m");
                    maximum = Mathf.Max(maximum, distance);
                }
                last = next;
            }
            return new Case { ageSeconds = age, framesPerSecond = fps, maximumFrameDisplacement = maximum };
        }

        static Vector3[] Sample(float time, float idle, float busy, float blend)
        {
            probe.SetFloat("_MotionTime", time);
            probe.SetFloat("_OrbitIdleTime", idle);
            probe.SetFloat("_OrbitBusyTime", busy);
            probe.SetFloat("_BusyBlend", blend);
            Graphics.Blit(null, target, probe);
            RenderTexture.active = target;
            pixels.ReadPixels(new Rect(0, 0, 12, 1), 0, 0, false);
            pixels.Apply(false, false);
            var values = pixels.GetPixels();
            var positions = new Vector3[12];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = new Vector3(values[i].r, values[i].g, values[i].b);
            return positions;
        }

        static void Require(bool ok, string message)
        {
            if (!ok) throw new InvalidOperationException(message);
        }

        [Serializable] class Case
        {
            public float ageSeconds, maximumFrameDisplacement;
            public int framesPerSecond;
        }

        [Serializable] class Report
        {
            public bool passed;
            public string validatedAtUtc, device, scope;
            public Case[] cases;
            public int[] growthAnchoredSamples, shrinkAnchoredSamples;
        }
    }
}
