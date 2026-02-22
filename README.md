# MCP Servers: .NET and Python

## Main project

- SalesPOC UI: https://github.com/csdmichael/SalesPOC.UI

This workspace contains two MCP server implementations for the same data sources:

1. **.NET MCP servers** (ASP.NET Core + MCP C# SDK)
2. **Python MCP servers**

---

## Agent Quick Start (Tool Routing)

Use this decision guide when an agent needs to answer user questions:

- Product search/details from catalog data → **Cosmos** (`find_products` / `get_product_by_id`)
- Product documents/spec files → **Blob** (`list_documents` / `read_document`)
- SQL/table exploration or custom read-only reporting → **SQL** (`list_tables` / `query_data`)

When possible, start with narrow queries (`limit`, filters, `TOP`) and only expand if needed.

## .NET MCP Servers

The .NET implementation is under `dotnet/` and exposes HTTP MCP endpoints.

### Servers

- `dotnet/CosmosMcpServer`
  - Tools:
    - `FindProducts(searchText?, category?, minPrice?, maxPrice?, limit?)`
    - `GetProductById(id)`
- `dotnet/BlobMcpServer`
  - Tools:
    - `ListDocuments(prefix?, limit?)`
    - `ReadDocument(blobName, maxChars?)`
- `dotnet/SqlMcpServer`
  - Tools:
    - `ListTables(schema?, limit?)`
    - `ListTableColumns(table, schema?)`
    - `QueryTableRows(table, filters?, schema?, orderBy?, descending?, maxRows?)`
    - `QueryData(sql, maxRows?)` (read-only `SELECT`/`WITH` only)

### .NET Tool Reference (for agents)

#### Cosmos MCP (`dotnet/CosmosMcpServer`)

- `FindProducts(searchText?, category?, minPrice?, maxPrice?, limit?)`
  - Use for: product discovery and filtered search
  - Inputs:
    - `searchText`: matches product name/description (case-insensitive contains)
    - `category`: exact category filter
    - `minPrice` / `maxPrice`: numeric price bounds
    - `limit`: clamped to `1..100`
  - Output shape: `{ count, items[] }` (item fields include `id`, `name`, `category`, `price`, `description`)
- `GetProductById(id)`
  - Use for: exact product lookup by id
  - Inputs: `id` required, trimmed
  - Output shape: full product object or `null`

#### Blob MCP (`dotnet/BlobMcpServer`)

- `ListDocuments(prefix?, limit?)`
  - Use for: discover available files before reading
  - Inputs:
    - `prefix`: optional blob path/prefix filter
    - `limit`: clamped to `1..200`
  - Output shape: `{ count, items[] }` (item fields include `name`, `size`, `content_type`, `last_modified`)
- `ReadDocument(blobName, maxChars?)`
  - Use for: retrieve one blob content
  - Inputs:
    - `blobName`: required exact blob path/name
    - `maxChars`: clamped to `1..200000`
  - Output shape: `{ blob_name, content_type, encoding, truncated, content, size_bytes }`
  - Behavior:
    - returns UTF-8 text when decodable
    - otherwise returns Base64

#### SQL MCP (`dotnet/SqlMcpServer`)

- `ListTables(schema?, limit?)`
  - Use for: schema discovery before query building
  - Inputs:
    - `schema`: default `dbo`
    - `limit`: clamped to `1..1000`
  - Output shape: `{ count, items[] }` where each item is `{ schema, table }`
- `ListTableColumns(table, schema?)`
  - Use for: discover valid columns to build safe filter queries
  - Inputs:
    - `table`: required table name
    - `schema`: default `dbo`
  - Output shape: `{ schema, table, count, columns[] }`
- `QueryTableRows(table, filters?, schema?, orderBy?, descending?, maxRows?)`
  - Use for: agent-safe row retrieval from any table using equality filters
  - Inputs:
    - `table`: required table name
    - `filters`: optional object of `{ columnName: value }` equality filters
    - `schema`: default `dbo`
    - `orderBy`: optional sort column
    - `descending`: sort direction toggle
    - `maxRows`: clamped to `1..2000`
  - Output shape: `{ schema, table, count, columns[], truncated, items[] }`
  - Safety constraints:
    - schema/table/columns must be valid identifiers
    - filters and `orderBy` must reference real columns in the target table
- `QueryData(sql, maxRows?)`
  - Use for: read-only analytical/data retrieval queries
  - Inputs:
    - `sql`: **single** statement only; must start with `SELECT` or `WITH`
    - `maxRows`: clamped to `1..2000`
  - Output shape: `{ count, columns[], truncated, items[] }`
  - Safety constraints:
    - blocks write/DDL/DCL keywords (`insert`, `update`, `delete`, `drop`, `alter`, etc.)
    - rejects multi-statement SQL

### Required environment variables

- `COSMOS_CONNECTION_STRING`
- `COSMOS_DATABASE_NAME` (optional, default: `sales`)
- `COSMOS_CONTAINER_NAME` (optional, default: `products`)
- `AZURE_BLOB_CONNECTION_STRING`
- `AZURE_BLOB_CONTAINER_NAME` (optional, default: `semiconductor-product-documents`)
- `SQL_CONNECTION_STRING`

### Run (.NET)

```powershell
dotnet run --project .\dotnet\CosmosMcpServer\CosmosMcpServer.csproj
dotnet run --project .\dotnet\BlobMcpServer\BlobMcpServer.csproj
dotnet run --project .\dotnet\SqlMcpServer\SqlMcpServer.csproj
```

Default local MCP endpoints:

- Cosmos: `http://localhost:6204`
- Blob: `http://localhost:6128`
- SQL: `http://localhost:6275`

Route prefix for .NET servers: `/` (root path).

### Test URLs (.NET MCP)

Use these URLs in your MCP client (HTTP transport) to test the deployed servers:

- Blob MCP: `https://mcp-salespoc-blob.azurewebsites.net`
- Cosmos MCP: `https://mcp-salespoc-cosmos.azurewebsites.net`
- SQL MCP: `https://mcp-salespoc-sql.azurewebsites.net`

If your MCP client requires a route path, append the configured MCP path (for example `/mcp` if configured in your client/server setup).

---

## Python MCP Servers

The Python implementation is under `src/`.

### Servers

- `src/cosmos_server.py`
  - `find_products`: Search by text/category and optional price range
  - `get_product_by_id`: Fetch one product by `id`
- `src/blob_server.py`
  - `list_documents`: List blob documents by optional prefix
  - `read_document`: Read a blob as text (or base64 for binary)
- `src/sql_server.py`
  - `list_tables`: List tables in a schema
  - `query_data`: Run read-only `SELECT`/`WITH` queries

### Python Tool Reference (for agents)

Python tools are semantically aligned to .NET, with snake_case names:

- Cosmos (`src/cosmos_server.py`)
  - `find_products(search_text?, category?, min_price?, max_price?, limit=20)`
  - `get_product_by_id(id)`
- Blob (`src/blob_server.py`)
  - `list_documents(prefix?, limit=50)`
  - `read_document(blob_name, max_chars=20000)`
- SQL (`src/sql_server.py`)
  - `list_tables(schema='dbo', limit=200)`
  - `query_data(sql, max_rows=200)`

Python-specific behavior important for agents:

- Transport can run as `stdio`, `sse`, or `streamable-http` via `MCP_TRANSPORT`
- Shared protections from `src/policies.py` enforce:
  - rate limiting
  - prompt-content checks
  - output-size shaping

---

## Example Agent Prompts → Recommended Tool

- "Find sensor chips under $400" → Cosmos `find_products` / `FindProducts`
- "Show me docs for Chip-101" → Blob `list_documents` then `read_document`
- "What tables are in dbo?" → SQL `list_tables` / `ListTables`
- "Run a quick product report from SQL" → SQL `query_data` / `QueryData`

---

## Troubleshooting (Common)

- `tools/list` works but `tools/call` fails:
  - usually missing connection environment variables
- HTTP `503` on deployed app:
  - app is starting/restarting or crashed; check App Service log stream
- SQL query blocked:
  - query violates read-only policy (non-SELECT, blocked keyword, or multiple statements)

### Shared runtime policies (Python servers)

Python servers enforce shared runtime policies from `src/policies.py`:

- Output size control via `POLICY_MAX_OUTPUT_CHARS` (default `12000`)
- Prompt-injection pattern blocking
- Harmful/hate content pattern blocking
- Per-tool in-memory rate limiting via `POLICY_RATE_LIMIT_PER_MINUTE` (default `60`)

Policy environment variables:

- `POLICY_MAX_OUTPUT_CHARS=12000`
- `POLICY_RATE_LIMIT_PER_MINUTE=60`

### Setup (Python)

1. Create and activate a virtual environment:
   ```powershell
   python -m venv .venv
   .\.venv\Scripts\Activate.ps1
   ```
2. Install dependencies:
   ```bash
   pip install -r requirements.txt
   ```
3. Set environment variables (PowerShell):
   ```powershell
   $env:COSMOS_CONNECTION_STRING="AccountEndpoint=https://cosmos-ai-poc.documents.azure.com:443/;AccountKey=<your-key>"
   $env:COSMOS_DATABASE_NAME="sales"
   $env:COSMOS_CONTAINER_NAME="products"
   $env:AZURE_BLOB_CONNECTION_STRING="DefaultEndpointsProtocol=https;AccountName=aistoragemyaacoub;AccountKey=<your-key>;EndpointSuffix=core.windows.net"
   $env:AZURE_BLOB_CONTAINER_NAME="semiconductor-product-documents"
   $env:SQL_CONNECTION_STRING="Driver={ODBC Driver 18 for SQL Server};Server=tcp:ai-db-poc.database.windows.net,1433;Database=ai-db-poc;Uid=dbadmin;Pwd=<your-password>;Encrypt=yes;TrustServerCertificate=no;Connection Timeout=30;"
   $env:POLICY_MAX_OUTPUT_CHARS="12000"
   $env:POLICY_RATE_LIMIT_PER_MINUTE="60"
   ```

### Run (Python)

```bash
python src/cosmos_server.py
python src/blob_server.py
python src/sql_server.py
```
