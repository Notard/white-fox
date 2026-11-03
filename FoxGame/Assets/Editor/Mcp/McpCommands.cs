using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MCP 브리지가 실행하는 명령. 여는 것은 이 목록뿐이다 (파일 쓰기·씬 저장·임의 코드 실행은 열지 않는다).
///   ping · play_mode start|stop|status · load_stage n · restart_stage · get_game_state · act w|a|s|d|jump|wait
///   run_actions a b c ... · read_console [개수] [all] · screenshot
///   list_stages · read_stage n · validate_map {json} · create_stage {json} · open_in_maptool n
/// act·run_actions 는 키보드와 같은 입구(FoxController.TryMove/TryJump/TryWait)만 쓴다. 여우를 직접 옮기지 않는다.
/// restart_stage 는 R 키와 같은 GameManager.Restart 를 부른다.
/// 맵 명령은 새 스테이지를 마지막 번호 뒤에 만들기만 한다. 기존 스테이지 파일은 고치거나 지우지 않는다 (원본은 사람이 맵툴에서).
/// MCP 로 연 스테이지는 테스트 저장 키를 써서 실제 진행·기록을 건드리지 않는다.
/// </summary>
static class McpCommands
{
    const string SaveKey = "FoxGame.Save.Mcp";

    public static string Run(string line, out McpBridge.Poll poll, out double seconds, out string timeoutReply)
    {
        poll = null;
        seconds = 0;
        timeoutReply = null;
        line = line.Trim();
        if (line.Length == 0) return "빈 명령";

        // JSON 을 받는 명령은 공백으로 쪼개지 않고 첫 낱말 뒤 전부를 넘긴다
        int space = line.IndexOf(' ');
        string word = space < 0 ? line : line.Substring(0, space);
        string rest = space < 0 ? "" : line.Substring(space + 1).Trim();
        if (word == "validate_map") return ValidateMap(rest);
        if (word == "create_stage") return CreateStage(rest);

        var p = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        switch (p[0])
        {
            case "list_stages":
                return ListStages();

            case "read_stage":
                if (p.Length < 2 || !int.TryParse(p[1], out var readIndex)) return "사용법: read_stage <번호 1부터>";
                return ReadStage(readIndex);

            case "open_in_maptool":
                if (p.Length < 2 || !int.TryParse(p[1], out var showIndex)) return "사용법: open_in_maptool <번호 1부터>";
                return OpenInMapTool(showIndex);

            case "ping":
                return "pong " + (EditorApplication.isPlaying ? "playing" : "editing");

            case "play_mode":
                return PlayMode(p.Length > 1 ? p[1] : "status");

            case "read_console":
                int count = p.Length > 1 && int.TryParse(p[1], out var n) ? Mathf.Clamp(n, 1, 200) : 20;
                return McpBridge.RecentLogs(count, p.Length > 2 && p[2] == "all");

            case "get_game_state":
                return GameState();

            case "load_stage":
                if (p.Length < 2 || !int.TryParse(p[1], out var stage)) return "사용법: load_stage <번호 1부터>";
                return LoadStage(stage, out poll, out seconds, out timeoutReply);

            case "restart_stage":
                return RestartStage(out poll, out seconds, out timeoutReply);

            case "act":
                if (p.Length < 2) return "사용법: act w|a|s|d|jump|wait";
                return Act(p[1], out poll, out seconds, out timeoutReply);

            case "run_actions":
                if (p.Length < 2) return "사용법: run_actions w d jump ... (최대 100개)";
                return RunActions(p.Skip(1).ToArray(), out poll, out seconds, out timeoutReply);

            case "screenshot":
                return Screenshot();

            default:
                return $"모르는 명령: {p[0]}";
        }
    }

    // ------------------------------------------------------------ Play 모드
    static string PlayMode(string mode)
    {
        switch (mode)
        {
            case "start":
                if (EditorApplication.isPlaying) return "이미 Play 중";
                EditorApplication.isPlaying = true;
                return "Play 모드로 들어가는 중";
            case "stop":
                if (!EditorApplication.isPlaying) return "이미 Play 아님";
                EditorApplication.isPlaying = false;
                return "Play 모드를 끄는 중";
            default:
                return EditorApplication.isPlaying ? "playing" : "editing";
        }
    }

