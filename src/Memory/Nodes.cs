using OllamaSharp.Models.Chat;
using PocketFlow;

namespace Memory;

// ── Shared data record ────────────────────────────────────────────────────────

/// <summary>
/// Stores an archived conversation pair together with its embedding vector.
/// Replaces the FAISS index entry from the Python implementation.
/// </summary>
record VectorItem(List<Message> Conversation, float[] Embedding);

// ── GetUserQuestionNode ───────────────────────────────────────────────────────

/// <summary>
/// Handles interactive user input. Initialises <c>shared["messages"]</c> on the
/// first run, then reads a line from the console and appends a user message.
/// Returns <c>"retrieve"</c> to continue the flow, or <c>null</c> on exit.
/// Port of <c>GetUserQuestionNode</c> in <c>nodes.py</c>.
/// </summary>
public class GetUserQuestionNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        if (!store.ContainsKey("messages"))
        {
            store["messages"] = new List<Message>();
            Console.WriteLine("Welcome to the interactive chat! Type 'exit' to end the conversation.");
        }
        return null;
    }

    protected override object? Execute(object? prepRes)
    {
        Console.Write("\nYou: ");
        var userInput = Console.ReadLine() ?? string.Empty;

        if (userInput.Trim().Equals("exit", StringComparison.OrdinalIgnoreCase))
            return null;

        return userInput;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        if (execRes is null)
        {
            Console.WriteLine("\nGoodbye!");
            return null; // End the conversation
        }

        var store    = (Dictionary<string, object>)shared;
        var messages = (List<Message>)store["messages"];
        messages.Add(new Message { Role = ChatRole.User, Content = (string)execRes });

        return "retrieve";
    }
}

// ── RetrieveNode ──────────────────────────────────────────────────────────────

/// <summary>
/// Finds the most relevant archived conversation using vector similarity (L2).
/// Stores the result in <c>shared["retrieved_conversation"]</c> and returns <c>"answer"</c>.
/// Port of <c>RetrieveNode</c> in <c>nodes.py</c>.
/// </summary>
public class RetrieveNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store    = (Dictionary<string, object>)shared;
        var messages = (List<Message>)store["messages"];

        // Get the latest user message to use as query
        var latestUserMsg = messages.LastOrDefault(m => m.Role == ChatRole.User);
        if (latestUserMsg is null) return null;

        // Nothing to retrieve yet if the archive is empty
        if (!store.TryGetValue("vector_items", out var vi) ||
            vi is not List<VectorItem> items || items.Count == 0)
            return null;

        return (latestUserMsg.Content ?? string.Empty, items);
    }

    protected override object? Execute(object? prepRes)
    {
        if (prepRes is null) return null;

        var (query, items) = ((string, List<VectorItem>))prepRes!;
        var preview        = query.Length > 30 ? query[..30] : query;
        Console.WriteLine($"🔍 Finding relevant conversation for: {preview}...");

        var queryEmbedding        = OllamaConnector.GetEmbedding(query);
        var (bestIdx, bestDist)   = SearchVectors(items, queryEmbedding);

        return (items[bestIdx].Conversation, bestDist);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;

        if (execRes is not null)
        {
            var (conversation, distance) = ((List<Message>, float))execRes!;
            store["retrieved_conversation"] = conversation;
            Console.WriteLine($"📄 Retrieved conversation (distance: {distance:F4})");
        }
        else
        {
            store.Remove("retrieved_conversation");
        }

        return "answer";
    }

    // ── Vector search helpers ─────────────────────────────────────────────────

    private static (int index, float distance) SearchVectors(List<VectorItem> items, float[] query)
    {
        var bestIdx  = 0;
        var bestDist = float.MaxValue;

        for (var i = 0; i < items.Count; i++)
        {
            var dist = L2Squared(query, items[i].Embedding);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx  = i;
            }
        }

        return (bestIdx, bestDist);
    }

    private static float L2Squared(float[] a, float[] b)
    {
        var sum = 0f;
        var len = Math.Min(a.Length, b.Length);
        for (var i = 0; i < len; i++)
        {
            var d = a[i] - b[i];
            sum += d * d;
        }
        return sum;
    }
}

