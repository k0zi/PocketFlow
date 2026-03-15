# Codebase Knowledge Builder

> Turns any GitHub repository or local directory into a beginner-friendly, chapter-by-chapter tutorial — entirely powered by AI.

Built with [PocketFlow](https://github.com/The-Pocket/PocketFlow) (C# port) and [Ollama](https://ollama.com) for local LLM inference.

## How it works

The tool runs a six-node PocketFlow pipeline:

```
FetchRepo → IdentifyAbstractions → AnalyzeRelationships → OrderChapters → WriteChapters → CombineTutorial
```

1. **FetchRepo** — crawls a GitHub repo (HTTPS API or SSH clone via LibGit2Sharp) or a local directory, applying include/exclude glob patterns.
2. **IdentifyAbstractions** — asks the LLM to identify the 5–10 most important concepts in the code.
3. **AnalyzeRelationships** — asks the LLM to map out how those concepts interact and produces a project summary.
4. **OrderChapters** — asks the LLM for the best teaching order of those concepts.
5. **WriteChapters** — writes each chapter as beginner-friendly Markdown (with Mermaid diagrams), passing the previous chapters as context.
6. **CombineTutorial** — assembles an `index.md` with a Mermaid relationship flowchart plus individual chapter files.

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com) running locally (or accessible via `OLLAMA_HOST`)

## Getting started

### 1. Pull a model

```bash
ollama pull llama3
# or any model you prefer, e.g. mistral, codellama, qwen2.5-coder
```

### 2. Set environment variables (optional)

| Variable | Default | Description |
|---|---|---|
| `OLLAMA_HOST` | `http://localhost:11434` | Ollama server URL |
| `OLLAMA_MODEL` | `llama3:latest` | Model name |
| `GITHUB_TOKEN` | *(none)* | GitHub PAT — recommended to avoid rate limits |

### 3. Run

```bash
# Analyse a GitHub repository
dotnet run --project src/CodebaseKnowledgeBuilder -- --repo https://github.com/username/repo

# Analyse a local directory
dotnet run --project src/CodebaseKnowledgeBuilder -- --dir /path/to/your/codebase

# Generate a tutorial in another language
dotnet run --project src/CodebaseKnowledgeBuilder -- --repo https://github.com/username/repo --language chinese

# Limit to specific file types
dotnet run --project src/CodebaseKnowledgeBuilder -- \
  --repo https://github.com/username/repo \
  --include "*.cs" "*.md" \
  --exclude "tests/*" "obj/*"

# Show all options
dotnet run --project src/CodebaseKnowledgeBuilder -- --help
```

### CLI reference

| Flag | Default | Description |
|---|---|---|
| `--repo <url>` | *(required if no --dir)* | GitHub repository URL (HTTPS or SSH) |
| `--dir <path>` | *(required if no --repo)* | Local directory path |
| `-n, --name <name>` | derived from source | Project name used in output headings |
| `-t, --token <tok>` | `$GITHUB_TOKEN` | GitHub personal access token |
| `-o, --output <dir>` | `./output` | Base directory for generated files |
| `-i, --include <pats>` | common code files | Include glob patterns (space-separated) |
| `-e, --exclude <pats>` | build/test dirs | Exclude glob patterns (space-separated) |
| `-s, --max-size <n>` | `100000` | Max file size in bytes |
| `--language <lang>` | `english` | Language for the generated tutorial |
| `--max-abstractions <n>` | `10` | Max number of abstractions to identify |
| `--no-cache` | *(caching on)* | Disable LLM response disk cache |
| `-h, --help` | | Print usage |

## Output structure

```
output/
└── <ProjectName>/
    ├── index.md          ← summary + Mermaid relationship diagram + chapter list
    ├── 01_<concept>.md
    ├── 02_<concept>.md
    └── ...
```

## LLM caching

Responses are cached in `llm_cache.json` (current directory) keyed by prompt text.  
Use `--no-cache` to bypass it, or delete the file to start fresh.  
Log files are written to `logs/llm_calls_<date>.log`.

## Project structure

```
CodebaseKnowledgeBuilder/
├── CodebaseKnowledgeBuilder.csproj
├── Program.cs          ← CLI entry point + flow wiring
├── Nodes.cs            ← Six PocketFlow nodes
└── Utils/
    ├── CallLlm.cs          ← Ollama wrapper + JSON cache
    ├── CrawlGithubFiles.cs ← GitHub API + LibGit2Sharp SSH clone
    └── CrawlLocalFiles.cs  ← Local directory walker + .gitignore support
```

## Dependencies

| Package | Purpose |
|---|---|
| `PocketFlow` (project ref) | Pipeline framework |
| `SharedUtils` (project ref) | `OllamaConnector` |
| `OllamaSharp` | Ollama API client |
| `YamlDotNet` | YAML parsing for LLM responses |
| `LibGit2Sharp` | SSH git clone support |
| `Microsoft.Extensions.FileSystemGlobbing` | Glob pattern matching |

