using System.Text;
using System.Text.RegularExpressions;
using CodebaseKnowledgeBuilder.Utils;
using PocketFlow;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace CodebaseKnowledgeBuilder;

// ── Shared-store keys used across nodes ──────────────────────────────────────
//   "repo_url"          string?
//   "local_dir"         string?
//   "project_name"      string
//   "github_token"      string?
//   "output_dir"        string
//   "include_patterns"  List<string>
//   "exclude_patterns"  List<string>
//   "max_file_size"     long
//   "language"          string
//   "use_cache"         bool
//   "max_abstraction_num" int
//   "files"             List<(string path, string content)>
//   "abstractions"      List<Dict>  [{name, description, files:[int]}]
//   "relationships"     Dict        {summary, details:[{from,to,label}]}
//   "chapter_order"     List<int>
//   "chapters"          List<string>
//   "final_output_dir"  string

// ── Helpers ──────────────────────────────────────────────────────────────────

file static class YamlHelper
{
    private static readonly IDeserializer Deserializer =
        new DeserializerBuilder()
            .WithNamingConvention(UnderscoredNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();

    /// <summary>Extracts the first ```yaml … ``` block from a response and parses it.</summary>
    public static T ParseYamlBlock<T>(string response)
    {
        var match = Regex.Match(response, @"```yaml(.*?)```",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        var yaml = match.Success ? match.Groups[1].Value.Trim() : response.Trim();
        return Deserializer.Deserialize<T>(yaml);
    }
}

file static class SharedStore
{
    public static T Get<T>(Dictionary<string, object> store, string key, T defaultValue)
        => store.TryGetValue(key, out var v) && v is T t ? t : defaultValue;
}

// ── Helper to build content map from file indices ─────────────────────────────

file static class FileHelper
{
    public static Dictionary<string, string> GetContentForIndices(
        List<(string path, string content)> files, IEnumerable<int> indices)
    {
        var map = new Dictionary<string, string>();
        foreach (var i in indices)
            if (i >= 0 && i < files.Count)
            {
                var (path, content) = files[i];
                map[$"{i} # {path}"] = content;
            }
        return map;
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Node 1 – FetchRepo
// ══════════════════════════════════════════════════════════════════════════════

public class FetchRepo : Node
{
    protected override object? Prep(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        var repoUrl    = SharedStore.Get<string?>(store, "repo_url", null);
        var localDir   = SharedStore.Get<string?>(store, "local_dir", null);
        var projectName = SharedStore.Get<string?>(store, "project_name", null);

        if (string.IsNullOrEmpty(projectName))
        {
            projectName = !string.IsNullOrEmpty(repoUrl)
                ? repoUrl.Split('/').Last().Replace(".git", "")
                : Path.GetFullPath(localDir!).Split(Path.DirectorySeparatorChar).Last();
            store["project_name"] = projectName;
        }

        return new Dictionary<string, object?>
        {
            ["repo_url"]          = repoUrl,
            ["local_dir"]         = localDir,
            ["token"]             = SharedStore.Get<string?>(store, "github_token", null),
            ["include_patterns"]  = SharedStore.Get(store, "include_patterns", new List<string>()),
            ["exclude_patterns"]  = SharedStore.Get(store, "exclude_patterns", new List<string>()),
            ["max_file_size"]     = SharedStore.Get<long>(store, "max_file_size", 100_000),
        };
    }

    protected override object? Exec(object? prepRes)
    {
        var p = (Dictionary<string, object?>)prepRes!;
        var repoUrl   = p["repo_url"] as string;
        var localDir  = p["local_dir"] as string;
        var token     = p["token"] as string;
        var include   = (List<string>)p["include_patterns"]!;
        var exclude   = (List<string>)p["exclude_patterns"]!;
        var maxSize   = (long)p["max_file_size"]!;

        Dictionary<string, string> raw;
        if (!string.IsNullOrEmpty(repoUrl))
        {
            Console.WriteLine($"Crawling repository: {repoUrl}...");
            raw = CrawlGithubFiles.Crawl(repoUrl, token, maxSize, true, include, exclude);
        }
        else
        {
            Console.WriteLine($"Crawling directory: {localDir}...");
            raw = CrawlLocalFiles.Crawl(localDir!, include, exclude, maxSize, true);
        }

        if (raw.Count == 0)
            throw new InvalidOperationException("Failed to fetch files – result was empty.");

        var files = raw.Select(kv => (kv.Key, kv.Value)).ToList();
        Console.WriteLine($"Fetched {files.Count} files.");
        return files;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        store["files"] = execRes!;
        return null;
    }
}

// ══════════════════════════════════════════════════════════════════════════════
// Node 2 – IdentifyAbstractions
// ══════════════════════════════════════════════════════════════════════════════

public class IdentifyAbstractions : Node
{
    public IdentifyAbstractions(int maxRetries = 1, int wait = 0) : base(maxRetries, wait) { }

    protected override object? Prep(object shared)
    {
        var store        = (Dictionary<string, object>)shared;
        var files        = (List<(string path, string content)>)store["files"];
        var projectName  = (string)store["project_name"];
        var language     = SharedStore.Get(store, "language", "english");
        var useCache     = SharedStore.Get(store, "use_cache", true);
        var maxAbstrNum  = SharedStore.Get(store, "max_abstraction_num", 10);

        var context = new StringBuilder();
        var fileInfo = new List<(int idx, string path)>();
        for (int i = 0; i < files.Count; i++)
        {
            var (path, content) = files[i];
            context.AppendLine($"--- File Index {i}: {path} ---");
            context.AppendLine(content);
            context.AppendLine();
            fileInfo.Add((i, path));
        }

        var listing = string.Join("\n", fileInfo.Select(f => $"- {f.idx} # {f.path}"));

        return (context.ToString(), listing, files.Count, projectName, language, useCache, maxAbstrNum);
    }

    protected override object? Exec(object? prepRes)
    {
        var (context, listing, fileCount, projectName, language, useCache, maxAbstrNum) =
            ((string, string, int, string, string, bool, int))prepRes!;

        Console.WriteLine("Identifying abstractions using LLM...");

        var langInstr = "";
        var nameLangHint = "";
        var descLangHint = "";
        if (!language.Equals("english", StringComparison.OrdinalIgnoreCase))
        {
            var lc = Capitalize(language);
            langInstr     = $"IMPORTANT: Generate the `name` and `description` for each abstraction in **{lc}** language. Do NOT use English for these fields.\n\n";
            nameLangHint  = $" (value in {lc})";
            descLangHint  = $" (value in {lc})";
        }

        var prompt = $"""
For the project `{projectName}`:

Codebase Context:
{context}

{langInstr}Analyze the codebase context.
Identify the top 5-{maxAbstrNum} core most important abstractions to help those new to the codebase.

For each abstraction, provide:
1. A concise `name`{nameLangHint}.
2. A beginner-friendly `description` explaining what it is with a simple analogy, in around 100 words{descLangHint}.
3. A list of relevant `file_indices` (integers) using the format `idx # path/comment`.

List of file indices and paths present in the context:
{listing}

Format the output as a YAML list of dictionaries:

```yaml
- name: |
    Query Processing{nameLangHint}
  description: |
    Explains what the abstraction does.
    It's like a central dispatcher routing requests.{descLangHint}
  file_indices:
    - 0 # path/to/file1.py
    - 3 # path/to/related.py
# ... up to {maxAbstrNum} abstractions
```
""";

        var response = CallLlm.Call(prompt, useCache && CurRetry == 0);

        // Validate YAML
        var rawList = YamlHelper.ParseYamlBlock<List<Dictionary<object, object>>>(response);
        if (rawList == null) throw new InvalidOperationException("LLM output is not a list");

        var result = new List<Dictionary<string, object>>();
        foreach (var item in rawList)
        {
            var name  = item["name"]?.ToString()?.Trim()
                        ?? throw new InvalidOperationException($"Missing name in {item}");
            var desc  = item["description"]?.ToString()?.Trim()
                        ?? throw new InvalidOperationException($"Missing description in {item}");
            var idxRaw = item.TryGetValue("file_indices", out var fi) ? fi : null;

            if (idxRaw is not System.Collections.IEnumerable idxList)
                throw new InvalidOperationException($"file_indices is not a list in {name}");

            var indices = new List<int>();
            foreach (var entry in idxList)
            {
                var idx = ParseIndex(entry?.ToString() ?? "", fileCount, name);
                indices.Add(idx);
            }

            result.Add(new Dictionary<string, object>
            {
                ["name"]        = name,
                ["description"] = desc,
                ["files"]       = indices.Distinct().OrderBy(x => x).ToList(),
            });
        }

        Console.WriteLine($"Identified {result.Count} abstractions.");
        return result;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        ((Dictionary<string, object>)shared)["abstractions"] = execRes!;
        return null;
    }

    private static int ParseIndex(string entry, int count, string name)
    {
        var s = entry.Contains('#') ? entry.Split('#')[0].Trim() : entry.Trim();
        if (!int.TryParse(s, out int idx) || idx < 0 || idx >= count)
            throw new InvalidOperationException($"Invalid file index '{entry}' in '{name}'");
        return idx;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}

// ══════════════════════════════════════════════════════════════════════════════
// Node 3 – AnalyzeRelationships
// ══════════════════════════════════════════════════════════════════════════════

public class AnalyzeRelationships : Node
{
    public AnalyzeRelationships(int maxRetries = 1, int wait = 0) : base(maxRetries, wait) { }

    protected override object? Prep(object shared)
    {
        var store       = (Dictionary<string, object>)shared;
        var abstractions = (List<Dictionary<string, object>>)store["abstractions"];
        var files        = (List<(string path, string content)>)store["files"];
        var projectName  = (string)store["project_name"];
        var language     = SharedStore.Get(store, "language", "english");
        var useCache     = SharedStore.Get(store, "use_cache", true);

        var allIndices   = new HashSet<int>();
        var abstrLines   = new List<string>();
        var ctx          = new StringBuilder("Identified Abstractions:\n");

        for (int i = 0; i < abstractions.Count; i++)
        {
            var a       = abstractions[i];
            var name    = a["name"].ToString()!;
            var desc    = a["description"].ToString()!;
            var fileIds = (List<int>)a["files"];
            ctx.AppendLine($"- Index {i}: {name} (Relevant file indices: [{string.Join(", ", fileIds)}])");
            ctx.AppendLine($"  Description: {desc}");
            abstrLines.Add($"{i} # {name}");
            foreach (var f in fileIds) allIndices.Add(f);
        }

        ctx.AppendLine("\nRelevant File Snippets (Referenced by Index and Path):");
        var contentMap = FileHelper.GetContentForIndices(files, allIndices.OrderBy(x => x));
        foreach (var kv in contentMap)
            ctx.AppendLine($"--- File: {kv.Key} ---\n{kv.Value}");

        return (ctx.ToString(), string.Join("\n", abstrLines),
                abstractions.Count, projectName, language, useCache);
    }

    protected override object? Exec(object? prepRes)
    {
        var (context, listing, numAbstr, projectName, language, useCache) =
            ((string, string, int, string, string, bool))prepRes!;

        Console.WriteLine("Analyzing relationships using LLM...");

        var langInstr = "";
        var langHint  = "";
        var listNote  = "";
        if (!language.Equals("english", StringComparison.OrdinalIgnoreCase))
        {
            var lc     = Capitalize(language);
            langInstr  = $"IMPORTANT: Generate the `summary` and relationship `label` fields in **{lc}** language. Do NOT use English for these fields.\n\n";
            langHint   = $" (in {lc})";
            listNote   = $" (Names might be in {lc})";
        }

        var prompt = $"""
Based on the following abstractions and relevant code snippets from the project `{projectName}`:

List of Abstraction Indices and Names{listNote}:
{listing}

Context (Abstractions, Descriptions, Code):
{context}

{langInstr}Please provide:
1. A high-level `summary` of the project's main purpose and functionality in a few beginner-friendly sentences{langHint}. Use markdown formatting with **bold** and *italic* text to highlight important concepts.
2. A list (`relationships`) describing the key interactions between these abstractions. For each relationship, specify:
    - `from_abstraction`: Index of the source abstraction (e.g., `0 # AbstractionName1`)
    - `to_abstraction`: Index of the target abstraction (e.g., `1 # AbstractionName2`)
    - `label`: A brief label for the interaction **in just a few words**{langHint} (e.g., "Manages", "Inherits", "Uses").
    Simplify the relationship and exclude those non-important ones.

IMPORTANT: Make sure EVERY abstraction is involved in at least ONE relationship (either as source or target).

Format the output as YAML:

```yaml
summary: |
  A brief, simple explanation of the project{langHint}.
relationships:
  - from_abstraction: 0 # AbstractionName1
    to_abstraction: 1 # AbstractionName2
    label: "Manages"{langHint}
```

Now, provide the YAML output:
""";

        var response = CallLlm.Call(prompt, useCache && CurRetry == 0);
        var parsed   = YamlHelper.ParseYamlBlock<Dictionary<object, object>>(response);

        var summary = parsed["summary"]?.ToString()?.Trim()
                      ?? throw new InvalidOperationException("Missing 'summary' in LLM output");

        if (parsed["relationships"] is not System.Collections.IEnumerable relList)
            throw new InvalidOperationException("'relationships' is not a list");

        var details = new List<Dictionary<string, object>>();
        foreach (var rel in relList.Cast<Dictionary<object, object>>())
        {
            var fromIdx = ParseRelIndex(rel["from_abstraction"]?.ToString() ?? "", numAbstr);
            var toIdx   = ParseRelIndex(rel["to_abstraction"]?.ToString() ?? "", numAbstr);
            var label   = rel["label"]?.ToString()?.Trim()
                          ?? throw new InvalidOperationException("Missing 'label' in relationship");
            details.Add(new() { ["from"] = fromIdx, ["to"] = toIdx, ["label"] = label });
        }

        Console.WriteLine("Generated project summary and relationship details.");
        return new Dictionary<string, object> { ["summary"] = summary, ["details"] = details };
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        ((Dictionary<string, object>)shared)["relationships"] = execRes!;
        return null;
    }

    private static int ParseRelIndex(string entry, int count)
    {
        var s = entry.Contains('#') ? entry.Split('#')[0].Trim() : entry.Trim();
        if (!int.TryParse(s, out int idx) || idx < 0 || idx >= count)
            throw new InvalidOperationException($"Invalid relationship index '{entry}'");
        return idx;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}

// ══════════════════════════════════════════════════════════════════════════════
// Node 4 – OrderChapters
// ══════════════════════════════════════════════════════════════════════════════

public class OrderChapters : Node
{
    public OrderChapters(int maxRetries = 1, int wait = 0) : base(maxRetries, wait) { }

    protected override object? Prep(object shared)
    {
        var store        = (Dictionary<string, object>)shared;
        var abstractions = (List<Dictionary<string, object>>)store["abstractions"];
        var relationships = (Dictionary<string, object>)store["relationships"];
        var projectName  = (string)store["project_name"];
        var language     = SharedStore.Get(store, "language", "english");
        var useCache     = SharedStore.Get(store, "use_cache", true);

        var listing  = string.Join("\n", abstractions.Select((a, i) => $"- {i} # {a["name"]}"));
        var summary  = relationships["summary"].ToString();
        var details  = (List<Dictionary<string, object>>)relationships["details"];

        var ctx = new StringBuilder($"Project Summary:\n{summary}\n\nRelationships:\n");
        foreach (var rel in details)
        {
            var from  = abstractions[(int)rel["from"]]["name"];
            var to    = abstractions[(int)rel["to"]]["name"];
            var label = rel["label"];
            ctx.AppendLine($"- From {rel["from"]} ({from}) to {rel["to"]} ({to}): {label}");
        }

        var listNote = "";
        if (!language.Equals("english", StringComparison.OrdinalIgnoreCase))
            listNote = $" (Names might be in {Capitalize(language)})";

        return (listing, ctx.ToString(), abstractions.Count, projectName, listNote, useCache);
    }

    protected override object? Exec(object? prepRes)
    {
        var (listing, context, numAbstr, projectName, listNote, useCache) =
            ((string, string, int, string, string, bool))prepRes!;

        Console.WriteLine("Determining chapter order using LLM...");

        var prompt = $"""
Given the following project abstractions and their relationships for the project `{projectName}`:

Abstractions (Index # Name){listNote}:
{listing}

Context about relationships and project summary:
{context}

What is the best order to explain these abstractions, from first to last?
Explain foundational/user-facing concepts first, then lower-level implementation details.

Output the ordered list of abstraction indices, including the name in a comment.

```yaml
- 2 # FoundationalConcept
- 0 # CoreClassA
- 1 # CoreClassB
```

Now, provide the YAML output:
""";

        var response = CallLlm.Call(prompt, useCache && CurRetry == 0);
        var raw      = YamlHelper.ParseYamlBlock<List<object>>(response);
        if (raw == null) throw new InvalidOperationException("LLM output is not a list");

        var ordered  = new List<int>();
        var seen     = new HashSet<int>();
        foreach (var entry in raw)
        {
            var s   = entry.ToString()!;
            var idx = s.Contains('#') ? int.Parse(s.Split('#')[0].Trim()) : int.Parse(s.Trim());
            if (idx < 0 || idx >= numAbstr) throw new InvalidOperationException($"Invalid index {idx}");
            if (seen.Contains(idx)) throw new InvalidOperationException($"Duplicate index {idx}");
            ordered.Add(idx);
            seen.Add(idx);
        }

        if (ordered.Count != numAbstr)
            throw new InvalidOperationException($"Ordered list has {ordered.Count} items, expected {numAbstr}");

        Console.WriteLine($"Determined chapter order: [{string.Join(", ", ordered)}]");
        return ordered;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        ((Dictionary<string, object>)shared)["chapter_order"] = execRes!;
        return null;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}

// ══════════════════════════════════════════════════════════════════════════════
// Node 5 – WriteChapters  (BatchNode)
// ══════════════════════════════════════════════════════════════════════════════

public class WriteChapters : BatchNode
{
    public WriteChapters(int maxRetries = 1, int wait = 0) : base(maxRetries, wait) { }

    // Progressive context across batch items (cleared in Post)
    private List<string> _chaptersWrittenSoFar = new();

    protected override object? Prep(object shared)
    {
        var store        = (Dictionary<string, object>)shared;
        var chapterOrder = (List<int>)store["chapter_order"];
        var abstractions = (List<Dictionary<string, object>>)store["abstractions"];
        var files        = (List<(string path, string content)>)store["files"];
        var projectName  = (string)store["project_name"];
        var language     = SharedStore.Get(store, "language", "english");
        var useCache     = SharedStore.Get(store, "use_cache", true);

        _chaptersWrittenSoFar = new();

        // Build chapter filename lookup
        var chapterFilenames = new Dictionary<int, Dictionary<string, object>>();
        var allChapters      = new List<string>();

        for (int i = 0; i < chapterOrder.Count; i++)
        {
            int abstrIdx = chapterOrder[i];
            if (abstrIdx < 0 || abstrIdx >= abstractions.Count) continue;
            var name     = abstractions[abstrIdx]["name"].ToString()!;
            var safeName = Regex.Replace(name, @"[^\w]", "_").ToLowerInvariant();
            var filename = $"{i + 1:D2}_{safeName}.md";
            chapterFilenames[abstrIdx] = new()
            {
                ["num"]      = i + 1,
                ["name"]     = name,
                ["filename"] = filename,
            };
            allChapters.Add($"{i + 1}. [{name}]({filename})");
        }

        var fullListing = string.Join("\n", allChapters);
        var items = new List<Dictionary<string, object>>();

        for (int i = 0; i < chapterOrder.Count; i++)
        {
            int abstrIdx = chapterOrder[i];
            if (abstrIdx < 0 || abstrIdx >= abstractions.Count) continue;
            var abstr      = abstractions[abstrIdx];
            var fileIds    = (List<int>)abstr["files"];
            var contentMap = FileHelper.GetContentForIndices(files, fileIds);

            var prev = i > 0 ? chapterFilenames[chapterOrder[i - 1]] : null;
            var next = i < chapterOrder.Count - 1 ? chapterFilenames[chapterOrder[i + 1]] : null;

            items.Add(new()
            {
                ["chapter_num"]               = i + 1,
                ["abstraction_index"]         = abstrIdx,
                ["abstraction_details"]       = abstr,
                ["related_files_content_map"] = contentMap,
                ["project_name"]              = projectName,
                ["full_chapter_listing"]      = fullListing,
                ["chapter_filenames"]         = chapterFilenames,
                ["prev_chapter"]              = prev!,
                ["next_chapter"]              = next!,
                ["language"]                  = language,
                ["use_cache"]                 = useCache,
            });
        }

        Console.WriteLine($"Preparing to write {items.Count} chapters...");
        return items;
    }

    protected override object? Exec(object? prepRes)
    {
        var item        = (Dictionary<string, object>)prepRes!;
        var abstr       = (Dictionary<string, object>)item["abstraction_details"];
        var name        = abstr["name"].ToString()!;
        var description = abstr["description"].ToString()!;
        int chapterNum  = (int)item["chapter_num"];
        var projectName = item["project_name"].ToString()!;
        var language    = item["language"].ToString()!;
        bool useCache   = (bool)item["use_cache"];
        var contentMap  = (Dictionary<string, string>)item["related_files_content_map"];
        var fullListing = item["full_chapter_listing"].ToString()!;
        var prev        = item["prev_chapter"] as Dictionary<string, object>;
        var next        = item["next_chapter"] as Dictionary<string, object>;

        Console.WriteLine($"Writing chapter {chapterNum} for: {name} using LLM...");

        var fileCtx = string.Join("\n\n", contentMap.Select(kv =>
        {
            var fname = kv.Key.Contains("# ") ? kv.Key.Split("# ")[1] : kv.Key;
            return $"--- File: {fname} ---\n{kv.Value}";
        }));

        var prevSummary = string.Join("\n---\n", _chaptersWrittenSoFar);

        var langInstr = "";
        var instrHint = "";
        var mermaidHint = "";
        var codeHint = "";
        var linkHint = "";
        var toneHint = "";
        if (!language.Equals("english", StringComparison.OrdinalIgnoreCase))
        {
            var lc        = Capitalize(language);
            langInstr     = $"IMPORTANT: Write this ENTIRE tutorial chapter in **{lc}**. Translate ALL generated content including explanations, examples, technical terms into {lc}. DO NOT use English except in code syntax or required proper nouns. The entire output MUST be in {lc}.\n\n";
            instrHint     = $" (in {lc})";
            mermaidHint   = $" (Use {lc} for labels/text if appropriate)";
            codeHint      = $" (Translate to {lc} if possible)";
            linkHint      = $" (Use the {lc} chapter title from the structure above)";
            toneHint      = $" (appropriate for {lc} readers)";
        }

        var prompt = $"""
{langInstr}Write a very beginner-friendly tutorial chapter (in Markdown format) for the project `{projectName}` about the concept: "{name}". This is Chapter {chapterNum}.

Concept Details:
- Name: {name}
- Description:
{description}

Complete Tutorial Structure:
{fullListing}

Context from previous chapters:
{(string.IsNullOrEmpty(prevSummary) ? "This is the first chapter." : prevSummary)}

Relevant Code Snippets:
{(string.IsNullOrEmpty(fileCtx) ? "No specific code snippets provided for this abstraction." : fileCtx)}

Instructions for the chapter:
- Start with a clear heading (e.g., `# Chapter {chapterNum}: {name}`).
- If not the first chapter, begin with a brief transition from the previous chapter{instrHint}.
- Begin with a high-level motivation explaining what problem this abstraction solves{instrHint}.
- If complex, break it down into key concepts{instrHint}.
- Explain how to use this abstraction with example inputs and outputs{instrHint}.
- Each code block should be BELOW 10 lines! Break larger blocks into smaller pieces{instrHint}.
- Describe the internal implementation{instrHint}. Use a sequenceDiagram with at most 5 participants{mermaidHint}.
- Use mermaid diagrams to illustrate complex concepts{mermaidHint}.
- When referring to other abstractions, ALWAYS use proper Markdown links{linkHint}.
- Heavily use analogies and examples throughout{instrHint}.
- End with a brief conclusion and transition to the next chapter{instrHint}.
- Ensure the tone is welcoming and easy for a newcomer{toneHint}.
- Output *only* the Markdown content for this chapter.

Now, directly provide a super beginner-friendly Markdown output (DON'T need ```markdown``` tags):
""";

        var content = CallLlm.Call(prompt, useCache && CurRetry == 0);

        // Ensure correct heading
        var expectedHeading = $"# Chapter {chapterNum}: {name}";
        if (!content.TrimStart().StartsWith($"# Chapter {chapterNum}"))
        {
            var lines = content.TrimStart().Split('\n').ToList();
            if (lines.Count > 0 && lines[0].TrimStart().StartsWith('#'))
                lines[0] = expectedHeading;
            else
                lines.Insert(0, expectedHeading + "\n");
            content = string.Join('\n', lines);
        }

        _chaptersWrittenSoFar.Add(content);
        return content;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store    = (Dictionary<string, object>)shared;
        var chapters = (List<object?>)execRes!;
        store["chapters"] = chapters.Select(c => c?.ToString() ?? "").ToList();
        _chaptersWrittenSoFar = new();
        Console.WriteLine($"Finished writing {chapters.Count} chapters.");
        return null;
    }

    private static string Capitalize(string s) =>
        string.IsNullOrEmpty(s) ? s : char.ToUpper(s[0]) + s[1..].ToLower();
}

// ══════════════════════════════════════════════════════════════════════════════
// Node 6 – CombineTutorial
// ══════════════════════════════════════════════════════════════════════════════

public class CombineTutorial : Node
{
    protected override object? Prep(object shared)
    {
        var store          = (Dictionary<string, object>)shared;
        var projectName    = (string)store["project_name"];
        var outputBaseDir  = SharedStore.Get(store, "output_dir", "output");
        var outputPath     = Path.Combine(outputBaseDir, projectName);
        var repoUrl        = SharedStore.Get<string?>(store, "repo_url", null);
        var relationships  = (Dictionary<string, object>)store["relationships"];
        var chapterOrder   = (List<int>)store["chapter_order"];
        var abstractions   = (List<Dictionary<string, object>>)store["abstractions"];
        var chaptersContent = (List<string>)store["chapters"];

        var details  = (List<Dictionary<string, object>>)relationships["details"];
        var summary  = relationships["summary"].ToString()!;

        // ── Mermaid diagram ─────────────────────────────────────────────────
        var mermaid = new StringBuilder("flowchart TD\n");
        for (int i = 0; i < abstractions.Count; i++)
        {
            var label = abstractions[i]["name"].ToString()!.Replace("\"", "");
            mermaid.AppendLine($"    A{i}[\"{label}\"]");
        }
        foreach (var rel in details)
        {
            var edge = rel["label"].ToString()!
                .Replace("\"", "").Replace("\n", " ");
            if (edge.Length > 30) edge = edge[..27] + "...";
            mermaid.AppendLine($"    A{rel["from"]} -- \"{edge}\" --> A{rel["to"]}");
        }

        // ── index.md ────────────────────────────────────────────────────────
        var index = new StringBuilder();
        index.AppendLine($"# Tutorial: {projectName}");
        index.AppendLine();
        index.AppendLine(summary);
        index.AppendLine();
        if (!string.IsNullOrEmpty(repoUrl))
        {
            index.AppendLine($"**Source Repository:** [{repoUrl}]({repoUrl})");
            index.AppendLine();
        }
        index.AppendLine("```mermaid");
        index.Append(mermaid);
        index.AppendLine("```");
        index.AppendLine();
        index.AppendLine("## Chapters");
        index.AppendLine();

        var chapterFiles = new List<(string filename, string content)>();
        for (int i = 0; i < chapterOrder.Count; i++)
        {
            int abstrIdx = chapterOrder[i];
            if (abstrIdx < 0 || abstrIdx >= abstractions.Count || i >= chaptersContent.Count) continue;

            var abstrName = abstractions[abstrIdx]["name"].ToString()!;
            var safeName  = Regex.Replace(abstrName, @"[^\w]", "_").ToLowerInvariant();
            var filename  = $"{i + 1:D2}_{safeName}.md";

            index.AppendLine($"{i + 1}. [{abstrName}]({filename})");

            var chapterContent = chaptersContent[i];
            if (!chapterContent.EndsWith("\n\n")) chapterContent += "\n\n";
            chapterContent += "---\n\nGenerated by [AI Codebase Knowledge Builder](https://github.com/The-Pocket/Tutorial-Codebase-Knowledge)";

            chapterFiles.Add((filename, chapterContent));
        }

        index.AppendLine();
        index.AppendLine("---");
        index.AppendLine();
        index.AppendLine("Generated by [AI Codebase Knowledge Builder](https://github.com/The-Pocket/Tutorial-Codebase-Knowledge)");

        return new Dictionary<string, object>
        {
            ["output_path"]   = outputPath,
            ["index_content"] = index.ToString(),
            ["chapter_files"] = chapterFiles,
        };
    }

    protected override object? Exec(object? prepRes)
    {
        var p            = (Dictionary<string, object>)prepRes!;
        var outputPath   = p["output_path"].ToString()!;
        var indexContent = p["index_content"].ToString()!;
        var chapterFiles = (List<(string filename, string content)>)p["chapter_files"];

        Console.WriteLine($"Combining tutorial into directory: {outputPath}");
        Directory.CreateDirectory(outputPath);

        var indexPath = Path.Combine(outputPath, "index.md");
        File.WriteAllText(indexPath, indexContent, System.Text.Encoding.UTF8);
        Console.WriteLine($"  - Wrote {indexPath}");

        foreach (var (filename, content) in chapterFiles)
        {
            var path = Path.Combine(outputPath, filename);
            File.WriteAllText(path, content, System.Text.Encoding.UTF8);
            Console.WriteLine($"  - Wrote {path}");
        }

        return outputPath;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        ((Dictionary<string, object>)shared)["final_output_dir"] = execRes!;
        Console.WriteLine($"\nTutorial generation complete! Files are in: {execRes}");
        return null;
    }
}


