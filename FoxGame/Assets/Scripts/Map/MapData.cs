using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// 정해진 길을 왕복하는 것: 눈덩이·부엉이(적), 발판. 여우가 한 번 행동할 때마다 한 칸씩 움직인다 (턴 방식).
/// 길의 첫 칸에서 출발해 끝까지 갔다가 되돌아오기를 반복한다.
/// </summary>
[Serializable]
public class Mover
{
    public const string Snowball = "snowball", Owl = "owl", Platform = "platform";
    public static readonly string[] Kinds = { Snowball, Owl, Platform };

    public string kind = Snowball;
    public List<Vector2Int> path = new();

    public bool IsEnemy => kind == Snowball || kind == Owl;
    public bool IsPlatform => kind == Platform;

    /// <summary>한 번 왕복하는 데 걸리는 턴 수 (길이 1이면 1 = 가만히).</summary>
    public int Period => path.Count <= 1 ? 1 : 2 * (path.Count - 1);

    /// <summary>turn 번째 턴의 위치.</summary>
    public Vector2Int PositionAt(int turn)
    {
        if (path.Count == 0) return Vector2Int.zero;
        if (path.Count == 1) return path[0];
        int i = ((turn % Period) + Period) % Period;
        return i < path.Count ? path[i] : path[Period - i];
    }

    public Mover Clone() => new() { kind = kind, path = new List<Vector2Int>(path) };
}

/// <summary>칸의 바닥 종류 (판 상태와 상관없는 원래 모습).</summary>
public enum CellType
{
    Ground,
    Hole,
    Obstacle,
    Ice,
    Crumble,
    Switch,
    Door,
    Stairs,
    /// <summary>레버: 부딪히면 당겨서 채널을 켜고 끈다. 바위처럼 막혀 있다.</summary>
    Lever,
    /// <summary>누름판: 여우나 블록이 올라가 있는 동안 채널을 켠다.</summary>
    Plate,
    /// <summary>솟는 다리: 채널이 꺼지면 구멍, 켜지면 땅.</summary>
    Bridge,
    /// <summary>승강 땅: 채널이 꺼지면 floors 층, 켜지면 floorsOn 층.</summary>
    Lift,
}

/// <summary>
/// 스테이지 한 장. JSON으로 그대로 저장된다 (MapJson). 세 층이 같은 모양의 문자열 배열이다.
///   rows     바닥: '.' 땅, 'O' 구멍, '#' 장애물, 'I' 얼음, 'C' 무너지는 칸, 'W' 스위치, 'D' 문, 'T' 계단,
///                  'L' 레버, 'P' 누름판, 'B' 솟는 다리, 'E' 승강 땅
///   items    물건: '.' 없음, 'G' 보석, 'S' 시작 위치, 'K' 미는 블록
///   floors   층:   '1'~'9'
///   channels 장치·바뀌는 땅의 채널 '1'~'4' (그 밖은 '.')
///   floorsOn 승강 땅이 켜졌을 때 층 '1'~'9' (그 밖은 '.')
/// rows[0]이 가장 먼 줄(+Z), 마지막 줄이 카메라 쪽(-Z). 좌표: x 왼쪽→오른쪽, y 아래(카메라 쪽)→위.
/// </summary>
[Serializable]
public class MapData
{
    public const int MinSize = 2;
    public const int MaxSize = 16;
    public const int MinFloor = 1;
    public const int MaxFloor = 9;
    /// <summary>5: 장치(레버·누름판·블록, 솟는 다리·승강 땅, channels·floorsOn). 4: 움직이는 것(movers). 3: 바닥(rows)과 물건(items) 분리, 퍼즐 칸 추가. 2: floors 추가. 1: 처음.</summary>
    public const int CurrentVersion = 5;

    // 바닥
    public const char Ground = '.', Hole = 'O', Obstacle = '#', Ice = 'I', Crumble = 'C', Switch = 'W', Door = 'D', Stairs = 'T',
        Lever = 'L', Plate = 'P', Bridge = 'B', Lift = 'E';
    public static readonly char[] TerrainSymbols = { Ground, Hole, Obstacle, Ice, Crumble, Switch, Door, Stairs, Lever, Plate, Bridge, Lift };
    // 물건
    public const char NoItem = '.', Gem = 'G', Start = 'S', Block = 'K';
    public static readonly char[] ItemSymbols = { NoItem, Gem, Start, Block };
    // 채널
    public const int MaxChannel = 4;
    public const char NoChannel = '.';

