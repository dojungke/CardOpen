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

    }
}
#endif
