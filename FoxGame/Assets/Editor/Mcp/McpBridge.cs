using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEditor;
using UnityEngine;

/// <summary>
/// MCP 서버(tools/UnityMcp)가 보내는 명령을 받는 에디터 브리지. 127.0.0.1:7801 에서만 듣는다.
/// 명령 한 줄을 받고 답을 쓴 뒤 연결을 닫는다 (명령마다 새 연결: Play 진입 때 도메인이 다시 불려 브리지가 재시작되므로).
/// 소켓은 백그라운드 스레드, Unity API 는 EditorApplication.update(메인 스레드)에서만. 명령 내용은 McpCommands.
/// </summary>
[InitializeOnLoad]
static class McpBridge
{
    public const int Port = 7801;
    const int MaxLogs = 200;

    /// <summary>답이 바로 나오지 않는 명령(act 등): null 을 돌려주는 동안 매 프레임 다시 부른다.</summary>
    public delegate string Poll();

    class Job
    {
        public string command;
        public TcpClient client;
        public Poll poll;
        public double deadline;
        public string timeoutReply;
    }

    static TcpListener listener;
    static readonly ConcurrentQueue<Job> incoming = new();
    static readonly List<Job> waiting = new();
    static readonly List<string> logs = new();

    static McpBridge()
    {
        try
        {
            listener = new TcpListener(IPAddress.Loopback, Port);
            listener.Start();
        }
        catch (SocketException e)
        {
            Debug.LogWarning($"[MCP] {Port} 포트를 열 수 없습니다: {e.Message}");
            return;
        }
        new Thread(Accept) { IsBackground = true, Name = "McpBridge" }.Start();
        EditorApplication.update += Drain;
        Application.logMessageReceived += OnLog;
        AssemblyReloadEvents.beforeAssemblyReload += Stop;
        EditorApplication.quitting += Stop;
    }

    static void Stop()
    {
        try { listener?.Stop(); } catch { }
        listener = null;
    }

    // ------------------------------------------------------------ 백그라운드: 받기만
    static void Accept()
    {
        var l = listener;
        try
        {
            while (true)
            {
                var c = l.AcceptTcpClient();
                c.ReceiveTimeout = 2000;
                try
                {
                    var line = new StreamReader(c.GetStream(), new UTF8Encoding(false)).ReadLine();
                    incoming.Enqueue(new Job { command = line ?? "", client = c });
                }
                catch (IOException) { c.Close(); }
            }
        }
        catch (SocketException) { }          // Stop() 으로 닫힘
        catch (ObjectDisposedException) { }
    }

    // ------------------------------------------------------------ 메인 스레드: 실행하고 답하기
    static void Drain()
    {
        while (incoming.TryDequeue(out var job))
        {
            string reply;
            try { reply = McpCommands.Run(job.command, out job.poll, out var seconds, out job.timeoutReply) ; job.deadline = EditorApplication.timeSinceStartup + seconds; }
            catch (Exception e) { reply = "오류: " + e.Message; job.poll = null; }
            if (job.poll == null) Reply(job, reply);
            else waiting.Add(job);
        }

        for (int i = waiting.Count - 1; i >= 0; i--)
        {
            var job = waiting[i];
            string reply;
            try { reply = job.poll(); }
            catch (Exception e) { reply = "오류: " + e.Message; }
            if (reply == null && EditorApplication.timeSinceStartup > job.deadline) reply = job.timeoutReply ?? "시간 초과";
            if (reply == null) continue;
            waiting.RemoveAt(i);
            Reply(job, reply);
        }
        // 에디터가 뒤에 있어도 Play 중이면 다음 프레임이 돌게 한다
        if (waiting.Count > 0 && EditorApplication.isPlaying) EditorApplication.QueuePlayerLoopUpdate();
    }

    static void Reply(Job job, string reply)
    {
        try
        {
            var bytes = new UTF8Encoding(false).GetBytes(reply);
            var s = job.client.GetStream();
            s.Write(bytes, 0, bytes.Length);
            s.Flush();
        }
        catch (IOException) { }
        catch (ObjectDisposedException) { }
        finally { job.client.Close(); }
    }

    // ------------------------------------------------------------ 콘솔
    static void OnLog(string message, string stack, LogType type)
    {
        lock (logs)
        {
            logs.Add($"[{type}] {message}");
            if (logs.Count > MaxLogs) logs.RemoveAt(0);
        }
    }

    /// <summary>최근 로그 count 줄. includeInfo 가 아니면 에러·경고만.</summary>
    public static string RecentLogs(int count, bool includeInfo)
    {
        lock (logs)
        {
            var picked = new List<string>();
            for (int i = logs.Count - 1; i >= 0 && picked.Count < count; i--)
                if (includeInfo || !logs[i].StartsWith("[Log]")) picked.Add(logs[i]);
            picked.Reverse();
            return picked.Count == 0 ? (includeInfo ? "로그 없음" : "에러·경고 없음") : string.Join("\n", picked);
        }
    }
}