    public int version = CurrentVersion;
    public string name = "새 스테이지";
    /// <summary>기록을 찾는 고유 이름. 맵툴이 처음 저장할 때 정하고 그 뒤로는 바뀌지 않는다 (없으면 name 을 쓴다).</summary>
    public string id;
    public int width;
    public int height;
    public string[] rows;
    public string[] items;
    public string[] floors;
    /// <summary>눈덩이·부엉이·발판 (버전 4).</summary>
    public List<Mover> movers = new();
    /// <summary>장치·바뀌는 땅의 채널 (버전 5).</summary>
    public string[] channels;
    /// <summary>승강 땅이 켜졌을 때 층 (버전 5).</summary>
    public string[] floorsOn;

    public int Width => width;
    public int Height => height;

    public static MapData CreateEmpty(int width, int height, string name = "새 스테이지")
    {
        var map = new MapData { name = name, width = width, height = height, rows = Fill(width, height, Ground) };
        map.items = Fill(width, height, NoItem);
        map.FillFloors(MinFloor);
        map.EnsureMechanismGrids();
        map.Set(new Vector2Int(0, 0), Start);
        return map;
    }

    static string[] Fill(int w, int h, char c)
    {
        var a = new string[h];
        for (int r = 0; r < h; r++) a[r] = new string(c, w);
        return a;
    }

    public MapData Clone()
    {
        EnsureItems();
        return new MapData
        {
            version = version, name = name, id = id, width = width, height = height,
            rows = (string[])rows.Clone(), items = (string[])items.Clone(), floors = (string[])floors?.Clone(),
            movers = movers == null ? new List<Mover>() : movers.ConvertAll(m => m.Clone()),
            channels = (string[])channels?.Clone(), floorsOn = (string[])floorsOn?.Clone(),
        };
    }

    /// <summary>channels·floorsOn 이 없으면(버전 4 이하) 빈 칸('.')으로 채운다.</summary>
    public void EnsureMechanismGrids()
    {
        if (channels == null || channels.Length != height) channels = Fill(width, height, NoChannel);
        if (floorsOn == null || floorsOn.Length != height) floorsOn = Fill(width, height, NoChannel);
    }

    /// <summary>floors 가 없으면(버전 1 파일) 모든 칸을 n층으로 채운다.</summary>
    public void FillFloors(int floor) => floors = Fill(width, height, (char)('0' + floor));

    /// <summary>
    /// 버전 1·2 파일 변환: rows 에 섞여 있던 보석·시작을 items 로 옮기고 그 자리 바닥은 땅으로.
    /// </summary>
    public void SplitItemsFromRows()
    {
        items = Fill(width, height, NoItem);
        for (int r = 0; r < height; r++)
        {
            var row = rows[r].ToCharArray();
            var it = items[r].ToCharArray();
            for (int x = 0; x < width; x++)
                if (row[x] == Gem || row[x] == Start)
                {
                    it[x] = row[x];
                    row[x] = Ground;
                }
            rows[r] = new string(row);
            items[r] = new string(it);
        }
    }

    // ------------------------------------------------------------ 조회
    public bool IsInside(Vector2Int c) => c.x >= 0 && c.y >= 0 && c.x < width && c.y < height;

    int Row(Vector2Int c) => height - 1 - c.y;

    /// <summary>items 가 없으면(옛 방식으로 rows 에 보석·시작을 적은 맵) 처음 쓸 때 나눈다.</summary>
    void EnsureItems()
    {
        if (items == null || items.Length == 0) SplitItemsFromRows();
    }

    /// <summary>바닥 기호.</summary>
    public char SymbolAt(Vector2Int c)
    {
        EnsureItems();
        return rows[Row(c)][c.x];
    }

    /// <summary>물건 기호 ('.', 'G', 'S').</summary>
    public char ItemAt(Vector2Int c)
    {
        EnsureItems();
        return items[Row(c)][c.x];
    }

    public CellType TypeAt(Vector2Int c)
    {
        if (!IsInside(c)) return CellType.Hole;   // 맵 밖은 구멍과 같음 (떨어짐)
        return SymbolAt(c) switch
        {
            Hole => CellType.Hole,
            Obstacle => CellType.Obstacle,
            Ice => CellType.Ice,
            Crumble => CellType.Crumble,
            Switch => CellType.Switch,
            Door => CellType.Door,
            Stairs => CellType.Stairs,
            Lever => CellType.Lever,
            Plate => CellType.Plate,
            Bridge => CellType.Bridge,
            Lift => CellType.Lift,
            _ => CellType.Ground,
        };
    }

