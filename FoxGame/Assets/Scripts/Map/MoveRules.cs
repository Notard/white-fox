using System.Collections.Generic;
using UnityEngine;

public enum MoveOutcome
{
    /// <summary>땅으로 이동 (얼음이면 미끄러져 멈춘 칸까지). 블록을 밀며 들어가는 것도 이동.</summary>
    Move,
    /// <summary>장애물·닫힌 문·너무 높은 칸·안 밀리는 블록에 막혀 제자리. 바라보는 방향만 바뀐다. 레버는 당긴다.</summary>
    Blocked,
    /// <summary>구멍·무너진 칸·꺼진 다리·맵 밖으로 가서 떨어진다.</summary>
    Fall,
}

/// <summary>
/// 판의 바뀌는 상태: 무너진 칸, 문 열림, 지금 턴(움직이는 것들의 위치), 블록 위치, 블록으로 메운 구멍, 켜진 레버, 켜진 채널.
/// 게임 중에는 Board 가, 검증 중에는 탐색이 들고 있다.
/// </summary>
public class PuzzleState
{
    public readonly HashSet<Vector2Int> crumbled = new();
    public bool doorsOpen;
    /// <summary>지금까지 지난 턴 수. 적·발판 위치는 이 값으로 정해진다.</summary>
    public int turn;
    /// <summary>미는 블록이 있는 칸.</summary>
    public readonly HashSet<Vector2Int> blocks = new();
    /// <summary>블록이 떨어져 메운 구멍 → 그 칸의 층.</summary>
    public readonly Dictionary<Vector2Int, int> filled = new();
    /// <summary>켜진(당겨진) 레버.</summary>
    public readonly HashSet<Vector2Int> leversOn = new();
    /// <summary>켜진 채널 비트 (1 &lt;&lt; 채널). MoveRules.UpdateChannels 가 계산한다.</summary>
    public int channels;

    public bool ChannelOn(int channel) => channel > 0 && (channels & (1 << channel)) != 0;

    /// <summary>맵의 처음 상태: 블록은 놓인 자리에, 여우가 처음 서 있는 누름판은 켜짐.</summary>
    public static PuzzleState Initial(MapData map, Vector2Int fox)
    {
        var s = new PuzzleState();
        s.blocks.UnionWith(map.Blocks);
        MoveRules.UpdateChannels(map, s, fox);
        return s;
    }

    public PuzzleState Clone()
    {
        var s = new PuzzleState { doorsOpen = doorsOpen, turn = turn, channels = channels };
        s.crumbled.UnionWith(crumbled);
        s.blocks.UnionWith(blocks);
        foreach (var kv in filled) s.filled[kv.Key] = kv.Value;
        s.leversOn.UnionWith(leversOn);
        return s;
    }
}

public class MoveResult
{
    public MoveOutcome outcome;
    /// <summary>이동 후 칸 (떨어지면 떨어진 칸).</summary>
    public Vector2Int cell;
    public Vector2Int facing;
    /// <summary>지나간 칸들 (시작 칸 제외, 마지막 = cell). 얼음에서 미끄러지면 여러 칸.</summary>
    public readonly List<Vector2Int> path = new();
    /// <summary>점프로 이동했는지 (path[0] 이 착지 칸).</summary>
    public bool jumped;
    /// <summary>민 블록: pushFrom 칸의 블록이 pushTo 로 (구멍이면 떨어져 메움).</summary>
    public Vector2Int? pushFrom, pushTo;
    /// <summary>당긴 레버 칸 (Blocked 와 함께).</summary>
    public Vector2Int? pulled;

    public bool Moved => outcome != MoveOutcome.Blocked && path.Count > 0;
}

