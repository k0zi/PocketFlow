using AgentSkills;
using PocketFlow;

// ── Build the flow (mirrors flow.py) ────────────────────────────────────────

var selectSkill = new SelectSkillNode();
var applySkill  = new ApplySkillNode();

selectSkill.Then(applySkill);

var flow = new Flow(start: selectSkill);

// ── Parse task from CLI args (mirrors main.py) ───────────────────────────────

const string defaultTask = "Summarize this launch plan for a VP audience";

var task = defaultTask;
foreach (var arg in args)
{
    if (arg.StartsWith("--"))
    {
        task = arg[2..];
        break;
    }
}

// ── Run ──────────────────────────────────────────────────────────────────────

var shared = new Dictionary<string, object>
{
    ["task"]       = task,
    ["skills_dir"] = "skills",
};

Console.WriteLine($"🧩 Task: {task}");
flow.Run(shared);

Console.WriteLine("\n=== Skill Used ===");
Console.WriteLine(shared.TryGetValue("selected_skill", out var skill) ? skill : "(none)");

Console.WriteLine("\n=== Output ===");
Console.WriteLine(shared.TryGetValue("result", out var result) ? result : "(no result)");
