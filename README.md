# MCP Servers (Python): Cosmos DB + Azure Blob Storage + Azure SQL

## Main project

- SalesPOC UI: https://github.com/csdmichael/SalesPOC.UI

This workspace contains three Python MCP servers:

- `src/cosmos_server.py`: Query Cosmos DB container `sales/products`
- `src/blob_server.py`: Read documents from Azure Blob container `semiconductor-product-documents`
- `src/sql_server.py`: Query Azure SQL Database with read-only SQL

## Features

- Cosmos server:
  - `find_products`: Search by text/category and optional price range
  - `get_product_by_id`: Fetch one product by `id`
- Blob server:
  - `list_documents`: List blob documents by optional prefix
  - `read_document`: Read a blob as text (or base64 for binary)
- SQL server:
  - `list_tables`: List tables in a schema
  - `query_data`: Run read-only `SELECT`/`WITH` queries

## Policies (All Servers)

All MCP servers enforce shared runtime policies from `src/policies.py`:

- Output size control (token-limit proxy):
  - Output is capped by serialized character length via `POLICY_MAX_OUTPUT_CHARS` (default `12000`).
  - Oversized responses are truncated and returned in a safe wrapper.
- Negative prompt / prompt-injection protection:
  - Blocks common jailbreak patterns like "ignore previous instructions", "bypass safety", and "system prompt" requests.
- Harmful/hate content protection:
  - Blocks request inputs and tool outputs that match configured harmful/hateful content patterns.
- Rate limiting:
  - Per-tool in-memory rate limiter enforced with `POLICY_RATE_LIMIT_PER_MINUTE` (default `60` requests/minute/tool).

Policy environment variables:

- `POLICY_MAX_OUTPUT_CHARS=12000`
- `POLICY_RATE_LIMIT_PER_MINUTE=60`

## Setup

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

## Run

```bash
python src/cosmos_server.py
python src/blob_server.py
python src/sql_server.py
```

## MCP Client Configuration (example)

Use this in your MCP client settings (adjust path):

```json
{
  "mcpServers": {
    "cosmos_server": {
      "command": "python",
      "args": ["c:/Learning/AI Foundry/MCP-POC/src/cosmos_server.py"],
      "env": {
        "COSMOS_CONNECTION_STRING": "AccountEndpoint=https://cosmos-ai-poc.documents.azure.com:443/;AccountKey=<your-key>",
        "COSMOS_DATABASE_NAME": "sales",
        "COSMOS_CONTAINER_NAME": "products"
      }
    },
    "azureblob": {
      "command": "python",
      "args": ["c:/Learning/AI Foundry/MCP-POC/src/blob_server.py"],
      "env": {
        "AZURE_BLOB_CONNECTION_STRING": "DefaultEndpointsProtocol=https;AccountName=aistoragemyaacoub;AccountKey=<your-key>;EndpointSuffix=core.windows.net",
        "AZURE_BLOB_CONTAINER_NAME": "semiconductor-product-documents"
      }
    },
    "sql_server": {
      "command": "python",
      "args": ["c:/Learning/AI Foundry/MCP-POC/src/sql_server.py"],
      "env": {
        "SQL_CONNECTION_STRING": "Driver={ODBC Driver 18 for SQL Server};Server=tcp:ai-db-poc.database.windows.net,1433;Database=ai-db-poc;Uid=dbadmin;Pwd=<your-password>;Encrypt=yes;TrustServerCertificate=no;Connection Timeout=30;"
      }
    }
  }
}
```