/// <summary>
/// 여우의 이동 규칙. 게임(FoxController)과 맵 검증(MapValidator)이 같은 코드를 쓴다.
/// 걷기: 한 칸. 장애물·닫힌 문·레버이거나 WalkClimb 층보다 높으면 제자리(방향만 바뀜). 구멍·무너진 칸·맵 밖이면 떨어짐.
///       계단 칸과 그 옆 칸 사이는 높이 차와 상관없이 걸을 수 있다.
///       레버 쪽으로 걸으면 제자리에서 레버를 당긴다. 블록 쪽으로 걸으면 블록을 한 칸 밀고 들어간다.
/// 밀기: 블록 건너편이 맵 안이고, 막히지 않았고(장애물·레버·닫힌 문·다른 블록), 블록 칸보다 높지 않으면 밀린다.
///       건너편이 구멍이면 블록이 떨어져 그 구멍을 메운다 (블록이 있던 칸 높이의 땅). 발판이 다니는 구멍은 메울 수 없다.
///       밀고 들어간 칸이 얼음이어도 미끄러지지 않는다.
/// 점프: 바로 앞 칸이 1~JumpClimb 층 높은 설 수 있는 칸이면 그 위로. 아니면 두 칸 앞에 착지.
///       착지 칸이 장애물·닫힌 문·블록·레버이거나 JumpClimb 층보다 높거나, 가운데 칸이 출발·착지 중 높은 쪽보다
///       WallOverJump 층 이상 솟아 있으면 제자리. 구멍·맵 밖이면 떨어짐.
/// 얼음: 얼음 칸에 들어서면 같은 방향으로 계속 미끄러진다. 얼음이 아닌 칸에 들어서면 멈추고,
///       앞이 장애물·닫힌 문·더 높은 칸이면 그 얼음 칸에서 멈추며, 구멍이면 떨어진다.
/// 내려가는 것은 몇 층이든 된다.
/// 상태 변화(Apply): 무너지는 칸을 떠나면 구멍, 스위치 칸에 서면 모든 문이 열린다, 블록 이동·메우기, 레버 바꾸기,
///       채널 다시 계산(켜진 레버 + 누름판 위 여우·블록). 꺼진 다리 위의 블록은 떨어져 메우고, 여우는 떨어진다.
/// 채널이 켜지면: 솟는 다리는 땅, 승강 땅은 floorsOn 층.
/// </summary>
public static class MoveRules
{
    public const int WalkClimb = 1;
    public const int JumpClimb = 2;
    public const int WallOverJump = 2;
    const int MaxSlide = 64;

    public static readonly Vector2Int[] Directions = { Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right };

    /// <summary>시작할 때 여우가 바라보는 방향 (카메라 쪽).</summary>
    public static readonly Vector2Int StartFacing = Vector2Int.down;

    /// <summary>
    /// 판 상태까지 반영한 칸 종류. 블록·레버·닫힌 문 = 장애물, 무너진 칸·꺼진 다리 = 구멍,
    /// 열린 문·발판이 있는 구멍·메운 구멍·누름판·승강 땅·켜진 다리 = 땅.
    /// </summary>
    public static CellType Kind(MapData map, PuzzleState state, Vector2Int c)
    {
        if (state != null)
        {
            if (state.blocks.Contains(c)) return CellType.Obstacle;
            if (state.filled.ContainsKey(c)) return CellType.Ground;
        }
        var t = map.TypeAt(c);
        switch (t)
        {
            case CellType.Hole when map.IsInside(c) && map.PlatformAt(c, state?.turn ?? 0): return CellType.Ground;
            case CellType.Crumble when state != null && state.crumbled.Contains(c): return CellType.Hole;
            case CellType.Door: return state != null && state.doorsOpen ? CellType.Ground : CellType.Obstacle;
            case CellType.Lever: return CellType.Obstacle;
            case CellType.Bridge: return state != null && state.ChannelOn(map.ChannelAt(c)) ? CellType.Ground : CellType.Hole;
            case CellType.Plate:
            case CellType.Lift: return CellType.Ground;
        }
        return t;
    }

    /// <summary>판 상태까지 반영한 칸 높이. 메운 구멍은 메운 높이, 켜진 승강 땅은 floorsOn 층.</summary>
    public static int Floor(MapData map, PuzzleState state, Vector2Int c)
    {
        if (state != null)
        {
            if (state.filled.TryGetValue(c, out int f)) return f;
            if (map.TypeAt(c) == CellType.Lift && state.ChannelOn(map.ChannelAt(c))) return map.FloorOnAt(c);
        }
        return map.FloorAt(c);
    }

