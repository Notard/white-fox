using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>예제 스테이지 6~9 에서 퍼즐 칸이 게임 안에서 실제로 동작하는지 (+ 화면 캡처).</summary>
public class PuzzlePlayTests
{
    static readonly Vector2Int Up = Vector2Int.up, Down = Vector2Int.down,
        Left = Vector2Int.left, Right = Vector2Int.right;

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

    IEnumerator Move(Vector2Int dir)
    {
        fox.TryMove(dir);
        yield return WaitIdle();
    }

    IEnumerator WaitIdle()
    {
        float t = 0;
        while (fox.IsBusy && !fox.HasFallen && t < 6f) { t += Time.deltaTime; yield return null; }
    }

    static Vector2Int C(int x, int y) => new(x, y);

    [UnityTest]
    public IEnumerator IceSlideCollectsGemOnTheWay()
    {
        yield return Load("미끄러운 연못");
        yield return Move(Right);
        yield return Move(Right);                 // (2,0)
        yield return Move(Up);                    // 얼음 (2,1) → (2,2) 보석 → (2,3) 장애물 앞에서 멈춤
        Assert.AreEqual(C(2, 3), fox.Cell);
        Assert.AreEqual(1, game.Collected);
        Assert.IsFalse(fox.HasFallen);
        yield return new WaitForSeconds(0.3f);
        TestUtil.Capture("puzzle_ice.png");
    }

    [UnityTest]
    public IEnumerator CrumbleCollapsesBehindFox()
    {
        yield return Load("무너지는 다리");
        yield return Move(Right);                 // (1,0)
        yield return Move(Up);                    // (1,1) 무너지는 칸
        yield return Move(Right);                 // (2,1) → (1,1) 무너짐
        Assert.IsTrue(board.State.crumbled.Contains(C(1, 1)));
        yield return new WaitForSeconds(0.6f);
        TestUtil.Capture("puzzle_crumble.png");
        yield return Move(Left);                  // 무너진 칸으로 → 떨어짐
        Assert.IsTrue(fox.HasFallen);
    }

    [UnityTest]
    public IEnumerator SwitchOpensDoorsToTreasure()
    {
        yield return Load("잠긴 보물");
        yield return Move(Left);
        yield return Move(Left);
        yield return Move(Left);                  // (1,0)
        yield return Move(Up);                    // (1,1)
        yield return Move(Right);                 // (2,1)
        yield return Move(Up);                    // (2,2) 닫힌 문 → 막힘
        Assert.AreEqual(C(2, 1), fox.Cell);
        Assert.IsFalse(board.State.doorsOpen);

        yield return Move(Down);                  // (2,0)
        yield return Move(Left);
        yield return Move(Left);                  // (0,0) 스위치 → 문 열림
        Assert.IsTrue(board.State.doorsOpen);
        yield return new WaitForSeconds(0.8f);
        TestUtil.Capture("puzzle_door.png");

        yield return Move(Right);
        yield return Move(Right);                 // (2,0)
        yield return Move(Up);                    // (2,1)
        yield return Move(Up);                    // (2,2) 열린 문
        yield return Move(Up);                    // (2,3)
        yield return Move(Up);                    // (2,4) 보석
        Assert.AreEqual(C(2, 4), fox.Cell);
        Assert.AreEqual(1, game.Collected);
    }

    [UnityTest]
    public IEnumerator StairsClimbToPlateau()
    {
        yield return Load("돌계단 언덕");
        yield return Move(Up);                    // (2,1)
        yield return Move(Up);                    // (2,2) 1층
        yield return Move(Up);                    // (2,3) 4층 → 막힘
        Assert.AreEqual(C(2, 2), fox.Cell);
        yield return Move(Left);                  // (1,2) 계단 2층
        yield return Move(Up);                    // (1,3) 4층: 계단에서는 높이 상관없이
        Assert.AreEqual(C(1, 3), fox.Cell);
        Assert.AreEqual(3 * board.FloorHeight, fox.transform.position.y, 0.01f);
        yield return new WaitForSeconds(0.3f);
        TestUtil.Capture("puzzle_stairs.png");
    }
}
