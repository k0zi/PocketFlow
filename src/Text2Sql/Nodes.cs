using Microsoft.Data.Sqlite;
using PocketFlow;

namespace Text2Sql;

// ── Internal result type ──────────────────────────────────────────────────────

/// <summary>Carries the outcome of a SQL execution attempt.</summary>
internal sealed class SqlExecResult
{
    public bool Success { get; init; }
    /// <summary>SELECT rows (non-null when <see cref="Success"/> is true and the query is a SELECT).</summary>
    public List<object?[]>? Rows { get; init; }
    /// <summary>Column names that match <see cref="Rows"/>.</summary>
    public List<string>? Columns { get; init; }
    /// <summary>Message for non-SELECT success (e.g. INSERT/UPDATE rowcount).</summary>
    public string? NonSelectMessage { get; init; }
    /// <summary>SQLite error message when <see cref="Success"/> is false.</summary>
    public string? Error { get; init; }
}

// ── Nodes ─────────────────────────────────────────────────────────────────────

/// <summary>
/// Connects to the SQLite database and extracts the full schema (table + column names).
/// Stores the result in <c>shared["schema"]</c>.
/// Port of <c>GetSchema</c> in nodes.py.
/// </summary>
public class GetSchemaNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (string)store["db_path"];
    }

    protected override object? Execute(object? prepRes)
    {
        var dbPath = (string)prepRes!;
        using var conn = new SqliteConnection($"Data Source={dbPath}");
        conn.Open();

        var schema = new List<string>();

        using var tablesCmd = conn.CreateCommand();
        tablesCmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table'";

        var tables = new List<string>();
        using (var r = tablesCmd.ExecuteReader())
            while (r.Read())
                tables.Add(r.GetString(0));

        foreach (var table in tables)
        {
            schema.Add($"Table: {table}");

            using var infoCmd = conn.CreateCommand();
            infoCmd.CommandText = $"PRAGMA table_info({table})";
            using var ir = infoCmd.ExecuteReader();
            while (ir.Read())
                schema.Add($"  - {ir.GetString(1)} ({ir.GetString(2)})");

            schema.Add("");
        }

        return string.Join("\n", schema).Trim();
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        store["schema"] = (string)execRes!;

        Console.WriteLine("\n===== DB SCHEMA =====\n");
        Console.WriteLine(execRes);
        Console.WriteLine("\n=====================\n");
        return "default";
    }
}

/// <summary>
/// Sends the natural-language query + schema to the LLM and parses the SQL reply.
/// Stores the result in <c>shared["generated_sql"]</c>.
/// Port of <c>GenerateSQL</c> in nodes.py.
/// </summary>
public class GenerateSqlNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return ((string)store["natural_query"], (string)store["schema"]);
    }

    protected override object? Execute(object? prepRes)
    {
        var (naturalQuery, schema) = ((string, string))prepRes!;

        var prompt = $"""
Given SQLite schema:
{schema}

Question: "{naturalQuery}"

Respond ONLY with a YAML block containing the SQL query under the key 'sql':
```yaml
sql: |
  SELECT ...
```
""";

        var llmResponse = Utils.CallLlm(prompt);
        return Utils.ParseSqlFromYaml(llmResponse);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        store["generated_sql"] = (string)execRes!;
        store["debug_attempts"] = 0; // reset on fresh SQL generation

        Console.WriteLine("\n===== GENERATED SQL =====\n");
        Console.WriteLine(execRes);
        Console.WriteLine("\n=========================\n");
        return "default";
    }
}

