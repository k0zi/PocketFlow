using System.Threading.Channels;
using PocketFlow;

namespace MultiAgent;

// ── GameState ────────────────────────────────────────────────────────────────

/// <summary>
/// Shared game state passed through both agent flows.
/// </summary>
public record class GameState
{
    public required string TargetWord { get; init; }
    public required List<string> ForbiddenWords { get; init; }

    /// <summary>Hinter reads guesses / "GAME_OVER" from this channel.</summary>
    public required Channel<string> HinterChannel { get; init; }

    /// <summary>Guesser reads hints from this channel.</summary>
    public required Channel<string> GuesserChannel { get; init; }

    /// <summary>Accumulates wrong guesses across turns (mutable).</summary>
    public List<string> PastGuesses { get; set; } = [];
}

// ── AsyncHinter ──────────────────────────────────────────────────────────────

public class AsyncHinter : AsyncNode
{
    /// <summary>
    /// Carries the last LLM-generated hint so <see cref="ExecFallbackAsync"/>
    /// can surface it even when all retries are exhausted.
    /// </summary>
    private sealed class ForbiddenWordException(string hint, string word)
        : InvalidOperationException($"Hint \"{hint}\" contains forbidden word \"{word}\".")
    {
        public string Hint { get; } = hint;
    }

    public AsyncHinter() : base(maxRetries: 3, wait: 0) { }

    protected override async Task<object?> PrepAsync(object shared)
    {
        var state = (GameState)shared;

        // Block until the guesser sends a guess (or the sentinel "GAME_OVER")
        var guess = await state.HinterChannel.Reader.ReadAsync();

        if (guess == "GAME_OVER")
            return null;

        return (state.TargetWord, state.ForbiddenWords, state.PastGuesses.ToList());
    }

    protected override Task<object?> ExecAsync(object? prepRes)
    {
        if (prepRes is null) return Task.FromResult<object?>(null);

        var (target, forbidden, pastGuesses) = ((string, List<string>, List<string>))prepRes;

        var prompt = $"Generate a hint for the word '{target}'\nForbidden words: {string.Join(", ", forbidden)}";
        if (pastGuesses.Count > 0)
            prompt += $"\nPrevious wrong guesses: {string.Join(", ", pastGuesses)}\nMake the hint more specific.";
        prompt += "\nUse at most 5 words. Reply with just the hint, no extra text.";

        var hint = Utils.CallLlm(prompt).Trim();

        // Validation: throw so the base-class retry loop in _ExecSingleAsync fires
        var lower = hint.ToLowerInvariant();
        foreach (var word in forbidden)
        {
            if (lower.Contains(word.ToLowerInvariant()))
                throw new ForbiddenWordException(hint, word);
        }

        Console.WriteLine($"\nHinter: Here's your hint - {hint}");
        return Task.FromResult<object?>(hint);
    }

    protected override Task<object?> ExecFallbackAsync(object? prepRes, Exception exc)
    {
        // All retries exhausted – surface the last hint with a warning
        if (exc is ForbiddenWordException fbEx)
        {
            Console.WriteLine($"\n⚠  Hinter could not avoid forbidden words after {MaxRetries} attempts.");
            Console.WriteLine($"Hinter: Here's your hint (best effort) - {fbEx.Hint}");
            return Task.FromResult<object?>(fbEx.Hint);
        }
        throw exc;
    }

    protected override async Task<object?> PostAsync(object shared, object? prepRes, object? execRes)
    {
        if (execRes is null) return "end";

        var state = (GameState)shared;
        await state.GuesserChannel.Writer.WriteAsync((string)execRes);
        return "continue";
    }
}

// ── AsyncGuesser ─────────────────────────────────────────────────────────────

public class AsyncGuesser : AsyncNode
{
    protected override async Task<object?> PrepAsync(object shared)
    {
        var state = (GameState)shared;

        // Block until the hinter sends a hint
        var hint = await state.GuesserChannel.Reader.ReadAsync();
        return (hint, state.PastGuesses.ToList());
    }

    protected override Task<object?> ExecAsync(object? prepRes)
    {
        var (hint, pastGuesses) = ((string, List<string>))prepRes!;

        var prompt = $"Given hint: {hint}";
        if (pastGuesses.Count > 0)
            prompt += $"\nPast wrong guesses: {string.Join(", ", pastGuesses)}";
        prompt += "\nGuess the single word being described. Reply with one word only:";

        var guess = Utils.CallLlm(prompt).Trim();
        Console.WriteLine($"Guesser: I guess it's - {guess}");
        return Task.FromResult<object?>(guess);
    }

    protected override async Task<object?> PostAsync(object shared, object? prepRes, object? execRes)
    {
        var state = (GameState)shared;
        var guess = (string)execRes!;

        if (string.Equals(guess, state.TargetWord, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Game Over - Correct guess!");
            await state.HinterChannel.Writer.WriteAsync("GAME_OVER");
            return "end";
        }

        state.PastGuesses.Add(guess);
        await state.HinterChannel.Writer.WriteAsync(guess);
        return "continue";
    }
}

