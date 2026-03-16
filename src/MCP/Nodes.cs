using System.Text.RegularExpressions;
using PocketFlow;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace MCP;

// ── GetToolsNode ──────────────────────────────────────────────────────────────

/// <summary>
/// Retrieves the list of available tools and stores them in shared state.
/// Mirrors <c>GetToolsNode</c> from <c>main.py</c>.
/// </summary>
public class GetToolsNode : Node
{
    protected override object? Prepare(object shared)
    {
        Console.WriteLine("🔍 Getting available tools...");
        return null;
    }

    protected override object? Execute(object? prepRes)
        => Utils.GetTools();

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        var tools = (List<ToolDefinition>)execRes!;

        store["tools"] = tools;

        // Build a human-readable tool list for the LLM prompt
        var toolInfo = tools.Select((tool, idx) =>
        {
            var paramLines = tool.Properties.Select(kvp =>
            {
                var req = tool.Required.Contains(kvp.Key) ? "(Required)" : "(Optional)";
                return $"    - {kvp.Key} ({kvp.Value.Type}): {req}";
            });
            return $"[{idx + 1}] {tool.Name}\n  Description: {tool.Description}\n  Parameters:\n{string.Join("\n", paramLines)}";
        });

        store["tool_info"] = string.Join("\n", toolInfo);
        return "decide";
    }
}

// ── DecideToolNode ────────────────────────────────────────────────────────────

/// <summary>
/// Sends the question and available tools to the LLM and extracts which tool
/// to call with which parameters.
/// Mirrors <c>DecideToolNode</c> from <c>main.py</c>.
/// </summary>
public class DecideToolNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store    = (Dictionary<string, object>)shared;
        var toolInfo = store["tool_info"].ToString()!;
        var question = store["question"].ToString()!;

        return $"""
### CONTEXT
You are an assistant that can use tools via Model Context Protocol (MCP).

### ACTION SPACE
{toolInfo}

### TASK
Answer this question: "{question}"

## NEXT ACTION
Analyse the question, extract any numbers or parameters, and decide which tool to use.
Return your response in this format:

```yaml
thinking: |
    <your step-by-step reasoning about what the question is asking and what numbers to extract>
tool: <name of the tool to use>
reason: <why you chose this tool>
parameters:
    <parameter_name>: <parameter_value>
    <parameter_name>: <parameter_value>
```
IMPORTANT:
1. Extract numbers from the question properly
2. Use proper indentation (4 spaces) for multi-line fields
3. Use the | character for multi-line text fields
""";
    }

    protected override object? Execute(object? prepRes)
    {
        Console.WriteLine("🤔 Analysing question and deciding which tool to use...");
        return Utils.CallLlm((string)prepRes!);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store    = (Dictionary<string, object>)shared;
        var response = (string)execRes!;

        try
        {
            var yamlBlock = ExtractYamlBlock(response);
            var decision  = ParseYaml(yamlBlock);

            var toolName = decision.TryGetValue("tool", out var t)
                ? t.ToString() ?? string.Empty
                : string.Empty;

            // Convert the nested YAML map to a string-keyed dict with proper types
            var parameters = new Dictionary<string, object?>();
            if (decision.TryGetValue("parameters", out var p) && p is Dictionary<object, object> paramsDict)
            {
                foreach (var kvp in paramsDict)
                {
                    var key = kvp.Key.ToString()!;
                    var val = NormalizeScalar(kvp.Value);
                    parameters[key] = val;
                }
            }

            store["tool_name"]  = toolName;
            store["parameters"] = parameters;
            store["thinking"]   = decision.TryGetValue("thinking", out var th) ? th.ToString() ?? "" : "";

            Console.WriteLine($"💡 Selected tool: {toolName}");
            Console.WriteLine($"🔢 Extracted parameters: {string.Join(", ", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}"))}");

            return "execute";
        }
        catch (Exception ex)
        {
            Console.WriteLine($"❌ Error parsing LLM response: {ex.Message}");
            Console.WriteLine($"Raw response:\n{response}");
            return null;
        }
    }

    // ── YAML helpers ─────────────────────────────────────────────────────────

    private static string ExtractYamlBlock(string text)
    {
        var match = Regex.Match(text, @"```yaml(.*?)```",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
    }

    private static readonly IDeserializer _yamlDeserializer =
        new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

    private static Dictionary<object, object> ParseYaml(string yaml)
        => _yamlDeserializer.Deserialize<Dictionary<object, object>>(yaml)
           ?? [];

    /// <summary>
    /// Converts a YAML scalar value to a numeric type when possible so that
    /// it can be safely forwarded to both local and MCP tool implementations.
    /// </summary>
    private static object? NormalizeScalar(object? val)
    {
        if (val is string s && long.TryParse(s, out var lv)) return lv;
        if (val is string s2 && double.TryParse(s2,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture, out var dv)) return dv;
        return val;
    }
}

// ── ExecuteToolNode ───────────────────────────────────────────────────────────

/// <summary>
/// Executes the tool chosen by <see cref="DecideToolNode"/> and prints the result.
/// Mirrors <c>ExecuteToolNode</c> from <c>main.py</c>.
/// </summary>
public class ExecuteToolNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (
            store["tool_name"].ToString()!,
            (Dictionary<string, object?>)store["parameters"]
        );
    }

    protected override object? Execute(object? prepRes)
    {
        var (toolName, parameters) = ((string, Dictionary<string, object?>))prepRes!;
        Console.WriteLine($"🔧 Executing tool '{toolName}' with parameters: " +
            string.Join(", ", parameters.Select(kvp => $"{kvp.Key}={kvp.Value}")));
        return Utils.CallTool(toolName, parameters);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        Console.WriteLine($"\n✅ Final Answer: {execRes}");
        return "done";
    }
}