    /// <summary>여우가 설 수 있는 바닥인지 (문은 열렸을 때만, 다리는 켜졌을 때만이라 여기서는 제외. 레버는 막힘).</summary>
    public static bool IsStandable(CellType t) =>
        t != CellType.Hole && t != CellType.Obstacle && t != CellType.Door && t != CellType.Lever && t != CellType.Bridge;

    /// <summary>채널이 붙는 칸 (장치·바뀌는 땅).</summary>
    public static bool HasChannel(CellType t) => t == CellType.Lever || t == CellType.Plate || t == CellType.Bridge || t == CellType.Lift;

    /// <summary>칸의 채널 1~4 (없으면 0).</summary>
    public int ChannelAt(Vector2Int c)
    {
        if (!IsInside(c) || channels == null || channels.Length != height) return 0;
        char ch = channels[Row(c)][c.x];
        return ch >= '1' && ch <= '0' + MaxChannel ? ch - '0' : 0;
    }

    /// <summary>승강 땅이 켜졌을 때 층 (정하지 않았으면 원래 층).</summary>
    public int FloorOnAt(Vector2Int c)
    {
        if (!IsInside(c) || floorsOn == null || floorsOn.Length != height) return FloorAt(c);
        char ch = floorsOn[Row(c)][c.x];
        return ch >= '0' + MinFloor && ch <= '0' + MaxFloor ? ch - '0' : FloorAt(c);
    }

    /// <summary>칸의 층 (1~9). 맵 밖은 0.</summary>
    public int FloorAt(Vector2Int c)
    {
        if (!IsInside(c)) return 0;
        return floors == null ? MinFloor : floors[Row(c)][c.x] - '0';   // floors 가 없으면 모두 1층
    }

    public int HighestFloor
    {
        get
        {
            int max = MinFloor;
            for (int y = 0; y < height; y++)
                for (int x = 0; x < width; x++)
                {
                    var c = new Vector2Int(x, y);
                    if (TypeAt(c) != CellType.Hole) max = Mathf.Max(max, FloorAt(c));
                    if (TypeAt(c) == CellType.Lift) max = Mathf.Max(max, FloorOnAt(c));
                }
            return max;
        }
    }

    // ------------------------------------------------------------ 편집
    /// <summary>
    /// 칸 하나를 바꾼다. 보석·시작이면 물건을, 그 밖이면 바닥을 바꾼다.
    /// 시작은 하나뿐이라 새로 놓으면 이전 시작은 지워진다.
    /// 땅·구멍·장애물·문을 칠하면 그 칸의 물건도 지운다 (땅 붓 = 지우개). 설 수 없는 칸에 물건을 놓으면 바닥을 땅으로.
    /// </summary>
    public void Set(Vector2Int c, char symbol)
    {
        if (!IsInside(c)) return;
        EnsureItems();
        if (symbol == Gem || symbol == Start || symbol == Block)
        {
            if (symbol == Start)
                foreach (var s in Find(Start)) SetItem(s, NoItem);
            if (!IsStandable(TypeAt(c)))
            {
                SetTerrain(c, Ground);
                SetMechanism(c, 0, 0);
            }
            SetItem(c, symbol);
            return;
        }
        SetTerrain(c, symbol);
        if (symbol == Ground || symbol == Hole || symbol == Obstacle || symbol == Door || symbol == Lever || symbol == Bridge) SetItem(c, NoItem);
        if (!HasChannel(TypeAt(c))) SetMechanism(c, 0, 0);
        else if (ChannelAt(c) == 0) SetMechanism(c, 1, 0);
    }

    /// <summary>칸의 채널(0 = 없음)과 승강 땅 켜짐 층(0 = 없음)을 정한다.</summary>
    public void SetMechanism(Vector2Int c, int channel, int floorOn)
    {
        if (!IsInside(c)) return;
        EnsureMechanismGrids();
        channels[Row(c)] = Replace(channels[Row(c)], c.x, channel > 0 ? (char)('0' + Mathf.Clamp(channel, 1, MaxChannel)) : NoChannel);
        floorsOn[Row(c)] = Replace(floorsOn[Row(c)], c.x, floorOn > 0 ? (char)('0' + Mathf.Clamp(floorOn, MinFloor, MaxFloor)) : NoChannel);
    }

    void SetTerrain(Vector2Int c, char ch) => rows[Row(c)] = Replace(rows[Row(c)], c.x, ch);
    void SetItem(Vector2Int c, char ch) => items[Row(c)] = Replace(items[Row(c)], c.x, ch);