    // ------------------------------------------------------------ 스테이지
    static string LoadStage(int stage, out McpBridge.Poll poll, out double seconds, out string timeoutReply)
    {
        poll = null;
        seconds = 10;
        timeoutReply = "스테이지를 여는 데 시간이 너무 걸림";
        if (!EditorApplication.isPlaying) return "Play 중이 아님: 먼저 play_mode start";
        if (stage < 1 || stage > StageLibrary.Count) return $"스테이지는 1~{StageLibrary.Count}";

        SaveData.UseKey(SaveKey);   // 실제 진행·기록을 건드리지 않는다
        SceneFlow.PlayStage(stage - 1);
        int frames = 0;
        poll = () =>
        {
            if (++frames < 3) return null;   // 씬 전환이 시작될 때까지
            var gm = FindGame();
            if (gm == null || gm.StageIndex != stage - 1 || gm.State == global::GameState.Ready) return null;
            return GameState();
        };
        return null;
    }

    /// <summary>R 키와 같다: 지금 스테이지를 처음부터. 씬이 다시 열리고 움직일 수 있게 되면 상태를 돌려준다.</summary>
    static string RestartStage(out McpBridge.Poll poll, out double seconds, out string timeoutReply)
    {
        poll = null;
        seconds = 10;
        timeoutReply = "스테이지를 다시 여는 데 시간이 너무 걸림";
        if (!EditorApplication.isPlaying) return "Play 중이 아님: 먼저 play_mode start, load_stage";
        var old = FindGame();
        if (old == null) return "게임 화면이 아님: load_stage 로 스테이지를 여세요";

        int index = old.StageIndex;
        SaveData.UseKey(SaveKey);
        old.Restart();
        int frames = 0;
        poll = () =>
        {
            if (++frames < 3) return null;
            var gm = FindGame();
            if (gm == null || gm == old || gm.StageIndex != index || gm.State == global::GameState.Ready) return null;
            return "스테이지를 다시 시작함\n\n" + GameState();
        };
        return null;
    }

    // ------------------------------------------------------------ 행동
    static readonly string[] ActionNames = { "w", "a", "s", "d", "jump", "wait" };

    static string Act(string action, out McpBridge.Poll poll, out double seconds, out string timeoutReply)
    {
        poll = null;
        seconds = 8;
        timeoutReply = "행동이 끝나지 않음 (애니메이션이 멈췄거나 일시정지)";
        if (!EditorApplication.isPlaying) return "Play 중이 아님: 먼저 play_mode start, load_stage";
        if (FindGame() == null) return "게임 화면이 아님: load_stage 로 스테이지를 여세요";
        if (Array.IndexOf(ActionNames, action) < 0) return "행동은 w, a, s, d, jump, wait 중 하나";

        var one = ActPoll(action);
        poll = () =>
        {
            var r = one();
            return r == null ? null : $"행동 {action}: {r}\n\n{GameState()}";
        };
        return null;
    }

