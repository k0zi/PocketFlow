using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using LibGit2Sharp;
using Microsoft.Extensions.FileSystemGlobbing;

namespace CodebaseKnowledgeBuilder.Utils;

/// <summary>
/// Crawls a GitHub repository (HTTPS API or SSH clone) and returns a path→content map.
/// Mirrors python/utils/crawl_github_files.py.
/// </summary>
public static class CrawlGithubFiles
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(60)
    };

    public static Dictionary<string, string> Crawl(
        string repoUrl,
        string? token = null,
        long maxFileSize = 1 * 1024 * 1024,
        bool useRelativePaths = false,
        IEnumerable<string>? includePatterns = null,
        IEnumerable<string>? excludePatterns = null)
    {
        var includeList = includePatterns?.ToList() ?? new();
        var excludeList = excludePatterns?.ToList() ?? new();

        bool ShouldInclude(string filePath, string fileName)
        {
            bool included = includeList.Count == 0 || MatchesAny(includeList, fileName);
            if (!included) return false;
            if (excludeList.Count > 0 && (MatchesAny(excludeList, filePath) || MatchesAny(excludeList, fileName)))
                return false;
            return true;
        }

        // ── SSH clone path ──────────────────────────────────────────────────
        bool isSsh = repoUrl.StartsWith("git@") || repoUrl.EndsWith(".git");
        if (isSsh)
            return CrawlViaSshClone(repoUrl, maxFileSize, useRelativePaths, ShouldInclude);

        // ── HTTPS GitHub API path ───────────────────────────────────────────
        return CrawlViaApi(repoUrl, token, maxFileSize, useRelativePaths, ShouldInclude);
    }

    // ── SSH Clone ───────────────────────────────────────────────────────────

    private static Dictionary<string, string> CrawlViaSshClone(
        string repoUrl,
        long maxFileSize,
        bool useRelativePaths,
        Func<string, string, bool> shouldInclude)
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"ckb_{Guid.NewGuid():N}");
        Console.WriteLine($"Cloning SSH repo {repoUrl} to {tmpDir} ...");

        try
        {
            Repository.Clone(repoUrl, tmpDir);
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error cloning repo: {e.Message}");
            return new();
        }

        var files = new Dictionary<string, string>();
        foreach (var filepath in Directory.EnumerateFiles(tmpDir, "*", SearchOption.AllDirectories))
        {
            var relPath = Path.GetRelativePath(tmpDir, filepath).Replace('\\', '/');
            var fileName = Path.GetFileName(filepath);

            if (!shouldInclude(relPath, fileName))
            {
                Console.WriteLine($"Skipping {relPath}: does not match include/exclude patterns");
                continue;
            }

            var fi = new FileInfo(filepath);
            if (fi.Length > maxFileSize)
            {
                Console.WriteLine($"Skipping {relPath}: size {fi.Length} exceeds limit {maxFileSize}");
                continue;
            }

            try
            {
                files[useRelativePaths ? relPath : filepath] = File.ReadAllText(filepath, System.Text.Encoding.UTF8);
                Console.WriteLine($"Added {relPath} ({fi.Length} bytes)");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Failed to read {relPath}: {e.Message}");
            }
        }

        try { Directory.Delete(tmpDir, true); } catch { }
        return files;
    }

    // ── GitHub API (HTTPS) ──────────────────────────────────────────────────

    private static Dictionary<string, string> CrawlViaApi(
        string repoUrl,
        string? token,
        long maxFileSize,
        bool useRelativePaths,
        Func<string, string, bool> shouldInclude)
    {
        // Set auth header
        Http.DefaultRequestHeaders.UserAgent.TryParseAdd("CodebaseKnowledgeBuilder/1.0");
        Http.DefaultRequestHeaders.Accept.Clear();
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github.v3+json");
        if (!string.IsNullOrEmpty(token))
            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("token", token);

        // Parse URL: https://github.com/{owner}/{repo}[/tree/{ref}[/{path}]]
        var uri   = new Uri(repoUrl);
        var parts = uri.AbsolutePath.Trim('/').Split('/');
        if (parts.Length < 2)
            throw new ArgumentException($"Invalid GitHub URL: {repoUrl}");

        string owner = parts[0];
        string repo  = parts[1];
        string? gitRef  = null;
        string specificPath = "";

        if (parts.Length > 3 && parts[2] == "tree")
        {
            // Try to resolve branch/commit
            var branches = FetchBranches(owner, repo);
            var candidate = string.Join("/", parts.Skip(3));
            gitRef = branches.FirstOrDefault(b => candidate.StartsWith(b));

            if (gitRef == null && parts.Length > 3)
            {
                string tree = parts[3];
                gitRef = CheckTree(owner, repo, tree) ? tree : null;
            }

            if (gitRef == null)
            {
                Console.WriteLine("Could not match URL to any branch or commit tree.");
                return new();
            }

            int pathStart = 3 + gitRef.Split('/').Length;
            specificPath = pathStart < parts.Length ? string.Join("/", parts.Skip(pathStart)) : "";
        }

        var files = new Dictionary<string, string>();
        FetchContents(owner, repo, gitRef, specificPath, useRelativePaths, specificPath,
            maxFileSize, shouldInclude, excludePatterns: null, files);
        return files;
    }

    // ── GitHub API helpers ──────────────────────────────────────────────────

    private static List<string> FetchBranches(string owner, string repo)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/branches";
        try
        {
            var resp = Http.GetAsync(url).GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode) return new();
            var json = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            var arr  = JsonSerializer.Deserialize<JsonArray>(json);
            return arr?.Select(n => n?["name"]?.ToString() ?? "").Where(s => s != "").ToList() ?? new();
        }
        catch { return new(); }
    }

    private static bool CheckTree(string owner, string repo, string tree)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/git/trees/{tree}";
        try
        {
            var resp = Http.GetAsync(url).GetAwaiter().GetResult();
            return resp.IsSuccessStatusCode;
        }
        catch { return false; }
    }

    private static void FetchContents(
        string owner, string repo, string? gitRef,
        string apiPath, bool useRelativePaths, string specificPath,
        long maxFileSize, Func<string, string, bool> shouldInclude,
        IEnumerable<string>? excludePatterns,
        Dictionary<string, string> files)
    {
        var url = $"https://api.github.com/repos/{owner}/{repo}/contents/{apiPath}";
        if (!string.IsNullOrEmpty(gitRef))
            url += $"?ref={Uri.EscapeDataString(gitRef)}";

        HttpResponseMessage resp;
        try
        {
            resp = Http.GetAsync(url).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            Console.WriteLine($"Error fetching {apiPath}: {e.Message}");
            return;
        }

        // Handle rate limit
        if ((int)resp.StatusCode == 403)
        {
            var body = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (body.Contains("rate limit", StringComparison.OrdinalIgnoreCase))
            {
                if (resp.Headers.TryGetValues("X-RateLimit-Reset", out var vals) &&
                    long.TryParse(vals.First(), out var resetTs))
                {
                    int wait = (int)Math.Max(resetTs - DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 0) + 1;
                    Console.WriteLine($"Rate limit exceeded. Waiting {wait}s ...");
                    Thread.Sleep(wait * 1000);
                    FetchContents(owner, repo, gitRef, apiPath, useRelativePaths, specificPath,
                        maxFileSize, shouldInclude, excludePatterns, files);
                }
                return;
            }
        }

        if (!resp.IsSuccessStatusCode)
        {
            Console.WriteLine($"Error fetching {apiPath}: {resp.StatusCode}");
            return;
        }

        var jsonStr  = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        JsonNode? root = JsonNode.Parse(jsonStr);
        JsonArray items = root is JsonArray arr ? arr : new JsonArray(root!);

        foreach (var item in items)
        {
            if (item == null) continue;
            var itemPath = item["path"]!.ToString();
            var itemName = item["name"]!.ToString();
            var itemType = item["type"]!.ToString();

            // Compute relative path
            string relPath = useRelativePaths && !string.IsNullOrEmpty(specificPath) && itemPath.StartsWith(specificPath)
                ? itemPath[(specificPath.Length)..].TrimStart('/')
                : itemPath;

            if (itemType == "file")
            {
                if (!shouldInclude(relPath, itemName))
                {
                    Console.WriteLine($"Skipping {relPath}: does not match include/exclude patterns");
                    continue;
                }

                long fileSize = item["size"]?.GetValue<long>() ?? 0;
                if (fileSize > maxFileSize)
                {
                    Console.WriteLine($"Skipping {relPath}: size {fileSize} exceeds limit");
                    continue;
                }

                string? downloadUrl = item["download_url"]?.ToString();
                if (!string.IsNullOrEmpty(downloadUrl))
                {
                    try
                    {
                        var fileResp = Http.GetAsync(downloadUrl).GetAwaiter().GetResult();
                        if (fileResp.IsSuccessStatusCode)
                        {
                            files[relPath] = fileResp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                            Console.WriteLine($"Downloaded: {relPath} ({fileSize} bytes)");
                        }
                        else
                        {
                            Console.WriteLine($"Failed to download {relPath}: {fileResp.StatusCode}");
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine($"Error downloading {relPath}: {e.Message}");
                    }
                }
            }
            else if (itemType == "dir")
            {
                // Check exclude before recursing
                if (excludePatterns != null && (MatchesAny(excludePatterns, itemPath) || MatchesAny(excludePatterns, relPath)))
                    continue;

                FetchContents(owner, repo, gitRef, itemPath, useRelativePaths, specificPath,
                    maxFileSize, shouldInclude, excludePatterns, files);
            }
        }
    }

    // ── Pattern matching ────────────────────────────────────────────────────

    private static bool MatchesAny(IEnumerable<string> patterns, string path)
    {
        var m = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var p in patterns)
        {
            var pat = p.TrimStart('/');
            if (!pat.StartsWith('!')) m.AddInclude(pat);
        }
        return m.Match(path.Replace('\\', '/')).HasMatches;
    }
}

