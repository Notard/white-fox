using System;
using UnityEngine;

public class MapFormatException : Exception
{
    public MapFormatException(string message) : base(message) { }
}

/// <summary>
/// 스테이지 JSON 읽기·쓰기. 형식이 틀리면 어디가 틀렸는지 MapFormatException 으로 알려 준다.
/// 옛 버전도 읽는다: 1 → floors 없음(모두 1층), 1·2 → rows 에 보석·시작이 섞여 있음(items 로 옮김),
/// 3 → movers 없음, 4 → channels·floorsOn 없음.
/// </summary>
public static class MapJson
{
    public static string ToJson(MapData map) => JsonUtility.ToJson(map, true);

    public static MapData FromJson(string json, string source = "스테이지 파일")
    {
        MapData map;
        try
        {
            map = JsonUtility.FromJson<MapData>(json);
        }
        catch (ArgumentException e)
        {
            throw new MapFormatException($"{source}: JSON 형식이 아닙니다 ({e.Message})");
        }
        if (map == null) throw new MapFormatException($"{source}: 내용이 비어 있습니다");
        if (map.version > MapData.CurrentVersion)
            throw new MapFormatException($"{source}: 버전 {map.version} 파일은 이 게임보다 새 버전입니다 (지원: {MapData.CurrentVersion})");
        if (map.width < MapData.MinSize || map.width > MapData.MaxSize ||
            map.height < MapData.MinSize || map.height > MapData.MaxSize)
            throw new MapFormatException($"{source}: 크기 {map.width}×{map.height} 는 범위({MapData.MinSize}~{MapData.MaxSize})를 벗어납니다");

        bool legacyItems = map.version < 3 || map.items == null || map.items.Length == 0;
        CheckGrid(map, map.rows, "rows", source, c =>
            Array.IndexOf(MapData.TerrainSymbols, c) >= 0 || (legacyItems && (c == MapData.Gem || c == MapData.Start)),
            "모르는 바닥 기호");

        if (legacyItems) map.SplitItemsFromRows();
        else CheckGrid(map, map.items, "items", source, c => Array.IndexOf(MapData.ItemSymbols, c) >= 0, "모르는 물건 기호");

        if (map.floors == null || map.floors.Length == 0) map.FillFloors(MapData.MinFloor);
        else CheckGrid(map, map.floors, "floors", source,
            c => c >= '0' + MapData.MinFloor && c <= '0' + MapData.MaxFloor, $"{MapData.MinFloor}~{MapData.MaxFloor} 층 숫자가 아님");

        // 버전 4 이하에는 장치 칸이 없다
        if (map.channels == null || map.channels.Length == 0) map.channels = null;
        else CheckGrid(map, map.channels, "channels", source,
            c => c == MapData.NoChannel || (c >= '1' && c <= '0' + MapData.MaxChannel), $"채널(1~{MapData.MaxChannel} 또는 .)이 아님");
        if (map.floorsOn == null || map.floorsOn.Length == 0) map.floorsOn = null;
        else CheckGrid(map, map.floorsOn, "floorsOn", source,
            c => c == MapData.NoChannel || (c >= '0' + MapData.MinFloor && c <= '0' + MapData.MaxFloor), "층 숫자(또는 .)가 아님");
        map.EnsureMechanismGrids();

        // 버전 3 이하에는 movers 가 없다
        map.movers ??= new System.Collections.Generic.List<Mover>();
        for (int i = 0; i < map.movers.Count; i++)
        {
            var m = map.movers[i];
            if (Array.IndexOf(Mover.Kinds, m.kind) < 0)
                throw new MapFormatException($"{source}: movers {i + 1}번째의 kind '{m.kind}' 는 모르는 종류입니다 (snowball, owl, platform)");
            if (m.path == null || m.path.Count == 0)
                throw new MapFormatException($"{source}: movers {i + 1}번째의 길(path)이 비어 있습니다");
            for (int k = 0; k < m.path.Count; k++)
            {
                if (!map.IsInside(m.path[k]))
                    throw new MapFormatException($"{source}: movers {i + 1}번째 길의 {k + 1}번째 칸 ({m.path[k].x},{m.path[k].y}) 이 맵 밖입니다");
                if (k > 0 && Mathf.Abs(m.path[k].x - m.path[k - 1].x) + Mathf.Abs(m.path[k].y - m.path[k - 1].y) != 1)
                    throw new MapFormatException($"{source}: movers {i + 1}번째 길의 {k}·{k + 1}번째 칸이 이웃하지 않습니다");
            }
        }
        map.version = MapData.CurrentVersion;
        return map;
    }

    static void CheckGrid(MapData map, string[] grid, string field, string source, Func<char, bool> ok, string what)
    {
        if (grid == null || grid.Length != map.height)
            throw new MapFormatException($"{source}: {field} 줄 수({grid?.Length ?? 0})가 height({map.height})와 다릅니다");
        for (int r = 0; r < grid.Length; r++)
        {
            string row = grid[r] ?? "";
            if (row.Length != map.width)
                throw new MapFormatException($"{source}: {field} {r + 1}번째 줄 \"{row}\" 의 길이({row.Length})가 width({map.width})와 다릅니다");
            for (int i = 0; i < row.Length; i++)
                if (!ok(row[i]))
                    throw new MapFormatException($"{source}: {field} {r + 1}번째 줄 {i + 1}번째 글자 '{row[i]}' 는 {what}입니다");
        }
    }
}
