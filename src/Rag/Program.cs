using PocketFlow;
using Rag;

// ── Sample texts (mirrors main.py) ───────────────────────────────────────────

List<string> texts =
[
    // PocketFlow framework
    """
    Pocket Flow is a 100-line minimalist LLM framework
    Lightweight: Just 100 lines. Zero bloat, zero dependencies, zero vendor lock-in.
    Expressive: Everything you love—(Multi-)Agents, Workflow, RAG, and more.
    Agentic Coding: Let AI Agents (e.g., Cursor AI) build Agents—10x productivity boost!
    To install, pip install pocketflow or just copy the source code (only 100 lines).
    """,

    // Fictional medical device
    """
    NeurAlign M7 is a revolutionary non-invasive neural alignment device.
    Targeted magnetic resonance technology increases neuroplasticity in specific brain regions.
    Clinical trials showed 72% improvement in PTSD treatment outcomes.
    Developed by Cortex Medical in 2024 as an adjunct to standard cognitive therapy.
    Portable design allows for in-home use with remote practitioner monitoring.
    """,

    // Made-up historical event
    """
    The Velvet Revolution of Caldonia (1967-1968) ended Generalissimo Verak's 40-year rule.
    Led by poet Eliza Markovian through underground literary societies.
    Culminated in the Great Silence Protest with 300,000 silent protesters.
    First democratic elections held in March 1968 with 94% voter turnout.
    Became a model for non-violent political transitions in neighboring regions.
    """,

    // Fictional technology
    """
    Q-Mesh is QuantumLeap Technologies' instantaneous data synchronization protocol.
    Utilizes directed acyclic graph consensus for 500,000 transactions per second.
    Consumes 95% less energy than traditional blockchain systems.
    Adopted by three central banks for secure financial data transfer.
    Released in February 2024 after five years of development in stealth mode.
    """,

    // Made-up scientific research
    """
    Harlow Institute's Mycelium Strain HI-271 removes 99.7% of PFAS from contaminated soil.
    Engineered fungi create symbiotic relationships with native soil bacteria.
    Breaks down "forever chemicals" into non-toxic compounds within 60 days.
    Field tests successfully remediated previously permanently contaminated industrial sites.
    Deployment costs 80% less than traditional chemical extraction methods.
    """
];

// ── CLI (mirrors main.py) ─────────────────────────────────────────────────────

const string defaultQuery = "How to install PocketFlow?";

var query = defaultQuery;
foreach (var arg in args)
{
    if (arg.StartsWith("--"))
    {
        query = arg[2..];
        break;
    }
}

// ── Build flows (mirrors flow.py) ─────────────────────────────────────────────

// Offline: chunk → embed → createIndex
var chunkDocs   = new ChunkDocumentsNode();
var embedDocs   = new EmbedDocumentsNode();
var createIndex = new CreateIndexNode();
_ = chunkDocs >> embedDocs >> createIndex;
var offlineFlow = new Flow(start: chunkDocs);

// Online: embedQuery → retrieve → generateAnswer
var embedQuery     = new EmbedQueryNode();
var retrieveDoc    = new RetrieveDocumentNode();
var generateAnswer = new GenerateAnswerNode();
_ = embedQuery >> retrieveDoc >> generateAnswer;
var onlineFlow = new Flow(start: embedQuery);

// ── Run ───────────────────────────────────────────────────────────────────────

Console.WriteLine(new string('=', 50));
Console.WriteLine("PocketFlow RAG Document Retrieval");
Console.WriteLine(new string('=', 50));
Console.WriteLine($"Query: {query}\n");

var shared = new Dictionary<string, object>
{
    ["texts"] = texts,
    ["query"] = query,
};

offlineFlow.Run(shared);
Console.WriteLine();
onlineFlow.Run(shared);
