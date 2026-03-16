using System.Text;
using System.Text.RegularExpressions;
using PocketFlow;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Thinking;

// ── Plan Formatting Helpers ───────────────────────────────────────────────────

internal static class PlanFormatter
{
    /// <summary>
    /// Recursively formats a plan list for console output (full detail).
    /// Mirrors the Python <c>format_plan</c> helper.
    /// </summary>
    public static string FormatPlan(object? planItems, int indentLevel = 0)
    {
        var indent = new string(' ', indentLevel * 2);
        var sb = new StringBuilder();

        if (planItems is List<object> list)
        {
            foreach (var item in list)
            {
                if (item is Dictionary<object, object> dict)
                {
                    var status = dict.TryGetValue("status", out var s) ? s?.ToString() ?? "Unknown" : "Unknown";
                    var desc   = dict.TryGetValue("description", out var d) ? d?.ToString() ?? "No description" : "No description";
                    var result = dict.TryGetValue("result", out var r) ? r?.ToString() : null;
                    var mark   = dict.TryGetValue("mark",   out var m) ? m?.ToString() : null;

                    var line = $"{indent}- [{status}] {desc}";
                    if (!string.IsNullOrEmpty(result)) line += $": {result}";
                    if (!string.IsNullOrEmpty(mark))   line += $" ({mark})";
                    sb.AppendLine(line);

                    if (dict.TryGetValue("sub_steps", out var subSteps) && subSteps != null)
                        sb.AppendLine(FormatPlan(subSteps, indentLevel + 1));
                }
                else if (item is string str)
                    sb.AppendLine($"{indent}- {str}");
                else
                    sb.AppendLine($"{indent}- {item}");
            }
        }
        else if (planItems is string s2)
            sb.AppendLine($"{indent}{s2}");
        else
            sb.AppendLine($"{indent}# Invalid plan format: {planItems?.GetType().Name ?? "null"}");

        return sb.ToString().TrimEnd('\n', '\r');
    }

    /// <summary>
    /// Recursively formats a plan list for inclusion in the LLM prompt (simplified view).
    /// Mirrors the Python <c>format_plan_for_prompt</c> helper.
    /// </summary>
    public static string FormatPlanForPrompt(object? planItems, int indentLevel = 0)
    {
        var indent = new string(' ', indentLevel * 2);
        var sb = new StringBuilder();

        if (planItems is List<object> list)
        {
            foreach (var item in list)
            {
                if (item is Dictionary<object, object> dict)
                {
                    var status = dict.TryGetValue("status", out var s) ? s?.ToString() ?? "Unknown" : "Unknown";
                    var desc   = dict.TryGetValue("description", out var d) ? d?.ToString() ?? "No description" : "No description";
                    sb.AppendLine($"{indent}- [{status}] {desc}");

                    if (dict.TryGetValue("sub_steps", out var subSteps) && subSteps != null)
                        sb.AppendLine(FormatPlanForPrompt(subSteps, indentLevel + 1));
                }
                else
                    sb.AppendLine($"{indent}- {item}");
            }
        }
        else
            sb.AppendLine($"{indent}{planItems}");

        return sb.ToString().TrimEnd('\n', '\r');
    }
}

// ── ChainOfThoughtNode ────────────────────────────────────────────────────────

public class ChainOfThoughtNode : Node
{
    private static readonly IDeserializer _deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

    public ChainOfThoughtNode(int maxRetries = 3, int wait = 10) : base(maxRetries, wait) { }

    // ── Prep ─────────────────────────────────────────────────────────────────

    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        var problem = store.TryGetValue("problem", out var p) ? (string)p : "";

        if (!store.ContainsKey("thoughts"))
            store["thoughts"] = new List<Dictionary<string, object?>>();
        var thoughts = (List<Dictionary<string, object?>>)store["thoughts"];

        var currentThoughtNumber = store.TryGetValue("current_thought_number", out var n) ? Convert.ToInt32(n) : 0;
        store["current_thought_number"] = currentThoughtNumber + 1;

        string thoughtsText;
        object? lastPlanStructure = null;

