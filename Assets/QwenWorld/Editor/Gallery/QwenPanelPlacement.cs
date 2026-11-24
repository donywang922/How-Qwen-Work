using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace QwenWorld.Editor
{
    public static class QwenPanelPlacement
    {
        public static readonly Vector3 CoreCenter = new Vector3(0, 3.25f, 0);
        public struct Pose
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        sealed class Panel
        {
            public Vector3 center, half;
            public Quaternion rotation;
            public string name;
        }

        static readonly List<Panel> placed = new List<Panel>();
        static System.Random random;
        static int attempts;
        public static void Reset()
        {
            placed.Clear();
            random = new System.Random(195103);
            attempts = 0;
        }

        public static void Register(Vector3 position, Quaternion rotation, Vector2 size, bool weights, int index)
        {
            placed.Add(new Panel{center = position, rotation = rotation, half = new Vector3(size.x * .5f + .015f, size.y * .5f + .015f, .060f), name = (weights ? "static-" : "live-") + index});
        }

        public static Pose PlaceVertically(Vector3 preferred, Quaternion rotation, Vector2 size, int index, float minHeight, float maxHeight)
        {
            for (int attempt = 0; attempt < 401; attempt++)
            {
                float offset = attempt == 0 ? 0 : ((attempt + 1) / 2) * .015f * (attempt % 2 == 1 ? 1 : -1);
                var position = preferred + Vector3.up * offset;
                var panel = new Panel{center = position, rotation = rotation, half = new Vector3(size.x * .5f + .015f, size.y * .5f + .015f, .060f), name = "live-" + index};
                if (position.y - panel.half.y < minHeight || position.y + panel.half.y > maxHeight)
                    continue;
                bool clear = true;
                foreach (var other in placed)
                    if (Intersects(CoreCenter, panel.center, other) || Intersects(CoreCenter, other.center, panel) || Touching(panel, other))
                    {
                        clear = false;
                        break;
                    }

                if (!clear)
                    continue;
                placed.Add(panel);
                return new Pose{position = position, rotation = rotation};
            }

            throw new InvalidOperationException("No unobstructed vertical placement for live-" + index);
        }

        public static Pose Place(Vector3 preferred, Vector2 size, bool weights, int index)
        {
            if (random == null)
                Reset();
            for (int attempt = 0; attempt < 30000; attempt++)
            {
                attempts++;
                Vector3 position = preferred;
                if (attempt > 0)
                {
                    float angle = (float)random.NextDouble() * Mathf.PI * 2;
                    float radius = (weights ? 9.2f : 7.4f) + ((float)random.NextDouble() - .5f) * (weights ? .76f : .64f);
                    float y = Mathf.Lerp(size.y * .5f + .15f, weights ? 4.8f : 3.6f, (float)random.NextDouble());
                    position = new Vector3(Mathf.Sin(angle) * radius, y, Mathf.Cos(angle) * radius);
                }

                var radial = new Vector3(position.x, 0, position.z).normalized;
                var panel = new Panel{center = position, rotation = Quaternion.LookRotation(-radial), half = new Vector3(size.x * .5f + .015f, size.y * .5f + .015f, .060f), name = (weights ? "static-" : "live-") + index};
                bool clear = true;
                foreach (var other in placed)
                {
                    if (Intersects(CoreCenter, panel.center, other) || Intersects(CoreCenter, other.center, panel) || Touching(panel, other))
                    {
                        clear = false;
                        break;
                    }
                }

                if (!clear)
                    continue;
                placed.Add(panel);
                return new Pose{position = position, rotation = panel.rotation};
            }

            throw new InvalidOperationException("No unobstructed placement for " + (weights ? "static" : "live") + " " + index + "; no panels were dropped.");
        }

        static bool Touching(Panel a, Panel b)
        {
            if (Mathf.Abs(a.center.y - b.center.y) > a.half.y + b.half.y)
                return false;
            if (Mathf.Abs(new Vector2(a.center.x, a.center.z).magnitude - new Vector2(b.center.x, b.center.z).magnitude) > .18f)
                return false;
            float angle = Mathf.Abs(Mathf.DeltaAngle(Mathf.Atan2(a.center.x, a.center.z) * Mathf.Rad2Deg, Mathf.Atan2(b.center.x, b.center.z) * Mathf.Rad2Deg)) * Mathf.Deg2Rad;
            return angle < Mathf.Atan2(a.half.x, new Vector2(a.center.x, a.center.z).magnitude) + Mathf.Atan2(b.half.x, new Vector2(b.center.x, b.center.z).magnitude);
        }

        static bool Intersects(Vector3 start, Vector3 end, Panel panel)
        {
            var inverse = Quaternion.Inverse(panel.rotation);
            var p = inverse * (start - panel.center);
            var d = inverse * (end - start);
            float lo = 0, hi = 1;
            for (int axis = 0; axis < 3; axis++)
            {
                if (Mathf.Abs(d[axis]) < .000001f)
                {
                    if (Mathf.Abs(p[axis]) > panel.half[axis])
                        return false;
                }
                else
                {
                    float t0 = (-panel.half[axis] - p[axis]) / d[axis], t1 = (panel.half[axis] - p[axis]) / d[axis];
                    lo = Mathf.Max(lo, Mathf.Min(t0, t1));
                    hi = Mathf.Min(hi, Mathf.Max(t0, t1));
                    if (lo > hi)
                        return false;
                }
            }

            return hi >= 0 && lo <= 1;
        }

        public static void ValidateAndReport(string model)
        {
            var blocked = new List<string>();
            for (int i = 0; i < placed.Count; i++)
                for (int j = 0; j < placed.Count; j++)
                    if (i != j && Intersects(CoreCenter, placed[i].center, placed[j]))
                        blocked.Add(placed[i].name + " -> " + placed[j].name);
            Directory.CreateDirectory(QwenProject.DataPath("validation/full-project"));
            File.WriteAllText(QwenProject.DataPath("validation/full-project/layout-") + model + ".json", JsonUtility.ToJson(new Report{panels = placed.Count, candidateAttempts = attempts, blockedPairs = blocked.ToArray(), core = CoreCenter}, true));
            if (blocked.Count > 0 || placed.Count != 573)
                throw new InvalidOperationException("Layout validation failed: " + placed.Count + " panels, " + blocked.Count + " blocked pairs.");
        }

        [Serializable]
        class Report
        {
            public int panels, candidateAttempts;
            public Vector3 core;
            public string[] blockedPairs;
        }
    }
}
