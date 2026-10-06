using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

/// <summary>예제 스테이지 13~16 에서 장치가 게임 안에서 실제로 동작하는지 (+ 화면 캡처).</summary>
public class MechanismPlayTests
{
    static readonly Vector2Int Up = Vector2Int.up, Down = Vector2Int.down, Left = Vector2Int.left, Right = Vector2Int.right;

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

    IEnumerator Moves(params Vector2Int[] dirs)
    {
        foreach (var d in dirs) yield return Move(d);
    }

    static Vector2Int C(int x, int y) => new(x, y);

    IEnumerator ExpectClear()
    {
        Assert.IsFalse(fox.HasFallen);
        Assert.AreEqual(game.Total, game.Collected);
        yield return new WaitForSeconds(0.4f);
        Assert.AreEqual(GameState.Cleared, game.State);
    }

    [UnityTest]
    public IEnumerator LeverRaisesBridge()
    {
        yield return Load("레버와 돌다리");
        Assert.IsNotNull(GameObject.Find("Lever_0_2"), "레버 모습");
        yield return new WaitForSeconds(0.2f);
        TestUtil.Capture("mech_lever_before.png");
        yield return Move(Up);                    // (0,1)
        yield return Move(Up);                    // 레버 (0,2) 당기기 — 제자리
        Assert.AreEqual(C(0, 1), fox.Cell);
        Assert.IsTrue(board.State.ChannelOn(1));
        Assert.AreEqual(2, board.State.turn, "걷기 1턴 + 당기기 1턴");
        yield return new WaitForSeconds(0.7f);
        TestUtil.Capture("mech_lever_after.png");
        yield return Moves(Right, Right, Right, Right, Right);   // 다리를 건너 (5,1)
        Assert.AreEqual(C(5, 1), fox.Cell);
        yield return Moves(Up, Down, Down);
        yield return ExpectClear();
    }

    [UnityTest]
    public IEnumerator BlockOnPlateHoldsBridge()
    {
        yield return Load("누름판과 상자");
        yield return Move(Down);                  // 상자를 누름판 (0,1) 위로
        Assert.IsTrue(board.State.blocks.Contains(C(0, 1)));
        Assert.AreEqual(C(0, 2), fox.Cell);
        Assert.IsTrue(board.State.ChannelOn(1), "상자가 누름판을 누름");
        yield return new WaitForSeconds(0.7f);
        TestUtil.Capture("mech_plate.png");
        yield return Moves(Right, Down, Right, Right, Right, Right);   // (5,1)
        Assert.AreEqual(C(5, 1), fox.Cell);
        yield return Moves(Up, Up, Down, Down, Down);
        yield return ExpectClear();
    }

    [UnityTest]
    public IEnumerator SteppingOffPlateDropsFoxFromBridge()
    {
        yield return Load("누름판과 상자");
        // 상자를 밀지 않고 여우가 직접 누름판에 섰다가 다리로 가면 다리가 사라진다
        yield return Moves(Right, Down, Down, Left);   // (1,3)→(1,2)→(1,1)→(0,1) 누름판
        Assert.AreEqual(C(0, 1), fox.Cell);
        Assert.IsTrue(board.State.ChannelOn(1));
        yield return Move(Right);                 // (1,1): 누름판에서 내려옴 → 다리 꺼짐 (아직 땅 위)
        Assert.IsFalse(board.State.ChannelOn(1));
        yield return Move(Right);                 // 꺼진 다리 (2,1) → 떨어짐
        Assert.IsTrue(fox.HasFallen);
    }

    [UnityTest]
    public IEnumerator PushBlockIntoHoleThenCross()
    {
        yield return Load("구멍 메우기");
        yield return Move(Right);                 // (1,1)
        yield return Move(Right);                 // 상자를 (3,1) 구멍으로 → 메움
        Assert.AreEqual(C(2, 1), fox.Cell);
        Assert.IsTrue(board.State.filled.ContainsKey(C(3, 1)));
        yield return new WaitForSeconds(0.8f);
        TestUtil.Capture("mech_fill.png");
        yield return Move(Right);                 // 메운 칸 (3,1)
        yield return Act(fox.TryJump);            // (4,1) 구멍을 넘어 (5,1)
        Assert.AreEqual(C(5, 1), fox.Cell);
        yield return Moves(Right, Up, Down, Down);
        yield return ExpectClear();
    }

    [UnityTest]
    public IEnumerator LeverRaisesLiftToPlateau()
    {
        yield return Load("오르내리는 땅");
        yield return Move(Left);                  // (1,0)
        yield return Move(Left);                  // 레버 (0,0) 당기기
        Assert.AreEqual(3, MoveRules.Floor(board.Map, board.State, C(2, 3)));
        yield return Moves(Right, Up, Up);        // (2,2)
        yield return Act(fox.TryJump);            // 승강 땅 (2,3), 3층
        Assert.AreEqual(C(2, 3), fox.Cell);
        yield return new WaitForSeconds(0.9f);
        Assert.AreEqual(board.CellToWorld(C(2, 3)).y, fox.transform.position.y, 0.02f, "여우가 승강 땅 높이에");
        TestUtil.Capture("mech_lift.png");
        yield return Move(Up);                    // (2,4) 4층 보석
        yield return Moves(Left, Left, Right, Right, Right, Right);
        yield return ExpectClear();
    }
}
