using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// 화면 흐름: 메인 → 스테이지 선택 → 게임(Ready → Playing → Clear/Fail) → 엔딩, 설정, 일시정지.
/// 모든 화면에서 메인으로 돌아올 수 있는지(막다른 화면 없음), 해금·기록 저장을 확인한다.
/// </summary>
public class MenuFlowTests
{
    static readonly Vector2Int Up = Vector2Int.up, Down = Vector2Int.down,
        Left = Vector2Int.left, Right = Vector2Int.right;

    [SetUp]
    public void SetUp() => TestUtil.FreshSave();

    [TearDown]
    public void TearDown()
    {
        Time.timeScale = 1;
        GameManager.ReadyDuration = 0;
    }

    IEnumerator OpenMenu(MenuPage page = MenuPage.Main)
    {
        SceneFlow.GoMenu(page);
        yield return TestUtil.WaitForScene(SceneFlow.MenuScene);
        yield return new WaitForSecondsRealtime(0.3f);
    }

    static MenuController Menu => Object.FindAnyObjectByType<MenuController>();
    static GameManager Game => Object.FindAnyObjectByType<GameManager>();
    static FoxController Fox => Object.FindAnyObjectByType<FoxController>();

    static IEnumerator Move(Vector2Int dir)
    {
        Fox.TryMove(dir);
        float t = 0;
        while (Fox.IsBusy && !Fox.HasFallen && t < 5f) { t += Time.deltaTime; yield return null; }
    }

    static IEnumerator Jump()
    {
        Fox.TryJump();
        float t = 0;
        while (Fox.IsBusy && !Fox.HasFallen && t < 5f) { t += Time.deltaTime; yield return null; }
    }

    /// <summary>스테이지 1 을 깨는 길 (FoxMovementTests 와 같은 맵).</summary>
    static IEnumerator ClearStage1()
    {
        yield return Move(Right); yield return Move(Right);
        yield return Move(Up); yield return Move(Up); yield return Move(Right);
        yield return Move(Up); yield return Move(Down); yield return Move(Left);
        yield return Jump();
        yield return Move(Up);
    }

    [UnityTest]
    public IEnumerator MainMenuStartsLockedAndCanGoEverywhere()
    {
        yield return OpenMenu();
        Assert.AreEqual(MenuPage.Main, Menu.Page);
        Assert.IsFalse(Menu.ContinueButton.interactable, "깬 스테이지가 없으면 이어하기는 꺼져 있다");
        TestUtil.Capture("menu_main.png");

        Menu.StartGame();
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.AreEqual(MenuPage.StageSelect, Menu.Page);
        Assert.IsTrue(Menu.StageButtons[0].interactable, "스테이지 1 은 처음부터 열림");
        for (int i = 1; i < Menu.StageButtons.Count; i++)
            Assert.IsFalse(Menu.StageButtons[i].interactable, $"스테이지 {i + 1} 은 잠김");
        TestUtil.Capture("menu_select_locked.png");

        Menu.PlayStage(1);                       // 잠긴 스테이지는 눌러도 시작하지 않음
        yield return null;
        Assert.AreEqual(SceneFlow.MenuScene, SceneManager.GetActiveScene().name);

        Menu.ShowPage(MenuPage.Main);
        Menu.ShowPage(MenuPage.Settings);
        yield return new WaitForSecondsRealtime(0.3f);
        TestUtil.Capture("menu_settings.png");
        Menu.ShowPage(MenuPage.Main);
        Assert.AreEqual(MenuPage.Main, Menu.Page);
    }

    [UnityTest]
    public IEnumerator ReadyThenPlayingThenClearRecordsAndUnlocks()
    {
        GameManager.ReadyDuration = 0.6f;
        yield return OpenMenu();
        Menu.StartGame();
        Menu.PlayStage(0);
        yield return TestUtil.WaitForScene(SceneFlow.GameScene);

        Assert.AreEqual(GameState.Ready, Game.State);
        Assert.IsFalse(Fox.InputEnabled, "Ready 동안은 움직일 수 없다");
        yield return new WaitForSeconds(0.1f);
        TestUtil.Capture("game_ready.png");
        yield return new WaitForSeconds(0.7f);
        Assert.AreEqual(GameState.Playing, Game.State);
        Fox.InputEnabled = false;

        yield return ClearStage1();
        Assert.AreEqual(GameState.Cleared, Game.State);
        Assert.IsTrue(Game.IsNewRecord);
        Assert.Greater(Game.Elapsed, 0f);
        Assert.AreEqual(2, SaveData.Current.unlocked, "스테이지 2 가 열림");
        Assert.IsTrue(SaveData.Current.BestTime(SaveData.StageId(Game.Map)).HasValue);
        yield return new WaitForSeconds(1.6f);
        TestUtil.Capture("game_clear.png");

        Game.ToStageSelect();                     // Clear → 스테이지 선택
        yield return TestUtil.WaitForScene(SceneFlow.MenuScene);
        yield return new WaitForSecondsRealtime(0.3f);
        Assert.AreEqual(MenuPage.StageSelect, Menu.Page);
        Assert.IsTrue(Menu.StageButtons[1].interactable);
        TestUtil.Capture("menu_select.png");

        Menu.ShowPage(MenuPage.Main);              // 이어하기 = 스테이지 2
        Assert.IsTrue(Menu.ContinueButton.interactable);
        Assert.AreEqual(1, SceneFlow.ContinueStage());
        Menu.ContinueGame();
        yield return TestUtil.WaitForScene(SceneFlow.GameScene);
        Assert.AreEqual(1, Game.StageIndex);
    }

