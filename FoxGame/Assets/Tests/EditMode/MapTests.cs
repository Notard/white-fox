using System.IO;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

public class MapDataTests
{
    static MapData Map(params string[] rows) => new MapData
    {
        name = "테스트", width = rows[0].Length, height = rows.Length, rows = rows,
    };

    [Test]
    public void JsonRoundTripKeepsEverything()
    {
        var a = Map("G.#G", ".O.G", "#..O", "S.G.");     // 옛 방식(rows 에 보석·시작)으로 만든 맵
        var b = MapJson.FromJson(MapJson.ToJson(a));
        Assert.AreEqual(a.name, b.name);
        Assert.AreEqual(a.width, b.width);
        Assert.AreEqual(a.height, b.height);
        // 버전 3: 바닥과 물건이 나뉘어 저장된다
        CollectionAssert.AreEqual(new[] { "..#.", ".O..", "#..O", "...." }, b.rows);
        CollectionAssert.AreEqual(new[] { "G..G", "...G", "....", "S.G." }, b.items);
        CollectionAssert.AreEqual(a.Gems, b.Gems);
        Assert.AreEqual(a.StartCell, b.StartCell);
        Assert.AreEqual(MapData.CurrentVersion, b.version);
    }

    [Test]
    public void BadFilesExplainWhatIsWrong()
    {
        var e = Assert.Throws<MapFormatException>(() =>
            MapJson.FromJson("{\"width\":3,\"height\":2,\"rows\":[\"S.G\",\"..\"]}", "x.json"));
        StringAssert.Contains("2번째 줄", e.Message);

        e = Assert.Throws<MapFormatException>(() =>
            MapJson.FromJson("{\"width\":3,\"height\":2,\"rows\":[\"S.G\",\".X.\"]}", "x.json"));
        StringAssert.Contains("'X'", e.Message);

        e = Assert.Throws<MapFormatException>(() =>
            MapJson.FromJson("{\"width\":1,\"height\":2,\"rows\":[\"S\",\"G\"]}", "x.json"));
        StringAssert.Contains("범위", e.Message);

        Assert.Throws<MapFormatException>(() => MapJson.FromJson("이건 JSON 이 아님"));
    }

    [Test]
    public void CoordinatesStartAtBottomLeft()
    {
        var m = Map("G.", "S.");
        Assert.AreEqual(new Vector2Int(0, 0), m.StartCell);
        CollectionAssert.AreEqual(new[] { new Vector2Int(0, 1) }, m.Gems);
        Assert.AreEqual(CellType.Hole, m.TypeAt(new Vector2Int(-1, 0)));   // 맵 밖 = 구멍
    }

    [Test]
    public void OnlyOneStart()
    {
        var m = Map("...", "S..");
        m.Set(new Vector2Int(2, 1), MapData.Start);
        Assert.AreEqual(1, m.Find(MapData.Start).Count);
        Assert.AreEqual(new Vector2Int(2, 1), m.StartCell);
    }

    [Test]
    public void ResizeKeepsBottomLeftAndClamps()
    {
        var m = Map("G.#", "S.O");
        m.Resize(4, 3);
        CollectionAssert.AreEqual(new[] { "....", "..#.", "..O." }, m.rows);
        CollectionAssert.AreEqual(new[] { "....", "G...", "S..." }, m.items);
        m.Resize(2, 1);   // 최소 2
        Assert.AreEqual(2, m.height);
        CollectionAssert.AreEqual(new[] { "G.", "S." }, m.items);
        m.Resize(99, 99);
        Assert.AreEqual(MapData.MaxSize, m.width);
    }
}

public class MapValidatorTests
{
    static MapData Map(params string[] rows) => new MapData
    {
        width = rows[0].Length, height = rows.Length, rows = rows,
    };

    [Test]
    public void FirstStageLayoutIsValid() =>
        Assert.IsTrue(MapValidator.Validate(Map("G.#G", ".O.G", "#..O", "S.G.")).IsValid);

    [Test]
    public void NeedsStartAndGem()
    {
        Assert.IsFalse(MapValidator.Validate(Map("G.", "..")).IsValid);
        Assert.IsFalse(MapValidator.Validate(Map("..", "S.")).IsValid);
    }

    [Test]
    public void GemBehindHolesIsUnreachable()
    {
        var v = MapValidator.Validate(Map("....", ".OOO", ".OOG", "SOOO"));
        Assert.IsFalse(v.IsValid);
        CollectionAssert.AreEqual(new[] { new Vector2Int(3, 1) }, v.unreachableGems);
    }