// ── AnswerNode ────────────────────────────────────────────────────────────────

/// <summary>
/// Builds a prompt from the 3 most recent conversation pairs plus the retrieved
/// past conversation, calls the LLM, and appends the response to the history.
/// Returns <c>"embed"</c> when the sliding window overflows, otherwise <c>"question"</c>.
/// Port of <c>AnswerNode</c> in <c>nodes.py</c>.
/// </summary>
public class AnswerNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store    = (Dictionary<string, object>)shared;
        var messages = (List<Message>)store["messages"];
        if (messages.Count == 0) return null;

        // Keep the last 6 messages (= 3 conversation pairs)
        var recentMessages = messages.Count > 6
            ? messages.GetRange(messages.Count - 6, 6)
            : new List<Message>(messages);

        // Prepend the retrieved relevant conversation when available
        var context = new List<Message>();
        if (store.TryGetValue("retrieved_conversation", out var rc) &&
            rc is List<Message> retrieved)
        {
            context.Add(new Message
            {
                Role    = ChatRole.System,
                Content = "The following is a relevant past conversation that may help with the current query:"
            });
            context.AddRange(retrieved);
            context.Add(new Message
            {
                Role    = ChatRole.System,
                Content = "Now continue the current conversation:"
            });
        }

        context.AddRange(recentMessages);
        return context;
    }

    protected override object? Execute(object? prepRes)
    {
        if (prepRes is null) return null;
        return OllamaConnector.CallLlm((List<Message>)prepRes);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        if (prepRes is null || execRes is null) return null;

        var store    = (Dictionary<string, object>)shared;
        var messages = (List<Message>)store["messages"];
        var reply    = (string)execRes;

        Console.WriteLine($"\nA: {reply}");
        messages.Add(new Message { Role = ChatRole.Assistant, Content = reply });

        // If we have more than 3 conversation pairs, archive the oldest one
        return messages.Count > 6 ? "embed" : "question";
    }
}

// ── EmbedNode ─────────────────────────────────────────────────────────────────

/// <summary>
/// Removes the oldest user/assistant pair from <c>shared["messages"]</c>,
/// embeds it, and stores the <see cref="VectorItem"/> in <c>shared["vector_items"]</c>.
/// Always returns <c>"question"</c> to continue the chat loop.
/// Port of <c>EmbedNode</c> in <c>nodes.py</c>.
/// </summary>
public class EmbedNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store    = (Dictionary<string, object>)shared;
        var messages = (List<Message>)store["messages"];
        if (messages.Count <= 6) return null;

        // Extract and remove the oldest pair
        var oldestPair = messages.GetRange(0, 2);
        store["messages"] = messages.GetRange(2, messages.Count - 2);

        return oldestPair;
    }

    protected override object? Execute(object? prepRes)
    {
        if (prepRes is null) return null;

        var conversation = (List<Message>)prepRes;
        var userContent  = conversation.FirstOrDefault(m => m.Role == ChatRole.User)?.Content
                           ?? string.Empty;
        var asstContent  = conversation.FirstOrDefault(m => m.Role == ChatRole.Assistant)?.Content
                           ?? string.Empty;

        var combined  = $"User: {userContent} Assistant: {asstContent}";
        var embedding = OllamaConnector.GetEmbedding(combined);

        return (conversation, embedding);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        if (execRes is null)
            return "question";

        var store = (Dictionary<string, object>)shared;
        if (!store.ContainsKey("vector_items"))
            store["vector_items"] = new List<VectorItem>();

        var items                        = (List<VectorItem>)store["vector_items"];
        var (conversation, embedding)    = ((List<Message>, float[]))execRes!;
        items.Add(new VectorItem(conversation, embedding));

        Console.WriteLine($"✅ Added conversation to index at position {items.Count - 1}");
        Console.WriteLine($"✅ Index now contains {items.Count} conversations");

        return "question";
    }
}

