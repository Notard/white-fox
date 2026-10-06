using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 맵툴에서 편집 중인 스테이지 한 장의 상태: 칠하기, 크기 변경, 실행 취소/다시 실행, 저장 안 한 변경 여부.
/// 화면(MapEditorWindow)과 분리해 테스트할 수 있게 했다.
/// </summary>
public class MapEditorSession
{
    public MapData Map { get; private set; }
    public string Path { get; private set; }
    public MapValidation Validation { get; private set; }

    string savedJson;
    readonly List<string> undo = new();
    readonly List<string> redo = new();
    const int MaxUndo = 100;

    public bool IsDirty => MapJson.ToJson(Map) != savedJson;
    public bool CanUndo => undo.Count > 0;
    public bool CanRedo => redo.Count > 0;

    public MapEditorSession(string path, MapData map)
    {
        Path = path;
        Map = map.Clone();
        savedJson = MapJson.ToJson(Map);
        Revalidate();
    }

    /// <summary>바뀌기 직전에 부른다 (칠하기 한 번, 끌어서 칠하기 한 번, 크기 변경, 이름 변경).</summary>
    public void RecordUndo()
    {
        undo.Add(MapJson.ToJson(Map));
        if (undo.Count > MaxUndo) undo.RemoveAt(0);
        redo.Clear();
    }

    /// <summary>칸을 칠한다. 이미 같은 기호면 아무 일도 하지 않고 false.</summary>
    public bool Paint(Vector2Int cell, char symbol)
    {
        if (!Map.IsInside(cell)) return false;
        var before = (Map.SymbolAt(cell), Map.ItemAt(cell));
        Map.Set(cell, symbol);
        if ((Map.SymbolAt(cell), Map.ItemAt(cell)) == before && symbol != MapData.Start) return false;
        Revalidate();
        return true;
    }

    /// <summary>장치·바뀌는 땅을 칠한다: 바닥 기호 + 채널 (+ 승강 땅이면 켜졌을 때 층). 바뀌지 않았으면 false.</summary>
    public bool PaintMechanism(Vector2Int cell, char symbol, int channel, int floorOn)
    {
        if (!Map.IsInside(cell)) return false;
        string before = Snapshot(cell);
        Map.Set(cell, symbol);
        Map.SetMechanism(cell, channel, symbol == MapData.Lift ? floorOn : 0);
        if (Snapshot(cell) == before) return false;
        Revalidate();
        return true;
    }

    string Snapshot(Vector2Int c) => $"{Map.SymbolAt(c)}{Map.ItemAt(c)}{Map.ChannelAt(c)}{Map.FloorOnAt(c)}";

    /// <summary>칸의 층을 바꾼다. 이미 같은 층이면 false.</summary>
    public bool PaintFloor(Vector2Int cell, int floor)
    {
        if (!Map.IsInside(cell) || Map.FloorAt(cell) == floor) return false;
        Map.SetFloor(cell, floor);
        Revalidate();
        return true;
    }

    // ------------------------------------------------------------ 움직이는 것 (적·발판)
    /// <summary>길을 그리고 있는 움직이는 것의 번호 (-1 = 그리는 중 아님).</summary>
    public int Drawing { get; private set; } = -1;

    /// <summary>새 움직이는 것을 만들고 길 그리기를 시작한다. 길은 비어 있다.</summary>
    public int AddMover(string kind)
    {
        RecordUndo();
        Map.movers.Add(new Mover { kind = kind });
        Drawing = Map.movers.Count - 1;
        return Drawing;
    }

    public void RemoveMover(int index)
    {
        if (index < 0 || index >= Map.movers.Count) return;
        RecordUndo();
        Map.movers.RemoveAt(index);
        if (Drawing == index) Drawing = -1;
        else if (Drawing > index) Drawing--;
        Revalidate();
    }

    /// <summary>index 의 길 그리기를 다시 시작한다 (기존 길 끝에 이어 붙인다).</summary>
    public void BeginDrawing(int index) => Drawing = index >= 0 && index < Map.movers.Count ? index : -1;

    /// <summary>길 그리기를 끝낸다. 길이 빈 움직이는 것은 지운다.</summary>
    public void EndDrawing()
    {
        if (Drawing >= 0 && Drawing < Map.movers.Count && Map.movers[Drawing].path.Count == 0)
            Map.movers.RemoveAt(Drawing);
        Drawing = -1;
        Revalidate();
    }

    /// <summary>
    /// 그리는 중인 길에 칸을 더한다. 첫 칸은 아무 데나, 그 뒤로는 마지막 칸의 이웃만.
    /// 마지막 칸을 다시 누르면 그 칸을 지운다. 바뀌었으면 true.
    /// </summary>
    public bool ClickPath(Vector2Int cell)
    {
        if (Drawing < 0 || Drawing >= Map.movers.Count || !Map.IsInside(cell)) return false;
        var path = Map.movers[Drawing].path;
        if (path.Count > 0 && path[^1] == cell)
        {
            RecordUndo();
            path.RemoveAt(path.Count - 1);
        }
        else
        {
            if (path.Count > 0)
            {
                var d = cell - path[^1];
                if (Mathf.Abs(d.x) + Mathf.Abs(d.y) != 1) return false;
                if (path.Count >= 2 && path[^2] == cell) return false;   // 바로 되돌아가기는 왕복이 알아서 함
            }
            RecordUndo();
            path.Add(cell);
        }
        Revalidate();
        return true;
    }

    public void Resize(int width, int height)
    {
        if (width == Map.Width && height == Map.Height) return;
        RecordUndo();
        Map.Resize(width, height);
        Revalidate();
    }

    public void Rename(string name)
    {
        if (name == Map.name) return;
        Map.name = name;
    }

    public void Undo() => Step(undo, redo);
    public void Redo() => Step(redo, undo);

    void Step(List<string> from, List<string> to)
    {
        if (from.Count == 0) return;
        to.Add(MapJson.ToJson(Map));
        Map = MapJson.FromJson(from[^1]);
        from.RemoveAt(from.Count - 1);
        Revalidate();
    }

    /// <summary>저장된 상태로 되돌린다.</summary>
    public void Revert()
    {
        RecordUndo();
        Map = MapJson.FromJson(savedJson);
        Revalidate();
    }

    public void MarkSaved(string path)
    {
        Path = path;
        savedJson = MapJson.ToJson(Map);
    }

    public void Revalidate()
    {
        if (Drawing >= Map.movers.Count) Drawing = -1;   // 실행 취소로 사라진 경우
        Validation = MapValidator.Validate(Map);
    }
}