        if (thoughts.Count > 0)
        {
            var blocks = new List<string>();
            for (int i = 0; i < thoughts.Count; i++)
            {
                var t = thoughts[i];
                var thoughtNum = t.TryGetValue("thought_number", out var tn) ? tn : i + 1;
                var thinking   = t.TryGetValue("current_thinking", out var ct) ? ct?.ToString()?.Trim() : "N/A";
                var planList   = t.TryGetValue("planning", out var pl) ? pl : null;
                var planStr    = PlanFormatter.FormatPlan(planList, 2);

                var block = $"Thought {thoughtNum}:\n"
                          + $"  Thinking:\n{IndentText(thinking ?? "N/A", "    ")}\n"
                          + $"  Plan Status After Thought {thoughtNum}:\n{planStr}";
                blocks.Add(block);

                if (i == thoughts.Count - 1)
                    lastPlanStructure = planList;
            }
            thoughtsText = string.Join("\n--------------------\n", blocks);
        }
        else
        {
            thoughtsText = "No previous thoughts yet.";
            lastPlanStructure = new List<object>
            {
                new Dictionary<object, object> { { "description", "Understand the problem" },   { "status", "Pending" } },
                new Dictionary<object, object> { { "description", "Develop a high-level plan" }, { "status", "Pending" } },
                new Dictionary<object, object> { { "description", "Conclusion" },                { "status", "Pending" } }
            };
        }

        var lastPlanTextForPrompt = lastPlanStructure != null
            ? PlanFormatter.FormatPlanForPrompt(lastPlanStructure)
            : "# No previous plan available.";

