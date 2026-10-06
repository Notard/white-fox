using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

/// <summary>움직이는 것(적·발판)과 턴 규칙: 왕복, 실려 가기, 부딪힘, 기다리기, JSON, 검증.</summary>
public class MoverTests
{
    static MapData Map(string[] rows, string[] items = null, params Mover[] movers)
    {
        int w = rows[0].Length, h = rows.Length;
        var m = new MapData { width = w, height = h, rows = rows, items = items };
        if (items == null)
        {
            m.items = new string[h];
            for (int i = 0; i < h; i++) m.items[i] = new string('.', w);
        }
        m.FillFloors(1);
        m.movers = new List<Mover>(movers);
        return m;
    }

    static Mover M(string kind, params (int x, int y)[] path)
    {
        var m = new Mover { kind = kind };
        foreach (var (x, y) in path) m.path.Add(new Vector2Int(x, y));
        return m;
    }

    static Vector2Int C(int x, int y) => new(x, y);
    static readonly Vector2Int Right = Vector2Int.right, Left = Vector2Int.left;

    /// <summary>걷기 → Apply → 턴 진행 (게임과 같은 순서).</summary>
    static (MoveResult r, TurnRules.Outcome o) WalkTurn(MapData map, PuzzleState s, Vector2Int from, Vector2Int dir)
    {
        var r = MoveRules.Walk(map, s, from, dir);
        MoveRules.Apply(map, s, from, r);
        var o = r.outcome == MoveOutcome.Fall ? default : TurnRules.Advance(map, s, from, r);
        return (r, o);
    }

    [Test]
    public void MoverPingPongsAlongPath()
    {
        var m = M(Mover.Snowball, (0, 0), (1, 0), (2, 0));
        Assert.AreEqual(4, m.Period);
        var expected = new[] { C(0, 0), C(1, 0), C(2, 0), C(1, 0), C(0, 0), C(1, 0) };
        for (int t = 0; t < expected.Length; t++) Assert.AreEqual(expected[t], m.PositionAt(t), $"turn {t}");
        Assert.AreEqual(C(3, 3), M(Mover.Owl, (3, 3)).PositionAt(7), "한 칸짜리 길은 제자리");
    }

    [Test]
    public void PlatformCarriesFoxAcrossHole()
    {
        var map = Map(new[] { "..OO.." }, null, M(Mover.Platform, (2, 0), (3, 0)));
        var s = new PuzzleState();
        var (r, o) = WalkTurn(map, s, C(1, 0), Right);       // 턴 0: 발판이 (2,0) 에 있다
        Assert.AreEqual(MoveOutcome.Move, r.outcome);
        Assert.IsTrue(o.carried);
        Assert.AreEqual(C(3, 0), o.cell, "발판에 실려 (3,0) 으로");
        var (r2, o2) = WalkTurn(map, s, o.cell, Right);
        Assert.AreEqual(MoveOutcome.Move, r2.outcome);
        Assert.AreEqual(C(4, 0), o2.cell);
        Assert.IsFalse(o2.hit);
    }

    [Test]
    public void StepIntoHoleWithoutPlatformFalls()
    {
        var map = Map(new[] { "..OO.." }, null, M(Mover.Platform, (2, 0), (3, 0)));
        var s = new PuzzleState { turn = 1 };                  // 발판은 (3,0) 에 있다
        Assert.AreEqual(MoveOutcome.Fall, MoveRules.Walk(map, s, C(1, 0), Right).outcome);
    }

    [Test]
    public void WalkingIntoEnemyHits()
    {
        var map = Map(new[] { "....." }, null, M(Mover.Snowball, (2, 0), (3, 0)));
        var s = new PuzzleState();
        var (_, o) = WalkTurn(map, s, C(1, 0), Right);        // 눈덩이가 있는 (2,0) 으로
        Assert.IsTrue(o.hit);
    }

    [Test]
    public void EnemyComingIntoWaitingFoxHits()
    {
        var map = Map(new[] { "....." }, null, M(Mover.Snowball, (3, 0), (2, 0)));
        var s = new PuzzleState();
        var o = TurnRules.Advance(map, s, C(2, 0), TurnRules.Wait(C(2, 0), Right));
        Assert.IsTrue(o.hit, "기다리는 칸으로 눈덩이가 들어옴");
        Assert.AreEqual(1, s.turn);
    }

