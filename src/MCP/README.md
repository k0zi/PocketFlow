# PocketFlow MCP Demo

This project shows how to build an agent that performs math operations using
**PocketFlow** and **Model Context Protocol (MCP)**. It demonstrates how to
toggle between an MCP server and a plain local-function implementation with a
single flag.

This implementation is based on the tutorial:
[MCP Simply Explained: Function Calling Rebranded or Genuine Breakthrough?](https://zacharyhuang.substack.com/p/mcp-simply-explained-function-calling)

## Features

- Four math tools (add, subtract, multiply, divide) exposed via MCP
- PocketFlow agent that discovers tools at runtime and calls the right one
- **One-line toggle** between MCP and local function calling

## How to Run

1. Start Ollama with the default model (e.g. `gemma3:latest`), or override
   via environment variables:
   ```bash
   export OLLAMA_MODEL="llama3.2:latest"
   export OLLAMA_HOST="http://localhost:11434"
   ```

2. Build and run:
   ```bash
   dotnet run
   ```

   Or pass a custom question:
   ```bash
   dotnet run -- "--What is 42 multiplied by 7?"
   ```

3. To use the MCP server instead of local functions, open `Program.cs` and
   change:
   ```csharp
   Utils.UseMcp = true;   // spawn this executable as an MCP server
   ```

> **Note on numbers:** the tools use 64-bit integers (`long`), so values
> must fit within −9,223,372,036,854,775,808 … 9,223,372,036,854,775,807.
> Division returns a `double`.

## MCP vs Function Calling

| | Function Calling | MCP |
|---|---|---|
| Tool location | Embedded in application code | Separate MCP server process |
| Adding new tools | Requires modifying the agent | Add to server; no agent changes |
| Interface | Any convention | Standard JSON-RPC over stdio |
| Discovery | Hardcoded in agent | Dynamic: `list_tools` at runtime |

Toggle with a single flag in `Program.cs`:
```csharp
Utils.UseMcp = false;  // ← local functions (default)
Utils.UseMcp = true;   // ← spawn this process as an MCP stdio server
```

## How It Works

```mermaid
flowchart LR
    tools[GetToolsNode] -->|decide| decide[DecideToolNode]
    decide -->|execute| execute[ExecuteToolNode]
```

1. **GetToolsNode** – retrieves tool definitions, either locally or from the
   MCP server, and formats them for the LLM prompt.
2. **DecideToolNode** – sends the question + tool list to the LLM; parses
   the YAML response to extract which tool to call and with what parameters.
3. **ExecuteToolNode** – calls the selected tool (local or MCP) and prints
   the result.

## Files

| File | Description |
|---|---|
| `Program.cs` | Entry point. Handles `--serve` (MCP server mode) and agent mode |
| `Nodes.cs` | PocketFlow nodes: `GetToolsNode`, `DecideToolNode`, `ExecuteToolNode` |
| `Utils.cs` | `CallLlm`, `GetTools`, and `CallTool` with local & MCP implementations |
| `ToolDefinition.cs` | `ToolDefinition` and `ParameterDef` model classes |

## Architecture – MCP mode

```
┌──────────────────────────────────────────────────────────────────┐
│  dotnet run                                                      │
│  ┌─────────────┐    stdio    ┌─────────────────────────────────┐ │
│  │  Agent flow │────spawn───▶│  same executable --serve        │ │
│  │  (client)   │◀────MCP ───│  MathServerTools (MCP server)   │ │
│  └─────────────┘             └─────────────────────────────────┘ │
└──────────────────────────────────────────────────────────────────┘
```

In MCP mode the agent spawns itself with `--serve` and communicates via the
MCP JSON-RPC protocol over stdin/stdout — exactly the same protocol used
between any MCP client and any MCP server, regardless of language.

