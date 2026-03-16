namespace MCP;

/// <summary>
/// Describes a single parameter accepted by a tool.
/// </summary>
public class ParameterDef
{
    public string Type { get; set; } = "integer";
}

/// <summary>
/// Normalised tool metadata used by both the local and MCP code-paths.
/// </summary>
public class ToolDefinition
{
    public string Name        { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    /// <summary>Parameter name → parameter descriptor.</summary>
    public Dictionary<string, ParameterDef> Properties { get; set; } = new();

    /// <summary>Names of required parameters.</summary>
    public List<string> Required { get; set; } = new();
}