    [UnityTest]
    public IEnumerator PauseStopsTimeAndCanGoHome()
    {
        SceneFlow.PlayStage(0);
        yield return TestUtil.WaitForScene(SceneFlow.GameScene);
        Fox.InputEnabled = false;
        yield return new WaitForSeconds(0.3f);
        Game.SetPaused(true);
        Assert.IsTrue(Game.IsPaused);
        Assert.AreEqual(0, Time.timeScale);
        float before = Game.Elapsed;
        yield return new WaitForSecondsRealtime(0.4f);
        Assert.AreEqual(before, Game.Elapsed, 1e-4, "일시정지 중에는 시간이 멈춘다");
        TestUtil.Capture("game_pause.png");

        Game.SetPaused(false);                    // 계속
        Assert.AreEqual(1, Time.timeScale);
        Game.SetPaused(true);
        Game.ToMain();                            // 메인으로
        yield return TestUtil.WaitForScene(SceneFlow.MenuScene);
        Assert.AreEqual(MenuPage.Main, Menu.Page);
        Assert.AreEqual(1, Time.timeScale);
    }

    [UnityTest]
    public IEnumerator FailQuitGoesToMain()
    {
        SceneFlow.PlayStage(0);
        yield return TestUtil.WaitForScene(SceneFlow.GameScene);
        Fox.InputEnabled = false;
        yield return Move(Left);                  // 맵 밖 → 떨어짐
        yield return new WaitForSeconds(Fox.fallDuration + 1f);
        Assert.AreEqual(GameState.GameOver, Game.State);
        Assert.AreEqual(1, SaveData.Current.unlocked, "실패하면 해금되지 않음");
        TestUtil.Capture("game_fail.png");
        Game.ToMain();                            // 그만하기
        yield return TestUtil.WaitForScene(SceneFlow.MenuScene);
        Assert.AreEqual(MenuPage.Main, Menu.Page);
    }

    [UnityTest]
    public IEnumerator LastStageClearShowsEndingThenMain()
    {
        // 마지막 스테이지까지 열어 두고, 마지막 스테이지의 클리어 결과 버튼("엔딩 보기")을 누른 것과 같은 동작
        var save = SaveData.Current;
        save.unlocked = StageLibrary.Count;
        for (int i = 0; i < StageLibrary.Count; i++) save.RecordClear(i, SaveData.StageId(StageLibrary.Load(i)), 10f + i);
        SceneFlow.PlayStage(StageLibrary.Count - 1);
        yield return TestUtil.WaitForScene(SceneFlow.GameScene);
        Assert.IsTrue(Game.IsLastStage);
        Game.Continue();
        yield return TestUtil.WaitForScene(SceneFlow.MenuScene);
        yield return new WaitForSecondsRealtime(1.2f);
        Assert.AreEqual(MenuPage.Ending, Menu.Page);
        TestUtil.Capture("menu_ending.png");
        Menu.ShowPage(MenuPage.Main);
        Assert.AreEqual(MenuPage.Main, Menu.Page);
        Assert.IsTrue(Menu.ContinueButton.interactable);
        Assert.AreEqual(-1, SceneFlow.ContinueStage(), "모두 깼으면 이어하기는 스테이지 선택으로");
    }

    [UnityTest]
    public IEnumerator ResetProgressLocksAgain()
    {
        SaveData.Current.RecordClear(0, SaveData.StageId(StageLibrary.Load(0)), 5f);
        SaveData.Current.SetVolume(0.3f);
        yield return OpenMenu(MenuPage.Settings);
        SaveData.ResetProgress();
        Assert.AreEqual(1, SaveData.Current.unlocked);
        Assert.AreEqual(0, SaveData.Current.records.Count);
        Assert.AreEqual(0.3f, SaveData.Current.volume, 1e-4, "음량은 남음");
    }
}
