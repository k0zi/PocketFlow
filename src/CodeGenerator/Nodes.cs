using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PocketFlow;
using YamlDotNet.Core;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodeGenerator;

// ── Shared YAML helpers ───────────────────────────────────────────────────────

internal static class YamlHelper
{
    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(NullNamingConvention.Instance)
            .Build();

    public static Dictionary<object, object> ParseBlock(string llmResponse)
    {
        var block = ExtractBlock(llmResponse);
        return ParseSafely(block);
    }

    private static string ExtractBlock(string text)
    {
        var match = Regex.Match(text, @"```yaml(.*?)```",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return match.Success ? match.Groups[1].Value.Trim() : text.Trim();
    }

    private static Dictionary<object, object> ParseSafely(string block)
    {
        // First pass — try as-is
        try
        {
            return Deserializer.Deserialize<Dictionary<object, object>>(block)
                   ?? throw new InvalidOperationException("YAML deserialized to null.");
        }
        catch (YamlException)
        {
            // Second pass — rewrite bare scalar lines for known block-scalar keys
            var blockKeys = new HashSet<string> { "reasoning", "function_code" };
            var fixedLines = block.Split('\n').Select(line =>
            {
                var m = Regex.Match(line, @"^(\w+):\s*(.*)$");
                if (m.Success && blockKeys.Contains(m.Groups[1].Value) && !line.Contains('|'))
                {
                    var key = m.Groups[1].Value;
                    var val = m.Groups[2].Value.Trim();
                    return string.IsNullOrEmpty(val) ? $"{key}: |" : $"{key}: |\n  {val}";
                }
                return line;
            });

            var fixedBlock = string.Join("\n", fixedLines);
            try
            {
                return Deserializer.Deserialize<Dictionary<object, object>>(fixedBlock)
                       ?? throw new InvalidOperationException("YAML deserialized to null.");
            }
            catch (YamlException ex)
            {
                throw new InvalidOperationException(
                    $"Unable to parse LLM YAML response:\n{block}", ex);
            }
        }
    }
}

// ── GenerateTestCasesNode ─────────────────────────────────────────────────────
// Mirrors nodes.py::GenerateTestCases
// Prompts the LLM for 5-7 C# test cases and stores them in shared["test_cases"].

public class GenerateTestCasesNode : Node
{
    protected override object? Prep(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (string)store["problem"];
    }

    protected override object? Exec(object? prepRes)
    {
        var problem = (string)prepRes!;
        Console.WriteLine("🧪 Generating test cases...");

        var prompt = $$"""
Generate 5-7 test cases for this C# coding problem:

{{problem}}

IMPORTANT:
- Parameter names in 'input' must exactly match the C# parameter names that RunCode will use.
- Use simple scalar or list values only (no nested objects).

Output in this YAML format:
```yaml
reasoning: |
    The parameters should be...
    I will consider basic, edge and corner cases.
test_cases:
  - name: "Basic case"
    input: {param1: value1, param2: value2}
    expected: result1
  - name: "Edge case - empty"
    input: {param1: value3, param2: value4}
    expected: result2
```
""";

        var response  = Utils.CallLlm(prompt);
        var result    = YamlHelper.ParseBlock(response);

        if (!result.ContainsKey("test_cases"))
            throw new InvalidOperationException("LLM response is missing 'test_cases'");

        var testCases = (List<object>)result["test_cases"];
        for (var i = 0; i < testCases.Count; i++)
        {
            var tc = (Dictionary<object, object>)testCases[i];
            if (!tc.ContainsKey("name"))
                throw new InvalidOperationException($"Test case {i} is missing 'name'");
            if (!tc.ContainsKey("input"))
                throw new InvalidOperationException($"Test case {i} is missing 'input'");
            if (!tc.ContainsKey("expected"))
                throw new InvalidOperationException($"Test case {i} is missing 'expected'");
            if (tc["input"] is not Dictionary<object, object>)
                throw new InvalidOperationException($"Test case {i} 'input' must be a mapping");
        }

        return result;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store     = (Dictionary<string, object>)shared;
        var result    = (Dictionary<object, object>)execRes!;
        var testCases = (List<object>)result["test_cases"];

        store["test_cases"] = testCases;

        Console.WriteLine($"\n=== Generated {testCases.Count} Test Cases ===");
        for (var i = 0; i < testCases.Count; i++)
        {
            var tc = (Dictionary<object, object>)testCases[i];
            Console.WriteLine($"{i + 1}. {tc["name"]}");
            Console.WriteLine($"   input:    {tc["input"]}");
            Console.WriteLine($"   expected: {tc["expected"]}");
        }

        return "default";
    }
}

// ── ImplementFunctionNode ─────────────────────────────────────────────────────
// Mirrors nodes.py::ImplementFunction
// Prompts the LLM to write a C# "public static object RunCode(...)" method
// and stores the code string in shared["function_code"].

public class ImplementFunctionNode : Node
{
    protected override object? Prep(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return ((string)store["problem"], (List<object>)store["test_cases"]);
    }

