using NUnit.Framework;
using UnityEngine;

/// <summary>칸 높이(층) 데이터와 오르내리기 규칙.</summary>
public class FloorTests
{
    static MapData Map(string[] rows, string[] floors) => new MapData
    {
        width = rows[0].Length, height = rows.Length, rows = rows, floors = floors,
    };

    static readonly Vector2Int Up = Vector2Int.up, Right = Vector2Int.right;

    [Test]
    public void Version1FilesReadAsFirstFloor()
    {
        var m = MapJson.FromJson("{\"version\":1,\"width\":2,\"height\":2,\"rows\":[\"G.\",\"S.\"]}");
        Assert.AreEqual(1, m.FloorAt(new Vector2Int(1, 1)));
        Assert.AreEqual(MapData.CurrentVersion, m.version);
        StringAssert.Contains("\"floors\"", MapJson.ToJson(m));
    }

    [Test]
    public void FloorsRoundTripAndValidate()
    {
        var m = Map(new[] { "G.", "S." }, new[] { "32", "11" });
        var back = MapJson.FromJson(MapJson.ToJson(m));
        Assert.AreEqual(3, back.FloorAt(new Vector2Int(0, 1)));
        Assert.AreEqual(3, back.HighestFloor);

        var e = Assert.Throws<MapFormatException>(() =>
            MapJson.FromJson("{\"width\":2,\"height\":2,\"rows\":[\"G.\",\"S.\"],\"floors\":[\"10\",\"11\"]}"));
        StringAssert.Contains("층 숫자", e.Message);
    }

    [Test]
    public void WalkClimbsOneFloorAndStepsDownAny()
    {
        var m = Map(new[] { "....", "S..." }, new[] { "1111", "1239" });
        var r = MoveRules.Walk(m, new Vector2Int(0, 0), Right);           // 1 → 2
        Assert.AreEqual(MoveOutcome.Move, r.outcome);
        r = MoveRules.Walk(m, new Vector2Int(1, 0), Right);               // 2 → 3
        Assert.AreEqual(MoveOutcome.Move, r.outcome);
        r = MoveRules.Walk(m, new Vector2Int(2, 0), Right);               // 3 → 9 벽
        Assert.AreEqual(MoveOutcome.Blocked, r.outcome);
        Assert.AreEqual(new Vector2Int(2, 0), r.cell);
        r = MoveRules.Walk(m, new Vector2Int(3, 0), Up);                  // 9 → 1 내려감
        Assert.AreEqual(MoveOutcome.Move, r.outcome);
    }

    [Test]
    public void JumpClimbsTwoFloors()
    {
        var m = Map(new[] { "S...." }, new[] { "11314" });
        Assert.AreEqual(MoveOutcome.Move, MoveRules.Jump(m, new Vector2Int(0, 0), Right).outcome);     // 1 → 3
        Assert.AreEqual(MoveOutcome.Move, MoveRules.Jump(m, new Vector2Int(2, 0), Right).outcome);     // 3 → 4, 가운데가 낮아도 됨
    }

    [Test]
    public void JumpLimits()
    {
        // 1 → 4 는 너무 높음
        var m = Map(new[] { "S.." }, new[] { "114" });
        var r = MoveRules.Jump(m, new Vector2Int(0, 0), Right);
        Assert.AreEqual(MoveOutcome.Blocked, r.outcome);
        Assert.AreEqual(new Vector2Int(0, 0), r.cell);

        // 바로 앞이 3층 이상 높으면 올라서지도 넘지도 못함 (가운데 벽)
        m = Map(new[] { "S.." }, new[] { "141" });
        r = MoveRules.Jump(m, new Vector2Int(0, 0), Right);
        Assert.AreEqual(MoveOutcome.Blocked, r.outcome);
        Assert.AreEqual(new Vector2Int(0, 0), r.cell);
    }

    [Test]
    public void JumpOntoHigherCellJustAhead()
    {
        // 바로 앞이 +1, +2 층이면 그 칸 위로
        var m = Map(new[] { "S.." }, new[] { "121" });
        Assert.AreEqual(new Vector2Int(1, 0), MoveRules.Jump(m, new Vector2Int(0, 0), Right).cell);
        m = Map(new[] { "S.." }, new[] { "131" });
        var r = MoveRules.Jump(m, new Vector2Int(0, 0), Right);
        Assert.AreEqual(MoveOutcome.Move, r.outcome);
        Assert.AreEqual(new Vector2Int(1, 0), r.cell);
        // 바로 앞이 같거나 낮으면 지금처럼 두 칸 앞으로
        m = Map(new[] { "S.." }, new[] { "211" });
        Assert.AreEqual(new Vector2Int(2, 0), MoveRules.Jump(m, new Vector2Int(0, 0), Right).cell);
        // 바로 앞이 장애물이면 넘어서 두 칸 앞으로
        m = Map(new[] { "S#." }, new[] { "121" });
        Assert.AreEqual(new Vector2Int(2, 0), MoveRules.Jump(m, new Vector2Int(0, 0), Right).cell);
    }

    [Test]
    public void ResizeKeepsFloors()
    {
        var m = Map(new[] { "G.", "S." }, new[] { "23", "11" });
        m.Resize(3, 3);
        Assert.AreEqual(3, m.FloorAt(new Vector2Int(1, 1)));
        Assert.AreEqual(1, m.FloorAt(new Vector2Int(2, 2)));
    }

    [Test]
    public void ValidatorUsesFloors()
    {
        // 보석이 4층 기둥 위, 주변은 1층 → 걸어서도(+1) 뛰어올라서도(+2) 못 감
        var m = Map(new[] { "...", ".G.", "S.." }, new[] { "111", "141", "111" });
        Assert.IsFalse(MapValidator.Validate(m).IsValid);
        // 3층 기둥이면 바로 옆에서 뛰어오를 수 있음
        m = Map(new[] { "...", ".G.", "S.." }, new[] { "111", "131", "111" });
        Assert.IsTrue(MapValidator.Validate(m).IsValid);
        // 4층 기둥도 옆에 2층 디딤돌이 있으면 갈 수 있음 (걸어서 2층 → 뛰어올라 4층)
        m = Map(new[] { "...", ".G.", "S.." }, new[] { "111", "241", "111" });
        Assert.IsTrue(MapValidator.Validate(m).IsValid);
    }
}
