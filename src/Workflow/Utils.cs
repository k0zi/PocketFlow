namespace Workflow;

public static class Utils
{
    /// <summary>
    /// Calls the LLM with a single user prompt and returns the response.
    /// Mirrors utils/call_llm.py from the Python cookbook.
    /// </summary>
    public static string CallLlm(string prompt) => OllamaConnector.CallLlm(prompt);
}

