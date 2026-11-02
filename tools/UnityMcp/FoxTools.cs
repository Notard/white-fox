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