    /// <summary>
    /// 행동 여러 개를 순서대로. 한 행동이 끝나야 다음을 시작하고, 클리어·실패·막힘이 아닌 멈춤(일시정지 등)이 나오면 거기서 멈춘다.
    /// 답은 행동마다 한 줄 결과, 마지막에 상태.
    /// </summary>
    static string RunActions(string[] actions, out McpBridge.Poll poll, out double seconds, out string timeoutReply)
    {
        poll = null;
        seconds = 8 * actions.Length + 2;
        timeoutReply = "행동이 끝나지 않음 (애니메이션이 멈췄거나 일시정지)";
        if (!EditorApplication.isPlaying) return "Play 중이 아님: 먼저 play_mode start, load_stage";
        if (FindGame() == null) return "게임 화면이 아님: load_stage 로 스테이지를 여세요";
        if (actions.Length > 100) return "행동은 한 번에 100개까지";
        foreach (var a in actions)
            if (Array.IndexOf(ActionNames, a) < 0) return $"모르는 행동 \"{a}\": w, a, s, d, jump, wait 중 하나";

        var log = new StringBuilder();
        int i = 0;
        McpBridge.Poll current = null;
        string Finish(string note)
        {
            if (note != null) log.AppendLine(note);
            return $"{i}/{actions.Length} 행동 실행\n{log}\n{GameState()}";
        }
        poll = () =>
        {
            if (current == null)
            {
                if (i >= actions.Length) return Finish(null);
                current = ActPoll(actions[i]);
            }
            var r = current();
            if (r == null) return null;
            log.AppendLine($"{i + 1}. {actions[i]}: {r}");
            current = null;
            i++;
            var g = FindGame();
            if (g == null) return Finish("게임 화면이 사라져 멈춤");
            if (g.State != global::GameState.Playing) return Finish(i < actions.Length ? $"상태 {g.State} 라서 남은 {actions.Length - i}개는 실행하지 않음" : null);
            return null;   // 다음 행동은 다음 프레임에
        };
        return null;
    }

    /// <summary>행동 하나를 시작하고 끝날 때까지 기다리는 폴. 결과는 한 줄 요약(상태 없음).</summary>
    static McpBridge.Poll ActPoll(string action)
    {
        Vector2Int? dir = action switch
        {
            "w" => Vector2Int.up, "s" => Vector2Int.down, "a" => Vector2Int.left, "d" => Vector2Int.right,
            _ => null,
        };
        bool started = false;
        Vector2Int fromCell = default;
        int fromTurn = 0, fromGems = 0;
        return () =>
        {
            var g = FindGame();
            if (g == null) return "게임 화면이 사라짐";
            var fox = g.fox;
            if (!started)
            {
                if (g.State == global::GameState.Ready || fox.IsBusy) return null;   // 시작 연출이 끝날 때까지
                if (g.State != global::GameState.Playing) return $"지금은 움직일 수 없음 (상태 {g.State})";
                if (g.IsPaused) return "일시정지 중이라 실행하지 않음";
                fromCell = fox.Cell;
                fromTurn = g.board.State.turn;
                fromGems = g.Collected;
                if (dir != null) fox.TryMove(dir.Value);
                else if (action == "jump") fox.TryJump();
                else fox.TryWait();
                started = true;
                return null;
            }
            if (g.State == global::GameState.Playing && fox.IsBusy) return null;
            if (g.State == global::GameState.Playing && fox.HasFallen) return null;   // 떨어지는 중

            var sb = new StringBuilder();
            if (g.State == global::GameState.Cleared) sb.Append("클리어!");
            else if (g.State == global::GameState.GameOver) sb.Append(fox.WasHit ? "적과 부딪혀 실패" : "떨어져서 실패");
            else if (fox.Cell == fromCell && g.board.State.turn == fromTurn) sb.Append("막힘 (턴이 지나지 않음, 바라보는 방향만 바뀜)");
            else if (fox.Cell == fromCell) sb.Append("제자리 (한 턴 지남)");
            else sb.Append($"{Fmt(fromCell)} → {Fmt(fox.Cell)}");
            if (g.Collected > fromGems) sb.Append($", 보석 +{g.Collected - fromGems}");
            return sb.ToString();
        };
    }

    // ------------------------------------------------------------ 상태
    static GameManager FindGame() =>
        SceneManager.GetActiveScene().name == SceneFlow.GameScene ? UnityEngine.Object.FindAnyObjectByType<GameManager>() : null;

    static string Fmt(Vector2Int c) => $"({c.x},{c.y})";

    static string DirName(Vector2Int d) =>
        d == Vector2Int.up ? "w(위)" : d == Vector2Int.down ? "s(아래)" : d == Vector2Int.left ? "a(왼쪽)" : d == Vector2Int.right ? "d(오른쪽)" : d.ToString();