    [Test]
    public void SwappingPlacesWithEnemyHits()
    {
        var map = Map(new[] { "....." }, null, M(Mover.Snowball, (2, 0), (1, 0)));
        var s = new PuzzleState();
        var (_, o) = WalkTurn(map, s, C(1, 0), Right);        // 서로 엇갈림
        Assert.IsTrue(o.hit);
    }

    [Test]
    public void JumpingOverEnemyIsSafe()
    {
        var map = Map(new[] { "....." }, null, M(Mover.Snowball, (2, 0)));
        var s = new PuzzleState();
        var r = MoveRules.Jump(map, s, C(1, 0), Right);
        var o = TurnRules.Advance(map, s, C(1, 0), r);
        Assert.AreEqual(C(3, 0), o.cell);
        Assert.IsFalse(o.hit, "점프로 넘은 칸은 지나간 칸이 아니다");
    }

    [Test]
    public void OwlFliesOverHolesButIsNotAFloor()
    {
        var map = Map(new[] { ".O." }, null, M(Mover.Owl, (1, 0)));
        var s = new PuzzleState();
        Assert.AreEqual(MoveOutcome.Fall, MoveRules.Walk(map, s, C(0, 0), Right).outcome, "부엉이는 발판이 아니다");
    }

    [Test]
    public void JsonRoundTripKeepsMovers()
    {
        var map = Map(new[] { "....", "...." }, new[] { "....", "S..G" }, M(Mover.Owl, (1, 0), (2, 0)));
        var back = MapJson.FromJson(MapJson.ToJson(map));
        Assert.AreEqual(1, back.movers.Count);
        Assert.AreEqual(Mover.Owl, back.movers[0].kind);
        CollectionAssert.AreEqual(map.movers[0].path, back.movers[0].path);
    }

    [Test]
    public void JsonRejectsBrokenPath()
    {
        var map = Map(new[] { "....", "...." }, new[] { "....", "S..G" }, M(Mover.Snowball, (0, 0), (2, 0)));
        Assert.Throws<MapFormatException>(() => MapJson.FromJson(MapJson.ToJson(map)), "이웃하지 않는 칸");
        map.movers[0] = M("dragon", (1, 0));
        Assert.Throws<MapFormatException>(() => MapJson.FromJson(MapJson.ToJson(map)), "모르는 종류");
    }

    [Test]
    public void ValidatorSolvesPlatformCrossing()
    {
        // 발판은 (2,0)↔(3,0) 왕복. 때를 맞춰 타야 건널 수 있다.
        var map = Map(new[] { "..OO.." }, new[] { "S....G" }, M(Mover.Platform, (2, 0), (3, 0)));
        var v = MapValidator.Validate(map);
        Assert.IsTrue(v.IsValid, string.Join("\n", v.errors));
    }

    [Test]
    public void ValidatorRejectsUnavoidableEnemy()
    {
        // 외길 한가운데에 움직이지 않는 눈덩이: 지나갈 수도, 뛰어넘을 공간도 없음 (보석이 바로 뒤)
        var map = Map(new[] { "...." }, new[] { "S..G" }, M(Mover.Snowball, (2, 0), (3, 0)));
        var v = MapValidator.Validate(map);
        Assert.IsFalse(v.IsValid);
    }

    [Test]
    public void ValidatorRejectsEnemyOnStart()
    {
        var map = Map(new[] { "...." }, new[] { "S..G" }, M(Mover.Snowball, (0, 0), (1, 0)));
        Assert.IsFalse(MapValidator.Validate(map).IsValid);
    }

    [Test]
    public void ResizeTrimsPathsOutsideMap()
    {
        var map = Map(new[] { "....." }, null, M(Mover.Snowball, (1, 0), (2, 0), (3, 0), (4, 0)));
        map.Resize(3, 1);
        Assert.AreEqual(2, map.movers[0].path.Count);
    }
}
