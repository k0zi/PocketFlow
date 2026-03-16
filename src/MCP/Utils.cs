using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using OllamaSharp.Models.Chat;
using System.Globalization;

namespace MCP;

public static class Utils
{
    // ── Configuration ────────────────────────────────────────────────────────

    /// <summary>
    /// When <c>true</c>, tools are discovered and called through the MCP
    /// protocol (this process is re-spawned as a server via stdio).
    /// When <c>false</c>, equivalent local implementations are used directly.
    /// </summary>
    public static bool UseMcp { get; set; } = false;

    // ── LLM ─────────────────────────────────────────────────────────────────

    public static string CallLlm(string prompt)
    {
        var messages = new List<Message>
        {
            new() { Role = ChatRole.User, Content = prompt }
        };
        return OllamaConnector.CallLlm(messages);
    }

    // ── Tool Management ──────────────────────────────────────────────────────

    /// <summary>Returns the list of available tools.</summary>
    public static List<ToolDefinition> GetTools()
        => UseMcp ? McpGetTools() : LocalGetTools();

    /// <summary>Calls a tool by name and returns its result as a string.</summary>
    public static string CallTool(string toolName, Dictionary<string, object?> arguments)
        => UseMcp ? McpCallTool(toolName, arguments) : LocalCallTool(toolName, arguments);

    // ── Local Implementation ─────────────────────────────────────────────────

    private static List<ToolDefinition> LocalGetTools() => new()
    {
        new() { Name = "add",      Description = "Add two numbers together",     Properties = IntProps(), Required = ["a", "b"] },
        new() { Name = "subtract", Description = "Subtract b from a",            Properties = IntProps(), Required = ["a", "b"] },
        new() { Name = "multiply", Description = "Multiply two numbers together", Properties = IntProps(), Required = ["a", "b"] },
        new() { Name = "divide",   Description = "Divide a by b",                Properties = IntProps(), Required = ["a", "b"] },
    };

    private static Dictionary<string, ParameterDef> IntProps() => new()
    {
        ["a"] = new ParameterDef { Type = "integer" },
        ["b"] = new ParameterDef { Type = "integer" },
    };

    private static string LocalCallTool(string toolName, Dictionary<string, object?> arguments)
    {
        long a = GetLong(arguments, "a");
        long b = GetLong(arguments, "b");
        return toolName switch
        {
            "add"      => (a + b).ToString(),
            "subtract" => (a - b).ToString(),
            "multiply" => (a * b).ToString(),
            "divide"   => b == 0
                ? "Error: Division by zero is not allowed"
                : ((double)a / b).ToString(CultureInfo.InvariantCulture),
            _ => $"Error: Unknown tool '{toolName}'"
        };
    }

    private static long GetLong(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var val) || val is null)
            throw new ArgumentException($"Missing required argument '{key}'");
        return Convert.ToInt64(val);
    }

    // ── MCP Implementation ───────────────────────────────────────────────────

    /// <summary>
    /// Spawns this executable in <c>--serve</c> mode, connects via stdio,
    /// and returns the tool list exposed by the MCP server.
    /// </summary>
    private static List<ToolDefinition> McpGetTools()
    {
        return Task.Run(async () =>
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command   = Environment.ProcessPath!,
                Arguments = ["--serve"]
            });
            await using var client = await McpClient.CreateAsync(transport);
            IList<McpClientTool> mcpTools = await client.ListToolsAsync();
            return mcpTools.Select(ConvertMcpTool).ToList();
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Spawns this executable in <c>--serve</c> mode, calls the named tool,
    /// and returns the text content of the result.
    /// </summary>
    private static string McpCallTool(string toolName, Dictionary<string, object?> arguments)
    {
        return Task.Run(async () =>
        {
            var transport = new StdioClientTransport(new StdioClientTransportOptions
            {
                Command   = Environment.ProcessPath!,
                Arguments = ["--serve"]
            });
            await using var client = await McpClient.CreateAsync(transport);
            CallToolResult result = await client.CallToolAsync(toolName, arguments);
            return (result.Content.FirstOrDefault() as TextContentBlock)?.Text
                   ?? string.Empty;
        }).GetAwaiter().GetResult();
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static ToolDefinition ConvertMcpTool(McpClientTool mcpTool)
    {
        var tool = new ToolDefinition
        {
            Name        = mcpTool.Name,
            Description = mcpTool.Description
        };

        var schema = mcpTool.JsonSchema;

        if (schema.TryGetProperty("properties", out var props))
        {
            foreach (var prop in props.EnumerateObject())
            {
                var paramDef = new ParameterDef();
                if (prop.Value.TryGetProperty("type", out var typeEl))
                    paramDef.Type = typeEl.GetString() ?? "integer";
                tool.Properties[prop.Name] = paramDef;
            }
        }

        if (schema.TryGetProperty("required", out var required))
        {
            foreach (var r in required.EnumerateArray())
            {
                var reqStr = r.GetString();
                if (reqStr is not null) tool.Required.Add(reqStr);
            }
        }

        return tool;
    }
}




