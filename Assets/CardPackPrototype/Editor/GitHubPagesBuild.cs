#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace CardOpen.Editor
{
    public static class GitHubPagesBuild
    {
        private const string OutputFolderName = "WebBuild";

        [MenuItem("CardOpen/Build/WebGL for GitHub Pages")]
        public static void Build()
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();

            if (scenes.Length == 0)
                throw new InvalidOperationException("No enabled scenes were found in Build Settings.");

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string outputPath = Path.Combine(projectRoot, OutputFolderName);
            BuildReport report = BuildPipeline.BuildPlayer(scenes, outputPath, BuildTarget.WebGL, BuildOptions.None);

            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException("WebGL build failed. Check the Unity Console for details.");

            PublishRootIndex(projectRoot, outputPath);
            AssetDatabase.Refresh();
            Debug.Log($"GitHub Pages WebGL build completed: {outputPath}");
        }

        private static void PublishRootIndex(string projectRoot, string outputPath)
        {
            string generatedIndexPath = Path.Combine(outputPath, "index.html");
            if (!File.Exists(generatedIndexPath))
                throw new FileNotFoundException("Unity did not generate the WebGL index.", generatedIndexPath);

            string html = File.ReadAllText(generatedIndexPath);
            RemoveStaleBuildFiles(outputPath, html);
            html = html.Replace("href=\"TemplateData/", "href=\"WebBuild/TemplateData/");
            html = html.Replace("var buildUrl = \"Build\";", "var buildUrl = \"WebBuild/Build\";");
            html = html.Replace("streamingAssetsUrl: \"StreamingAssets\"", "streamingAssetsUrl: \"WebBuild/StreamingAssets\"");
            html = InjectVisibleErrorHandling(html);
            File.WriteAllText(Path.Combine(projectRoot, "index.html"), html);
        }

        private static void RemoveStaleBuildFiles(string outputPath, string generatedIndex)
        {
            string buildFolder = Path.Combine(outputPath, "Build");
            if (!Directory.Exists(buildFolder)) return;

            string[] staleExtensions = { ".data", ".data.br", ".data.gz", ".wasm", ".wasm.br", ".wasm.gz",
                ".framework.js", ".framework.js.br", ".framework.js.gz" };
            foreach (string filePath in Directory.GetFiles(buildFolder))
            {
                string fileName = Path.GetFileName(filePath);
                if (!fileName.StartsWith("WebBuild", StringComparison.OrdinalIgnoreCase)) continue;
                if (!staleExtensions.Any(extension => fileName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
                    continue;
                if (!generatedIndex.Contains(fileName))
                    File.Delete(filePath);
            }
        }

        private static string InjectVisibleErrorHandling(string html)
        {
            const string bodyMarker = "  <body>";
            const string diagnosticElement =
                "\n    <pre id=\"cardopen-diagnostics\" style=\"display:none;position:fixed;z-index:99999;left:12px;right:12px;top:12px;max-height:45vh;overflow:auto;margin:0;padding:14px;border:2px solid #ff6b6b;border-radius:8px;background:rgba(12,12,18,.94);color:#fff;font:14px/1.45 monospace;white-space:pre-wrap\"></pre>";
            if (!html.Contains("cardopen-diagnostics"))
                html = html.Replace(bodyMarker, bodyMarker + diagnosticElement);

            const string canvasMarker = "      var canvas = document.querySelector(\"#unity-canvas\");";
            const string diagnosticsScript =
                "      window.cardOpenGameReady = false;\n" +
                "      var diagnostics = document.querySelector(\"#cardopen-diagnostics\");\n" +
                "      function showDiagnostic(message) {\n" +
                "        diagnostics.style.display = \"block\";\n" +
                "        diagnostics.textContent += (diagnostics.textContent ? \"\\n\" : \"\") + String(message);\n" +
                "      }\n" +
                "      window.addEventListener(\"error\", function(event) { showDiagnostic(event.message || event.error); });\n" +
                "      window.addEventListener(\"unhandledrejection\", function(event) { showDiagnostic(event.reason); });\n\n";
            if (!html.Contains("function showDiagnostic"))
                html = html.Replace(canvasMarker, diagnosticsScript + canvasMarker);

            const string configMarker = "        showBanner: unityShowBanner,";
            const string configDiagnostics =
                "\n        printErr: function(message) { console.error(message); showDiagnostic(\"Unity error: \" + message); }," +
                "\n        errorHandler: function(error, url, line) { showDiagnostic(\"Unity runtime error: \" + error); return true; },";
            if (!html.Contains("printErr: function(message)"))
                html = html.Replace(configMarker, configMarker + configDiagnostics);

            const string loadedMarker = "                document.querySelector(\"#unity-loading-bar\").style.display = \"none\";";
            const string readyTimeout =
                "\n                setTimeout(function() { if (!window.cardOpenGameReady) showDiagnostic(\"Unity loaded, but game initialization did not complete.\"); }, 8000);";
            if (!html.Contains("game initialization did not complete"))
                html = html.Replace(loadedMarker, loadedMarker + readyTimeout);

            html = html.Replace(
                "                alert(message);",
                "                showDiagnostic(\"Unity failed to start: \" + message);");
            return html;
        }
    }
}
#endif
