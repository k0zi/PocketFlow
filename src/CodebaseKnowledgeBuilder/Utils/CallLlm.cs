using System.Text.Json;
using OllamaSharp.Models.Chat;

namespace CodebaseKnowledgeBuilder.Utils;

/// <summary>
/// Wraps OllamaConnector with a JSON disk cache keyed by prompt text.
/// Mirrors python/utils/call_llm.py (Ollama path only).
/// </summary>
public static class CallLlm
{
    private static readonly string CacheFile =
        Environment.GetEnvironmentVariable("LLM_CACHE_FILE") ?? "llm_cache.json";

    private static readonly string LogDir =
        Environment.GetEnvironmentVariable("LOG_DIR") ?? "logs";

    // ── Public entry point ──────────────────────────────────────────────────

    public static string Call(string prompt, bool useCache = true)
    {
        Log($"PROMPT: {prompt}");

        if (useCache)
        {
            var cache = LoadCache();
            if (cache.TryGetValue(prompt, out var cached))
            {
                Log($"CACHE HIT");
                return cached;
            }
        }

        var messages = new List<Message>
        {
            new() { Role = ChatRole.User, Content = prompt }
        };

        var response = OllamaConnector.CallLlm(messages);
        Log($"RESPONSE: {response}");

        if (useCache)
        {
            var cache = LoadCache();
            cache[prompt] = response;
            SaveCache(cache);
        }

        return response;
    }

    // ── Cache helpers ───────────────────────────────────────────────────────

    private static Dictionary<string, string> LoadCache()
    {
        try
        {
            if (!File.Exists(CacheFile)) return new();
            var json = File.ReadAllText(CacheFile);
            return JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new();
        }
        catch
        {
            return new();
        }
    }

    private static void SaveCache(Dictionary<string, string> cache)
    {
        try
        {
            File.WriteAllText(CacheFile, JsonSerializer.Serialize(cache));
        }
        catch
        {
            // non-fatal
        }
    }

    // ── Logging ─────────────────────────────────────────────────────────────

    private static void Log(string message)
    {
        try
        {
            Directory.CreateDirectory(LogDir);
            var logFile = Path.Combine(LogDir, $"llm_calls_{DateTime.Now:yyyyMMdd}.log");
            File.AppendAllText(logFile, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // non-fatal
        }
    }
}

