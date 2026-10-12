// SceneBuilder.cs (Editor only)
// Creates the URP pipeline asset, the tooth material and the playable scene "Assets/Scenes/EndoSim.unity".
// Run from the menu (SurgiFlow > Build Endo Scene) or headless:
//   Unity.exe -batchmode -quit -projectPath <path> -executeMethod SurgiFlow.Endo.Editor.SceneBuilder.BuildBatch
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace SurgiFlow.Endo.Editor
{
    public static class SceneBuilder
    {
        const string SettingsDir = "Assets/Settings", ScenePath = "Assets/Scenes/EndoSim.unity";
        const string MaterialPath = "Assets/SurgiFlow/Endo/Materials/ToothVertexColor.mat";

        [MenuItem("SurgiFlow/Build Endo Scene")]
        public static void Build()
        {
            EnsureUrp();
            var mat = EnsureMaterial();
            foreach (var n in new[] { "SurgiFlow/VertexColorLit", "SurgiFlow/VertexColorUnlit" }) AlwaysInclude(Shader.Find(n));

            Directory.CreateDirectory("Assets/Scenes");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var camGo = new GameObject("Main Camera") { tag = "MainCamera" };
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor; cam.backgroundColor = new Color(0.07f, 0.09f, 0.12f);
            cam.fieldOfView = 32f; cam.nearClipPlane = 0.05f; cam.farClipPlane = 60f;
            camGo.AddComponent<AudioListener>();
            camGo.transform.position = new Vector3(0, -1.3f, 6.4f);

            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional; light.intensity = 1.15f; light.shadows = LightShadows.None;
            lightGo.transform.rotation = Quaternion.Euler(35f, 160f, 0f);   // shines toward -z: the camera looks at the +z (cut) face

            var sessionGo = new GameObject("EndoSession");
            var session = sessionGo.AddComponent<EndoSession>();
            var so = new SerializedObject(session);
            so.FindProperty("toothMaterial").objectReferenceValue = mat;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            AssetDatabase.SaveAssets();
            Debug.Log("[SurgiFlow] Scene built: " + ScenePath);
        }

        public static void BuildBatch()
        {
            Build();
            EditorApplication.Exit(0);
        }

        static Material EnsureMaterial()
        {
            var existing = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (existing != null) return existing;
            Directory.CreateDirectory(Path.GetDirectoryName(MaterialPath));
            var shader = Shader.Find("SurgiFlow/VertexColorLit");
            if (shader == null) throw new System.Exception("Shader SurgiFlow/VertexColorLit not found (compile error in the .shader?)");
            var m = new Material(shader) { name = "ToothVertexColor" };
            AssetDatabase.CreateAsset(m, MaterialPath);
            return m;
        }

        /// <summary>Shaders found by Shader.Find at runtime must be in "Always Included Shaders" to survive a player build.</summary>
        static void AlwaysInclude(Shader shader)
        {
            if (shader == null) return;
            var gs = AssetDatabase.LoadAssetAtPath<Object>("ProjectSettings/GraphicsSettings.asset");
            var so = new SerializedObject(gs);
            var arr = so.FindProperty("m_AlwaysIncludedShaders");
            for (int i = 0; i < arr.arraySize; i++) if (arr.GetArrayElementAtIndex(i).objectReferenceValue == shader) return;
            arr.InsertArrayElementAtIndex(arr.arraySize);
            arr.GetArrayElementAtIndex(arr.arraySize - 1).objectReferenceValue = shader;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureUrp()
        {
            Directory.CreateDirectory(SettingsDir);
            string rpPath = SettingsDir + "/Prototype_URP.asset", rdPath = SettingsDir + "/Prototype_URP_Renderer.asset";
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(rpPath);
            if (rp == null)
            {
                var rd = ScriptableObject.CreateInstance<UniversalRendererData>();
                AssetDatabase.CreateAsset(rd, rdPath);
                rp = UniversalRenderPipelineAsset.Create(rd);
                // Light settings for an 8 GB laptop / RTX 4060: 4x MSAA, no HDR, no shadows needed (shader is unlit-shadow).
                rp.msaaSampleCount = 4; rp.supportsHDR = false; rp.shadowDistance = 0f;
                AssetDatabase.CreateAsset(rp, rpPath);
            }
            GraphicsSettings.defaultRenderPipeline = rp;
            int current = QualitySettings.GetQualityLevel();
            for (int i = 0; i < QualitySettings.names.Length; i++) { QualitySettings.SetQualityLevel(i, false); QualitySettings.renderPipeline = rp; }
            QualitySettings.SetQualityLevel(current, false);
            AssetDatabase.SaveAssets();
        }
    }
}
