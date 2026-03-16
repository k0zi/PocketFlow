using MCP;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using PocketFlow;
using System.ComponentModel;

// ── MCP Server mode ───────────────────────────────────────────────────────────
// When the process is launched with "--serve" it acts as an MCP stdio server
// that exposes the four math tools.  The agent spawns itself in this mode
// when Utils.UseMcp = true.

if (args.Contains("--serve"))
{
    var serverBuilder = Host.CreateApplicationBuilder(args.Where(a => a != "--serve").ToArray());
    serverBuilder.Logging.ClearProviders();          // keep stdio clean for MCP protocol
    serverBuilder.Services.AddMcpServer()
        .WithStdioServerTransport()
        .WithTools<MathServerTools>();
    await serverBuilder.Build().RunAsync();
    return;
}

// ── Agent mode ────────────────────────────────────────────────────────────────

// Toggle: set true to call tools through the MCP server,
//         set false to call the local implementations directly.
Utils.UseMcp = false;

const string defaultQuestion = "What is 1234567890123456789 plus 987654321098765432?";

var question = defaultQuestion;
foreach (var arg in args)
{
    if (arg.StartsWith("--"))
    {
        question = arg[2..];
        break;
    }
}

Console.WriteLine($"🤔 Processing question: {question}");
Console.WriteLine($"🔌 Mode: {(Utils.UseMcp ? "MCP" : "Local")}");

// Build the flow (mirrors main.py)
var getToolsNode = new GetToolsNode();
var decideNode   = new DecideToolNode();
var executeNode  = new ExecuteToolNode();

_ = getToolsNode.On("decide").Then(decideNode);
_ = decideNode.On("execute").Then(executeNode);

var flow   = new Flow(start: getToolsNode);
var shared = new Dictionary<string, object> { ["question"] = question };
flow.Run(shared);

// ── MCP Server Tools ──────────────────────────────────────────────────────────
// These are exposed when the process runs in --serve mode.
// Mirrors simple_server.py.

[McpServerToolType]
public class MathServerTools
{
    [McpServerTool, Description("Add two numbers together")]
    public long Add(long a, long b) => a + b;

    [McpServerTool, Description("Subtract b from a")]
    public long Subtract(long a, long b) => a - b;

    [McpServerTool, Description("Multiply two numbers together")]
    public long Multiply(long a, long b) => a * b;

    [McpServerTool, Description("Divide a by b")]
    public double Divide(long a, long b)
    {
        if (b == 0) throw new ArgumentException("Division by zero is not allowed");
        return (double)a / b;
    }
}
