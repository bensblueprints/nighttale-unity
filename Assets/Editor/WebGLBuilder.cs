using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace NightTale.Editor
{
    /// <summary>
    /// Headless WebGL build. Run from the command line:
    ///   Unity -batchmode -quit -projectPath <repo> -executeMethod NightTale.Editor.WebGLBuilder.Build
    /// Creates the Main scene (with NightTaleBootstrap attached) programmatically,
    /// so no scene wiring is required before the first build.
    /// </summary>
    public static class WebGLBuilder
    {
        public static void Build()
        {
            const string scenePath = "Assets/Scenes/Main.unity";
            const string outDir = "Builds/WebGL";

            // Ensure the scene exists with the bootstrap component attached.
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var go = new GameObject("NightTaleBootstrap");
            var bootstrap = go.AddComponent<NightTale.NightTaleBootstrap>();
            bootstrap.apiBaseUrl = "https://play.nighttalegames.com";

            System.IO.Directory.CreateDirectory("Assets/Scenes");
            EditorSceneManager.SaveScene(scene, scenePath);

            // Player settings.
            PlayerSettings.companyName = "NightTaleGames";
            PlayerSettings.productName = "NightTale";
            PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;

            var report = BuildPipeline.BuildPlayer(
                new[] { scenePath },
                outDir,
                BuildTarget.WebGL,
                BuildOptions.None);

            if (report.summary.result != UnityEditor.Build.Reporting.BuildResult.Succeeded)
            {
                Debug.LogError("WebGL build FAILED: " + report.summary.result);
                EditorApplication.Exit(1);
            }
            Debug.Log("WebGL build SUCCEEDED -> " + outDir);
        }
    }
}
