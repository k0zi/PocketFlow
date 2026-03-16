using CodebaseKnowledgeBuilder;
using PocketFlow;

// ── Default patterns (mirrors main.py) ───────────────────────────────────────

var defaultInclude = new List<string>
{
    "*.py","*.js","*.jsx","*.ts","*.tsx","*.go","*.java","*.pyi","*.pyx",
    "*.c","*.cc","*.cpp","*.h","*.md","*.rst","*Dockerfile","*Makefile",
    "*.yaml","*.yml","*.cs","*.fs","*.vb","*.csproj","*.fsproj",
};

var defaultExclude = new List<string>
{
    "assets/*","data/*","images/*","public/*","static/*","temp/*",
    "*docs/*","*venv/*","*.venv/*","*test*","*tests/*","*examples/*",
    "v1/*","*dist/*","*build/*","*experimental/*","*deprecated/*",
    "*misc/*","*legacy/*",".git/*",".github/*",".next/*",".vscode/*",
    "*obj/*","*bin/*","*node_modules/*","*.log",
};

// ── Argument parsing ──────────────────────────────────────────────────────────

string? repoUrl     = null;
string? localDir    = null;
string? projectName = null;
string? token       = null;
string  outputDir   = "output";
var     include     = new List<string>();
var     exclude     = new List<string>();
long    maxSize     = 100_000;
string  language    = "english";
bool    noCache     = false;
int     maxAbstr    = 10;

for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--repo":          repoUrl     = args[++i]; break;
        case "--dir":           localDir    = args[++i]; break;
        case "-n": case "--name":    projectName = args[++i]; break;
        case "-t": case "--token":   token       = args[++i]; break;
        case "-o": case "--output":  outputDir   = args[++i]; break;
        case "-s": case "--max-size": maxSize    = long.Parse(args[++i]); break;
        case "--language":      language    = args[++i]; break;
        case "--no-cache":      noCache     = true; break;
        case "--max-abstractions": maxAbstr = int.Parse(args[++i]); break;
        case "-i": case "--include":
            while (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                include.Add(args[++i]);
            break;
        case "-e": case "--exclude":
            while (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
                exclude.Add(args[++i]);
            break;
        case "-h": case "--help":
            PrintHelp();
            return 0;
    }
}

if (repoUrl == null && localDir == null)
{
    Console.Error.WriteLine("Error: specify either --repo <url> or --dir <path>");
    PrintHelp();
    return 1;
}

// Resolve GitHub token
if (repoUrl != null && token == null)
{
    token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
    if (token == null)
        Console.WriteLine("Warning: No GitHub token provided. You might hit rate limits for public repositories.");
}

// ── Shared store ──────────────────────────────────────────────────────────────

var shared = new Dictionary<string, object?>
{
    ["repo_url"]           = repoUrl,
    ["local_dir"]          = localDir,
    ["project_name"]       = projectName,
    ["github_token"]       = token,
    ["output_dir"]         = outputDir,
    ["include_patterns"]   = include.Count > 0 ? include : defaultInclude,
    ["exclude_patterns"]   = exclude.Count > 0 ? exclude : defaultExclude,
    ["max_file_size"]      = maxSize,
    ["language"]           = language,
    ["use_cache"]          = !noCache,
    ["max_abstraction_num"]= maxAbstr,
    ["files"]              = new List<(string, string)>(),
    ["abstractions"]       = new List<Dictionary<string, object>>(),
    ["relationships"]      = new Dictionary<string, object>(),
    ["chapter_order"]      = new List<int>(),
    ["chapters"]           = new List<string>(),
    ["final_output_dir"]   = null,
};

Console.WriteLine($"Starting tutorial generation for: {repoUrl ?? localDir} in {char.ToUpper(language[0])}{language[1..]} language");
Console.WriteLine($"LLM caching: {(noCache ? "Disabled" : "Enabled")}");

// ── Build flow (mirrors flow.py) ──────────────────────────────────────────────

var fetchRepo             = new FetchRepo();
var identifyAbstractions  = new IdentifyAbstractions(maxRetries: 5, wait: 20);
var analyzeRelationships  = new AnalyzeRelationships(maxRetries: 5, wait: 20);
var orderChapters         = new OrderChapters(maxRetries: 5, wait: 20);
var writeChapters         = new WriteChapters(maxRetries: 5, wait: 20);
var combineTutorial       = new CombineTutorial();

fetchRepo.Then(identifyAbstractions)
    .Then(analyzeRelationships)
    .Then(orderChapters)
    .Then(writeChapters)
    .Then(combineTutorial);

var flow = new Flow(start: fetchRepo);
flow.Run(shared);

return 0;

// ── Help text ─────────────────────────────────────────────────────────────────

static void PrintHelp()
{
    Console.WriteLine("""
Usage: CodebaseKnowledgeBuilder [options]

Source (required, mutually exclusive):
  --repo <url>               GitHub repository URL
  --dir  <path>              Local directory path

Options:
  -n, --name <name>          Project name (derived from source if omitted)
  -t, --token <token>        GitHub token (or set GITHUB_TOKEN env var)
  -o, --output <dir>         Output base directory (default: ./output)
  -i, --include <patterns>   Include file patterns, e.g. *.cs *.md
  -e, --exclude <patterns>   Exclude file patterns, e.g. tests/* obj/*
  -s, --max-size <bytes>     Max file size in bytes (default: 100000)
      --language <lang>      Tutorial language (default: english)
      --no-cache             Disable LLM response caching
      --max-abstractions <n> Max abstractions to identify (default: 10)
  -h, --help                 Show this help message

Environment variables:
  OLLAMA_HOST    Ollama server URL (default: http://localhost:11434)
  OLLAMA_MODEL   Model name (default: llama3:latest)
  GITHUB_TOKEN   GitHub personal access token
""");
}
