using OllamaSharp.Models.Chat;

namespace MultiAgent;

/// <summary>
/// Thin wrapper around <see cref="OllamaConnector"/> – mirrors utils.py.
/// </summary>
public static class Utils
{
    public static string CallLlm(string prompt)
    {
        var messages = new List<Message>
        {
            new() { Role = ChatRole.User, Content = prompt }
        };
        return OllamaConnector.CallLlm(messages);
    }
}

