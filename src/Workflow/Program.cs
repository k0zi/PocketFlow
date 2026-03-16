using PocketFlow;
using Workflow;

// ── Build the flow (mirrors flow.py) ─────────────────────────────────────────

var outlineNode = new GenerateOutline();
var writeNode   = new WriteSimpleContent();
var styleNode   = new ApplyStyle();

outlineNode
    .Then(writeNode)
    .Then(styleNode);

var flow = new Flow(start: outlineNode);

// ── Read topic from CLI (mirrors main.py) ─────────────────────────────────────

var topic = args.Length > 0 ? string.Join(" ", args) : "AI Safety";

// ── Run ──────────────────────────────────────────────────────────────────────

Console.WriteLine($"\n=== Starting Article Workflow on Topic: {topic} ===\n");

var shared = new Dictionary<string, object> { ["topic"] = topic };
flow.Run(shared);

Console.WriteLine("\n=== Workflow Completed ===\n");
Console.WriteLine($"Topic: {shared["topic"]}");
Console.WriteLine($"Outline Length: {((string)shared["outline"]).Length} characters");
Console.WriteLine($"Draft Length: {((string)shared["draft"]).Length} characters");
Console.WriteLine($"Final Article Length: {((string)shared["final_article"]).Length} characters");
