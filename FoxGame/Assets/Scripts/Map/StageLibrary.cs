using System;
using System.Linq;
using UnityEngine;

/// <summary>
/// 스테이지 목록. Assets/Resources/Stages/stage_NN.json 을 이름 순서대로 읽는다.
/// 파일은 맵툴(FoxGame/맵툴)에서만 만들고 고친다.
/// </summary>
public static class StageLibrary
{
    public const string ResourceFolder = "Stages";
    public const string AssetFolder = "Assets/Resources/" + ResourceFolder;

    static TextAsset[] cache;

    static TextAsset[] Files
    {
        get
        {
            if (cache == null)
                cache = Resources.LoadAll<TextAsset>(ResourceFolder)
                    .OrderBy(t => t.name, StringComparer.Ordinal)
                    .ToArray();
            return cache;
        }
    }

    public static int Count => Files.Length;

    public static MapData Load(int index)
    {
        if (Count == 0) throw new InvalidOperationException($"스테이지가 없습니다. {AssetFolder} 에 맵툴로 스테이지를 만들어 주세요.");
        var file = Files[Mathf.Clamp(index, 0, Count - 1)];
        return MapJson.FromJson(file.text, file.name + ".json");
    }

    /// <summary>에디터에서 스테이지 파일을 바꾼 뒤 다시 읽게 한다.</summary>
    public static void ClearCache() => cache = null;

    public static string FileName(int index) => $"stage_{index + 1:00}";
}

/// <summary>지금 플레이 중인 스테이지 번호 (0부터). 씬을 다시 불러와도 유지된다.</summary>
public static class StageProgress
{
    public static int Current { get; set; }

    public static bool IsLast => Current >= StageLibrary.Count - 1;

    // 실행 인자 "-stage N" 으로 N번째 스테이지부터 시작 (빌드 확인용)
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void ApplyCommandLine()
    {
        var args = Environment.GetCommandLineArgs();
        int i = Array.IndexOf(args, "-stage");
        if (i >= 0 && i + 1 < args.Length && int.TryParse(args[i + 1], out int n))
            Current = Mathf.Max(0, n - 1);
    }

#if UNITY_EDITOR
    public const string PlayStageKey = "FoxGame.PlayStage";

    // 맵툴의 "이 스테이지로 플레이"가 지정한 스테이지로 시작
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void ApplyEditorRequest()
    {
        int requested = UnityEditor.SessionState.GetInt(PlayStageKey, -1);
        Current = Mathf.Max(0, requested);
        UnityEditor.SessionState.EraseInt(PlayStageKey);
        StageLibrary.ClearCache();
    }
#endif
}
