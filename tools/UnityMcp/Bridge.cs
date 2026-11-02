using System.Net.Sockets;
using System.Text;

namespace UnityMcp;

/// <summary>
/// Unity 에디터 브리지(FoxGame/Assets/Editor/Mcp/McpBridge.cs)와 주고받기. 명령 한 줄을 보내고, 브리지가 연결을 닫을 때까지 답을 읽는다.
/// 명령마다 새로 연결한다: Play 모드에 들어가면 에디터 도메인이 다시 불려 브리지가 재시작되기 때문.
/// </summary>
public static class Bridge
{
    /// <summary>브리지 포트. UNITY_MCP_PORT 로 바꿀 수 있다 (테스트는 가짜 브리지 포트로 바꾼다).</summary>
    public static int Port { get; set; } =
        int.TryParse(Environment.GetEnvironmentVariable("UNITY_MCP_PORT"), out var p) ? p : 7801;

    /// <summary>답을 기다리는 최대 시간. 브리지 쪽 act·load_stage 시간 제한(8·10초)보다 길게.</summary>
    public static TimeSpan ReplyTimeout { get; set; } = TimeSpan.FromSeconds(20);

    public const string Unreachable = "Unity 브리지에 연결할 수 없음";

    /// <summary>명령을 보내고 답을 돌려준다. 연결이 안 되면 예외 대신 이유를 문장으로 돌려준다.</summary>
    public static string Send(string command)
    {
        try { return SendOrThrow(command); }
        catch (SocketException e) { return $"{Unreachable} ({e.SocketErrorCode}): Unity 에디터에 FoxGame 이 열려 있는지, 스크립트 컴파일 중이 아닌지 확인"; }
        catch (IOException e) { return $"{Unreachable} (답을 받는 중 끊김: {e.Message}). 컴파일이나 Play 전환 중일 수 있음"; }
    }

    static string SendOrThrow(string command)
    {
        using var client = new TcpClient();
        if (!client.ConnectAsync("127.0.0.1", Port).Wait(TimeSpan.FromSeconds(2)))
            throw new SocketException((int)SocketError.TimedOut);
        client.ReceiveTimeout = (int)ReplyTimeout.TotalMilliseconds;
        using var stream = client.GetStream();
        var line = Encoding.UTF8.GetBytes(command.Replace('\n', ' ') + "\n");
        stream.Write(line);
        stream.Flush();
        using var reader = new StreamReader(stream, new UTF8Encoding(false));
        return reader.ReadToEnd();
    }

    /// <summary>
    /// 조건이 맞는 답이 올 때까지 같은 명령을 다시 보낸다 (Play 전환처럼 브리지가 잠깐 사라지는 동안).
    /// </summary>
    public static string WaitFor(string command, Func<string, bool> done, TimeSpan timeout, TimeSpan? interval = null)
    {
        var until = DateTime.UtcNow + timeout;
        string last = "";
        while (DateTime.UtcNow < until)
        {
            last = Send(command);
            if (done(last)) return last;
            Thread.Sleep(interval ?? TimeSpan.FromMilliseconds(500));
        }
        return last;
    }
}
