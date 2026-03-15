using System.Reflection;
using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using OllamaSharp.Models.Chat;

namespace CodeGenerator;

public static class Utils
{
    // ── LLM ─────────────────────────────────────────────────────────────────
    // Mirrors utils/call_llm.py but targets Ollama via OllamaConnector
    // instead of Anthropic Claude.

    public static string CallLlm(string prompt)
    {
        var messages = new List<Message>
        {
            new() { Role = ChatRole.User, Content = prompt }
        };
        return OllamaConnector.CallLlm(messages);
    }

    // ── In-Process C# Code Execution ─────────────────────────────────────────
    // Mirrors utils/code_executor.py – but instead of Python exec(), we compile
    // the LLM-generated C# method with Roslyn, load the assembly, and invoke
    // RunCode via reflection, converting input dict values to the declared
    // parameter types through a JSON round-trip.

    /// <summary>
    /// Returns all currently-loaded, non-dynamic assemblies as Roslyn metadata
    /// references so the compiled snippet can use the full standard library.
    /// </summary>
    private static MetadataReference[] GetReferences() =>
        AppDomain.CurrentDomain.GetAssemblies()
            .Where(a => !a.IsDynamic && !string.IsNullOrWhiteSpace(a.Location))
            .Select(a => MetadataReference.CreateFromFile(a.Location))
            .ToArray();

    /// <summary>
    /// Separates any leading <c>using</c> directives from the method body so
    /// they can be placed at file scope outside the generated wrapper class.
    /// </summary>
    private static (string usings, string body) SplitUsings(string code)
    {
        var usingLines = new List<string>();
        var bodyLines  = new List<string>();

        foreach (var line in code.Split('\n'))
        {
            var t = line.TrimStart();
            // Match file-scoped using directives; exclude "using var/(...)"
            if (t.StartsWith("using ") && t.Contains(';') && !t.Contains('('))
                usingLines.Add(line);
            else
                bodyLines.Add(line);
        }

        return (string.Join("\n", usingLines), string.Join("\n", bodyLines));
    }

    /// <summary>
    /// Compiles <paramref name="functionCode"/> (expected to contain a
    /// <c>public static object RunCode(...)</c> method) with Roslyn, loads
    /// the resulting assembly in-process, and invokes <c>RunCode</c> by
    /// matching the <paramref name="input"/> dictionary keys to parameter
    /// names.  Type conversion is handled via a JSON round-trip so that
    /// YAML-deserialized <c>List&lt;object&gt;</c> sequences are correctly
    /// widened to <c>int[]</c>, <c>List&lt;int&gt;</c>, etc.
    /// </summary>
    public static (object? result, string? error) ExecuteCode(
        string functionCode, Dictionary<object, object> input)
    {
        // Pre-load assemblies that may not yet be referenced at call time
        _ = typeof(Enumerable).Assembly;          // System.Linq
        _ = typeof(System.Collections.Generic.List<int>).Assembly; // System.Collections

        var (extraUsings, methodBody) = SplitUsings(functionCode);

        var fullCode = $$"""
using System;
using System.Collections.Generic;
using System.Linq;
{{extraUsings}}
public static class GeneratedSolution
{
{{methodBody}}
}
""";

        var syntaxTree  = CSharpSyntaxTree.ParseText(fullCode);
        var compilation = CSharpCompilation.Create(
            "DynCodeGen_" + Guid.NewGuid().ToString("N"),
            [syntaxTree],
            GetReferences(),
            new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                nullableContextOptions: NullableContextOptions.Disable));

        using var ms         = new MemoryStream();
        var       emitResult = compilation.Emit(ms);

        if (!emitResult.Success)
        {
            var errors = string.Join("\n", emitResult.Diagnostics
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => d.GetMessage()));
            return (null, $"Compilation error:\n{errors}");
        }

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());
        var type     = assembly.GetType("GeneratedSolution");
        if (type is null)
            return (null, "GeneratedSolution class not found in compiled assembly");

        var method = type.GetMethod("RunCode", BindingFlags.Public | BindingFlags.Static);
        if (method is null)
            return (null, "RunCode method not found — ensure the LLM generated 'public static object RunCode(...)'");

        // ── Build argument list by matching parameter names to input keys ─────
        var parameters = method.GetParameters();
        var args       = new object?[parameters.Length];
        var jsonOpts   = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        for (var i = 0; i < parameters.Length; i++)
        {
            var p = parameters[i];
            var key = input.Keys.FirstOrDefault(k =>
                string.Equals(k?.ToString(), p.Name, StringComparison.OrdinalIgnoreCase));

            if (key is null)
                return (null, $"Missing parameter '{p.Name}' in test input");

            var raw = input[key];
            try
            {
                // JSON round-trip: List<object>{2,7,11,15} → "[2,7,11,15]" → int[]
                var json      = JsonSerializer.Serialize(raw, raw?.GetType() ?? typeof(object));
                args[i] = JsonSerializer.Deserialize(json, p.ParameterType, jsonOpts);
            }
            catch (Exception ex)
            {
                return (null, $"Type conversion failed for '{p.Name}': {ex.Message}");
            }
        }

        try
        {
            return (method.Invoke(null, args), null);
        }
        catch (TargetInvocationException tie)
        {
            var inner = tie.InnerException ?? tie;
            return (null, $"{inner.GetType().Name}: {inner.Message}");
        }
    }

    // ── Value Comparison ─────────────────────────────────────────────────────

    /// <summary>
    /// Compares <paramref name="a"/> and <paramref name="b"/> for equality
    /// via JSON normalisation so that <c>int[]{0,1}</c>, <c>List&lt;int&gt;{0,1}</c>
    /// and YAML-deserialized <c>List&lt;object&gt;{0,1}</c> all compare equal.
    /// </summary>
    public static bool ValuesEqual(object? a, object? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;

        var ja = JsonSerializer.Serialize(a, a.GetType());
        var jb = JsonSerializer.Serialize(b, b.GetType());
        return ja == jb;
    }
}

