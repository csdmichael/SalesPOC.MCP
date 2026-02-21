import os
from functools import lru_cache
from typing import Any

import pyodbc
from mcp.server.fastmcp import FastMCP

from policies import enforce_output_policy, enforce_rate_limit, enforce_text_policy

connection_string = os.getenv("SQL_CONNECTION_STRING")
host = os.getenv("MCP_HOST", "127.0.0.1")
port = int(os.getenv("MCP_PORT", os.getenv("PORT", os.getenv("WEBSITES_PORT", "8000"))))

if not connection_string:
    raise RuntimeError("Missing SQL_CONNECTION_STRING environment variable.")

mcp = FastMCP("sql_server", host=host, port=port)

BLOCKED_KEYWORDS = {
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
    "deny",
}


@lru_cache(maxsize=1)
def get_connection() -> pyodbc.Connection:
    return pyodbc.connect(connection_string, autocommit=True, timeout=30)


def _validate_read_only_sql(sql: str) -> str:
    normalized = (sql or "").strip()
    if not normalized:
        raise ValueError("'sql' is required.")

    statements = [part.strip() for part in normalized.split(";") if part.strip()]
    if len(statements) != 1:
        raise ValueError("Only a single SQL statement is allowed.")

    statement = statements[0]
    lowered = statement.lower()

    if not (lowered.startswith("select") or lowered.startswith("with")):
        raise ValueError("Only read-only SELECT/CTE queries are allowed.")

    for keyword in BLOCKED_KEYWORDS:
        if f" {keyword} " in f" {lowered} ":
            raise ValueError(f"Blocked SQL keyword detected: {keyword}")

    return statement


@mcp.tool()
def list_tables(schema: str = "dbo", limit: int = 200) -> dict[str, Any]:
    """List user tables for a schema."""
    enforce_rate_limit("sql.list_tables")
    enforce_text_policy(schema, "schema")

    safe_limit = max(1, min(int(limit), 1000))
    schema_name = (schema or "dbo").strip()

    conn = get_connection()
    query = """
        SELECT TOP (?)
            s.name AS schema_name,
            t.name AS table_name
        FROM sys.tables t
        INNER JOIN sys.schemas s ON t.schema_id = s.schema_id
        WHERE s.name = ?
        ORDER BY t.name
    """

    cursor = conn.cursor()
    cursor.execute(query, safe_limit, schema_name)
    rows = cursor.fetchall()

    items = [{"schema": row.schema_name, "table": row.table_name} for row in rows]
    return enforce_output_policy({"count": len(items), "items": items})


@mcp.tool()
def query_data(sql: str, max_rows: int = 200) -> dict[str, Any]:
    """Run a read-only SQL query and return rows as JSON objects."""
    enforce_rate_limit("sql.query_data")
    enforce_text_policy(sql, "sql")

    statement = _validate_read_only_sql(sql)
    safe_rows = max(1, min(int(max_rows), 2000))

    conn = get_connection()
    cursor = conn.cursor()
    cursor.execute(statement)

    columns = [col[0] for col in cursor.description] if cursor.description else []
    rows = cursor.fetchmany(safe_rows)

    items = []
    for row in rows:
        item = {}
        for idx, column in enumerate(columns):
            value = row[idx]
            if hasattr(value, "isoformat"):
                value = value.isoformat()
            item[column] = value
        items.append(item)

    return enforce_output_policy({
        "count": len(items),
        "columns": columns,
        "truncated": len(items) == safe_rows,
        "items": items,
    })


if __name__ == "__main__":
    transport = os.getenv("MCP_TRANSPORT", "stdio").strip().lower()
    mount_path = os.getenv("MCP_MOUNT_PATH")

    if transport == "sse":
        mcp.run(transport="sse", mount_path=mount_path)
    elif transport in {"stdio", "streamable-http"}:
        mcp.run(transport=transport)
    else:
        raise RuntimeError(
            "Invalid MCP_TRANSPORT value. Expected one of: stdio, sse, streamable-http"
        )
