using System.ComponentModel;
using Microsoft.Data.SqlClient;
using ModelContextProtocol.Server;

internal class SqlTools
{
    private static readonly HashSet<string> BlockedKeywords =
    [
        "insert",
        "update",
        "delete",
        "drop",
        "alter",
        "create",
        "merge",
        "truncate",
        "exec",
        "execute",
        "grant",
        "revoke",
        "deny"
    ];

    private static SqlConnection GetConnection()
    {
        var connectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException("Missing SQL_CONNECTION_STRING environment variable.");
        }

        return new SqlConnection(connectionString);
    }

    private static string ValidateReadOnlySql(string sql)
    {
        var normalized = (sql ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new ArgumentException("'sql' is required.", nameof(sql));
        }

        var statements = normalized.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (statements.Length != 1)
        {
            throw new InvalidOperationException("Only a single SQL statement is allowed.");
        }

        var statement = statements[0];
        var lowered = statement.ToLowerInvariant();

        if (!(lowered.StartsWith("select") || lowered.StartsWith("with")))
        {
            throw new InvalidOperationException("Only read-only SELECT/CTE queries are allowed.");
        }

        foreach (var keyword in BlockedKeywords)
        {
            if ($" {lowered} ".Contains($" {keyword} ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Blocked SQL keyword detected: {keyword}");
            }
        }

        return statement;
    }

    [McpServerTool]
    [Description("List user tables for a schema.")]
    public async Task<object> ListTables(
        [Description("Database schema name.")] string schema = "dbo",
        [Description("Maximum number of rows to return.")] int limit = 200)
    {
        var safeLimit = Math.Clamp(limit, 1, 1000);
        var schemaName = string.IsNullOrWhiteSpace(schema) ? "dbo" : schema.Trim();

        await using var conn = GetConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT TOP (@limit)
                s.name AS schema_name,
                t.name AS table_name
            FROM sys.tables t
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema
            ORDER BY t.name";
        cmd.Parameters.Add(new SqlParameter("@limit", safeLimit));
        cmd.Parameters.Add(new SqlParameter("@schema", schemaName));

        var items = new List<object>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            items.Add(new
            {
                schema = reader.GetString(0),
                table = reader.GetString(1)
            });
        }

        return new
        {
            count = items.Count,
            items
        };
    }

    [McpServerTool]
    [Description("Run a read-only SQL query and return rows as JSON-like objects.")]
    public async Task<object> QueryData(
        [Description("Read-only SQL query (SELECT/CTE).") ] string sql,
        [Description("Maximum number of rows to return.")] int maxRows = 200)
    {
        var statement = ValidateReadOnlySql(sql);
        var safeRows = Math.Clamp(maxRows, 1, 2000);

        await using var conn = GetConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = statement;

        var items = new List<Dictionary<string, object?>>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);

        var columns = Enumerable.Range(0, reader.FieldCount)
            .Select(reader.GetName)
            .ToList();

        while (items.Count < safeRows && await reader.ReadAsync().ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < reader.FieldCount; i++)
            {
                var value = reader.IsDBNull(i) ? null : reader.GetValue(i);
                row[reader.GetName(i)] = value;
            }

            items.Add(row);
        }

        return new
        {
            count = items.Count,
            columns,
            truncated = items.Count == safeRows,
            items
        };
    }
}
