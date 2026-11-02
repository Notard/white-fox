using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// White Fox 전용 Unity MCP 서버. Claude Code 가 stdio 로 띄우고, 도구 호출을 Unity 에디터 브리지(127.0.0.1:7801)로 넘긴다.
// stdout 은 MCP 통신 전용이라 로그는 모두 stderr 로 보낸다.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Services.AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly();
await builder.Build().RunAsync();