        return new Dictionary<string, object?>
        {
            ["problem"]               = problem,
            ["thoughts_text"]         = thoughtsText,
            ["last_plan_text"]        = lastPlanTextForPrompt,
            ["last_plan_structure"]   = lastPlanStructure,
            ["current_thought_number"] = currentThoughtNumber + 1,
            ["is_first_thought"]      = thoughts.Count == 0
        };
    }

    // ── Exec ─────────────────────────────────────────────────────────────────

    protected override object? Execute(object? prepRes)
    {
        var prep               = (Dictionary<string, object?>)prepRes!;
        var problem            = (string)prep["problem"]!;
        var thoughtsText       = (string)prep["thoughts_text"]!;
        var lastPlanText       = (string)prep["last_plan_text"]!;
        var currentThoughtNum  = Convert.ToInt32(prep["current_thought_number"]);
        var isFirstThought     = (bool)prep["is_first_thought"]!;

        // ── Build prompt (mirrors nodes.py) ──────────────────────────────────
        var instructionBase = $$"""
            Your task is to generate the next thought (Thought {{currentThoughtNum}}).

            Instructions:
            1.  **Evaluate Previous Thought:** If not the first thought, start `current_thinking` by evaluating Thought {{currentThoughtNum - 1}}. State: "Evaluation of Thought {{currentThoughtNum - 1}}: [Correct/Minor Issues/Major Error - explain]". Address errors first.
            2.  **Execute Step:** Execute the first step in the plan with `status: Pending`.
            3.  **Maintain Plan (Structure):** Generate an updated `planning` list. Each item should be a dictionary with keys: `description` (string), `status` (string: "Pending", "Done", "Verification Needed"), and optionally `result` (string, concise summary when Done) or `mark` (string, reason for Verification Needed). Sub-steps are represented by a `sub_steps` key containing a *list* of these dictionaries.
            4.  **Update Current Step Status:** In the updated plan, change the `status` of the executed step to "Done" and add a `result` key with a concise summary. If verification is needed based on evaluation, change status to "Verification Needed" and add a `mark`.
            5.  **Refine Plan (Sub-steps):** If a "Pending" step is complex, add a `sub_steps` key to its dictionary containing a list of new step dictionaries (status: "Pending") breaking it down. Keep the parent step's status "Pending" until all sub-steps are "Done".
            6.  **Refine Plan (Errors):** Modify the plan logically based on evaluation findings (e.g., change status, add correction steps).
            7.  **Final Step:** Ensure the plan progresses towards a final step dictionary like `{"description": "Conclusion", "status": "Pending"}`.
            8.  **Termination:** Set `next_thought_needed` to `false` ONLY when executing the step with `description: "Conclusion"`.
            """;

        string instructionContext;
        if (isFirstThought)
        {
            instructionContext = """
                **This is the first thought:** Create an initial plan as a list of dictionaries (keys: description, status). Include sub-steps via the `sub_steps` key if needed. Then, execute the first step in `current_thinking` and provide the updated plan (marking step 1 `status: Done` with a `result`).
                """;
        }
        else
        {
            instructionContext = $"""
                **Previous Plan (Simplified View):**
                {lastPlanText}

                Start `current_thinking` by evaluating Thought {currentThoughtNum - 1}. Then, proceed with the first step where `status: Pending`. Update the plan structure (list of dictionaries) reflecting evaluation, execution, and refinements.
                """;
        }

        var instructionFormat = """
            Format your response ONLY as a YAML structure enclosed in ```yaml ... ```:
            ```yaml
            current_thinking: |
              # Evaluation of Thought N: [Assessment] ... (if applicable)
              # Thinking for the current step...
            planning:
              # List of dictionaries (keys: description, status, Optional[result, mark, sub_steps])
              - description: "Step 1"
                status: "Done"
                result: "Concise result summary"
              - description: "Step 2 Complex Task" # Now broken down
                status: "Pending" # Parent remains Pending
                sub_steps:
                  - description: "Sub-task 2a"
                    status: "Pending"
                  - description: "Sub-task 2b"
                    status: "Verification Needed"
                    mark: "Result from Thought X seems off"
              - description: "Step 3"
                status: "Pending"
              - description: "Conclusion"
                status: "Pending"
            next_thought_needed: true # Set to false ONLY when executing the Conclusion step.
            ```
            """;

        var prompt = $"""
            You are a meticulous AI assistant solving a complex problem step-by-step using a structured plan. You critically evaluate previous steps, refine the plan with sub-steps if needed, and handle errors logically. Use the specified YAML dictionary structure for the plan.

            Problem: {problem}

            Previous thoughts:
            {thoughtsText}
            --------------------
            {instructionBase}
            {instructionContext}
            {instructionFormat}
            """;

        var response = Utils.CallLlm(prompt);

        // Extract YAML block
        var yamlMatch = Regex.Match(response, @"```yaml(.*?)```", RegexOptions.Singleline);
        if (!yamlMatch.Success)
            throw new InvalidOperationException("LLM response does not contain a ```yaml block");
        var yamlStr = yamlMatch.Groups[1].Value.Trim();

        var thoughtData = _deserializer.Deserialize<Dictionary<object, object>>(yamlStr)
                          ?? throw new InvalidOperationException("YAML parsing failed, result is null");

        if (!thoughtData.ContainsKey("current_thinking"))
            throw new InvalidOperationException("LLM response missing 'current_thinking'");
        if (!thoughtData.ContainsKey("next_thought_needed"))
            throw new InvalidOperationException("LLM response missing 'next_thought_needed'");
        if (!thoughtData.ContainsKey("planning"))
            throw new InvalidOperationException("LLM response missing 'planning'");
        if (thoughtData["planning"] is not List<object>)
            throw new InvalidOperationException("LLM response 'planning' is not a list");

        return new Dictionary<string, object?>
        {
            ["current_thinking"]   = thoughtData["current_thinking"]?.ToString(),
            ["planning"]           = thoughtData["planning"],
            ["next_thought_needed"] = Convert.ToBoolean(thoughtData["next_thought_needed"]),
            ["thought_number"]     = currentThoughtNum
        };
    }

    // ── Post ─────────────────────────────────────────────────────────────────

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        var exec  = (Dictionary<string, object?>)execRes!;

        if (!store.ContainsKey("thoughts"))
            store["thoughts"] = new List<Dictionary<string, object?>>();
        var thoughts = (List<Dictionary<string, object?>>)store["thoughts"];
        thoughts.Add(exec);

        var planList        = exec.TryGetValue("planning",         out var pl)  ? pl  : "Error: Planning data missing.";
        var planFormatted   = PlanFormatter.FormatPlan(planList, 1);
        var thoughtNum      = exec.TryGetValue("thought_number",   out var tn)  ? tn  : "N/A";
        var currentThinking = exec.TryGetValue("current_thinking", out var ct)  ? ct?.ToString()?.Trim() ?? "" : "";

        var nextThoughtNeeded = exec.TryGetValue("next_thought_needed", out var ntn) && Convert.ToBoolean(ntn);

        if (!nextThoughtNeeded)
        {
            store["solution"] = currentThinking;
            Console.WriteLine($"\nThought {thoughtNum} (Conclusion):");
            Console.WriteLine(IndentText(currentThinking, "  "));
            Console.WriteLine("\nFinal Plan Status:");
            Console.WriteLine(IndentText(planFormatted, "  "));
            Console.WriteLine("\n=== FINAL SOLUTION ===");
            Console.WriteLine(currentThinking);
            Console.WriteLine("======================\n");
            return "end";
        }

        Console.WriteLine($"\nThought {thoughtNum}:");
        Console.WriteLine(IndentText(currentThinking, "  "));
        Console.WriteLine("\nCurrent Plan Status:");
        Console.WriteLine(IndentText(planFormatted, "  "));
        Console.WriteLine(new string('-', 50));

        return "continue";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static string IndentText(string text, string prefix) =>
        string.Join("\n", text.Split('\n').Select(line => prefix + line));
}


