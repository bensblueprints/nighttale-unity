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
            // Patch the default template's 960x600 landscape canvas to portrait (540x960).
            // NightTale's UI is a 1080x1920 portrait layout, so a landscape canvas clips
            // the picker/story lists. This keeps every build portrait without a custom template.
            var indexPath = System.IO.Path.Combine(outDir, "index.html");
            if (System.IO.File.Exists(indexPath))
            {
                var html = System.IO.File.ReadAllText(indexPath);
                html = html.Replace("width=960 height=600", "width=540 height=960");
                html = html.Replace("canvas.style.width = \"960px\"", "canvas.style.width = \"540px\"");
                html = html.Replace("canvas.style.height = \"600px\"", "canvas.style.height = \"960px\"");
                System.IO.File.WriteAllText(indexPath, html);
                Debug.Log("WebGL canvas patched to portrait (540x960)");
            }

            Debug.Log("WebGL build SUCCEEDED -> " + outDir);
        }
    }
}
