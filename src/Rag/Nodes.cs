using PocketFlow;

namespace Rag;

// ── Offline flow ─────────────────────────────────────────────────────────────

/// <summary>
/// Reads <c>shared["texts"]</c>, splits each document into fixed-size chunks,
/// then stores the flattened list back into <c>shared["texts"]</c>.
/// Port of <c>ChunkDocumentsNode</c> in <c>nodes.py</c>.
/// </summary>
public class ChunkDocumentsNode : BatchNode
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (IEnumerable<string>)store["texts"];
    }

    protected override object? Execute(object? prepRes)
    {
        var text = (string)prepRes!;
        return Utils.FixedSizeChunk(text);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store       = (Dictionary<string, object>)shared;
        var origTexts   = (IEnumerable<string>)prepRes!;
        var execResList = (List<object?>)execRes!;

        var allChunks = new List<string>();
        foreach (var item in execResList)
            allChunks.AddRange((List<string>)item!);

        store["texts"] = allChunks;
        Console.WriteLine($"✅ Created {allChunks.Count} chunks from {origTexts.Count()} documents");
        return "default";
    }
}

/// <summary>
/// Reads <c>shared["texts"]</c> (chunks), embeds each one, and stores the
/// resulting vectors in <c>shared["embeddings"]</c> as a <c>List&lt;float[]&gt;</c>.
/// Port of <c>EmbedDocumentsNode</c> in <c>nodes.py</c>.
/// </summary>
public class EmbedDocumentsNode : BatchNode
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (IEnumerable<string>)store["texts"];
    }

    protected override object? Execute(object? prepRes)
    {
        var text = (string)prepRes!;
        return Utils.GetEmbedding(text);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store       = (Dictionary<string, object>)shared;
        var execResList = (List<object?>)execRes!;

        var embeddings = execResList
            .Select(e => (float[])e!)
            .ToList();

        store["embeddings"] = embeddings;
        Console.WriteLine($"✅ Created {embeddings.Count} document embeddings");
        return "default";
    }
}

/// <summary>
/// Wraps the <c>List&lt;float[]&gt;</c> embeddings as an in-memory index and stores
/// it in <c>shared["index"]</c>.
/// Replaces the FAISS <c>IndexFlatL2</c> from <c>nodes.py</c> with a pure C# structure.
/// </summary>
public class CreateIndexNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (List<float[]>)store["embeddings"];
    }

    protected override object? Execute(object? prepRes)
    {
        Console.WriteLine("🔍 Creating search index...");
        // Pure C# in-memory index: the embedding list IS the index.
        return prepRes;
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        var index = (List<float[]>)execRes!;
        store["index"] = index;
        Console.WriteLine($"✅ Index created with {index.Count} vectors");
        return "default";
    }
}

// ── Online flow ──────────────────────────────────────────────────────────────

/// <summary>
/// Embeds <c>shared["query"]</c> and stores the vector in <c>shared["query_embedding"]</c>.
/// Port of <c>EmbedQueryNode</c> in <c>nodes.py</c>.
/// </summary>
public class EmbedQueryNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (string)store["query"];
    }

    protected override object? Execute(object? prepRes)
    {
        var query = (string)prepRes!;
        Console.WriteLine($"🔍 Embedding query: {query}");
        return Utils.GetEmbedding(query);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        store["query_embedding"] = (float[])execRes!;
        return "default";
    }
}

/// <summary>
/// Performs a linear nearest-neighbour search (squared L2 distance) over the
/// in-memory index and stores the best match in <c>shared["retrieved_document"]</c>.
/// Replaces <c>faiss.IndexFlatL2.search</c> from <c>nodes.py</c>.
/// </summary>
public class RetrieveDocumentNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (
            (float[])store["query_embedding"],
            (List<float[]>)store["index"],
            (List<string>)store["texts"]
        );
    }

    protected override object? Execute(object? prepRes)
    {
        Console.WriteLine("🔎 Searching for relevant documents...");
        var (queryEmbedding, index, texts) =
            ((float[], List<float[]>, List<string>))prepRes!;

        var bestIdx  = 0;
        var bestDist = float.MaxValue;

        for (var i = 0; i < index.Count; i++)
        {
            var dist = L2Squared(queryEmbedding, index[i]);
            if (dist < bestDist)
            {
                bestDist = dist;
                bestIdx  = i;
            }
        }

        return new Dictionary<string, object>
        {
            ["text"]     = texts[bestIdx],
            ["index"]    = bestIdx,
            ["distance"] = bestDist
        };
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store  = (Dictionary<string, object>)shared;
        var result = (Dictionary<string, object>)execRes!;

        store["retrieved_document"] = result;

        Console.WriteLine(
            $"📄 Retrieved document (index: {result["index"]}, distance: {(float)result["distance"]:F4})");
        Console.WriteLine($"📄 Most relevant text: \"{result["text"]}\"");
        return "default";
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

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

/// <summary>
/// Calls the LLM with the query and retrieved chunk to generate a concise answer
/// stored in <c>shared["generated_answer"]</c>.
/// Port of <c>GenerateAnswerNode</c> in <c>nodes.py</c>.
/// </summary>
public class GenerateAnswerNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (
            (string)store["query"],
            (Dictionary<string, object>)store["retrieved_document"]
        );
    }

    protected override object? Execute(object? prepRes)
    {
        var (query, retrievedDoc) = ((string, Dictionary<string, object>))prepRes!;
        var contextText = (string)retrievedDoc["text"];

        var prompt = $"""
Briefly answer the following question based on the context provided:
Question: {query}
Context: {contextText}
Answer:
""";

        return Utils.CallLlm(prompt);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store  = (Dictionary<string, object>)shared;
        var answer = (string)execRes!;

        store["generated_answer"] = answer;

        Console.WriteLine("\n🤖 Generated Answer:");
        Console.WriteLine(answer);
        return "default";
    }
}

