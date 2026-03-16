# PocketFlow Async Basic Example (C#)

This example demonstrates async operations using a simple **Recipe Finder** built with PocketFlow's `AsyncNode` and `AsyncFlow`. It mirrors the Python `pocketflow-async-basic` cookbook.

## What This Example Does

1. You enter an ingredient (e.g. `chicken`).
2. The app fetches matching recipes (simulated async API call).
3. An LLM picks the best recipe (async Ollama call via `OllamaConnector`).
4. You approve or reject the suggestion.
5. If rejected, the flow loops back and tries again with a new suggestion.

## Project Structure

| File | Description |
|---|---|
| `Program.cs` | Entry point – builds the flow and runs it (mirrors `main.py` + `flow.py`) |
| `Nodes.cs` | `FetchRecipes`, `SuggestRecipe`, `GetApproval` AsyncNode implementations (mirrors `nodes.py`) |
| `Utils.cs` | `FetchRecipesAsync`, `CallLlmAsync`, `GetUserInputAsync` helpers (mirrors `utils.py`) |

## How It Works

### 1. `FetchRecipes` (`AsyncNode`)

```csharp
protected override async Task<object?> PrepAsync(object shared)
{
    // Prompt user for an ingredient
    var ingredient = await Utils.GetUserInputAsync("Enter ingredient: ");
    return ingredient;
}

protected override async Task<object?> ExecAsync(object? prepRes)
{
    // Simulate async API call
    return await Utils.FetchRecipesAsync((string)prepRes!);
}
```

### 2. `SuggestRecipe` (`AsyncNode`)

```csharp
protected override async Task<object?> ExecAsync(object? prepRes)
{
    // Async LLM call
    var recipes = (List<string>)prepRes!;
    return await Utils.CallLlmAsync(
        $"Choose best recipe from: {string.Join(", ", recipes)}"
    );
}
```

### 3. `GetApproval` (`AsyncNode`)

```csharp
protected override Task<object?> PostAsync(object shared, object? prepRes, object? execRes)
{
    // Route flow: "accept" exits, "retry" loops back to SuggestRecipe
    return (string)execRes! == "y"
        ? Task.FromResult<object?>("accept")
        : Task.FromResult<object?>("retry");
}
```

## Flow Graph

```
FetchRecipes ──"suggest"──► SuggestRecipe ──"approve"──► GetApproval
                                  ▲                            │
                                  └──────────"retry"───────────┘
                                                               │
                                                          "accept"
                                                               │
                                                             (end)
```

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Ollama](https://ollama.com/) running locally (`http://localhost:11434`) with `gemma3:latest` pulled

## Running the Example

```bash
dotnet run --project src/AsyncBasic
```

## Sample Interaction

```
Welcome to Recipe Finder!
------------------------
Enter ingredient: chicken
Fetching recipes for chicken...
Found 3 recipes.

Suggesting best recipe...
How about: Grilled Chicken with Herbs

Accept this recipe? (y/n): n

Let's try another recipe...

Suggesting best recipe...
How about: Chicken Stir Fry

Accept this recipe? (y/n): y

Great choice! Here's your recipe...
Recipe:     Chicken Stir Fry
Ingredient: chicken

Thanks for using Recipe Finder!
```

## Key Concepts

1. **`AsyncNode`** – Override `PrepAsync`, `ExecAsync`, and `PostAsync` to build non-blocking pipeline steps.
2. **`AsyncFlow`** – Orchestrates async nodes; routes between them based on the string returned by `PostAsync`.
3. **Async I/O** – `Task.Delay` simulates network latency; `Console.In.ReadLineAsync()` avoids blocking the async flow on user input.
4. **Flow Control** – Returning `"retry"` from `PostAsync` loops back to `SuggestRecipe`; returning `"accept"` exits the flow.

