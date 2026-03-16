using PocketFlow;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Workflow;

// ── GenerateOutline ──────────────────────────────────────────────────────────

/// <summary>
/// Creates a simple outline with up to 3 main sections using YAML structured output.
/// Mirrors GenerateOutline in nodes.py.
/// </summary>
public class GenerateOutline : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (string)store["topic"];
    }

    protected override object? Execute(object? prepRes)
    {
        var topic = (string)prepRes!;
        var prompt = $"""
Create a simple outline for an article about {topic}.
Include at most 3 main sections (no subsections).

Output the sections in YAML format as shown below:

```yaml
sections:
    - First section
    - Second section
    - Third section
```
""";
        var response = Utils.CallLlm(prompt);

        // Extract the YAML block from the LLM response
        var yamlStr = response.Split("```yaml")[1].Split("```")[0].Trim();

        var deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .Build();

        return deserializer.Deserialize<OutlineData>(yamlStr);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        var data = (OutlineData)execRes!;

        var sections = data.Sections.Select(s => s.Trim()).ToList();
        store["sections"] = sections;

        var formattedOutline = string.Join("\n", sections.Select((s, i) => $"{i + 1}. {s}"));
        store["outline"] = formattedOutline;

        Console.WriteLine("\n===== OUTLINE (YAML) =====\n");
        Console.WriteLine("sections:");
        foreach (var s in sections)
            Console.WriteLine($"- {s}");

        Console.WriteLine("\n===== PARSED OUTLINE =====\n");
        Console.WriteLine(formattedOutline);
        Console.WriteLine("\n=========================\n");

        return "default";
    }
}

// ── WriteSimpleContent ───────────────────────────────────────────────────────

/// <summary>
/// Writes a concise (100 words max) explanation for each section.
/// Mirrors WriteSimpleContent (BatchNode) in nodes.py.
/// </summary>
public class WriteSimpleContent : BatchNode
{
    private List<string> _sections = new();

    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        _sections = store.TryGetValue("sections", out var s)
            ? (List<string>)s
            : new List<string>();
        return _sections;
    }

    protected override object? Execute(object? prepRes)
    {
        var section = (string)prepRes!;
        var prompt = $"""
Write a short paragraph (MAXIMUM 100 WORDS) about this section:

{section}

Requirements:
- Explain the idea in simple, easy-to-understand terms
- Use everyday language, avoiding jargon
- Keep it very concise (no more than 100 words)
- Include one brief example or analogy
""";
        var content = Utils.CallLlm(prompt);

        var idx = _sections.IndexOf(section);
        Console.WriteLine($"✓ Completed section {idx + 1}/{_sections.Count}: {section}");

        return (section, content);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        var results = (List<object?>)execRes!;

        var sectionContents = new Dictionary<string, string>();
        var allSectionsContent = new List<string>();

        foreach (var item in results)
        {
            var (section, content) = ((string, string))item!;
            sectionContents[section] = content;
            allSectionsContent.Add($"## {section}\n\n{content}\n");
        }

        var draft = string.Join("\n", allSectionsContent);
        store["section_contents"] = sectionContents;
        store["draft"] = draft;

        Console.WriteLine("\n===== SECTION CONTENTS =====\n");
        foreach (var (section, content) in sectionContents)
        {
            Console.WriteLine($"--- {section} ---");
            Console.WriteLine(content);
            Console.WriteLine();
        }
        Console.WriteLine("===========================\n");

        return "default";
    }
}

// ── ApplyStyle ───────────────────────────────────────────────────────────────

/// <summary>
/// Rewrites the combined draft in a conversational, engaging style.
/// Mirrors ApplyStyle in nodes.py.
/// </summary>
public class ApplyStyle : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (string)store["draft"];
    }

    protected override object? Execute(object? prepRes)
    {
        var draft = (string)prepRes!;
        var prompt = $"""
Rewrite the following draft in a conversational, engaging style:

{draft}

Make it:
- Conversational and warm in tone
- Include rhetorical questions that engage the reader
- Add analogies and metaphors where appropriate
- Include a strong opening and conclusion
""";
        return Utils.CallLlm(prompt);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        store["final_article"] = (string)execRes!;

        Console.WriteLine("\n===== FINAL ARTICLE =====\n");
        Console.WriteLine(execRes);
        Console.WriteLine("\n========================\n");

        return "default";
    }
}

// ── YAML model ───────────────────────────────────────────────────────────────

internal sealed class OutlineData
{
    public List<string> Sections { get; set; } = new();
}

