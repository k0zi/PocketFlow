using PocketFlow;

namespace AsyncBasic;

// ── FetchRecipes ─────────────────────────────────────────────────────────────

/// <summary>
/// AsyncNode that prompts for an ingredient and fetches matching recipes.
/// Mirrors <c>FetchRecipes</c> in nodes.py.
/// </summary>
public class FetchRecipes : AsyncNode
{
    /// <summary>Prompts the user for an ingredient.</summary>
    protected override async Task<object?> PrepAsync(object shared)
    {
        var ingredient = await Utils.GetUserInputAsync("Enter ingredient: ");
        return ingredient;
    }

    /// <summary>Fetches recipes for the ingredient asynchronously.</summary>
    protected override async Task<object?> ExecAsync(object? prepRes)
    {
        var ingredient = (string)prepRes!;
        return await Utils.FetchRecipesAsync(ingredient);
    }

    /// <summary>Stores results in shared state and advances to "suggest".</summary>
    protected override Task<object?> PostAsync(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        store["recipes"]    = (List<string>)execRes!;
        store["ingredient"] = (string)prepRes!;
        return Task.FromResult<object?>("suggest");
    }
}

// ── SuggestRecipe ─────────────────────────────────────────────────────────────

/// <summary>
/// AsyncNode that asks the LLM to pick the best recipe from the fetched list.
/// Mirrors <c>SuggestRecipe</c> in nodes.py.
/// </summary>
public class SuggestRecipe : AsyncNode
{
    /// <summary>Reads the recipe list from shared state.</summary>
    protected override Task<object?> PrepAsync(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return Task.FromResult<object?>(store["recipes"]);
    }

    /// <summary>Sends the recipe list to the LLM and returns its suggestion.</summary>
    protected override async Task<object?> ExecAsync(object? prepRes)
    {
        var recipes = (List<string>)prepRes!;
        var prompt  = $"Choose best recipe from: {string.Join(", ", recipes)}";
        return await Utils.CallLlmAsync(prompt);
    }

    /// <summary>Stores the suggestion and advances to "approve".</summary>
    protected override Task<object?> PostAsync(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        store["suggestion"] = (string)execRes!;
        return Task.FromResult<object?>("approve");
    }
}

// ── GetApproval ───────────────────────────────────────────────────────────────

/// <summary>
/// AsyncNode that asks the user whether to accept the suggested recipe.
/// Returns "accept" or "retry" to control flow.
/// Mirrors <c>GetApproval</c> in nodes.py.
/// </summary>
public class GetApproval : AsyncNode
{
    /// <summary>Reads the current suggestion from shared state.</summary>
    protected override Task<object?> PrepAsync(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return Task.FromResult<object?>(store["suggestion"]);
    }

    /// <summary>Prompts the user for a yes/no answer.</summary>
    protected override async Task<object?> ExecAsync(object? prepRes)
    {
        return await Utils.GetUserInputAsync("\nAccept this recipe? (y/n): ");
    }

    /// <summary>Routes to "accept" or "retry" based on the user's answer.</summary>
    protected override Task<object?> PostAsync(object shared, object? prepRes, object? execRes)
    {
        var store  = (Dictionary<string, object>)shared;
        var answer = (string)execRes!;

        if (answer == "y")
        {
            Console.WriteLine("\nGreat choice! Here's your recipe...");
            Console.WriteLine($"Recipe:     {store["suggestion"]}");
            Console.WriteLine($"Ingredient: {store["ingredient"]}");
            return Task.FromResult<object?>("accept");
        }

        Console.WriteLine("\nLet's try another recipe...");
        return Task.FromResult<object?>("retry");
    }
}

