using System.Threading.Channels;
using MultiAgent;
using PocketFlow;

// ── Game setup ───────────────────────────────────────────────────────────────

var state = new GameState
{
    TargetWord     = "nostalgic",
    ForbiddenWords = ["memory", "past", "remember", "feeling", "longing"],
    HinterChannel  = Channel.CreateUnbounded<string>(),
    GuesserChannel = Channel.CreateUnbounded<string>()
};

Console.WriteLine("=========== Taboo Game Starting! ===========");
Console.WriteLine($"Target word: {state.TargetWord}");
Console.WriteLine($"Forbidden words: [{string.Join(", ", state.ForbiddenWords)}]");
Console.WriteLine("============================================\n");

// Seed hinter channel with an empty guess to kick off the first turn
await state.HinterChannel.Writer.WriteAsync("");

// ── Wire nodes ────────────────────────────────────────────────────────────────

var hinter = new AsyncHinter();
var guesser = new AsyncGuesser();

hinter.On("continue").Then(hinter);   // hinter loops to itself
guesser.On("continue").Then(guesser); // guesser loops to itself

var hinterFlow = new AsyncFlow(start: hinter);
var guesserFlow = new AsyncFlow(start: guesser);

// ── Run both agents concurrently ──────────────────────────────────────────────

await Task.WhenAll(
    hinterFlow.RunAsync(state),
    guesserFlow.RunAsync(state)
);

Console.WriteLine("\n=========== Game Complete! ===========");