/// <summary>
/// Executes the generated SQL against the database.
/// On success stores results in <c>shared["final_result"]</c>;
/// on failure increments <c>shared["debug_attempts"]</c> and returns <c>"error_retry"</c>
/// (or sets <c>shared["final_error"]</c> when max retries are exhausted).
/// Port of <c>ExecuteSQL</c> in nodes.py.
/// </summary>
public class ExecuteSqlNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return ((string)store["db_path"], (string)store["generated_sql"]);
    }

    protected override object? Execute(object? prepRes)
    {
        var (dbPath, sqlQuery) = ((string, string))prepRes!;

        try
        {
            using var conn = new SqliteConnection($"Data Source={dbPath}");
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = sqlQuery;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool isSelect = sqlQuery.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase)
                         || sqlQuery.TrimStart().StartsWith("WITH",   StringComparison.OrdinalIgnoreCase);

            if (isSelect)
            {
                using var reader = cmd.ExecuteReader();
                sw.Stop();
                Console.WriteLine($"SQL executed in {sw.Elapsed.TotalSeconds:F3} seconds.");

                var columns = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                    columns.Add(reader.GetName(i));

                var rows = new List<object?[]>();
                while (reader.Read())
                {
                    var row = new object?[reader.FieldCount];
                    reader.GetValues(row!);
                    rows.Add(row);
                }
                return new SqlExecResult { Success = true, Rows = rows, Columns = columns };
            }
            else
            {
                int affected = cmd.ExecuteNonQuery();
                sw.Stop();
                Console.WriteLine($"SQL executed in {sw.Elapsed.TotalSeconds:F3} seconds.");
                return new SqlExecResult
                {
                    Success = true,
                    NonSelectMessage = $"Query OK. Rows affected: {affected}"
                };
            }
        }
        catch (SqliteException ex)
        {
            Console.WriteLine($"SQLite Error during execution: {ex.Message}");
            return new SqlExecResult { Success = false, Error = ex.Message };
        }
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store  = (Dictionary<string, object>)shared;
        var result = (SqlExecResult)execRes!;

        if (result.Success)
        {
            Console.WriteLine("\n===== SQL EXECUTION SUCCESS =====\n");

            if (result.Rows is not null && result.Columns is not null)
            {
                if (result.Columns.Count > 0)
                {
                    Console.WriteLine(string.Join(" | ", result.Columns));
                    int sepLen = result.Columns.Sum(c => c.Length) + 3 * (result.Columns.Count - 1);
                    Console.WriteLine(new string('-', Math.Max(sepLen, 1)));
                }
                if (result.Rows.Count == 0)
                    Console.WriteLine("(No results found)");
                else
                    foreach (var row in result.Rows)
                        Console.WriteLine(string.Join(" | ", row.Select(v => v?.ToString() ?? "NULL")));

                store["final_result"]   = result.Rows;
                store["result_columns"] = result.Columns;
            }
            else
            {
                Console.WriteLine(result.NonSelectMessage);
                store["final_result"] = result.NonSelectMessage ?? string.Empty;
            }

            Console.WriteLine("\n=================================\n");
            return "default";
        }
        else
        {
            store["execution_error"] = result.Error ?? string.Empty;
            int debugAttempts = (int)store.GetValueOrDefault("debug_attempts", 0) + 1;
            store["debug_attempts"] = debugAttempts;
            int maxAttempts = (int)store.GetValueOrDefault("max_debug_attempts", 3);

            Console.WriteLine($"\n===== SQL EXECUTION FAILED (Attempt {debugAttempts}) =====\n");
            Console.WriteLine($"Error: {result.Error}");
            Console.WriteLine("=========================================\n");

            if (debugAttempts >= maxAttempts)
            {
                Console.WriteLine($"Max debug attempts ({maxAttempts}) reached. Stopping.");
                store["final_error"] =
                    $"Failed to execute SQL after {maxAttempts} attempts. Last error: {result.Error}";
                return "default";
            }

            Console.WriteLine("Attempting to debug the SQL...");
            return "error_retry";
        }
    }
}

/// <summary>
/// When SQL execution fails, asks the LLM to generate a corrected query.
/// Updates <c>shared["generated_sql"]</c> with the fixed SQL.
/// Port of <c>DebugSQL</c> in nodes.py.
/// </summary>
public class DebugSqlNode : Node
{
    protected override object? Prepare(object shared)
    {
        var store = (Dictionary<string, object>)shared;
        return (
            store.GetValueOrDefault("natural_query") as string,
            store.GetValueOrDefault("schema")        as string,
            store.GetValueOrDefault("generated_sql") as string,
            store.GetValueOrDefault("execution_error") as string
        );
    }

    protected override object? Execute(object? prepRes)
    {
        var (naturalQuery, schema, failedSql, errorMessage) =
            ((string?, string?, string?, string?))prepRes!;

        var prompt = $"""
The following SQLite SQL query failed:
```sql
{failedSql}
```
It was generated for: "{naturalQuery}"
Schema:
{schema}
Error: "{errorMessage}"

Provide a corrected SQLite query.

Respond ONLY with a YAML block containing the corrected SQL under the key 'sql':
```yaml
sql: |
  SELECT ... -- corrected query
```
""";

        var llmResponse = Utils.CallLlm(prompt);
        return Utils.ParseSqlFromYaml(llmResponse);
    }

    protected override object? Post(object shared, object? prepRes, object? execRes)
    {
        var store = (Dictionary<string, object>)shared;
        store["generated_sql"] = (string)execRes!;
        store.Remove("execution_error");

        int debugAttempts = (int)store.GetValueOrDefault("debug_attempts", 0);
        Console.WriteLine($"\n===== REVISED SQL (Attempt {debugAttempts + 1}) =====\n");
        Console.WriteLine(execRes);
        Console.WriteLine("\n====================================\n");
        return "default";
    }
}