    static bool Blocks(CellType k) => k == CellType.Obstacle;
    static bool Falls(CellType k) => k == CellType.Hole;

    // ------------------------------------------------------------ 걷기
    public static MoveResult Walk(MapData map, PuzzleState state, Vector2Int from, Vector2Int dir)
    {
        var r = new MoveResult { cell = from, facing = dir };
        var target = from + dir;
        if (state != null && state.blocks.Contains(target)) return Push(map, state, r, from, target, dir);
        if (map.IsInside(target) && map.TypeAt(target) == CellType.Lever)
        {
            r.outcome = MoveOutcome.Blocked;
            r.pulled = target;
            return r;
        }
        var k = Kind(map, state, target);
        if (Blocks(k)) { r.outcome = MoveOutcome.Blocked; return r; }
        if (Falls(k)) return FallAt(r, target);

        if (!StairsBetween(map, from, target) && Floor(map, state, target) - Floor(map, state, from) > WalkClimb) { r.outcome = MoveOutcome.Blocked; return r; }

        r.outcome = MoveOutcome.Move;
        r.path.Add(target);
        r.cell = target;
        if (k == CellType.Ice) Slide(map, state, r, dir);
        return r;
    }

    static bool StairsBetween(MapData map, Vector2Int a, Vector2Int b) =>
        map.TypeAt(a) == CellType.Stairs || map.TypeAt(b) == CellType.Stairs;

    // ------------------------------------------------------------ 밀기
    static MoveResult Push(MapData map, PuzzleState state, MoveResult r, Vector2Int from, Vector2Int target, Vector2Int dir)
    {
        r.outcome = MoveOutcome.Blocked;
        if (!StairsBetween(map, from, target) && Floor(map, state, target) - Floor(map, state, from) > WalkClimb) return r;
        var dest = target + dir;
        if (!map.IsInside(dest)) return r;
        var dk = Kind(map, state, dest);
        if (Blocks(dk)) return r;
        bool platformHole = map.TypeAt(dest) == CellType.Hole && !state.filled.ContainsKey(dest) && OnPlatformPath(map, dest);
        if (platformHole) return r;                                                     // 발판 길은 메울 수 없다
        if (!Falls(dk) && Floor(map, state, dest) > Floor(map, state, target)) return r;  // 더 높은 땅으로는 못 민다

        r.outcome = MoveOutcome.Move;
        r.path.Add(target);
        r.cell = target;
        r.pushFrom = target;
        r.pushTo = dest;
        return r;
    }

    static bool OnPlatformPath(MapData map, Vector2Int c)
    {
        if (map.movers == null) return false;
        foreach (var m in map.movers)
            if (m.IsPlatform && m.path.Contains(c)) return true;
        return false;
    }

    // 하위 호환 (판 상태 없이)
    public static MoveResult Walk(MapData map, Vector2Int from, Vector2Int dir) => Walk(map, null, from, dir);
    public static MoveResult Jump(MapData map, Vector2Int from, Vector2Int facing) => Jump(map, null, from, facing);

    // ------------------------------------------------------------ 점프
    public static MoveResult Jump(MapData map, PuzzleState state, Vector2Int from, Vector2Int facing)
    {
        var r = new MoveResult { cell = from, facing = facing, jumped = true };
        int start = Floor(map, state, from);
        var middle = from + facing;
        var mk = Kind(map, state, middle);

        // 바로 앞 칸이 1~JumpClimb 층 높은 설 수 있는 칸이면 그 위로 뛰어오른다
        if (!Blocks(mk) && !Falls(mk))
        {
            int up = Floor(map, state, middle) - start;
            if (up >= 1 && up <= JumpClimb) return Land(map, state, r, middle, facing, mk);
        }

        // 그 밖에는 두 칸 앞으로
        var target = from + facing * 2;
        var tk = Kind(map, state, target);
        if (Blocks(tk)) { r.outcome = MoveOutcome.Blocked; return r; }

        int top = Falls(tk) ? start : Mathf.Max(start, Floor(map, state, target));
        if (!Falls(mk) && Floor(map, state, middle) - top >= WallOverJump) { r.outcome = MoveOutcome.Blocked; return r; }   // 가운데 벽

        if (Falls(tk)) return FallAt(r, target);
        if (Floor(map, state, target) - start > JumpClimb) { r.outcome = MoveOutcome.Blocked; return r; }                     // 너무 높음
        return Land(map, state, r, target, facing, tk);
    }