    static string GameState()
    {
        if (!EditorApplication.isPlaying) return "Play 중이 아님 (play_mode start)";
        var gm = FindGame();
        if (gm == null) return $"게임 화면이 아님 (지금 씬: {SceneManager.GetActiveScene().name}). load_stage 로 스테이지를 여세요";

        var map = gm.Map;
        var st = gm.board.State;
        var fox = gm.fox;
        int turn = st.turn;
        var gems = new HashSet<Vector2Int>(UnityEngine.Object.FindObjectsByType<Gem>().Where(x => !x.Collected).Select(x => x.Cell));
        var enemies = map.EnemiesAt(turn);

        var sb = new StringBuilder();
        sb.AppendLine($"스테이지 {gm.StageIndex + 1}/{gm.StageCount} \"{map.name}\" · 상태 {gm.State}{(gm.IsPaused ? " (일시정지)" : "")} · 턴 {turn} · 보석 {gm.Collected}/{gm.Total} · {gm.Elapsed:0.0}초");
        sb.AppendLine($"여우 {Fmt(fox.Cell)} 층 {MoveRules.Floor(map, st, fox.Cell)}, 바라보는 방향 {DirName(fox.Facing)}");
        sb.AppendLine();
        sb.AppendLine("칸 = 바닥 기호 + 층 + 위에 있는 것. 맨 윗줄이 y가 가장 큰 줄(먼 쪽), 맨 아랫줄이 y=0(카메라 쪽).");
        for (int y = map.Height - 1; y >= 0; y--)
        {
            sb.Append($"y{y,-2}|");
            for (int x = 0; x < map.Width; x++)
            {
                var c = new Vector2Int(x, y);
                char on = c == fox.Cell ? '@' : gems.Contains(c) ? 'G' : st.blocks.Contains(c) ? 'K'
                    : enemies.Contains(c) ? 'X' : map.IsInside(c) && map.PlatformAt(c, turn) ? '=' : ' ';
                sb.Append(' ').Append(Terrain(map, st, c)).Append(MoveRules.Floor(map, st, c)).Append(on);
            }
            sb.AppendLine();
        }
        sb.Append("    ");
        for (int x = 0; x < map.Width; x++) sb.Append($" x{x,-2}");
        sb.AppendLine();
        sb.AppendLine();
        sb.AppendLine("바닥: . 땅, O 구멍(떨어짐), # 막힘, I 얼음(미끄러짐), C 무너지는 칸, W 스위치, D 닫힌 문, d 열린 문, T 계단, L 레버, P 누름판, B 꺼진 다리(구멍), b 켜진 다리, E 승강 땅");
        sb.AppendLine("위: @ 여우, G 보석, K 미는 블록, X 적, = 발판");

        if (map.movers != null && map.movers.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("움직이는 것 (지금 → 다음 턴):");
            foreach (var m in map.movers)
                if (m.path.Count > 0) sb.AppendLine($"- {m.kind}: {Fmt(m.PositionAt(turn))} → {Fmt(m.PositionAt(turn + 1))}");
        }
        sb.AppendLine();
        sb.Append("행동: w(+y) s(-y) a(-x) d(+x) 한 칸 이동, jump = 바라보는 방향으로 두 칸 앞 또는 바로 앞 1~2층 높은 칸 위로, wait = 한 턴 쉼.");
        return sb.ToString();
    }

    /// <summary>판 상태까지 반영한 바닥 기호.</summary>
    static char Terrain(MapData map, PuzzleState st, Vector2Int c)
    {
        char s = map.SymbolAt(c);
        if (st.filled.ContainsKey(c)) return '.';
        if (s == MapData.Crumble && st.crumbled.Contains(c)) return 'O';
        if (s == MapData.Door && st.doorsOpen) return 'd';
        if (s == MapData.Bridge && st.ChannelOn(map.ChannelAt(c))) return 'b';
        return s;
    }

    // ------------------------------------------------------------ 맵툴 (새 스테이지 만들기만)
    static readonly StageFiles Files = new();

