using System.Collections.Generic;
using UnityEngine;

public class MapValidation
{
    public readonly List<string> errors = new();
    public readonly List<string> warnings = new();
    /// <summary>어떤 길로도 먹을 수 없는 보석.</summary>
    public readonly List<Vector2Int> unreachableGems = new();
    public bool IsValid => errors.Count == 0;
}

/// <summary>
/// 저장 전 검증: 시작 1개, 보석 1개 이상, 보석·시작이 설 수 있는 칸 위, 한 판에 모든 보석을 모을 수 있음.
/// "모을 수 있음"은 (칸, 바라보는 방향, 문 열림, 무너진 칸들, 모은 보석들, 턴 주기, 블록·메운 구멍, 켜진 레버) 상태를 넓이 우선 탐색해서
/// MoveRules 로 계산한다. 상태가 너무 많으면(맵이 복잡하면) 경고만 내고 통과시킨다.
/// </summary>
public static class MapValidator
{
    public const int StateLimit = 500_000;

    public static MapValidation Validate(MapData map)
    {
        var v = new MapValidation();
        var starts = map.Find(MapData.Start);
        var gems = map.Gems;

        if (starts.Count == 0) v.errors.Add("시작 위치가 없습니다.");
        else if (starts.Count > 1) v.errors.Add($"시작 위치가 {starts.Count}개입니다. 하나만 둘 수 있습니다.");
        if (gems.Count == 0) v.errors.Add("보석이 없습니다. 1개 이상 놓아야 클리어할 수 있습니다.");

        foreach (var c in starts)
            if (!MapData.IsStandable(map.TypeAt(c))) v.errors.Add($"시작 위치 ({c.x},{c.y}) 가 설 수 없는 칸 위에 있습니다.");
        foreach (var c in gems)
            if (!MapData.IsStandable(map.TypeAt(c))) v.errors.Add($"보석 ({c.x},{c.y}) 이 설 수 없는 칸 위에 있습니다.");
        foreach (var c in map.Blocks)
            if (!MapData.IsStandable(map.TypeAt(c))) v.errors.Add($"블록 ({c.x},{c.y}) 이 놓일 수 없는 칸 위에 있습니다.");
        CheckMechanisms(map, v);
        CheckMovers(map, starts, v);

        if (starts.Count == 1 && gems.Count > 0 && v.IsValid) Search(map, starts[0], gems, v);
        return v;
    }

    static readonly Dictionary<string, string> KindNames = new()
    {
        [Mover.Snowball] = "눈덩이", [Mover.Owl] = "부엉이", [Mover.Platform] = "발판",
    };

    /// <summary>움직이는 것의 길 검사: 눈덩이는 설 수 있는 땅, 발판은 구멍, 부엉이는 아무 칸. 이웃한 칸끼리, 적이 시작 칸에서 출발하지 않음.</summary>
    static void CheckMovers(MapData map, List<Vector2Int> starts, MapValidation v)
    {
        if (map.movers == null) return;
        for (int i = 0; i < map.movers.Count; i++)
        {
            var m = map.movers[i];
            string who = $"{KindNames.GetValueOrDefault(m.kind, m.kind)} {i + 1}";
            if (m.path.Count == 0) { v.errors.Add($"{who}: 길이 비어 있습니다."); continue; }
            for (int k = 0; k < m.path.Count; k++)
            {
                var c = m.path[k];
                if (!map.IsInside(c)) { v.errors.Add($"{who}: 길이 맵 밖으로 나갑니다."); break; }
                if (k > 0 && Mathf.Abs(c.x - m.path[k - 1].x) + Mathf.Abs(c.y - m.path[k - 1].y) != 1)
                { v.errors.Add($"{who}: 길의 {k}·{k + 1}번째 칸이 이웃하지 않습니다."); break; }
                var t = map.TypeAt(c);
                if (m.kind == Mover.Snowball && !MapData.IsStandable(t))
                { v.errors.Add($"{who}: ({c.x},{c.y}) 는 눈덩이가 굴러갈 수 없는 칸입니다 (땅 위만)."); break; }
                if (m.kind == Mover.Platform && t != CellType.Hole)
                { v.errors.Add($"{who}: ({c.x},{c.y}) 는 구멍이 아니라서 발판이 다닐 수 없습니다."); break; }
            }
            if (m.IsEnemy && starts.Count == 1 && m.PositionAt(0) == starts[0])
                v.errors.Add($"{who}: 여우 시작 칸에서 출발합니다.");
        }
        if (map.MoverCycle > 5000)
            v.warnings.Add("움직이는 것들의 주기가 너무 길어 검증이 느리거나 끝까지 확인하지 못할 수 있습니다. 길 길이를 맞춰 보세요.");
    }

