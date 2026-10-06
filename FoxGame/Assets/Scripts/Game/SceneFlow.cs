using UnityEngine;
using UnityEngine.SceneManagement;

public enum MenuPage
{
    Main,
    StageSelect,
    Settings,
    Ending,
}

/// <summary>
/// 화면 이동은 모두 여기서. Menu 씬(메인·스테이지 선택·설정·엔딩) ↔ Main 씬(게임).
/// 게임 종료는 메인 화면의 종료 버튼에서만 부른다.
/// </summary>
public static class SceneFlow
{
    public const string MenuScene = "Menu";
    public const string GameScene = "Main";

    /// <summary>Menu 씬이 열릴 때 보여 줄 화면.</summary>
    public static MenuPage NextMenuPage { get; private set; } = MenuPage.Main;

    public static void GoMenu(MenuPage page = MenuPage.Main)
    {
        Time.timeScale = 1;
        NextMenuPage = page;
        SceneManager.LoadScene(MenuScene);
    }

    public static void PlayStage(int index)
    {
        Time.timeScale = 1;
        StageProgress.Current = index;
        SceneManager.LoadScene(GameScene);
    }

    /// <summary>Menu 씬이 페이지를 읽은 뒤 기본값으로 되돌린다.</summary>
    public static MenuPage TakeMenuPage()
    {
        var p = NextMenuPage;
        NextMenuPage = MenuPage.Main;
        return p;
    }

    public static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>이어하기: 열린 스테이지 중 아직 깨지 않은 가장 앞 스테이지. 모두 깼으면 -1.</summary>
    public static int ContinueStage()
    {
        var save = SaveData.Current;
        int open = Mathf.Min(save.unlocked, StageLibrary.Count);
        for (int i = 0; i < open; i++)
            if (!save.HasCleared(SaveData.StageId(StageLibrary.Load(i)))) return i;
        return -1;
    }
}
