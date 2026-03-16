namespace AgentSkills;

public static class Utils
{
    /// <summary>
    /// Loads all Markdown skill files from <paramref name="skillsDir"/> and returns
    /// a dictionary keyed by file stem (e.g. "executive_brief").
    /// </summary>
    public static Dictionary<string, string> LoadSkills(string skillsDir)
    {
        var dir = new DirectoryInfo(skillsDir);
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Skills directory not found: {skillsDir}");

        var skills = dir
            .GetFiles("*.md")
            .OrderBy(f => f.Name)
            .ToDictionary(
                f => Path.GetFileNameWithoutExtension(f.Name),
                f => File.ReadAllText(f.FullName));

        if (skills.Count == 0)
            throw new InvalidOperationException($"No skill files found in {skillsDir}");

        return skills;
    }

    /// <summary>Sends <paramref name="prompt"/> to the local LLM and returns its reply.</summary>
    public static string CallLlm(string prompt) => OllamaConnector.CallLlm(prompt);
}