    public void SetFloor(Vector2Int c, int floor)
    {
        if (!IsInside(c)) return;
        if (floors == null) FillFloors(MinFloor);
        floor = Mathf.Clamp(floor, MinFloor, MaxFloor);
        floors[Row(c)] = Replace(floors[Row(c)], c.x, (char)('0' + floor));
    }

    static string Replace(string row, int index, char ch)
    {
        var chars = row.ToCharArray();
        chars[index] = ch;
        return new string(chars);
    }

    /// <summary>크기를 바꾼다. 왼쪽 아래(카메라 쪽, 보통 시작 위치가 있는 곳)를 기준으로 자르거나 1층 땅으로 늘린다.</summary>
    public void Resize(int newWidth, int newHeight)
    {
        newWidth = Mathf.Clamp(newWidth, MinSize, MaxSize);
        newHeight = Mathf.Clamp(newHeight, MinSize, MaxSize);
        EnsureItems();
        var nr = new string[newHeight];
        var ni = new string[newHeight];
        var nf = new string[newHeight];
        EnsureMechanismGrids();
        var nc = new string[newHeight];
        var no = new string[newHeight];
        for (int y = 0; y < newHeight; y++)
        {
            var r = new char[newWidth];
            var it = new char[newWidth];
            var f = new char[newWidth];
            var ch = new char[newWidth];
            var on = new char[newWidth];
            for (int x = 0; x < newWidth; x++)
            {
                bool keep = x < width && y < height;
                var c = new Vector2Int(x, y);
                r[x] = keep ? SymbolAt(c) : Ground;
                it[x] = keep ? ItemAt(c) : NoItem;
                f[x] = keep ? (char)('0' + FloorAt(c)) : (char)('0' + MinFloor);
                ch[x] = keep ? channels[Row(c)][x] : NoChannel;
                on[x] = keep ? floorsOn[Row(c)][x] : NoChannel;
            }
            nr[newHeight - 1 - y] = new string(r);
            ni[newHeight - 1 - y] = new string(it);
            nf[newHeight - 1 - y] = new string(f);
            nc[newHeight - 1 - y] = new string(ch);
            no[newHeight - 1 - y] = new string(on);
        }
        rows = nr;
        items = ni;
        floors = nf;
        channels = nc;
        floorsOn = no;
        width = newWidth;
        height = newHeight;
        // 길은 맵 밖으로 나가는 칸부터 잘라낸다. 다 잘리면 지운다.
        if (movers != null)
        {
            foreach (var m in movers)
            {
                int cut = m.path.FindIndex(c => !IsInside(c));
                if (cut >= 0) m.path.RemoveRange(cut, m.path.Count - cut);
            }
            movers.RemoveAll(m => m.path.Count == 0);
        }
    }

    public Vector2Int StartCell
    {
        get
        {
            var s = Find(Start);
            return s.Count > 0 ? s[0] : Vector2Int.zero;
        }
    }

    public List<Vector2Int> Gems => Find(Gem);
    public List<Vector2Int> Blocks => Find(Block);

    /// <summary>turn 번째 턴에 c 에 발판이 있는지.</summary>
    public bool PlatformAt(Vector2Int c, int turn)
    {
        if (movers == null) return false;
        foreach (var m in movers)
            if (m.IsPlatform && m.path.Count > 0 && m.PositionAt(turn) == c) return true;
        return false;
    }

    /// <summary>turn 번째 턴에 적이 있는 칸들.</summary>
    public HashSet<Vector2Int> EnemiesAt(int turn)
    {
        var set = new HashSet<Vector2Int>();
        if (movers == null) return set;
        foreach (var m in movers)
            if (m.IsEnemy && m.path.Count > 0) set.Add(m.PositionAt(turn));
        return set;
    }

    /// <summary>모든 움직이는 것의 움직임이 처음으로 돌아오는 턴 수 (최소공배수).</summary>
    public int MoverCycle
    {
        get
        {
            int l = 1;
            if (movers != null)
                foreach (var m in movers)
                {
                    int p = m.Period, a = l, b = p;
                    while (b != 0) { int t = a % b; a = b; b = t; }
                    l = l / a * p;
                    if (l > 100000) return l;
                }
            return l;
        }
    }

    /// <summary>기호가 있는 칸들. 보석·시작('G','S')은 물건 층에서, 그 밖은 바닥에서 찾는다.</summary>
    public List<Vector2Int> Find(char symbol)
    {
        bool item = symbol == Gem || symbol == Start || symbol == Block;
        var found = new List<Vector2Int>();
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
            {
                var c = new Vector2Int(x, y);
                if ((item ? ItemAt(c) : SymbolAt(c)) == symbol) found.Add(c);
            }
        return found;
    }
}
