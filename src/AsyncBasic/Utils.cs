namespace AsyncBasic;

/// <summary>
/// Utility helpers for the async Recipe Finder.
/// Mirrors utils.py from the original Python example.
/// </summary>
internal static class Utils
{
    // ── Recipe Fetching ──────────────────────────────────────────────────────

    /// <summary>
    /// Fetches mock recipes for the given ingredient asynchronously.
    /// Simulates a remote API call with a short delay.
    /// </summary>
    public static async Task<List<string>> FetchRecipesAsync(string ingredient)
    {
        Console.WriteLine($"Fetching recipes for {ingredient}...");

        // Simulate async I/O (e.g. HTTP request)
        await Task.Delay(1_000);

        var recipes = new List<string>
        {
            $"{ingredient} Stir Fry",
            $"Grilled {ingredient} with Herbs",
            $"Baked {ingredient} with Vegetables"
        };

        Console.WriteLine($"Found {recipes.Count} recipes.");
        return recipes;
    }

    // ── LLM ─────────────────────────────────────────────────────────────────

    /// <summary>
    /// Sends <paramref name="prompt"/> to the LLM asynchronously and returns
    /// the model's reply. Uses <see cref="OllamaConnector.CallLlm"/> on a
    /// background thread so the async flow is never blocked.
    /// </summary>
    public static async Task<string> CallLlmAsync(string prompt)
    {
        Console.WriteLine("\nSuggesting best recipe...");

        // Run the blocking Ollama call on a thread-pool thread
        var suggestion = await Task.Run<string>(() => OllamaConnector.CallLlm(prompt));

        Console.WriteLine($"How about: {suggestion}");
        return suggestion;
    }

    // ── User Input ───────────────────────────────────────────────────────────

    /// <summary>
    /// Reads a line from the console asynchronously (non-blocking).
    /// Returns the trimmed, lower-cased answer.
    /// </summary>
    public static async Task<string> GetUserInputAsync(string prompt)
    {
        Console.Write(prompt);

        // Console.In.ReadLineAsync is truly async on .NET 10
        var answer = await Console.In.ReadLineAsync() ?? string.Empty;
        return answer.Trim().ToLowerInvariant();
    }
}


