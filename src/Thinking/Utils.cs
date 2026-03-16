namespace Thinking;

public static class Utils
{
    public static string CallLlm(string prompt) => OllamaConnector.CallLlm(prompt);
}