    static MoveResult Land(MapData map, PuzzleState state, MoveResult r, Vector2Int at, Vector2Int dir, CellType k)
    {
        r.outcome = MoveOutcome.Move;
        r.path.Add(at);
        r.cell = at;
        if (k == CellType.Ice) Slide(map, state, r, dir);
        return r;
    }

    static MoveResult FallAt(MoveResult r, Vector2Int at)
    {
        r.outcome = MoveOutcome.Fall;
        r.path.Add(at);
        r.cell = at;
        return r;
    }

    // ------------------------------------------------------------ 얼음
    /// <summary>r.cell(얼음)에서 dir 로 미끄러진다. 지나간 칸을 path 에 더한다.</summary>
    static void Slide(MapData map, PuzzleState state, MoveResult r, Vector2Int dir)
    {
        for (int i = 0; i < MaxSlide; i++)
        {
            var cur = r.cell;
            var next = cur + dir;
            var k = Kind(map, state, next);
            if (Blocks(k)) return;                                                  // 막힘: 이 얼음 칸에서 멈춤
            if (Falls(k)) { FallAt(r, next); return; }                              // 미끄러져 떨어짐
            if (Floor(map, state, next) > Floor(map, state, cur)) return;           // 더 높은 칸 앞에서 멈춤
            r.path.Add(next);
            r.cell = next;
            if (k != CellType.Ice) return;                                          // 얼음이 아닌 칸에 들어서면 멈춤
        }
    }

    // ------------------------------------------------------------ 상태 변화
    public struct Changes
    {
        public Vector2Int? crumbled;    // 이번에 무너진 칸
        public bool doorsOpened;        // 이번에 문이 열렸는지
        public bool filledHole;         // 민 블록이 구멍에 떨어져 메웠는지
        public Vector2Int? leverPulled; // 당긴 레버
        public bool channelsChanged;    // 채널이 바뀌었는지 (다리·승강 땅이 움직임)
        public List<Vector2Int> blocksDropped;   // 꺼진 다리에서 떨어져 그 칸을 메운 블록
        public bool foxFalls;           // 여우가 선 땅이 사라져 떨어지는지
    }

    /// <summary>
    /// 이동 결과를 판 상태에 반영한다. 떠난 무너지는 칸은 구멍, 스위치에 서면 문이 열린다,
    /// 블록 이동·메우기, 레버 바꾸기, 채널 다시 계산, 꺼진 다리 위 블록 떨어짐.
    /// </summary>
    public static Changes Apply(MapData map, PuzzleState state, Vector2Int from, MoveResult r)
    {
        var ch = new Changes();
        int before = state.channels;
        if (r.pulled is { } lever)
        {
            if (!state.leversOn.Remove(lever)) state.leversOn.Add(lever);
            ch.leverPulled = lever;
        }
        if (r.Moved)
        {
            if (map.TypeAt(from) == CellType.Crumble && !state.crumbled.Contains(from) && r.cell != from)
            {
                state.crumbled.Add(from);
                ch.crumbled = from;
            }
            if (r.outcome == MoveOutcome.Move && map.TypeAt(r.cell) == CellType.Switch && !state.doorsOpen)
            {
                state.doorsOpen = true;
                ch.doorsOpened = true;
            }
            if (r.pushFrom is { } pf && r.pushTo is { } pt)
            {
                state.blocks.Remove(pf);
                if (Kind(map, state, pt) == CellType.Hole)
                {
                    state.filled[pt] = Floor(map, state, pf);
                    ch.filledHole = true;
                }
                else state.blocks.Add(pt);
            }
        }

        Vector2Int? fox = r.outcome == MoveOutcome.Fall ? null : r.Moved ? r.cell : from;
        UpdateChannels(map, state, fox);
        ch.channelsChanged = state.channels != before;
        if (ch.channelsChanged)
        {
            // 꺼진 다리 위의 블록은 떨어져 그 칸을 메운다
            foreach (var b in new List<Vector2Int>(state.blocks))
                if (map.TypeAt(b) == CellType.Bridge && !state.ChannelOn(map.ChannelAt(b)))
                {
                    state.blocks.Remove(b);
                    state.filled[b] = map.FloorAt(b);
                    (ch.blocksDropped ??= new List<Vector2Int>()).Add(b);
                }
            if (fox is { } f && Kind(map, state, f) == CellType.Hole) ch.foxFalls = true;
        }
        return ch;
    }