    /// <summary>장치 검사: 채널마다 켜는 장치(레버·누름판)와 바뀌는 땅(다리·승강 땅)이 함께 있는지, 승강 땅 높이가 바뀌는지.</summary>
    static void CheckMechanisms(MapData map, MapValidation v)
    {
        var activators = new HashSet<int>();
        var targets = new HashSet<int>();
        for (int y = 0; y < map.Height; y++)
        for (int x = 0; x < map.Width; x++)
        {
            var c = new Vector2Int(x, y);
            var t = map.TypeAt(c);
            if (!MapData.HasChannel(t)) continue;
            int ch = map.ChannelAt(c);
            if (ch == 0) { v.errors.Add($"({x},{y}) 의 장치에 채널이 없습니다."); continue; }
            if (t == CellType.Lever || t == CellType.Plate) activators.Add(ch);
            else targets.Add(ch);
            if (t == CellType.Lift && map.FloorOnAt(c) == map.FloorAt(c))
                v.warnings.Add($"승강 땅 ({x},{y}) 의 켜짐 층이 꺼짐 층과 같아 움직이지 않습니다.");
        }
        foreach (int ch in targets)
            if (!activators.Contains(ch)) v.warnings.Add($"채널 {ch} 에 레버·누름판이 없어 다리·승강 땅이 바뀌지 않습니다.");
        foreach (int ch in activators)
            if (!targets.Contains(ch)) v.warnings.Add($"채널 {ch} 의 레버·누름판이 바꾸는 땅이 없습니다.");
    }

    // ------------------------------------------------------------ 상태 탐색
    readonly struct Node : System.IEquatable<Node>
    {
        public readonly Vector2Int cell;
        public readonly byte facing;
        public readonly bool doors;
        public readonly ulong crumbled;
        public readonly ulong collected;
        public readonly int turn;   // 움직이는 것들 주기 안의 턴
        public readonly ulong levers;   // 켜진 레버 (레버 번호 비트)
        public readonly string objects; // 블록 위치·메운 구멍 (정렬한 문자열, 없으면 "")

        public Node(Vector2Int cell, byte facing, bool doors, ulong crumbled, ulong collected, int turn, ulong levers, string objects)
        {
            this.cell = cell; this.facing = facing; this.doors = doors; this.crumbled = crumbled; this.collected = collected; this.turn = turn;
            this.levers = levers; this.objects = objects;
        }

        public bool Equals(Node o) => cell == o.cell && facing == o.facing && doors == o.doors && crumbled == o.crumbled &&
                                      collected == o.collected && turn == o.turn && levers == o.levers && objects == o.objects;
        public override bool Equals(object obj) => obj is Node n && Equals(n);
        public override int GetHashCode()
        {
            unchecked
            {
                int h = cell.GetHashCode();
                h = h * 31 + facing;
                h = h * 31 + (doors ? 1 : 0);
                h = h * 31 + crumbled.GetHashCode();
                h = h * 31 + collected.GetHashCode();
                h = h * 31 + turn;
                h = h * 31 + levers.GetHashCode();
                h = h * 31 + objects.GetHashCode();
                return h;
            }
        }
    }

    /// <summary>블록·메운 구멍을 한 문자열로: 칸 번호 순서대로 블록은 "b", 메운 구멍은 "f층".</summary>
    static string ObjectsKey(MapData map, PuzzleState s)
    {
        if (s.blocks.Count == 0 && s.filled.Count == 0) return "";
        var parts = new List<int>();
        foreach (var b in s.blocks) parts.Add((b.y * map.Width + b.x) * 16);
        foreach (var kv in s.filled) parts.Add((kv.Key.y * map.Width + kv.Key.x) * 16 + kv.Value);
        parts.Sort();
        var chars = new char[parts.Count];
        for (int i = 0; i < parts.Count; i++) chars[i] = (char)(parts[i] + 1);
        return new string(chars);
    }

    static void ObjectsFromKey(MapData map, string key, PuzzleState s)
    {
        foreach (char ch in key)
        {
            int v = ch - 1, idx = v / 16, floor = v % 16;
            var c = new Vector2Int(idx % map.Width, idx / map.Width);
            if (floor == 0) s.blocks.Add(c);
            else s.filled[c] = floor;
        }
    }

