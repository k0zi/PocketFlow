namespace Text2Sql;

/// <summary>
/// Thin wrapper around <see cref="OllamaConnector"/> and YAML parsing helpers.
/// Port of utils/call_llm.py.
/// </summary>
public static class Utils
{
    /// <summary>Sends a prompt to the LLM and returns the reply.</summary>
    public static string CallLlm(string prompt) => OllamaConnector.CallLlm(prompt);

    /// <summary>
    /// Extracts the SQL string from an LLM response that contains a YAML code block
    /// in the form:
    /// <code>
    /// ```yaml
    /// sql: |
    ///   SELECT ...
    /// ```
    /// </code>
    /// Port of the yaml.safe_load / split logic in nodes.py.
    /// </summary>
    public static string ParseSqlFromYaml(string llmResponse)
    {
        int start = llmResponse.IndexOf("```yaml", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
            throw new InvalidOperationException("No YAML block found in LLM response.");

        start += 7; // skip "```yaml"
        int end = llmResponse.IndexOf("```", start);
        if (end < 0)
            throw new InvalidOperationException("YAML block not properly closed.");

        var yamlStr = llmResponse[start..end].Trim();
        var lines = yamlStr.Split('\n');

        bool collecting = false;
        var sqlLines = new List<string>();

        foreach (var rawLine in lines)
        {
            if (!collecting)
            {
                var trimmed = rawLine.TrimStart();
                if (trimmed.StartsWith("sql:", StringComparison.Ordinal))
                {
                    var afterColon = trimmed[4..].Trim();

                    // Block scalar indicators (|, |-, |+, >, >-, >+)
                    if (afterColon is "" or "|" or "|-" or "|+" or ">" or ">-" or ">+")
                    {
                        collecting = true;
                    }
                    else
                    {
                        // Inline value: sql: SELECT ...
                        return afterColon.TrimEnd(';').Trim();
                    }
                }
            }
            else
            {
                // Collect indented lines for block scalar
                if (rawLine.Trim().Length == 0)
                    sqlLines.Add("");
                else if (rawLine.Length > 0 && (rawLine[0] == ' ' || rawLine[0] == '\t'))
                    sqlLines.Add(rawLine.TrimStart());
                else
                    break; // end of block
            }
        }

        return string.Join("\n", sqlLines).Trim().TrimEnd(';');
    }
}

