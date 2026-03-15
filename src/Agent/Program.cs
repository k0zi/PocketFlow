using Agent;
using PocketFlow;

// ── Build the flow (mirrors flow.py) ────────────────────────────────────────

var decide = new DecideActionNode();
var search = new SearchWebNode();
var answer = new AnswerQuestionNode();

_ = (decide - "search") >> search;   // search branch
_ = (decide - "answer") >> answer;   // answer branch
_ = (search - "decide") >> decide;   // loop back after each search

var flow = new Flow(start: decide);

// ── Read question from CLI (mirrors main.py) ─────────────────────────────────

const string defaultQuestion = "Who won the Nobel Prize in Physics 2024?";

var question = defaultQuestion;
foreach (var arg in args)
{
    if (arg.StartsWith("--"))
    {
        question = arg[2..];
        break;
    }
}

// ── Run ──────────────────────────────────────────────────────────────────────

var shared = new Dictionary<string, object> { ["question"] = question };

Console.WriteLine($"🤔 Processing question: {question}");
flow.Run(shared);

Console.WriteLine("\n🎯 Final Answer:");
Console.WriteLine(shared.TryGetValue("answer", out var ans) ? ans : "No answer found");
