using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace QwenWorld.Editor
{
    /// <summary>Renderer.material is the Udon-exposed way to instance mutable uniforms.</summary>
    public static class QwenRunnerMaterials
    {
        public static void Configure(QwenTextureRunner runner)
        {
            var root = Array.Find(runner.gameObject.scene.GetRootGameObjects(), go => go.name == "PC MODEL DATA - excluded from Android");
            if (root == null)
                throw new InvalidOperationException("Missing PC model root.");
            runner.outputUniforms = Holder(root.transform, "Output uniforms", runner.steps[runner.steps.Length - 1]);
            runner.tokenUniforms = Holder(root.transform, "Token uniforms", runner.controlMaterial);
        }

        private static Renderer Holder(Transform parent, string name, Material material)
        {
            var child = parent.Find(name);
            if (child == null)
            {
                child = new GameObject(name, typeof(MeshRenderer)).transform;
                child.SetParent(parent, false);
            }

            var renderer = child.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.enabled = false;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return renderer;
        }
    }
}