    /// <summary>켜진 채널을 다시 계산한다: 켜진 레버 + 누름판 위의 여우(fox)·블록.</summary>
    public static void UpdateChannels(MapData map, PuzzleState state, Vector2Int? fox)
    {
        int bits = 0;
        foreach (var l in state.leversOn) bits |= Bit(map.ChannelAt(l));
        if (fox is { } f && map.TypeAt(f) == CellType.Plate) bits |= Bit(map.ChannelAt(f));
        foreach (var b in state.blocks)
            if (map.TypeAt(b) == CellType.Plate) bits |= Bit(map.ChannelAt(b));
        state.channels = bits;
    }

    static int Bit(int channel) => channel > 0 ? 1 << channel : 0;
}

/// <summary>
/// 턴 방식: 여우가 한 번 행동하면(걷기·점프·밀기·당기기·부딪히기·기다리기) 발판과 적이 한 칸씩 움직인다.
/// 순서: 여우 이동 → (무너짐·스위치·블록·레버·채널은 MoveRules.Apply 로 먼저) → 턴 +1 → 발판 이동(타고 있으면 실려 감) → 적과 부딪혔는지.
/// 부딪힘: 여우가 지나간 칸에 적이 있었거나(턴 전), 여우가 멈춘 칸으로 적이 들어오면(턴 후). 점프로 넘은 칸은 지나간 칸이 아니다.
/// </summary>
public static class TurnRules
{
    public struct Outcome
    {
        /// <summary>턴이 끝난 뒤 여우 칸 (발판에 실려 갔으면 옮겨진 칸).</summary>
        public Vector2Int cell;
        /// <summary>발판에 실려 갔는지.</summary>
        public bool carried;
        /// <summary>적과 부딪혔는지 (실패).</summary>
        public bool hit;
    }

    /// <summary>
    /// 떨어지지 않은 행동 r 뒤의 턴을 진행한다. state.turn 을 1 올린다.
    /// r 이 Blocked 이거나 기다리기(path 비어 있음)면 여우는 from 에 그대로 있다.
    /// </summary>
    public static Outcome Advance(MapData map, PuzzleState state, Vector2Int from, MoveResult r)
    {
        var o = new Outcome { cell = r != null && r.Moved ? r.cell : from };
        var enemiesBefore = map.EnemiesAt(state.turn);
        if (r != null)
            foreach (var c in r.path)
                if (enemiesBefore.Contains(c)) o.hit = true;      // 적이 있는 칸으로 들어감

        bool onPlatform = map.TypeAt(o.cell) == CellType.Hole && !state.filled.ContainsKey(o.cell) && map.PlatformAt(o.cell, state.turn);
        Mover ride = null;
        if (onPlatform)
            foreach (var m in map.movers)
                if (m.IsPlatform && m.PositionAt(state.turn) == o.cell) { ride = m; break; }

        state.turn++;
        if (ride != null)
        {
            var next = ride.PositionAt(state.turn);
            o.carried = next != o.cell;
            o.cell = next;
        }
        if (map.EnemiesAt(state.turn).Contains(o.cell)) o.hit = true;   // 적이 여우 칸으로 들어옴
        return o;
    }

    /// <summary>기다리기 (제자리에서 한 턴).</summary>
    public static MoveResult Wait(Vector2Int at, Vector2Int facing) =>
        new() { outcome = MoveOutcome.Blocked, cell = at, facing = facing };
}