    protected override object? Exec(object? prepRes)
    {
        var (problem, testCases) = ((string, List<object>))prepRes!;
        Console.WriteLine("⚙️  Implementing C# solution...");

        var sb = new StringBuilder();
        for (var i = 0; i < testCases.Count; i++)
        {
            var tc = (Dictionary<object, object>)testCases[i];
            sb.AppendLine($"{i + 1}. {tc["name"]}");
            sb.AppendLine($"   input:    {tc["input"]}");
            sb.AppendLine($"   expected: {tc["expected"]}");
        }

        var prompt = $$"""
Implement a C# solution for this problem:

{{problem}}

Test cases to pass:
{{sb}}
IMPORTANT:
- The method MUST be named exactly "RunCode".
- It MUST be declared as: public static object RunCode(...)
- Parameter names MUST exactly match the keys shown in the test case inputs above.
- Do NOT include using statements, a namespace, or a class declaration — provide only the method.
- The return type in the signature must be object; cast the result explicitly if needed.

Output in this YAML format:
```yaml
reasoning: |
    To solve this I will...
    My algorithm is...
function_code: |
    public static object RunCode(int[] nums, int target)
    {
        // implementation
        return result;
    }
```
""";

        var response = Utils.CallLlm(prompt);
        var result   = YamlHelper.ParseBlock(response);

        if (!result.ContainsKey("function_code"))
            throw new InvalidOperationException("LLM response is missing 'function_code'");

        var code = result["function_code"]?.ToString()
                   ?? throw new InvalidOperationException("'function_code' is null");

        if (!code.Contains("RunCode"))
            throw new InvalidOperationException(
                "Generated code does not contain 'RunCode' — the method must be named exactly RunCode");

        return code;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        var code  = (string)execRes!;

        store["function_code"] = code;

        Console.WriteLine("\n=== Implemented Function ===");
        Console.WriteLine(code);

        return "default";
    }
}

// ── RunTestsNode ──────────────────────────────────────────────────────────────
// Mirrors nodes.py::RunTests (BatchNode)
// Compiles and runs the C# RunCode method against every test case in parallel
// via BatchNode, then decides "success" / "failure" / "max_iterations".

public class RunTestsNode : BatchNode
{
    // Prep returns a list; BatchNode calls Exec once per element.
    protected override object? Prep(object shared)
    {
        var store        = (Dictionary<string, object>)shared;
        var functionCode = (string)store["function_code"];
        var testCases    = (List<object>)store["test_cases"];

        return testCases
            .Select(tc => (functionCode, (Dictionary<object, object>)tc))
            .ToList();
    }

