using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

public static class BuildWebGL
{
    [MenuItem("FoxGame/Build WebGL")]
    public static void Build()
    {
        string root = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string output = Path.Combine(root, "Build", "WebGL");
        // GitHub Pages does not configure Content-Encoding for Unity build files.
        PlayerSettings.WebGL.compressionFormat = WebGLCompressionFormat.Gzip;
        PlayerSettings.WebGL.decompressionFallback = true;
        PlayerSettings.WebGL.dataCaching = true;
        PlayerSettings.runInBackground = true;
        PlayerSettings.defaultWebScreenWidth = 1280;
        PlayerSettings.defaultWebScreenHeight = 720;
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = EditorBuildSettings.scenes.Where(s => s.enabled).Select(s => s.path).ToArray(),
            locationPathName = output,
            target = BuildTarget.WebGL,
            options = BuildOptions.None,
        });
        Debug.Log($"[BuildWebGL] {report.summary.result} — {output} ({report.summary.totalSize / (1024 * 1024)} MB, {report.summary.totalErrors} errors)");
        if (report.summary.result == BuildResult.Succeeded)
            File.WriteAllText(Path.Combine(output, ".nojekyll"), "");
        if (Application.isBatchMode && report.summary.result != BuildResult.Succeeded)
            EditorApplication.Exit(1);
    }
}
