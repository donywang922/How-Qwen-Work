using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace QwenWorld.Editor
{
    public static class QwenGraphDisplayBuilder
    {
        public static void Build(QwenComputeDisplay display, string folder)
        {
            var runner = display.runner;
            var outer = GameObject.Find("Outer ring - static textures");
            var inner = GameObject.Find("Inner ring - live CRT");
            if (outer == null || inner == null || runner.steps == null)
                return;
            var panels = outer.GetComponentsInChildren<MeshRenderer>(true).Where(r => Regex.IsMatch(r.name, @"^\d{3} ")).OrderBy(r => r.name, StringComparer.Ordinal).Concat(inner.GetComponentsInChildren<MeshRenderer>(true).OrderBy(r => r.name, StringComparer.Ordinal)).ToArray();
            var textures = panels.Select(p => p.sharedMaterial.GetTexture("_MainTex")).ToArray();
            if (panels.Length != 573 || textures.Distinct().Count() != 573)
                throw new InvalidOperationException("Display resource map is not 484+89 unique textures.");
            var ids = textures.Select((t, i) => (t, i)).ToDictionary(pair => pair.t, pair => pair.i);
            var offsets = new List<int>();
            var reads = new List<int>();
            var writes = new List<int>();
            var layerNames = new List<string>();
            int currentLayer = -1;
            runner.stepLayers = new int[runner.steps.Length];
            for (int pass = 0; pass < runner.steps.Length; pass++)
            {
                var material = runner.steps[pass];
                offsets.Add(reads.Count);
                var seen = new HashSet<int>();
                foreach (string property in material.GetTexturePropertyNames())
                {
                    var texture = material.GetTexture(property);
                    if (texture == null || !ids.TryGetValue(texture, out int id) || property == "TexO")
                        continue;
                    string dim = "Dim" + property.Substring(3);
                    if (material.HasProperty(dim) && material.GetVector(dim).x <= 0)
                        continue;
                    seen.Add(id);
                    var layer = Regex.Match(texture.name, @"layers\.(\d+)\.");
                    if (layer.Success)
                        currentLayer = int.Parse(layer.Groups[1].Value);
                    else if (texture.name.Contains("embed_tokens") || Regex.IsMatch(texture.name, @"(^|\.)norm\.weight") || texture.name.Contains("lm_head"))
                        currentLayer = -1;
                }

                int output = ids.TryGetValue(material.GetTexture("TexO"), out int found) ? found : -1;
                if (output >= 0 && material.HasProperty("DstBlend") && material.GetInt("DstBlend") != 0)
                    seen.Add(output);
                reads.AddRange(seen.OrderBy(id => id));
                writes.Add(output);
                runner.stepLayers[pass] = currentLayer;
                layerNames.Add(pass + ":" + currentLayer + ":" + material.shader.name);
            }

            offsets.Add(reads.Count);
            display.resourceCount = panels.Length;
            display.readOffsets = offsets.ToArray();
            display.readResources = reads.ToArray();
            display.writeResources = writes.ToArray();
            var geo = new QwenExhibitGeometry();
            var random = new System.Random(809173);
            for (int id = 0; id < panels.Length; id++)
            {
                Vector3 p = panels[id].transform.position, c = QwenPanelPlacement.CoreCenter;
                for (int direction = 0; direction < 2; direction++)
                {
                    Vector3 origin = direction == 0 ? p : c, target = direction == 0 ? c : p, axis = target - origin;
                    Vector3 side = Vector3.Cross(axis, Vector3.up).normalized;
                    if (side.sqrMagnitude < .01f)
                        side = Vector3.right;
                    Vector3 other = Vector3.Cross(axis, side).normalized;
                    float length = axis.magnitude, start = 0;
                    while (start < length)
                    {
                        float end = Mathf.Min(length, start + Mathf.Lerp(.18f, .65f, (float)random.NextDouble()));
                        geo.FlowQuad(origin, axis, side, id * 2 + direction, start / length, end / length, .006f);
                        geo.FlowQuad(origin, axis, other, id * 2 + direction, start / length, end / length, .006f);
                        start = end + .012f;
                    }
                }

                var size = panels[id].GetComponent<MeshFilter>().sharedMesh.bounds.size;
                Vector3 right = panels[id].transform.right, up = panels[id].transform.up;
                // Read: four complete sides shrink about their own midpoint.
                for (int sign = -1; sign <= 1; sign += 2)
                {
                    geo.FlowQuad(p + up * size.y * .5f * sign, right * size.x * .5f, up, id * 2, 0, 1, .007f, 1, true);
                    geo.FlowQuad(p + right * size.x * .5f * sign, up * size.y * .5f, right, id * 2, 0, 1, .007f, 1, true);
                }

                // Write: four L corners, anchored at the corners as their arms shrink.
                for (int x = -1; x <= 1; x += 2)
                    for (int y = -1; y <= 1; y += 2)
                    {
                        Vector3 corner = p + right * size.x * .5f * x + up * size.y * .5f * y;
                        geo.FlowQuad(corner, -right * size.x * .22f * x, up, id * 2 + 1, 0, 1, .008f, 1);
                        geo.FlowQuad(corner, -up * size.y * .22f * y, right, id * 2 + 1, 0, 1, .008f, 1);
                    }
            }

            var mesh = geo.Save(folder + "/DataFlow.asset", "Local straight read-write segments", new Bounds(new Vector3(0, 2.5f, 0), new Vector3(23, 8, 23)));
            var go = new GameObject("Local GPU read-write links", typeof(MeshFilter), typeof(MeshRenderer));
            go.transform.SetParent(display.transform, false);
            go.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = Material(folder + "/DataFlow.mat", "QwenWorld/LocalDataFlow");
            renderer.enabled = false;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            display.flowRenderer = renderer;
            File.WriteAllText(QwenProject.DataPath("validation/full-project/graph-") + Path.GetFileName(folder) + ".json", JsonUtility.ToJson(new Report{resources = panels.Length, passes = runner.steps.Length, readReferences = reads.Count, triangles = mesh.triangles.Length / 3, passLayers = layerNames.ToArray()}, true));
        }

        public static Material Material(string path, string shader)
        {
            var found = Shader.Find(shader);
            if (found == null || ShaderUtil.ShaderHasError(found))
            {
                if (found != null)
                    foreach (var message in ShaderUtil.GetShaderMessages(found))
                        Debug.LogError(shader + " line " + message.line + ": " + message.message);
                throw new InvalidOperationException("Shader failed: " + shader);
            }

            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(found);
                AssetDatabase.CreateAsset(material, path);
            }

            return material;
        }

        [Serializable]
        class Report
        {
            public int resources, passes, readReferences, triangles;
            public string[] passLayers;
        }
    }
}
