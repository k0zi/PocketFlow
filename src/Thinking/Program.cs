using PocketFlow;
using Thinking;

// ── Build the flow (mirrors flow.py) ─────────────────────────────────────────

var cotNode = new ChainOfThoughtNode(maxRetries: 3, wait: 10);

// Connect the node to itself for the "continue" action
cotNode.On("continue").Then(cotNode);

var flow = new Flow(start: cotNode);

// ── Read question from CLI (mirrors main.py) ──────────────────────────────────

const string defaultQuestion =
    "You keep rolling a fair die until you roll three, four, five in that order consecutively on three rolls. " +
    "What is the probability that you roll the die an odd number of times?";

var question = defaultQuestion;
foreach (var arg in args)
{
    if (arg.StartsWith("--"))
    {
        question = arg[2..];
        break;
    }
}

Console.WriteLine($"🤔 Processing question: {question}");

// ── Set up shared state ───────────────────────────────────────────────────────

var shared = new Dictionary<string, object>
{
    ["problem"]               = question,
    ["thoughts"]              = new List<Dictionary<string, object?>>(),
    ["current_thought_number"] = 0,
    ["solution"]              = null!
};

// ── Run ───────────────────────────────────────────────────────────────────────

flow.Run(shared);

