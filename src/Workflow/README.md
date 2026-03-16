# Article Writing Workflow

A PocketFlow C# example that demonstrates an article writing workflow using a sequence of LLM calls.

## Features

- Generate a simple outline with up to 3 main sections using YAML structured output
- Write concise (100 words max) content for each section in simple terms
- Apply a conversational, engaging style to the final article

## Getting Started

1. Make sure [Ollama](https://ollama.com/) is running locally with a model available (default: `gemma3:latest`):

```bash
ollama serve
ollama pull gemma3:latest
```

2. Optionally override the model or host via environment variables:

```bash
export OLLAMA_MODEL=llama3.2
export OLLAMA_HOST=http://localhost:11434
```

3. Run with the default topic ("AI Safety"):

```bash
dotnet run --project Workflow
```

4. Or specify your own topic:

```bash
dotnet run --project Workflow -- Climate Change
```

## How It Works

The workflow consists of three sequential nodes:

```mermaid
graph LR
    Outline[GenerateOutline] --> Write[WriteSimpleContent]
    Write --> Style[ApplyStyle]
```

Here's what each node does:

1. **GenerateOutline** — Calls the LLM to produce up to 3 section titles in YAML format, then parses and stores them in shared state.
2. **WriteSimpleContent** — A `BatchNode` that calls the LLM once per section to write a concise (≤ 100 words) explanation with an analogy.
3. **ApplyStyle** — Rewrites the combined draft in a warm, conversational tone with rhetorical questions and a strong opening/conclusion.

## Files

| File | Description |
|------|-------------|
| `Program.cs` | Entry point — builds the flow and runs it (mirrors `main.py` + `flow.py`) |
| `Nodes.cs` | `GenerateOutline`, `WriteSimpleContent`, and `ApplyStyle` node classes (mirrors `nodes.py`) |
| `Utils.cs` | `CallLlm` helper that delegates to `OllamaConnector` (mirrors `utils/call_llm.py`) |
| `Workflow.csproj` | Project file with references to `PocketFlow`, `SharedUtils`, and `YamlDotNet` |

## Example Output

```
=== Starting Article Workflow on Topic: AI Safety ===


===== OUTLINE (YAML) =====

sections:
- Introduction to AI Safety
- Key Challenges in AI Safety
- Strategies for Ensuring AI Safety


===== PARSED OUTLINE =====

1. Introduction to AI Safety
2. Key Challenges in AI Safety
3. Strategies for Ensuring AI Safety

=========================

✓ Completed section 1/3: Introduction to AI Safety
✓ Completed section 2/3: Key Challenges in AI Safety
✓ Completed section 3/3: Strategies for Ensuring AI Safety


===== SECTION CONTENTS =====

--- Introduction to AI Safety ---
AI Safety is about making sure that artificial intelligence systems are helpful and not harmful...

--- Key Challenges in AI Safety ---
One key challenge is making sure AI makes decisions that align with human values...

--- Strategies for Ensuring AI Safety ---
By testing AI systems under different conditions and keeping human oversight, we can manage risks...

===========================


===== FINAL ARTICLE =====

# Welcome to the World of AI Safety
...

========================


=== Workflow Completed ===

Topic: AI Safety
Outline Length: 96 characters
Draft Length: 1690 characters
Final Article Length: 2266 characters
```