    [Test]
    public void FacingMattersForJumps()
    {
        // 시작할 때 아래를 보고 있고, 위를 볼 방법(걸어 올라가거나 위 장애물에 부딪히기)이 없다
        var v = MapValidator.Validate(Map("GOO", "OOO", "SOO"));
        CollectionAssert.AreEqual(new[] { new Vector2Int(0, 2) }, v.unreachableGems);

        // 위쪽 장애물에 부딪히면 위를 보게 되고, 점프는 장애물을 넘어 두 칸 앞에 착지한다
        v = MapValidator.Validate(Map("G.", "#O", "SO"));
        Assert.IsTrue(v.IsValid, string.Join(" / ", v.errors));
    }

    [Test]
    public void JumpOverHoleReachesGem()
    {
        // (0,1)로 걸어 올라가면 위를 보고, 구멍 (0,2)를 넘어 (0,3)에 착지
        Assert.IsTrue(MapValidator.Validate(Map("GO", "OO", "..", "S.")).IsValid);
        // 구멍 바로 앞에서 시작하면 위를 볼 방법이 없어 못 감
        Assert.IsFalse(MapValidator.Validate(Map("G", "O", "S")).IsValid);
    }

    [Test]
    public void EveryStageInLibraryIsValid()
    {
        StageLibrary.ClearCache();
        Assert.GreaterOrEqual(StageLibrary.Count, 1);
        for (int i = 0; i < StageLibrary.Count; i++)
        {
            var map = StageLibrary.Load(i);
            var v = MapValidator.Validate(map);
            Assert.IsTrue(v.IsValid, $"스테이지 {i + 1} '{map.name}': " + string.Join(" / ", v.errors));
        }
    }
}

public class MapToolTests
{
    const string Folder = "Assets/Tests/EditMode/TempStages";
    StageFiles files;

    [SetUp]
    public void SetUp()
    {
        AssetDatabase.DeleteAsset(Folder);
        AssetDatabase.CreateFolder("Assets/Tests/EditMode", "TempStages");
        files = new StageFiles(Folder);
    }

    [TearDown]
    public void TearDown() => AssetDatabase.DeleteAsset(Folder);

    static MapData Named(string name)
    {
        var m = MapData.CreateEmpty(3, 3, name);
        m.Set(new Vector2Int(2, 2), MapData.Gem);
        return m;
    }

    [Test]
    public void CreateSaveAndReopen()
    {
        var path = files.Create(Named("하나"));
        Assert.AreEqual(Folder + "/stage_01.json", path);
        var session = new MapEditorSession(path, files.Load(path));
        session.RecordUndo();
        session.Paint(new Vector2Int(1, 1), MapData.Obstacle);
        Assert.IsTrue(session.IsDirty);
        files.Save(path, session.Map);
        session.MarkSaved(path);
        Assert.IsFalse(session.IsDirty);
        Assert.AreEqual(MapData.Obstacle, files.Load(path).SymbolAt(new Vector2Int(1, 1)));
    }

    [Test]
    public void ReorderAndDeleteRenumbersFiles()
    {
        files.Create(Named("A"));
        files.Create(Named("B"));
        files.Create(Named("C"));
        string guidC = AssetDatabase.AssetPathToGUID(Folder + "/stage_03.json");

        Assert.AreEqual(0, files.Move(2, -2));   // C 를 맨 앞으로
        var list = files.List();
        CollectionAssert.AreEqual(new[] { "C", "A", "B" }, list.ConvertAll(p => files.Load(p).name));
        Assert.AreEqual(guidC, AssetDatabase.AssetPathToGUID(Folder + "/stage_01.json"), "이름이 바뀌어도 GUID 유지");

        files.Delete(1);                          // A 삭제
        list = files.List();
        CollectionAssert.AreEqual(new[] { Folder + "/stage_01.json", Folder + "/stage_02.json" }, list);
        CollectionAssert.AreEqual(new[] { "C", "B" }, list.ConvertAll(p => files.Load(p).name));
    }

    [Test]
    public void UndoRedoAndRevert()
    {
        var s = new MapEditorSession("x", Named("편집"));
        s.RecordUndo();
        s.Paint(new Vector2Int(0, 2), MapData.Hole);
        s.Resize(5, 4);
        Assert.AreEqual(5, s.Map.Width);
        s.Undo();
        Assert.AreEqual(3, s.Map.Width);
        Assert.AreEqual(MapData.Hole, s.Map.SymbolAt(new Vector2Int(0, 2)));
        s.Undo();
        Assert.AreEqual(MapData.Ground, s.Map.SymbolAt(new Vector2Int(0, 2)));
        Assert.IsFalse(s.IsDirty);
        s.Redo();
        Assert.IsTrue(s.IsDirty);
        s.Revert();
        Assert.IsFalse(s.IsDirty);
    }
}
