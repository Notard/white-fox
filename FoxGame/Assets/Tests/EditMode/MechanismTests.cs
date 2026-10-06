using NUnit.Framework;
using UnityEngine;

/// <summary>장치 규칙: 미는 블록·구멍 메우기, 레버, 누름판, 솟는 다리, 승강 땅, 채널, JSON, 검증.</summary>
public class MechanismTests
{
    static string[] Fill(int w, int h, char c)
    {
        var a = new string[h];
        for (int i = 0; i < h; i++) a[i] = new string(c, w);
        return a;
    }

    /// <summary>rows·items·channels·floors·floorsOn 을 받아 맵을 만든다 (빠진 것은 기본값).</summary>
    static MapData Map(string[] rows, string[] items = null, string[] channels = null, string[] floors = null, string[] floorsOn = null)
    {
        int w = rows[0].Length, h = rows.Length;
        var m = new MapData
        {
            width = w, height = h, rows = rows,
            items = items ?? Fill(w, h, '.'),
            channels = channels ?? Fill(w, h, '.'),
            floorsOn = floorsOn ?? Fill(w, h, '.'),
            floors = floors,
        };
        if (floors == null) m.FillFloors(1);
        return m;
    }

    static Vector2Int C(int x, int y) => new(x, y);
    static readonly Vector2Int Right = Vector2Int.right, Left = Vector2Int.left, Up = Vector2Int.up;

    static MoveRules.Changes Do(MapData map, PuzzleState s, Vector2Int from, MoveResult r) => MoveRules.Apply(map, s, from, r);

    // ------------------------------------------------------------ 블록
    [Test]
    public void PushMovesBlockAndFox()
    {
        var map = Map(new[] { "....." }, new[] { ".K..." });
        var s = PuzzleState.Initial(map, C(0, 0));
        var r = MoveRules.Walk(map, s, C(0, 0), Right);
        Assert.AreEqual(MoveOutcome.Move, r.outcome);
        Assert.AreEqual(C(1, 0), r.cell);
        Assert.AreEqual(C(2, 0), r.pushTo);
        Do(map, s, C(0, 0), r);
        Assert.IsTrue(s.blocks.Contains(C(2, 0)));
        Assert.IsFalse(s.blocks.Contains(C(1, 0)));
    }

    [Test]
    public void BlockDoesNotMoveIntoWallBlockOrHigherGround()
    {
        var map = Map(new[] { "..#.." }, new[] { ".K.KK" }, floors: new[] { "11112" });
        var s = PuzzleState.Initial(map, C(0, 0));
        Assert.AreEqual(MoveOutcome.Blocked, MoveRules.Walk(map, s, C(0, 0), Right).outcome, "건너편이 바위");
        var map2 = Map(new[] { "....." }, new[] { "..KK." });
        var s2 = PuzzleState.Initial(map2, C(1, 0));
        Assert.AreEqual(MoveOutcome.Blocked, MoveRules.Walk(map2, s2, C(1, 0), Right).outcome, "블록 두 개는 못 민다");
        var map3 = Map(new[] { "...." }, new[] { ".K.." }, floors: new[] { "1122" });
        var s3 = PuzzleState.Initial(map3, C(0, 0));
        Assert.AreEqual(MoveOutcome.Blocked, MoveRules.Walk(map3, s3, C(0, 0), Right).outcome, "더 높은 땅으로는 못 민다");
    }

    [Test]
    public void BlockPushedIntoHoleFillsIt()
    {
        var map = Map(new[] { "..O.." }, new[] { ".K..." }, floors: new[] { "22122" });
        var s = PuzzleState.Initial(map, C(0, 0));
        var r = MoveRules.Walk(map, s, C(0, 0), Right);
        var ch = Do(map, s, C(0, 0), r);
        Assert.IsTrue(ch.filledHole);
        Assert.AreEqual(0, s.blocks.Count, "블록은 구멍 속으로");
        Assert.AreEqual(CellType.Ground, MoveRules.Kind(map, s, C(2, 0)));
        Assert.AreEqual(2, MoveRules.Floor(map, s, C(2, 0)), "민 칸 높이로 메워진다");
        var walk = MoveRules.Walk(map, s, C(1, 0), Right);
        Assert.AreEqual(MoveOutcome.Move, walk.outcome, "메운 칸 위로 걸어간다");
    }

    [Test]
    public void BlockCanBeJumpedOverButNotLandedOn()
    {
        var map = Map(new[] { "....." }, new[] { ".K..." });
        var s = PuzzleState.Initial(map, C(0, 0));
        Assert.AreEqual(C(2, 0), MoveRules.Jump(map, s, C(0, 0), Right).cell, "블록 넘기");
        var map2 = Map(new[] { "....." }, new[] { "...K." });
        var s2 = PuzzleState.Initial(map2, C(1, 0));
        Assert.AreEqual(MoveOutcome.Blocked, MoveRules.Jump(map2, s2, C(1, 0), Right).outcome, "블록 위에 내려앉지 않음");
    }

