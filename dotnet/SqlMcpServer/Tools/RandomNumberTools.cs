using System.ComponentModel;
using System.Text.Json;
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
        var rawConnectionString = Environment.GetEnvironmentVariable("SQL_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(rawConnectionString))
        {
            throw new InvalidOperationException("Missing SQL_CONNECTION_STRING environment variable.");
        }

        SqlConnectionStringBuilder builder;
        try
        {
            builder = new SqlConnectionStringBuilder(rawConnectionString);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException("SQL_CONNECTION_STRING is invalid.", ex);
        }

        if (!string.IsNullOrWhiteSpace(builder.UserID) || !string.IsNullOrWhiteSpace(builder.Password))
        {
            throw new InvalidOperationException(
                "SQL_CONNECTION_STRING must not include SQL username/password. " +
                "Use Microsoft Entra auth with Authentication=Active Directory Default.");
        }

        builder.Authentication = SqlAuthenticationMethod.ActiveDirectoryDefault;

        return new SqlConnection(builder.ConnectionString);
    }

    private static bool IsValidIdentifier(string identifier)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            return false;
        }

        if (!(char.IsLetter(identifier[0]) || identifier[0] == '_'))
        {
            return false;
        }

        for (var i = 1; i < identifier.Length; i++)
        {
            var ch = identifier[i];
            if (!(char.IsLetterOrDigit(ch) || ch == '_'))
            {
                return false;
            }
        }

        return true;
    }

    private static string QuoteIdentifier(string identifier)
    {
        return $"[{identifier.Replace("]", "]]", StringComparison.Ordinal)}]";
    }

    private static object? NormalizeFilterValue(object? value)
    {
        if (value is null)
        {
            return null;
        }

        if (value is JsonElement json)
        {
            return json.ValueKind switch
            {
                JsonValueKind.String => json.GetString(),
                JsonValueKind.Number => json.TryGetInt64(out var intVal)
                    ? intVal
                    : json.TryGetDouble(out var dblVal)
                        ? dblVal
                        : json.GetRawText(),
                JsonValueKind.True => true,
                JsonValueKind.False => false,
                JsonValueKind.Null => null,
                _ => throw new InvalidOperationException("Filter values must be scalar (string, number, bool, or null).")
            };
        }

        return value;
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

    [McpServerTool]
    [Description("List columns for a table so agents can build safe filters.")]
    public async Task<object> ListTableColumns(
        [Description("Database table name.")] string table,
        [Description("Database schema name.")] string schema = "dbo")
    {
        var tableName = (table ?? string.Empty).Trim();
        var schemaName = (schema ?? "dbo").Trim();

        if (!IsValidIdentifier(tableName) || !IsValidIdentifier(schemaName))
        {
            throw new InvalidOperationException("Invalid schema or table identifier.");
        }

        await using var conn = GetConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            SELECT c.name
            FROM sys.columns c
            INNER JOIN sys.tables t ON c.object_id = t.object_id
            INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
            WHERE s.name = @schema AND t.name = @table
            ORDER BY c.column_id";
        cmd.Parameters.Add(new SqlParameter("@schema", schemaName));
        cmd.Parameters.Add(new SqlParameter("@table", tableName));

        var columns = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        while (await reader.ReadAsync().ConfigureAwait(false))
        {
            columns.Add(reader.GetString(0));
        }

        return new
        {
            schema = schemaName,
            table = tableName,
            count = columns.Count,
            columns
        };
    }

    [McpServerTool]
    [Description("Query any table with optional equality filters. Use ListTableColumns first to discover valid columns.")]
    public async Task<object> QueryTableRows(
        [Description("Database table name.")] string table,
        [Description("Optional equality filters as column:value pairs.")] Dictionary<string, object?>? filters = null,
        [Description("Database schema name.")] string schema = "dbo",
        [Description("Optional column to sort by.")] string? orderBy = null,
        [Description("Sort descending when true.")] bool descending = false,
        [Description("Maximum number of rows to return.")] int maxRows = 200)
    {
        var tableName = (table ?? string.Empty).Trim();
        var schemaName = (schema ?? "dbo").Trim();
        var safeRows = Math.Clamp(maxRows, 1, 2000);

        if (!IsValidIdentifier(tableName) || !IsValidIdentifier(schemaName))
        {
            throw new InvalidOperationException("Invalid schema or table identifier.");
        }

        await using var conn = GetConnection();
        await conn.OpenAsync().ConfigureAwait(false);

        var validColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var colsCmd = conn.CreateCommand())
        {
            colsCmd.CommandText = @"
                SELECT c.name
                FROM sys.columns c
                INNER JOIN sys.tables t ON c.object_id = t.object_id
                INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
                WHERE s.name = @schema AND t.name = @table";
            colsCmd.Parameters.Add(new SqlParameter("@schema", schemaName));
            colsCmd.Parameters.Add(new SqlParameter("@table", tableName));

            await using var reader = await colsCmd.ExecuteReaderAsync().ConfigureAwait(false);
            while (await reader.ReadAsync().ConfigureAwait(false))
            {
                validColumns.Add(reader.GetString(0));
            }
        }

        if (validColumns.Count == 0)
        {
            throw new InvalidOperationException($"Table '{schemaName}.{tableName}' was not found.");
        }

        var whereClauses = new List<string>();
        await using var cmd = conn.CreateCommand();
        cmd.Parameters.Add(new SqlParameter("@maxRows", safeRows));

        if (filters is not null)
        {
            var paramIndex = 0;
            foreach (var entry in filters)
            {
                var column = (entry.Key ?? string.Empty).Trim();
                if (!IsValidIdentifier(column) || !validColumns.Contains(column))
                {
                    throw new InvalidOperationException($"Invalid filter column: {column}");
                }

                var value = NormalizeFilterValue(entry.Value);
                if (value is null)
                {
                    whereClauses.Add($"{QuoteIdentifier(column)} IS NULL");
                    continue;
                }

                var paramName = $"@f{paramIndex++}";
                whereClauses.Add($"{QuoteIdentifier(column)} = {paramName}");
                cmd.Parameters.Add(new SqlParameter(paramName, value));
            }
        }

        string? orderByClause = null;
        if (!string.IsNullOrWhiteSpace(orderBy))
        {
            var orderByColumn = orderBy.Trim();
            if (!IsValidIdentifier(orderByColumn) || !validColumns.Contains(orderByColumn))
            {
                throw new InvalidOperationException($"Invalid orderBy column: {orderByColumn}");
            }

            orderByClause = $" ORDER BY {QuoteIdentifier(orderByColumn)} {(descending ? "DESC" : "ASC")}";
        }

        var whereSql = whereClauses.Count == 0 ? string.Empty : $" WHERE {string.Join(" AND ", whereClauses)}";
        cmd.CommandText =
            $"SELECT TOP (@maxRows) * FROM {QuoteIdentifier(schemaName)}.{QuoteIdentifier(tableName)}{whereSql}{orderByClause}";

        var items = new List<Dictionary<string, object?>>();
        await using var resultReader = await cmd.ExecuteReaderAsync().ConfigureAwait(false);
        var columns = Enumerable.Range(0, resultReader.FieldCount)
            .Select(resultReader.GetName)
            .ToList();

        while (items.Count < safeRows && await resultReader.ReadAsync().ConfigureAwait(false))
        {
            var row = new Dictionary<string, object?>();
            for (var i = 0; i < resultReader.FieldCount; i++)
            {
                var value = resultReader.IsDBNull(i) ? null : resultReader.GetValue(i);
                row[resultReader.GetName(i)] = value;
            }

            items.Add(row);
        }

        return new
        {
            schema = schemaName,
            table = tableName,
            count = items.Count,
            columns,
            truncated = items.Count == safeRows,
            items
        };
    }
}
