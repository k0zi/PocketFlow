using PocketFlow;
using Text2Sql;

// ── Configuration ─────────────────────────────────────────────────────────────

const string DbFile          = "ecommerce.db";
const int    MaxDebugRetries = 3;

string naturalQuery = args.Length > 0
    ? string.Join(" ", args)
    : "total products per category";

// ── Ensure database exists ────────────────────────────────────────────────────

if (!File.Exists(DbFile) || new FileInfo(DbFile).Length == 0)
{
    Console.WriteLine($"Database at {DbFile} missing or empty. Populating...");
    PopulateDb.Populate(DbFile);
}

// ── Shared state (mirrors the Python dict) ────────────────────────────────────

var shared = new Dictionary<string, object>
{
    ["db_path"]            = DbFile,
    ["natural_query"]      = naturalQuery,
    ["max_debug_attempts"] = MaxDebugRetries,
    ["debug_attempts"]     = 0,
};

Console.WriteLine("\n=== Starting Text-to-SQL Workflow ===");
Console.WriteLine($"Query: '{naturalQuery}'");
Console.WriteLine($"Database: {DbFile}");
Console.WriteLine($"Max Debug Retries on SQL Error: {MaxDebugRetries}");
Console.WriteLine(new string('=', 45));

// ── Build the flow ────────────────────────────────────────────────────────────

var getSchema   = new GetSchemaNode();
var generateSql = new GenerateSqlNode();
var executeSql  = new ExecuteSqlNode();
var debugSql    = new DebugSqlNode();

// Main sequence
getSchema.Then(generateSql).Then(executeSql);

// Debug loop: ExecuteSQL --error_retry--> DebugSQL --> ExecuteSQL
executeSql.On("error_retry").Then(debugSql);
debugSql.Then(executeSql);

var flow = new Flow(start: getSchema);

// ── Run ───────────────────────────────────────────────────────────────────────

flow.Run(shared);

// ── Report final state ────────────────────────────────────────────────────────

if (shared.TryGetValue("final_error", out var finalError) && finalError is string errorMsg)
{
    Console.WriteLine("\n=== Workflow Completed with Error ===");
    Console.WriteLine($"Error: {errorMsg}");
}
else if (shared.ContainsKey("final_result"))
{
    Console.WriteLine("\n=== Workflow Completed Successfully ===");
}
else
{
    Console.WriteLine("\n=== Workflow Completed (Unknown State) ===");
}

Console.WriteLine(new string('=', 36));
