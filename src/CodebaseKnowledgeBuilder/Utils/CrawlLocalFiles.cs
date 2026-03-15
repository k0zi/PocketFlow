using Microsoft.Extensions.FileSystemGlobbing;

namespace CodebaseKnowledgeBuilder.Utils;

/// <summary>
/// Crawls a local directory with include/exclude glob patterns and optional .gitignore support.
/// Mirrors python/utils/crawl_local_files.py.
/// </summary>
public static class CrawlLocalFiles
{
    public static Dictionary<string, string> Crawl(
        string directory,
        IEnumerable<string>? includePatterns = null,
        IEnumerable<string>? excludePatterns = null,
        long maxFileSize = 0,
        bool useRelativePaths = true)
    {
        if (!Directory.Exists(directory))
            throw new ArgumentException($"Directory does not exist: {directory}");

        var includeList = includePatterns?.ToList() ?? new List<string>();
        var excludeList = excludePatterns?.ToList() ?? new List<string>();

        // ── Load .gitignore ─────────────────────────────────────────────────
        List<string> gitignorePatterns = new();
        var gitignorePath = Path.Combine(directory, ".gitignore");
        if (File.Exists(gitignorePath))
        {
            try
            {
                gitignorePatterns = File.ReadAllLines(gitignorePath)
                    .Where(l => !string.IsNullOrWhiteSpace(l) && !l.TrimStart().StartsWith('#'))
                    .ToList();
                Console.WriteLine($"Loaded .gitignore patterns from {gitignorePath}");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Warning: Could not read .gitignore: {e.Message}");
            }
        }

        // Build globbing matchers
        var gitignoreMatcher = BuildMatcher(gitignorePatterns);
        var excludeMatcher   = BuildMatcher(excludeList);
        var includeMatcher   = includeList.Count > 0 ? BuildMatcher(includeList) : null;

        var files = new Dictionary<string, string>();
        var allFiles = Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).ToList();
        int total = allFiles.Count;
        int processed = 0;

        foreach (var filepath in allFiles)
        {
            processed++;
            var relpath = useRelativePaths
                ? Path.GetRelativePath(directory, filepath)
                : filepath;

            // Normalise separators for matcher (always forward slash)
            var relpathNorm = relpath.Replace('\\', '/');

            // Exclusion checks
            if (gitignorePatterns.Count > 0 && MatchesGlob(gitignoreMatcher, relpathNorm))
            {
                PrintProgress(processed, total, relpathNorm, "skipped (gitignore)");
                continue;
            }
            if (excludeList.Count > 0 && MatchesGlob(excludeMatcher, relpathNorm))
            {
                PrintProgress(processed, total, relpathNorm, "skipped (excluded)");
                continue;
            }

            // Inclusion check
            if (includeMatcher != null && !MatchesGlob(includeMatcher, relpathNorm))
            {
                PrintProgress(processed, total, relpathNorm, "skipped (not included)");
                continue;
            }

            // Size check
            if (maxFileSize > 0)
            {
                var fi = new FileInfo(filepath);
                if (fi.Length > maxFileSize)
                {
                    PrintProgress(processed, total, relpathNorm, "skipped (size limit)");
                    continue;
                }
            }

            // Read
            try
            {
                var content = File.ReadAllText(filepath, System.Text.Encoding.UTF8);
                files[relpath] = content;
                PrintProgress(processed, total, relpathNorm, "processed");
            }
            catch (Exception e)
            {
                Console.WriteLine($"Warning: Could not read file {filepath}: {e.Message}");
                PrintProgress(processed, total, relpathNorm, "skipped (read error)");
            }
        }

        return files;
    }

    // ── Helpers ─────────────────────────────────────────────────────────────

    private static Matcher BuildMatcher(IEnumerable<string> patterns)
    {
        var m = new Matcher(StringComparison.OrdinalIgnoreCase);
        foreach (var p in patterns)
        {
            var pat = p.TrimStart('/');
            // Negation patterns not supported for simplicity; skip
            if (!pat.StartsWith('!'))
                m.AddInclude(pat);
        }
        return m;
    }

    private static bool MatchesGlob(Matcher matcher, string relpath)
    {
        var result = matcher.Match(relpath);
        return result.HasMatches;
    }

    private static void PrintProgress(int processed, int total, string relpath, string status)
    {
        int pct = total > 0 ? (int)((double)processed / total * 100) : 0;
        Console.WriteLine($"\x1b[92mProgress: {processed}/{total} ({pct}%) {relpath} [{status}]\x1b[0m");
    }
}

