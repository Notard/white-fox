using System.Net;
using System.Net.Sockets;
using ModelContextProtocol.Protocol;

namespace UnityMcp.Tests;

// Bridge.Port 가 static 이라 한 클래스 안에서 차례로 돈다.
public class FoxToolsTests
{
    static int FreePort()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    [Fact]
    public void 도구는_브리지에_정해진_명령을_보내고_답을_그대로_돌려준다()
    {
        using var fake = new FakeBridge(cmd => "답:" + cmd);
        Bridge.Port = fake.Port;

        Assert.Equal("답:get_game_state", FoxTools.GetGameState());
        Assert.Equal("답:act w", FoxTools.Act("w"));
        Assert.Equal("답:act jump", FoxTools.Act("jump"));
        Assert.Equal("답:load_stage 3", FoxTools.LoadStage(3));
        Assert.Equal("답:read_console 20", FoxTools.ReadConsole());
        Assert.Equal("답:read_console 200 all", FoxTools.ReadConsole(999, includeInfo: true));
        Assert.Equal(["get_game_state", "act w", "act jump", "load_stage 3", "read_console 20", "read_console 200 all"], fake.Received.ToArray());
    }

    [Fact]
    public void run_actions는_행동_열을_정리해서_한_명령으로_보낸다()
    {
        using var fake = new FakeBridge(cmd => "답:" + cmd);
        Bridge.Port = fake.Port;

        Assert.Equal("답:run_actions d d w jump", FoxTools.RunActions("d, d,w  jump"));
        Assert.Equal("답:restart_stage", FoxTools.RestartStage());
        Assert.Equal(["run_actions d d w jump", "restart_stage"], fake.Received.ToArray());
    }

    [Fact]
    public void run_actions는_잘못된_행동이_하나라도_있으면_보내지_않는다()
    {
        using var fake = new FakeBridge(_ => "보내면 안 됨");
        Bridge.Port = fake.Port;

        Assert.Contains("\"fly\"", FoxTools.RunActions("w fly d"));
        Assert.Contains("행동이 없음", FoxTools.RunActions("  ,  "));
        Assert.Contains("100개", FoxTools.RunActions(string.Join(' ', Enumerable.Repeat("w", 101))));
        Assert.Empty(fake.Received);
    }

    [Fact]
    public void 맵_도구는_JSON을_한_줄로_만들어_보낸다()
    {
        using var fake = new FakeBridge(cmd => "답:" + cmd);
        Bridge.Port = fake.Port;

        Assert.Equal("답:list_stages", FoxTools.ListStages());
        Assert.Equal("답:read_stage 16", FoxTools.ReadStage(16));
        Assert.Equal("답:open_in_maptool 17", FoxTools.OpenInMapTool(17));
        Assert.Equal("답:validate_map {\"name\": \"a\",    \"rows\": [\"..\"] }", FoxTools.ValidateMap("{\"name\": \"a\",\r\n  \"rows\": [\"..\"] }"));
        Assert.Equal("답:create_stage {\"name\":\"b\"}", FoxTools.CreateStage("\n{\"name\":\"b\"}\n"));
        Assert.Contains("비어 있음", FoxTools.CreateStage("  \n "));
        Assert.Equal(5, fake.Received.Count);
    }

    [Fact]
    public void 한글_답이_깨지지_않는다()
    {
        using var fake = new FakeBridge(_ => "스테이지 1/16 \"첫 번째 숲\" · 보석 0/5");
        Bridge.Port = fake.Port;
        Assert.Equal("스테이지 1/16 \"첫 번째 숲\" · 보석 0/5", FoxTools.GetGameState());
    }

    [Fact]
    public void 잘못된_행동은_브리지에_보내지_않는다()
    {
        using var fake = new FakeBridge(_ => "보내면 안 됨");
        Bridge.Port = fake.Port;
        Assert.Contains("w, a, s, d, jump, wait", FoxTools.Act("teleport 3 3"));
        Assert.Contains("start, stop, status", FoxTools.PlayMode("delete"));
        Assert.Empty(fake.Received);
    }

    [Fact]
    public void 브리지가_없으면_죽지_않고_이유를_돌려준다()
    {
        Bridge.Port = FreePort();
        Assert.StartsWith(Bridge.Unreachable, FoxTools.GetGameState());
        Assert.StartsWith(Bridge.Unreachable, FoxTools.Act("d"));
        Assert.StartsWith(Bridge.Unreachable, FoxTools.PlayMode("start"));
        var blocks = FoxTools.Screenshot().ToList();
        Assert.IsType<TextContentBlock>(Assert.Single(blocks));
    }

    [Fact]
    public void play_mode_start는_playing이_될_때까지_기다린다()
    {
        int statusCalls = 0;
        using var fake = new FakeBridge(cmd => cmd switch
        {
            "play_mode start" => "Play 모드로 들어가는 중",
            "play_mode status" => ++statusCalls < 3 ? "editing" : "playing",
            _ => "?",
        });
        Bridge.Port = fake.Port;

        var reply = FoxTools.PlayMode("start");
        Assert.Contains("완료 (playing)", reply);
        Assert.Equal(3, statusCalls);
    }

    [Fact]
    public void screenshot은_파일을_이미지로_돌려준다()
    {
        var file = Path.Combine(Path.GetTempPath(), $"mcp_test_{Guid.NewGuid():N}.png");
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        File.WriteAllBytes(file, png);
        try
        {
            using var fake = new FakeBridge(_ => file);
            Bridge.Port = fake.Port;
            var blocks = FoxTools.Screenshot().ToList();
            Assert.Equal(2, blocks.Count);
            var image = Assert.IsType<ImageContentBlock>(blocks[1]);
            Assert.Equal("image/png", image.MimeType);
            Assert.Equal(png, image.DecodedData.ToArray());
        }
        finally { File.Delete(file); }
    }
}
