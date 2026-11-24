using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace QwenWorld.Editor
{
    public static class QwenObservationValidation
    {
        [MenuItem("Qwen World/Validation/Preview observation decks and Earth aligned ring")]
        public static void ValidateBatch()
        {
            try
            {
                QwenModelPipeline.Compile();
                EditorSceneManager.OpenScene(QwenProject.MainScene, OpenSceneMode.Single);
                var control = UnityEngine.Object.FindObjectOfType<QwenObservationPlatform>();
                var menu = UnityEngine.Object.FindObjectOfType<QwenLocalMenu>();
                var outer = GameObject.Find("Outer translucent hex ground");
                var deck = control.deck;
                Require(outer.transform.localScale.x == QwenWalkwaysBuilder.OuterRadius && outer.transform.position.y == -.18f,
                    "Outer ground size or step changed");
                Require(deck.localScale.x == 11.2f && !control.deckRenderer.enabled && !control.deckCollider.enabled,
                    "Observation deck must match white ground and be hidden initially");
                var spawn = control.recoveryPoint.position;
                Require(new Vector2(spawn.x,spawn.z).magnitude > 11.2f && new Vector2(spawn.x,spawn.z).magnitude < QwenWalkwaysBuilder.OuterRadius,
                    "Spawn must be on the outer annulus");
                Require(control.heightSlider == menu.heightSlider && menu.observation == control &&
                    menu.heightSlider.minValue == 0 && menu.heightSlider.maxValue == control.maximumHeight &&
                    menu.heightSlider.onValueChanged.GetPersistentEventCount() == 1,
                    "Height slider is not wired to the local observation deck");
                Require(control.GetComponent<VRC.SDK3.Components.VRCObjectSync>() == null,
                    "Personal deck must not synchronize transforms");
                var glass = menu.menuRoot.transform.Find("White panel").GetComponent<Image>().material;
                var inputGlass = UnityEngine.Object.FindObjectOfType<QwenSocialUI>().inputRoot.transform.Find("White panel").GetComponent<Image>().material;
                Require(glass.shader == inputGlass.shader && glass.GetFloat("_Opacity") >= .85f,
                    "Menu must use the input screen's dark glass style");
                var shader = Shader.Find("QwenWorld/AcrylicHexGround");
                Require(shader != null && shader.isSupported && !ShaderUtil.ShaderHasError(shader), "Acrylic shader failed");
                var ring = GameObject.Find("Orbital ring - imported Blender distant asset").GetComponent<MeshFilter>().sharedMesh;
                var normal = QwenOrbitalRingMesh.EarthDirection.normalized;
                var vertices = ring.vertices;
                // Baked ring is centered at world origin. Its thickness along the Earth axis
                // must be much smaller than the radius, independently of its visual AABB.
                float depth = vertices.Max(v => Mathf.Abs(Vector3.Dot(v,normal)));
                float minimumRadius = vertices.Min(v => v.magnitude);
                Require(ring.bounds.center.magnitude < .1f && depth < QwenOrbitalRingMesh.Radius * .07f && minimumRadius > QwenWalkwaysBuilder.WhiteRadius,
                    "Mechanical ring axis must pass through floor center and face Earth");
                string output = QwenProject.DataPath("validation/observation-platform");
                Directory.CreateDirectory(output);
                QwenPreview.Capture(output + "/outer-step.png", new Vector3(-4,1.45f,-12),new Vector3(0,-.05f,-10.2f),65);
                QwenPreview.Capture(output + "/earth-aligned-ring.png", -normal * 33,Vector3.zero,55,1600,1200);
                bool wasMenu = menu.menuRoot.activeSelf;
                var oldMenuPosition = menu.menuRoot.transform.position;
                var oldMenuRotation = menu.menuRoot.transform.rotation;
                try
                {
                    deck.position = Vector3.up * 3;
                    control.deckRenderer.enabled = true;
                    QwenPreview.Capture(output + "/raised-acrylic.png",new Vector3(10,4.45f,-8),new Vector3(0,2.5f,0),66);
                    QwenPreview.Capture(output + "/raised-overview.png",new Vector3(-22,18,-24),new Vector3(0,1.5f,0),55);
                    menu.menuRoot.SetActive(true);
                    menu.menuRoot.transform.SetPositionAndRotation(new Vector3(0,1.65f,-1.1f),Quaternion.identity);
                    var corners = new Vector3[4];
                    menu.menuRoot.transform.Find("White panel").GetComponent<RectTransform>().GetWorldCorners(corners);
                    Vector3 center = (corners[0] + corners[2]) * .5f;
                    QwenPreview.Capture(output + "/glass-menu.png",center + Vector3.back * .95f,center,42,1100,1100);
                }
                finally
                {
                    deck.position = Vector3.up * -.04f;
                    control.deckRenderer.enabled = false;
                    menu.menuRoot.transform.SetPositionAndRotation(oldMenuPosition,oldMenuRotation);
                    menu.menuRoot.SetActive(wasMenu);
                }
                File.WriteAllText(output + "/validation.json",JsonUtility.ToJson(new Report
                {
                    passed = true, validatedAtUtc = DateTime.UtcNow.ToString("o"),
                    outerDiameter = QwenWalkwaysBuilder.OuterRadius * 2, whiteAndObservationDiameter = 22.4f, step = .18f,
                    maximumHeight = control.maximumHeight, spawn = spawn,
                    ringCenter = QwenOrbitalRingMesh.Origin, ringAxis = normal,
                    ringMaximumAxialDepth = depth, ringMinimumRadius = minimumRadius,
                    localOnly = true, hiddenAtLowest = true, menuMatchesInput = true,
                    scope = "Authored geometry, shader compilation, slider wiring and rendered previews. Carry/fall/recovery are exercised by ClientSim and the SDK native test."
                },true));
                Debug.Log("QWEN_OBSERVATION_VALIDATION_PASSED");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception error)
            {
                Debug.LogException(error);
                if (Application.isBatchMode) EditorApplication.Exit(1);
                else throw;
            }
        }

        static void Require(bool condition,string message)
        { if (!condition) throw new InvalidOperationException(message); }

        [Serializable] class Report
        {
            public bool passed, localOnly, hiddenAtLowest, menuMatchesInput;
            public string validatedAtUtc,scope;
            public float outerDiameter,whiteAndObservationDiameter,step,maximumHeight,ringMaximumAxialDepth,ringMinimumRadius;
            public Vector3 spawn,ringCenter,ringAxis;
        }
    }
}
