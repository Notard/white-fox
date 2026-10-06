using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>보석 획득 → 클리어, 낙하 → 게임 오버, 재시작 흐름 확인 (+ 확인용 스크린샷).</summary>
public class GameFlowTests
{
    FoxController fox;
    GameManager game;

    static readonly Vector2Int Up = Vector2Int.up, Down = Vector2Int.down,
        Left = Vector2Int.left, Right = Vector2Int.right;

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        TestUtil.FreshSave();
        StageProgress.Current = 0;
        yield return SceneManager.LoadSceneAsync("Main");
        yield return null;
        fox = Object.FindAnyObjectByType<FoxController>();
        game = Object.FindAnyObjectByType<GameManager>();
        fox.InputEnabled = false;
    }

    IEnumerator Move(Vector2Int dir)
    {
        fox.TryMove(dir);
        yield return WaitIdle();
    }

    IEnumerator WaitIdle()
    {
        float t = 0;
        while (fox.IsBusy && !fox.HasFallen && t < 5f) { t += Time.deltaTime; yield return null; }
    }

    [UnityTest]
    public IEnumerator CollectingAllGemsClears()
    {
        Assert.AreEqual(4, game.Total);
        Assert.AreEqual(4, GameObject.FindObjectsByType<Gem>().Length);
        yield return Move(Right);   // (1,0)
        yield return Move(Right);   // (2,0) 보석 1
        Assert.AreEqual(1, game.Collected);
        yield return Move(Up);      // (2,1)
        yield return Move(Up);      // (2,2)
        yield return Move(Right);   // (3,2) 보석 2
        yield return new WaitForSeconds(0.1f);
        TestUtil.Capture("game_play.png");
        yield return Move(Up);      // (3,3) 보석 3
        Assert.AreEqual(3, game.Collected);
        Assert.AreEqual(GameState.Playing, game.State);
        yield return Move(Down);    // (3,2)
        yield return Move(Left);    // (2,2) 왼쪽을 바라봄
        fox.TryJump();              // 구멍을 넘어 (0,2)
        yield return WaitIdle();
        yield return Move(Up);      // (0,3) 보석 4
        Assert.AreEqual(4, game.Collected);
        Assert.AreEqual(GameState.Cleared, game.State);
        yield return new WaitForSeconds(2.0f);
        TestUtil.Capture("game_clear.png");
    }

    /// <summary>씬을 다시 불러온 뒤 새 씬의 오브젝트가 생길 때까지 기다렸다가 다시 잡는다.</summary>
    IEnumerator Reacquire()
    {
        var old = game;
        float t = 0;
        while ((old != null || Object.FindAnyObjectByType<GameManager>() == null) && t < 3f)
        {
            t += Time.unscaledDeltaTime;
            yield return null;
        }
        yield return null;
        fox = Object.FindAnyObjectByType<FoxController>();
        game = Object.FindAnyObjectByType<GameManager>();
        fox.InputEnabled = false;
    }

    [UnityTest]
    public IEnumerator ClearLeadsToNextStageWithItsOwnSize()
    {
        Assert.GreaterOrEqual(game.StageCount, 3);
        yield return Move(Right); yield return Move(Right);
        yield return Move(Up); yield return Move(Up); yield return Move(Right);
        yield return Move(Up); yield return Move(Down); yield return Move(Left);
        fox.TryJump(); yield return WaitIdle();
        yield return Move(Up);
        Assert.AreEqual(GameState.Cleared, game.State);
        Assert.IsFalse(game.IsLastStage);

        game.Continue();                                   // 스테이지 2 (5×5, 보석 4개)
        yield return Reacquire();
        var board = Object.FindAnyObjectByType<Board>();
        Assert.AreEqual(1, game.StageIndex);
        Assert.AreEqual(5, board.Width);
        Assert.AreEqual(5, board.Height);
        Assert.AreEqual(4, game.Total);                    // G.O.G / O.G.O / S...G
        Assert.AreEqual(game.Map.StartCell, fox.Cell);
        yield return new WaitForSeconds(0.2f);
        TestUtil.Capture("stage_02.png");

        StageProgress.Current = 2;                         // 스테이지 3 (6×4)
        game.Restart();
        yield return Reacquire();
        board = Object.FindAnyObjectByType<Board>();
        Assert.AreEqual(6, board.Width);
        Assert.AreEqual(4, board.Height);
        yield return new WaitForSeconds(0.2f);
        TestUtil.Capture("stage_03.png");

        StageProgress.Current = game.StageCount - 1;       // 마지막 스테이지
        game.Restart();
        yield return Reacquire();
        Assert.IsTrue(game.IsLastStage);
        game.Continue();                                   // 마지막 다음은 엔딩 (메뉴 씬)
        yield return TestUtil.WaitForScene(SceneFlow.MenuScene);
        Assert.AreEqual(MenuPage.Ending, Object.FindAnyObjectByType<MenuController>().Page);
    }

    static int StageIndexByName(string name)
    {
        for (int i = 0; i < StageLibrary.Count; i++)
            if (StageLibrary.Load(i).name == name) return i;
        Assert.Fail($"스테이지 '{name}' 이 없습니다");
        return -1;
    }

    [UnityTest]
    public IEnumerator ClimbingFloors()
    {
        // 언덕 오르기 (5×5)          층
        //   y=4  G . # . G          3 3 2 1 3
        //   y=3  . . . . .          3 2 2 1 1
        //   y=2  . . G . .          2 2 3 1 1
        //   y=1  . O . . .          1 1 2 1 1
        //   y=0  S . . . .          1 1 1 1 1
        StageProgress.Current = StageIndexByName("언덕 오르기");
        game.Restart();
        yield return Reacquire();
        var board = Object.FindAnyObjectByType<Board>();
        float fh = board.FloorHeight;

        yield return Move(Up);       // (0,1) 1층
        yield return Move(Up);       // (0,2) 2층: 걸어서 1층 오름
        Assert.AreEqual(new Vector2Int(0, 2), fox.Cell);
        Assert.AreEqual(fh, fox.transform.position.y, 0.01f);
        yield return Move(Up);       // (0,3) 3층
        yield return Move(Right);    // (1,3) 2층: 내려감
        yield return Move(Right);    // (2,3) 2층
        yield return Move(Right);    // (3,3) 1층
        yield return Move(Right);    // (4,3) 1층
        yield return Move(Up);       // (4,4) 3층: 걸어서 2층은 못 오름 → 제자리
        Assert.AreEqual(new Vector2Int(4, 3), fox.Cell);
        Assert.AreEqual(Up, fox.Facing);
        Assert.IsFalse(fox.HasFallen);

        int before = game.Collected;
        fox.TryJump();               // 바로 앞 (4,4) 3층으로 뛰어오름 (+2층)
        yield return WaitIdle();
        Assert.AreEqual(new Vector2Int(4, 4), fox.Cell);
        Assert.AreEqual(2 * fh, fox.transform.position.y, 0.01f);
        Assert.AreEqual(before + 1, game.Collected);
        yield return new WaitForSeconds(0.3f);
        TestUtil.Capture("stage_floors.png");
    }

    [UnityTest]
    public IEnumerator WheelZoomMovesCameraToFoxAndBack()
    {
        var cam = Camera.main;
        var zoom = cam.GetComponent<CameraZoom>();
        Assert.IsNotNull(zoom, "게임 카메라에 CameraZoom 이 있어야 한다");
        yield return null;
        float far = Vector3.Distance(cam.transform.position, fox.transform.position);

        zoom.SetZoom(1);                                   // 끝까지 확대
        yield return new WaitForSeconds(1f);
        float near = Vector3.Distance(cam.transform.position, fox.transform.position);
        Assert.Less(near, far * 0.75f, "확대하면 여우에게 다가간다");
        TestUtil.Capture("zoom_in.png");

        yield return Move(Right);                          // 확대된 채로 여우를 따라간다
        yield return new WaitForSeconds(0.6f);
        Assert.AreEqual(near, Vector3.Distance(cam.transform.position, fox.transform.position), 0.3f);

        zoom.SetZoom(0);                                   // 다시 보드 전체
        yield return new WaitForSeconds(1f);
        Assert.AreEqual(0, zoom.Current, 0.01f);
    }

    [UnityTest]
    public IEnumerator FallingIsGameOver()
    {
        yield return Move(Left);
        yield return new WaitForSeconds(fox.fallDuration + 0.8f);
        Assert.AreEqual(GameState.GameOver, game.State);
        TestUtil.Capture("game_over.png");
    }

    [UnityTest]
    public IEnumerator RestartResetsEverything()
    {
        yield return Move(Right);
        yield return Move(Right);   // 보석 1
        Assert.AreEqual(1, game.Collected);
        game.Restart();
        yield return null;
        yield return null;
        fox = Object.FindAnyObjectByType<FoxController>();
        game = Object.FindAnyObjectByType<GameManager>();
        Assert.AreEqual(0, game.Collected);
        Assert.AreEqual(GameState.Playing, game.State);
        Assert.AreEqual(new Vector2Int(0, 0), fox.Cell);
    }
}
