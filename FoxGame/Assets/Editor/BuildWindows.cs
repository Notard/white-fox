using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

/// <summary>
/// Windows 64비트 빌드. 메뉴 FoxGame/Build Windows 또는
/// Unity.exe -batchmode -projectPath FoxGame -executeMethod BuildWindows.Build -quit
/// 결과: (프로젝트 상위 폴더)/Build/WhiteFox/WhiteFox.exe
/// </summary>
public static class BuildWindows
{
    [MenuItem("FoxGame/Build Windows")]
    public static void Build()
    {
        string root = Directory.GetParent(Directory.GetParent(Application.dataPath).FullName).FullName;
        string exe = Path.Combine(root, "Build", "WhiteFox", "WhiteFox.exe");

        PlayerSettings.SetScriptingBackend(UnityEditor.Build.NamedBuildTarget.Standalone, ScriptingImplementation.Mono2x);

        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            // Build Settings 의 씬 순서 그대로 (Menu 가 첫 화면, Main 이 게임)
            scenes = System.Array.ConvertAll(EditorBuildSettings.scenes, s => s.path),
            locationPathName = exe,
            target = BuildTarget.StandaloneWindows64,
            options = BuildOptions.None,
        });

        var s = report.summary;
        Debug.Log($"[BuildWindows] {s.result} — {exe} ({s.totalSize / (1024 * 1024)} MB, {s.totalErrors} errors)");
        if (Application.isBatchMode && s.result != BuildResult.Succeeded) EditorApplication.Exit(1);
    }
}