    // ------------------------------------------------------------ 레버·다리
    [Test]
    public void LeverTogglesBridge()
    {
        var map = Map(new[] { "L.B.." }, channels: new[] { "1.1.." });
        var s = PuzzleState.Initial(map, C(1, 0));
        Assert.AreEqual(CellType.Hole, MoveRules.Kind(map, s, C(2, 0)), "꺼진 다리 = 구멍");
        var pull = MoveRules.Walk(map, s, C(1, 0), Left);
        Assert.AreEqual(MoveOutcome.Blocked, pull.outcome);
        Assert.AreEqual(C(0, 0), pull.pulled);
        var ch = Do(map, s, C(1, 0), pull);
        Assert.IsTrue(ch.channelsChanged);
        Assert.AreEqual(CellType.Ground, MoveRules.Kind(map, s, C(2, 0)), "켜진 다리 = 땅");
        Assert.AreEqual(MoveOutcome.Move, MoveRules.Walk(map, s, C(1, 0), Right).outcome);
        Do(map, s, C(1, 0), MoveRules.Walk(map, s, C(1, 0), Left));
        Assert.AreEqual(CellType.Hole, MoveRules.Kind(map, s, C(2, 0)), "다시 당기면 꺼짐");
    }

    [Test]
    public void LeverOnlyAffectsItsChannel()
    {
        var map = Map(new[] { "L.BB." }, channels: new[] { "1.12." });
        var s = PuzzleState.Initial(map, C(1, 0));
        Do(map, s, C(1, 0), MoveRules.Walk(map, s, C(1, 0), Left));
        Assert.AreEqual(CellType.Ground, MoveRules.Kind(map, s, C(2, 0)));
        Assert.AreEqual(CellType.Hole, MoveRules.Kind(map, s, C(3, 0)), "다른 채널 다리는 그대로");
    }

    // ------------------------------------------------------------ 누름판
    [Test]
    public void PlateIsOnlyOnWhileWeighted()
    {
        var map = Map(new[] { ".PB.." }, channels: new[] { ".11.." });
        var s = PuzzleState.Initial(map, C(0, 0));
        var r = MoveRules.Walk(map, s, C(0, 0), Right);           // 누름판 위로
        Do(map, s, C(0, 0), r);
        Assert.IsTrue(s.ChannelOn(1));
        Assert.AreEqual(CellType.Ground, MoveRules.Kind(map, s, C(2, 0)));
        var off = MoveRules.Walk(map, s, C(1, 0), Right);         // 다리로 내려서면 누름판이 풀려 다리가 사라짐
        Assert.AreEqual(MoveOutcome.Move, off.outcome);
        var ch = Do(map, s, C(1, 0), off);
        Assert.IsFalse(s.ChannelOn(1));
        Assert.IsTrue(ch.foxFalls, "발밑 다리가 사라져 떨어진다");
    }

    [Test]
    public void BlockOnPlateKeepsBridgeUp()
    {
        var map = Map(new[] { "..P", "...", "BB." }, new[] { "...", "..K", "S.." }, new[] { "..1", "...", "11." });
        // 블록 (2,1) 을 위로 밀어 누름판 (2,2) 에 올린다
        var s = PuzzleState.Initial(map, C(2, 0));
        var r = MoveRules.Walk(map, s, C(2, 0), Up);
        Do(map, s, C(2, 0), r);
        Assert.IsTrue(s.blocks.Contains(C(2, 2)));
        Assert.IsTrue(s.ChannelOn(1));
        Assert.AreEqual(CellType.Ground, MoveRules.Kind(map, s, C(0, 0)));
    }

    [Test]
    public void BlockOnBridgeDropsAndFillsWhenItTurnsOff()
    {
        var map = Map(new[] { "L.B.." }, new[] { "...K." }, new[] { "1.1.." });
        var s = PuzzleState.Initial(map, C(1, 0));
        Do(map, s, C(1, 0), MoveRules.Walk(map, s, C(1, 0), Left));          // 레버 켜기 → 다리 솟음
        var push = MoveRules.Walk(map, s, C(4, 0), Left);                    // (4,0) 에서 블록을 다리 위로
        Assert.AreEqual(C(2, 0), push.pushTo);
        Do(map, s, C(4, 0), push);
        Assert.IsTrue(s.blocks.Contains(C(2, 0)), "블록이 다리 위로");
        var ch = Do(map, s, C(1, 0), MoveRules.Walk(map, s, C(1, 0), Left)); // 레버 끄기 → 다리 가라앉음
        Assert.IsNotNull(ch.blocksDropped);
        Assert.IsTrue(s.filled.ContainsKey(C(2, 0)), "블록이 떨어져 다리 칸을 메움");
        Assert.AreEqual(CellType.Ground, MoveRules.Kind(map, s, C(2, 0)));
    }

