using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UdonSharpEditor;

namespace QwenWorld.Editor
{
    public static class QwenGalleryLayout
    {
        public static readonly string Output = QwenProject.DataPath("validation/raised-gallery");
        public static void Align(Transform root, string model)
        {
            var outer = root.Find("Outer ring - static textures");
            var inner = root.Find("Inner ring - live CRT");
            if (outer == null || inner == null)
                throw new InvalidOperationException("Missing texture rings.");
            var panels = outer.GetComponentsInChildren<MeshRenderer>(true).Where(r => Regex.IsMatch(r.name, @"^\d{3} ")).OrderBy(r => r.name, StringComparer.Ordinal).Concat(inner.GetComponentsInChildren<MeshRenderer>(true).OrderBy(r => r.name, StringComparer.Ordinal)).ToArray();
            if (panels.Length != 573)
                throw new InvalidOperationException("Expected 484 static and 89 CRT panels.");
            var bounds = panels[0].bounds;
            foreach (var panel in panels.Skip(1))
                bounds.Encapsulate(panel.bounds);
            float delta = QwenPanelPlacement.CoreCenter.y - bounds.center.y;
            outer.position += Vector3.up * delta;
            inner.position += Vector3.up * delta;
            QwenPanelPlacement.Reset();
            int adjusted = 0;
            float largestAdjustment = 0;
            for (int i = 0; i < panels.Length; i++)
            {
                var panel = panels[i];
                var size = panel.GetComponent<MeshFilter>().sharedMesh.bounds.size;
                if (i < 484)
                    QwenPanelPlacement.Register(panel.transform.position, panel.transform.rotation, new Vector2(size.x, size.y), true, i);
                else
                {
                    var pose = QwenPanelPlacement.PlaceVertically(panel.transform.position, panel.transform.rotation, new Vector2(size.x, size.y), i - 484, bounds.min.y + delta - .001f, bounds.max.y + delta + .001f);
                    float adjustment = Mathf.Abs(pose.position.y - panel.transform.position.y);
                    if (adjustment > .001f)
                    {
                        adjusted++;
                        largestAdjustment = Mathf.Max(largestAdjustment, adjustment);
                    }

                    panel.transform.SetPositionAndRotation(pose.position, pose.rotation);
                }
            }

            Directory.CreateDirectory(Output);
            var finalBounds = panels[0].bounds;
            foreach (var panel in panels.Skip(1))
                finalBounds.Encapsulate(panel.bounds);
            if (Mathf.Abs(finalBounds.center.y - QwenPanelPlacement.CoreCenter.y) > .001f)
                throw new InvalidOperationException("Gallery bounds are not centred on the core.");
            File.WriteAllText(Output + "/alignment.json", JsonUtility.ToJson(new Report{panels = 573, heightOffset = delta, previousCentre = bounds.center.y, centreHeight = finalBounds.center.y, coreHeight = QwenPanelPlacement.CoreCenter.y, minHeight = finalBounds.min.y, maxHeight = finalBounds.max.y, pureTranslation = adjusted == 0, verticallyAdjustedPanels = adjusted, largestAdjustment = largestAdjustment}, true));
            QwenPanelPlacement.ValidateAndReport(model);
        }

        [Serializable]
        class Report
        {
            public int panels, verticallyAdjustedPanels;
            public float heightOffset, previousCentre, centreHeight, coreHeight, minHeight, maxHeight, largestAdjustment;
            public bool pureTranslation;
        }
    }
}
