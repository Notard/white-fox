using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>예제 스테이지 10~12 에서 적·발판·연출이 게임 안에서 실제로 동작하는지 (+ 화면 캡처).</summary>
public class MoverPlayTests
{
    static readonly Vector2Int Up = Vector2Int.up, Down = Vector2Int.down, Right = Vector2Int.right;

    FoxController fox;
    GameManager game;
    Board board;

    IEnumerator Load(string stageName)
    {
        TestUtil.FreshSave();
        int index = -1;
        for (int i = 0; i < StageLibrary.Count; i++)
            if (StageLibrary.Load(i).name == stageName) index = i;
        Assert.GreaterOrEqual(index, 0, $"스테이지 '{stageName}' 이 없습니다");
        StageProgress.Current = index;
        yield return SceneManager.LoadSceneAsync(SceneFlow.GameScene);
        yield return null;
        yield return null;
        fox = Object.FindAnyObjectByType<FoxController>();
        game = Object.FindAnyObjectByType<GameManager>();
        board = Object.FindAnyObjectByType<Board>();
        fox.InputEnabled = false;
    }

    IEnumerator Act(System.Func<bool> action)
    {
        action();
        float t = 0;
        while (fox.IsBusy && !fox.HasFallen && t < 6f) { t += Time.deltaTime; yield return null; }
    }

    IEnumerator Move(Vector2Int dir) => Act(() => fox.TryMove(dir));

    static Vector2Int C(int x, int y) => new(x, y);

    [UnityTest]
    public IEnumerator WalkingIntoSnowballFails()
    {
        yield return Load("눈덩이 길");
        Assert.IsNotNull(Object.FindAnyObjectByType<TurnSystem>(), "TurnSystem 이 씬에 있어야 함");
        yield return new WaitForSeconds(0.2f);
        TestUtil.Capture("movers_snowball.png");
        yield return Move(Right);                 // (1,2), 턴 1: 눈덩이 (2,1)
        Assert.AreEqual(1, board.State.turn);
        Assert.IsFalse(fox.HasFallen);
        yield return Move(Right);                 // (2,2) 로 들어서는데 눈덩이도 (2,2) 로 → 부딪힘
        Assert.IsTrue(fox.WasHit);
        yield return new WaitForSeconds(0.5f);
        TestUtil.Capture("movers_hit.png");
        yield return new WaitForSeconds(1.0f);
        Assert.AreEqual(GameState.GameOver, game.State);
    }

    [UnityTest]
    public IEnumerator WaitThenRidePlatformAndClear()
    {
        yield return Load("떠다니는 발판");
        yield return Move(Right);                 // (1,1), 턴 1: 발판은 (3,1)
        yield return Act(fox.TryWait);            // 턴 2: 발판이 (2,1) 로 옴
        Assert.AreEqual(2, board.State.turn);
        Assert.AreEqual(C(1, 1), fox.Cell);
        yield return Move(Right);                 // 발판에 올라 (3,1) 로 실려 감
        Assert.AreEqual(C(3, 1), fox.Cell);
        Assert.IsFalse(fox.HasFallen);
        yield return new WaitForSeconds(0.2f);
        TestUtil.Capture("movers_platform.png");
        yield return Move(Right);                 // (4,1)
        yield return Move(Right);                 // (5,1)
        yield return Move(Up);                    // (5,2) 보석
        yield return Move(Down);
        yield return Move(Down);                  // (5,0) 보석 → 클리어
        Assert.AreEqual(2, game.Collected);
        yield return new WaitForSeconds(0.6f);
        Assert.AreEqual(GameState.Cleared, game.State);
        var zoom = Camera.main.GetComponent<CameraZoom>();
        Assert.Greater(zoom.Target, 0.8f, "클리어하면 카메라가 여우 쪽으로 다가감");
        yield return new WaitForSeconds(0.6f);
        TestUtil.Capture("movers_clear_zoom.png");
    }

    [UnityTest]
    public IEnumerator OwlPatrolsOverHoles()
    {
        yield return Load("부엉이 순찰");
        var owl = GameObject.Find("owl_0");
        Assert.IsNotNull(owl, "부엉이 모습이 있어야 함");
        var before = owl.transform.position;
        yield return Act(fox.TryWait);
        Assert.Greater(owl.transform.position.x - before.x, board.cellSize * 0.8f, "기다리면 부엉이가 한 칸 옆으로");
        yield return new WaitForSeconds(0.2f);
        TestUtil.Capture("movers_owl.png");
    }

    [UnityTest]
    public IEnumerator FootprintsAndDustAppear()
    {
        yield return Load("눈덩이 길");             // 시작 (0,2), 왼쪽 세로줄은 눈덩이와 멀다
        Assert.IsNotNull(fox.GetComponent<FoxTrail>(), "여우에 FoxTrail 이 있어야 함");
        yield return Move(Down);                  // (0,1)
        Assert.IsNotNull(GameObject.Find("Footprint"), "걸으면 발자국");
        yield return Move(Up);                    // (0,2), 위를 봄
        yield return Act(fox.TryJump);            // (0,4) 에 착지
        Assert.AreEqual(C(0, 4), fox.Cell);
        Assert.IsNotNull(GameObject.Find("Dust(Clone)"), "착지하면 먼지");
        yield return new WaitForSeconds(0.05f);
        TestUtil.Capture("movers_trail.png");
    }
}
