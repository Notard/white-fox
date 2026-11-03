using System.ComponentModel;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace UnityMcp;

/// <summary>
/// White Fox 를 AI 가 보고 조작하는 도구. 여기 있는 것만 연다 (Docs/mcp_tools.md).
/// 일부러 열지 않는 것: 스테이지 파일 저장, 진행 초기화, 빌드, 씬 저장, 임의 코드 실행.
/// </summary>
[McpServerToolType]
public static class FoxTools
{
    [McpServerTool(Name = "play_mode"), Description(
        "Unity 에디터의 Play 모드를 켜거나 끈다. mode: start, stop, status. " +
        "start 는 Play 에 들어가 브리지가 다시 응답할 때까지 기다린다 (최대 30초). 켠 뒤에는 load_stage 로 스테이지를 연다.")]
    public static string PlayMode([Description("start, stop, status")] string mode = "status")
    {
        if (mode is not ("start" or "stop" or "status")) return "mode 는 start, stop, status 중 하나";
        var first = Bridge.Send($"play_mode {mode}");
        if (mode == "status" || first.StartsWith(Bridge.Unreachable) || first.StartsWith("이미")) return first;

        string want = mode == "start" ? "playing" : "editing";
        var now = Bridge.WaitFor("play_mode status", r => r == want, TimeSpan.FromSeconds(30));
        return now == want ? $"{first} → 완료 ({want})" : $"{first} → 30초 안에 {want} 가 되지 않음 (마지막 답: {now})";
    }

    [McpServerTool(Name = "load_stage"), Description(
        "Play 중에 스테이지를 연다 (1부터). 시작 연출이 끝나 움직일 수 있게 되면 게임 상태를 돌려준다. " +
        "실제 저장 진행을 건드리지 않도록 테스트용 저장 키를 쓴다.")]
    public static string LoadStage([Description("스테이지 번호 (1부터)")] int stage) =>
        Bridge.Send($"load_stage {stage}");

    [McpServerTool(Name = "get_game_state", ReadOnly = true), Description(
        "지금 게임 상태를 글자로 돌려준다: 스테이지, 상태(Ready/Playing/Cleared/GameOver), 턴, 보석 수, 여우 위치와 방향, " +
        "칸마다 바닥 기호·층·위에 있는 것(@ 여우, G 보석, X 적 등)의 격자, 적·발판의 다음 턴 위치, 쓸 수 있는 행동.")]
    public static string GetGameState() => Bridge.Send("get_game_state");

    [McpServerTool(Name = "act"), Description(
        "여우에게 행동 하나를 시킨다. 플레이어가 키를 누른 것과 같다. " +
        "w(+y), s(-y), a(-x), d(+x) 한 칸 이동, jump 바라보는 방향으로 점프, wait 한 턴 쉬기. " +
        "애니메이션과 턴 진행이 끝날 때까지 기다린 뒤 결과(이동/막힘/떨어짐/클리어)와 새 게임 상태를 돌려준다.")]
    public static string Act([Description("w, a, s, d, jump, wait 중 하나")] string action) =>
        action is "w" or "a" or "s" or "d" or "jump" or "wait"
            ? Bridge.Send($"act {action}")
            : "action 은 w, a, s, d, jump, wait 중 하나";

    [McpServerTool(Name = "run_actions"), Description(
        "행동 여러 개를 순서대로 실행한다 (최대 100개). 공백이나 쉼표로 나눈 w a s d jump wait 의 열, 예: \"d d w jump a\". " +
        "한 행동이 끝나야 다음을 시작하므로 순서가 섞이지 않는다. 클리어·실패가 나오면 거기서 멈춘다. " +
        "행동마다 한 줄 결과와 마지막 게임 상태를 돌려준다.")]
    public static string RunActions([Description("공백 또는 쉼표로 나눈 행동 열, 예: d d w jump")] string actions)
    {
        var list = actions.Split([' ', ',', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (list.Length == 0) return "행동이 없음: 예 \"d d w jump\"";
        if (list.Length > 100) return "행동은 한 번에 100개까지";
        var bad = list.FirstOrDefault(a => a is not ("w" or "a" or "s" or "d" or "jump" or "wait"));
        if (bad != null) return $"모르는 행동 \"{bad}\": w, a, s, d, jump, wait 중 하나";
        return Bridge.Send("run_actions " + string.Join(' ', list), TimeSpan.FromSeconds(8 * list.Length + 12));
    }

    [McpServerTool(Name = "restart_stage"), Description(
        "지금 스테이지를 처음부터 다시 시작한다 (R 키와 같음). 시작 연출이 끝나 움직일 수 있게 되면 게임 상태를 돌려준다.")]
    public static string RestartStage() => Bridge.Send("restart_stage");

    [McpServerTool(Name = "read_console", ReadOnly = true), Description(
        "Unity 콘솔의 최근 로그를 읽는다. 기본은 에러와 경고만. includeInfo 가 true 면 일반 로그도.")]
    public static string ReadConsole(
        [Description("읽을 줄 수 (1~200)")] int count = 20,
        [Description("일반 Debug.Log 도 포함할지")] bool includeInfo = false) =>
        Bridge.Send($"read_console {Math.Clamp(count, 1, 200)}{(includeInfo ? " all" : "")}");

    [McpServerTool(Name = "screenshot", ReadOnly = true), Description(
        "게임 화면(메인 카메라 + UI)을 1280x720 PNG 로 찍어 이미지로 돌려준다. 파일은 FoxGame/Screenshots/mcp 에 남는다.")]
    public static IEnumerable<ContentBlock> Screenshot()
    {
        var reply = Bridge.Send("screenshot");
        if (!File.Exists(reply)) return [new TextContentBlock { Text = reply }];
        return
        [
            new TextContentBlock { Text = reply },
            ImageContentBlock.FromBytes(File.ReadAllBytes(reply), "image/png"),
        ];
    }
}
