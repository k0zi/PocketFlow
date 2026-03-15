using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using OllamaSharp.Models.Chat;

namespace Agent;

public static class Utils
{
    // ── LLM ─────────────────────────────────────────────────────────────────

    public static string CallLlm(string prompt)
    {
        var messages = new List<Message>
        {
            new() { Role = ChatRole.User, Content = prompt }
        };
        return OllamaConnector.CallLlm(messages);
    }

    // ── Web Search ───────────────────────────────────────────────────────────

    private static readonly HttpClient _http = new();

    /// <summary>
    /// Searches DuckDuckGo HTML endpoint and returns the top 5 results
    /// formatted as "Title: ...\nURL: ...\nSnippet: ..." blocks,
    /// matching the Python ddgs output shape.
    /// </summary>
    public static string SearchWebDuckDuckGo(string query)
    {
        var content = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["q"] = query,
            ["b"] = "",      // start at first page
            ["kl"] = "wt-wt" // no region bias
        });

        using var request = new HttpRequestMessage(HttpMethod.Post, "https://html.duckduckgo.com/html/")
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation(
            "User-Agent",
            "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 " +
            "(KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36");

        HttpResponseMessage response;
        try
        {
            response = _http.Send(request);
            response.EnsureSuccessStatusCode();
        }
        catch (Exception ex)
        {
            return $"Search failed: {ex.Message}";
        }

        var html = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();

        var results = ParseDdgHtml(html);
        if (results.Count == 0)
            return "No results found.";

        return string.Join("\n\n", results.Take(5)
            .Select(r => $"Title: {r.Title}\nURL: {r.Url}\nSnippet: {r.Snippet}"));
    }

    // ── HTML Parser ──────────────────────────────────────────────────────────

    private record DdgResult(string Title, string Url, string Snippet);

    private static List<DdgResult> ParseDdgHtml(string html)
    {
        var results = new List<DdgResult>();

        // Each result lives inside <div class="result ..."> ... </div>
        var resultBlocks = Regex.Split(html, @"<div class=""result[^""]*""", RegexOptions.IgnoreCase)
                                .Skip(1); // first element is content before first match

        foreach (var block in resultBlocks)
        {
            var title   = ExtractFirst(block, @"<a class=""result__a""[^>]*>(.*?)</a>");
            var href    = ExtractFirst(block, @"<a class=""result__a""\s+href=""([^""]+)""");
            var snippet = ExtractFirst(block, @"<a class=""result__snippet""[^>]*>(.*?)</a>");

            // Fallback: try uddg (DuckDuckGo redirect URL contains the real URL)
            if (string.IsNullOrWhiteSpace(href))
                href = ExtractFirst(block, @"uddg=([^&""]+)");

            if (string.IsNullOrWhiteSpace(title) && string.IsNullOrWhiteSpace(href))
                continue;

            title   = HtmlDecode(StripTags(title));
            href    = string.IsNullOrWhiteSpace(href) ? "" : Uri.UnescapeDataString(href);
            snippet = HtmlDecode(StripTags(snippet));

            results.Add(new DdgResult(title, href, snippet));
        }

        return results;
    }

    private static string ExtractFirst(string input, string pattern)
    {
        var m = Regex.Match(input, pattern,
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
        return m.Success ? m.Groups[m.Groups.Count - 1].Value.Trim() : string.Empty;
    }

    private static string StripTags(string html) =>
        Regex.Replace(html ?? string.Empty, "<[^>]+>", string.Empty).Trim();

    private static string HtmlDecode(string text) =>
        WebUtility.HtmlDecode(text ?? string.Empty);
}

