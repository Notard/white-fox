using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>
/// Main 씬의 기본 맵으로 여우 이동 규칙을 확인한다.
///   y=3  G . # G
///   y=2  . O . G
///   y=1  # . . O
///   y=0  S . G .
/// </summary>
public class FoxMovementTests
{
    FoxController fox;

    static readonly Vector2Int Up = Vector2Int.up, Down = Vector2Int.down,
        Left = Vector2Int.left, Right = Vector2Int.right;

    [UnitySetUp]
    public IEnumerator LoadScene()
    {
        TestUtil.FreshSave();
        StageProgress.Current = 0;   // 스테이지 1 (아래 맵)
        yield return SceneManager.LoadSceneAsync("Main");
        yield return null;
        fox = Object.FindAnyObjectByType<FoxController>();
        fox.InputEnabled = false;
        Assert.AreEqual(new Vector2Int(0, 0), fox.Cell);
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
    public IEnumerator WalksOneCell()
    {
        yield return Move(Right);
        Assert.AreEqual(new Vector2Int(1, 0), fox.Cell);
        Assert.IsFalse(fox.HasFallen);
        var board = Object.FindAnyObjectByType<Board>();
        Assert.Less(Vector3.Distance(fox.transform.position, board.CellToWorld(fox.Cell)), 0.01f);
    }

    [UnityTest]
    public IEnumerator ObstacleBlocks()
    {
        yield return Move(Up);                       // (0,1) 은 장애물
        Assert.AreEqual(new Vector2Int(0, 0), fox.Cell);
        Assert.IsFalse(fox.HasFallen);
        Assert.AreEqual(Up, fox.Facing);
    }

    [UnityTest]
    public IEnumerator FallsIntoHole()
    {
        bool fell = false;
        fox.Fell += () => fell = true;
        yield return Move(Right);
        yield return Move(Up);
        yield return Move(Up);                       // (1,2) 는 구멍
        Assert.IsTrue(fox.HasFallen);
        yield return new WaitForSeconds(fox.fallDuration + 0.2f);
        Assert.IsTrue(fell);
    }

    [UnityTest]
    public IEnumerator FallsOffTheEdge()
    {
        yield return Move(Left);                     // 맵 밖
        Assert.IsTrue(fox.HasFallen);
    }

    [UnityTest]
    public IEnumerator JumpsOverHole()
    {
        yield return Move(Right);   // (1,0)
        yield return Move(Up);      // (1,1)
        yield return Move(Right);   // (2,1)
        yield return Move(Up);      // (2,2)
        yield return Move(Right);   // (3,2)
        yield return Move(Left);    // (2,2) 왼쪽을 바라봄
        fox.TryJump();              // 구멍 (1,2) 를 넘어 (0,2) 에 착지
        yield return WaitIdle();
        Assert.AreEqual(new Vector2Int(0, 2), fox.Cell);
        Assert.IsFalse(fox.HasFallen);
        yield return Move(Up);
        Assert.AreEqual(new Vector2Int(0, 3), fox.Cell);
    }

    [UnityTest]
    public IEnumerator JumpOntoObstacleStaysInPlace()
    {
        yield return Move(Right);   // (1,0)
        yield return Move(Right);   // (2,0)
        yield return Move(Up);      // (2,1) 위를 바라봄
        fox.TryJump();              // (2,3) 은 장애물 → 제자리 점프
        yield return WaitIdle();
        Assert.AreEqual(new Vector2Int(2, 1), fox.Cell);
        Assert.IsFalse(fox.HasFallen);
    }

    [UnityTest]
    public IEnumerator JumpOffTheBoardFalls()
    {
        yield return Move(Right);   // (1,0)
        yield return Move(Right);   // (2,0)
        yield return Move(Up);      // (2,1)
        yield return Move(Up);      // (2,2)
        yield return Move(Up);      // (2,3) 은 장애물 → 제자리, 위를 바라봄
        Assert.AreEqual(new Vector2Int(2, 2), fox.Cell);
        fox.TryJump();              // (2,4) 는 맵 밖 → 떨어짐
        yield return WaitIdle();
        Assert.IsTrue(fox.HasFallen);
    }
}
