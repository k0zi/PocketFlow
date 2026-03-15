using OllamaSharp.Models.Chat;

namespace Rag;

/// <summary>
/// Thin wrappers around <see cref="OllamaConnector"/> kept for backwards compatibility.
/// All implementations live in the SharedUtils project.
/// </summary>
public static class Utils
{
    public static string   CallLlm(string prompt)                         => OllamaConnector.CallLlm(prompt);
    public static string   CallLlm(List<Message> messages)                => OllamaConnector.CallLlm(messages);
    public static float[]  GetEmbedding(string text)                      => OllamaConnector.GetEmbedding(text);
    public static List<string> FixedSizeChunk(string text, int chunkSize = 2000) => OllamaConnector.FixedSizeChunk(text, chunkSize);
}



