using NUnit.Framework;
using UnityEngine;

/// <summary>퍼즐 칸 규칙: 얼음, 무너지는 칸, 스위치·문, 계단. 바닥(rows)·물건(items)·층(floors)을 따로 준다.</summary>
public class PuzzleTileTests
{
    static MapData Map(string[] rows, string[] items = null, string[] floors = null)
    {
        int w = rows[0].Length, h = rows.Length;
        var m = new MapData { width = w, height = h, rows = rows, items = items, floors = floors };
        if (items == null) m.items = Fill(w, h, '.');
        if (floors == null) m.FillFloors(1);
        return m;
    }

    static string[] Fill(int w, int h, char c)
    {
        var a = new string[h];
        for (int i = 0; i < h; i++) a[i] = new string(c, w);
        return a;
    }

    static readonly Vector2Int Up = Vector2Int.up, Right = Vector2Int.right, Left = Vector2Int.left;
    static Vector2Int C(int x, int y) => new(x, y);

    // ------------------------------------------------------------ 얼음
    [Test]
    public void IceSlidesUntilGround()
    {
        var m = Map(new[] { ".III." });
        var r = MoveRules.Walk(m, new PuzzleState(), C(0, 0), Right);
        Assert.AreEqual(MoveOutcome.Move, r.outcome);
        Assert.AreEqual(C(4, 0), r.cell);
        CollectionAssert.AreEqual(new[] { C(1, 0), C(2, 0), C(3, 0), C(4, 0) }, r.path);
    }

    [Test]
    public void IceStopsBeforeObstacleClosedDoorAndHigherCell()
    {
        var r = MoveRules.Walk(Map(new[] { ".II#" }), new PuzzleState(), C(0, 0), Right);
        Assert.AreEqual(C(2, 0), r.cell, "장애물 앞 얼음에서 멈춤");
        r = MoveRules.Walk(Map(new[] { ".IID" }), new PuzzleState(), C(0, 0), Right);
        Assert.AreEqual(C(2, 0), r.cell, "닫힌 문 앞에서 멈춤");
        r = MoveRules.Walk(Map(new[] { ".II." }, null, new[] { "1112" }), new PuzzleState(), C(0, 0), Right);
        Assert.AreEqual(C(2, 0), r.cell, "더 높은 칸 앞에서 멈춤");
    }

    [Test]
    public void IceSlidesIntoHole()
    {
        var r = MoveRules.Walk(Map(new[] { ".IIO" }), new PuzzleState(), C(0, 0), Right);
        Assert.AreEqual(MoveOutcome.Fall, r.outcome);
        Assert.AreEqual(C(3, 0), r.cell);
        r = MoveRules.Walk(Map(new[] { ".II" }), new PuzzleState(), C(0, 0), Right);
        Assert.AreEqual(MoveOutcome.Fall, r.outcome, "맵 밖으로 미끄러져 떨어짐");
    }

    [Test]
    public void JumpLandingOnIceKeepsSliding()
    {
        var r = MoveRules.Jump(Map(new[] { ".OII." }), new PuzzleState(), C(0, 0), Right);
        Assert.AreEqual(C(4, 0), r.cell);
        Assert.IsTrue(r.jumped);
    }

    // ------------------------------------------------------------ 무너지는 칸
    [Test]
    public void CrumbleBecomesHoleAfterLeaving()
    {
        var m = Map(new[] { ".C." });
        var s = new PuzzleState();
        var r = MoveRules.Walk(m, s, C(0, 0), Right);
        MoveRules.Apply(m, s, C(0, 0), r);
        Assert.AreEqual(CellType.Crumble, MoveRules.Kind(m, s, C(1, 0)), "올라서 있을 때는 그대로");
        r = MoveRules.Walk(m, s, C(1, 0), Right);
        var ch = MoveRules.Apply(m, s, C(1, 0), r);
        Assert.AreEqual(C(1, 0), ch.crumbled);
        Assert.AreEqual(CellType.Hole, MoveRules.Kind(m, s, C(1, 0)));
        Assert.AreEqual(MoveOutcome.Fall, MoveRules.Walk(m, s, C(2, 0), Left).outcome, "무너진 칸으로 가면 떨어짐");
    }

    [Test]
    public void BumpingOnCrumbleDoesNotCollapse()
    {
        var m = Map(new[] { "C#" });
        var s = new PuzzleState();
        var r = MoveRules.Walk(m, s, C(0, 0), Right);
        Assert.AreEqual(MoveOutcome.Blocked, r.outcome);
        Assert.IsNull(MoveRules.Apply(m, s, C(0, 0), r).crumbled);
    }