    static void Search(MapData map, Vector2Int start, List<Vector2Int> gems, MapValidation v)
    {
        var crumbles = map.Find(MapData.Crumble);
        var levers = map.Find(MapData.Lever);
        if (gems.Count > 64 || crumbles.Count > 64 || levers.Count > 64)
        {
            v.warnings.Add("보석·무너지는 칸·레버가 64개를 넘어 클리어 가능 여부를 끝까지 확인하지 못했습니다.");
            return;
        }
        var gemIndex = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < gems.Count; i++) gemIndex[gems[i]] = i;
        var crumbleIndex = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < crumbles.Count; i++) crumbleIndex[crumbles[i]] = i;
        var leverIndex = new Dictionary<Vector2Int, int>();
        for (int i = 0; i < levers.Count; i++) leverIndex[levers[i]] = i;
        ulong all = gems.Count == 64 ? ulong.MaxValue : (1UL << gems.Count) - 1;

        var seen = new HashSet<Node>();
        var queue = new Queue<Node>();
        ulong everCollected = 0;
        bool solved = false;

        int cycle = Mathf.Max(1, map.MoverCycle);

        PuzzleState ToState(Node n)
        {
            var s = new PuzzleState { doorsOpen = n.doors, turn = n.turn };
            for (int i = 0; i < crumbles.Count; i++)
                if ((n.crumbled & (1UL << i)) != 0) s.crumbled.Add(crumbles[i]);
            for (int i = 0; i < levers.Count; i++)
                if ((n.levers & (1UL << i)) != 0) s.leversOn.Add(levers[i]);
            ObjectsFromKey(map, n.objects, s);
            MoveRules.UpdateChannels(map, s, n.cell);
            return s;
        }

        void Push(Node n)
        {
            if (!seen.Add(n)) return;
            everCollected |= n.collected;
            if (n.collected == all) solved = true;
            queue.Enqueue(n);
        }

        byte FacingIndex(Vector2Int d) => (byte)System.Array.IndexOf(MoveRules.Directions, d);

        Push(new Node(start, FacingIndex(MoveRules.StartFacing), false, 0, 0, 0, 0, ObjectsKey(map, PuzzleState.Initial(map, start))));
        while (queue.Count > 0 && !solved)
        {
            if (seen.Count > StateLimit)
            {
                v.warnings.Add($"맵이 복잡해 상태 {StateLimit:N0}개까지만 확인했습니다. 클리어할 수 있는지 직접 플레이해 확인해 주세요.");
                return;
            }
            var n = queue.Dequeue();
            var dirs = MoveRules.Directions;
            // 행동: 4방향 걷기, 점프, 기다리기 — 모두 한 턴
            for (int a = 0; a <= dirs.Length + 1; a++)
            {
                MoveResult r = a < dirs.Length ? MoveRules.Walk(map, ToState(n), n.cell, dirs[a])
                    : a == dirs.Length ? MoveRules.Jump(map, ToState(n), n.cell, dirs[n.facing])
                    : TurnRules.Wait(n.cell, dirs[n.facing]);
                if (r.outcome == MoveOutcome.Fall) continue;
                var st = ToState(n);
                var changes = MoveRules.Apply(map, st, n.cell, r);
                if (changes.foxFalls) continue;
                var turn = TurnRules.Advance(map, st, n.cell, r);
                if (turn.hit) continue;
                ulong leverBits = 0;
                foreach (var l in st.leversOn) leverBits |= 1UL << leverIndex[l];
                ulong crumbled = n.crumbled;
                foreach (var c in st.crumbled) crumbled |= 1UL << crumbleIndex[c];
                ulong collected = n.collected;
                foreach (var c in r.path)
                    if (gemIndex.TryGetValue(c, out int gi)) collected |= 1UL << gi;
                Push(new Node(turn.cell, FacingIndex(r.facing), st.doorsOpen, crumbled, collected, st.turn % cycle, leverBits, ObjectsKey(map, st)));
            }
        }

        if (solved) return;
        for (int i = 0; i < gems.Count; i++)
            if ((everCollected & (1UL << i)) == 0) v.unreachableGems.Add(gems[i]);
        if (v.unreachableGems.Count > 0)
            v.errors.Add($"갈 수 없는 보석이 {v.unreachableGems.Count}개 있습니다: " +
                         string.Join(" ", v.unreachableGems.ConvertAll(g => $"({g.x},{g.y})")));
        else
            v.errors.Add("보석마다 갈 수는 있지만 한 판에 모두 모을 수 없습니다. 무너지는 칸의 순서, 적·발판의 타이밍, 블록·장치를 확인해 주세요.");
    }
}
