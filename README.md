# MCP Servers: .NET and Python

## Main project

- SalesPOC UI: https://github.com/csdmichael/SalesPOC.UI

This workspace contains two MCP server implementations for the same data sources:

1. **.NET MCP servers** (ASP.NET Core + MCP C# SDK)
2. **Python MCP servers**

---

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
    - `QueryData(sql, maxRows?)` (read-only `SELECT`/`WITH` only)

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
