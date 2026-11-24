using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace QwenWorld.Editor
{
    public sealed class QwenExhibitGeometry
    {
        readonly List<Vector3> vertices = new List<Vector3>(), normals = new List<Vector3>();
        readonly List<Vector4> tangents = new List<Vector4>(), data = new List<Vector4>();
        readonly List<Vector2> uv = new List<Vector2>();
        readonly List<int> indices = new List<int>();
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 tag)
        {
            int n = vertices.Count;
            vertices.AddRange(new[]{a, b, c, d});
            Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
            for (int i = 0; i < 4; i++)
            {
                normals.Add(normal);
                uv.Add(tag);
                tangents.Add(Vector4.zero);
                data.Add(Vector4.zero);
            }

            indices.AddRange(new[]{n, n + 1, n + 2, n, n + 2, n + 3});
        }

        public void Box(Vector3 center, Vector3 size)
        {
            Vector3 h = size * .5f;
            Vector3 p0 = center + new Vector3(-h.x, -h.y, -h.z), p1 = center + new Vector3(h.x, -h.y, -h.z), p2 = center + new Vector3(h.x, h.y, -h.z), p3 = center + new Vector3(-h.x, h.y, -h.z);
            Vector3 p4 = center + new Vector3(-h.x, -h.y, h.z), p5 = center + new Vector3(h.x, -h.y, h.z), p6 = center + new Vector3(h.x, h.y, h.z), p7 = center + new Vector3(-h.x, h.y, h.z);
            Quad(p0, p3, p2, p1, Vector2.zero);
            Quad(p4, p5, p6, p7, Vector2.zero);
            Quad(p0, p4, p7, p3, Vector2.zero);
            Quad(p1, p2, p6, p5, Vector2.zero);
            Quad(p3, p7, p6, p2, Vector2.zero);
            Quad(p0, p1, p5, p4, Vector2.zero);
        }

        public void Ring(float inner, float outer, float halfHeight, int layer, Vector3 offset, int segments = 96)
        {
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                Vector3[] p = new Vector3[8];
                for (int side = 0; side < 2; side++)
                    for (int height = 0; height < 2; height++)
                        for (int end = 0; end < 2; end++)
                        {
                            float angle = end == 0 ? a : b, r = side == 0 ? inner : outer;
                            p[side * 4 + height * 2 + end] = new Vector3(Mathf.Cos(angle) * r, height == 0 ? -halfHeight : halfHeight, Mathf.Sin(angle) * r) + offset;
                        }

                var tag = new Vector2(layer, 0);
                Quad(p[2], p[3], p[7], p[6], tag);
                Quad(p[0], p[4], p[5], p[1], tag);
                Quad(p[4], p[6], p[7], p[5], tag);
                Quad(p[0], p[1], p[3], p[2], tag);
            }
        }

        public void BeveledRing(float inner, float outer, float halfHeight, float bevel, Vector3 offset, int segments = 160)
        {
            var profile = new[]{new Vector2(inner + bevel, -halfHeight), new Vector2(outer - bevel, -halfHeight), new Vector2(outer, -halfHeight + bevel), new Vector2(outer, halfHeight - bevel), new Vector2(outer - bevel, halfHeight), new Vector2(inner + bevel, halfHeight), new Vector2(inner, halfHeight - bevel), new Vector2(inner, -halfHeight + bevel)};
            var distance = new float[9];
            for (int j = 0; j < 8; j++)
                distance[j + 1] = distance[j] + Vector2.Distance(profile[j], profile[(j + 1) % 8]);
            for (int i = 0; i < segments; i++)
                for (int j = 0; j < profile.Length; j++)
                {
                    float a = i * Mathf.PI * 2 / segments, b = (i + 1) * Mathf.PI * 2 / segments;
                    var p = profile[j];
                    var q = profile[(j + 1) % profile.Length];
                    int start = vertices.Count;
                    Quad(offset + new Vector3(Mathf.Cos(a) * p.x, p.y, Mathf.Sin(a) * p.x), offset + new Vector3(Mathf.Cos(a) * q.x, q.y, Mathf.Sin(a) * q.x), offset + new Vector3(Mathf.Cos(b) * q.x, q.y, Mathf.Sin(b) * q.x), offset + new Vector3(Mathf.Cos(b) * p.x, p.y, Mathf.Sin(b) * p.x), Vector2.zero);
                    float u0 = .02f + .96f * i / segments, u1 = .02f + .96f * (i + 1) / segments, v0 = .02f + .96f * distance[j] / distance[8], v1 = .02f + .96f * distance[j + 1] / distance[8];
                    data[start] = new Vector4(u0, v0, 0, 0);
                    data[start + 1] = new Vector4(u0, v1, 0, 0);
                    data[start + 2] = new Vector4(u1, v1, 0, 0);
                    data[start + 3] = new Vector4(u1, v0, 0, 0);
                }
        }

        public void BeveledBox(Vector3 center, Vector3 size, float bevel)
        {
            Vector3 half = size * .5f, inside = half - Vector3.one * bevel;
            for (int face = 0; face < 6; face++)
            {
                int axis = face / 2, u = (axis + 1) % 3, v = (axis + 2) % 3;
                float sign = face % 2 == 0 ? 1 : -1;
                float[] a = {-half[u], -inside[u], inside[u], half[u]}, b = {-half[v], -inside[v], inside[v], half[v]};
                for (int x = 0; x < 3; x++)
                    for (int y = 0; y < 3; y++)
                    {
                        var points = new Vector3[4];
                        var ns = new Vector3[4];
                        for (int k = 0; k < 4; k++)
                        {
                            int xx = x + (k == 1 || k == 2 ? 1 : 0), yy = y + (k >= 2 ? 1 : 0);
                            var p = Vector3.zero;
                            p[axis] = half[axis] * sign;
                            p[u] = a[xx];
                            p[v] = b[yy];
                            var q = new Vector3(Mathf.Clamp(p.x, -inside.x, inside.x), Mathf.Clamp(p.y, -inside.y, inside.y), Mathf.Clamp(p.z, -inside.z, inside.z));
                            ns[k] = (p - q).normalized;
                            points[k] = center + q + ns[k] * bevel;
                        }

                        if (sign < 0)
                        {
                            var p = points[1];
                            points[1] = points[3];
                            points[3] = p;
                            var n = ns[1];
                            ns[1] = ns[3];
                            ns[3] = n;
                        }

                        int start = vertices.Count;
                        Quad(points[0], points[1], points[2], points[3], Vector2.zero);
                        for (int k = 0; k < 4; k++)
                            normals[start + k] = ns[k];
                    }
            }
        }

        public void Bars()
        {
            const int segments = 32;
            Vector2[] corners = {new Vector2(-.014f, -.014f), new Vector2(.014f, -.014f), new Vector2(.014f, .014f), new Vector2(-.014f, .014f)};
            for (int bar = 0; bar < 6; bar++)
                for (int s = 0; s < segments; s++)
                    for (int face = 0; face < 4; face++)
                    {
                        int start = vertices.Count;
                        for (int i = 0; i < 4; i++)
                        {
                            int corner = (face + (i == 1 || i == 2 ? 1 : 0)) % 4;
                            float q = (float)(s + (i >= 2 ? 1 : 0)) / segments;
                            var faceNormals = new[]{Vector3.back, Vector3.right, Vector3.forward, Vector3.left};
                            vertices.Add(new Vector3(corners[corner].x, 0, corners[corner].y));
                            normals.Add(faceNormals[face]);
                            uv.Add(new Vector2(bar, q));
                            tangents.Add(Vector4.zero);
                            data.Add(Vector4.zero);
                        }

                        indices.AddRange(new[]{start, start + 1, start + 2, start, start + 2, start + 3});
                    }

            for (int bar = 0; bar < 6; bar++)
                for (int end = 0; end < 2; end++)
                {
                    int start = vertices.Count;
                    for (int i = 0; i < 4; i++)
                    {
                        vertices.Add(new Vector3(corners[i].x, 0, corners[i].y));
                        normals.Add(end == 0 ? Vector3.down : Vector3.up);
                        uv.Add(new Vector2(bar, end));
                        tangents.Add(Vector4.zero);
                        data.Add(Vector4.zero);
                    }

                    indices.AddRange(new[]{start, start + 1, start + 2, start, start + 2, start + 3});
                }
        }

        public void FlowQuad(Vector3 origin, Vector3 axis, Vector3 side, int eventId, float a, float b, float width, int role = 0, bool centered = false)
        {
            int n = vertices.Count;
            for (int i = 0; i < 4; i++)
            {
                vertices.Add(origin);
                normals.Add(axis);
                tangents.Add(new Vector4(side.x, side.y, side.z, i == 0 || i == 3 ? -1 : 1));
                uv.Add(new Vector2(i >= 2 ? 1 : (centered ? -1 : 0), width));
                data.Add(new Vector4(eventId, a, b, role));
            }

            indices.AddRange(new[]{n, n + 1, n + 2, n, n + 2, n + 3});
        }

        public Mesh Save(string path, string name, Bounds? bounds = null)
        {
            var mesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (mesh == null)
            {
                mesh = new Mesh();
                AssetDatabase.CreateAsset(mesh, path);
            }

            mesh.name = name;
            mesh.Clear();
            mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTangents(tangents);
            mesh.SetUVs(0, uv);
            mesh.SetUVs(1, data);
            mesh.SetTriangles(indices, 0);
            mesh.RecalculateBounds();
            if (bounds.HasValue)
                mesh.bounds = bounds.Value;
            EditorUtility.SetDirty(mesh);
            return mesh;
        }
    }
}