    // Exec receives one (functionCode, testCase) tuple at a time.
    protected override object? Exec(object? prepRes)
    {
        var (functionCode, testCase) = ((string, Dictionary<object, object>))prepRes!;
        var input    = (Dictionary<object, object>)testCase["input"];
        var expected = testCase["expected"];

        var (actual, error) = Utils.ExecuteCode(functionCode, input);

        if (error is not null)
        {
            return new Dictionary<string, object?>
            {
                ["test_case"] = testCase,
                ["passed"]    = (object)false,
                ["actual"]    = null,
                ["expected"]  = expected,
                ["error"]     = error
            };
        }

        var passed = Utils.ValuesEqual(actual, expected);
        return new Dictionary<string, object?>
        {
            ["test_case"] = testCase,
            ["passed"]    = (object)passed,
            ["actual"]    = actual,
            ["expected"]  = expected,
            ["error"]     = passed
                ? null
                : (object?)$"Expected {Str(expected)}, got {Str(actual)}"
        };
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;

        var results = ((List<object?>)execRes!)
            .Select(r => (Dictionary<string, object?>)r!)
            .ToList();

        store["test_results"]    = results;
        store["iteration_count"] = (int)store.GetValueOrDefault("iteration_count", 0) + 1;

        var iteration = (int)store["iteration_count"];
        var passed    = results.Count(r => (bool)r["passed"]!);
        var total     = results.Count;
        var allPassed = passed == total;

        Console.WriteLine($"\n=== Test Results: {passed}/{total} Passed ===");

        var failed = results.Where(r => !(bool)r["passed"]!).ToList();
        if (failed.Count > 0)
        {
            Console.WriteLine("Failed tests:");
            for (var i = 0; i < failed.Count; i++)
            {
                var r  = failed[i];
                var tc = (Dictionary<object, object>)r["test_case"]!;
                Console.WriteLine($"{i + 1}. {tc["name"]}:");
                Console.WriteLine(r["error"] is not null
                    ? $"   error:    {r["error"]}"
                    : $"   actual:   {Str(r["actual"])}");
                Console.WriteLine($"   expected: {Str(r["expected"])}");
            }
        }

        if (allPassed) return "success";

        var maxIter = (int)store.GetValueOrDefault("max_iterations", 5);
        return iteration >= maxIter ? "max_iterations" : "failure";
    }

    private static string Str(object? v) =>
        v is null ? "null" : JsonSerializer.Serialize(v, v.GetType());
}

// ── ReviseNode ────────────────────────────────────────────────────────────────
// Mirrors nodes.py::Revise
// Prompts the LLM to diagnose failures and output revised test cases and/or
// a revised function.  Updates shared state accordingly.

public class ReviseNode : Node
{
    protected override object? Prep(object shared)
    {
        var store       = (Dictionary<string, object>)shared;
        var testResults = (List<Dictionary<string, object?>>)store["test_results"];
        var failedTests = testResults.Where(r => !(bool)r["passed"]!).ToList();

        return new Dictionary<string, object>
        {
            ["problem"]       = store["problem"],
            ["test_cases"]    = store["test_cases"],
            ["function_code"] = store["function_code"],
            ["failed_tests"]  = (object)failedTests
        };
    }