    // ------------------------------------------------------------ 스위치와 문
    [Test]
    public void SwitchOpensDoors()
    {
        var m = Map(new[] { "W.D." });
        var s = new PuzzleState();
        Assert.AreEqual(MoveOutcome.Blocked, MoveRules.Walk(m, s, C(1, 0), Right).outcome, "닫힌 문은 막힘");
        var r = MoveRules.Walk(m, s, C(1, 0), Left);
        Assert.IsTrue(MoveRules.Apply(m, s, C(1, 0), r).doorsOpened);
        Assert.AreEqual(CellType.Ground, MoveRules.Kind(m, s, C(2, 0)));
        Assert.AreEqual(MoveOutcome.Move, MoveRules.Walk(m, s, C(1, 0), Right).outcome, "열린 문은 지나감");
    }

    [Test]
    public void ClosedDoorCanBeJumpedButNotLandedOn()
    {
        var m = Map(new[] { ".D." });
        Assert.AreEqual(C(2, 0), MoveRules.Jump(m, new PuzzleState(), C(0, 0), Right).cell, "문 하나는 넘을 수 있음");
        m = Map(new[] { ".DD" });
        Assert.AreEqual(MoveOutcome.Blocked, MoveRules.Jump(m, new PuzzleState(), C(0, 0), Right).outcome, "닫힌 문 위로는 착지 못함");
    }

    // ------------------------------------------------------------ 계단
    [Test]
    public void StairsIgnoreClimbLimit()
    {
        var m = Map(new[] { ".T." }, null, new[] { "125" });
        var s = new PuzzleState();
        Assert.AreEqual(MoveOutcome.Move, MoveRules.Walk(m, s, C(0, 0), Right).outcome, "땅 → 계단");
        Assert.AreEqual(MoveOutcome.Move, MoveRules.Walk(m, s, C(1, 0), Right).outcome, "계단 → 3층 높은 땅");
        Assert.AreEqual(MoveOutcome.Move, MoveRules.Walk(m, s, C(2, 0), Left).outcome, "높은 땅 → 계단");
        var noStairs = Map(new[] { "..." }, null, new[] { "125" });
        Assert.AreEqual(MoveOutcome.Blocked, MoveRules.Walk(noStairs, s, C(1, 0), Right).outcome, "계단이 없으면 막힘");
    }

    // ------------------------------------------------------------ 물건 층 / 검증
    [Test]
    public void GemOnIceIsCollectedWhileSliding()
    {
        var m = Map(new[] { "....", ".II." }, new[] { "....", "S.G." });
        Assert.IsTrue(MapValidator.Validate(m).IsValid);
    }

    [Test]
    public void ItemsMustStandOnWalkableTerrain()
    {
        var m = Map(new[] { "..", ".#" }, new[] { "..", "SG" });
        var v = MapValidator.Validate(m);
        Assert.IsFalse(v.IsValid);
        StringAssert.Contains("설 수 없는", string.Join(" ", v.errors));
    }

    [Test]
    public void CrumbleOrderCanMakeStageUnsolvable()
    {
        // 무너지는 다리 하나를 건너 양쪽 끝 보석을 모두 먹어야 하는데, 돌아올 길이 없다
        var m = Map(new[] { "OOOOO", ".CCC.", "OOOOO" }, new[] { ".....", "G.S.G", "....." });
        var v = MapValidator.Validate(m);
        Assert.IsFalse(v.IsValid);
        Assert.AreEqual(0, v.unreachableGems.Count, "보석 하나하나는 갈 수 있다");
        StringAssert.Contains("한 판에 모두", string.Join(" ", v.errors));
    }

    [Test]
    public void DoorNeedsSwitch()
    {
        // 보석은 문 두 칸 뒤 (점프로 못 넘음). 스위치가 없으면 못 감, 있으면 감.
        var noSwitch = Map(new[] { "..", "D#", "D#", ".." }, new[] { "G.", "..", "..", "S." });
        Assert.IsFalse(MapValidator.Validate(noSwitch).IsValid);
        var withSwitch = Map(new[] { "..", "D#", "D#", ".W" }, new[] { "G.", "..", "..", "S." });
        Assert.IsTrue(MapValidator.Validate(withSwitch).IsValid);
    }

    [Test]
    public void SetKeepsTerrainForItemsAndEraserClearsThem()
    {
        var m = Map(new[] { "I." }, new[] { "S." });
        m.Set(C(1, 0), MapData.Gem);
        m.Set(C(0, 0), MapData.Gem);                     // 얼음 위 보석: 바닥은 그대로
        Assert.AreEqual(MapData.Ice, m.SymbolAt(C(0, 0)));
        Assert.AreEqual(MapData.Gem, m.ItemAt(C(0, 0)));
        m.Set(C(0, 0), MapData.Ground);                  // 땅 붓 = 지우개
        Assert.AreEqual(MapData.NoItem, m.ItemAt(C(0, 0)));
        m.Set(C(1, 0), MapData.Obstacle);                // 장애물을 칠하면 보석이 지워짐
        Assert.AreEqual(MapData.NoItem, m.ItemAt(C(1, 0)));
    }
}
