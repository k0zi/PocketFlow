using PocketFlow;

namespace AgentSkills;

// ── SelectSkillNode ──────────────────────────────────────────────────────────

/// <summary>
/// Picks the best skill file for the current task.
/// Mirrors SelectSkill in nodes.py.
/// </summary>
public class SelectSkillNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        var task   = (string)store["task"];
        var skills = Utils.LoadSkills((string)store["skills_dir"]);
        return (task, skills);
    }

    protected override object? Execute(object? prepRes)
    {
        var (task, skills) = ((string, Dictionary<string, string>))prepRes!;

        // Tiny deterministic router – mirrors the Python demo logic.
        var preferred = task.Contains("checklist", StringComparison.OrdinalIgnoreCase)
                     || task.Contains("steps",     StringComparison.OrdinalIgnoreCase)
            ? "checklist_writer"
            : "executive_brief";

        if (skills.TryGetValue(preferred, out var content))
            return (preferred, content);

        // Fallback: first available skill.
        var first = skills.First();
        return (first.Key, first.Value);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        var (name, skillContent) = ((string, string))execRes!;
        store["selected_skill"]         = name;
        store["selected_skill_content"] = skillContent;
        return "default";
    }
}

// ── ApplySkillNode ───────────────────────────────────────────────────────────

/// <summary>
/// Injects the selected skill instructions into the LLM prompt and runs the task.
/// Mirrors ApplySkill in nodes.py.
/// </summary>
public class ApplySkillNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (
            task:         (string)store["task"],
            skillName:    (string)store["selected_skill"],
            skillContent: (string)store["selected_skill_content"]
        );
    }

    protected override object? Execute(object? prepRes)
    {
        var (task, skillName, skillContent) = ((string, string, string))prepRes!;

        var prompt = $"""
You are running an Agent Skill.

Skill name: {skillName}

Skill instructions:
---
{skillContent}
---

User task:
{task}

Follow the skill instructions exactly and return the final result only.
""".Trim();

        return Utils.CallLlm(prompt);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        store["result"] = (string)execRes!;
        return "default";
    }
}