    protected override object? Exec(object? prepRes)
    {
        var inputs      = (Dictionary<string, object>)prepRes!;
        var testCases   = (List<object>)inputs["test_cases"];
        var failedTests = (List<Dictionary<string, object?>>)inputs["failed_tests"];

        // ── Format current test cases ─────────────────────────────────────
        var sbTests = new StringBuilder();
        for (var i = 0; i < testCases.Count; i++)
        {
            var tc = (Dictionary<object, object>)testCases[i];
            sbTests.AppendLine($"{i + 1}. {tc["name"]}");
            sbTests.AppendLine($"   input:    {tc["input"]}");
            sbTests.AppendLine($"   expected: {tc["expected"]}");
        }

        // ── Format failed tests ───────────────────────────────────────────
        var sbFailed = new StringBuilder();
        for (var i = 0; i < failedTests.Count; i++)
        {
            var r  = failedTests[i];
            var tc = (Dictionary<object, object>)r["test_case"]!;
            sbFailed.AppendLine($"{i + 1}. {tc["name"]}:");
            sbFailed.AppendLine(r["error"] is not null
                ? $"   error:    {r["error"]}"
                : $"   actual:   {r["actual"]}");
            sbFailed.AppendLine($"   expected: {r["expected"]}");
        }

        var prompt = $$"""
Problem: {{inputs["problem"]}}

Current test cases:
{{sbTests}}
Current C# function:
```csharp
{{inputs["function_code"]}}
```

Failed tests:
{{sbFailed}}
Analyse the failures and output revisions in YAML.
You may revise test cases (if the expected output was wrong), the function code (if the logic is wrong), or both.

IMPORTANT:
- test_cases is a dictionary mapping 1-based integer keys to revised test case entries.
- function_code must be a method named "RunCode" declared as public static object RunCode(...).
- Do NOT include using statements, a namespace, or a class declaration.

```yaml
reasoning: |
    Looking at the failures I see that...
    I will revise...
test_cases:
  1:
    name: "Revised test name"
    input: {...}
    expected: ...
function_code: |
  public static object RunCode(...)
  {
      return ...;
  }
```
""";

        var response = Utils.CallLlm(prompt);
        var result   = YamlHelper.ParseBlock(response);

        // ── Validate test case revisions ──────────────────────────────────
        if (result.ContainsKey("test_cases"))
        {
            var revisions = (Dictionary<object, object>)result["test_cases"];
            foreach (var kvp in revisions)
            {
                var tc = (Dictionary<object, object>)kvp.Value;
                if (!tc.ContainsKey("name"))
                    throw new InvalidOperationException($"Revision {kvp.Key} missing 'name'");
                if (!tc.ContainsKey("input"))
                    throw new InvalidOperationException($"Revision {kvp.Key} missing 'input'");
                if (!tc.ContainsKey("expected"))
                    throw new InvalidOperationException($"Revision {kvp.Key} missing 'expected'");
            }
        }

        // ── Validate function code ────────────────────────────────────────
        if (result.ContainsKey("function_code"))
        {
            var code = result["function_code"]?.ToString() ?? string.Empty;
            if (!code.Contains("RunCode"))
                throw new InvalidOperationException(
                    "Revised function does not contain 'RunCode'");
        }

        return result;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store     = (Dictionary<string, object>)shared;
        var revision  = (Dictionary<object, object>)execRes!;
        var iteration = (int)store["iteration_count"];

        Console.WriteLine($"\n=== Revisions (Iteration {iteration}) ===");

        // ── Apply test-case revisions ─────────────────────────────────────
        if (revision.ContainsKey("test_cases"))
        {
            var revisions    = (Dictionary<object, object>)revision["test_cases"];
            var currentTests = ((List<object>)store["test_cases"]).ToList();

            Console.WriteLine("Revising test cases:");
            foreach (var kvp in revisions)
            {
                var index   = Convert.ToInt32(kvp.Key) - 1; // 1-based → 0-based
                var revised = (Dictionary<object, object>)kvp.Value;

                if (index < 0 || index >= currentTests.Count) continue;

                var old = (Dictionary<object, object>)currentTests[index];
                Console.WriteLine($"  Test {kvp.Key}: '{old["name"]}' → '{revised["name"]}'");
                Console.WriteLine($"    old input:    {old["input"]}  →  new input:    {revised["input"]}");
                Console.WriteLine($"    old expected: {old["expected"]}  →  new expected: {revised["expected"]}");
                currentTests[index] = revised;
            }

            store["test_cases"] = currentTests;
        }

        // ── Apply function-code revision ──────────────────────────────────
        if (revision.ContainsKey("function_code"))
        {
            var newCode = revision["function_code"]!.ToString()!;
            Console.WriteLine("Revising function code:");
            Console.WriteLine(newCode);
            store["function_code"] = newCode;
        }

        return "default";
    }
}