    static string ListStages()
    {
        var paths = Files.List();
        if (paths.Count == 0) return $"스테이지가 없음 ({StageLibrary.AssetFolder})";
        var sb = new StringBuilder();
        sb.AppendLine($"스테이지 {paths.Count}개 ({StageLibrary.AssetFolder}). 새 스테이지는 {paths.Count + 1}번으로 만들어짐.");
        for (int i = 0; i < paths.Count; i++)
        {
            try
            {
                var m = Files.Load(paths[i]);
                var v = MapValidator.Validate(m);
                string movers = m.movers.Count > 0 ? $", 움직이는 것 {m.movers.Count}" : "";
                string check = v.IsValid ? (v.warnings.Count > 0 ? $"경고 {v.warnings.Count}" : "통과") : $"오류 {v.errors.Count}";
                sb.AppendLine($"{i + 1,2}. \"{m.name}\" {m.Width}×{m.Height}, 보석 {m.Gems.Count}, 최고 {m.HighestFloor}층{movers} · 검증 {check}");
            }
            catch (MapFormatException e) { sb.AppendLine($"{i + 1,2}. 읽기 실패: {e.Message}"); }
        }
        return sb.ToString().TrimEnd();
    }

    static string ReadStage(int stage)
    {
        var paths = Files.List();
        if (stage < 1 || stage > paths.Count) return $"스테이지는 1~{paths.Count}";
        var json = File.ReadAllText(paths[stage - 1]);
        try
        {
            var m = MapJson.FromJson(json, Path.GetFileName(paths[stage - 1]));
            return $"{paths[stage - 1]}\n\n{MapPreview(m)}\n\n{json}";
        }
        catch (MapFormatException e) { return $"{paths[stage - 1]}\n읽기 실패: {e.Message}\n\n{json}"; }
    }

    /// <summary>JSON 을 읽고 검증한다. 저장하지 않는다.</summary>
    static string ValidateMap(string json)
    {
        if (json.Length == 0) return "사용법: validate_map {스테이지 JSON}";
        var (map, error) = Parse(json);
        if (map == null) return error;
        var v = MapValidator.Validate(map);
        return Report(map, v);
    }

    /// <summary>검증을 통과한 맵만 마지막 번호 뒤에 새 파일로 만든다. 기존 파일은 건드리지 않는다.</summary>
    static string CreateStage(string json)
    {
        if (json.Length == 0) return "사용법: create_stage {스테이지 JSON}";
        var (map, error) = Parse(json);
        if (map == null) return error;
        if (string.IsNullOrWhiteSpace(map.name) || map.name == "새 스테이지") return "name 을 정해 주세요 (스테이지 제목으로 화면에 보임)";
        foreach (var p in Files.List())
        {
            try { if (Files.Load(p).name == map.name) return $"같은 이름 \"{map.name}\" 의 스테이지가 이미 있음 ({p}). 다른 이름을 쓰세요"; }
            catch (MapFormatException) { }
        }
        var v = MapValidator.Validate(map);
        if (!v.IsValid) return "검증을 통과하지 못해 만들지 않음\n\n" + Report(map, v);

        map.id = null;   // 새 기록
        var path = Files.Create(map);
        int number = Files.List().IndexOf(path) + 1;
        return $"스테이지 {number} 로 만듦: {path}\n(플레이: load_stage {number} · 맵툴에서 보기: open_in_maptool {number})\n\n{Report(map, v)}";
    }

    static string OpenInMapTool(int stage)
    {
        var paths = Files.List();
        if (stage < 1 || stage > paths.Count) return $"스테이지는 1~{paths.Count}";
        var window = MapEditorWindow.Open();
        return window.ShowStage(paths[stage - 1])
            ? $"맵툴에서 스테이지 {stage} 를 엶"
            : $"맵툴을 열었지만 스테이지 {stage} 로 바꾸지 못함 (저장 안 한 변경을 취소했거나 파일을 못 읽음)";
    }

    static (MapData map, string error) Parse(string json)
    {
        try { return (MapJson.FromJson(json, "보낸 JSON"), null); }
        catch (MapFormatException e) { return (null, e.Message); }
    }