    // ------------------------------------------------------------ 승강 땅
    [Test]
    public void LiftChangesFloorWithChannel()
    {
        var map = Map(new[] { "L.E." }, channels: new[] { "1.1." }, floors: new[] { "1114" }, floorsOn: new[] { "..3." });
        var s = PuzzleState.Initial(map, C(1, 0));
        Assert.AreEqual(1, MoveRules.Floor(map, s, C(2, 0)));
        Assert.AreEqual(MoveOutcome.Blocked, MoveRules.Walk(map, s, C(2, 0), Right).outcome, "1층에서 4층은 못 오름");
        Do(map, s, C(1, 0), MoveRules.Walk(map, s, C(1, 0), Left));
        Assert.AreEqual(3, MoveRules.Floor(map, s, C(2, 0)));
        Assert.AreEqual(MoveOutcome.Move, MoveRules.Walk(map, s, C(2, 0), Right).outcome, "3층에서 4층은 걸어서");
        Assert.AreEqual(4, map.HighestFloor);
    }

    // ------------------------------------------------------------ 데이터
    [Test]
    public void JsonV5RoundTripAndV4Upgrade()
    {
        var map = Map(new[] { "L.E.", "...." }, new[] { "....", "S.KG" }, new[] { "1.1.", "...." }, floorsOn: new[] { "..3.", "...." });
        map.version = MapData.CurrentVersion;
        var back = MapJson.FromJson(MapJson.ToJson(map));
        Assert.AreEqual(1, back.ChannelAt(C(0, 1)));
        Assert.AreEqual(3, back.FloorOnAt(C(2, 1)));
        Assert.AreEqual(1, back.Blocks.Count);

        const string v4 = "{\"version\":4,\"name\":\"옛\",\"width\":2,\"height\":2,\"rows\":[\"..\",\"..\"],\"items\":[\"..\",\"SG\"],\"floors\":[\"11\",\"11\"]}";
        var old = MapJson.FromJson(v4);
        Assert.AreEqual(2, old.channels.Length);
        Assert.AreEqual(0, old.ChannelAt(C(0, 0)));
    }

    [Test]
    public void PaintingMechanismSetsChannelAndGroundClearsIt()
    {
        var map = MapData.CreateEmpty(3, 3);
        map.Set(C(1, 1), MapData.Lever);
        Assert.AreEqual(1, map.ChannelAt(C(1, 1)), "장치는 채널 1 로 시작");
        map.SetMechanism(C(1, 1), 3, 0);
        Assert.AreEqual(3, map.ChannelAt(C(1, 1)));
        map.Set(C(1, 1), MapData.Ground);
        Assert.AreEqual(0, map.ChannelAt(C(1, 1)), "땅으로 칠하면 채널도 지움");
    }

    // ------------------------------------------------------------ 검증
    [Test]
    public void ValidatorNeedsLeverForBridge()
    {
        var withLever = Map(new[] { "L.OO..", "..BB..", "..OO.." }, new[] { ".....G", "......", "S....G" },
            new[] { "1.....", "..11..", "......" });
        Assert.IsTrue(MapValidator.Validate(withLever).IsValid, string.Join("\n", MapValidator.Validate(withLever).errors));
        var noLever = Map(new[] { "..OO..", "..BB..", "..OO.." }, new[] { ".....G", "......", "S....G" },
            new[] { "......", "..11..", "......" });
        Assert.IsFalse(MapValidator.Validate(noLever).IsValid, "레버가 없으면 건널 수 없다");
    }

    [Test]
    public void ValidatorSolvesBlockOnPlate()
    {
        var map = Map(new[] { "..OO..", "..OO..", "P.BB..", "..OO.." }, new[] { "S....G", "K.....", "......", ".....G" },
            new[] { "......", "......", "1.11..", "......" });
        var v = MapValidator.Validate(map);
        Assert.IsTrue(v.IsValid, string.Join("\n", v.errors));
    }

    [Test]
    public void ValidatorWarnsUnlinkedChannel()
    {
        var map = Map(new[] { "L...", "...." }, new[] { "....", "S..G" }, new[] { "2...", "...." });
        var v = MapValidator.Validate(map);
        Assert.IsTrue(v.warnings.Exists(w => w.Contains("채널 2")));
    }
}
