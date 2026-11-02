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
///   ping · play_mode start|stop|status · load_stage n · get_game_state · act w|a|s|d|jump|wait
///   read_console [개수] [all] · screenshot
/// act 는 키보드와 같은 입구(FoxController.TryMove/TryJump/TryWait)만 쓴다. 여우를 직접 옮기지 않는다.
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
        var p = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (p.Length == 0) return "빈 명령";

        switch (p[0])
        {
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

            case "act":
                if (p.Length < 2) return "사용법: act w|a|s|d|jump|wait";
                return Act(p[1], out poll, out seconds, out timeoutReply);

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

    // ------------------------------------------------------------ 행동
    static string Act(string action, out McpBridge.Poll poll, out double seconds, out string timeoutReply)
    {
        poll = null;
        seconds = 8;
        timeoutReply = "행동이 끝나지 않음 (애니메이션이 멈췄거나 일시정지)";
        if (!EditorApplication.isPlaying) return "Play 중이 아님: 먼저 play_mode start, load_stage";
        var gm = FindGame();
        if (gm == null) return "게임 화면이 아님: load_stage 로 스테이지를 여세요";

        Vector2Int? dir = action switch
        {
            "w" => Vector2Int.up, "s" => Vector2Int.down, "a" => Vector2Int.left, "d" => Vector2Int.right,
            _ => null,
        };
        if (dir == null && action != "jump" && action != "wait") return "행동은 w, a, s, d, jump, wait 중 하나";

        bool started = false;
        Vector2Int fromCell = default;
        int fromTurn = 0, fromGems = 0;
        poll = () =>
        {
            var g = FindGame();
            if (g == null) return "게임 화면이 사라짐";
            var fox = g.fox;
            if (!started)
            {
                if (g.State == global::GameState.Ready || fox.IsBusy) return null;   // 시작 연출이 끝날 때까지
                if (g.State != global::GameState.Playing) return $"지금은 움직일 수 없음 (상태 {g.State})\n\n" + GameState();
                if (g.IsPaused) return "일시정지 중";
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
            sb.Append($"행동 {action}: ");
            if (g.State == global::GameState.Cleared) sb.Append("클리어!");
            else if (g.State == global::GameState.GameOver) sb.Append(fox.WasHit ? "적과 부딪혀 실패" : "떨어져서 실패");
            else if (fox.Cell == fromCell && g.board.State.turn == fromTurn) sb.Append("막힘 (턴이 지나지 않음, 바라보는 방향만 바뀜)");
            else if (fox.Cell == fromCell) sb.Append("제자리 (한 턴 지남)");
            else sb.Append($"{Fmt(fromCell)} → {Fmt(fox.Cell)}");
            if (g.Collected > fromGems) sb.Append($", 보석 +{g.Collected - fromGems}");
            sb.Append("\n\n").Append(GameState());
            return sb.ToString();
        };
        return null;
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
