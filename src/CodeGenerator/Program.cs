using CodeGenerator;
using PocketFlow;

// ── Build the flow (mirrors flow.py) ─────────────────────────────────────────

var generateTests = new GenerateTestCasesNode();
var implement     = new ImplementFunctionNode();
var runTests      = new RunTestsNode();
var revise        = new ReviseNode();

_ = generateTests >> implement;        // always proceed to implement
_ = implement     >> runTests;         // always proceed to run tests
_ = (runTests - "failure") >> revise;  // on failure: revise
_ = revise        >> runTests;         // after revision: re-run tests
// "success" and "max_iterations" have no successor → flow ends

var flow = new Flow(start: generateTests);

// ── Problem (mirrors main.py) ─────────────────────────────────────────────────

const string defaultProblem = """
Two Sum

Given an array of integers nums and an integer target, return indices of the
two numbers such that they add up to target.

You may assume that each input would have exactly one solution, and you may not
use the same element twice.

Example 1:
  Input:  nums = [2,7,11,15], target = 9
  Output: [0,1]

Example 2:
  Input:  nums = [3,2,4], target = 6
  Output: [1,2]

Example 3:
  Input:  nums = [3,3], target = 6
  Output: [0,1]
""";

var problem = args.Length > 0 ? string.Join(" ", args) : defaultProblem;

// ── Shared state ──────────────────────────────────────────────────────────────

var shared = new Dictionary<string, object>
{
    ["problem"]         = problem,
    ["test_cases"]      = new List<object>(),
    ["function_code"]   = string.Empty,
    ["test_results"]    = new List<object>(),
    ["iteration_count"] = 0,
    ["max_iterations"]  = 5
};

// ── Run ───────────────────────────────────────────────────────────────────────

Console.WriteLine("Starting PocketFlow Code Generator...");
flow.Run(shared);

// ── Print final summary ───────────────────────────────────────────────────────

Console.WriteLine("\n=== Final Results ===");

var prob = (string)shared["problem"];
Console.WriteLine($"Problem:    {prob[..Math.Min(50, prob.Length)].Replace('\n', ' ')}...");
Console.WriteLine($"Iterations: {shared["iteration_count"]}");
Console.WriteLine($"Function:\n{shared["function_code"]}");

if (shared["test_results"] is List<Dictionary<string, object?>> results)
{
    var passed = results.Count(r => (bool)r["passed"]!);
    Console.WriteLine($"Test Results: {passed}/{results.Count} passed");
}

