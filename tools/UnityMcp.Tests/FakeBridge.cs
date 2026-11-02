using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace UnityMcp.Tests;

/// <summary>Unity 없이 MCP 서버를 검사하는 가짜 브리지: 명령 한 줄을 받아 정해진 답을 쓰고 닫는다.</summary>
sealed class FakeBridge : IDisposable
{
    readonly TcpListener listener = new(IPAddress.Loopback, 0);
    readonly Func<string, string> answer;
    readonly CancellationTokenSource stop = new();
    public readonly ConcurrentQueue<string> Received = new();
    public int Port => ((IPEndPoint)listener.LocalEndpoint).Port;

    public FakeBridge(Func<string, string> answer)
    {
        this.answer = answer;
        listener.Start();
        _ = Task.Run(Loop);
    }

    async Task Loop()
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient c;
            try { c = await listener.AcceptTcpClientAsync(stop.Token); }
            catch { return; }
            using (c)
            {
                var s = c.GetStream();
                var line = new StreamReader(s, new UTF8Encoding(false)).ReadLine() ?? "";
                Received.Enqueue(line);
                var bytes = new UTF8Encoding(false).GetBytes(answer(line));
                s.Write(bytes);
            }
        }
    }

    public void Dispose()
    {
        stop.Cancel();
        listener.Stop();
    }
}
