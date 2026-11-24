using System;
using System.IO;
using UnityEngine;

namespace QwenWorld.Editor
{
    /// <summary>Shared editor-only render/readback; always restores GPU state and releases temporary objects.</summary>
    public static class QwenPreview
    {
        public static Color32[] Capture(string path, Vector3 eye, Vector3 target, float fov, int width = 1600, int height = 1000)
        {
            var go = new GameObject("Temporary Qwen preview");
            try
            {
                var camera = go.AddComponent<Camera>();
                camera.enabled = false;
                camera.transform.position = eye;
                camera.transform.LookAt(target);
                camera.fieldOfView = fov;
                camera.clearFlags = CameraClearFlags.Skybox;
                camera.nearClipPlane = .05f;
                camera.farClipPlane = 800;
                camera.allowHDR = false;
                return Capture(camera, path, width, height);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        public static Color32[] Capture(Camera camera, string path, int width = 1600, int height = 1000)
        {
            Canvas.ForceUpdateCanvases();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var previousActive = RenderTexture.active;
            var previousTarget = camera.targetTexture;
            var target = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB)
            {antiAliasing = 4};
            var image = new Texture2D(width, height, TextureFormat.RGB24, false, false);
            try
            {
                target.Create();
                camera.targetTexture = target;
                camera.Render();
                RenderTexture.active = target;
                image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                image.Apply();
                File.WriteAllBytes(path, image.EncodeToPNG());
                return image.GetPixels32();
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                target.Release();
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(image);
            }
        }
    }
}