    static string Report(MapData map, MapValidation v)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"\"{map.name}\" {map.Width}×{map.Height}, 보석 {map.Gems.Count}, 최고 {map.HighestFloor}층, 움직이는 것 {map.movers.Count}");
        sb.AppendLine(v.IsValid ? "검증 통과" : "검증 실패");
        foreach (var e in v.errors) sb.AppendLine("- 오류: " + e);
        foreach (var w in v.warnings) sb.AppendLine("- 경고: " + w);
        if (v.unreachableGems.Count > 0) sb.AppendLine("- 먹을 수 없는 보석: " + string.Join(" ", v.unreachableGems.Select(Fmt)));
        sb.AppendLine();
        sb.Append(MapPreview(map));
        return sb.ToString();
    }

    /// <summary>게임 상태와 같은 모양의 격자 (바닥 기호 + 층 + 물건). 장치는 채널 번호를 덧붙인다.</summary>
    static string MapPreview(MapData map)
    {
        var sb = new StringBuilder();
        sb.AppendLine("맨 윗줄이 y가 가장 큰 줄. JSON 의 rows/items/floors 는 첫 줄이 y가 가장 큰 줄이다 (화면과 같은 방향).");
        for (int y = map.Height - 1; y >= 0; y--)
        {
            sb.Append($"y{y,-2}|");
            for (int x = 0; x < map.Width; x++)
            {
                var c = new Vector2Int(x, y);
                char item = map.ItemAt(c) == MapData.NoItem ? ' ' : map.ItemAt(c);
                int ch = map.ChannelAt(c);
                sb.Append(' ').Append(map.SymbolAt(c)).Append(map.FloorAt(c)).Append(item).Append(ch > 0 ? (char)('0' + ch) : ' ');
            }
            sb.AppendLine();
        }
        sb.Append("    ");
        for (int x = 0; x < map.Width; x++) sb.Append($" x{x,-3}");
        sb.AppendLine();
        sb.AppendLine("바닥: . 땅, O 구멍, # 막힘, I 얼음, C 무너짐, W 스위치, D 문, T 계단, L 레버, P 누름판, B 다리, E 승강 땅 · 물건: S 시작, G 보석, K 블록 · 네 번째 글자는 장치 채널");
        if (map.movers.Count > 0)
            foreach (var m in map.movers) sb.AppendLine($"- {m.kind}: {string.Join(" → ", m.path.Select(Fmt))}");
        return sb.ToString().TrimEnd();
    }

    // ------------------------------------------------------------ 화면
    static string Screenshot()
    {
        var cam = Camera.main;
        if (cam == null) return "메인 카메라가 없음";
        const int w = 1280, h = 720;
        var rt = new RenderTexture(w, h, 24) { antiAliasing = 4 };
        var canvases = UnityEngine.Object.FindObjectsByType<Canvas>();
        var modes = new RenderMode[canvases.Length];
        var distances = new float[canvases.Length];
        var cams = new Camera[canvases.Length];
        try
        {
            cam.targetTexture = rt;
            // 오버레이 UI 도 찍히게 잠시 카메라 앞으로 옮긴다
            for (int i = 0; i < canvases.Length; i++)
            {
                modes[i] = canvases[i].renderMode;
                distances[i] = canvases[i].planeDistance;
                cams[i] = canvases[i].worldCamera;
                if (modes[i] != RenderMode.ScreenSpaceOverlay) continue;
                canvases[i].renderMode = RenderMode.ScreenSpaceCamera;
                canvases[i].worldCamera = cam;
                canvases[i].planeDistance = 1 + 0.01f * (10 - canvases[i].sortingOrder);
            }
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            string dir = Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Screenshots", "mcp");
            Directory.CreateDirectory(dir);
            string file = Path.Combine(dir, $"mcp_{DateTime.Now:yyyyMMdd_HHmmss_fff}.png");
            File.WriteAllBytes(file, tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            return file;
        }
        finally
        {
            cam.targetTexture = null;
            RenderTexture.active = null;
            for (int i = 0; i < canvases.Length; i++)
            {
                canvases[i].renderMode = modes[i];
                canvases[i].planeDistance = distances[i];
                canvases[i].worldCamera = cams[i];
            }
            UnityEngine.Object.DestroyImmediate(rt);
        }
    }
}
